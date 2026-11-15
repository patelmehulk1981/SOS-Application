using CommunitySos.Application.Abstractions;
using CommunitySos.Application.Common.Notifications;
using CommunitySos.Application.Common.Options;
using CommunitySos.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CommunitySos.Application.Features.Sos.Jobs;

// Idempotent like every job: re-running never alerts anyone twice (unique recipient rows + the
// selector only returning undelivered recipients), and closed SOS are skipped.

/// <summary>
/// The 5 km neighbour alert. Runs on the critical queue right after approval (or auto-approval).
/// Recipients are grouped by lock-screen distance text so each group is one FCM multicast.
/// Per recipient: delivered to any device = Sent; every device unregistered = NoDevice; otherwise Failed,
/// and the job throws so Hangfire retries (only the failed recipients are returned on retry).
/// </summary>
public sealed class DispatchSosAlertsJob(
    IAppDbContext db,
    IAlertRecipientSelector selector,
    IPushSender push,
    IOptions<AlertOptions> options,
    TimeProvider clock,
    ILogger<DispatchSosAlertsJob> logger)
{
    public async Task RunAsync(Guid sosId, CancellationToken cancellationToken)
    {
        var sos = await db.SosRequests.AsNoTracking().FirstOrDefaultAsync(s => s.Id == sosId, cancellationToken);
        if (sos is not { Status: SosStatus.Approved })
        {
            return; // closed before the job ran: nobody should be alerted any more
        }

        var settings = options.Value;
        // The selector CLAIMS what it returns (see usp_SelectSosRecipients), so overlapping runs never push twice.
        var deliveries = await selector.SelectAsync(sosId, settings, cancellationToken);
        if (deliveries.Count == 0)
        {
            await ThrowIfOthersStillDeliveringAsync(sosId, settings, cancellationToken);
            logger.LogInformation("SOS {SosId}: no neighbour to push to (nobody in range, or all delivered)", sosId);
            return;
        }

        var notificationIds = deliveries.Select(d => d.SosNotificationId).Distinct().ToList();
        var ttl = TimeSpan.FromMinutes(settings.TimeToLiveMinutes);
        var results = new Dictionary<Guid, PushResult>();
        try
        {
            foreach (var group in deliveries.GroupBy(d => PushMessages.DescribeDistance(d.DistanceMeters)))
            {
                var targets = group
                    .DistinctBy(d => d.DeviceId)
                    .Select(d => new PushTarget(d.DeviceId, d.FcmToken, d.InstallationId))
                    .ToList();
                foreach (var result in await push.SendAsync(targets, PushMessages.AlertFor(sos, group.Key, ttl), cancellationToken))
                {
                    results[result.DeviceId] = result;
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Release the claims (as a failed attempt) so the Hangfire retry can resend at once instead of
            // finding everyone still claimed. Some may have been delivered before the failure; a resend is
            // collapsed on the phone (same collapse key), whereas a missed SOS alert is not recoverable.
            var unsent = await db.SosNotifications.Where(n => notificationIds.Contains(n.Id)).ToListAsync(CancellationToken.None);
            unsent.ForEach(n => n.MarkFailed($"Send failed: {ex.GetType().Name}"));
            await db.SaveChangesAsync(CancellationToken.None);
            throw;
        }

        var now = clock.GetUtcNow().UtcDateTime;
        var notifications = await db.SosNotifications.Where(n => notificationIds.Contains(n.Id)).ToListAsync(cancellationToken);
        int sent = 0, noDevice = 0, failed = 0;
        var retry = false;

        foreach (var notification in notifications)
        {
            var outcomes = deliveries
                .Where(d => d.SosNotificationId == notification.Id && results.ContainsKey(d.DeviceId))
                .Select(d => results[d.DeviceId])
                .ToList();

            if (outcomes.Any(r => r.Outcome == PushOutcome.Sent))
            {
                notification.MarkSent(now);
                sent++;
            }
            else if (outcomes.Count > 0 && outcomes.All(r => r.Outcome == PushOutcome.InvalidToken))
            {
                notification.MarkNoDevice("Every device of the recipient is unregistered.");
                noDevice++;
            }
            else
            {
                notification.MarkFailed(outcomes.Select(r => r.Error).FirstOrDefault(e => e is not null) ?? "Push failed.");
                failed++;
                retry |= notification.AttemptCount < settings.MaxAttempts;
            }
        }

        var invalidDevices = results.Values.Where(r => r.Outcome == PushOutcome.InvalidToken).Select(r => r.DeviceId).ToList();
        if (invalidDevices.Count > 0)
        {
            foreach (var device in await db.UserDevices.Where(d => invalidDevices.Contains(d.Id)).ToListAsync(cancellationToken))
            {
                device.Deactivate();
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation(
            "SOS {SosId} alerts: {Sent} sent, {NoDevice} without a valid device, {Failed} failed",
            sosId, sent, noDevice, failed);

        if (retry)
        {
            throw new PushDeliveryException($"SOS {sosId}: {failed} neighbour alert(s) failed; retrying.");
        }
    }

    /// <summary>
    /// Nothing was handed to this run, but recipients are still undelivered under another run's claim (it is
    /// sending right now, or its worker died and the claim has yet to expire). Throwing makes Hangfire check
    /// again later, so a crashed run's recipients are still alerted once its claim expires.
    /// </summary>
    private async Task ThrowIfOthersStillDeliveringAsync(Guid sosId, AlertOptions settings, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var pending = await db.SosNotifications.AnyAsync(
            n => n.SosRequestId == sosId
                 && (n.DeliveryStatus == DeliveryStatus.Queued || n.DeliveryStatus == DeliveryStatus.Failed)
                 && n.AttemptCount < settings.MaxAttempts
                 && n.ClaimedUntil != null && n.ClaimedUntil > now,
            ct);
        if (pending)
        {
            throw new PushDeliveryException($"SOS {sosId}: alerts are still being delivered by another run; checking again later.");
        }
    }
}

/// <summary>Tells the requester that someone nearby is offering help or has arrived (collapsed per SOS).</summary>
public sealed class NotifyRequesterOfResponseJob(IAppDbContext db, UserNotifier notifier)
{
    private static readonly ResponseType[] Helping = [ResponseType.CanHelp, ResponseType.OnTheWay, ResponseType.Arrived];

    public async Task RunAsync(Guid sosId, CancellationToken cancellationToken)
    {
        var sos = await db.SosRequests.AsNoTracking().FirstOrDefaultAsync(s => s.Id == sosId, cancellationToken);
        if (sos is not { Status: SosStatus.Approved })
        {
            return;
        }

        var helping = await db.SosNotifications.CountAsync(n => n.SosRequestId == sosId && Helping.Contains(n.ResponseType), cancellationToken);
        if (helping > 0)
        {
            await notifier.NotifyAsync([sos.RequesterUserId], PushMessages.ResponderUpdateFor(sos, helping), cancellationToken);
        }
    }
}

/// <summary>
/// Once an approved SOS is resolved, cancelled or expired, everyone who was alerted gets "no longer needs help".
/// It uses the alert's collapse key, so on most phones it replaces the alert banner, and nobody keeps driving
/// to an emergency that is over.
/// </summary>
public sealed class NotifyRespondersOfClosureJob(IAppDbContext db, UserNotifier notifier)
{
    private static readonly SosStatus[] Closed = [SosStatus.Resolved, SosStatus.Cancelled, SosStatus.Expired];

    public async Task RunAsync(Guid sosId, CancellationToken cancellationToken)
    {
        var sos = await db.SosRequests.AsNoTracking().FirstOrDefaultAsync(s => s.Id == sosId, cancellationToken);
        if (sos is null || !Closed.Contains(sos.Status))
        {
            return;
        }

        var recipients = await db.SosNotifications
            .Where(n => n.SosRequestId == sosId && n.DeliveryStatus == DeliveryStatus.Sent)
            .Select(n => n.RecipientUserId)
            .ToListAsync(cancellationToken);

        await notifier.NotifyAsync(recipients, PushMessages.ClosedFor(sos), cancellationToken);
    }
}
