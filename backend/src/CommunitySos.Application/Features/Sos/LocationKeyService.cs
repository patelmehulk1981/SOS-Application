using CommunitySos.Application.Abstractions;
using CommunitySos.Application.Common.Exceptions;
using CommunitySos.Domain.Entities;
using CommunitySos.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace CommunitySos.Application.Features.Sos;

/// <summary>
/// Background location credentials (docs/06-background-location.md). The app obtains a key while signed
/// in and stores it in secure storage; background tasks send it instead of a session token, so they never
/// refresh (rotate) the session. A key is accepted only while:
/// - it has not been revoked (turned off, replaced, signed out, consent withdrawn, account deleted);
/// - the same phone (install id) still holds an unexpired, unrevoked refresh token for the account,
///   so a suspended, signed-out or 30-days-idle phone stops reporting automatically;
/// - the account is active and LocationSharing consent is on.
/// </summary>
public sealed class LocationKeyService(IAppDbContext db, ITokenService tokens, LocationService locations, TimeProvider clock)
{
    /// <summary>Distinguishes location keys from refresh tokens in logs and secret scanners.</summary>
    public const string KeyPrefix = "lk_";

    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    /// <summary>Issues a key for this phone, revoking any earlier key it had for the account.</summary>
    public async Task<LocationKeyResponse> IssueAsync(Guid userId, IssueLocationKeyRequest request, CancellationToken ct)
    {
        var now = Now;
        var sharing = await db.Users.AsNoTracking()
            .Where(u => u.Id == userId && u.Status == UserStatus.Active)
            .Select(u => (bool?)u.LocationSharingEnabled)
            .FirstOrDefaultAsync(ct);
        if (sharing is not true)
        {
            throw LocationService.ConsentRequired();
        }

        if (!await HasActiveSessionAsync(userId, request.DeviceId, now, ct))
        {
            // The key is bound to the phone's session; an install id that never signed in cannot hold one.
            throw new UnprocessableException("location_key.unknown_device", "Sign in again on this phone to turn on background location.");
        }

        var previous = await db.LocationKeys.Where(k => k.UserId == userId && k.DeviceId == request.DeviceId && k.RevokedAt == null).ToListAsync(ct);
        if (previous.Count > 0)
        {
            previous.ForEach(k => k.Revoke(now, "replaced"));
            await db.SaveChangesAsync(ct); // before the insert: one active key per phone is a unique index
        }

        var secret = tokens.CreateRefreshToken(); // same primitive: 256 random bits, SHA-256 stored
        var key = KeyPrefix + secret.Token;
        db.LocationKeys.Add(LocationKey.Issue(userId, tokens.HashRefreshToken(key), request.DeviceId, now));

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Two issues for the same phone raced; the other one won. The app simply asks again.
            throw new ConflictException("location_key.conflict", "Please try again.");
        }

        return new LocationKeyResponse(key);
    }

    /// <summary>Background location turned off on this phone. Idempotent.</summary>
    public async Task RevokeAsync(Guid userId, string deviceId, string reason, CancellationToken ct)
    {
        var now = Now;
        var keys = await db.LocationKeys.Where(k => k.UserId == userId && k.DeviceId == deviceId && k.RevokedAt == null).ToListAsync(ct);
        if (keys.Count == 0)
        {
            return;
        }

        keys.ForEach(k => k.Revoke(now, reason));
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Records a background fix sent with a location key.</summary>
    /// <exception cref="AuthenticationFailedException">location_key.invalid: stop background updates and ask for a new key when next signed in.</exception>
    /// <exception cref="UnprocessableException">consent.location_required: location sharing was turned off.</exception>
    public async Task ReportAsync(string? presentedKey, UpdateLocationRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(presentedKey) || !presentedKey.StartsWith(KeyPrefix, StringComparison.Ordinal) || presentedKey.Length > 200)
        {
            throw InvalidKey();
        }

        var now = Now;
        var hash = tokens.HashRefreshToken(presentedKey);
        var key = await db.LocationKeys.FirstOrDefaultAsync(k => k.KeyHash == hash, ct);
        if (key is not { IsActive: true })
        {
            throw InvalidKey();
        }

        var user = await db.Users.AsNoTracking()
            .Where(u => u.Id == key.UserId)
            .Select(u => new { u.Status, u.LocationSharingEnabled })
            .FirstOrDefaultAsync(ct);

        if (user is null || user.Status != UserStatus.Active || !await HasActiveSessionAsync(key.UserId, key.DeviceId, now, ct))
        {
            key.Revoke(now, user is null || user.Status != UserStatus.Active ? "account_inactive" : "signed_out");
            await db.SaveChangesAsync(ct);
            throw InvalidKey();
        }

        if (!user.LocationSharingEnabled)
        {
            key.Revoke(now, "consent_withdrawn");
            await db.SaveChangesAsync(ct);
            throw LocationService.ConsentRequired();
        }

        await locations.StageAsync(key.UserId, request, ct);
        key.MarkUsed(now);
        await db.SaveChangesAsync(ct);
    }

    private Task<bool> HasActiveSessionAsync(Guid userId, string deviceId, DateTime now, CancellationToken ct) =>
        db.RefreshTokens.AnyAsync(t => t.UserId == userId && t.DeviceId == deviceId && t.RevokedAt == null && t.ExpiresAt > now, ct);

    private static AuthenticationFailedException InvalidKey() =>
        new("location_key.invalid", "Background location has stopped on this phone. Open the app to turn it back on.");
}

public sealed record IssueLocationKeyRequest(string DeviceId);

public sealed record RevokeLocationKeyRequest(string DeviceId);

/// <param name="Key">Send as the X-Location-Key header to PUT /location/background. Shown once; only its hash is stored.</param>
public sealed record LocationKeyResponse(string Key);
