using CommunitySos.Application.Abstractions;
using CommunitySos.Application.Common.Exceptions;
using CommunitySos.Domain.Entities;
using CommunitySos.Domain.Enums;
using CommunitySos.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;

namespace CommunitySos.Application.Features.Sos;

/// <summary>
/// Stores the caller's LAST KNOWN location (one row, overwritten) for the 5 km alert search.
/// Requires LocationSharing consent; nothing is stored otherwise and no history is kept.
/// Called from the app (signed in) and from background work (location key, see LocationKeyService).
/// </summary>
public sealed class LocationService(IAppDbContext db, TimeProvider clock)
{
    public async Task UpdateAsync(Guid userId, UpdateLocationRequest request, CancellationToken ct)
    {
        var sharing = await db.Users.AsNoTracking()
            .Where(u => u.Id == userId && u.Status == UserStatus.Active)
            .Select(u => (bool?)u.LocationSharingEnabled)
            .FirstOrDefaultAsync(ct);

        if (sharing is not true)
        {
            throw ConsentRequired();
        }

        await StageAsync(userId, request, ct);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Adds or updates the tracked location row without saving, for callers that have already checked
    /// consent. Older fixes than the stored one (out-of-order delivery) are ignored.
    /// </summary>
    internal async Task StageAsync(Guid userId, UpdateLocationRequest request, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var coordinate = new GeoCoordinate(request.Latitude, request.Longitude);
        var capturedAt = request.CapturedAt.ToUniversalTime();

        var location = await db.UserLocations.FirstOrDefaultAsync(l => l.UserId == userId, ct);
        if (location is null)
        {
            db.UserLocations.Add(UserLocation.Create(userId, coordinate, request.AccuracyMeters, capturedAt, now));
        }
        else
        {
            location.Update(coordinate, request.AccuracyMeters, capturedAt, now);
        }
    }

    internal static UnprocessableException ConsentRequired() => new(
        "consent.location_required",
        "Turn on location sharing in Privacy settings to receive nearby SOS alerts.");
}
