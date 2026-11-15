using CommunitySos.Application.Abstractions;
using CommunitySos.Application.Common;
using CommunitySos.Application.Common.Exceptions;
using CommunitySos.Application.Common.Security;
using CommunitySos.Domain.Entities;
using CommunitySos.Domain.Enums;
using CommunitySos.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;

namespace CommunitySos.Application.Features.Admin;

/// <summary>
/// Admin user directory. Scope: a Sub-Admin sees only the users they referred; the Master Admin sees everyone.
/// Suspension is a Master Admin action.
/// </summary>
public sealed class AdminUserService(
    IAppDbContext db,
    ICurrentUser caller,
    IUserStatusReader statusReader,
    TimeProvider clock)
{
    public async Task<PagedResult<AdminUserListItem>> ListAsync(UserListQuery query, CancellationToken ct)
    {
        var users = ScopedUsers(caller.RequireAdmin(), query.ReferredBy);

        users = query.Status is { } status ? users.Where(u => u.Status == status) : users.Where(u => u.Status != UserStatus.Deleted);
        if (query.Role is { } role)
        {
            users = users.Where(u => u.Role == role);
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            users = ApplySearch(users, query.Search.Trim());
        }

        var page = await Project(users.OrderByDescending(u => u.CreatedAt).ThenBy(u => u.Id)).ToPagedAsync(query, ct);
        return page.Map(ToListItem);
    }

    public async Task<AdminUserDetail> GetAsync(Guid userId, CancellationToken ct)
    {
        var scope = caller.RequireAdmin();
        var row = await Project(ScopedUsers(scope, referredBy: null).Where(u => u.Id == userId)).FirstOrDefaultAsync(ct)
                  ?? throw NotFoundException.For(nameof(User)); // outside scope looks like missing

        var now = clock.GetUtcNow().UtcDateTime;
        var referral = await (
                from r in db.ReferralHistory.AsNoTracking()
                join i in db.Invitations.AsNoTracking() on r.InvitationId equals i.Id
                where r.ReferredUserId == userId
                select new AdminReferralInfo(r.InvitationId, i.Label, r.RegisteredAt))
            .FirstOrDefaultAsync(ct);

        var sosRaised = await db.SosRequests.CountAsync(s => s.RequesterUserId == userId, ct);
        var activeInvitations = await db.Invitations.CountAsync(
            i => i.CreatedByUserId == userId && !i.IsRevoked
                 && (i.ExpiresAt == null || i.ExpiresAt > now)
                 && (i.MaxUses == null || i.UsedCount < i.MaxUses),
            ct);

        return new AdminUserDetail(ToListItem(row), row.User.Email, row.User.AcceptedPolicyVersion, referral, sosRaised, activeInvitations);
    }

    /// <summary>
    /// Master Admin: suspend or reactivate. Suspension signs the user out everywhere (sessions revoked,
    /// status cache invalidated); a suspended Sub-Admin's invitation links stop working and their
    /// users' SOS requests are routed to the Master Admin.
    /// </summary>
    public async Task<AdminUserDetail> UpdateStatusAsync(Guid userId, UpdateUserStatusRequest request, CancellationToken ct)
    {
        var masterId = caller.RequireMasterAdmin();
        if (userId == masterId)
        {
            throw new UnprocessableException("user.cannot_change_own_status", "You cannot change your own account status.");
        }

        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct) ?? throw NotFoundException.For(nameof(User));
        var now = clock.GetUtcNow().UtcDateTime;
        var previous = user.Status;

        switch (request.Status)
        {
            case UserStatus.Suspended:
                user.Suspend();
                var sessions = await db.RefreshTokens.Where(t => t.UserId == userId && t.RevokedAt == null).ToListAsync(ct);
                sessions.ForEach(t => t.Revoke(now, "suspended"));
                break;
            case UserStatus.Active:
                user.Reactivate();
                break;
            default:
                throw new UnprocessableException("user.invalid_status", "Status must be Active or Suspended.");
        }

        db.AuditLogs.Add(SecurityAudit.Event(
            caller,
            request.Status == UserStatus.Suspended ? SecurityAudit.UserSuspended : SecurityAudit.UserReactivated,
            userId,
            now,
            new { From = previous.ToString(), To = request.Status.ToString(), request.Reason }));

        await db.SaveChangesAsync(ct);
        statusReader.Invalidate(userId);
        return await GetAsync(userId, ct);
    }

    private IQueryable<User> ScopedUsers((Guid UserId, UserRole Role) scope, Guid? referredBy)
    {
        var users = db.Users.AsNoTracking();
        if (scope.Role == UserRole.SubAdmin)
        {
            return users.Where(u => u.ReferredByAdminId == scope.UserId);
        }

        return referredBy is { } adminId ? users.Where(u => u.ReferredByAdminId == adminId) : users;
    }

    private static IQueryable<User> ApplySearch(IQueryable<User> users, string search)
    {
        if (search.Any(char.IsAsciiDigit) && search.All(c => char.IsAsciiDigit(c) || c is '+' or ' ' or '-'))
        {
            PhoneNumber? phone;
            try
            {
                phone = PhoneInput.ParseOptional(search);
            }
            catch (CommunitySos.Domain.Common.DomainException)
            {
                return users.Where(_ => false); // not a valid phone: no matches
            }

            return users.Where(u => u.PhoneNumber == phone);
        }

        return users.Where(u => u.FullName != null && u.FullName.Contains(search));
    }

    private IQueryable<UserRow> Project(IQueryable<User> users) =>
        users.Select(u => new UserRow(
            u,
            db.Users.Where(a => a.Id == u.ReferredByAdminId).Select(a => new UserSummary(a.Id, a.FullName, a.Role, a.Status)).FirstOrDefault(),
            db.Users.Count(r => r.ReferredByAdminId == u.Id && r.Status != UserStatus.Deleted)));

    private static AdminUserListItem ToListItem(UserRow row) => new(
        row.User.Id,
        row.User.FullName,
        row.User.Status == UserStatus.Deleted ? null : row.User.PhoneNumber.Value,
        row.User.Role,
        row.User.Status,
        row.ReferredBy,
        row.ReferredUsersCount,
        row.User.AlertsEnabled,
        row.User.CreatedAt,
        row.User.LastLoginAt);

    private sealed record UserRow(User User, UserSummary? ReferredBy, int ReferredUsersCount);
}
