using CommunitySos.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CommunitySos.Infrastructure.Persistence.Configurations;

internal sealed class UserConsentConfiguration : IEntityTypeConfiguration<UserConsent>
{
    public const string AppendOnlyTrigger = "TR_UserConsents_AppendOnly";

    public void Configure(EntityTypeBuilder<UserConsent> b)
    {
        b.ToTable("UserConsents", t =>
        {
            t.HasCheckConstraint("CK_UserConsents_Type", "[Type] BETWEEN 1 AND 3");
            t.HasTrigger(AppendOnlyTrigger);
        });

        b.HasKey(x => x.Id);
        b.Property(x => x.Id).UseIdentityColumn();

        b.Property(x => x.PolicyVersion).HasMaxLength(UserConsent.PolicyVersionMaxLength).IsUnicode(false).IsRequired();
        b.Property(x => x.IpAddress).HasMaxLength(UserConsent.IpMaxLength).IsUnicode(false);
        b.Property(x => x.Source).HasMaxLength(UserConsent.SourceMaxLength).IsUnicode(false);

        // Restrict: consent proof must survive account anonymisation (users are never hard-deleted).
        b.HasOne<User>()
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        // "Current consent" lookup = latest row per (UserId, Type).
        b.HasIndex(x => new { x.UserId, x.Type, x.CapturedAt })
            .IsDescending(false, false, true)
            .IncludeProperties(x => new { x.Granted, x.PolicyVersion })
            .HasDatabaseName("IX_UserConsents_User_Type_CapturedAt");
    }
}
