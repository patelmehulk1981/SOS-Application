using CommunitySos.Domain.Enums;

namespace CommunitySos.Application.Features.Auth;

/// <param name="FirebaseIdToken">ID token from Firebase Phone Auth on the device.</param>
/// <param name="InviteCode">Required only for first-time registration (from the invite link or typed by hand).</param>
/// <param name="Consent">Required only for first-time registration.</param>
/// <param name="DeviceId">Stable per-install id; lets a user see and revoke sessions per device.</param>
public sealed record LoginRequest(string FirebaseIdToken, string? InviteCode, LoginConsent? Consent, string? DeviceId);

/// <param name="PolicyVersion">The Terms/Privacy Notice version shown on screen (from GET /app-info).</param>
/// <param name="ConfirmedAdult">User ticked "I am 18 or older".</param>
public sealed record LoginConsent(string PolicyVersion, bool ConfirmedAdult);

public sealed record RefreshRequest(string RefreshToken, string? DeviceId);

/// <param name="InstallationId">This phone's Firebase Installation ID, deactivated so it stops receiving alerts.</param>
/// <param name="FcmToken">Legacy push token from older app versions (same effect).</param>
public sealed record LogoutRequest(string RefreshToken, string? FcmToken = null, string? InstallationId = null);

public sealed record AuthResponse(
    string AccessToken,
    DateTime AccessTokenExpiresAt,
    string RefreshToken,
    DateTime RefreshTokenExpiresAt,
    bool IsNewUser,
    SessionUser User);

/// <param name="ProfileCompleted">False until the user has set a name - the app should show profile setup.</param>
/// <param name="PolicyAcceptanceRequired">True when the current notice version has not been accepted - the app must show it before anything else.</param>
public sealed record SessionUser(
    Guid Id,
    UserRole Role,
    string? FullName,
    bool ProfileCompleted,
    bool PolicyAcceptanceRequired);
