using CommunitySos.Domain.Enums;

namespace CommunitySos.Application.Abstractions;

/// <summary>
/// The caller of the current operation. The API implements this from the HTTP context;
/// background jobs and seeding use a "system" implementation (UserId = null).
/// </summary>
public interface ICurrentUser
{
    Guid? UserId { get; }

    UserRole? Role { get; }

    string? IpAddress { get; }

    string? UserAgent { get; }

    string? CorrelationId { get; }

    bool IsAuthenticated => UserId.HasValue;
}
