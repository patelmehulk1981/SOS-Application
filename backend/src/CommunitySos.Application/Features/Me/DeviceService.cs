using CommunitySos.Application.Abstractions;
using CommunitySos.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace CommunitySos.Application.Features.Me;

/// <summary>
/// Push registration. Current apps register their Firebase Installation ID (FID) on every start and whenever
/// Firebase reports a new one; older builds send a legacy FCM token. The identifier follows the latest
/// sign-in on that phone.
/// </summary>
public sealed class DeviceService(IAppDbContext db, TimeProvider clock)
{
    public async Task RegisterAsync(Guid userId, RegisterDeviceRequest request, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var fid = Normalise(request.InstallationId);
        var token = Normalise(request.FcmToken);

        if (fid is not null)
        {
            var device = await db.UserDevices.FirstOrDefaultAsync(d => d.InstallationId == fid, ct);
            var legacy = token is null ? null : await db.UserDevices.FirstOrDefaultAsync(d => d.FcmToken == token, ct);

            if (device is null && legacy is not null)
            {
                legacy.AttachInstallationId(fid); // upgraded app: same row, now addressed by FID
                device = legacy;
            }
            else if (legacy is not null && legacy != device)
            {
                db.UserDevices.Remove(legacy); // the FID row already exists; drop the stale token row
            }

            if (device is null)
            {
                db.UserDevices.Add(UserDevice.RegisterInstallation(userId, fid, request.Platform, request.AppVersion, request.DeviceModel, now));
            }
            else
            {
                device.AssignTo(userId, request.Platform, request.AppVersion, request.DeviceModel, now);
            }
        }
        else
        {
            var device = await db.UserDevices.FirstOrDefaultAsync(d => d.FcmToken == token, ct);
            if (device is null)
            {
                db.UserDevices.Add(UserDevice.Register(userId, token!, request.Platform, request.AppVersion, request.DeviceModel, now));
            }
            else
            {
                // Same physical device, possibly a different account now: the token follows the latest login.
                device.AssignTo(userId, request.Platform, request.AppVersion, request.DeviceModel, now);
            }
        }

        await db.SaveChangesAsync(ct);
    }

    public async Task UnregisterAsync(Guid userId, UnregisterDeviceRequest request, CancellationToken ct)
    {
        if (await FindOwnAsync(userId, request.InstallationId, request.FcmToken, ct) is { } device)
        {
            device.Deactivate();
            await db.SaveChangesAsync(ct);
        }
    }

    /// <summary>The caller's device by FID or legacy token (null if it is not theirs: idempotent no-op).</summary>
    internal async Task<UserDevice?> FindOwnAsync(Guid userId, string? installationId, string? fcmToken, CancellationToken ct)
    {
        var fid = Normalise(installationId);
        var token = Normalise(fcmToken);
        if (fid is not null)
        {
            return await db.UserDevices.FirstOrDefaultAsync(d => d.InstallationId == fid && d.UserId == userId, ct);
        }

        return token is null ? null : await db.UserDevices.FirstOrDefaultAsync(d => d.FcmToken == token && d.UserId == userId, ct);
    }

    private static string? Normalise(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
