using CommunitySos.Application.Abstractions;
using CommunitySos.Domain.Entities;
using CommunitySos.Domain.Enums;

namespace CommunitySos.Application.Common.Notifications;

/// <summary>
/// Every push the system sends, in one place. Lock-screen text never contains names, phone numbers,
/// descriptions or addresses; the app fetches details after authenticating.
/// </summary>
public static class PushMessages
{
    public const string AdminChannel = "sos-admin";
    public const string StatusChannel = "sos-status";
    public const string AlertChannel = "sos-alert";

    public const string ReviewRequested = "sos.review_requested";
    public const string Escalated = "sos.escalated";
    public const string AutoApproved = "sos.auto_approved";
    public const string Approved = "sos.approved";
    public const string Rejected = "sos.rejected";
    public const string Alert = "sos.alert";
    public const string ResponderUpdate = "sos.responder_update";
    public const string Closed = "sos.closed";

    /// <summary>
    /// Lock-screen distance, in 500 m steps. Recipients are grouped by this text so one multicast serves
    /// everyone at a similar distance (at most ~10 groups within 5 km).
    /// </summary>
    public static string DescribeDistance(double meters)
    {
        if (meters < 500)
        {
            return "Less than 500 m from you.";
        }

        var km = Math.Round(meters / 500, MidpointRounding.AwayFromZero) / 2;
        return string.Create(System.Globalization.CultureInfo.InvariantCulture, $"About {km:0.#} km from you.");
    }

    /// <summary>To neighbours within the radius of an approved SOS. Collapse key shared with <see cref="ClosedFor"/>,
    /// so a later "no longer needed" replaces the alert banner.</summary>
    public static PushMessage AlertFor(SosRequest sos, string distanceText, TimeSpan timeToLive) => Build(
        Alert, sos, AlertChannel,
        title: $"{sos.Category} SOS near you",
        body: $"{distanceText} Open the app to see if you can help.",
        collapseKey: AlertCollapseKey(sos),
        timeToLive: timeToLive);

    /// <summary>To the requester when a neighbour offers help or arrives (collapsed: the latest count wins).</summary>
    public static PushMessage ResponderUpdateFor(SosRequest sos, int helping) => Build(
        ResponderUpdate, sos, StatusChannel,
        title: helping == 1 ? "Someone nearby is responding" : $"{helping} people nearby are responding",
        body: "Open the app to see who is on the way.");

    /// <summary>To alerted neighbours once the SOS is resolved, cancelled or expired.</summary>
    public static PushMessage ClosedFor(SosRequest sos) => Build(
        Closed, sos, StatusChannel,
        title: $"{sos.Category} SOS no longer needs help",
        body: sos.Status == SosStatus.Resolved ? "It has been resolved. Thank you for being there." : "It has been closed. Thank you.",
        collapseKey: AlertCollapseKey(sos));

    private static string AlertCollapseKey(SosRequest sos) => $"{Alert}:{sos.Id:N}";

    public static PushMessage ReviewRequestedFor(SosRequest sos) => Build(
        ReviewRequested, sos, AdminChannel,
        title: $"New {sos.Category} SOS",
        body: "An SOS from your community is waiting for your review.");

    public static PushMessage EscalatedFor(SosRequest sos) => Build(
        Escalated, sos, AdminChannel,
        title: $"Escalated {sos.Category} SOS",
        body: "Not reviewed in time by the assigned admin. Please review now.");

    public static PushMessage AutoApprovedFor(SosRequest sos) => Build(
        AutoApproved, sos, AdminChannel,
        title: $"{sos.Category} SOS raised by an admin",
        body: "Approved automatically. Nearby members are being alerted.");

    public static PushMessage ApprovedFor(SosRequest sos) => Build(
        Approved, sos, StatusChannel,
        title: "Your SOS was approved",
        body: "Nearby community members are being alerted.");

    public static PushMessage RejectedFor(SosRequest sos) => Build(
        Rejected, sos, StatusChannel,
        title: "Your SOS was not approved",
        body: "Open the app to see the admin's remarks. In danger? Call 112.");

    private static PushMessage Build(
        string type, SosRequest sos, string channel, string title, string body, string? collapseKey = null, TimeSpan? timeToLive = null) => new(
        type,
        title,
        body,
        new Dictionary<string, string>
        {
            ["type"] = type,
            ["sosId"] = sos.Id.ToString(),
            ["referenceNo"] = sos.ReferenceNo,
        },
        channel,
        CollapseKey: collapseKey ?? $"{type}:{sos.Id:N}",
        TimeToLive: timeToLive ?? TimeSpan.FromHours(1));
}
