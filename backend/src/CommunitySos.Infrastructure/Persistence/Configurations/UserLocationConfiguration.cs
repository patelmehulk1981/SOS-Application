using CommunitySos.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CommunitySos.Infrastructure.Persistence.Configurations;

internal sealed class UserLocationConfiguration : IEntityTypeConfiguration<UserLocation>
{
    public void Configure(EntityTypeBuilder<UserLocation> b)
    {
        b.ToTable("UserLocations");

        // Clustered PK on UserId is required by the spatial index (database/scripts/010_spatial_indexes.sql).
        b.HasKey(x => x.UserId);
        b.Property(x => x.UserId).ValueGeneratedNever();

        b.Property(x => x.Location).HasColumnType("geography").IsRequired();

        b.HasOne<User>()
            .WithOne()
            .HasForeignKey<UserLocation>(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        b.HasIndex(x => x.CapturedAt).HasDatabaseName("IX_UserLocations_CapturedAt");

        b.Ignore(x => x.Coordinate);
    }
}
