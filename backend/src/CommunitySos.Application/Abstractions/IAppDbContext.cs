using CommunitySos.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace CommunitySos.Application.Abstractions;

/// <summary>Unit of work exposed to use cases. Implemented by the EF Core context in Infrastructure.</summary>
public interface IAppDbContext
{
    DbSet<User> Users { get; }

    DbSet<Invitation> Invitations { get; }

    DbSet<ReferralHistory> ReferralHistory { get; }

    DbSet<UserConsent> UserConsents { get; }

    DbSet<RefreshToken> RefreshTokens { get; }

    DbSet<UserDevice> UserDevices { get; }

    DbSet<UserLocation> UserLocations { get; }

    DbSet<LocationKey> LocationKeys { get; }

    DbSet<SosRequest> SosRequests { get; }

    DbSet<SosAttachment> SosAttachments { get; }

    DbSet<SosStatusHistory> SosStatusHistory { get; }

    DbSet<SosNotification> SosNotifications { get; }

    DbSet<AuditLog> AuditLogs { get; }

    /// <summary>Exposed so use cases can discard tracked changes before retrying after a concurrency conflict.</summary>
    ChangeTracker ChangeTracker { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
