namespace CommunitySos.Domain.Common;

/// <summary>Base type for all entities. Identity is assigned by the domain (Guid) or by SQL Server (long identity).</summary>
public abstract class Entity<TId>
    where TId : struct
{
    public TId Id { get; protected set; }
}

/// <summary>Entity whose CreatedAt/UpdatedAt are maintained automatically by the persistence layer.</summary>
public interface IHasTimestamps
{
    DateTime CreatedAt { get; }
    DateTime UpdatedAt { get; }
}

/// <summary>Marker: every insert/update/delete of this entity is written to the AuditLogs table.</summary>
public interface IAuditedEntity;
