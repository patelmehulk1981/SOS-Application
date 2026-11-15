using CommunitySos.Domain.Common;
using CommunitySos.Domain.Enums;

namespace CommunitySos.Domain.Entities;

/// <summary>
/// A push-capable app installation. It is addressed by its Firebase Installation ID (FID), which is how
/// current app versions register, or by a legacy FCM registration token (deprecated by Firebase in 2026,
/// still accepted from older builds). Exactly one target is stored: attaching an FID drops the token,
/// following Firebase's advice not to mix the two APIs for one client.
/// Both identifiers are globally unique: when a different account signs in on the same phone the row is
/// re-assigned, so one phone never receives the same alert twice.
/// </summary>
public sealed class UserDevice : Entity<Guid>, IHasTimestamps
{
    public const int FcmTokenMaxLength = 512;
    public const int InstallationIdMaxLength = 200;
    public const int AppVersionMaxLength = 20;
    public const int DeviceModelMaxLength = 100;

    private UserDevice()
    {
    }

    public Guid UserId { get; private set; }

    /// <summary>Legacy FCM registration token; null once the device registers with an FID.</summary>
    public string? FcmToken { get; private set; }

    /// <summary>Firebase Installation ID registered with FCM (messaging.register() on the device).</summary>
    public string? InstallationId { get; private set; }

    public DevicePlatform Platform { get; private set; }

    public string? AppVersion { get; private set; }

    public string? DeviceModel { get; private set; }

    public bool IsActive { get; private set; }

    public DateTime LastSeenAt { get; private set; }

    public DateTime CreatedAt { get; private set; }

    public DateTime UpdatedAt { get; private set; }

    /// <summary>Legacy registration by FCM token.</summary>
    public static UserDevice Register(Guid userId, string fcmToken, DevicePlatform platform, string? appVersion, string? deviceModel, DateTime now)
    {
        var device = new UserDevice
        {
            Id = SequentialGuid.NewGuid(),
            FcmToken = Text.Required(fcmToken, FcmTokenMaxLength, nameof(FcmToken)),
        };
        device.AssignTo(userId, platform, appVersion, deviceModel, now);
        return device;
    }

    /// <summary>Registration by Firebase Installation ID (current app versions).</summary>
    public static UserDevice RegisterInstallation(Guid userId, string installationId, DevicePlatform platform, string? appVersion, string? deviceModel, DateTime now)
    {
        var device = new UserDevice
        {
            Id = SequentialGuid.NewGuid(),
            InstallationId = Text.Required(installationId, InstallationIdMaxLength, nameof(InstallationId)),
        };
        device.AssignTo(userId, platform, appVersion, deviceModel, now);
        return device;
    }

    public void AssignTo(Guid userId, DevicePlatform platform, string? appVersion, string? deviceModel, DateTime now)
    {
        if (!Enum.IsDefined(platform))
        {
            throw new DomainException("device.invalid_platform", "Unsupported device platform.");
        }

        UserId = userId;
        Platform = platform;
        AppVersion = Text.Optional(appVersion, AppVersionMaxLength, nameof(AppVersion));
        DeviceModel = Text.Optional(deviceModel, DeviceModelMaxLength, nameof(DeviceModel));
        IsActive = true;
        LastSeenAt = now;
    }

    /// <summary>An upgraded app moves this row from its legacy token to its FID (the token is dropped).</summary>
    public void AttachInstallationId(string installationId)
    {
        InstallationId = Text.Required(installationId, InstallationIdMaxLength, nameof(InstallationId));
        FcmToken = null;
    }

    public void Touch(DateTime now) => LastSeenAt = now;

    /// <summary>Called on logout or when FCM reports the token/FID as unregistered or invalid.</summary>
    public void Deactivate() => IsActive = false;
}
