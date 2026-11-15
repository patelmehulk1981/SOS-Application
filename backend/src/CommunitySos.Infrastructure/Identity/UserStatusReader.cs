using CommunitySos.Application.Abstractions;
using CommunitySos.Domain.Enums;
using CommunitySos.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace CommunitySos.Infrastructure.Identity;

/// <summary>
/// Per-request "is the account still active?" check with a 30-second in-memory cache, so a suspension
/// or deletion takes effect quickly without a database hit on every request.
/// With several instances, each instance may serve a stale "active" for up to <see cref="CacheDuration"/>.
/// </summary>
public sealed class UserStatusReader(AppDbContext db, IMemoryCache cache) : IUserStatusReader
{
    private static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(30);

    public async Task<bool> IsActiveAsync(Guid userId, CancellationToken cancellationToken)
    {
        if (cache.TryGetValue(Key(userId), out bool active))
        {
            return active;
        }

        var status = await db.Users
            .AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => (UserStatus?)u.Status)
            .FirstOrDefaultAsync(cancellationToken);

        active = status == UserStatus.Active;
        cache.Set(Key(userId), active, CacheDuration);
        return active;
    }

    public void Invalidate(Guid userId) => cache.Remove(Key(userId));

    private static string Key(Guid userId) => $"user-status:{userId:N}";
}
