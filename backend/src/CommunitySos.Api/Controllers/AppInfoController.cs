using CommunitySos.Application.Common.Options;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace CommunitySos.Api.Controllers;

/// <summary>Public bootstrap info the app fetches on launch (force-update check, policy version, emergency numbers).</summary>
[AllowAnonymous]
[Route("api/v1/app-info")]
public sealed class AppInfoController(IOptionsSnapshot<AppOptions> options) : ApiControllerBase
{
    /// <summary>Get app bootstrap configuration.</summary>
    /// <remarks>
    /// Example response:
    ///
    ///     {
    ///       "policyVersion": "2026-11",
    ///       "minimumAppVersion": { "android": "1.0.0", "ios": "1.0.0" },
    ///       "emergencyNumbers": [ { "label": "National Emergency", "number": "112" } ],
    ///       "grievanceContactEmail": "grievance@example.com"
    ///     }
    /// </remarks>
    [HttpGet]
    [ProducesResponseType<AppInfoResponse>(StatusCodes.Status200OK)]
    public ActionResult<AppInfoResponse> Get()
    {
        var app = options.Value;
        Response.Headers.CacheControl = "public, max-age=300"; // not sensitive; overrides the API default
        return new AppInfoResponse(
            app.PolicyVersion,
            new MinimumAppVersion(app.MinimumAppVersion.Android, app.MinimumAppVersion.Ios),
            app.EmergencyNumbers.Select(n => new EmergencyNumber(n.Label, n.Number)).ToList(),
            app.GrievanceContactEmail);
    }
}

public sealed record AppInfoResponse(
    string PolicyVersion,
    MinimumAppVersion MinimumAppVersion,
    IReadOnlyList<EmergencyNumber> EmergencyNumbers,
    string? GrievanceContactEmail);

public sealed record MinimumAppVersion(string Android, string Ios);

public sealed record EmergencyNumber(string Label, string Number);
