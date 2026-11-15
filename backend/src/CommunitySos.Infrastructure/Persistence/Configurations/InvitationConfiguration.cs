using CommunitySos.Domain.Entities;
using CommunitySos.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CommunitySos.Infrastructure.Persistence.Configurations;

internal sealed class InvitationConfiguration : IEntityTypeConfiguration<Invitation>
{
    public void Configure(EntityTypeBuilder<Invitation> b)
    {
        b.ToTable("Invitations", t =>
        {
            t.HasCheckConstraint("CK_Invitations_InviteeRole", "[InviteeRole] IN (1, 2)");
            t.HasCheckConstraint("CK_Invitations_MaxUses", "[MaxUses] IS NULL OR [MaxUses] > 0");
            t.HasCheckConstraint("CK_Invitations_UsedCount", "[UsedCount] >= 0 AND ([MaxUses] IS NULL OR [UsedCount] <= [MaxUses])");
        });

        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();

        b.Property(x => x.Code).HasMaxLength(InvitationCode.MaxLength).IsUnicode(false).IsRequired();
        b.Property(x => x.Label).HasMaxLength(Invitation.LabelMaxLength);
        b.Property(x => x.UsedCount).HasDefaultValue(0);
        b.Property(x => x.RowVersion).IsRowVersion();

        b.HasOne(x => x.CreatedBy)
            .WithMany()
            .HasForeignKey(x => x.CreatedByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        b.HasIndex(x => x.Code).IsUnique().HasDatabaseName("UX_Invitations_Code");

        b.HasIndex(x => new { x.CreatedByUserId, x.CreatedAt })
            .IsDescending(false, true)
            .HasDatabaseName("IX_Invitations_CreatedBy_CreatedAt");
    }
}
