using CommunitySos.Application.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace CommunitySos.Application.Features.Sos.Jobs;

/// <summary>
/// Fills in the human-readable address after the SOS is saved, so a slow or failing geocoder never
/// delays an emergency. Safe to run more than once.
/// </summary>
public sealed class GeocodeSosJob(IAppDbContext db, IGeocoder geocoder)
{
    public async Task RunAsync(Guid sosId, CancellationToken cancellationToken)
    {
        var sos = await db.SosRequests.FirstOrDefaultAsync(s => s.Id == sosId, cancellationToken);
        if (sos is null || sos.Address is not null)
        {
            return;
        }

        var address = await geocoder.ReverseGeocodeAsync(sos.Coordinate, cancellationToken);
        if (address is null)
        {
            return;
        }

        sos.SetAddress(address);
        await db.SaveChangesAsync(cancellationToken);
    }
}
