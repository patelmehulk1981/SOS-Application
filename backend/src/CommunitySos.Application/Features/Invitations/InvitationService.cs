using CommunitySos.Application.Abstractions;
using CommunitySos.Application.Common;
using CommunitySos.Application.Common.Exceptions;
using CommunitySos.Application.Common.Options;
using CommunitySos.Application.Common.Security;
using CommunitySos.Domain.Entities;
using CommunitySos.Domain.Enums;
using CommunitySos.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CommunitySos.Application.Features.Invitations;

/// <summary>
/// Invitation links. The Master Admin's links create Sub-Admins; a Sub-Admin's links create Users.
/// </summary>
public sealed class InvitationService(
    IAppDbContext db,
    ICurrentUser caller,
    IOptionsSnapshot<AppOptions> appOptions,
    TimeProvider clock)
{
    /// <summary>Guards against an admin generating unbounded live links.</summary>
    public const int MaxActiveInvitationsPerAdmin = 100;
    private const int CodeGenerationAttempts = 5;

    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    /// <summary>Public lookup for the sign-up screen. Unknown codes → 404.</summary>
    public async Task<InvitationPreview> PreviewAsync(string code, CancellationToken ct)
    {
        if (!InvitationCode.IsWellFormed(code))
        {
            throw NotFound();
        }

        var normalized = InvitationCode.Normalize(code);
        var row = await db.Invitations
            .AsNoTracking()
            .Where(i => i.Code == normalized)
            .Select(i => new
            {
                Invitation = i,
                InviterName = db.Users.Where(u => u.Id == i.CreatedByUserId).Select(u => u.FullName).FirstOrDefault(),
                InviterRole = db.Users.Where(u => u.Id == i.CreatedByUserId).Select(u => u.Role).FirstOrDefault(),
                InviterActive = db.Users.Any(u => u.Id == i.CreatedByUserId && u.Status == UserStatus.Active),
            })
            .FirstOrDefaultAsync(ct)
            ?? throw NotFound();

        var status = row.Invitation.GetStatus(Now);
        if (status == InvitationStatus.Active && !row.InviterActive)
        {
            status = InvitationStatus.Revoked; // a suspended admin's links stop working
        }

        return new InvitationPreview(
            row.Invitation.Code,
            status,
            row.Invitation.InviteeRole,
            status == InvitationStatus.Active ? new InviterSummary(row.InviterName, row.InviterRole) : null,
            row.Invitation.ExpiresAt);
    }

    public async Task<InvitationResponse> CreateAsync(CreateInvitationRequest request, CancellationToken ct)
    {
        var (callerId, _) = caller.RequireAdmin();
        var creator = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == callerId, ct)
                      ?? throw NotFoundException.For(nameof(User));
        var now = Now;

        var active = await ActiveFilter(db.Invitations.Where(i => i.CreatedByUserId == callerId), now).CountAsync(ct);
        if (active >= MaxActiveInvitationsPerAdmin)
        {
            throw new UnprocessableException(
                "invitation.limit_reached",
                $"You already have {MaxActiveInvitationsPerAdmin} active links. Revoke unused ones first.");
        }

        var expiresAt = request.ExpiresAt?.ToUniversalTime();
        Invitation invitation;
        var attempt = 0;
        do
        {
            // 31^10 codes: a collision is astronomically unlikely, but never hand out a duplicate.
            invitation = Invitation.Create(creator, request.Label, request.MaxUses, expiresAt, now);
        }
        while (await db.Invitations.AnyAsync(i => i.Code == invitation.Code, ct) && ++attempt < CodeGenerationAttempts);

        db.Invitations.Add(invitation);
        await db.SaveChangesAsync(ct);
        return ToResponse(invitation, now);
    }

    /// <summary>The caller's own invitations, newest first.</summary>
    public async Task<PagedResult<InvitationResponse>> ListAsync(InvitationListQuery query, CancellationToken ct)
    {
        var (callerId, _) = caller.RequireAdmin();
        var now = Now;

        var invitations = db.Invitations.AsNoTracking().Where(i => i.CreatedByUserId == callerId);
        invitations = query.Status switch
        {
            InvitationStatus.Active => ActiveFilter(invitations, now),
            InvitationStatus.Revoked => invitations.Where(i => i.IsRevoked),
            InvitationStatus.Expired => invitations.Where(i => !i.IsRevoked && i.ExpiresAt != null && i.ExpiresAt <= now),
            InvitationStatus.Exhausted => invitations.Where(i => !i.IsRevoked && (i.ExpiresAt == null || i.ExpiresAt > now)
                                                                 && i.MaxUses != null && i.UsedCount >= i.MaxUses),
            _ => invitations,
        };

        var page = await invitations
            .OrderByDescending(i => i.CreatedAt)
            .ThenByDescending(i => i.Id)
            .ToPagedAsync(query, ct);

        return page.Map(i => ToResponse(i, now));
    }

    /// <summary>Revokes one of the caller's links (the Master Admin may revoke any). Already-registered users are unaffected.</summary>
    public async Task RevokeAsync(Guid invitationId, CancellationToken ct)
    {
        var (callerId, role) = caller.RequireAdmin();
        var invitation = await db.Invitations.FirstOrDefaultAsync(i => i.Id == invitationId, ct);

        // Someone else's link looks exactly like a missing one.
        if (invitation is null || (invitation.CreatedByUserId != callerId && role != UserRole.MasterAdmin))
        {
            throw NotFoundException.For(nameof(Invitation));
        }

        invitation.Revoke(Now);
        await db.SaveChangesAsync(ct);
    }

    private static IQueryable<Invitation> ActiveFilter(IQueryable<Invitation> query, DateTime now) =>
        query.Where(i => !i.IsRevoked
                         && (i.ExpiresAt == null || i.ExpiresAt > now)
                         && (i.MaxUses == null || i.UsedCount < i.MaxUses));

    private InvitationResponse ToResponse(Invitation i, DateTime now) => new(
        i.Id,
        i.Code,
        $"{appOptions.Value.InviteLinkBaseUrl.TrimEnd('/')}/i/{i.Code}",
        i.InviteeRole,
        i.Label,
        i.MaxUses,
        i.UsedCount,
        i.ExpiresAt,
        i.GetStatus(now),
        i.CreatedAt,
        i.RevokedAt);

    private static NotFoundException NotFound() => new("invitation.not_found", "This invitation link is not valid.");
}
