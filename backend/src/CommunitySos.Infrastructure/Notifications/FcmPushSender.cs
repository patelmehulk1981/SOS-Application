using CommunitySos.Application.Abstractions;
using CommunitySos.Infrastructure.Identity;
using FirebaseAdmin.Messaging;
using Microsoft.Extensions.Logging;

namespace CommunitySos.Infrastructure.Notifications;

/// <summary>
/// Firebase Cloud Messaging (HTTP v1 via the Admin SDK). High priority on both platforms so alerts
/// wake the device; collapse keys stop duplicate banners when a message is retried.
/// </summary>
public sealed class FcmPushSender(FirebaseAppProvider firebase, ILogger<FcmPushSender> logger) : IPushSender
{
    /// <summary>FCM multicast limit.</summary>
    public const int BatchSize = 500;

    public async Task<IReadOnlyList<PushResult>> SendAsync(IReadOnlyList<PushTarget> targets, PushMessage message, CancellationToken cancellationToken)
    {
        var messaging = FirebaseMessaging.GetMessaging(firebase.App);
        var results = new List<PushResult>(targets.Count);

        foreach (var batch in targets.Chunk(BatchSize))
        {
            // FCM answers tokens first, then FIDs, each in request order.
            var ordered = batch.Where(t => t.InstallationId is null).Concat(batch.Where(t => t.InstallationId is not null)).ToArray();

            BatchResponse response;
            try
            {
                response = await messaging.SendEachForMulticastAsync(ToMulticast(ordered, message), cancellationToken);
            }
            catch (FirebaseMessagingException ex)
            {
                logger.LogError(ex, "FCM batch of {Count} failed: {Code}", batch.Length, ex.MessagingErrorCode);
                results.AddRange(batch.Select(t => new PushResult(t.DeviceId, PushOutcome.Failed, ex.Message)));
                continue;
            }

            for (var i = 0; i < ordered.Length; i++)
            {
                var r = response.Responses[i];
                results.Add(r.IsSuccess
                    ? new PushResult(ordered[i].DeviceId, PushOutcome.Sent)
                    : new PushResult(ordered[i].DeviceId, Classify(r.Exception), r.Exception?.Message));
            }
        }

        return results;
    }

    /// <summary>"Unregistered" covers both a dead registration token and an FID that is no longer registered.</summary>
    private static PushOutcome Classify(FirebaseMessagingException? ex) => ex?.MessagingErrorCode switch
    {
        MessagingErrorCode.Unregistered or MessagingErrorCode.SenderIdMismatch or MessagingErrorCode.InvalidArgument => PushOutcome.InvalidToken,
        _ => PushOutcome.Failed,
    };

    private static MulticastMessage ToMulticast(PushTarget[] ordered, PushMessage m) => new()
    {
        // Firebase deprecated registration tokens in favour of Firebase Installation IDs in 2026. Current app
        // versions register FIDs; legacy token rows (older builds) are still delivered until those builds are gone.
#pragma warning disable CS0618
        Tokens = ordered.Where(t => t.InstallationId is null).Select(t => t.Token!).ToList() is { Count: > 0 } tokens ? tokens : null,
#pragma warning restore CS0618
        Fids = ordered.Where(t => t.InstallationId is not null).Select(t => t.InstallationId!).ToList() is { Count: > 0 } fids ? fids : null,
        Notification = new Notification { Title = m.Title, Body = m.Body },
        Data = m.Data,
        Android = new AndroidConfig
        {
            Priority = Priority.High,
            CollapseKey = m.CollapseKey,
            TimeToLive = m.TimeToLive,
            Notification = new AndroidNotification
            {
                ChannelId = m.AndroidChannelId,
                Sound = "default",
                Tag = m.CollapseKey, // replaces an existing banner with the same tag
                Priority = NotificationPriority.MAX,
                Visibility = NotificationVisibility.PRIVATE, // hide content on a locked screen unless the user allows it
            },
        },
        Apns = new ApnsConfig
        {
            Headers = BuildApnsHeaders(m),
            Aps = new Aps
            {
                Sound = "default",
                ContentAvailable = true,
                ThreadId = m.Data.TryGetValue("sosId", out var sosId) ? sosId : null,
            },
        },
    };

    private static Dictionary<string, string> BuildApnsHeaders(PushMessage m)
    {
        var headers = new Dictionary<string, string> { ["apns-priority"] = "10", ["apns-push-type"] = "alert" };
        if (m.CollapseKey is { } key)
        {
            headers["apns-collapse-id"] = key.Length <= 64 ? key : key[..64]; // APNs limit: 64 bytes
        }

        if (m.TimeToLive is { } ttl)
        {
            headers["apns-expiration"] = DateTimeOffset.UtcNow.Add(ttl).ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        return headers;
    }
}

/// <summary>Development stand-in: logs instead of sending.</summary>
public sealed class LoggingPushSender(ILogger<LoggingPushSender> logger) : IPushSender
{
    public Task<IReadOnlyList<PushResult>> SendAsync(IReadOnlyList<PushTarget> targets, PushMessage message, CancellationToken cancellationToken)
    {
        logger.LogWarning("DEVELOPMENT push {Type} to {Count} device(s): {Title}", message.Type, targets.Count, message.Title);
        return Task.FromResult<IReadOnlyList<PushResult>>(targets.Select(t => new PushResult(t.DeviceId, PushOutcome.Sent)).ToList());
    }
}
