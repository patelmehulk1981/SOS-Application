using System.Text.Json;
using CommunitySos.Api.Extensions;
using CommunitySos.Api.Security;
using CommunitySos.Application.Features.Me;
using CommunitySos.Application.Features.Sos;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace CommunitySos.Api.Controllers;

/// <summary>
/// The signed-in user's own account: profile, preferences, consents, devices, data export and deletion.
/// Request/response examples: docs/03b-auth-profile.md.
/// </summary>
public sealed class MeController(
    ProfileService profiles,
    DeviceService devices,
    AccountService accounts,
    LocationService locations) : ApiControllerBase
{
    private const long PhotoRequestLimit = 16L * 1024 * 1024;

    /// <summary>Report my current location (last-known only, used for the 5 km alert radius). Requires LocationSharing consent.</summary>
    /// <response code="422">consent.location_required.</response>
    [EnableRateLimiting(RateLimitPolicies.LocationUpdate)]
    [HttpPut("location")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity, "application/problem+json")]
    public async Task<IActionResult> UpdateLocation(UpdateLocationRequest request, CancellationToken ct)
    {
        await locations.UpdateAsync(CurrentUserId, request, ct);
        return NoContent();
    }

    /// <summary>
    /// Turn on background location for this phone: returns a key that background tasks send to
    /// PUT /location/background instead of a session token. Replaces any earlier key of this phone.
    /// </summary>
    /// <response code="422">consent.location_required, or location_key.unknown_device (the install id has no session).</response>
    [HttpPost("location-key")]
    [ProducesResponseType<LocationKeyResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity, "application/problem+json")]
    public Task<LocationKeyResponse> IssueLocationKey(IssueLocationKeyRequest request, [FromServices] LocationKeyService keys, CancellationToken ct) =>
        keys.IssueAsync(CurrentUserId, request, ct);

    /// <summary>Turn off background location for this phone. Idempotent.</summary>
    [AllowWithoutCurrentPolicy]
    [HttpPost("location-key/revoke")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> RevokeLocationKey(RevokeLocationKeyRequest request, [FromServices] LocationKeyService keys, CancellationToken ct)
    {
        await keys.RevokeAsync(CurrentUserId, request.DeviceId, "turned_off", ct);
        return NoContent();
    }

    /// <summary>Upload / replace my profile photo (multipart field "photo"). Metadata is stripped.</summary>
    [HttpPost("photo")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(PhotoRequestLimit)]
    [RequestFormLimits(MultipartBodyLengthLimit = PhotoRequestLimit)]
    [ProducesResponseType<ProfileResponse>(StatusCodes.Status200OK)]
    public Task<ProfileResponse> SetPhoto([FromForm(Name = "photo")] IFormFile photo, CancellationToken ct) =>
        profiles.SetPhotoAsync(CurrentUserId, SosController.ToUpload(photo), ct);

    /// <summary>Remove my profile photo.</summary>
    [HttpDelete("photo")]
    [ProducesResponseType<ProfileResponse>(StatusCodes.Status200OK)]
    public Task<ProfileResponse> RemovePhoto(CancellationToken ct) => profiles.RemovePhotoAsync(CurrentUserId, ct);

    /// <summary>Get my profile, current consents and who invited me.</summary>
    [AllowWithoutCurrentPolicy]
    [HttpGet]
    [ProducesResponseType<ProfileResponse>(StatusCodes.Status200OK)]
    public Task<ProfileResponse> Get(CancellationToken ct) => profiles.GetAsync(CurrentUserId, ct);

    /// <summary>Replace my profile details (PUT semantics: omitted optional fields are cleared).</summary>
    /// <response code="422">consent.health_data_required when blood group / emergency contact is sent without HealthData consent.</response>
    [HttpPut("profile")]
    [ProducesResponseType<ProfileResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity, "application/problem+json")]
    public Task<ProfileResponse> UpdateProfile(UpdateProfileRequest request, CancellationToken ct) =>
        profiles.UpdateProfileAsync(CurrentUserId, request, ct);

    /// <summary>Turn nearby SOS alerts on or off.</summary>
    [HttpPut("preferences")]
    [ProducesResponseType<ProfileResponse>(StatusCodes.Status200OK)]
    public Task<ProfileResponse> UpdatePreferences(UpdatePreferencesRequest request, CancellationToken ct) =>
        profiles.UpdatePreferencesAsync(CurrentUserId, request, ct);

    /// <summary>Grant or withdraw a consent (LocationSharing, HealthData). Withdrawal deletes the related data immediately.</summary>
    [AllowWithoutCurrentPolicy]
    [HttpPut("consents")]
    [ProducesResponseType<ConsentState>(StatusCodes.Status200OK)]
    public Task<ConsentState> UpdateConsent(UpdateConsentRequest request, CancellationToken ct) =>
        profiles.UpdateConsentAsync(CurrentUserId, request, ct);

    /// <summary>Accept the current Terms / Privacy Notice. Returns a new access token; keep the existing refresh token.</summary>
    [AllowWithoutCurrentPolicy]
    [HttpPost("policy-acceptance")]
    [ProducesResponseType<AccessTokenResponse>(StatusCodes.Status200OK)]
    public Task<AccessTokenResponse> AcceptPolicy(AcceptPolicyRequest request, CancellationToken ct) =>
        profiles.AcceptPolicyAsync(CurrentUserId, request, ct);

    /// <summary>Register (or re-register) this device's FCM push token.</summary>
    [HttpPost("devices")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> RegisterDevice(RegisterDeviceRequest request, CancellationToken ct)
    {
        await devices.RegisterAsync(CurrentUserId, request, ct);
        return NoContent();
    }

    /// <summary>Stop push notifications to a device token.</summary>
    [AllowWithoutCurrentPolicy]
    [HttpPost("devices/unregister")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> UnregisterDevice(UnregisterDeviceRequest request, CancellationToken ct)
    {
        await devices.UnregisterAsync(CurrentUserId, request, ct);
        return NoContent();
    }

    /// <summary>Download all my personal data as JSON (right of access / portability).</summary>
    [AllowWithoutCurrentPolicy]
    [HttpGet("data-export")]
    [ProducesResponseType<DataExport>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Export([FromServices] IOptions<Microsoft.AspNetCore.Mvc.JsonOptions> json, CancellationToken ct)
    {
        var export = await accounts.ExportAsync(CurrentUserId, ct);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(export, json.Value.JsonSerializerOptions);
        return File(bytes, "application/json", $"community-sos-data-{DateTime.UtcNow:yyyyMMdd}.json");
    }

    /// <summary>Permanently delete my account and personal data.</summary>
    /// <response code="409">account.open_sos - cancel or resolve your active SOS first.</response>
    /// <response code="422">user.cannot_delete_master - the Master Admin account cannot be deleted.</response>
    [AllowWithoutCurrentPolicy]
    [HttpDelete]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
    public async Task<IActionResult> Delete(CancellationToken ct)
    {
        await accounts.DeleteAsync(CurrentUserId, ct);
        return NoContent();
    }
}
