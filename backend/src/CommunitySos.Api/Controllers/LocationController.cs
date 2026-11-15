using CommunitySos.Api.Extensions;
using CommunitySos.Application.Features.Sos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace CommunitySos.Api.Controllers;

/// <summary>
/// Location reports from background work on the phone (docs/06-background-location.md). Authenticated by
/// a location key in the X-Location-Key header, not by a session token, so background tasks never rotate
/// the session.
/// </summary>
public sealed class LocationController(LocationKeyService keys) : ApiControllerBase
{
    public const string KeyHeader = "X-Location-Key";

    /// <summary>Report my last-known location from the background. Same body as PUT /me/location.</summary>
    /// <response code="401">location_key.invalid: stop background updates; the app asks for a new key when next open.</response>
    /// <response code="422">consent.location_required: location sharing was turned off (the key is revoked).</response>
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.LocationBackground)]
    [HttpPut("background")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity, "application/problem+json")]
    public async Task<IActionResult> ReportBackground(UpdateLocationRequest request, CancellationToken ct)
    {
        await keys.ReportAsync(Request.Headers[KeyHeader].ToString(), request, ct);
        return NoContent();
    }
}
