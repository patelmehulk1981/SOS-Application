using CommunitySos.Api.Security;
using CommunitySos.Application.Common;
using CommunitySos.Application.Features.Admin;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CommunitySos.Api.Controllers;

/// <summary>
/// User directory for admins. Sub-Admins see only the users they referred; the Master Admin sees everyone
/// and can suspend/reactivate accounts. Examples: docs/03c-invitations-admin.md.
/// </summary>
[Authorize(Policy = Policies.Admin)]
[Route("api/v1/admin/users")]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
public sealed class AdminUsersController(AdminUserService users) : ApiControllerBase
{
    /// <summary>Search and list users in my scope.</summary>
    [HttpGet]
    [ProducesResponseType<PagedResult<AdminUserListItem>>(StatusCodes.Status200OK)]
    public Task<PagedResult<AdminUserListItem>> List([FromQuery] UserListQuery query, CancellationToken ct) =>
        users.ListAsync(query, ct);

    /// <summary>One user's admin view (no health data or location).</summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType<AdminUserDetail>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    public Task<AdminUserDetail> Get(Guid id, CancellationToken ct) => users.GetAsync(id, ct);

    /// <summary>Suspend or reactivate an account (Master Admin).</summary>
    /// <remarks>Suspension signs the user out on every device within seconds.</remarks>
    [Authorize(Policy = Policies.MasterAdmin)]
    [HttpPut("{id:guid}/status")]
    [ProducesResponseType<AdminUserDetail>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity, "application/problem+json")]
    public Task<AdminUserDetail> UpdateStatus(Guid id, UpdateUserStatusRequest request, CancellationToken ct) =>
        users.UpdateStatusAsync(id, request, ct);
}
