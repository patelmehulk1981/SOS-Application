using CommunitySos.Application.Abstractions;
using CommunitySos.Application.Common.Exceptions;
using CommunitySos.Application.Common.Options;
using CommunitySos.Application.Common.Security;
using CommunitySos.Domain.Entities;
using CommunitySos.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CommunitySos.Application.Features.Me;

/// <summary>Data-principal rights: access/portability (export) and erasure (delete account).</summary>
public sealed class AccountService(
    IAppDbContext db,
    ProfileService profiles,
    IFirebaseAuthService firebase,
    IUserStatusReader statusReader,
    ICurrentUser caller,
    IOptionsSnapshot<AppOptions> appOptions,
    TimeProvider clock,
    ILogger<AccountService> logger)
{
    private static readonly SosStatus[] OpenStatuses = [SosStatus.Pending, SosStatus.Escalated, SosStatus.Approved];

    /// <summary>Everything we hold about the caller, as one machine-readable document (DPDP s.11, GDPR Art. 15 and 20).</summary>
    public async Task<DataExport> ExportAsync(Guid userId, CancellationToken ct)
    {
        var profile = await profiles.GetAsync(userId, ct);

        var referral = await db.ReferralHistory
            .AsNoTracking()
            .Where(r => r.ReferredUserId == userId)
            .Select(r => new ExportReferral(r.InvitationId, r.ReferrerAdminId, r.AssignedRole, r.RegisteredAt))
            .FirstOrDefaultAsync(ct);

        var consents = await db.UserConsents
            .AsNoTracking()
            .Where(c => c.UserId == userId)
            .OrderBy(c => c.CapturedAt)
            .Select(c => new ExportConsent(c.Type, c.Granted, c.PolicyVersion, c.CapturedAt, c.Source))
            .ToListAsync(ct);

        var devices = await db.UserDevices
            .AsNoTracking()
            .Where(d => d.UserId == userId)
            .Select(d => new ExportDevice(d.Platform, d.AppVersion, d.DeviceModel, d.IsActive, d.LastSeenAt))
            .ToListAsync(ct);

        var location = await db.UserLocations
            .AsNoTracking()
            .Where(l => l.UserId == userId)
            .Select(l => new { l.Location, l.AccuracyMeters, l.CapturedAt })
            .FirstOrDefaultAsync(ct);

        var locationKeys = await db.LocationKeys
            .AsNoTracking()
            .Where(k => k.UserId == userId)
            .OrderBy(k => k.CreatedAt)
            .Select(k => new ExportLocationKey(k.CreatedAt, k.LastUsedAt, k.RevokedAt, k.RevokedReason))
            .ToListAsync(ct);

        var sos = await db.SosRequests
            .AsNoTracking()
            .Where(s => s.RequesterUserId == userId)
            .OrderBy(s => s.CreatedAt)
            .Select(s => new ExportSos(
                s.ReferenceNo,
                s.Category,
                s.Description,
                s.Latitude,
                s.Longitude,
                s.Address,
                s.Status,
                s.ReviewRemarks,
                s.CreatedAt,
                s.ClosedAt,
                s.StatusHistory.OrderBy(h => h.ChangedAt).Select(h => new ExportStatusChange(h.FromStatus, h.ToStatus, h.Remarks, h.ChangedAt)).ToList()))
            .ToListAsync(ct);

        var alerts = await (
                from n in db.SosNotifications.AsNoTracking()
                join s in db.SosRequests.AsNoTracking() on n.SosRequestId equals s.Id
                where n.RecipientUserId == userId
                orderby n.CreatedAt
                select new ExportAlert(s.ReferenceNo, n.DistanceMeters, n.CreatedAt, n.AcknowledgedAt, n.ResponseType, n.ResponseNote, n.RespondedAt))
            .ToListAsync(ct);

        var invitations = await db.Invitations
            .AsNoTracking()
            .Where(i => i.CreatedByUserId == userId)
            .OrderBy(i => i.CreatedAt)
            .Select(i => new ExportInvitation(i.Code, i.Label, i.InviteeRole, i.UsedCount, i.MaxUses, i.ExpiresAt, i.IsRevoked, i.CreatedAt))
            .ToListAsync(ct);

        var now = clock.GetUtcNow().UtcDateTime;
        db.AuditLogs.Add(SecurityAudit.Event(caller, SecurityAudit.DataExported, userId, now));
        await db.SaveChangesAsync(ct);

        return new DataExport(
            now,
            profile,
            referral,
            consents,
            devices,
            location is null ? null : new ExportLocation(location.Location.Y, location.Location.X, location.AccuracyMeters, location.CapturedAt),
            locationKeys,
            sos,
            alerts,
            invitations);
    }

    /// <summary>
    /// Erases the account: personal data is anonymised in place (rows are kept so referral and SOS
    /// history stay consistent), location/devices/sessions are deleted, consent withdrawals are recorded,
    /// and the Firebase phone identity is removed.
    /// </summary>
    public async Task DeleteAsync(Guid userId, CancellationToken ct)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct) ?? throw NotFoundException.For(nameof(User));

        if (await db.SosRequests.AnyAsync(s => s.RequesterUserId == userId && OpenStatuses.Contains(s.Status), ct))
        {
            throw new ConflictException("account.open_sos", "Cancel or resolve your active SOS before deleting your account.");
        }

        var now = clock.GetUtcNow().UtcDateTime;
        var firebaseUid = user.FirebaseUid;
        var photoBlob = user.ProfilePhotoBlobName;
        var policyVersion = user.AcceptedPolicyVersion ?? appOptions.Value.PolicyVersion;

        user.DeleteAndAnonymise();

        foreach (var type in Enum.GetValues<ConsentType>())
        {
            db.UserConsents.Add(UserConsent.Record(userId, type, policyVersion, granted: false, now, caller.IpAddress, caller.UserAgent));
        }

        db.UserLocations.RemoveRange(await db.UserLocations.Where(l => l.UserId == userId).ToListAsync(ct));
        db.UserDevices.RemoveRange(await db.UserDevices.Where(d => d.UserId == userId).ToListAsync(ct));
        db.LocationKeys.RemoveRange(await db.LocationKeys.Where(k => k.UserId == userId).ToListAsync(ct));
        db.RefreshTokens.RemoveRange(await db.RefreshTokens.Where(t => t.UserId == userId).ToListAsync(ct));

        // Alerts received stay (they belong to other people's SOS records); free-text notes may identify the person.
        foreach (var alert in await db.SosNotifications.Where(n => n.RecipientUserId == userId && n.ResponseNote != null).ToListAsync(ct))
        {
            alert.ClearNote();
        }

        db.AuditLogs.Add(SecurityAudit.Event(caller, SecurityAudit.AccountDeleted, userId, now));

        await db.SaveChangesAsync(ct);
        statusReader.Invalidate(userId);

        if (photoBlob is not null)
        {
            await profiles.TryDeleteBlobAsync(photoBlob);
        }

        if (firebaseUid is not null)
        {
            try
            {
                await firebase.DeleteUserAsync(firebaseUid, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // The app account is already erased; flag the leftover Firebase identity for manual cleanup.
                logger.LogError(ex, "Account {UserId} deleted but Firebase user removal failed; remove it manually", userId);
            }
        }
    }
}

public sealed record DataExport(
    DateTime ExportedAt,
    ProfileResponse Profile,
    ExportReferral? Referral,
    IReadOnlyList<ExportConsent> Consents,
    IReadOnlyList<ExportDevice> Devices,
    ExportLocation? LastKnownLocation,
    IReadOnlyList<ExportLocationKey> BackgroundLocationKeys,
    IReadOnlyList<ExportSos> SosRequests,
    IReadOnlyList<ExportAlert> AlertsReceived,
    IReadOnlyList<ExportInvitation> InvitationsCreated);

public sealed record ExportReferral(Guid InvitationId, Guid InvitedByUserId, UserRole AssignedRole, DateTime RegisteredAt);

public sealed record ExportConsent(ConsentType Type, bool Granted, string PolicyVersion, DateTime CapturedAt, string? Source);

public sealed record ExportDevice(DevicePlatform Platform, string? AppVersion, string? DeviceModel, bool IsActive, DateTime LastSeenAt);

public sealed record ExportLocation(double Latitude, double Longitude, double? AccuracyMeters, DateTime CapturedAt);

/// <summary>Background location on a phone: when it was turned on, last reported, and stopped (the key itself is never exported).</summary>
public sealed record ExportLocationKey(DateTime CreatedAt, DateTime? LastUsedAt, DateTime? RevokedAt, string? RevokedReason);

public sealed record ExportSos(
    string ReferenceNo,
    SosCategory Category,
    string? Description,
    decimal Latitude,
    decimal Longitude,
    string? Address,
    SosStatus Status,
    string? ReviewRemarks,
    DateTime CreatedAt,
    DateTime? ClosedAt,
    IReadOnlyList<ExportStatusChange> History);

public sealed record ExportStatusChange(SosStatus? From, SosStatus To, string? Remarks, DateTime ChangedAt);

public sealed record ExportAlert(
    string SosReferenceNo,
    double DistanceMeters,
    DateTime ReceivedAt,
    DateTime? AcknowledgedAt,
    ResponseType Response,
    string? ResponseNote,
    DateTime? RespondedAt);

public sealed record ExportInvitation(
    string Code,
    string? Label,
    UserRole InviteeRole,
    int UsedCount,
    int? MaxUses,
    DateTime? ExpiresAt,
    bool IsRevoked,
    DateTime CreatedAt);
