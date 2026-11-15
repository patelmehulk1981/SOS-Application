using CommunitySos.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CommunitySos.Infrastructure.Persistence.Configurations;

internal sealed class LocationKeyConfiguration : IEntityTypeConfiguration<LocationKey>
{
    public void Configure(EntityTypeBuilder<LocationKey> b)
    {
        b.ToTable("LocationKeys");

        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();

        b.Property(x => x.KeyHash).HasMaxLength(LocationKey.HashLength).IsFixedLength().IsUnicode(false).IsRequired();
        b.Property(x => x.DeviceId).HasMaxLength(LocationKey.DeviceIdMaxLength).IsUnicode(false).IsRequired();
        b.Property(x => x.RevokedReason).HasMaxLength(LocationKey.ReasonMaxLength).IsUnicode(false);

        b.HasOne<User>()
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        b.HasIndex(x => x.KeyHash).IsUnique().HasDatabaseName("UX_LocationKeys_KeyHash");

        // At most one usable key per phone and account; issuing a new one revokes the old one.
        b.HasIndex(x => new { x.UserId, x.DeviceId })
            .IsUnique()
            .HasFilter("[RevokedAt] IS NULL")
            .HasDatabaseName("UX_LocationKeys_User_Device_Active");

        b.Ignore(x => x.IsActive);
    }
}
