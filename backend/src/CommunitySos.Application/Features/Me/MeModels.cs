using CommunitySos.Domain.Enums;

namespace CommunitySos.Application.Features.Me;

public sealed record ProfileResponse(
    Guid Id,
    string PhoneNumber,
    UserRole Role,
    string? FullName,
    string? Email,
    string? ProfilePhotoUrl,
    string? BloodGroup,
    string? EmergencyContactName,
    string? EmergencyContactPhone,
    bool AlertsEnabled,
    string? AcceptedPolicyVersion,
    ReferrerSummary? ReferredBy,
    ConsentState Consents,
    DateTime CreatedAt);

/// <summary>Who invited this user. Name only - never the admin's phone number.</summary>
public sealed record ReferrerSummary(Guid Id, string? FullName, UserRole Role);

/// <summary>Current consent per purpose (latest ledger entry wins).</summary>
public sealed record ConsentState(bool TermsAndPrivacy, bool LocationSharing, bool HealthData);

/// <param name="BloodGroup">Health data: requires HealthData consent. One of A+, A-, B+, B-, AB+, AB-, O+, O-.</param>
/// <param name="EmergencyContactPhone">Health data. +91 is assumed for 10-digit Indian mobiles.</param>
public sealed record UpdateProfileRequest(
    string FullName,
    string? Email,
    string? BloodGroup,
    string? EmergencyContactName,
    string? EmergencyContactPhone);

public sealed record UpdatePreferencesRequest(bool AlertsEnabled);

/// <param name="Type">LocationSharing or HealthData. (TermsAndPrivacy is withdrawn by deleting the account.)</param>
public sealed record UpdateConsentRequest(ConsentType Type, bool Granted);

public sealed record AcceptPolicyRequest(string PolicyVersion, bool ConfirmedAdult);

/// <summary>New access token carrying the updated policy-version claim.</summary>
public sealed record AccessTokenResponse(string AccessToken, DateTime AccessTokenExpiresAt);

/// <param name="InstallationId">Firebase Installation ID from messaging.onRegistered (current app versions).</param>
/// <param name="FcmToken">Legacy FCM registration token. Older builds send only this; an upgraded build may send
/// both once so its existing row is moved to the FID.</param>
public sealed record RegisterDeviceRequest(
    DevicePlatform Platform,
    string? AppVersion,
    string? DeviceModel,
    string? InstallationId = null,
    string? FcmToken = null);

/// <summary>Stops pushes to one installation: give its FID or legacy token.</summary>
public sealed record UnregisterDeviceRequest(string? InstallationId = null, string? FcmToken = null);
