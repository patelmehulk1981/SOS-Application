using CommunitySos.Application.Abstractions;
using CommunitySos.Application.Common.Exceptions;
using CommunitySos.Application.Common.Options;
using CommunitySos.Application.Common.Security;
using CommunitySos.Domain.Entities;
using CommunitySos.Domain.Enums;
using CommunitySos.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CommunitySos.Application.Features.Auth;

/// <summary>
/// Phone login / invitation registration, refresh-token rotation with reuse detection, and logout.
/// </summary>
public sealed class AuthService(
    IAppDbContext db,
    IFirebaseAuthService firebase,
    ITokenService tokens,
    ICurrentUser caller,
    IOptionsSnapshot<AppOptions> appOptions,
    TimeProvider clock,
    ILogger<AuthService> logger)
{
    private const int MaxRegistrationAttempts = 3;
    private const string RevokedRotated = "rotated";

    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    /// <summary>
    /// Existing users are signed in (any invite code is ignored: the original referral is permanent).
    /// New users must supply a valid invitation code and consent; otherwise 422 "auth.registration_required".
    /// </summary>
    public async Task<AuthResponse> LoginAsync(LoginRequest request, CancellationToken ct)
    {
        var identity = await firebase.VerifyAsync(request.FirebaseIdToken, ct);
        var phone = PhoneNumber.Create(identity.PhoneNumber);

        var user = await db.Users.FirstOrDefaultAsync(u => u.FirebaseUid == identity.Uid, ct)
                   ?? await db.Users.FirstOrDefaultAsync(u => u.PhoneNumber == phone, ct);

        if (user is not null)
        {
            return await SignInExistingAsync(user, identity, request.DeviceId, ct);
        }

        if (string.IsNullOrWhiteSpace(request.InviteCode))
        {
            throw new UnprocessableException(
                "auth.registration_required",
                "This number is not registered yet. Open the invitation link you received to join.");
        }

        if (request.Consent is not { } consent)
        {
            throw new UnprocessableException("auth.consent_required", "Please accept the Terms and Privacy Notice to continue.");
        }

        if (!string.Equals(consent.PolicyVersion, appOptions.Value.PolicyVersion, StringComparison.Ordinal))
        {
            throw new UnprocessableException("policy.version_mismatch", "The Privacy Notice has been updated. Please review the latest version.");
        }

        return await RegisterAsync(identity, phone, InvitationCode.Normalize(request.InviteCode), consent, request.DeviceId, ct);
    }

    public async Task<AuthResponse> RefreshAsync(RefreshRequest request, CancellationToken ct)
    {
        var hash = tokens.HashRefreshToken(request.RefreshToken);
        var current = await db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == hash, ct) ?? throw InvalidRefreshToken();
        var now = Now;

        if (current.RevokedAt is not null)
        {
            if (current.ReplacedByTokenId is not null)
            {
                // A rotated token came back: it was copied or stolen. End the whole session family.
                await RevokeFamilyAsync(current.FamilyId, "reuse_detected", now, ct);
                db.AuditLogs.Add(SecurityAudit.Event(caller, SecurityAudit.RefreshTokenReuse, current.UserId, now, new { current.FamilyId }));
                await db.SaveChangesAsync(ct);
                logger.LogWarning("Refresh token reuse detected for user {UserId}, family {FamilyId}", current.UserId, current.FamilyId);
                throw SessionEnded();
            }

            throw InvalidRefreshToken();
        }

        if (!current.IsActive(now))
        {
            throw new AuthenticationFailedException("auth.refresh_expired", "Your session has expired. Please sign in again.");
        }

        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == current.UserId, ct);
        if (user is not { IsActive: true })
        {
            await RevokeFamilyAsync(current.FamilyId, "account_inactive", now, ct);
            await db.SaveChangesAsync(ct);
            throw AccountInactive();
        }

        var refresh = tokens.CreateRefreshToken();
        var next = RefreshToken.Issue(user.Id, refresh.Hash, current.FamilyId, request.DeviceId ?? current.DeviceId, now, tokens.RefreshTokenLifetime);
        current.Revoke(now, RevokedRotated, next.Id);
        db.RefreshTokens.Add(next);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Another request rotated this token a moment ago (the app must serialise refreshes).
            throw SessionEnded();
        }

        var access = tokens.CreateAccessToken(user);
        return new AuthResponse(access.Token, access.ExpiresAt, refresh.Token, next.ExpiresAt, IsNewUser: false, ToSessionUser(user));
    }

    public async Task LogoutAsync(Guid userId, LogoutRequest request, CancellationToken ct)
    {
        var now = Now;
        var hash = tokens.HashRefreshToken(request.RefreshToken);
        var token = await db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == hash && t.UserId == userId, ct);
        if (token is not null)
        {
            await RevokeFamilyAsync(token.FamilyId, "logout", now, ct);

            // This phone stops background location reporting for the account (docs/06).
            if (token.DeviceId is { } deviceId)
            {
                foreach (var key in await db.LocationKeys.Where(k => k.UserId == userId && k.DeviceId == deviceId && k.RevokedAt == null).ToListAsync(ct))
                {
                    key.Revoke(now, "logout");
                }
            }
        }

        // Stop pushes to this phone (by FID from current builds, or a legacy token).
        var device = !string.IsNullOrWhiteSpace(request.InstallationId)
            ? await db.UserDevices.FirstOrDefaultAsync(d => d.InstallationId == request.InstallationId && d.UserId == userId, ct)
            : !string.IsNullOrWhiteSpace(request.FcmToken)
                ? await db.UserDevices.FirstOrDefaultAsync(d => d.FcmToken == request.FcmToken && d.UserId == userId, ct)
                : null;
        device?.Deactivate();

        db.AuditLogs.Add(SecurityAudit.Event(caller, SecurityAudit.Logout, userId, now));
        await db.SaveChangesAsync(ct);
    }

    internal SessionUser ToSessionUser(User user) => new(
        user.Id,
        user.Role,
        user.FullName,
        ProfileCompleted: !string.IsNullOrWhiteSpace(user.FullName),
        PolicyAcceptanceRequired: !user.HasAcceptedPolicy(appOptions.Value.PolicyVersion));

    private async Task<AuthResponse> SignInExistingAsync(User user, FirebaseIdentity identity, string? deviceId, CancellationToken ct)
    {
        if (!user.IsActive)
        {
            throw AccountInactive();
        }

        var now = Now;
        user.RecordLogin(identity.Uid, now);
        db.AuditLogs.Add(SecurityAudit.Event(caller, SecurityAudit.Login, user.Id, now));
        return await IssueSessionAsync(user, deviceId, isNewUser: false, ct);
    }

    private async Task<AuthResponse> RegisterAsync(
        FirebaseIdentity identity,
        PhoneNumber phone,
        string inviteCode,
        LoginConsent consent,
        string? deviceId,
        CancellationToken ct)
    {
        for (var attempt = 1; ; attempt++)
        {
            var now = Now;
            var invitation = await db.Invitations.FirstOrDefaultAsync(i => i.Code == inviteCode, ct)
                             ?? throw new NotFoundException("invitation.not_found", "This invitation link is not valid.");

            var inviterActive = await db.Users.AnyAsync(u => u.Id == invitation.CreatedByUserId && u.Status == UserStatus.Active, ct);
            if (!inviterActive)
            {
                throw new UnprocessableException("invitation.unusable", "This invitation link is no longer valid.");
            }

            var redemption = invitation.Redeem(
                phone,
                identity.Uid,
                new RegistrationConsent(consent.PolicyVersion, consent.ConfirmedAdult, caller.UserAgent),
                now,
                caller.IpAddress,
                caller.UserAgent);

            db.Users.Add(redemption.User);
            db.ReferralHistory.Add(redemption.Referral);
            db.UserConsents.Add(redemption.TermsConsent);
            db.AuditLogs.Add(SecurityAudit.Event(
                caller, SecurityAudit.Registered, redemption.User.Id, now, new { InvitationId = invitation.Id, Role = redemption.User.Role.ToString() }));

            try
            {
                return await IssueSessionAsync(redemption.User, deviceId, isNewUser: true, ct);
            }
            catch (DbUpdateConcurrencyException) when (attempt < MaxRegistrationAttempts)
            {
                // Someone else used the same invitation at the same moment; reload and re-check MaxUses.
                db.ChangeTracker.Clear();
            }
        }
    }

    private async Task<AuthResponse> IssueSessionAsync(User user, string? deviceId, bool isNewUser, CancellationToken ct)
    {
        var refresh = tokens.CreateRefreshToken();
        var stored = RefreshToken.Issue(user.Id, refresh.Hash, familyId: Guid.NewGuid(), deviceId, Now, tokens.RefreshTokenLifetime);
        db.RefreshTokens.Add(stored);

        await db.SaveChangesAsync(ct);

        var access = tokens.CreateAccessToken(user);
        return new AuthResponse(access.Token, access.ExpiresAt, refresh.Token, stored.ExpiresAt, isNewUser, ToSessionUser(user));
    }

    private async Task RevokeFamilyAsync(Guid familyId, string reason, DateTime now, CancellationToken ct)
    {
        var active = await db.RefreshTokens.Where(t => t.FamilyId == familyId && t.RevokedAt == null).ToListAsync(ct);
        foreach (var token in active)
        {
            token.Revoke(now, reason);
        }
    }

    private static AuthenticationFailedException InvalidRefreshToken() =>
        new("auth.invalid_refresh_token", "Your session is not valid. Please sign in again.");

    private static AuthenticationFailedException SessionEnded() =>
        new("auth.refresh_reused", "Your session has ended for security reasons. Please sign in again.");

    private static AuthenticationFailedException AccountInactive() =>
        new("auth.account_inactive", "This account is suspended. Please contact your admin.");
}
