namespace CommunitySos.Domain.Enums;

// All enums are persisted as tinyint. Values are part of the database contract
// (CHECK constraints and stored procedures use them) - never renumber, only append.

public enum UserRole : byte
{
    User = 1,
    SubAdmin = 2,
    MasterAdmin = 3,
}

public enum UserStatus : byte
{
    Active = 1,
    Suspended = 2,
    Deleted = 3,
}

public enum SosStatus : byte
{
    Pending = 1,
    Approved = 2,
    Rejected = 3,
    Cancelled = 4,
    Escalated = 5,
    Resolved = 6,
    Expired = 7,
}

public enum SosCategory : byte
{
    Medical = 1,
    Fire = 2,
    Accident = 3,
    Crime = 4,
    Other = 5,
}

public enum DeliveryStatus : byte
{
    Queued = 1,
    Sent = 2,
    Failed = 3,
    NoDevice = 4,
}

public enum ResponseType : byte
{
    None = 0,
    CanHelp = 1,
    OnTheWay = 2,
    CannotHelp = 3,
    Arrived = 4,
}

/// <summary>Derived (not stored) state of an invitation at a point in time.</summary>
public enum InvitationStatus : byte
{
    Active = 1,
    Revoked = 2,
    Expired = 3,
    Exhausted = 4,
}

/// <summary>Purposes that need separate, withdrawable consent (DPDP Act 2023 s.6 / GDPR Art. 6-9).</summary>
public enum ConsentType : byte
{
    /// <summary>Terms of Use + Privacy Notice, including the 18+ declaration.</summary>
    TermsAndPrivacy = 1,

    /// <summary>Storing last-known location to receive nearby SOS alerts.</summary>
    LocationSharing = 2,

    /// <summary>Blood group / emergency contact (health data - GDPR Art. 9 special category).</summary>
    HealthData = 3,
}

public enum DevicePlatform : byte
{
    Android = 1,
    Ios = 2,
}
