using CommunitySos.Application.Features.Retention;
using CommunitySos.Domain.Entities;
using CommunitySos.Domain.Enums;
using CommunitySos.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CommunitySos.Api.IntegrationTests;

/// <summary>RetentionPurgeJob against the InMemory provider (the SQL Server audit-log gate is covered in SqlServerTests).</summary>
public sealed class RetentionTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly GeoCoordinate Point = new(23.022505, 72.571365);
    private static readonly string Sha = new('b', 64);

    private async Task<RetentionPurgeResult> RunAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<RetentionPurgeJob>().RunAsync(CancellationToken.None);
    }

    /// <summary>An SOS raised <paramref name="createdDaysAgo"/> days ago with one stored photo, optionally resolved <paramref name="closedDaysAgo"/> days ago.</summary>
    private async Task<(Guid SosId, string Blob)> SeedSosWithPhotoAsync(int createdDaysAgo, int? closedDaysAgo)
    {
        var master = await factory.SeedMasterAdminAsync();
        var member = await factory.SeedInviteeAsync(await factory.SeedInviteeAsync(master, "Lead"), "Member");
        var created = DateTime.UtcNow.AddDays(-createdDaysAgo);
        var blob = $"sos/{Guid.NewGuid():N}/photo.jpg";

        await factory.WithDbAsync(async db =>
        {
            var requester = await db.Users.SingleAsync(u => u.Id == member.Id);
            var sos = SosRequest.Create(requester, master.Id, Guid.NewGuid(), SosCategory.Medical, "test", Point, 10, created, created);
            sos.AddAttachment(requester.Id, blob, "image/jpeg", 1000, Sha, 10, 10, created);
            if (closedDaysAgo is { } days)
            {
                var closed = DateTime.UtcNow.AddDays(-days);
                sos.Approve(master.Id, null, created.AddMinutes(1));
                sos.Resolve(requester.Id, null, closed);
            }

            db.SosRequests.Add(sos);
            await db.SaveChangesAsync();
            return sos.Id;
        });

        factory.Blobs.Blobs[blob] = ([1, 2, 3], "image/jpeg");
        var sosId = await factory.WithDbAsync(db => db.SosAttachments.Where(a => a.BlobName == blob).Select(a => a.SosRequestId).SingleAsync());
        return (sosId, blob);
    }

    private Task<bool> AttachmentExistsAsync(string blob) => factory.WithDbAsync(db => db.SosAttachments.AnyAsync(a => a.BlobName == blob));

    [Fact]
    public async Task Photos_are_deleted_90_days_after_the_SOS_closed_and_the_record_is_kept()
    {
        var (oldSos, oldBlob) = await SeedSosWithPhotoAsync(createdDaysAgo: 120, closedDaysAgo: 95);
        var (_, recentBlob) = await SeedSosWithPhotoAsync(createdDaysAgo: 30, closedDaysAgo: 10);

        var result = await RunAsync();

        Assert.True(result.SosPhotos >= 1);
        Assert.False(await AttachmentExistsAsync(oldBlob));
        Assert.False(factory.Blobs.Blobs.ContainsKey(oldBlob));
        Assert.True(await factory.WithDbAsync(db => db.SosRequests.AnyAsync(s => s.Id == oldSos))); // the SOS record stays

        Assert.True(await AttachmentExistsAsync(recentBlob));
        Assert.True(factory.Blobs.Blobs.ContainsKey(recentBlob));
    }

    [Fact]
    public async Task Photos_of_an_SOS_that_never_closed_are_kept()
    {
        var (_, blob) = await SeedSosWithPhotoAsync(createdDaysAgo: 200, closedDaysAgo: null);

        await RunAsync();

        Assert.True(await AttachmentExistsAsync(blob));
    }

    [Fact]
    public async Task A_failed_blob_delete_keeps_the_row_for_the_next_run()
    {
        var (_, blob) = await SeedSosWithPhotoAsync(createdDaysAgo: 120, closedDaysAgo: 100);
        factory.Blobs.FailingDeletes[blob] = true;

        await RunAsync();
        Assert.True(await AttachmentExistsAsync(blob));

        factory.Blobs.FailingDeletes.TryRemove(blob, out _);
        await RunAsync();
        Assert.False(await AttachmentExistsAsync(blob));
    }

    [Fact]
    public async Task Ended_sessions_old_keys_inactive_devices_and_stale_locations_are_deleted()
    {
        var master = await factory.SeedMasterAdminAsync();
        var userId = master.Id;
        var now = DateTime.UtcNow;
        Guid oldToken = Guid.Empty, liveToken = Guid.Empty, oldKey = Guid.Empty, oldDevice = Guid.Empty, liveDevice = Guid.Empty;

        await factory.WithDbAsync(async db =>
        {
            var expired = RefreshToken.Issue(userId, new string('1', 64), Guid.NewGuid(), "phone", now.AddDays(-70), TimeSpan.FromDays(30));
            var live = RefreshToken.Issue(userId, new string('2', 64), Guid.NewGuid(), "phone", now, TimeSpan.FromDays(30));
            var key = LocationKey.Issue(userId, new string('3', 64), "phone", now.AddDays(-60));
            key.Revoke(now.AddDays(-45), "turned_off");
            var deadDevice = UserDevice.RegisterInstallation(userId, $"fid-{Guid.NewGuid():N}", DevicePlatform.Android, "1.0.0", null, now.AddDays(-120));
            deadDevice.Deactivate();
            var activeDevice = UserDevice.RegisterInstallation(userId, $"fid-{Guid.NewGuid():N}", DevicePlatform.Android, "1.0.0", null, now.AddDays(-120));

            db.RefreshTokens.AddRange(expired, live);
            db.LocationKeys.Add(key);
            db.UserDevices.AddRange(deadDevice, activeDevice);
            db.UserLocations.Add(UserLocation.Create(userId, Point, 20, now.AddHours(-11), now.AddHours(-11)));
            await db.SaveChangesAsync();
            (oldToken, liveToken, oldKey, oldDevice, liveDevice) = (expired.Id, live.Id, key.Id, deadDevice.Id, activeDevice.Id);
        });

        // A location under 12 h old today becomes stale later: move the clock forward by rewriting it.
        await factory.WithDbAsync(async db =>
        {
            var location = await db.UserLocations.SingleAsync(l => l.UserId == userId);
            db.Entry(location).Property(l => l.CapturedAt).CurrentValue = now.AddDays(-3);
            await db.SaveChangesAsync();
        });

        await RunAsync();

        await factory.WithDbAsync(async db =>
        {
            Assert.False(await db.RefreshTokens.AnyAsync(t => t.Id == oldToken));
            Assert.True(await db.RefreshTokens.AnyAsync(t => t.Id == liveToken));
            Assert.False(await db.LocationKeys.AnyAsync(k => k.Id == oldKey));
            Assert.False(await db.UserDevices.AnyAsync(d => d.Id == oldDevice));
            Assert.True(await db.UserDevices.AnyAsync(d => d.Id == liveDevice)); // active devices are never purged
            Assert.False(await db.UserLocations.AnyAsync(l => l.UserId == userId));
        });
    }

    [Fact]
    public async Task Audit_logs_older_than_a_year_are_deleted()
    {
        var now = DateTime.UtcNow;
        var marker = $"retention-{Guid.NewGuid():N}";
        await factory.WithDbAsync(async db =>
        {
            db.AuditLogs.Add(new AuditLog(null, "Test", "Test", marker + "-old", null, null, null, null, null, now.AddDays(-400)));
            db.AuditLogs.Add(new AuditLog(null, "Test", "Test", marker + "-new", null, null, null, null, null, now.AddDays(-200)));
            await db.SaveChangesAsync();
        });

        var result = await RunAsync();

        Assert.True(result.AuditLogs >= 1);
        var left = await factory.WithDbAsync(db => db.AuditLogs.Where(a => a.EntityId!.StartsWith(marker)).Select(a => a.EntityId).ToListAsync());
        Assert.Equal([marker + "-new"], left);
    }

    [Fact]
    public async Task Running_twice_is_harmless()
    {
        await SeedSosWithPhotoAsync(createdDaysAgo: 150, closedDaysAgo: 120);
        await RunAsync();

        var second = await RunAsync();

        Assert.Equal(0, second.SosPhotos);
    }
}
