using CommunitySos.Application.Abstractions;
using CommunitySos.Application.Common;
using CommunitySos.Application.Common.Exceptions;
using CommunitySos.Application.Common.Options;
using CommunitySos.Application.Common.Security;
using CommunitySos.Domain.Entities;
using CommunitySos.Domain.Enums;
using CommunitySos.Application.Features.Sos;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CommunitySos.Application.Features.Me;

/// <summary>The signed-in user's own profile, preferences, consents and policy acceptance.</summary>
public sealed class ProfileService(
    IAppDbContext db,
    ITokenService tokens,
    ICurrentUser caller,
    IBlobStorage blobs,
    IImageProcessor images,
    IOptionsSnapshot<AppOptions> appOptions,
    TimeProvider clock,
    ILogger<ProfileService> logger)
{
    public const long MaxPhotoUploadBytes = 15 * 1024 * 1024;

    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    public async Task<ProfileResponse> GetAsync(Guid userId, CancellationToken ct)
    {
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, ct)
                   ?? throw NotFoundException.For(nameof(User));

        ReferrerSummary? referrer = null;
        if (user.ReferredByAdminId is { } referrerId)
        {
            referrer = await db.Users
                .AsNoTracking()
                .Where(u => u.Id == referrerId)
                .Select(u => new ReferrerSummary(u.Id, u.FullName, u.Role))
                .FirstOrDefaultAsync(ct);
        }

        var consents = await db.GetCurrentAsync(userId, ct);
        var photoUrl = user.ProfilePhotoBlobName is { } blob ? (await blobs.GetReadUriAsync(blob, ct)).ToString() : null;

        return new ProfileResponse(
            user.Id,
            user.PhoneNumber.Value,
            user.Role,
            user.FullName,
            user.Email,
            photoUrl,
            user.BloodGroup,
            user.EmergencyContactName,
            user.EmergencyContactPhone?.Value,
            user.AlertsEnabled,
            user.AcceptedPolicyVersion,
            referrer,
            consents,
            user.CreatedAt);
    }

    public async Task<ProfileResponse> UpdateProfileAsync(Guid userId, UpdateProfileRequest request, CancellationToken ct)
    {
        var user = await LoadAsync(userId, ct);

        var touchesHealthData = !string.IsNullOrWhiteSpace(request.BloodGroup)
                                || !string.IsNullOrWhiteSpace(request.EmergencyContactName)
                                || !string.IsNullOrWhiteSpace(request.EmergencyContactPhone);

        if (touchesHealthData && !(await db.GetCurrentAsync(userId, ct)).HealthData)
        {
            throw new UnprocessableException(
                "consent.health_data_required",
                "Allow storing health information (blood group, emergency contact) in Privacy settings first.");
        }

        user.UpdateProfile(
            request.FullName,
            request.Email,
            request.BloodGroup,
            request.EmergencyContactName,
            PhoneInput.ParseOptional(request.EmergencyContactPhone));

        await db.SaveChangesAsync(ct);
        return await GetAsync(userId, ct);
    }

    public async Task<ProfileResponse> UpdatePreferencesAsync(Guid userId, UpdatePreferencesRequest request, CancellationToken ct)
    {
        var user = await LoadAsync(userId, ct);
        user.SetAlertsEnabled(request.AlertsEnabled);
        await db.SaveChangesAsync(ct);
        return await GetAsync(userId, ct);
    }

    /// <summary>
    /// Grants or withdraws a purpose-specific consent. Withdrawal takes effect immediately:
    /// location withdrawal deletes the stored location; health-data withdrawal erases those fields.
    /// </summary>
    public async Task<ConsentState> UpdateConsentAsync(Guid userId, UpdateConsentRequest request, CancellationToken ct)
    {
        var user = await LoadAsync(userId, ct);
        var now = Now;
        var policyVersion = appOptions.Value.PolicyVersion;

        switch (request.Type)
        {
            case ConsentType.LocationSharing:
                db.UserConsents.Add(user.SetLocationSharing(request.Granted, policyVersion, now, caller.IpAddress, caller.UserAgent));
                if (!request.Granted)
                {
                    var location = await db.UserLocations.FirstOrDefaultAsync(l => l.UserId == userId, ct);
                    if (location is not null)
                    {
                        db.UserLocations.Remove(location);
                    }

                    // Background reporting stops on every phone (docs/06).
                    foreach (var key in await db.LocationKeys.Where(k => k.UserId == userId && k.RevokedAt == null).ToListAsync(ct))
                    {
                        key.Revoke(now, "consent_withdrawn");
                    }
                }

                break;

            case ConsentType.HealthData:
                db.UserConsents.Add(UserConsent.Record(userId, ConsentType.HealthData, policyVersion, request.Granted, now, caller.IpAddress, caller.UserAgent));
                if (!request.Granted)
                {
                    user.ClearHealthData();
                }

                break;

            case ConsentType.TermsAndPrivacy:
                throw new UnprocessableException(
                    "consent.terms_not_withdrawable",
                    "Consent to the Terms cannot be withdrawn separately. Delete your account instead.");

            default:
                throw new UnprocessableException("consent.invalid_type", "Unknown consent type.");
        }

        db.AuditLogs.Add(SecurityAudit.Event(caller, SecurityAudit.ConsentChanged, userId, now, new { Type = request.Type.ToString(), request.Granted }));
        await db.SaveChangesAsync(ct);
        return await db.GetCurrentAsync(userId, ct);
    }

    /// <summary>Accepts the CURRENT notice version and returns a fresh access token with the updated claim.</summary>
    public async Task<AccessTokenResponse> AcceptPolicyAsync(Guid userId, AcceptPolicyRequest request, CancellationToken ct)
    {
        if (!string.Equals(request.PolicyVersion, appOptions.Value.PolicyVersion, StringComparison.Ordinal))
        {
            throw new UnprocessableException("policy.version_mismatch", "The Privacy Notice has been updated. Please review the latest version.");
        }

        var user = await LoadAsync(userId, ct);
        var now = Now;
        db.UserConsents.Add(user.AcceptPolicy(request.PolicyVersion, request.ConfirmedAdult, now, caller.IpAddress, caller.UserAgent));
        db.AuditLogs.Add(SecurityAudit.Event(caller, SecurityAudit.PolicyAccepted, userId, now, new { request.PolicyVersion }));
        await db.SaveChangesAsync(ct);

        var access = tokens.CreateAccessToken(user);
        return new AccessTokenResponse(access.Token, access.ExpiresAt);
    }

    /// <summary>Replaces the profile photo with a sanitised copy (EXIF/GPS stripped, ≤ 2048 px JPEG).</summary>
    public async Task<ProfileResponse> SetPhotoAsync(Guid userId, PhotoUpload photo, CancellationToken ct)
    {
        if (photo.Length is <= 0 or > MaxPhotoUploadBytes)
        {
            throw new UnprocessableException("attachment.too_large", $"The photo must be under {MaxPhotoUploadBytes / (1024 * 1024)} MB.");
        }

        var user = await LoadAsync(userId, ct);
        await using var input = photo.OpenReadStream();
        var image = await images.ProcessAsync(input, ct);

        var blobName = $"profiles/{userId:N}/{Guid.NewGuid():N}.jpg";
        using (var content = new MemoryStream(image.Content, writable: false))
        {
            await blobs.UploadAsync(blobName, content, image.ContentType, ct);
        }

        var previous = user.ProfilePhotoBlobName;
        try
        {
            user.SetProfilePhoto(blobName);
            await db.SaveChangesAsync(ct);
        }
        catch
        {
            await TryDeleteBlobAsync(blobName);
            throw;
        }

        if (previous is not null)
        {
            await TryDeleteBlobAsync(previous);
        }

        return await GetAsync(userId, ct);
    }

    public async Task<ProfileResponse> RemovePhotoAsync(Guid userId, CancellationToken ct)
    {
        var user = await LoadAsync(userId, ct);
        if (user.ProfilePhotoBlobName is { } previous)
        {
            user.SetProfilePhoto(null);
            await db.SaveChangesAsync(ct);
            await TryDeleteBlobAsync(previous);
        }

        return await GetAsync(userId, ct);
    }

    internal async Task TryDeleteBlobAsync(string blobName)
    {
        try
        {
            await blobs.DeleteAsync(blobName, CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not delete blob {BlobName}", blobName);
        }
    }

    private async Task<User> LoadAsync(Guid userId, CancellationToken ct) =>
        await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct) ?? throw NotFoundException.For(nameof(User));
}
