using System.Text.Json;
using CommunitySos.Application.Abstractions;
using CommunitySos.Domain.Common;
using CommunitySos.Domain.Entities;
using CommunitySos.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using NetTopologySuite.Geometries;

namespace CommunitySos.Infrastructure.Persistence.Interceptors;

/// <summary>
/// Writes an <see cref="AuditLog"/> row for every insert/update/delete of an <see cref="IAuditedEntity"/>,
/// in the same transaction as the change itself. Only changed columns are recorded for updates.
/// Phone numbers are masked; concurrency tokens and timestamps are omitted.
/// </summary>
public sealed class AuditInterceptor(ICurrentUser currentUser, TimeProvider clock) : SaveChangesInterceptor
{
    private static readonly HashSet<string> IgnoredProperties = new(StringComparer.Ordinal)
    {
        "RowVersion",
        "ReferenceSeq", // database-generated, not known until after insert
        "ReferenceNo",
        nameof(IHasTimestamps.CreatedAt),
        nameof(IHasTimestamps.UpdatedAt),
        nameof(User.LastLoginAt), // logins are recorded as explicit "Login" security events
    };

    /// <summary>
    /// Personal/health fields: the audit records THAT they changed, never their values, so erasing a
    /// profile (DPDP s.12 / GDPR Art. 17) does not leave a copy in the 1-year audit trail.
    /// </summary>
    private static readonly HashSet<string> SensitiveProperties = new(StringComparer.Ordinal)
    {
        nameof(User.FullName),
        nameof(User.Email),
        nameof(User.BloodGroup),
        nameof(User.EmergencyContactName),
        nameof(User.EmergencyContactPhone),
        nameof(User.FirebaseUid),
        nameof(User.ProfilePhotoBlobName),
    };

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        AddAuditEntries(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        AddAuditEntries(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void AddAuditEntries(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        context.ChangeTracker.DetectChanges();
        var now = clock.GetUtcNow().UtcDateTime;
        var logs = new List<AuditLog>();

        foreach (var entry in context.ChangeTracker.Entries<IAuditedEntity>())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified or EntityState.Deleted))
            {
                continue;
            }

            var (oldValues, newValues) = CollectValues(entry);
            if (entry.State == EntityState.Modified && newValues.Count == 0)
            {
                continue; // only ignored columns changed
            }

            logs.Add(new AuditLog(
                currentUser.UserId,
                entry.State switch
                {
                    EntityState.Added => "Created",
                    EntityState.Deleted => "Deleted",
                    _ => "Updated",
                },
                entry.Metadata.ClrType.Name,
                GetKey(entry),
                oldValues.Count > 0 ? JsonSerializer.Serialize(oldValues, JsonOptions) : null,
                newValues.Count > 0 ? JsonSerializer.Serialize(newValues, JsonOptions) : null,
                currentUser.IpAddress,
                currentUser.UserAgent,
                currentUser.CorrelationId,
                now));
        }

        if (logs.Count > 0)
        {
            context.Set<AuditLog>().AddRange(logs);
        }
    }

    private static (Dictionary<string, object?> Old, Dictionary<string, object?> New) CollectValues(EntityEntry<IAuditedEntity> entry)
    {
        var oldValues = new Dictionary<string, object?>(StringComparer.Ordinal);
        var newValues = new Dictionary<string, object?>(StringComparer.Ordinal);

        foreach (var property in entry.Properties)
        {
            var name = property.Metadata.Name;
            if (IgnoredProperties.Contains(name))
            {
                continue;
            }

            var sensitive = SensitiveProperties.Contains(name);
            switch (entry.State)
            {
                case EntityState.Added:
                    newValues[name] = Normalize(property.CurrentValue, sensitive);
                    break;
                case EntityState.Deleted:
                    oldValues[name] = Normalize(property.OriginalValue, sensitive);
                    break;
                case EntityState.Modified when property.IsModified && !Equals(property.OriginalValue, property.CurrentValue):
                    oldValues[name] = Normalize(property.OriginalValue, sensitive);
                    newValues[name] = Normalize(property.CurrentValue, sensitive);
                    break;
            }
        }

        return (oldValues, newValues);
    }

    private static string? GetKey(EntityEntry entry)
    {
        var key = entry.Metadata.FindPrimaryKey();
        return key is null
            ? null
            : string.Join(",", key.Properties.Select(p => entry.Property(p.Name).CurrentValue?.ToString()));
    }

    private static object? Normalize(object? value, bool sensitive) => value switch
    {
        null => null,
        _ when sensitive => "***",
        PhoneNumber phone => phone.Masked,
        Geometry geometry => geometry.AsText(),
        byte[] => "(binary)",
        Enum e => e.ToString(),
        _ => value,
    };
}
