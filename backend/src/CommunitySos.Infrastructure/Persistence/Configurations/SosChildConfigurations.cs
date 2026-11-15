using CommunitySos.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CommunitySos.Infrastructure.Persistence.Configurations;

internal sealed class SosAttachmentConfiguration : IEntityTypeConfiguration<SosAttachment>
{
    public void Configure(EntityTypeBuilder<SosAttachment> b)
    {
        b.ToTable("SosAttachments", t => t.HasCheckConstraint("CK_SosAttachments_Size", "[SizeBytes] > 0"));

        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();

        b.Property(x => x.BlobName).HasMaxLength(SosAttachment.BlobNameMaxLength).IsUnicode(false).IsRequired();
        b.Property(x => x.ContentType).HasMaxLength(SosAttachment.ContentTypeMaxLength).IsUnicode(false).IsRequired();
        b.Property(x => x.Sha256).HasMaxLength(SosAttachment.Sha256Length).IsFixedLength().IsUnicode(false).IsRequired();

        b.HasIndex(x => x.SosRequestId).HasDatabaseName("IX_SosAttachments_SosRequestId");
    }
}

internal sealed class SosStatusHistoryConfiguration : IEntityTypeConfiguration<SosStatusHistory>
{
    public const string AppendOnlyTrigger = "TR_SosStatusHistory_AppendOnly";

    public void Configure(EntityTypeBuilder<SosStatusHistory> b)
    {
        b.ToTable("SosStatusHistory", t => t.HasTrigger(AppendOnlyTrigger));

        b.HasKey(x => x.Id);
        b.Property(x => x.Id).UseIdentityColumn();

        b.Property(x => x.Remarks).HasMaxLength(SosRequest.RemarksMaxLength);

        b.HasOne<User>()
            .WithMany()
            .HasForeignKey(x => x.ChangedByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        b.HasIndex(x => new { x.SosRequestId, x.ChangedAt }).HasDatabaseName("IX_SosStatusHistory_Sos_ChangedAt");
    }
}

internal sealed class SosNotificationConfiguration : IEntityTypeConfiguration<SosNotification>
{
    public void Configure(EntityTypeBuilder<SosNotification> b)
    {
        b.ToTable("SosNotifications", t =>
        {
            t.HasCheckConstraint("CK_SosNotifications_DeliveryStatus", "[DeliveryStatus] BETWEEN 1 AND 4");
            t.HasCheckConstraint("CK_SosNotifications_ResponseType", "[ResponseType] BETWEEN 0 AND 4");
            t.HasCheckConstraint("CK_SosNotifications_Distance", "[DistanceMeters] >= 0");
        });

        b.HasKey(x => x.Id);
        b.Property(x => x.Id).UseIdentityColumn();

        b.Property(x => x.LastError).HasMaxLength(SosNotification.ErrorMaxLength);
        b.Property(x => x.ResponseNote).HasMaxLength(SosNotification.NoteMaxLength);
        b.Property(x => x.AttemptCount).HasDefaultValue(0);
        b.Property(x => x.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()");

        b.HasOne<SosRequest>()
            .WithMany()
            .HasForeignKey(x => x.SosRequestId)
            .OnDelete(DeleteBehavior.Restrict);

        b.HasOne<User>()
            .WithMany()
            .HasForeignKey(x => x.RecipientUserId)
            .OnDelete(DeleteBehavior.Restrict);

        // THE duplicate-notification guard: one alert per user per SOS.
        b.HasIndex(x => new { x.SosRequestId, x.RecipientUserId })
            .IsUnique()
            .IncludeProperties(x => new { x.DeliveryStatus, x.ResponseType })
            .HasDatabaseName("UX_SosNotifications_Sos_Recipient");

        // "My alerts" feed.
        b.HasIndex(x => new { x.RecipientUserId, x.CreatedAt })
            .IsDescending(false, true)
            .HasDatabaseName("IX_SosNotifications_Recipient_CreatedAt");
    }
}

internal sealed class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public const string AppendOnlyTrigger = "TR_AuditLogs_AppendOnly";

    public void Configure(EntityTypeBuilder<AuditLog> b)
    {
        b.ToTable("AuditLogs", t => t.HasTrigger(AppendOnlyTrigger));

        b.HasKey(x => x.Id);
        b.Property(x => x.Id).UseIdentityColumn();

        b.Property(x => x.Action).HasMaxLength(AuditLog.ActionMaxLength).IsUnicode(false).IsRequired();
        b.Property(x => x.EntityName).HasMaxLength(AuditLog.EntityNameMaxLength).IsUnicode(false).IsRequired();
        b.Property(x => x.EntityId).HasMaxLength(AuditLog.EntityIdMaxLength).IsUnicode(false);
        b.Property(x => x.IpAddress).HasMaxLength(AuditLog.IpMaxLength).IsUnicode(false);
        b.Property(x => x.UserAgent).HasMaxLength(AuditLog.UserAgentMaxLength);
        b.Property(x => x.CorrelationId).HasMaxLength(AuditLog.CorrelationIdMaxLength).IsUnicode(false);
        // OldValues / NewValues: nvarchar(max) JSON.

        // No FK to Users on purpose: audit rows must outlive any data change and may be anonymous.
        b.HasIndex(x => new { x.EntityName, x.EntityId }).HasDatabaseName("IX_AuditLogs_Entity");
        b.HasIndex(x => new { x.UserId, x.CreatedAt }).HasDatabaseName("IX_AuditLogs_User_CreatedAt");
        b.HasIndex(x => x.CreatedAt).HasDatabaseName("IX_AuditLogs_CreatedAt");
    }
}
