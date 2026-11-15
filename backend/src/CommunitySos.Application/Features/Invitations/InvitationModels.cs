using CommunitySos.Application.Common;
using CommunitySos.Domain.Enums;

namespace CommunitySos.Application.Features.Invitations;

/// <param name="Label">Private note for the admin, e.g. "Sector 21 WhatsApp group".</param>
/// <param name="MaxUses">null = unlimited (1–10,000).</param>
/// <param name="ExpiresAt">null = never (max one year ahead).</param>
public sealed record CreateInvitationRequest(string? Label, int? MaxUses, DateTime? ExpiresAt);

public sealed record InvitationResponse(
    Guid Id,
    string Code,
    string Link,
    UserRole InviteeRole,
    string? Label,
    int? MaxUses,
    int UsedCount,
    DateTime? ExpiresAt,
    InvitationStatus Status,
    DateTime CreatedAt,
    DateTime? RevokedAt);

public sealed class InvitationListQuery : PagedQuery
{
    /// <summary>Filter by derived status; omit for all.</summary>
    public InvitationStatus? Status { get; init; }
}

/// <summary>Public preview shown on the sign-up screen. Inviter details only while the link is usable.</summary>
public sealed record InvitationPreview(
    string Code,
    InvitationStatus Status,
    UserRole InviteeRole,
    InviterSummary? InvitedBy,
    DateTime? ExpiresAt);

public sealed record InviterSummary(string? FullName, UserRole Role);
