using CommunitySos.Domain.Common;
using CommunitySos.Domain.Enums;
using CommunitySos.Domain.ValueObjects;

namespace CommunitySos.Domain.Entities;

public sealed class User : Entity<Guid>, IHasTimestamps, IAuditedEntity
{
    public const int FullNameMaxLength = 100;
    public const int EmailMaxLength = 256;
    public const int FirebaseUidMaxLength = 128;
    public const int BlobNameMaxLength = 300;
    public const int BloodGroupMaxLength = 5;

    private static readonly string[] BloodGroups = ["A+", "A-", "B+", "B-", "AB+", "AB-", "O+", "O-"];

    private User()
    {
    }

    public PhoneNumber PhoneNumber { get; private set; } = null!;

    /// <summary>Firebase Authentication uid; set on first successful phone verification.</summary>
    public string? FirebaseUid { get; private set; }

    public DateTime? PhoneVerifiedAt { get; private set; }

    public string? FullName { get; private set; }

    public string? Email { get; private set; }

    public string? ProfilePhotoBlobName { get; private set; }

    public string? BloodGroup { get; private set; }

    public string? EmergencyContactName { get; private set; }

    public PhoneNumber? EmergencyContactPhone { get; private set; }

    public UserRole Role { get; private set; }

    public UserStatus Status { get; private set; }

    /// <summary>The admin whose invitation this user accepted. Set once; immutable (also enforced by a DB trigger).</summary>
    public Guid? ReferredByAdminId { get; private set; }

    public User? ReferredByAdmin { get; private set; }

    /// <summary>User wants to receive nearby SOS alerts.</summary>
    public bool AlertsEnabled { get; private set; }

    /// <summary>Explicit consent to store the device's last-known location.</summary>
    public bool LocationSharingEnabled { get; private set; }

    /// <summary>Latest Terms/Privacy Notice version accepted (denormalised from <see cref="UserConsent"/>).</summary>
    public string? AcceptedPolicyVersion { get; private set; }

    public DateTime? PolicyAcceptedAt { get; private set; }

    /// <summary>When the user declared they are 18 or older.</summary>
    public DateTime? AdultConfirmedAt { get; private set; }

    public DateTime? LastLoginAt { get; private set; }

    public DateTime CreatedAt { get; private set; }

    public DateTime UpdatedAt { get; private set; }

    public byte[] RowVersion { get; private set; } = [];

    public bool IsActive => Status == UserStatus.Active;

    public bool IsAdmin => Role is UserRole.SubAdmin or UserRole.MasterAdmin;

    public bool CanInvite => IsActive && IsAdmin;

    /// <summary>Only plain Users need an admin to approve their SOS; admins' SOS are auto-approved.</summary>
    public bool SosRequiresApproval => Role == UserRole.User;

    /// <summary>Role granted to people who join through this user's invite links.</summary>
    public UserRole? InviteeRole => Role switch
    {
        UserRole.MasterAdmin => UserRole.SubAdmin,
        UserRole.SubAdmin => UserRole.User,
        _ => null,
    };

    public static User CreateMasterAdmin(PhoneNumber phoneNumber, string fullName) => new()
    {
        Id = SequentialGuid.NewGuid(),
        PhoneNumber = phoneNumber,
        FullName = Text.Required(fullName, FullNameMaxLength, nameof(FullName)),
        Role = UserRole.MasterAdmin,
        Status = UserStatus.Active,
        ReferredByAdminId = null,
        AlertsEnabled = true,
        LocationSharingEnabled = false,
    };

    /// <summary>Called only by <see cref="Invitation.Redeem"/> so a referral record is always produced.</summary>
    internal static User CreateFromInvitation(PhoneNumber phoneNumber, string firebaseUid, Invitation invitation, RegistrationConsent consent, DateTime now)
    {
        EnsureAdult(consent.ConfirmedAdult);

        return new User
        {
            Id = SequentialGuid.NewGuid(),
            PhoneNumber = phoneNumber,
            FirebaseUid = Text.Required(firebaseUid, FirebaseUidMaxLength, nameof(FirebaseUid)),
            PhoneVerifiedAt = now,
            Role = invitation.InviteeRole,
            Status = UserStatus.Active,
            ReferredByAdminId = invitation.CreatedByUserId,
            AlertsEnabled = true,
            LocationSharingEnabled = false,
            AcceptedPolicyVersion = Text.Required(consent.PolicyVersion, UserConsent.PolicyVersionMaxLength, "PolicyVersion"),
            PolicyAcceptedAt = now,
            AdultConfirmedAt = now,
            LastLoginAt = now,
        };
    }

    public bool HasAcceptedPolicy(string currentVersion) =>
        AdultConfirmedAt is not null
        && string.Equals(AcceptedPolicyVersion, currentVersion, StringComparison.Ordinal);

    /// <summary>
    /// Accepts the current Terms/Privacy Notice (first login of the seeded Master Admin, or after a policy update).
    /// The returned consent record must be persisted by the caller.
    /// </summary>
    public UserConsent AcceptPolicy(string policyVersion, bool confirmedAdult, DateTime now, string? ipAddress, string? source)
    {
        EnsureActive();
        if (AdultConfirmedAt is null)
        {
            EnsureAdult(confirmedAdult);
            AdultConfirmedAt = now;
        }

        var consent = UserConsent.Record(Id, ConsentType.TermsAndPrivacy, policyVersion, true, now, ipAddress, source);
        AcceptedPolicyVersion = consent.PolicyVersion;
        PolicyAcceptedAt = now;
        return consent;
    }

    /// <summary>Records a successful login. Links the Firebase identity the first time (e.g. the seeded Master Admin).</summary>
    public void RecordLogin(string firebaseUid, DateTime now)
    {
        EnsureActive();

        if (FirebaseUid is null)
        {
            FirebaseUid = Text.Required(firebaseUid, FirebaseUidMaxLength, nameof(FirebaseUid));
        }
        else if (!string.Equals(FirebaseUid, firebaseUid, StringComparison.Ordinal))
        {
            throw new DomainException("auth.identity_mismatch", "This phone number is linked to a different identity.");
        }

        PhoneVerifiedAt ??= now;
        LastLoginAt = now;
    }

    public void UpdateProfile(
        string fullName,
        string? email,
        string? bloodGroup,
        string? emergencyContactName,
        PhoneNumber? emergencyContactPhone)
    {
        EnsureActive();

        FullName = Text.Required(fullName, FullNameMaxLength, nameof(FullName));
        Email = Text.Optional(email, EmailMaxLength, nameof(Email))?.ToLowerInvariant();

        var group = Text.Optional(bloodGroup, BloodGroupMaxLength, nameof(BloodGroup))?.ToUpperInvariant();
        if (group is not null && !BloodGroups.Contains(group))
        {
            throw new DomainException("profile.invalid_blood_group", "Blood group is not valid.");
        }

        BloodGroup = group;
        EmergencyContactName = Text.Optional(emergencyContactName, FullNameMaxLength, nameof(EmergencyContactName));

        if (emergencyContactPhone is not null && emergencyContactPhone == PhoneNumber)
        {
            throw new DomainException("profile.emergency_contact_self", "Emergency contact must be a different number.");
        }

        EmergencyContactPhone = emergencyContactPhone;
    }

    public bool HasHealthData => BloodGroup is not null || EmergencyContactName is not null || EmergencyContactPhone is not null;

    /// <summary>Erases health-related fields; called when the user withdraws HealthData consent.</summary>
    public void ClearHealthData()
    {
        BloodGroup = null;
        EmergencyContactName = null;
        EmergencyContactPhone = null;
    }

    public void SetProfilePhoto(string? blobName)
    {
        EnsureActive();
        ProfilePhotoBlobName = Text.Optional(blobName, BlobNameMaxLength, nameof(ProfilePhotoBlobName));
    }

    public void SetAlertsEnabled(bool enabled)
    {
        EnsureActive();
        AlertsEnabled = enabled;
    }

    /// <summary>
    /// Grants or withdraws location consent. Withdrawing must be followed by deleting the user's
    /// <see cref="UserLocation"/> row in the same transaction (done by the application service).
    /// The returned consent record must be persisted by the caller.
    /// </summary>
    public UserConsent SetLocationSharing(bool enabled, string policyVersion, DateTime now, string? ipAddress, string? source)
    {
        EnsureActive();
        LocationSharingEnabled = enabled;
        return UserConsent.Record(Id, ConsentType.LocationSharing, policyVersion, enabled, now, ipAddress, source);
    }

    public void Suspend()
    {
        if (Role == UserRole.MasterAdmin)
        {
            throw new DomainException("user.cannot_suspend_master", "The Master Admin cannot be suspended.");
        }

        if (Status == UserStatus.Deleted)
        {
            throw new DomainException("user.deleted", "Account has been deleted.");
        }

        Status = UserStatus.Suspended;
    }

    public void Reactivate()
    {
        if (Status == UserStatus.Deleted)
        {
            throw new DomainException("user.deleted", "Account has been deleted.");
        }

        Status = UserStatus.Active;
    }

    /// <summary>
    /// Right-to-erasure: removes personal data but keeps the row so referral history,
    /// SOS history and audit references stay intact.
    /// </summary>
    public void DeleteAndAnonymise()
    {
        if (Role == UserRole.MasterAdmin)
        {
            throw new DomainException("user.cannot_delete_master", "The Master Admin account cannot be deleted.");
        }

        Status = UserStatus.Deleted;
        PhoneNumber = PhoneNumber.Anonymised(Id);
        FirebaseUid = null;
        FullName = null;
        Email = null;
        ProfilePhotoBlobName = null;
        BloodGroup = null;
        EmergencyContactName = null;
        EmergencyContactPhone = null;
        AlertsEnabled = false;
        LocationSharingEnabled = false;
    }

    private static void EnsureAdult(bool confirmedAdult)
    {
        if (!confirmedAdult)
        {
            throw new DomainException("registration.adult_required", "You must be 18 or older to use this app.");
        }
    }

    private void EnsureActive()
    {
        if (Status != UserStatus.Active)
        {
            throw new DomainException("user.inactive", "Account is not active.");
        }
    }
}
