using CommunitySos.Domain.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace CommunitySos.Infrastructure.Persistence.Interceptors;

/// <summary>Maintains CreatedAt / UpdatedAt for every <see cref="IHasTimestamps"/> entity.</summary>
public sealed class TimestampInterceptor(TimeProvider clock) : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Apply(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        Apply(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void Apply(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        context.ChangeTracker.DetectChanges();
        var now = clock.GetUtcNow().UtcDateTime;

        foreach (var entry in context.ChangeTracker.Entries<IHasTimestamps>())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    entry.Property(nameof(IHasTimestamps.CreatedAt)).CurrentValue = now;
                    entry.Property(nameof(IHasTimestamps.UpdatedAt)).CurrentValue = now;
                    break;
                case EntityState.Modified:
                    entry.Property(nameof(IHasTimestamps.CreatedAt)).IsModified = false;
                    entry.Property(nameof(IHasTimestamps.UpdatedAt)).CurrentValue = now;
                    break;
            }
        }
    }
}
