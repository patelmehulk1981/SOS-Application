using CommunitySos.Application.Abstractions;
using CommunitySos.Application.Common.Notifications;
using CommunitySos.Application.Common.Options;
using CommunitySos.Domain.Entities;
using CommunitySos.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CommunitySos.Application.Features.Sos.Jobs;

// All jobs are idempotent: they re-read current state and do nothing if the SOS has moved on,
// so job-runner retries or duplicate scheduling are harmless.

/// <summary>Right after an SOS is raised: tell the assigned admin (Pending) or the Master Admin (auto-approved admin SOS).</summary>
public sealed class NotifyAdminOfNewSosJob(IAppDbContext db, UserNotifier notifier)
{
    public async Task RunAsync(Guid sosId, CancellationToken cancellationToken)
    {
        var sos = await db.SosRequests.AsNoTracking().FirstOrDefaultAsync(s => s.Id == sosId, cancellationToken);
        switch (sos)
        {
            case { Status: SosStatus.Pending }:
                await notifier.NotifyAsync([sos.AssignedAdminId], PushMessages.ReviewRequestedFor(sos), cancellationToken);
                break;

            // Sub-Admin's own SOS: keep the Master Admin informed (unless the Master raised it).
            case { IsAutoApproved: true, Status: SosStatus.Approved } when sos.AssignedAdminId != sos.RequesterUserId:
                await notifier.NotifyAsync([sos.AssignedAdminId], PushMessages.AutoApprovedFor(sos), cancellationToken);
                break;
        }
    }
}

/// <summary>
/// Runs <see cref="SosOptions.EscalationMinutes"/> after creation. If nobody reviewed the SOS, it is marked
/// Escalated and the Master Admin is alerted (the Master Admin can review any SOS).
/// </summary>
public sealed class EscalateSosJob(IAppDbContext db, UserNotifier notifier, TimeProvider clock, ILogger<EscalateSosJob> logger)
{
    public async Task RunAsync(Guid sosId, CancellationToken cancellationToken)
    {
        var sos = await db.SosRequests.FirstOrDefaultAsync(s => s.Id == sosId, cancellationToken);
        if (sos is not { Status: SosStatus.Pending })
        {
            return; // reviewed, cancelled or already escalated
        }

        sos.Escalate(clock.GetUtcNow().UtcDateTime);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return; // an admin acted at the same moment - nothing to escalate
        }

        logger.LogWarning("SOS {SosId} escalated: not reviewed by {AdminId} in time", sos.Id, sos.AssignedAdminId);

        var masterIds = await db.Users
            .Where(u => u.Role == UserRole.MasterAdmin && u.Status == UserStatus.Active)
            .Select(u => u.Id)
            .ToListAsync(cancellationToken);

        await notifier.NotifyAsync(masterIds, PushMessages.EscalatedFor(sos), cancellationToken);
    }
}

/// <summary>Tells the requester their SOS was approved or rejected.</summary>
public sealed class NotifyRequesterOfReviewJob(IAppDbContext db, UserNotifier notifier)
{
    public async Task RunAsync(Guid sosId, CancellationToken cancellationToken)
    {
        var sos = await db.SosRequests.AsNoTracking().FirstOrDefaultAsync(s => s.Id == sosId, cancellationToken);
        var message = sos?.Status switch
        {
            SosStatus.Approved => PushMessages.ApprovedFor(sos),
            SosStatus.Rejected => PushMessages.RejectedFor(sos),
            _ => null,
        };

        if (message is not null)
        {
            await notifier.NotifyAsync([sos!.RequesterUserId], message, cancellationToken);
        }
    }
}

/// <summary>Recurring housekeeping: expires SOS that were never reviewed, or approved long ago and never closed.</summary>
public sealed class ExpireStaleSosJob(
    IAppDbContext db,
    IOptions<SosOptions> options,
    IJobScheduler jobs,
    TimeProvider clock,
    ILogger<ExpireStaleSosJob> logger)
{
    public const string RecurringId = "expire-stale-sos";
    public const string Cron = "*/15 * * * *"; // every 15 minutes
    private const int BatchSize = 200;

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var pendingCutoff = now.AddHours(-options.Value.PendingExpiryHours);
        var approvedCutoff = now.AddHours(-options.Value.ApprovedExpiryHours);
        var total = 0;

        while (!cancellationToken.IsCancellationRequested)
        {
            var batch = await db.SosRequests
                .Where(s => ((s.Status == SosStatus.Pending || s.Status == SosStatus.Escalated) && s.CreatedAt < pendingCutoff)
                            || (s.Status == SosStatus.Approved && s.ReviewedAt < approvedCutoff))
                .OrderBy(s => s.CreatedAt)
                .Take(BatchSize)
                .ToListAsync(cancellationToken);

            if (batch.Count == 0)
            {
                break;
            }

            var alerted = batch.Where(s => s.Status == SosStatus.Approved && s.RecipientCount > 0).Select(s => s.Id).ToList();
            batch.ForEach(s => s.Expire(now));
            try
            {
                await db.SaveChangesAsync(cancellationToken);
                total += batch.Count;
            }
            catch (DbUpdateConcurrencyException)
            {
                db.ChangeTracker.Clear(); // someone acted on one of them; retry the batch from fresh state
                continue;
            }

            db.ChangeTracker.Clear();
            foreach (var sosId in alerted)
            {
                jobs.Enqueue<NotifyRespondersOfClosureJob>(j => j.RunAsync(sosId, CancellationToken.None));
            }
        }

        if (total > 0)
        {
            logger.LogInformation("Expired {Count} stale SOS request(s)", total);
        }
    }
}

public static class RecurringJobs
{
    /// <summary>Registers all recurring jobs. Called once at application start.</summary>
    public static void Register(IJobScheduler jobs)
    {
        jobs.AddOrUpdateRecurring<ExpireStaleSosJob>(ExpireStaleSosJob.RecurringId, j => j.RunAsync(CancellationToken.None), ExpireStaleSosJob.Cron);
        jobs.AddOrUpdateRecurring<Retention.RetentionPurgeJob>(
            Retention.RetentionPurgeJob.RecurringId, j => j.RunAsync(CancellationToken.None), Retention.RetentionPurgeJob.Cron);
    }
}
