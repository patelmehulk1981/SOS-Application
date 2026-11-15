using CommunitySos.Domain.Common;
using CommunitySos.Domain.Enums;

namespace CommunitySos.Domain.Entities;

/// <summary>Append-only record of who invited whom. One row per referred user (unique), never updated.</summary>
public sealed class ReferralHistory : Entity<long>
{
    public const int IpMaxLength = 45;
    public const int DeviceInfoMaxLength = 200;

    private ReferralHistory()
    {
    }

    public Guid InvitationId { get; private set; }

    public Guid ReferrerAdminId { get; private set; }

    public Guid ReferredUserId { get; private set; }

    public User? ReferredUser { get; private set; }

    public UserRole AssignedRole { get; private set; }

    public DateTime RegisteredAt { get; private set; }

    public string? IpAddress { get; private set; }

    public string? DeviceInfo { get; private set; }

    internal static ReferralHistory Create(Invitation invitation, User user, DateTime now, string? ipAddress, string? deviceInfo) => new()
    {
        InvitationId = invitation.Id,
        ReferrerAdminId = invitation.CreatedByUserId,
        ReferredUserId = user.Id,
        ReferredUser = user,
        AssignedRole = user.Role,
        RegisteredAt = now,
        IpAddress = ipAddress is null ? null : Text.Truncate(ipAddress, IpMaxLength),
        DeviceInfo = deviceInfo is null ? null : Text.Truncate(deviceInfo, DeviceInfoMaxLength),
    };
}
