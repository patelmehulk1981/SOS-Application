using CommunitySos.Application.Abstractions;
using CommunitySos.Application.Common;
using CommunitySos.Application.Common.Exceptions;
using CommunitySos.Application.Common.Security;
using CommunitySos.Application.Features.Sos;
using CommunitySos.Application.Features.Sos.Jobs;
using CommunitySos.Domain.Entities;
using CommunitySos.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CommunitySos.Application.Features.Admin;

/// <summary>
/// Admin side of SOS: review queue, approve/reject, dashboard and map.
/// A Sub-Admin acts on SOS assigned to them; the Master Admin on any SOS (including escalations).
/// </summary>
public sealed class SosReviewService(
    IAppDbContext db,
    ICurrentUser caller,
    SosService sosService,
    IJobScheduler jobs,
    IDashboardReader dashboard,
    TimeProvider clock,
    ILogger<SosReviewService> logger)
{
    private static readonly SosStatus[] AwaitingStatuses = [SosStatus.Pending, SosStatus.Escalated];

    public async Task<PagedResult<AdminSosListItem>> ListAsync(AdminSosListQuery query, CancellationToken ct)
    {
        var sos = Scoped(caller.RequireAdmin(), query.AssignedAdminId);

        if (query.AwaitingReview)
        {
            sos = sos.Where(s => AwaitingStatuses.Contains(s.Status));
        }
        else if (query.Status is { } status)
        {
            sos = sos.Where(s => s.Status == status);
        }

        if (query.From is { } from)
        {
            var fromUtc = from.ToUniversalTime();
            sos = sos.Where(s => s.CreatedAt >= fromUtc);
        }

        if (query.To is { } to)
        {
            var toUtc = to.ToUniversalTime();
            sos = sos.Where(s => s.CreatedAt < toUtc);
        }

        // The queue is worked oldest-first (longest waiting at the top); history is newest-first.
        sos = query.AwaitingReview
            ? sos.OrderBy(s => s.CreatedAt).ThenBy(s => s.Id)
            : sos.OrderByDescending(s => s.CreatedAt).ThenByDescending(s => s.Id);

        var rows =
            from s in sos
            join r in db.Users.AsNoTracking() on s.RequesterUserId equals r.Id
            join a in db.Users.AsNoTracking() on s.AssignedAdminId equals a.Id
            select new
            {
                Sos = s,
                PhotoCount = s.Attachments.Count,
                Requester = new UserSummary(r.Id, r.FullName, r.Role, r.Status),
                r.PhoneNumber,
                Admin = new UserSummary(a.Id, a.FullName, a.Role, a.Status),
            };

        var page = await rows.ToPagedAsync(query, ct);
        return page.Map(x => new AdminSosListItem(
            x.Sos.Id,
            x.Sos.ReferenceNo,
            x.Sos.Category,
            x.Sos.Status,
            x.Sos.IsAutoApproved,
            x.Requester,
            x.Requester.Status == UserStatus.Deleted ? null : x.PhoneNumber.Value,
            x.Admin,
            x.Sos.Address,
            (double)x.Sos.Latitude,
            (double)x.Sos.Longitude,
            x.PhotoCount,
            x.Sos.RecipientCount,
            x.Sos.CreatedAt,
            x.Sos.EscalatedAt,
            x.Sos.ReviewedAt));
    }

    /// <summary>
    /// Approves a Pending/Escalated SOS. Two admins approving at once cannot both succeed:
    /// the row-version check makes the second get 409 concurrency_conflict.
    /// </summary>
    public async Task<SosDetailResponse> ApproveAsync(Guid sosId, ApproveSosRequest request, CancellationToken ct)
    {
        var (adminId, sos) = await LoadForReviewAsync(sosId, ct);
        sos.Approve(adminId, request.Remarks, clock.GetUtcNow().UtcDateTime);
        await db.SaveChangesAsync(ct);

        logger.LogInformation("SOS {SosId} approved by {AdminId}", sos.Id, adminId);
        jobs.Enqueue<DispatchSosAlertsJob>(j => j.RunAsync(sos.Id, CancellationToken.None), JobQueue.Critical);
        jobs.Enqueue<NotifyRequesterOfReviewJob>(j => j.RunAsync(sos.Id, CancellationToken.None), JobQueue.Critical);

        return await sosService.GetAsync(sosId, ct);
    }

    public async Task<SosDetailResponse> RejectAsync(Guid sosId, RejectSosRequest request, CancellationToken ct)
    {
        var (adminId, sos) = await LoadForReviewAsync(sosId, ct);
        sos.Reject(adminId, request.Remarks, clock.GetUtcNow().UtcDateTime);
        await db.SaveChangesAsync(ct);

        logger.LogInformation("SOS {SosId} rejected by {AdminId}", sos.Id, adminId);
        jobs.Enqueue<NotifyRequesterOfReviewJob>(j => j.RunAsync(sos.Id, CancellationToken.None), JobQueue.Critical);

        return await sosService.GetAsync(sosId, ct);
    }

    public Task<DashboardStats> GetDashboardAsync(DashboardQuery query, CancellationToken ct)
    {
        var (adminId, role) = caller.RequireAdmin();
        var scope = role == UserRole.SubAdmin ? adminId : query.AdminId;
        return dashboard.GetAsync(scope, query.From?.ToUniversalTime(), query.To?.ToUniversalTime(), ct);
    }

    /// <summary>SOS pins inside the visible map area (bounding box), newest first, capped at 1000.</summary>
    public async Task<SosMapResponse> GetMapAsync(SosMapQuery query, CancellationToken ct)
    {
        var sos = Scoped(caller.RequireAdmin(), assignedAdminId: null);

        var (minLat, maxLat) = ((decimal)query.MinLatitude, (decimal)query.MaxLatitude);
        var (minLng, maxLng) = ((decimal)query.MinLongitude, (decimal)query.MaxLongitude);
        sos = sos.Where(s => s.Latitude >= minLat && s.Latitude <= maxLat && s.Longitude >= minLng && s.Longitude <= maxLng);

        if (query.Status is { } status)
        {
            sos = sos.Where(s => s.Status == status);
        }

        if (query.From is { } from)
        {
            var fromUtc = from.ToUniversalTime();
            sos = sos.Where(s => s.CreatedAt >= fromUtc);
        }

        if (query.To is { } to)
        {
            var toUtc = to.ToUniversalTime();
            sos = sos.Where(s => s.CreatedAt < toUtc);
        }

        var points = await sos
            .OrderByDescending(s => s.CreatedAt)
            .Take(SosMapResponse.MaxPoints + 1)
            .Select(s => new SosMapPoint(s.Id, s.ReferenceNo, s.Category, s.Status, (double)s.Latitude, (double)s.Longitude, s.CreatedAt))
            .ToListAsync(ct);

        var truncated = points.Count > SosMapResponse.MaxPoints;
        return new SosMapResponse(truncated ? points.Take(SosMapResponse.MaxPoints).ToList() : points, truncated);
    }

    private IQueryable<SosRequest> Scoped((Guid UserId, UserRole Role) scope, Guid? assignedAdminId)
    {
        var sos = db.SosRequests.AsNoTracking();
        if (scope.Role == UserRole.SubAdmin)
        {
            return sos.Where(s => s.AssignedAdminId == scope.UserId);
        }

        return assignedAdminId is { } id ? sos.Where(s => s.AssignedAdminId == id) : sos;
    }

    private async Task<(Guid AdminId, SosRequest Sos)> LoadForReviewAsync(Guid sosId, CancellationToken ct)
    {
        var (adminId, role) = caller.RequireAdmin();
        var sos = await db.SosRequests.FirstOrDefaultAsync(s => s.Id == sosId, ct);
        if (sos is null || !SosAccess.CanReview(adminId, role, sos))
        {
            throw NotFoundException.For("Sos"); // not yours to review looks like missing
        }

        return (adminId, sos);
    }
}
