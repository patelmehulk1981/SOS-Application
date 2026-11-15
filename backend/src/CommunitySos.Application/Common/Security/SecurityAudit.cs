using System.Text.Json;
using CommunitySos.Application.Abstractions;
using CommunitySos.Domain.Entities;

namespace CommunitySos.Application.Common.Security;

/// <summary>Explicit security events (login, token reuse, account deletion) written to AuditLogs.</summary>
public static class SecurityAudit
{
    public const string Login = "Login";
    public const string Registered = "Registered";
    public const string Logout = "Logout";
    public const string RefreshTokenReuse = "RefreshTokenReuse";
    public const string PolicyAccepted = "PolicyAccepted";
    public const string ConsentChanged = "ConsentChanged";
    public const string DataExported = "DataExported";
    public const string AccountDeleted = "AccountDeleted";
    public const string UserSuspended = "UserSuspended";
    public const string UserReactivated = "UserReactivated";

    public static AuditLog Event(ICurrentUser caller, string action, Guid subjectUserId, DateTime now, object? details = null) =>
        new(
            caller.UserId ?? subjectUserId,
            action,
            nameof(User),
            subjectUserId.ToString(),
            oldValues: null,
            newValues: details is null ? null : JsonSerializer.Serialize(details),
            caller.IpAddress,
            caller.UserAgent,
            caller.CorrelationId,
            now);
}
