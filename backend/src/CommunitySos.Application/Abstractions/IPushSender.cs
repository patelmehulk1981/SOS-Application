using CommunitySos.Domain.Entities;

namespace CommunitySos.Application.Abstractions;

/// <summary>Push delivery to app installations (Firebase Cloud Messaging in production).</summary>
public interface IPushSender
{
    /// <summary>Sends one message to many devices (batched internally). Returns one result per target.</summary>
    Task<IReadOnlyList<PushResult>> SendAsync(IReadOnlyList<PushTarget> targets, PushMessage message, CancellationToken cancellationToken);
}

/// <summary>One device to push to: by Firebase Installation ID when known, otherwise by legacy FCM token.</summary>
public sealed record PushTarget(Guid DeviceId, string? Token, string? InstallationId)
{
    public static PushTarget For(UserDevice device) => new(device.Id, device.FcmToken, device.InstallationId);

    /// <summary>The identifier sent to FCM (for logs and test doubles).</summary>
    public string Address => InstallationId ?? Token ?? string.Empty;
}

/// <param name="Type">Machine-readable kind, also sent in Data["type"] (e.g. "sos.review_requested").</param>
/// <param name="Title">Lock-screen title. Keep personal data out of it.</param>
/// <param name="Body">Lock-screen text. Keep personal data out of it.</param>
/// <param name="Data">String key/values delivered to the app (sosId, referenceNo, …).</param>
/// <param name="AndroidChannelId">Notification channel created by the app (e.g. "sos-admin").</param>
/// <param name="CollapseKey">Newer messages with the same key replace older undelivered ones (FCM collapse_key / apns-collapse-id).</param>
/// <param name="TimeToLive">Drop the message if the device is offline longer than this.</param>
public sealed record PushMessage(
    string Type,
    string Title,
    string Body,
    IReadOnlyDictionary<string, string> Data,
    string AndroidChannelId,
    string? CollapseKey = null,
    TimeSpan? TimeToLive = null);

public enum PushOutcome
{
    Sent,

    /// <summary>Token is unregistered/invalid: the device must be deactivated.</summary>
    InvalidToken,

    /// <summary>Transient or unknown failure: may be retried.</summary>
    Failed,
}

public sealed record PushResult(Guid DeviceId, PushOutcome Outcome, string? Error = null);
