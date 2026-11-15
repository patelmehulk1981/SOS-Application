using CommunitySos.Application.Common;
using CommunitySos.Domain.Enums;

namespace CommunitySos.Application.Features.Admin;

public sealed record UserSummary(Guid Id, string? FullName, UserRole Role, UserStatus Status);

public sealed class UserListQuery : PagedQuery
{
    public UserRole? Role { get; init; }

    /// <summary>Default excludes Deleted accounts.</summary>
    public UserStatus? Status { get; init; }

    /// <summary>Name fragment, or a phone number (exact match; 10-digit Indian mobiles accepted).</summary>
    public string? Search { get; init; }

    /// <summary>Master Admin only: users referred by this admin. Ignored for Sub-Admins (always themselves).</summary>
    public Guid? ReferredBy { get; init; }
}

/// <param name="PhoneNumber">Visible to the referring admin and the Master Admin so they can call in an emergency; null for deleted accounts.</param>
/// <param name="ReferredUsersCount">People this user has invited (Sub-Admins only).</param>
public sealed record AdminUserListItem(
    Guid Id,
    string? FullName,
    string? PhoneNumber,
    UserRole Role,
    UserStatus Status,
    UserSummary? ReferredBy,
    int ReferredUsersCount,
    bool AlertsEnabled,
    DateTime CreatedAt,
    DateTime? LastLoginAt);

/// <summary>Admin view of one user. Deliberately excludes health data and location (shown only in SOS review).</summary>
public sealed record AdminUserDetail(
    AdminUserListItem User,
    string? Email,
    string? AcceptedPolicyVersion,
    AdminReferralInfo? Referral,
    int SosRaised,
    int ActiveInvitations);

public sealed record AdminReferralInfo(Guid InvitationId, string? InvitationLabel, DateTime RegisteredAt);

/// <param name="Status">Active or Suspended.</param>
/// <param name="Reason">Required when suspending; recorded in the audit log.</param>
public sealed record UpdateUserStatusRequest(UserStatus Status, string? Reason);

public sealed class ReferralListQuery : PagedQuery
{
    /// <summary>Master Admin only: referrals made by this admin. Sub-Admins always see their own.</summary>
    public Guid? AdminId { get; init; }

    public DateTime? From { get; init; }

    public DateTime? To { get; init; }
}

public sealed record ReferralEntry(
    long Id,
    UserSummary ReferredUser,
    UserSummary Referrer,
    Guid InvitationId,
    string? InvitationLabel,
    UserRole AssignedRole,
    DateTime RegisteredAt);

/// <summary>Master Admin → Sub-Admins → Users, summarised per Sub-Admin.</summary>
public sealed record ReferralTree(UserSummary Root, int TotalSubAdmins, int TotalUsers, IReadOnlyList<SubAdminNode> SubAdmins);

public sealed record SubAdminNode(
    UserSummary SubAdmin,
    string? PhoneNumber,
    int ReferredUsers,
    int ActiveUsers,
    int ActiveInvitations,
    DateTime JoinedAt);
