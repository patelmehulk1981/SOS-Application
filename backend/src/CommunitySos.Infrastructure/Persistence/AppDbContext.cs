using CommunitySos.Application.Abstractions;
using CommunitySos.Domain.Entities;
using CommunitySos.Infrastructure.Persistence.Converters;
using Microsoft.EntityFrameworkCore;

namespace CommunitySos.Infrastructure.Persistence;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options), IAppDbContext
{
    public const string SosReferenceSequence = "SosReferenceSeq";

    public DbSet<User> Users => Set<User>();

    public DbSet<Invitation> Invitations => Set<Invitation>();

    public DbSet<ReferralHistory> ReferralHistory => Set<ReferralHistory>();

    public DbSet<UserConsent> UserConsents => Set<UserConsent>();

    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    public DbSet<UserDevice> UserDevices => Set<UserDevice>();

    public DbSet<UserLocation> UserLocations => Set<UserLocation>();

    public DbSet<LocationKey> LocationKeys => Set<LocationKey>();

    public DbSet<SosRequest> SosRequests => Set<SosRequest>();

    public DbSet<SosAttachment> SosAttachments => Set<SosAttachment>();

    public DbSet<SosStatusHistory> SosStatusHistory => Set<SosStatusHistory>();

    public DbSet<SosNotification> SosNotifications => Set<SosNotification>();

    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("dbo");
        modelBuilder.HasSequence<long>(SosReferenceSequence).StartsAt(1).IncrementsBy(1);

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);

        ApplyGlobalConventions(modelBuilder);
    }

    /// <summary>
    /// - Every DateTime is stored as datetime2(3) and read back as <see cref="DateTimeKind.Utc"/>.
    /// - Every enum is stored as tinyint (all domain enums are byte-backed).
    /// </summary>
    private static void ApplyGlobalConventions(ModelBuilder modelBuilder)
    {
        var utcConverter = new UtcDateTimeConverter();

        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            foreach (var property in entityType.GetProperties())
            {
                var clrType = Nullable.GetUnderlyingType(property.ClrType) ?? property.ClrType;

                if (clrType == typeof(DateTime))
                {
                    property.SetValueConverter(utcConverter);
                    property.SetColumnType("datetime2(3)");
                }
                else if (clrType.IsEnum)
                {
                    property.SetProviderClrType(typeof(byte));
                }
            }
        }
    }
}
