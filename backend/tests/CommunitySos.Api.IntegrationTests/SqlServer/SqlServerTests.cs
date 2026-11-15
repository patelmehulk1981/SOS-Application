using System.Diagnostics;
using CommunitySos.Application.Abstractions;
using CommunitySos.Application.Common.Notifications;
using CommunitySos.Application.Common.Options;
using CommunitySos.Application.Features.Sos.Jobs;
using CommunitySos.Domain.Entities;
using CommunitySos.Domain.Enums;
using CommunitySos.Domain.ValueObjects;
using CommunitySos.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Abstractions;

namespace CommunitySos.Api.IntegrationTests.SqlServer;

/// <summary>
/// What the InMemory suite cannot check: migrations and T-SQL scripts on a real SQL Server, the spatial
/// recipient procedure (and its sp_getapplock serialisation), triggers, filtered unique indexes, and how
/// the 5 km search scales.
/// </summary>
[Collection(SqlServerCollection.Name)]
[Trait("Category", SqlServerAvailability.Category)]
public sealed class SqlServerTests(SqlServerFixture fixture, ITestOutputHelper output)
{
    private static int _originSequence;

    /// <summary>
    /// Every test (xunit makes one instance per test) gets its own SOS point, 0.5° (~55 km) from every other
    /// test's, because the collection shares one database: otherwise one test's neighbours would be alerted
    /// by another test's SOS.
    /// </summary>
    private readonly GeoCoordinate Origin = new(8.0 + (0.5 * Interlocked.Increment(ref _originSequence)), 77.0);

    private SqlServerApiFactory Factory => fixture.Factory;

    /// <summary>A point <paramref name="metersNorth"/> north of <see cref="Origin"/> (1° latitude ≈ 111,195 m).</summary>
    private GeoCoordinate North(double metersNorth) => new(Origin.Latitude + (metersNorth / 111_195.0), Origin.Longitude);

    private sealed record NeighbourSpec(double MetersNorth, double Accuracy = 20, bool Sharing = true, bool Alerts = true, bool Suspended = false, double AgeHours = 0.1);

    /// <summary>Seeds a Sub-Admin with one invitation and the given members, each with a location and a push installation.</summary>
    private async Task<(User Lead, List<User> Members)> SeedCommunityAsync(params NeighbourSpec[] specs)
    {
        var now = DateTime.UtcNow;
        var leadInvite = Invitation.Create(fixture.Master, null, null, null, now);
        var lead = leadInvite.Redeem(PhoneNumber.Create(ApiFactory.RandomPhone()), $"uid-{Guid.NewGuid():N}", new RegistrationConsent(ApiFactory.PolicyVersion, true, null), now, null, null);
        lead.User.UpdateProfile("Lead", null, null, null, null);
        var memberInvite = Invitation.Create(lead.User, null, null, null, now);

        var members = new List<User>();
        await Factory.WithDbAsync(async db =>
        {
            db.Invitations.AddRange(leadInvite, memberInvite);
            db.Users.Add(lead.User);
            db.ReferralHistory.Add(lead.Referral);
            db.UserConsents.Add(lead.TermsConsent);

            foreach (var spec in specs)
            {
                var r = memberInvite.Redeem(PhoneNumber.Create(ApiFactory.RandomPhone()), $"uid-{Guid.NewGuid():N}", new RegistrationConsent(ApiFactory.PolicyVersion, true, null), now, null, null);
                r.User.UpdateProfile("Neighbour", null, null, null, null);
                r.User.SetAlertsEnabled(spec.Alerts);
                db.Users.Add(r.User);
                db.ReferralHistory.Add(r.Referral);
                db.UserConsents.Add(r.TermsConsent);
                if (spec.Sharing)
                {
                    db.UserConsents.Add(r.User.SetLocationSharing(true, ApiFactory.PolicyVersion, now, null, null));
                }

                if (spec.Suspended)
                {
                    r.User.Suspend();
                }

                var captured = now.AddHours(-Math.Min(spec.AgeHours, 11));
                db.UserLocations.Add(UserLocation.Create(r.User.Id, North(spec.MetersNorth), spec.Accuracy, captured, captured));
                db.UserDevices.Add(UserDevice.RegisterInstallation(r.User.Id, $"fid-{Guid.NewGuid():N}", DevicePlatform.Android, "1.0.0", null, now));
                members.Add(r.User);
            }

            await db.SaveChangesAsync();
        });

        // Ages past the domain's 12 h limit can only be produced by ageing the row afterwards.
        foreach (var (spec, member) in specs.Zip(members).Where(x => x.First.AgeHours > 11))
        {
            await Factory.WithDbAsync(db => db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE dbo.UserLocations SET CapturedAt = {now.AddHours(-spec.AgeHours)} WHERE UserId = {member.Id}"));
        }

        return (lead.User, members);
    }

    private async Task<Guid> SeedApprovedSosAsync(User lead, User requester)
    {
        var now = DateTime.UtcNow;
        return await Factory.WithDbAsync(async db =>
        {
            var tracked = await db.Users.SingleAsync(u => u.Id == requester.Id);
            var sos = SosRequest.Create(tracked, lead.Id, Guid.NewGuid(), SosCategory.Medical, "sql test", Origin, 10, now, now);
            sos.Approve(lead.Id, null, now);
            db.SosRequests.Add(sos);
            await db.SaveChangesAsync();
            return sos.Id;
        });
    }

    private Task DispatchAsync(Guid sosId) =>
        Factory.WithServiceAsync<DispatchSosAlertsJob, int>(async job => { await job.RunAsync(sosId, CancellationToken.None); return 0; });

    private Task<List<Guid>> RecipientsAsync(Guid sosId) =>
        Factory.WithDbAsync(db => db.SosNotifications.Where(n => n.SosRequestId == sosId).Select(n => n.RecipientUserId).ToListAsync());

    [SqlServerFact]
    public async Task Migrations_and_scripts_are_current_and_rerunnable()
    {
        await Factory.WithServiceAsync<DatabaseInitializer, int>(async init => { await init.InitializeAsync(); return 0; }); // second run: idempotent

        await Factory.WithDbAsync(async db =>
        {
            Assert.Empty(await db.Database.GetPendingMigrationsAsync());

            var objects = await db.Database.SqlQuery<string>($"""
                SELECT name AS Value FROM sys.objects WHERE type IN ('P', 'TR') AND is_ms_shipped = 0
                UNION ALL SELECT name FROM sys.spatial_indexes
                UNION ALL SELECT s.name + '.' + t.name FROM sys.tables t JOIN sys.schemas s ON s.schema_id = t.schema_id WHERE s.name = 'jobs'
                """).ToListAsync();
            foreach (var expected in new[]
                     {
                         "jobs.Job", "jobs.Server", // Hangfire, installed by the initializer (the app has no DDL rights in Azure)
                         "usp_SelectSosRecipients", "usp_GetDashboardStats", "TR_Users_ReferralImmutable", "TR_AuditLogs_AppendOnly",
                         "TR_ReferralHistory_AppendOnly", "TR_SosStatusHistory_AppendOnly", "SIX_UserLocations_Location",
                     })
            {
                Assert.Contains(expected, objects);
            }
        });
    }

    [SqlServerFact]
    public async Task Recipient_procedure_applies_every_eligibility_rule()
    {
        var (lead, members) = await SeedCommunityAsync(
            new NeighbourSpec(1_000),                      // 0 eligible
            new NeighbourSpec(4_900),                      // 1 eligible: just inside 5 km
            new NeighbourSpec(5_200),                      // 2 outside
            new NeighbourSpec(8_000),                      // 3 far
            new NeighbourSpec(500, Accuracy: 800),         // 4 inaccurate fix
            new NeighbourSpec(500, AgeHours: 13),          // 5 stale
            new NeighbourSpec(500, Sharing: false),        // 6 consent off
            new NeighbourSpec(500, Alerts: false),         // 7 alerts off
            new NeighbourSpec(500, Suspended: true),       // 8 suspended
            new NeighbourSpec(10));                        // 9 the requester
        var sosId = await SeedApprovedSosAsync(lead, members[9]);

        await DispatchAsync(sosId);

        var recipients = await RecipientsAsync(sosId);
        Assert.Equal(new[] { members[0].Id, members[1].Id }.Order(), recipients.Order());
        var distances = await Factory.WithDbAsync(db => db.SosNotifications.Where(n => n.SosRequestId == sosId).Select(n => n.DistanceMeters).ToListAsync());
        Assert.All(distances, d => Assert.InRange(d, 900, 5_000));
    }

    [SqlServerFact]
    public async Task Dispatch_is_idempotent_and_serialised_under_concurrency()
    {
        var (lead, members) = await SeedCommunityAsync(new NeighbourSpec(800), new NeighbourSpec(1_500), new NeighbourSpec(2_500), new NeighbourSpec(5));
        var sosId = await SeedApprovedSosAsync(lead, members[3]);
        var before = Factory.Push.Sent.Count;

        // Two approvals racing / a Hangfire retry overlapping the first run. A run that finds every recipient
        // claimed by another asks Hangfire to check again later (PushDeliveryException): expected here.
        static async Task Tolerant(Task run)
        {
            try
            {
                await run;
            }
            catch (PushDeliveryException)
            {
            }
        }

        await Task.WhenAll(Tolerant(DispatchAsync(sosId)), Tolerant(DispatchAsync(sosId)), Tolerant(DispatchAsync(sosId)));
        await DispatchAsync(sosId); // everything delivered by now: completes quietly

        Assert.Equal(3, (await RecipientsAsync(sosId)).Count);
        var pushed = Factory.Push.Sent.Skip(before).Where(s => s.Message.Type == "sos.alert").SelectMany(s => s.Targets).ToList();
        Assert.Equal(3, pushed.Count); // nobody alerted twice
        var count = await Factory.WithDbAsync(db => db.SosRequests.Where(s => s.Id == sosId).Select(s => s.RecipientCount).SingleAsync());
        Assert.Equal(3, count);
    }

    [SqlServerFact]
    public async Task Referrer_is_immutable_in_the_database()
    {
        var (lead, members) = await SeedCommunityAsync(new NeighbourSpec(100));

        var error = await Assert.ThrowsAsync<SqlException>(() => Factory.WithDbAsync(db => db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE dbo.Users SET ReferredByAdminId = {fixture.Master.Id} WHERE Id = {members[0].Id}")));
        Assert.Equal(51001, error.Number);
        var referrer = await Factory.WithDbAsync(db => db.Users.Where(u => u.Id == members[0].Id).Select(u => u.ReferredByAdminId).SingleAsync());
        Assert.Equal(lead.Id, referrer);
    }

    [SqlServerFact]
    public async Task Audit_log_is_append_only_except_through_the_purge_gate()
    {
        var old = DateTime.UtcNow.AddDays(-400);
        var marker = $"sql-audit-{Guid.NewGuid():N}";
        await Factory.WithDbAsync(async db =>
        {
            db.AuditLogs.Add(new AuditLog(null, "Test", "Test", marker, null, null, null, null, null, old));
            await db.SaveChangesAsync();
        });

        var blocked = await Assert.ThrowsAsync<SqlException>(() => Factory.WithDbAsync(db => db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM dbo.AuditLogs WHERE EntityId = {marker}")));
        Assert.Equal(51002, blocked.Number);
        var update = await Assert.ThrowsAsync<SqlException>(() => Factory.WithDbAsync(db => db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE dbo.AuditLogs SET Action = 'Tampered' WHERE EntityId = {marker}")));
        Assert.Equal(51002, update.Number);

        // The purger opens the gate for its own batch only...
        await Factory.WithDbAsync(async db =>
        {
            var purger = new AuditLogPurger(db);
            var deleted = await purger.DeleteBatchAsync(old.AddDays(1), 10_000, CancellationToken.None);
            Assert.True(deleted >= 1);
            Assert.False(await db.AuditLogs.AnyAsync(a => a.EntityId == marker));

            // ...and closes it again on the same connection.
            await db.Database.OpenConnectionAsync();
            db.AuditLogs.Add(new AuditLog(null, "Test", "Test", marker + "-2", null, null, null, null, null, DateTime.UtcNow));
            await db.SaveChangesAsync();
            var stillBlocked = await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM dbo.AuditLogs WHERE EntityId = {marker + "-2"}"));
            Assert.Equal(51002, stillBlocked.Number);
        });
    }

    [SqlServerFact]
    public async Task Constraints_hold_in_the_database()
    {
        var (_, members) = await SeedCommunityAsync(new NeighbourSpec(100));
        var userId = members[0].Id;

        // Only one Master Admin.
        await Assert.ThrowsAsync<DbUpdateException>(() => Factory.WithDbAsync(async db =>
        {
            db.Users.Add(User.CreateMasterAdmin(PhoneNumber.Create(ApiFactory.RandomPhone()), "Second master"));
            await db.SaveChangesAsync();
        }));

        // One active background-location key per phone; revoked ones do not count.
        await Factory.WithDbAsync(async db =>
        {
            var first = LocationKey.Issue(userId, new string('a', 64), "phone-1", DateTime.UtcNow);
            first.Revoke(DateTime.UtcNow, "replaced");
            db.LocationKeys.AddRange(first, LocationKey.Issue(userId, new string('b', 64), "phone-1", DateTime.UtcNow));
            await db.SaveChangesAsync();
        });
        await Assert.ThrowsAsync<DbUpdateException>(() => Factory.WithDbAsync(async db =>
        {
            db.LocationKeys.Add(LocationKey.Issue(userId, new string('c', 64), "phone-1", DateTime.UtcNow));
            await db.SaveChangesAsync();
        }));

        // A device needs exactly one push target.
        var target = await Assert.ThrowsAsync<SqlException>(() => Factory.WithDbAsync(db => db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO dbo.UserDevices (Id, UserId, FcmToken, InstallationId, Platform, IsActive, LastSeenAt, CreatedAt, UpdatedAt)
            VALUES ({Guid.NewGuid()}, {userId}, NULL, NULL, 1, 1, SYSUTCDATETIME(), SYSUTCDATETIME(), SYSUTCDATETIME())
            """)));
        Assert.Contains("CK_UserDevices_Target", target.Message, StringComparison.Ordinal);
    }

    [SqlServerFact]
    public async Task Dashboard_procedure_returns_consistent_counts()
    {
        var (lead, members) = await SeedCommunityAsync(new NeighbourSpec(100), new NeighbourSpec(200));
        await SeedApprovedSosAsync(lead, members[0]);

        var stats = await Factory.WithServiceAsync<IDashboardReader, Application.Features.Admin.DashboardStats>(
            reader => reader.GetAsync(lead.Id, null, null, CancellationToken.None));

        Assert.Equal(2, stats.TotalUsers);
        Assert.Equal(1, stats.TotalSos);
        Assert.Equal(1, stats.ApprovedSos);
        Assert.Equal(1, stats.ActiveSos);
    }

    /// <summary>
    /// Fan-out at community scale: 10,000 members spread over a 16 km square, half within 4.8 km of the SOS.
    /// Measures the spatial selection and the whole dispatch (FCM itself is faked).
    /// </summary>
    [SqlServerFact]
    public async Task Five_km_search_and_dispatch_scale_to_ten_thousand_members()
    {
        const int members = 10_000;
        var random = new Random(42);
        var specs = Enumerable.Range(0, members).Select(i =>
        {
            // Even i: inside (≤ 4.8 km); odd i: outside (5.3–8 km). Random bearing.
            var distance = i % 2 == 0 ? random.NextDouble() * 4_800 : 5_300 + (random.NextDouble() * 2_700);
            var bearing = random.NextDouble() * 2 * Math.PI;
            return (North: distance * Math.Cos(bearing), East: distance * Math.Sin(bearing));
        }).ToList();

        var seed = Stopwatch.StartNew();
        var (lead, requesterList) = await SeedCommunityAsync(new NeighbourSpec(0));
        await SeedBulkAsync(lead, specs);
        output.WriteLine($"Seeded {members} members in {seed.Elapsed.TotalSeconds:F1} s");

        var sosId = await SeedApprovedSosAsync(lead, requesterList[0]);

        var select = Stopwatch.StartNew();
        var deliveries = await Factory.WithServiceAsync<IAlertRecipientSelector, IReadOnlyList<AlertDelivery>>(
            s => s.SelectAsync(sosId, new AlertOptions(), CancellationToken.None));
        select.Stop();

        Assert.Equal(members / 2, deliveries.Select(d => d.RecipientUserId).Distinct().Count());
        output.WriteLine($"usp_SelectSosRecipients: {deliveries.Count} deliveries in {select.ElapsedMilliseconds} ms");

        // The timed selection above claimed every recipient, as a dispatch run would. Release the claims so the
        // dispatch below measures a normal run (in production selection and sending are one run).
        await Factory.WithDbAsync(db => db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE dbo.SosNotifications SET ClaimedUntil = NULL WHERE SosRequestId = {sosId}"));

        var dispatch = Stopwatch.StartNew();
        await DispatchAsync(sosId);
        dispatch.Stop();
        output.WriteLine($"DispatchSosAlertsJob (fake FCM): {dispatch.ElapsedMilliseconds} ms");

        var sent = await Factory.WithDbAsync(db => db.SosNotifications.CountAsync(n => n.SosRequestId == sosId && n.DeliveryStatus == DeliveryStatus.Sent));
        Assert.Equal(members / 2, sent);

        // Generous ceilings for shared CI runners; the printed timings are the real result.
        Assert.True(select.Elapsed < TimeSpan.FromSeconds(10), $"Selection took {select.Elapsed}");
        Assert.True(dispatch.Elapsed < TimeSpan.FromSeconds(60), $"Dispatch took {dispatch.Elapsed}");
    }

    /// <summary>Set-based seed for the scale test (EF change tracking would dominate the run time).</summary>
    private async Task SeedBulkAsync(User lead, IReadOnlyList<(double North, double East)> offsets)
    {
        var invitation = await Factory.WithDbAsync(db => db.Invitations.Where(i => i.CreatedByUserId == lead.Id).Select(i => i.Id).FirstAsync());
        const int batch = 1_000;
        // Sequential numbers in their own +917… range: random ones would collide (birthday problem) at this scale.
        var phoneBase = Random.Shared.NextInt64(0, 900_000_000);
        var sequence = 0L;
        for (var start = 0; start < offsets.Count; start += batch)
        {
            var slice = offsets.Skip(start).Take(batch).ToList();
            await Factory.WithDbAsync(async db =>
            {
                var memberInvite = await db.Invitations.SingleAsync(i => i.Id == invitation);
                var now = DateTime.UtcNow;
                foreach (var (north, east) in slice)
                {
                    var phone = PhoneNumber.Create($"+917{phoneBase + sequence++:D9}");
                    var r = memberInvite.Redeem(phone, $"uid-{Guid.NewGuid():N}", new RegistrationConsent(ApiFactory.PolicyVersion, true, null), now, null, null);
                    r.User.SetAlertsEnabled(true);
                    db.Users.Add(r.User);
                    db.ReferralHistory.Add(r.Referral);
                    db.UserConsents.Add(r.TermsConsent);
                    db.UserConsents.Add(r.User.SetLocationSharing(true, ApiFactory.PolicyVersion, now, null, null));
                    var point = new GeoCoordinate(
                        Origin.Latitude + (north / 111_195.0),
                        Origin.Longitude + (east / (111_195.0 * Math.Cos(Origin.Latitude * Math.PI / 180))));
                    db.UserLocations.Add(UserLocation.Create(r.User.Id, point, 30, now, now));
                    db.UserDevices.Add(UserDevice.RegisterInstallation(r.User.Id, $"fid-{Guid.NewGuid():N}", DevicePlatform.Android, "1.0.0", null, now));
                }

                await db.SaveChangesAsync();
            });
        }
    }
}
