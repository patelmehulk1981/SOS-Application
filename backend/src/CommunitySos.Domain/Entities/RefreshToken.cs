using CommunitySos.Domain.Common;

namespace CommunitySos.Domain.Entities;

/// <summary>
/// Rotating refresh token. Only the SHA-256 hash is stored. All tokens issued from one login share a
/// <see cref="FamilyId"/>; if a revoked token is replayed the whole family is revoked (theft detection).
/// </summary>
public sealed class RefreshToken : Entity<Guid>
{
    public const int HashLength = 64;
    public const int DeviceIdMaxLength = 100;
    public const int ReasonMaxLength = 50;

    private RefreshToken()
    {
    }

    public Guid UserId { get; private set; }

    public string TokenHash { get; private set; } = null!;

    public Guid FamilyId { get; private set; }

    public string? DeviceId { get; private set; }

    public DateTime CreatedAt { get; private set; }

    public DateTime ExpiresAt { get; private set; }

    public DateTime? RevokedAt { get; private set; }

    public string? RevokedReason { get; private set; }

    public Guid? ReplacedByTokenId { get; private set; }

    public static RefreshToken Issue(Guid userId, string tokenHash, Guid familyId, string? deviceId, DateTime now, TimeSpan lifetime)
    {
        if (tokenHash.Length != HashLength)
        {
            throw new ArgumentException("Token hash must be a hex-encoded SHA-256.", nameof(tokenHash));
        }

        return new RefreshToken
        {
            Id = SequentialGuid.NewGuid(),
            UserId = userId,
            TokenHash = tokenHash,
            FamilyId = familyId,
            DeviceId = deviceId is null ? null : Text.Truncate(deviceId, DeviceIdMaxLength),
            CreatedAt = now,
            ExpiresAt = now + lifetime,
        };
    }

    public bool IsActive(DateTime now) => RevokedAt is null && ExpiresAt > now;

    public void Revoke(DateTime now, string reason, Guid? replacedByTokenId = null)
    {
        if (RevokedAt is not null)
        {
            return;
        }

        RevokedAt = now;
        RevokedReason = Text.Truncate(reason, ReasonMaxLength);
        ReplacedByTokenId = replacedByTokenId;
    }
}
