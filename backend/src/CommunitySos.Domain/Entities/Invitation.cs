using CommunitySos.Domain.Common;
using CommunitySos.Domain.Enums;
using CommunitySos.Domain.ValueObjects;

namespace CommunitySos.Domain.Entities;

/// <summary>
/// A shareable invite link created by the Master Admin (invites Sub-Admins) or a Sub-Admin (invites Users).
/// Concurrent redemptions are serialised by <see cref="RowVersion"/>: the loser gets a concurrency
/// conflict and the application retries, so <see cref="MaxUses"/> can never be exceeded.
/// </summary>
public sealed class Invitation : Entity<Guid>, IHasTimestamps, IAuditedEntity
{
    public const int LabelMaxLength = 100;
    public const int MaxUsesLimit = 10_000;
    public static readonly TimeSpan MaxLifetime = TimeSpan.FromDays(365);

    private Invitation()
    {
    }

    public string Code { get; private set; } = null!;

    public Guid CreatedByUserId { get; private set; }

    public User? CreatedBy { get; private set; }

    /// <summary>Role that people joining through this link receive. Derived from the creator's role.</summary>
    public UserRole InviteeRole { get; private set; }

    public string? Label { get; private set; }

    /// <summary>null = unlimited.</summary>
    public int? MaxUses { get; private set; }

    public int UsedCount { get; private set; }

    /// <summary>null = never expires.</summary>
    public DateTime? ExpiresAt { get; private set; }

    public bool IsRevoked { get; private set; }

    public DateTime? RevokedAt { get; private set; }

    public DateTime CreatedAt { get; private set; }

    public DateTime UpdatedAt { get; private set; }

    public byte[] RowVersion { get; private set; } = [];

    public static Invitation Create(User creator, string? label, int? maxUses, DateTime? expiresAt, DateTime now)
    {
        ArgumentNullException.ThrowIfNull(creator);

        if (!creator.CanInvite || creator.InviteeRole is not { } inviteeRole)
        {
            throw new DomainException("invitation.forbidden", "Only active admins can create invitations.");
        }

        if (maxUses is < 1 or > MaxUsesLimit)
        {
            throw new DomainException("invitation.invalid_max_uses", $"Max uses must be between 1 and {MaxUsesLimit}.");
        }

        if (expiresAt is { } expiry && (expiry <= now || expiry > now + MaxLifetime))
        {
            throw new DomainException("invitation.invalid_expiry", "Expiry must be in the future and within one year.");
        }

        return new Invitation
        {
            Id = SequentialGuid.NewGuid(),
            Code = InvitationCode.Generate(),
            CreatedByUserId = creator.Id,
            InviteeRole = inviteeRole,
            Label = Text.Optional(label, LabelMaxLength, nameof(Label)),
            MaxUses = maxUses,
            ExpiresAt = expiresAt,
        };
    }

    public bool IsUsable(DateTime now) => GetStatus(now) == InvitationStatus.Active;

    /// <summary>Revocation wins over expiry, which wins over exhaustion.</summary>
    public InvitationStatus GetStatus(DateTime now) =>
        IsRevoked ? InvitationStatus.Revoked
        : ExpiresAt is not null && ExpiresAt <= now ? InvitationStatus.Expired
        : MaxUses is not null && UsedCount >= MaxUses ? InvitationStatus.Exhausted
        : InvitationStatus.Active;

    /// <summary>
    /// Registers a new user through this invitation and produces the permanent referral record.
    /// The caller must have verified that the creator is still active.
    /// </summary>
    public InvitationRedemption Redeem(
        PhoneNumber phoneNumber,
        string firebaseUid,
        RegistrationConsent consent,
        DateTime now,
        string? ipAddress,
        string? deviceInfo)
    {
        ArgumentNullException.ThrowIfNull(consent);

        if (!IsUsable(now))
        {
            throw new DomainException("invitation.unusable", "This invitation link is no longer valid.");
        }

        var user = User.CreateFromInvitation(phoneNumber, firebaseUid, this, consent, now);
        var referral = ReferralHistory.Create(this, user, now, ipAddress, deviceInfo);
        var termsConsent = UserConsent.Record(
            user.Id, ConsentType.TermsAndPrivacy, consent.PolicyVersion, true, now, ipAddress, consent.Source);
        UsedCount++;

        return new InvitationRedemption(user, referral, termsConsent);
    }

    public void Revoke(DateTime now)
    {
        if (IsRevoked)
        {
            return;
        }

        IsRevoked = true;
        RevokedAt = now;
    }
}

/// <summary>Everything a successful registration produces; the caller persists all three in one transaction.</summary>
public sealed record InvitationRedemption(User User, ReferralHistory Referral, UserConsent TermsConsent);
