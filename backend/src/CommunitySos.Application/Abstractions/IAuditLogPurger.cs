namespace CommunitySos.Application.Abstractions;

/// <summary>
/// Deletes audit rows past retention. AuditLogs is append-only in SQL Server (trigger TR_AuditLogs_AppendOnly):
/// the implementation must open the purge gate (SESSION_CONTEXT 'CommunitySos.AllowPurge') for its own batch only.
/// </summary>
public interface IAuditLogPurger
{
    /// <summary>Deletes up to <paramref name="batchSize"/> rows created before <paramref name="cutoffUtc"/>; returns how many.</summary>
    Task<int> DeleteBatchAsync(DateTime cutoffUtc, int batchSize, CancellationToken cancellationToken);
}
