using System.Globalization;
using CommunitySos.Application.Abstractions;
using CommunitySos.Application.Common;
using CommunitySos.Application.Common.Exceptions;
using CommunitySos.Application.Common.Options;
using CommunitySos.Application.Common.Security;
using CommunitySos.Application.Features.Sos.Jobs;
using CommunitySos.Domain.Entities;
using CommunitySos.Domain.Enums;
using CommunitySos.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CommunitySos.Application.Features.Sos;

/// <summary>Raising, viewing, cancelling and resolving SOS requests, plus photo upload.</summary>
public sealed class SosService(
    IAppDbContext db,
    ICurrentUser caller,
    IBlobStorage blobs,
    IImageProcessor images,
    IJobScheduler jobs,
    IOptions<SosOptions> sosOptions,
    TimeProvider clock,
    ILogger<SosService> logger)
{
    public const long MaxUploadBytes = 15 * 1024 * 1024;

    private static readonly SosStatus[] OpenStatuses = [SosStatus.Pending, SosStatus.Escalated, SosStatus.Approved];

    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    /// <summary>
    /// Raises an SOS. Idempotent per ClientRequestId. A User's SOS is assigned to the Sub-Admin who invited
    /// them (or the Master Admin if that Sub-Admin is not active); admins' SOS are auto-approved.
    /// </summary>
    public async Task<CreateSosResult> CreateAsync(CreateSosRequest request, CancellationToken ct)
    {
        var (userId, _) = caller.RequireUser();

        var replay = await db.SosRequests.AsNoTracking()
            .Where(s => s.RequesterUserId == userId && s.ClientRequestId == request.ClientRequestId)
            .Select(s => (Guid?)s.Id)
            .FirstOrDefaultAsync(ct);
        if (replay is { } existingId)
        {
            return new CreateSosResult(await GetAsync(existingId, ct), Created: false);
        }

        var open = await db.SosRequests.AsNoTracking()
            .Where(s => s.RequesterUserId == userId && OpenStatuses.Contains(s.Status))
            .Select(s => (Guid?)s.Id)
            .FirstOrDefaultAsync(ct);
        if (open is { } openId)
        {
            throw new ConflictException("sos.already_open", "You already have an active SOS. Open it to add details or cancel it.")
            {
                Details = new Dictionary<string, object?> { ["sosId"] = openId },
            };
        }

        var requester = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct) ?? throw NotFoundException.For(nameof(User));
        var assignedAdminId = await ResolveAssignedAdminAsync(requester, ct);

        var sos = SosRequest.Create(
            requester,
            assignedAdminId,
            request.ClientRequestId,
            request.Category,
            request.Description,
            new GeoCoordinate(request.Latitude, request.Longitude),
            request.AccuracyMeters,
            request.CapturedAt.ToUniversalTime(),
            Now);

        db.SosRequests.Add(sos);
        await db.SaveChangesAsync(ct);

        logger.LogInformation(
            "SOS {SosId} raised by {UserId}, assigned to {AdminId}, status {Status}",
            sos.Id, userId, assignedAdminId, sos.Status);

        jobs.Enqueue<NotifyAdminOfNewSosJob>(j => j.RunAsync(sos.Id, CancellationToken.None), JobQueue.Critical);
        if (sos.Status == SosStatus.Pending)
        {
            jobs.Schedule<EscalateSosJob>(j => j.RunAsync(sos.Id, CancellationToken.None), TimeSpan.FromMinutes(sosOptions.Value.EscalationMinutes), JobQueue.Critical);
        }

        else if (sos.Status == SosStatus.Approved)
        {
            // An admin's own SOS is auto-approved: alert the neighbourhood straight away.
            jobs.Enqueue<DispatchSosAlertsJob>(j => j.RunAsync(sos.Id, CancellationToken.None), JobQueue.Critical);
        }

        jobs.Enqueue<GeocodeSosJob>(j => j.RunAsync(sos.Id, CancellationToken.None));

        return new CreateSosResult(await GetAsync(sos.Id, ct), Created: true);
    }

    /// <summary>Adds up to 5 photos (in total) to the caller's open SOS. Each photo is sanitised before storage.</summary>
    public async Task<SosDetailResponse> AddPhotosAsync(Guid sosId, IReadOnlyList<PhotoUpload> photos, CancellationToken ct)
    {
        var (userId, _) = caller.RequireUser();
        var sos = await db.SosRequests.Include(s => s.Attachments).FirstOrDefaultAsync(s => s.Id == sosId, ct);
        if (sos is null || !SosAccess.IsRequester(userId, sos))
        {
            throw NotFoundException.For("Sos");
        }

        if (photos.Count == 0)
        {
            throw new UnprocessableException("attachment.none", "Select at least one photo.");
        }

        if (sos.Attachments.Count + photos.Count > SosRequest.MaxAttachments)
        {
            throw new UnprocessableException("sos.too_many_attachments", $"At most {SosRequest.MaxAttachments} photos are allowed per SOS.");
        }

        var uploaded = new List<string>();
        try
        {
            foreach (var photo in photos)
            {
                if (photo.Length is <= 0 or > MaxUploadBytes)
                {
                    throw new UnprocessableException("attachment.too_large", $"Each photo must be under {MaxUploadBytes / (1024 * 1024)} MB.");
                }

                await using var input = photo.OpenReadStream();
                var image = await images.ProcessAsync(input, ct);
                var blobName = $"sos/{sos.Id:N}/{Guid.NewGuid():N}.jpg";

                using (var content = new MemoryStream(image.Content, writable: false))
                {
                    await blobs.UploadAsync(blobName, content, image.ContentType, ct);
                }

                uploaded.Add(blobName);
                sos.AddAttachment(userId, blobName, image.ContentType, image.Content.LongLength, image.Sha256, image.Width, image.Height, Now);
            }

            await db.SaveChangesAsync(ct);
        }
        catch
        {
            // Compensate: never leave orphaned blobs for a failed request.
            foreach (var blobName in uploaded)
            {
                await TryDeleteBlobAsync(blobName);
            }

            throw;
        }

        return await GetAsync(sosId, ct);
    }

    public async Task<SosDetailResponse> GetAsync(Guid sosId, CancellationToken ct)
    {
        var (userId, role) = caller.RequireUser();
        var sos = await db.SosRequests.AsNoTracking()
            .Include(s => s.Attachments)
            .Include(s => s.StatusHistory)
            .FirstOrDefaultAsync(s => s.Id == sosId, ct)
            ?? throw NotFoundException.For("Sos");

        var viewer = await ResolveViewerAsync(userId, role, sos, ct) ?? throw NotFoundException.For("Sos");
        return await ToDetailAsync(sos, viewer, userId, ct);
    }

    public async Task<PagedResult<SosSummary>> ListMineAsync(SosListQuery query, CancellationToken ct)
    {
        var (userId, _) = caller.RequireUser();
        var sos = db.SosRequests.AsNoTracking().Where(s => s.RequesterUserId == userId);
        if (query.Status is { } status)
        {
            sos = sos.Where(s => s.Status == status);
        }

        return await sos
            .OrderByDescending(s => s.CreatedAt)
            .ThenByDescending(s => s.Id)
            .Select(s => new SosSummary(s.Id, s.ReferenceNo, s.Category, s.Status, s.Address, s.IsAutoApproved, s.Attachments.Count, s.CreatedAt, s.ClosedAt))
            .ToPagedAsync(query, ct);
    }

    public async Task<SosDetailResponse> CancelAsync(Guid sosId, CancelSosRequest request, CancellationToken ct)
    {
        var (userId, _) = caller.RequireUser();
        var sos = await db.SosRequests.FirstOrDefaultAsync(s => s.Id == sosId, ct);
        if (sos is null || !SosAccess.CanCancel(userId, sos))
        {
            throw NotFoundException.For("Sos");
        }

        var wasApproved = sos.Status == SosStatus.Approved;
        sos.Cancel(userId, request.Reason, Now);
        await db.SaveChangesAsync(ct);
        if (wasApproved)
        {
            jobs.Enqueue<NotifyRespondersOfClosureJob>(j => j.RunAsync(sosId, CancellationToken.None));
        }

        return await GetAsync(sosId, ct);
    }

    public async Task<SosDetailResponse> ResolveAsync(Guid sosId, ResolveSosRequest request, CancellationToken ct)
    {
        var (userId, role) = caller.RequireUser();
        var sos = await db.SosRequests.FirstOrDefaultAsync(s => s.Id == sosId, ct);
        if (sos is null || (!SosAccess.CanResolve(userId, role, sos) && await ResolveViewerAsync(userId, role, sos, ct) is null))
        {
            throw NotFoundException.For("Sos");
        }

        if (!SosAccess.CanResolve(userId, role, sos))
        {
            throw new ForbiddenException("sos.forbidden", "Only the requester or the reviewing admin can mark this SOS resolved.");
        }

        sos.Resolve(userId, request.Remarks, Now);
        await db.SaveChangesAsync(ct);
        jobs.Enqueue<NotifyRespondersOfClosureJob>(j => j.RunAsync(sosId, CancellationToken.None));
        return await GetAsync(sosId, ct);
    }

    /// <summary>Requester's referrer if active, otherwise the Master Admin. Admins' SOS go to the Master Admin.</summary>
    private async Task<Guid> ResolveAssignedAdminAsync(User requester, CancellationToken ct)
    {
        if (requester.Role == UserRole.MasterAdmin)
        {
            return requester.Id;
        }

        if (requester.ReferredByAdminId is { } referrerId
            && await db.Users.AnyAsync(u => u.Id == referrerId && u.Status == UserStatus.Active, ct))
        {
            return referrerId;
        }

        var masterId = await db.Users
            .Where(u => u.Role == UserRole.MasterAdmin && u.Status == UserStatus.Active)
            .Select(u => (Guid?)u.Id)
            .FirstOrDefaultAsync(ct);

        logger.LogWarning("SOS from {UserId}: referring admin unavailable, routing to Master Admin", requester.Id);
        return masterId ?? throw new UnprocessableException("sos.no_admin", "No admin is available right now. Please call 112.");
    }

    private async Task<SosViewerRole?> ResolveViewerAsync(Guid userId, UserRole role, SosRequest sos, CancellationToken ct)
    {
        if (SosAccess.IsRequester(userId, sos))
        {
            return SosViewerRole.Requester;
        }

        if (SosAccess.CanReview(userId, role, sos))
        {
            return SosViewerRole.Admin;
        }

        if (SosAccess.RecipientMayView(sos)
            && await db.SosNotifications.AnyAsync(n => n.SosRequestId == sos.Id && n.RecipientUserId == userId, ct))
        {
            return SosViewerRole.Recipient;
        }

        return null;
    }

    private async Task<SosDetailResponse> ToDetailAsync(SosRequest sos, SosViewerRole viewer, Guid viewerId, CancellationToken ct)
    {
        var requester = await db.Users.AsNoTracking()
            .Where(u => u.Id == sos.RequesterUserId)
            .Select(u => new { u.Id, u.FullName, u.PhoneNumber, u.BloodGroup, u.EmergencyContactName, u.EmergencyContactPhone })
            .FirstAsync(ct);

        var firstName = requester.FullName?.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        var requesterInfo = viewer switch
        {
            SosViewerRole.Admin => new SosRequesterInfo(
                firstName,
                requester.Id,
                requester.FullName,
                requester.PhoneNumber.Value,
                requester.BloodGroup,
                requester.EmergencyContactName,
                requester.EmergencyContactPhone?.Value),
            SosViewerRole.Requester => new SosRequesterInfo(firstName, requester.Id, requester.FullName),
            _ => new SosRequesterInfo(firstName),
        };

        var photos = new List<SosPhoto>(sos.Attachments.Count);
        foreach (var a in sos.Attachments.OrderBy(a => a.UploadedAt))
        {
            var url = await blobs.GetReadUriAsync(a.BlobName, ct);
            photos.Add(new SosPhoto(a.Id, url.ToString(), a.Width, a.Height, a.UploadedAt));
        }

        var isRecipient = viewer == SosViewerRole.Recipient;
        var coordinate = sos.Coordinate;
        var myAlert = isRecipient ? await LoadMyAlertAsync(sos.Id, viewerId, ct) : null;
        var responders = !isRecipient && sos.RecipientCount > 0 ? await LoadRespondersAsync(sos.Id, ct) : null;

        return new SosDetailResponse(
            sos.Id,
            sos.ReferenceNo,
            sos.Category,
            sos.Description,
            new SosLocation(coordinate.Latitude, coordinate.Longitude, sos.AccuracyMeters, MapsUrl(coordinate)),
            sos.Address,
            sos.Status,
            sos.IsAutoApproved,
            sos.CreatedAt,
            sos.ReviewedAt,
            isRecipient ? null : sos.ReviewRemarks,
            sos.ClosedAt,
            isRecipient ? null : sos.RecipientCount,
            viewer,
            requesterInfo,
            photos,
            isRecipient
                ? null
                : sos.StatusHistory
                    .OrderBy(h => h.ChangedAt).ThenBy(h => h.Id)
                    .Select(h => new SosTimelineEntry(h.FromStatus, h.ToStatus, h.Remarks, h.ChangedAt, h.ChangedByUserId is null))
                    .ToList(),
            myAlert,
            responders);
    }

    private Task<SosMyAlert?> LoadMyAlertAsync(Guid sosId, Guid userId, CancellationToken ct) =>
        db.SosNotifications.AsNoTracking()
            .Where(n => n.SosRequestId == sosId && n.RecipientUserId == userId)
            .Select(n => new SosMyAlert(n.DistanceMeters, n.CreatedAt, n.AcknowledgedAt, n.ResponseType, n.ResponseNote, n.RespondedAt))
            .FirstOrDefaultAsync(ct);

    private async Task<SosResponders> LoadRespondersAsync(Guid sosId, CancellationToken ct)
    {
        var rows = await (
                from n in db.SosNotifications.AsNoTracking()
                join u in db.Users.AsNoTracking() on n.RecipientUserId equals u.Id
                where n.SosRequestId == sosId
                select new { n.AcknowledgedAt, n.ResponseType, n.ResponseNote, n.RespondedAt, n.DistanceMeters, u.FullName, u.Status })
            .ToListAsync(ct);

        var helping = rows
            .Where(r => (r.ResponseType is ResponseType.CanHelp or ResponseType.OnTheWay or ResponseType.Arrived) && r.RespondedAt is not null)
            .OrderByDescending(r => r.ResponseType == ResponseType.Arrived)
            .ThenByDescending(r => r.ResponseType == ResponseType.OnTheWay)
            .ThenBy(r => r.DistanceMeters)
            .Select(r => new SosResponder(
                r.Status == UserStatus.Deleted ? null : r.FullName?.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault(),
                r.ResponseType,
                r.DistanceMeters,
                r.RespondedAt!.Value,
                r.ResponseNote))
            .ToList();

        return new SosResponders(
            rows.Count,
            rows.Count(r => r.AcknowledgedAt is not null),
            rows.Count(r => r.ResponseType == ResponseType.CanHelp),
            rows.Count(r => r.ResponseType == ResponseType.OnTheWay),
            rows.Count(r => r.ResponseType == ResponseType.Arrived),
            rows.Count(r => r.ResponseType == ResponseType.CannotHelp),
            helping);
    }

    private static string MapsUrl(GeoCoordinate c) =>
        string.Create(CultureInfo.InvariantCulture, $"https://www.google.com/maps/search/?api=1&query={c.Latitude},{c.Longitude}");

    private async Task TryDeleteBlobAsync(string blobName)
    {
        try
        {
            await blobs.DeleteAsync(blobName, CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not delete orphaned blob {BlobName}", blobName);
        }
    }
}
