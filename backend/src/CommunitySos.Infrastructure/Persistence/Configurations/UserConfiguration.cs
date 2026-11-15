using CommunitySos.Domain.Entities;
using CommunitySos.Domain.ValueObjects;
using CommunitySos.Infrastructure.Persistence.Converters;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CommunitySos.Infrastructure.Persistence.Configurations;

internal sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public const string ReferralImmutableTrigger = "TR_Users_ReferralImmutable";

    public void Configure(EntityTypeBuilder<User> b)
    {
        b.ToTable("Users", t =>
        {
            t.HasCheckConstraint("CK_Users_Role", "[Role] IN (1, 2, 3)");
            t.HasCheckConstraint("CK_Users_Status", "[Status] IN (1, 2, 3)");
            // Everyone except the Master Admin must have joined through an admin's invitation.
            t.HasCheckConstraint(
                "CK_Users_Referral",
                "([Role] = 3 AND [ReferredByAdminId] IS NULL) OR ([Role] <> 3 AND [ReferredByAdminId] IS NOT NULL)");
            // EF must know about triggers so it does not use a bare OUTPUT clause on this table.
            t.HasTrigger(ReferralImmutableTrigger);
        });

        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();

        b.Property(x => x.PhoneNumber)
            .HasConversion<PhoneNumberConverter>()
            .HasMaxLength(PhoneNumber.MaxLength)
            .IsUnicode(false)
            .IsRequired();

        b.Property(x => x.EmergencyContactPhone)
            .HasConversion<PhoneNumberConverter>()
            .HasMaxLength(PhoneNumber.MaxLength)
            .IsUnicode(false);

        b.Property(x => x.FirebaseUid).HasMaxLength(User.FirebaseUidMaxLength).IsUnicode(false);
        b.Property(x => x.FullName).HasMaxLength(User.FullNameMaxLength);
        b.Property(x => x.Email).HasMaxLength(User.EmailMaxLength);
        b.Property(x => x.ProfilePhotoBlobName).HasMaxLength(User.BlobNameMaxLength).IsUnicode(false);
        b.Property(x => x.BloodGroup).HasMaxLength(User.BloodGroupMaxLength).IsUnicode(false);
        b.Property(x => x.EmergencyContactName).HasMaxLength(User.FullNameMaxLength);
        b.Property(x => x.AcceptedPolicyVersion).HasMaxLength(UserConsent.PolicyVersionMaxLength).IsUnicode(false);
        b.Property(x => x.AlertsEnabled).HasDefaultValue(true);
        b.Property(x => x.LocationSharingEnabled).HasDefaultValue(false);
        b.Property(x => x.RowVersion).IsRowVersion();

        b.HasOne(x => x.ReferredByAdmin)
            .WithMany()
            .HasForeignKey(x => x.ReferredByAdminId)
            .OnDelete(DeleteBehavior.Restrict);

        b.HasIndex(x => x.PhoneNumber).IsUnique().HasDatabaseName("UX_Users_PhoneNumber");

        b.HasIndex(x => x.FirebaseUid)
            .IsUnique()
            .HasFilter("[FirebaseUid] IS NOT NULL")
            .HasDatabaseName("UX_Users_FirebaseUid");

        // Guarantees there can only ever be one Master Admin.
        b.HasIndex(x => x.Role)
            .IsUnique()
            .HasFilter("[Role] = 3")
            .HasDatabaseName("UX_Users_SingleMasterAdmin");

        b.HasIndex(x => new { x.ReferredByAdminId, x.Status })
            .IncludeProperties(x => new { x.Role, x.CreatedAt })
            .HasDatabaseName("IX_Users_ReferredByAdminId_Status");

        b.HasIndex(x => x.CreatedAt).HasDatabaseName("IX_Users_CreatedAt");

        b.Ignore(x => x.IsActive);
        b.Ignore(x => x.IsAdmin);
        b.Ignore(x => x.CanInvite);
        b.Ignore(x => x.SosRequiresApproval);
        b.Ignore(x => x.InviteeRole);
        b.Ignore(x => x.HasHealthData);
    }
}
