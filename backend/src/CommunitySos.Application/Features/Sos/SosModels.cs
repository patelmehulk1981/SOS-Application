using CommunitySos.Application.Common;
using CommunitySos.Domain.Enums;

namespace CommunitySos.Application.Features.Sos;

/// <param name="ClientRequestId">Generated once by the app per SOS press and reused on every retry (idempotency key).</param>
/// <param name="CapturedAt">When the device took the GPS fix (UTC).</param>
public sealed record CreateSosRequest(
    Guid ClientRequestId,
    SosCategory Category,
    string? Description,
    double Latitude,
    double Longitude,
    double? AccuracyMeters,
    DateTime CapturedAt);

public sealed record CancelSosRequest(string? Reason);

public sealed record ResolveSosRequest(string? Remarks);

public sealed class SosListQuery : PagedQuery
{
    public SosStatus? Status { get; init; }
}

/// <summary>An uploaded file handed over by the API layer (keeps ASP.NET types out of the use case).</summary>
public sealed record PhotoUpload(string? FileName, long Length, Func<Stream> OpenReadStream);

/// <summary>How the caller relates to the SOS; decides which fields are returned.</summary>
public enum SosViewerRole
{
    Requester,
    Admin,
    Recipient,
}

/// <summary>
/// SOS details shaped for the viewer:
/// requester - everything about their own SOS; admin - plus requester contact and health info;
/// recipient - only what is needed to help (first name, location, description, photos).
/// </summary>
public sealed record SosDetailResponse(
    Guid Id,
    string ReferenceNo,
    SosCategory Category,
    string? Description,
    SosLocation Location,
    string? Address,
    SosStatus Status,
    bool IsAutoApproved,
    DateTime CreatedAt,
    DateTime? ReviewedAt,
    string? ReviewRemarks,
    DateTime? ClosedAt,
    int? RecipientCount,
    SosViewerRole ViewerRole,
    SosRequesterInfo Requester,
    IReadOnlyList<SosPhoto> Photos,
    IReadOnlyList<SosTimelineEntry>? Timeline,
    SosMyAlert? MyAlert = null,
    SosResponders? Responders = null);

/// <summary>Recipient view only: the caller's own alert and response.</summary>
public sealed record SosMyAlert(
    double DistanceMeters,
    DateTime AlertedAt,
    DateTime? AcknowledgedAt,
    ResponseType Response,
    string? Note,
    DateTime? RespondedAt);

/// <summary>
/// Requester and admin views: how the neighbourhood is responding. <see cref="Helping"/> lists only people
/// offering help, by first name (mirroring what they see of the requester).
/// </summary>
public sealed record SosResponders(
    int Alerted,
    int Acknowledged,
    int CanHelp,
    int OnTheWay,
    int Arrived,
    int CannotHelp,
    IReadOnlyList<SosResponder> Helping);

public sealed record SosResponder(string? FirstName, ResponseType Response, double DistanceMeters, DateTime RespondedAt, string? Note);

/// <summary>"Nearby alerts" feed. <see cref="OpenOnly"/> = only SOS that still need help (Approved).</summary>
public sealed class AlertListQuery : PagedQuery
{
    public bool OpenOnly { get; init; }
}

/// <param name="Address">Only while the SOS is open: closed emergencies are not browsable by neighbours.</param>
public sealed record SosAlertItem(
    Guid SosId,
    string ReferenceNo,
    SosCategory Category,
    SosStatus Status,
    double DistanceMeters,
    string? Address,
    DateTime AlertedAt,
    DateTime? AcknowledgedAt,
    ResponseType Response,
    DateTime? RespondedAt);

/// <param name="Note">Optional, ≤ 300 characters, shown to the requester and the reviewing admin (e.g. "Nurse, 5 min away").</param>
public sealed record RespondToSosRequest(ResponseType Response, string? Note);

/// <param name="MapsUrl">Opens Google Maps (app or web) at the exact point.</param>
public sealed record SosLocation(double Latitude, double Longitude, double? AccuracyMeters, string MapsUrl);

/// <summary>Recipients receive only <see cref="FirstName"/>; the other fields are for the reviewing admin.</summary>
public sealed record SosRequesterInfo(
    string? FirstName,
    Guid? Id = null,
    string? FullName = null,
    string? PhoneNumber = null,
    string? BloodGroup = null,
    string? EmergencyContactName = null,
    string? EmergencyContactPhone = null);

/// <param name="Url">Read-only link valid for a few minutes; refetch the SOS to get a fresh one.</param>
public sealed record SosPhoto(Guid Id, string Url, int Width, int Height, DateTime UploadedAt);

public sealed record SosTimelineEntry(SosStatus? From, SosStatus To, string? Remarks, DateTime ChangedAt, bool BySystem);

public sealed record SosSummary(
    Guid Id,
    string ReferenceNo,
    SosCategory Category,
    SosStatus Status,
    string? Address,
    bool IsAutoApproved,
    int PhotoCount,
    DateTime CreatedAt,
    DateTime? ClosedAt);

/// <summary>Result of create: <see cref="Created"/> is false when an earlier submit with the same ClientRequestId is replayed.</summary>
public sealed record CreateSosResult(SosDetailResponse Sos, bool Created);

/// <param name="CapturedAt">When the device took the fix (UTC).</param>
public sealed record UpdateLocationRequest(double Latitude, double Longitude, double? AccuracyMeters, DateTime CapturedAt);
