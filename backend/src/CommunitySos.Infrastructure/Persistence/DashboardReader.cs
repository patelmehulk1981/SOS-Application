using CommunitySos.Application.Abstractions;
using CommunitySos.Application.Features.Admin;
using CommunitySos.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace CommunitySos.Infrastructure.Persistence;

/// <summary>
/// SQL Server: one call to dbo.usp_GetDashboardStats (database/scripts/031). Other providers (tests):
/// equivalent LINQ, so the numbers' definitions are exercised by the test suite.
/// </summary>
public sealed class DashboardReader(AppDbContext db, TimeProvider clock) : IDashboardReader
{
    public async Task<DashboardStats> GetAsync(Guid? adminId, DateTime? fromUtc, DateTime? toUtc, CancellationToken cancellationToken)
    {
        if (db.Database.IsSqlServer())
        {
            var rows = await db.Database
                .SqlQuery<DashboardRow>($"EXEC dbo.usp_GetDashboardStats @AdminId = {adminId}, @FromUtc = {fromUtc}, @ToUtc = {toUtc}")
                .ToListAsync(cancellationToken); // EXEC cannot be composed: materialise as-is
            return rows.Single().ToStats();
        }

        return await ComputeWithLinqAsync(adminId, fromUtc, toUtc, cancellationToken);
    }

    private async Task<DashboardStats> ComputeWithLinqAsync(Guid? adminId, DateTime? fromUtc, DateTime? toUtc, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;

        var users = db.Users.AsNoTracking().Where(u => u.Status != UserStatus.Deleted);
        if (adminId is { } id)
        {
            users = users.Where(u => u.ReferredByAdminId == id);
        }

        var sos = db.SosRequests.AsNoTracking().AsQueryable();
        if (adminId is { } aid)
        {
            sos = sos.Where(s => s.AssignedAdminId == aid);
        }

        if (fromUtc is { } from)
        {
            sos = sos.Where(s => s.CreatedAt >= from);
        }

        if (toUtc is { } to)
        {
            sos = sos.Where(s => s.CreatedAt < to);
        }

        var u = await users.Select(x => new { x.Role, x.CreatedAt, x.ReferredByAdminId }).ToListAsync(ct);
        var s = await sos.Select(x => new { x.Status, x.IsAutoApproved, x.CreatedAt, x.ReviewedAt }).ToListAsync(ct);
        var reviewed = s.Where(x => x.ReviewedAt != null && !x.IsAutoApproved).Select(x => (x.ReviewedAt!.Value - x.CreatedAt).TotalSeconds).ToList();

        return new DashboardStats(
            u.Count,
            u.Count(x => x.Role == UserRole.SubAdmin),
            u.Count(x => x.Role == UserRole.User),
            u.Count(x => x.ReferredByAdminId != null),
            u.Count(x => x.CreatedAt >= now.AddDays(-7)),
            s.Count,
            s.Count(x => x.Status == SosStatus.Pending),
            s.Count(x => x.Status == SosStatus.Escalated),
            s.Count(x => x.ReviewedAt != null && x.Status != SosStatus.Rejected),
            s.Count(x => x.IsAutoApproved),
            s.Count(x => x.Status == SosStatus.Rejected),
            s.Count(x => x.Status == SosStatus.Approved),
            s.Count(x => x.Status == SosStatus.Resolved),
            s.Count(x => x.Status == SosStatus.Cancelled),
            s.Count(x => x.Status == SosStatus.Expired),
            s.Count(x => x.CreatedAt >= now.AddHours(-24)),
            reviewed.Count > 0 ? Math.Round(reviewed.Average(), 1) : null);
    }

    /// <summary>Column names match the procedure's result set.</summary>
    internal sealed class DashboardRow
    {
        public int TotalUsers { get; init; }
        public int SubAdmins { get; init; }
        public int NormalUsers { get; init; }
        public int ReferredUsers { get; init; }
        public int NewUsersLast7Days { get; init; }
        public int TotalSos { get; init; }
        public int PendingSos { get; init; }
        public int EscalatedSos { get; init; }
        public int ApprovedSos { get; init; }
        public int AutoApprovedSos { get; init; }
        public int RejectedSos { get; init; }
        public int ActiveSos { get; init; }
        public int ResolvedSos { get; init; }
        public int CancelledSos { get; init; }
        public int ExpiredSos { get; init; }
        public int SosLast24Hours { get; init; }
        public double? AvgReviewSeconds { get; init; }

        public DashboardStats ToStats() => new(
            TotalUsers, SubAdmins, NormalUsers, ReferredUsers, NewUsersLast7Days,
            TotalSos, PendingSos, EscalatedSos, ApprovedSos, AutoApprovedSos, RejectedSos,
            ActiveSos, ResolvedSos, CancelledSos, ExpiredSos, SosLast24Hours,
            AvgReviewSeconds is { } avg ? Math.Round(avg, 1) : null);
    }
}
