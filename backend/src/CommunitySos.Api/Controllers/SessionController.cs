using CommunitySos.Api.Security;
using CommunitySos.Application.Common.Security;
using CommunitySos.Domain.Enums;
using Microsoft.AspNetCore.Mvc;

namespace CommunitySos.Api.Controllers;

/// <summary>Lets the app confirm its access token is still accepted and learn the caller's role.</summary>
[AllowWithoutCurrentPolicy]
public sealed class SessionController : ApiControllerBase
{
    /// <summary>Describe the current session.</summary>
    /// <remarks>
    /// Example response:
    ///
    ///     { "userId": "6f1c2a9e-0b7d-4c55-9a51-0193a1c2d4e5", "role": "SubAdmin", "policyVersion": "2026-11" }
    /// </remarks>
    [HttpGet]
    [ProducesResponseType<SessionResponse>(StatusCodes.Status200OK)]
    public ActionResult<SessionResponse> Get() => new SessionResponse(
        CurrentUserId,
        User.GetRole() ?? UserRole.User,
        User.FindFirst(AppClaimTypes.PolicyVersion)?.Value);
}

public sealed record SessionResponse(Guid UserId, UserRole Role, string? PolicyVersion);
