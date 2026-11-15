using CommunitySos.Api.Security;
using CommunitySos.Application.Common;
using CommunitySos.Application.Features.Admin;
using CommunitySos.Application.Features.Sos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CommunitySos.Api.Controllers;

/// <summary>
/// Admin review of SOS requests. Sub-Admins see and act on SOS assigned to them; the Master Admin on all
/// (including escalations). Full details of one SOS: GET /api/v1/sos/{id}. Examples: docs/03e-admin-review.md.
/// </summary>
[Authorize(Policy = Policies.Admin)]
[Route("api/v1/admin/sos")]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
public sealed class AdminSosController(SosReviewService review) : ApiControllerBase
{
    /// <summary>Review queue (awaitingReview=true, oldest first) or SOS history (newest first).</summary>
    [HttpGet]
    [ProducesResponseType<PagedResult<AdminSosListItem>>(StatusCodes.Status200OK)]
    public Task<PagedResult<AdminSosListItem>> List([FromQuery] AdminSosListQuery query, CancellationToken ct) => review.ListAsync(query, ct);

    /// <summary>SOS pins inside a map bounding box (max 1000).</summary>
    [HttpGet("map")]
    [ProducesResponseType<SosMapResponse>(StatusCodes.Status200OK)]
    public Task<SosMapResponse> Map([FromQuery] SosMapQuery query, CancellationToken ct) => review.GetMapAsync(query, ct);

    /// <summary>Approve: the requester is notified and (Module 5) nearby members are alerted.</summary>
    /// <response code="409">concurrency_conflict - another admin acted on this SOS at the same moment.</response>
    /// <response code="422">sos.already_reviewed.</response>
    [HttpPost("{id:guid}/approve")]
    [ProducesResponseType<SosDetailResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity, "application/problem+json")]
    public Task<SosDetailResponse> Approve(Guid id, ApproveSosRequest request, CancellationToken ct) => review.ApproveAsync(id, request, ct);

    /// <summary>Reject with remarks (shown to the requester).</summary>
    [HttpPost("{id:guid}/reject")]
    [ProducesResponseType<SosDetailResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity, "application/problem+json")]
    public Task<SosDetailResponse> Reject(Guid id, RejectSosRequest request, CancellationToken ct) => review.RejectAsync(id, request, ct);
}

/// <summary>Dashboard counters: users and SOS in the caller's scope.</summary>
[Authorize(Policy = Policies.Admin)]
[Route("api/v1/admin/dashboard")]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
public sealed class AdminDashboardController(SosReviewService review) : ApiControllerBase
{
    [HttpGet]
    [ProducesResponseType<DashboardStats>(StatusCodes.Status200OK)]
    public Task<DashboardStats> Get([FromQuery] DashboardQuery query, CancellationToken ct) => review.GetDashboardAsync(query, ct);
}
