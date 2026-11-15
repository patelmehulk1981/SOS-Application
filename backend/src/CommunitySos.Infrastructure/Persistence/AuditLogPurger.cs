using System.Data;
using CommunitySos.Application.Abstractions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace CommunitySos.Infrastructure.Persistence;

/// <summary>
/// SQL Server: one batch that opens the append-only trigger's purge gate, deletes, and closes it again.
/// SESSION_CONTEXT is per connection and is also cleared when the pooled connection is reset, so the gate
/// can never stay open for other work. Other providers (tests): plain LINQ delete.
/// </summary>
public sealed class AuditLogPurger(AppDbContext db) : IAuditLogPurger
{
    private const string PurgeSql = """
        EXEC sys.sp_set_session_context @key = N'CommunitySos.AllowPurge', @value = 1;
        DELETE TOP (@batch) FROM dbo.AuditLogs WHERE CreatedAt < @cutoff;
        SET @deleted = @@ROWCOUNT;
        EXEC sys.sp_set_session_context @key = N'CommunitySos.AllowPurge', @value = 0;
        """;

    public async Task<int> DeleteBatchAsync(DateTime cutoffUtc, int batchSize, CancellationToken cancellationToken)
    {
        if (db.Database.IsSqlServer())
        {
            var deleted = new SqlParameter("@deleted", SqlDbType.Int) { Direction = ParameterDirection.Output };
            await db.Database.ExecuteSqlRawAsync(
                PurgeSql,
                [
                    new SqlParameter("@batch", SqlDbType.Int) { Value = batchSize },
                    new SqlParameter("@cutoff", SqlDbType.DateTime2) { Value = cutoffUtc },
                    deleted,
                ],
                cancellationToken);
            return deleted.Value is int count ? count : 0;
        }

        var rows = await db.AuditLogs.Where(a => a.CreatedAt < cutoffUtc).OrderBy(a => a.Id).Take(batchSize).ToListAsync(cancellationToken);
        db.AuditLogs.RemoveRange(rows);
        await db.SaveChangesAsync(cancellationToken);
        return rows.Count;
    }
}
