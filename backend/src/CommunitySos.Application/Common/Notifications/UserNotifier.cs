using CommunitySos.Application.Abstractions;
using CommunitySos.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CommunitySos.Application.Common.Notifications;

/// <summary>
/// Sends a push to every active device of the given users and deactivates tokens FCM reports as invalid.
/// Used for admin/requester/responder notifications; the 5 km neighbour fan-out (DispatchSosAlertsJob) tracks
/// per-recipient delivery itself.
/// </summary>
public sealed class UserNotifier(IAppDbContext db, IPushSender push, ILogger<UserNotifier> logger)
{
    public async Task<int> NotifyAsync(IReadOnlyCollection<Guid> userIds, PushMessage message, CancellationToken ct)
    {
        if (userIds.Count == 0)
        {
            return 0;
        }

        var devices = await db.UserDevices
            .Where(d => userIds.Contains(d.UserId) && d.IsActive)
            .Join(db.Users.Where(u => u.Status == UserStatus.Active), d => d.UserId, u => u.Id, (d, _) => d)
            .ToListAsync(ct);

        if (devices.Count == 0)
        {
            logger.LogWarning("No active device for {Count} user(s); push {Type} not delivered", userIds.Count, message.Type);
            return 0;
        }

        var results = await push.SendAsync(devices.Select(PushTarget.For).ToList(), message, ct);

        var invalid = results.Where(r => r.Outcome == PushOutcome.InvalidToken).Select(r => r.DeviceId).ToHashSet();
        foreach (var device in devices.Where(d => invalid.Contains(d.Id)))
        {
            device.Deactivate();
        }

        if (invalid.Count > 0)
        {
            await db.SaveChangesAsync(ct);
        }

        var sent = results.Count(r => r.Outcome == PushOutcome.Sent);
        var failed = results.Count(r => r.Outcome == PushOutcome.Failed);
        logger.LogInformation("Push {Type}: {Sent} sent, {Invalid} invalid tokens, {Failed} failed", message.Type, sent, invalid.Count, failed);

        if (sent == 0 && failed > 0)
        {
            // Let the job runner retry: nobody received this notification.
            throw new PushDeliveryException($"Push {message.Type} failed for all {failed} device(s).");
        }

        return sent;
    }
}

public sealed class PushDeliveryException(string message) : Exception(message);
