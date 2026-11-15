using CommunitySos.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CommunitySos.Infrastructure.Persistence.Configurations;

internal sealed class SosRequestConfiguration : IEntityTypeConfiguration<SosRequest>
{
    public void Configure(EntityTypeBuilder<SosRequest> b)
    {
        b.ToTable("SosRequests", t =>
        {
            t.HasCheckConstraint("CK_SosRequests_Status", "[Status] BETWEEN 1 AND 7");
            t.HasCheckConstraint("CK_SosRequests_Category", "[Category] BETWEEN 1 AND 5");
            t.HasCheckConstraint("CK_SosRequests_Latitude", "[Latitude] BETWEEN -90 AND 90");
            t.HasCheckConstraint("CK_SosRequests_Longitude", "[Longitude] BETWEEN -180 AND 180");
            t.HasCheckConstraint("CK_SosRequests_RejectRemarks", "[Status] <> 3 OR [ReviewRemarks] IS NOT NULL");
        });

        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();

        b.Property(x => x.ReferenceSeq)
            .HasDefaultValueSql($"NEXT VALUE FOR [dbo].[{AppDbContext.SosReferenceSequence}]")
            .ValueGeneratedOnAdd();

        b.Property(x => x.ReferenceNo)
            .HasMaxLength(SosRequest.ReferenceNoMaxLength)
            .IsUnicode(false)
            .HasComputedColumnSql("'SOS-' + RIGHT('00000000' + CAST([ReferenceSeq] AS varchar(20)), 8)", stored: true);

        b.Property(x => x.Description).HasMaxLength(SosRequest.DescriptionMaxLength);
        b.Property(x => x.Location).HasColumnType("geography").IsRequired();
        b.Property(x => x.Latitude).HasPrecision(9, 6);
        b.Property(x => x.Longitude).HasPrecision(9, 6);
        b.Property(x => x.Address).HasMaxLength(SosRequest.AddressMaxLength);
        b.Property(x => x.ReviewRemarks).HasMaxLength(SosRequest.RemarksMaxLength);
        b.Property(x => x.RecipientCount).HasDefaultValue(0);
        b.Property(x => x.RowVersion).IsRowVersion();

        b.HasOne(x => x.Requester)
            .WithMany()
            .HasForeignKey(x => x.RequesterUserId)
            .OnDelete(DeleteBehavior.Restrict);

        b.HasOne(x => x.AssignedAdmin)
            .WithMany()
            .HasForeignKey(x => x.AssignedAdminId)
            .OnDelete(DeleteBehavior.Restrict);

        b.HasOne(x => x.ReviewedBy)
            .WithMany()
            .HasForeignKey(x => x.ReviewedByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        b.HasMany(x => x.Attachments)
            .WithOne()
            .HasForeignKey(a => a.SosRequestId)
            .OnDelete(DeleteBehavior.Cascade);

        b.HasMany(x => x.StatusHistory)
            .WithOne()
            .HasForeignKey(h => h.SosRequestId)
            .OnDelete(DeleteBehavior.Restrict);

        b.Navigation(x => x.Attachments).UsePropertyAccessMode(PropertyAccessMode.Field);
        b.Navigation(x => x.StatusHistory).UsePropertyAccessMode(PropertyAccessMode.Field);

        b.HasIndex(x => x.ReferenceNo).IsUnique().HasDatabaseName("UX_SosRequests_ReferenceNo");

        // Idempotency: one SOS per (requester, client request id).
        b.HasIndex(x => new { x.RequesterUserId, x.ClientRequestId })
            .IsUnique()
            .HasDatabaseName("UX_SosRequests_Requester_ClientRequestId");

        // Admin review queue.
        b.HasIndex(x => new { x.AssignedAdminId, x.Status, x.CreatedAt })
            .IsDescending(false, false, true)
            .HasDatabaseName("IX_SosRequests_Admin_Status_CreatedAt");

        // "My SOS" list.
        b.HasIndex(x => new { x.RequesterUserId, x.CreatedAt })
            .IsDescending(false, true)
            .HasDatabaseName("IX_SosRequests_Requester_CreatedAt");

        // Escalation / expiry jobs and the global dashboard.
        b.HasIndex(x => new { x.Status, x.CreatedAt }).HasDatabaseName("IX_SosRequests_Status_CreatedAt");

        b.Ignore(x => x.IsAwaitingReview);
        b.Ignore(x => x.IsOpen);
        b.Ignore(x => x.Coordinate);
    }
}
