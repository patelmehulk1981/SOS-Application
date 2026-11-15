using CommunitySos.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CommunitySos.Infrastructure.Persistence.Configurations;

internal sealed class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> b)
    {
        b.ToTable("RefreshTokens");

        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();

        b.Property(x => x.TokenHash).HasMaxLength(RefreshToken.HashLength).IsFixedLength().IsUnicode(false).IsRequired();
        b.Property(x => x.DeviceId).HasMaxLength(RefreshToken.DeviceIdMaxLength).IsUnicode(false);
        b.Property(x => x.RevokedReason).HasMaxLength(RefreshToken.ReasonMaxLength).IsUnicode(false);

        // Rotation is "UPDATE ... WHERE RevokedAt IS NULL": two concurrent refreshes with the same
        // token cannot both succeed (the loser gets a concurrency exception).
        b.Property(x => x.RevokedAt).IsConcurrencyToken();

        b.HasOne<User>()
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        b.HasIndex(x => x.TokenHash).IsUnique().HasDatabaseName("UX_RefreshTokens_TokenHash");
        b.HasIndex(x => x.UserId).HasDatabaseName("IX_RefreshTokens_UserId");
        b.HasIndex(x => x.FamilyId).HasDatabaseName("IX_RefreshTokens_FamilyId");
        b.HasIndex(x => x.ExpiresAt).HasDatabaseName("IX_RefreshTokens_ExpiresAt"); // cleanup job
    }
}
