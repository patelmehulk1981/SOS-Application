using CommunitySos.Domain.Common;

namespace CommunitySos.Domain.Entities;

/// <summary>
/// A per-phone credential that can do exactly one thing: report the owner's last-known location from
/// background work (docs/06-background-location.md). It never rotates, so a background request that is cut
/// off mid-flight cannot trip refresh-token reuse detection and sign the person out. It is accepted only
/// while the same phone still holds an active session (see LocationKeyService), and is revoked when
/// background location is turned off, location sharing is withdrawn, the phone signs out or the account is deleted.
/// Only the SHA-256 hash is stored.
/// </summary>
public sealed class LocationKey : Entity<Guid>
{
    public const int HashLength = 64;
    public const int DeviceIdMaxLength = RefreshToken.DeviceIdMaxLength;
    public const int ReasonMaxLength = 50;

    private LocationKey()
    {
    }

    public Guid UserId { get; private set; }

    public string KeyHash { get; private set; } = null!;

    /// <summary>The app's install id (the same value its refresh tokens carry).</summary>
    public string DeviceId { get; private set; } = null!;

    public DateTime CreatedAt { get; private set; }

    public DateTime? LastUsedAt { get; private set; }

    public DateTime? RevokedAt { get; private set; }

    public string? RevokedReason { get; private set; }

    public bool IsActive => RevokedAt is null;

    public static LocationKey Issue(Guid userId, string keyHash, string deviceId, DateTime now)
    {
        if (keyHash.Length != HashLength)
        {
            throw new ArgumentException("Key hash must be a hex-encoded SHA-256.", nameof(keyHash));
        }

        if (string.IsNullOrWhiteSpace(deviceId) || deviceId.Length > DeviceIdMaxLength)
        {
            throw new DomainException("location_key.invalid_device", "A device id is required.");
        }

        return new LocationKey
        {
            Id = SequentialGuid.NewGuid(),
            UserId = userId,
            KeyHash = keyHash,
            DeviceId = deviceId,
            CreatedAt = now,
        };
    }

    public void MarkUsed(DateTime now) => LastUsedAt = now;

    public void Revoke(DateTime now, string reason)
    {
        if (RevokedAt is not null)
        {
            return;
        }

        RevokedAt = now;
        RevokedReason = Text.Truncate(reason, ReasonMaxLength);
    }
}
