using CommunitySos.Application.Abstractions;
using CommunitySos.Domain.Enums;

namespace CommunitySos.Infrastructure.Persistence;

/// <summary>Caller identity for background jobs, migrations and seeding. Replaced by the HTTP implementation in the API.</summary>
public sealed class SystemCurrentUser : ICurrentUser
{
    public Guid? UserId => null;

    public UserRole? Role => null;

    public string? IpAddress => null;

    public string? UserAgent => "system";

    public string? CorrelationId => null;
}
