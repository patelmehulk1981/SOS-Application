using CommunitySos.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CommunitySos.Infrastructure.Persistence.Configurations;

internal sealed class UserDeviceConfiguration : IEntityTypeConfiguration<UserDevice>
{
    public void Configure(EntityTypeBuilder<UserDevice> b)
    {
        b.ToTable("UserDevices", t =>
        {
            t.HasCheckConstraint("CK_UserDevices_Platform", "[Platform] IN (1, 2)");
            // Exactly one push target per row: an FID (current) or a legacy FCM token.
            t.HasCheckConstraint(
                "CK_UserDevices_Target",
                "([FcmToken] IS NOT NULL AND [InstallationId] IS NULL) OR ([FcmToken] IS NULL AND [InstallationId] IS NOT NULL)");
        });

        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();

        b.Property(x => x.FcmToken).HasMaxLength(UserDevice.FcmTokenMaxLength).IsUnicode(false);
        b.Property(x => x.InstallationId).HasMaxLength(UserDevice.InstallationIdMaxLength).IsUnicode(false);
        b.Property(x => x.AppVersion).HasMaxLength(UserDevice.AppVersionMaxLength).IsUnicode(false);
        b.Property(x => x.DeviceModel).HasMaxLength(UserDevice.DeviceModelMaxLength);
        b.Property(x => x.IsActive).HasDefaultValue(true);

        b.HasOne<User>()
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        b.HasIndex(x => x.FcmToken).IsUnique().HasFilter("[FcmToken] IS NOT NULL").HasDatabaseName("UX_UserDevices_FcmToken");
        b.HasIndex(x => x.InstallationId).IsUnique().HasFilter("[InstallationId] IS NOT NULL").HasDatabaseName("UX_UserDevices_InstallationId");

        b.HasIndex(x => x.UserId)
            .HasFilter("[IsActive] = 1")
            .IncludeProperties(x => new { x.FcmToken, x.InstallationId, x.Platform })
            .HasDatabaseName("IX_UserDevices_UserId_Active");
    }
}
