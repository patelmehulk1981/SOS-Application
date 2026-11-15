using CommunitySos.Application.Abstractions;
using CommunitySos.Application.Common;
using CommunitySos.Application.Common.Exceptions;
using CommunitySos.Application.Common.Security;
using CommunitySos.Domain.Entities;
using CommunitySos.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace CommunitySos.Application.Features.Admin;

/// <summary>Referral history (who joined through whom, when, via which link) and the referral tree.</summary>
public sealed class ReferralService(IAppDbContext db, ICurrentUser caller, TimeProvider clock)
{
    public async Task<PagedResult<ReferralEntry>> ListAsync(ReferralListQuery query, CancellationToken ct)
    {
        var (callerId, role) = caller.RequireAdmin();
        var adminId = role == UserRole.SubAdmin ? callerId : query.AdminId;

        var referrals = db.ReferralHistory.AsNoTracking();
        if (adminId is { } id)
        {
            referrals = referrals.Where(r => r.ReferrerAdminId == id);
        }

        if (query.From is { } from)
        {
            var fromUtc = from.ToUniversalTime();
            referrals = referrals.Where(r => r.RegisteredAt >= fromUtc);
        }

        if (query.To is { } to)
        {
            var toUtc = to.ToUniversalTime();
            referrals = referrals.Where(r => r.RegisteredAt < toUtc);
        }

        var rows =
            from r in referrals
            join referred in db.Users.AsNoTracking() on r.ReferredUserId equals referred.Id
            join referrer in db.Users.AsNoTracking() on r.ReferrerAdminId equals referrer.Id
            join invitation in db.Invitations.AsNoTracking() on r.InvitationId equals invitation.Id
            orderby r.RegisteredAt descending, r.Id descending
            select new ReferralEntry(
                r.Id,
                new UserSummary(referred.Id, referred.FullName, referred.Role, referred.Status),
                new UserSummary(referrer.Id, referrer.FullName, referrer.Role, referrer.Status),
                r.InvitationId,
                invitation.Label,
                r.AssignedRole,
                r.RegisteredAt);

        return await rows.ToPagedAsync(query, ct);
    }

    /// <summary>Master Admin: every Sub-Admin with their community size.</summary>
    public async Task<ReferralTree> GetTreeAsync(CancellationToken ct)
    {
        var masterId = caller.RequireMasterAdmin();
        var now = clock.GetUtcNow().UtcDateTime;

        var root = await db.Users.AsNoTracking()
            .Where(u => u.Id == masterId)
            .Select(u => new UserSummary(u.Id, u.FullName, u.Role, u.Status))
            .FirstOrDefaultAsync(ct)
            ?? throw NotFoundException.For(nameof(User));

        var nodes = await db.Users.AsNoTracking()
            .Where(s => s.Role == UserRole.SubAdmin && s.Status != UserStatus.Deleted)
            .OrderBy(s => s.FullName)
            .Select(s => new
            {
                Summary = new UserSummary(s.Id, s.FullName, s.Role, s.Status),
                s.PhoneNumber,
                ReferredUsers = db.Users.Count(u => u.ReferredByAdminId == s.Id && u.Status != UserStatus.Deleted),
                ActiveUsers = db.Users.Count(u => u.ReferredByAdminId == s.Id && u.Status == UserStatus.Active),
                ActiveInvitations = db.Invitations.Count(i => i.CreatedByUserId == s.Id && !i.IsRevoked
                                                              && (i.ExpiresAt == null || i.ExpiresAt > now)
                                                              && (i.MaxUses == null || i.UsedCount < i.MaxUses)),
                JoinedAt = s.CreatedAt,
            })
            .ToListAsync(ct);

        var subAdmins = nodes
            .Select(n => new SubAdminNode(n.Summary, n.PhoneNumber.Value, n.ReferredUsers, n.ActiveUsers, n.ActiveInvitations, n.JoinedAt))
            .ToList();

        return new ReferralTree(root, subAdmins.Count, subAdmins.Sum(s => s.ReferredUsers), subAdmins);
    }
}
