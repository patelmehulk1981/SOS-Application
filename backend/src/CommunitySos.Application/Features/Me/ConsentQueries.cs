using CommunitySos.Application.Abstractions;
using CommunitySos.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace CommunitySos.Application.Features.Me;

internal static class ConsentQueries
{
    /// <summary>Latest ledger entry per consent type. A user has only a handful of rows, so this is done in memory.</summary>
    public static async Task<ConsentState> GetCurrentAsync(this IAppDbContext db, Guid userId, CancellationToken ct)
    {
        var rows = await db.UserConsents
            .AsNoTracking()
            .Where(c => c.UserId == userId)
            .Select(c => new { c.Id, c.Type, c.Granted, c.CapturedAt })
            .ToListAsync(ct);

        bool Latest(ConsentType type) => rows
            .Where(r => r.Type == type)
            .OrderByDescending(r => r.CapturedAt)
            .ThenByDescending(r => r.Id)
            .Select(r => r.Granted)
            .FirstOrDefault();

        return new ConsentState(
            Latest(ConsentType.TermsAndPrivacy),
            Latest(ConsentType.LocationSharing),
            Latest(ConsentType.HealthData));
    }
}
