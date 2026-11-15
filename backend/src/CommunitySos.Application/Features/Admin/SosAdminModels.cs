using CommunitySos.Application.Common;
using CommunitySos.Domain.Enums;

namespace CommunitySos.Application.Features.Admin;

public sealed class AdminSosListQuery : PagedQuery
{
    /// <summary>true = the review queue (Pending + Escalated, oldest first). Otherwise newest first, optionally filtered by <see cref="Status"/>.</summary>
    public bool AwaitingReview { get; init; }

    public SosStatus? Status { get; init; }

    public DateTime? From { get; init; }

    public DateTime? To { get; init; }

    /// <summary>Master Admin only: SOS assigned to this Sub-Admin.</summary>
    public Guid? AssignedAdminId { get; init; }
}

public sealed record AdminSosListItem(
    Guid Id,
    string ReferenceNo,
    SosCategory Category,
    SosStatus Status,
    bool IsAutoApproved,
    UserSummary Requester,
    string? RequesterPhone,
    UserSummary AssignedAdmin,
    string? Address,
    double Latitude,
    double Longitude,
    int PhotoCount,
    int RecipientCount,
    DateTime CreatedAt,
    DateTime? EscalatedAt,
    DateTime? ReviewedAt);

public sealed record ApproveSosRequest(string? Remarks);

/// <param name="Remarks">Required: shown to the requester.</param>
public sealed record RejectSosRequest(string Remarks);

public sealed class DashboardQuery
{
    public DateTime? From { get; init; }

    public DateTime? To { get; init; }

    /// <summary>Master Admin only: one Sub-Admin's numbers. Omit for the whole community.</summary>
    public Guid? AdminId { get; init; }
}

/// <summary>User counts are scoped to the admin's referrals; SOS counts to SOS assigned to them (or everything for the Master Admin).</summary>
public sealed record DashboardStats(
    int TotalUsers,
    int SubAdmins,
    int NormalUsers,
    int ReferredUsers,
    int NewUsersLast7Days,
    int TotalSos,
    int PendingSos,
    int EscalatedSos,
    int ApprovedSos,
    int AutoApprovedSos,
    int RejectedSos,
    int ActiveSos,
    int ResolvedSos,
    int CancelledSos,
    int ExpiredSos,
    int SosLast24Hours,
    double? AvgReviewSeconds);

public sealed class SosMapQuery
{
    public double MinLatitude { get; init; }

    public double MinLongitude { get; init; }

    public double MaxLatitude { get; init; }

    public double MaxLongitude { get; init; }

    public DateTime? From { get; init; }

    public DateTime? To { get; init; }

    public SosStatus? Status { get; init; }
}

public sealed record SosMapPoint(Guid Id, string ReferenceNo, SosCategory Category, SosStatus Status, double Latitude, double Longitude, DateTime CreatedAt);

/// <param name="Truncated">More than <see cref="MaxPoints"/> SOS in view: zoom in or narrow the dates.</param>
public sealed record SosMapResponse(IReadOnlyList<SosMapPoint> Points, bool Truncated)
{
    public const int MaxPoints = 1000;
}
