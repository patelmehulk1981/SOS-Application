using CommunitySos.Application.Abstractions;
using CommunitySos.Application.Common.Options;
using CommunitySos.Domain.Entities;
using CommunitySos.Domain.Enums;
using CommunitySos.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;

namespace CommunitySos.Infrastructure.Persistence;

/// <summary>
/// SQL Server: one call to dbo.usp_SelectSosRecipients (database/scripts/030), which uses the spatial index,
/// serialises concurrent dispatches with sp_getapplock, and inserts recipients set-based.
/// Other providers (tests): the same rules in LINQ with a haversine distance, so the eligibility rules are
/// exercised by the test suite.
/// </summary>
public sealed class AlertRecipientSelector(AppDbContext db, TimeProvider clock) : IAlertRecipientSelector
{
    public async Task<IReadOnlyList<AlertDelivery>> SelectAsync(Guid sosId, AlertOptions options, CancellationToken cancellationToken)
    {
        if (db.Database.IsSqlServer())
        {
            var rows = await db.Database
                .SqlQuery<DeliveryRow>($"""
                    EXEC dbo.usp_SelectSosRecipients
                         @SosRequestId = {sosId},
                         @RadiusMeters = {options.RadiusMeters},
                         @FreshHours = {options.FreshHours},
                         @MaxAccuracyMeters = {options.MaxAccuracyMeters},
                         @MaxAttempts = {options.MaxAttempts},
                         @ClaimSeconds = {options.ClaimSeconds}
                    """)
                .ToListAsync(cancellationToken); // EXEC cannot be composed: materialise as-is
            return rows.Select(r => r.ToDelivery()).ToList();
        }

        return await SelectWithLinqAsync(sosId, options, cancellationToken);
    }

    private async Task<IReadOnlyList<AlertDelivery>> SelectWithLinqAsync(Guid sosId, AlertOptions options, CancellationToken ct)
    {
        var sos = await db.SosRequests.FirstOrDefaultAsync(s => s.Id == sosId, ct);
        if (sos is not { Status: SosStatus.Approved })
        {
            return [];
        }

        var now = clock.GetUtcNow().UtcDateTime;
        var freshSince = now.AddHours(-options.FreshHours);
        var origin = sos.Coordinate;

        var candidates = await (
                from l in db.UserLocations
                join u in db.Users on l.UserId equals u.Id
                where u.Status == UserStatus.Active
                      && u.AlertsEnabled
                      && u.LocationSharingEnabled
                      && u.Id != sos.RequesterUserId
                      && l.CapturedAt >= freshSince
                      && (l.AccuracyMeters == null || l.AccuracyMeters <= options.MaxAccuracyMeters)
                select l)
            .ToListAsync(ct);

        var alreadyAlerted = await db.SosNotifications
            .Where(n => n.SosRequestId == sosId)
            .Select(n => n.RecipientUserId)
            .ToListAsync(ct);

        foreach (var location in candidates.Where(l => !alreadyAlerted.Contains(l.UserId)))
        {
            var distance = origin.DistanceMetersTo(location.Coordinate);
            if (distance <= options.RadiusMeters)
            {
                db.SosNotifications.Add(SosNotification.Create(sosId, location.UserId, distance, now));
            }
        }

        await db.SaveChangesAsync(ct);

        var notifications = await db.SosNotifications.Where(n => n.SosRequestId == sosId).ToListAsync(ct);
        var recipientIds = notifications.Select(n => n.RecipientUserId).ToList();
        var activeDevices = await db.UserDevices.Where(d => d.IsActive && recipientIds.Contains(d.UserId)).ToListAsync(ct);
        foreach (var queued in notifications.Where(n => n.DeliveryStatus == DeliveryStatus.Queued && activeDevices.All(d => d.UserId != n.RecipientUserId)))
        {
            queued.MarkNoDevice("No active device.");
        }

        sos.SetRecipientCount(notifications.Count);

        // Same claim rule as the procedure: hand undelivered, unclaimed recipients with a device to this run.
        var claimed = notifications
            .Where(n => (n.DeliveryStatus is DeliveryStatus.Queued or DeliveryStatus.Failed)
                        && n.AttemptCount < options.MaxAttempts
                        && !n.IsClaimed(now)
                        && activeDevices.Any(d => d.UserId == n.RecipientUserId))
            .ToList();
        claimed.ForEach(n => n.Claim(now.AddSeconds(options.ClaimSeconds)));
        await db.SaveChangesAsync(ct);

        return (
                from n in claimed
                join d in activeDevices on n.RecipientUserId equals d.UserId
                orderby n.DistanceMeters
                select new AlertDelivery(n.Id, n.RecipientUserId, n.DistanceMeters, d.Id, d.FcmToken, d.InstallationId))
            .ToList();
    }

    /// <summary>Column names match the procedure's result set.</summary>
    internal sealed class DeliveryRow
    {
        public long SosNotificationId { get; init; }
        public Guid RecipientUserId { get; init; }
        public double DistanceMeters { get; init; }
        public Guid DeviceId { get; init; }
        public string? FcmToken { get; init; }
        public string? InstallationId { get; init; }
        public byte Platform { get; init; }

        public AlertDelivery ToDelivery() => new(SosNotificationId, RecipientUserId, DistanceMeters, DeviceId, FcmToken, InstallationId);
    }
}
