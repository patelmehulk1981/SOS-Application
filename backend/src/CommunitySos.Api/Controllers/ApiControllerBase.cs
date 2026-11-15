using CommunitySos.Api.Security;
using Microsoft.AspNetCore.Mvc;

namespace CommunitySos.Api.Controllers;

/// <summary>
/// Base for all API controllers: JSON in/out, versioned route prefix, and the standard error responses
/// documented once. Authentication is enforced by the global fallback policy.
/// </summary>
[ApiController]
[Route("api/v1/[controller]")]
[Produces("application/json")]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests, "application/problem+json")]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status500InternalServerError, "application/problem+json")]
public abstract class ApiControllerBase : ControllerBase
{
    /// <summary>The authenticated caller's id. Only call from actions that require authentication.</summary>
    protected Guid CurrentUserId =>
        User.GetUserId() ?? throw new InvalidOperationException("No authenticated user on this request.");
}
