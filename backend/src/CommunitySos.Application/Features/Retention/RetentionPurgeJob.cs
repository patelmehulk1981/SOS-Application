using CommunitySos.Application.Abstractions;
using CommunitySos.Application.Common.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CommunitySos.Application.Features.Retention;

/// <summary>
/// Daily storage-limitation purge (docs/07-testing-deployment.md#retention). Idempotent and batched:
/// a run cut short (deploy, crash) simply continues the next day.
/// - SOS photos <see cref="RetentionOptions.SosMediaDays"/> after the SOS closed: blob first, then the row,
///   so a failed blob delete is retried and no row ever points at a photo that might still exist unaccounted.
/// - Ended sessions and location keys, long-inactive devices, stale last-known locations.
/// - Audit logs past <see cref="RetentionOptions.AuditLogDays"/> (through the append-only trigger's purge gate).
/// SOS records themselves, status history, referrals and consent records are kept: they are the
/// accountability trail, and account deletion already anonymises the person.
/// </summary>
public sealed class RetentionPurgeJob(
    IAppDbContext db,
    IBlobStorage blobs,
    IAuditLogPurger auditLogs,
    IOptions<RetentionOptions> options,
    TimeProvider clock,
    ILogger<RetentionPurgeJob> logger)
{
    public const string RecurringId = "retention-purge";
    public const string Cron = "30 21 * * *"; // 21:30 UTC = 03:00 IST, the quietest hour

    /// <summary>Upper bound per category per run, so one run never holds the worker for hours.</summary>
    private const int MaxBatchesPerCategory = 100;

    public async Task<RetentionPurgeResult> RunAsync(CancellationToken cancellationToken)
    {
        var o = options.Value;
        var now = clock.GetUtcNow().UtcDateTime;

        var photos = await PurgeSosMediaAsync(now.AddDays(-o.SosMediaDays), o.BatchSize, cancellationToken);

        var sessionCutoff = now.AddDays(-o.EndedSessionDays);
        var sessions = await DeleteInBatchesAsync(
            db.RefreshTokens.Where(t => (t.RevokedAt != null && t.RevokedAt < sessionCutoff) || t.ExpiresAt < sessionCutoff),
            o.BatchSize,
            cancellationToken);

        var keys = await DeleteInBatchesAsync(
            db.LocationKeys.Where(k => k.RevokedAt != null && k.RevokedAt < sessionCutoff),
            o.BatchSize,
            cancellationToken);

        var deviceCutoff = now.AddDays(-o.InactiveDeviceDays);
        var devices = await DeleteInBatchesAsync(
            db.UserDevices.Where(d => !d.IsActive && d.LastSeenAt < deviceCutoff),
            o.BatchSize,
            cancellationToken);

        var locationCutoff = now.AddHours(-o.StaleLocationHours);
        var locations = await DeleteInBatchesAsync(
            db.UserLocations.Where(l => l.CapturedAt < locationCutoff),
            o.BatchSize,
            cancellationToken);

        var auditCutoff = now.AddDays(-o.AuditLogDays);
        var audits = 0;
        for (var i = 0; i < MaxBatchesPerCategory && !cancellationToken.IsCancellationRequested; i++)
        {
            var deleted = await auditLogs.DeleteBatchAsync(auditCutoff, o.BatchSize, cancellationToken);
            audits += deleted;
            if (deleted < o.BatchSize)
            {
                break;
            }
        }

        var result = new RetentionPurgeResult(photos, sessions, keys, devices, locations, audits);
        logger.LogInformation(
            "Retention purge: {Photos} SOS photos, {Sessions} sessions, {Keys} location keys, {Devices} devices, {Locations} stale locations, {Audits} audit rows",
            result.SosPhotos, result.Sessions, result.LocationKeys, result.Devices, result.StaleLocations, result.AuditLogs);
        return result;
    }

    private async Task<int> PurgeSosMediaAsync(DateTime closedBefore, int batchSize, CancellationToken ct)
    {
        var total = 0;
        var failedBlobs = new HashSet<Guid>();

        for (var i = 0; i < MaxBatchesPerCategory && !ct.IsCancellationRequested; i++)
        {
            var batch = await (
                    from a in db.SosAttachments
                    join s in db.SosRequests on a.SosRequestId equals s.Id
                    where s.ClosedAt != null && s.ClosedAt < closedBefore && !failedBlobs.Contains(a.Id)
                    orderby s.ClosedAt
                    select a)
                .Take(batchSize)
                .ToListAsync(ct);

            if (batch.Count == 0)
            {
                break;
            }

            var removed = 0;
            foreach (var attachment in batch)
            {
                try
                {
                    await blobs.DeleteAsync(attachment.BlobName, ct); // idempotent: a missing blob is fine
                    db.SosAttachments.Remove(attachment);
                    removed++;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    failedBlobs.Add(attachment.Id); // keep the row; tomorrow's run retries
                    logger.LogWarning(ex, "Could not delete SOS photo blob {BlobName}; will retry", attachment.BlobName);
                }
            }

            await db.SaveChangesAsync(ct);
            db.ChangeTracker.Clear();
            total += removed;

            if (batch.Count < batchSize)
            {
                break;
            }
        }

        return total;
    }

    private async Task<int> DeleteInBatchesAsync<T>(IQueryable<T> query, int batchSize, CancellationToken ct)
        where T : class
    {
        var total = 0;
        for (var i = 0; i < MaxBatchesPerCategory && !ct.IsCancellationRequested; i++)
        {
            var batch = await query.Take(batchSize).ToListAsync(ct);
            if (batch.Count == 0)
            {
                break;
            }

            db.ChangeTracker.Context.RemoveRange(batch);
            await db.SaveChangesAsync(ct);
            db.ChangeTracker.Clear();
            total += batch.Count;
            if (batch.Count < batchSize)
            {
                break;
            }
        }

        return total;
    }
}

public sealed record RetentionPurgeResult(int SosPhotos, int Sessions, int LocationKeys, int Devices, int StaleLocations, int AuditLogs);
