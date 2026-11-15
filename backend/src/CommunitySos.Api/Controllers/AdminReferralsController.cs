using CommunitySos.Api.Security;
using CommunitySos.Application.Common;
using CommunitySos.Application.Features.Admin;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CommunitySos.Api.Controllers;

/// <summary>Referral history and the Master Admin's referral tree. Examples: docs/03c-invitations-admin.md.</summary>
[Authorize(Policy = Policies.Admin)]
[Route("api/v1/admin/referrals")]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
public sealed class AdminReferralsController(ReferralService referrals) : ApiControllerBase
{
    /// <summary>Who joined through whom, newest first. Sub-Admins see their own referrals.</summary>
    [HttpGet]
    [ProducesResponseType<PagedResult<ReferralEntry>>(StatusCodes.Status200OK)]
    public Task<PagedResult<ReferralEntry>> List([FromQuery] ReferralListQuery query, CancellationToken ct) =>
        referrals.ListAsync(query, ct);

    /// <summary>Master Admin → Sub-Admins → Users, with community size per Sub-Admin.</summary>
    [Authorize(Policy = Policies.MasterAdmin)]
    [HttpGet("tree")]
    [ProducesResponseType<ReferralTree>(StatusCodes.Status200OK)]
    public Task<ReferralTree> Tree(CancellationToken ct) => referrals.GetTreeAsync(ct);
}
