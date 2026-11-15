using CommunitySos.Api.Extensions;
using CommunitySos.Application.Common;
using CommunitySos.Application.Features.Sos;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace CommunitySos.Api.Controllers;

/// <summary>
/// Raise and track SOS requests. Raising is a small JSON call so it succeeds even on poor networks;
/// photos follow separately. Examples: docs/03d-sos.md.
/// </summary>
public sealed class SosController(SosService sos, AlertService alerts) : ApiControllerBase
{
    /// <summary>Total multipart body limit for one photo request (5 photos × 15 MB + overhead).</summary>
    private const long PhotoRequestLimit = 80L * 1024 * 1024;

    /// <summary>Raise an SOS. Idempotent: resending the same clientRequestId returns the original SOS with 200.</summary>
    /// <response code="201">Created. Admin SOS are already Approved.</response>
    /// <response code="200">Replay of an earlier request with the same clientRequestId.</response>
    /// <response code="409">sos.already_open - the response includes "sosId" of the active SOS.</response>
    [EnableRateLimiting(RateLimitPolicies.SosCreate)]
    [HttpPost]
    [ProducesResponseType<SosDetailResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<SosDetailResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity, "application/problem+json")]
    public async Task<ActionResult<SosDetailResponse>> Create(CreateSosRequest request, CancellationToken ct)
    {
        var result = await sos.CreateAsync(request, ct);
        if (!result.Created)
        {
            Response.Headers["Idempotent-Replayed"] = "true";
            return Ok(result.Sos);
        }

        return CreatedAtAction(nameof(Get), new { id = result.Sos.Id }, result.Sos);
    }

    /// <summary>Attach photos (multipart field "photos", 1–5 files, JPEG/PNG/WebP). Photos are re-encoded and stripped of metadata.</summary>
    [HttpPost("{id:guid}/photos")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(PhotoRequestLimit)]
    [RequestFormLimits(MultipartBodyLengthLimit = PhotoRequestLimit)]
    [ProducesResponseType<SosDetailResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity, "application/problem+json")]
    public Task<SosDetailResponse> AddPhotos(Guid id, [FromForm(Name = "photos")] List<IFormFile> photos, CancellationToken ct) =>
        sos.AddPhotosAsync(id, photos.Select(ToUpload).ToList(), ct);

    /// <summary>My SOS requests, newest first.</summary>
    [HttpGet("mine")]
    [ProducesResponseType<PagedResult<SosSummary>>(StatusCodes.Status200OK)]
    public Task<PagedResult<SosSummary>> Mine([FromQuery] SosListQuery query, CancellationToken ct) => sos.ListMineAsync(query, ct);

    /// <summary>SOS details, shaped for the caller (requester, reviewing admin, or alerted user).</summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType<SosDetailResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    public Task<SosDetailResponse> Get(Guid id, CancellationToken ct) => sos.GetAsync(id, ct);

    /// <summary>Cancel my SOS (false alarm / no longer needed).</summary>
    [HttpPost("{id:guid}/cancel")]
    [ProducesResponseType<SosDetailResponse>(StatusCodes.Status200OK)]
    public Task<SosDetailResponse> Cancel(Guid id, CancelSosRequest request, CancellationToken ct) => sos.CancelAsync(id, request, ct);

    /// <summary>Mark an approved SOS resolved (requester or reviewing admin).</summary>
    [HttpPost("{id:guid}/resolve")]
    [ProducesResponseType<SosDetailResponse>(StatusCodes.Status200OK)]
    public Task<SosDetailResponse> Resolve(Guid id, ResolveSosRequest request, CancellationToken ct) => sos.ResolveAsync(id, request, ct);

    /// <summary>Nearby alerts I received (neighbour side), newest first. openOnly=true: only SOS still needing help.</summary>
    [HttpGet("alerts")]
    [ProducesResponseType<PagedResult<SosAlertItem>>(StatusCodes.Status200OK)]
    public Task<PagedResult<SosAlertItem>> Alerts([FromQuery] AlertListQuery query, CancellationToken ct) => alerts.ListMineAsync(query, ct);

    /// <summary>"I have seen this alert". Idempotent.</summary>
    /// <response code="404">You were not alerted about this SOS.</response>
    [HttpPost("{id:guid}/acknowledge")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    public async Task<IActionResult> Acknowledge(Guid id, CancellationToken ct)
    {
        await alerts.AcknowledgeAsync(id, ct);
        return NoContent();
    }

    /// <summary>Respond to an alert: CanHelp, OnTheWay, Arrived or CannotHelp (can be changed while the SOS is open).</summary>
    /// <response code="422">sos.not_active - the SOS no longer needs help.</response>
    [HttpPut("{id:guid}/response")]
    [ProducesResponseType<SosDetailResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity, "application/problem+json")]
    public Task<SosDetailResponse> Respond(Guid id, RespondToSosRequest request, CancellationToken ct) => alerts.RespondAsync(id, request, ct);

    internal static PhotoUpload ToUpload(IFormFile file) => new(file.FileName, file.Length, file.OpenReadStream);
}
