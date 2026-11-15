using CommunitySos.Domain.Common;
using CommunitySos.Domain.Enums;

namespace CommunitySos.Domain.Entities;

/// <summary>
/// One recipient of one SOS alert, plus their acknowledgement/response.
/// Rows are created set-based by <c>dbo.usp_SelectSosRecipients</c>; the unique key
/// (SosRequestId, RecipientUserId) guarantees a user is alerted at most once per SOS.
/// </summary>
public sealed class SosNotification : Entity<long>
{
    public const int ErrorMaxLength = 500;
    public const int NoteMaxLength = 300;
    public const int MaxDeliveryAttempts = 5;

    private SosNotification()
    {
    }

    public Guid SosRequestId { get; private set; }

    public Guid RecipientUserId { get; private set; }

    public double DistanceMeters { get; private set; }

    public DeliveryStatus DeliveryStatus { get; private set; }

    public int AttemptCount { get; private set; }

    public string? LastError { get; private set; }

    public DateTime? SentAt { get; private set; }

    public DateTime? AcknowledgedAt { get; private set; }

    public ResponseType ResponseType { get; private set; }

    public string? ResponseNote { get; private set; }

    public DateTime? RespondedAt { get; private set; }

    public DateTime CreatedAt { get; private set; }

    /// <summary>
    /// Set when a dispatch run hands this recipient out for pushing; another run skips it until the claim
    /// is released (by recording the outcome) or expires (the worker died mid-send). Stops overlapping
    /// runs (a retry, two approvals) from pushing the same person twice.
    /// </summary>
    public DateTime? ClaimedUntil { get; private set; }

    public bool IsClaimed(DateTime now) => ClaimedUntil is { } until && until > now;

    /// <summary>Hands the recipient to one dispatch run until <paramref name="until"/>.</summary>
    public void Claim(DateTime until) => ClaimedUntil = until;

    /// <summary>
    /// In production, recipients are inserted set-based by dbo.usp_SelectSosRecipients. This factory exists for
    /// non-SQL providers (tests) and tooling.
    /// </summary>
    public static SosNotification Create(Guid sosRequestId, Guid recipientUserId, double distanceMeters, DateTime now)
    {
        if (distanceMeters < 0 || double.IsNaN(distanceMeters))
        {
            throw new DomainException("alert.invalid_distance", "Distance must be zero or positive.");
        }

        return new SosNotification
        {
            SosRequestId = sosRequestId,
            RecipientUserId = recipientUserId,
            DistanceMeters = distanceMeters,
            DeliveryStatus = DeliveryStatus.Queued,
            ResponseType = ResponseType.None,
            CreatedAt = now,
        };
    }

    public void MarkSent(DateTime now)
    {
        ClaimedUntil = null;
        DeliveryStatus = DeliveryStatus.Sent;
        AttemptCount++;
        SentAt ??= now;
        LastError = null;
    }

    public void MarkFailed(string error)
    {
        ClaimedUntil = null;
        DeliveryStatus = DeliveryStatus.Failed;
        AttemptCount++;
        LastError = Text.Truncate(error, ErrorMaxLength);
    }

    /// <summary>Every device of the recipient turned out to be unregistered: nothing left to push to.</summary>
    public void MarkNoDevice(string error)
    {
        ClaimedUntil = null;
        DeliveryStatus = DeliveryStatus.NoDevice;
        AttemptCount++;
        LastError = Text.Truncate(error, ErrorMaxLength);
    }

    /// <summary>The recipient is offering (or has given) help: worth telling the requester.</summary>
    public bool IsHelping => ResponseType is ResponseType.CanHelp or ResponseType.OnTheWay or ResponseType.Arrived;

    /// <summary>Idempotent "I have seen this alert".</summary>
    public void Acknowledge(DateTime now) => AcknowledgedAt ??= now;

    /// <summary>Records or changes the recipient's response. Returns true when the requester should be told
    /// (the recipient newly offers help, or reports arrival).</summary>
    public bool Respond(ResponseType response, string? note, DateTime now)
    {
        if (response == ResponseType.None || !Enum.IsDefined(response))
        {
            throw new DomainException("alert.invalid_response", "Response type is not valid.");
        }

        var wasHelping = IsHelping;
        var previous = ResponseType;

        Acknowledge(now);
        ResponseType = response;
        ResponseNote = Text.Optional(note, NoteMaxLength, "Note");
        RespondedAt = now;

        return (IsHelping && !wasHelping) || (response == ResponseType.Arrived && previous != ResponseType.Arrived);
    }

    /// <summary>Account erasure: the free-text note may identify the person; the response itself stays for the record.</summary>
    public void ClearNote() => ResponseNote = null;
}
