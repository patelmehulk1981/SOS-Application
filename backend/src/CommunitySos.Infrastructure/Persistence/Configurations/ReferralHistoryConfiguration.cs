using CommunitySos.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CommunitySos.Infrastructure.Persistence.Configurations;

internal sealed class ReferralHistoryConfiguration : IEntityTypeConfiguration<ReferralHistory>
{
    public const string AppendOnlyTrigger = "TR_ReferralHistory_AppendOnly";

    public void Configure(EntityTypeBuilder<ReferralHistory> b)
    {
        b.ToTable("ReferralHistory", t =>
        {
            t.HasCheckConstraint("CK_ReferralHistory_AssignedRole", "[AssignedRole] IN (1, 2)");
            t.HasTrigger(AppendOnlyTrigger);
        });

        b.HasKey(x => x.Id);
        b.Property(x => x.Id).UseIdentityColumn();

        b.Property(x => x.IpAddress).HasMaxLength(ReferralHistory.IpMaxLength).IsUnicode(false);
        b.Property(x => x.DeviceInfo).HasMaxLength(ReferralHistory.DeviceInfoMaxLength);

        b.HasOne<Invitation>()
            .WithMany()
            .HasForeignKey(x => x.InvitationId)
            .OnDelete(DeleteBehavior.Restrict);

        b.HasOne<User>()
            .WithMany()
            .HasForeignKey(x => x.ReferrerAdminId)
            .OnDelete(DeleteBehavior.Restrict);

        b.HasOne(x => x.ReferredUser)
            .WithMany()
            .HasForeignKey(x => x.ReferredUserId)
            .OnDelete(DeleteBehavior.Restrict);

        // A user is referred exactly once, permanently.
        b.HasIndex(x => x.ReferredUserId).IsUnique().HasDatabaseName("UX_ReferralHistory_ReferredUserId");

        b.HasIndex(x => new { x.ReferrerAdminId, x.RegisteredAt })
            .IsDescending(false, true)
            .HasDatabaseName("IX_ReferralHistory_Referrer_RegisteredAt");

        b.HasIndex(x => x.InvitationId).HasDatabaseName("IX_ReferralHistory_InvitationId");
    }
}
