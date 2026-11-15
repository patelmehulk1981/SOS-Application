using CommunitySos.Api.Extensions;
using CommunitySos.Api.Security;
using CommunitySos.Application.Features.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace CommunitySos.Api.Controllers;

/// <summary>
/// Sign-in with a Firebase phone-OTP ID token, registration through an invitation, token refresh and logout.
/// Request/response examples: docs/03b-auth-profile.md.
/// </summary>
[EnableRateLimiting(RateLimitPolicies.Auth)]
public sealed class AuthController(AuthService auth) : ApiControllerBase
{
    /// <summary>Sign in, or register on first use with an invitation code and consent.</summary>
    /// <response code="200">Signed in. Store the refresh token in secure storage.</response>
    /// <response code="401">Firebase token invalid/expired, or the account is suspended.</response>
    /// <response code="404">Invitation code does not exist.</response>
    /// <response code="422">auth.registration_required / auth.consent_required / registration.adult_required / invitation.unusable / policy.version_mismatch.</response>
    [AllowAnonymous]
    [HttpPost("login")]
    [ProducesResponseType<AuthResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity, "application/problem+json")]
    public Task<AuthResponse> Login(LoginRequest request, CancellationToken ct) => auth.LoginAsync(request, ct);

    /// <summary>Exchange a refresh token for a new access + refresh token pair (rotation).</summary>
    /// <remarks>The old refresh token becomes invalid. Presenting it again ends the session (reuse detection), so call this once at a time.</remarks>
    /// <response code="401">auth.invalid_refresh_token / auth.refresh_expired / auth.refresh_reused / auth.account_inactive.</response>
    [AllowAnonymous]
    [HttpPost("refresh")]
    [ProducesResponseType<AuthResponse>(StatusCodes.Status200OK)]
    public Task<AuthResponse> Refresh(RefreshRequest request, CancellationToken ct) => auth.RefreshAsync(request, ct);

    /// <summary>End this device's session and stop push alerts to it.</summary>
    [AllowWithoutCurrentPolicy]
    [HttpPost("logout")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Logout(LogoutRequest request, CancellationToken ct)
    {
        await auth.LogoutAsync(CurrentUserId, request, ct);
        return NoContent();
    }
}
