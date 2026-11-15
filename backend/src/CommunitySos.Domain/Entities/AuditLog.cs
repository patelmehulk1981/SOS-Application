using CommunitySos.Domain.Common;

namespace CommunitySos.Domain.Entities;

/// <summary>Append-only audit trail (entity changes and security events).</summary>
public sealed class AuditLog : Entity<long>
{
    public const int ActionMaxLength = 50;
    public const int EntityNameMaxLength = 100;
    public const int EntityIdMaxLength = 100;
    public const int IpMaxLength = 45;
    public const int UserAgentMaxLength = 300;
    public const int CorrelationIdMaxLength = 64;

    private AuditLog()
    {
    }

    public AuditLog(
        Guid? userId,
        string action,
        string entityName,
        string? entityId,
        string? oldValues,
        string? newValues,
        string? ipAddress,
        string? userAgent,
        string? correlationId,
        DateTime createdAt)
    {
        UserId = userId;
        Action = Text.Truncate(action, ActionMaxLength);
        EntityName = Text.Truncate(entityName, EntityNameMaxLength);
        EntityId = entityId is null ? null : Text.Truncate(entityId, EntityIdMaxLength);
        OldValues = oldValues;
        NewValues = newValues;
        IpAddress = ipAddress is null ? null : Text.Truncate(ipAddress, IpMaxLength);
        UserAgent = userAgent is null ? null : Text.Truncate(userAgent, UserAgentMaxLength);
        CorrelationId = correlationId is null ? null : Text.Truncate(correlationId, CorrelationIdMaxLength);
        CreatedAt = createdAt;
    }

    public Guid? UserId { get; private set; }

    public string Action { get; private set; } = null!;

    public string EntityName { get; private set; } = null!;

    public string? EntityId { get; private set; }

    public string? OldValues { get; private set; }

    public string? NewValues { get; private set; }

    public string? IpAddress { get; private set; }

    public string? UserAgent { get; private set; }

    public string? CorrelationId { get; private set; }

    public DateTime CreatedAt { get; private set; }
}
