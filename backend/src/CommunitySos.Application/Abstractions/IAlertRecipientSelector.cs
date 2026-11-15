using CommunitySos.Application.Common.Options;

namespace CommunitySos.Application.Abstractions;

/// <summary>
/// Finds and records the neighbours to alert for an approved SOS, and returns the devices that still need a
/// push. SQL Server: dbo.usp_SelectSosRecipients (database/scripts/030). Idempotent: calling it again for the
/// same SOS never alerts anyone twice and only returns undelivered recipients with attempts left.
/// </summary>
public interface IAlertRecipientSelector
{
    Task<IReadOnlyList<AlertDelivery>> SelectAsync(Guid sosId, AlertOptions options, CancellationToken cancellationToken);
}

/// <summary>One (recipient, device) pair to push to. A recipient with two phones appears twice.</summary>
public sealed record AlertDelivery(
    long SosNotificationId,
    Guid RecipientUserId,
    double DistanceMeters,
    Guid DeviceId,
    string? FcmToken,
    string? InstallationId);
