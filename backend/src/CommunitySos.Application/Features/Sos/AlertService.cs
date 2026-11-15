using CommunitySos.Application.Abstractions;
using CommunitySos.Application.Common;
using CommunitySos.Application.Common.Exceptions;
using CommunitySos.Application.Common.Security;
using CommunitySos.Application.Features.Sos.Jobs;
using CommunitySos.Domain.Entities;
using CommunitySos.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CommunitySos.Application.Features.Sos;

/// <summary>
/// The neighbour side of an SOS: the "nearby alerts" feed, "I've seen it" and "I can help / on my way /
/// arrived / can't help". Only people who were alerted can act; anyone else gets 404.
/// </summary>
public sealed class AlertService(
    IAppDbContext db,
    ICurrentUser caller,
    SosService sosService,
    IJobScheduler jobs,
    TimeProvider clock,
    ILogger<AlertService> logger)
{
    /// <summary>Alerts I received, newest first. Addresses only for SOS that still need help.</summary>
    public async Task<PagedResult<SosAlertItem>> ListMineAsync(AlertListQuery query, CancellationToken ct)
    {
        var (userId, _) = caller.RequireUser();
        var rows =
            from n in db.SosNotifications.AsNoTracking()
            join s in db.SosRequests.AsNoTracking() on n.SosRequestId equals s.Id
            where n.RecipientUserId == userId
            select new { n, s };

        if (query.OpenOnly)
        {
            rows = rows.Where(x => x.s.Status == SosStatus.Approved);
        }

        return await rows
            .OrderByDescending(x => x.n.CreatedAt)
            .ThenByDescending(x => x.n.Id)
            .Select(x => new SosAlertItem(
                x.s.Id,
                x.s.ReferenceNo,
                x.s.Category,
                x.s.Status,
                x.n.DistanceMeters,
                x.s.Status == SosStatus.Approved ? x.s.Address : null,
                x.n.CreatedAt,
                x.n.AcknowledgedAt,
                x.n.ResponseType,
                x.n.RespondedAt))
            .ToPagedAsync(query, ct);
    }

    /// <summary>"I have seen this alert" (idempotent; allowed after closure too).</summary>
    public async Task AcknowledgeAsync(Guid sosId, CancellationToken ct)
    {
        var notification = await LoadMineAsync(sosId, ct);
        notification.Acknowledge(clock.GetUtcNow().UtcDateTime);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Records my response while the SOS still needs help. The requester is pushed when someone newly offers
    /// help or arrives. Returns the SOS as I see it (recipient view, with my response).
    /// </summary>
    public async Task<SosDetailResponse> RespondAsync(Guid sosId, RespondToSosRequest request, CancellationToken ct)
    {
        var notification = await LoadMineAsync(sosId, ct);
        var status = await db.SosRequests.Where(s => s.Id == sosId).Select(s => s.Status).FirstAsync(ct);
        if (status != SosStatus.Approved)
        {
            throw new UnprocessableException("sos.not_active", "This SOS no longer needs help. Thank you for responding.");
        }

        var tellRequester = notification.Respond(request.Response, request.Note, clock.GetUtcNow().UtcDateTime);
        await db.SaveChangesAsync(ct);

        logger.LogInformation("SOS {SosId}: recipient {UserId} responded {Response}", sosId, notification.RecipientUserId, request.Response);
        if (tellRequester)
        {
            jobs.Enqueue<NotifyRequesterOfResponseJob>(j => j.RunAsync(sosId, CancellationToken.None), JobQueue.Critical);
        }

        return await sosService.GetAsync(sosId, ct);
    }

    private async Task<SosNotification> LoadMineAsync(Guid sosId, CancellationToken ct)
    {
        var (userId, _) = caller.RequireUser();
        return await db.SosNotifications.FirstOrDefaultAsync(n => n.SosRequestId == sosId && n.RecipientUserId == userId, ct)
            ?? throw NotFoundException.For("Sos");
    }
}
