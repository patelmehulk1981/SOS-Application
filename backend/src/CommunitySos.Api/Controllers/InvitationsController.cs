using CommunitySos.Api.Extensions;
using CommunitySos.Api.Security;
using CommunitySos.Application.Common;
using CommunitySos.Application.Features.Invitations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace CommunitySos.Api.Controllers;

/// <summary>
/// Invitation links. Master Admin links create Sub-Admins; Sub-Admin links create Users.
/// Request/response examples: docs/03c-invitations-admin.md.
/// </summary>
public sealed class InvitationsController(InvitationService invitations) : ApiControllerBase
{
    /// <summary>Public preview of an invitation for the sign-up screen ("Invited by …").</summary>
    /// <response code="404">invitation.not_found - unknown or malformed code.</response>
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.InviteLookup)]
    [HttpGet("{code}")]
    [ProducesResponseType<InvitationPreview>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    public Task<InvitationPreview> Preview(string code, CancellationToken ct) => invitations.PreviewAsync(code, ct);

    /// <summary>Create a shareable invitation link.</summary>
    /// <response code="422">invitation.limit_reached - too many active links.</response>
    [Authorize(Policy = Policies.Admin)]
    [HttpPost]
    [ProducesResponseType<InvitationResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
    public async Task<ActionResult<InvitationResponse>> Create(CreateInvitationRequest request, CancellationToken ct)
    {
        var created = await invitations.CreateAsync(request, ct);
        return CreatedAtAction(nameof(Preview), new { code = created.Code }, created);
    }

    /// <summary>List my invitation links, newest first.</summary>
    [Authorize(Policy = Policies.Admin)]
    [HttpGet]
    [ProducesResponseType<PagedResult<InvitationResponse>>(StatusCodes.Status200OK)]
    public Task<PagedResult<InvitationResponse>> List([FromQuery] InvitationListQuery query, CancellationToken ct) =>
        invitations.ListAsync(query, ct);

    /// <summary>Revoke a link. People who already joined through it are unaffected.</summary>
    [Authorize(Policy = Policies.Admin)]
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    public async Task<IActionResult> Revoke(Guid id, CancellationToken ct)
    {
        await invitations.RevokeAsync(id, ct);
        return NoContent();
    }
}
