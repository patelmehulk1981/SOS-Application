using System.Net;
using CommunitySos.Application.Common.Options;
using CommunitySos.Application.Features.Sos.Jobs;
using CommunitySos.Domain.Entities;
using CommunitySos.Domain.Enums;
using CommunitySos.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace CommunitySos.Api.IntegrationTests;

public sealed class AdminReviewTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private sealed record Community(User Master, User SubAdmin, User Member);

    private async Task<Community> NewCommunityAsync(bool withDevices = true)
    {
        var master = await factory.SeedMasterAdminAsync();
        var sub = await factory.SeedInviteeAsync(master, "Review Lead");
        var member = await factory.SeedInviteeAsync(sub, "Asha Patel");
        if (withDevices)
        {
            await factory.WithDbAsync(async db =>
            {
                foreach (var u in new[] { master, sub, member })
                {
                    db.UserDevices.Add(UserDevice.Register(u.Id, $"tok-{u.Id:N}", DevicePlatform.Android, "1.0.0", null, DateTime.UtcNow));
                }

                await db.SaveChangesAsync();
            });
        }

        return new Community(master, sub, member);
    }

    private async Task<Guid> RaiseAsync(User requester, double lat = 23.0225, double lng = 72.5714)
    {
        var body = new { clientRequestId = Guid.NewGuid(), category = "Medical", latitude = lat, longitude = lng, capturedAt = DateTime.UtcNow };
        var sos = await (await factory.CreateClientFor(requester).PostJsonAsync("/api/v1/sos", body)).ExpectAsync(HttpStatusCode.Created);
        return sos.GetProperty("id").GetGuid();
    }

    private bool Pushed(string type, User to) => factory.Push.Deliveries.Contains((type, $"tok-{to.Id:N}"));

    private Task<SosStatus> StatusOf(Guid sosId) => factory.WithDbAsync(db => db.SosRequests.Where(s => s.Id == sosId).Select(s => s.Status).SingleAsync());

    [Fact]
    public async Task New_sos_pushes_the_assigned_admin_and_schedules_escalation()
    {
        var c = await NewCommunityAsync();
        var sosId = await RaiseAsync(c.Member);

        Assert.Contains($"EscalateSosJob.RunAsync@{TimeSpan.FromMinutes(3)}", factory.Jobs.Scheduled);
        await factory.Jobs.RunAllAsync();

        Assert.True(Pushed("sos.review_requested", c.SubAdmin));
        var push = factory.Push.Sent.First(p => p.Message.Type == "sos.review_requested" && p.Message.Data["sosId"] == sosId.ToString());
        Assert.Equal("sos-admin", push.Message.AndroidChannelId);
        Assert.DoesNotContain("Asha", push.Message.Body + push.Message.Title, StringComparison.Ordinal); // no names on the lock screen
    }

    [Fact]
    public async Task Approve_notifies_requester_and_cannot_be_repeated()
    {
        var c = await NewCommunityAsync();
        var sosId = await RaiseAsync(c.Member);
        var admin = factory.CreateClientFor(c.SubAdmin);

        var approved = await (await admin.PostJsonAsync($"/api/v1/admin/sos/{sosId}/approve", new { remarks = "Called and verified" }))
            .ExpectAsync(HttpStatusCode.OK);
        Assert.Equal("Approved", approved.GetProperty("status").GetString());
        Assert.Equal("Admin", approved.GetProperty("viewerRole").GetString());

        var again = await (await admin.PostJsonAsync($"/api/v1/admin/sos/{sosId}/approve", new { })).ExpectAsync(HttpStatusCode.UnprocessableEntity);
        Assert.Equal("sos.already_reviewed", again.Code());

        await factory.Jobs.RunAllAsync();
        Assert.True(Pushed("sos.approved", c.Member));
    }

    [Fact]
    public async Task Reject_requires_remarks_and_notifies_requester()
    {
        var c = await NewCommunityAsync();
        var sosId = await RaiseAsync(c.Member);
        var admin = factory.CreateClientFor(c.SubAdmin);

        var missing = await (await admin.PostJsonAsync($"/api/v1/admin/sos/{sosId}/reject", new { remarks = "" })).ExpectAsync(HttpStatusCode.BadRequest);
        Assert.True(missing.GetProperty("errors").TryGetProperty("remarks", out _));

        var rejected = await (await admin.PostJsonAsync($"/api/v1/admin/sos/{sosId}/reject", new { remarks = "Duplicate of SOS-00000041" }))
            .ExpectAsync(HttpStatusCode.OK);
        Assert.Equal("Rejected", rejected.GetProperty("status").GetString());

        await factory.Jobs.RunAllAsync();
        Assert.True(Pushed("sos.rejected", c.Member));
    }

    [Fact]
    public async Task Only_the_assigned_sub_admin_or_master_can_review()
    {
        var c = await NewCommunityAsync();
        var otherSub = await factory.SeedInviteeAsync(c.Master, "Other Lead");
        var sosId = await RaiseAsync(c.Member);

        await (await factory.CreateClientFor(otherSub).PostJsonAsync($"/api/v1/admin/sos/{sosId}/approve", new { })).ExpectAsync(HttpStatusCode.NotFound);
        await (await factory.CreateClientFor(c.Member).PostJsonAsync($"/api/v1/admin/sos/{sosId}/approve", new { })).ExpectAsync(HttpStatusCode.Forbidden);
        await (await factory.CreateClientFor(c.Master).PostJsonAsync($"/api/v1/admin/sos/{sosId}/approve", new { })).ExpectAsync(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Unreviewed_sos_escalates_to_master_who_can_then_approve()
    {
        var c = await NewCommunityAsync();
        var sosId = await RaiseAsync(c.Member);

        await factory.Jobs.RunAllAsync(); // notify + escalation (delay elapsed)

        Assert.Equal(SosStatus.Escalated, await StatusOf(sosId));
        Assert.True(Pushed("sos.escalated", c.Master));

        await (await factory.CreateClientFor(c.Master).PostJsonAsync($"/api/v1/admin/sos/{sosId}/approve", new { remarks = "Approved on escalation" }))
            .ExpectAsync(HttpStatusCode.OK);
        Assert.Equal(SosStatus.Approved, await StatusOf(sosId));
    }

    [Fact]
    public async Task Escalation_does_nothing_if_the_sos_was_already_reviewed()
    {
        var c = await NewCommunityAsync();
        var sosId = await RaiseAsync(c.Member);
        await (await factory.CreateClientFor(c.SubAdmin).PostJsonAsync($"/api/v1/admin/sos/{sosId}/approve", new { })).ExpectAsync(HttpStatusCode.OK);

        await factory.Jobs.RunAllAsync();

        Assert.Equal(SosStatus.Approved, await StatusOf(sosId));
        Assert.DoesNotContain(factory.Push.Sent, p => p.Message.Type == "sos.escalated" && p.Message.Data["sosId"] == sosId.ToString());
    }

    [Fact]
    public async Task Sub_admin_auto_approved_sos_informs_master()
    {
        var c = await NewCommunityAsync();
        await factory.Jobs.RunAllAsync(); // drain jobs left by earlier tests in this class
        var sosId = await RaiseAsync(c.SubAdmin);

        Assert.DoesNotContain(factory.Jobs.Scheduled, j => j.StartsWith("EscalateSosJob", StringComparison.Ordinal)); // nothing to escalate
        await factory.Jobs.RunAllAsync();

        Assert.Equal(SosStatus.Approved, await StatusOf(sosId));
        Assert.True(Pushed("sos.auto_approved", c.Master));
    }

    [Fact]
    public async Task Review_queue_is_scoped_and_oldest_first()
    {
        var c = await NewCommunityAsync(withDevices: false);
        var second = await factory.SeedInviteeAsync(c.SubAdmin, "Second Member");
        var firstSos = await RaiseAsync(c.Member);
        var secondSos = await RaiseAsync(second);
        var otherSub = await factory.SeedInviteeAsync(c.Master, "Unrelated Lead");
        await RaiseAsync(await factory.SeedInviteeAsync(otherSub, "Unrelated Member"));

        var queue = await (await factory.CreateClientFor(c.SubAdmin).GetAsync("/api/v1/admin/sos?awaitingReview=true")).ExpectAsync(HttpStatusCode.OK);
        var ids = queue.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetGuid()).ToList();

        Assert.Equal([firstSos, secondSos], ids);
        var item = queue.GetProperty("items")[0];
        Assert.Equal(c.Member.PhoneNumber.Value, item.GetProperty("requesterPhone").GetString());
        Assert.Equal(c.SubAdmin.Id, item.GetProperty("assignedAdmin").GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task Dashboard_counts_are_scoped_to_the_sub_admin()
    {
        var c = await NewCommunityAsync(withDevices: false);
        var m2 = await factory.SeedInviteeAsync(c.SubAdmin, "Dash Two");
        var s1 = await RaiseAsync(c.Member);
        var s2 = await RaiseAsync(m2);
        var admin = factory.CreateClientFor(c.SubAdmin);
        await admin.PostJsonAsync($"/api/v1/admin/sos/{s1}/approve", new { });
        await admin.PostJsonAsync($"/api/v1/admin/sos/{s2}/reject", new { remarks = "Test" });

        var stats = await (await admin.GetAsync("/api/v1/admin/dashboard")).ExpectAsync(HttpStatusCode.OK);

        Assert.Equal(2, stats.GetProperty("totalUsers").GetInt32());
        Assert.Equal(2, stats.GetProperty("referredUsers").GetInt32());
        Assert.Equal(2, stats.GetProperty("totalSos").GetInt32());
        Assert.Equal(1, stats.GetProperty("approvedSos").GetInt32());
        Assert.Equal(1, stats.GetProperty("rejectedSos").GetInt32());
        Assert.Equal(0, stats.GetProperty("pendingSos").GetInt32());
        Assert.True(stats.TryGetProperty("avgReviewSeconds", out _));

        var global = await (await factory.CreateClientFor(c.Master).GetAsync("/api/v1/admin/dashboard")).ExpectAsync(HttpStatusCode.OK);
        Assert.True(global.GetProperty("totalSos").GetInt32() >= 2);
        Assert.True(global.GetProperty("subAdmins").GetInt32() >= 1);
    }

    [Fact]
    public async Task Map_returns_only_pins_inside_the_bounding_box()
    {
        var c = await NewCommunityAsync(withDevices: false);
        var m2 = await factory.SeedInviteeAsync(c.SubAdmin, "Far Away");
        var near = await RaiseAsync(c.Member, 23.0225, 72.5714);   // Ahmedabad
        var far = await RaiseAsync(m2, 19.0760, 72.8777);          // Mumbai

        var map = await (await factory.CreateClientFor(c.SubAdmin)
                .GetAsync("/api/v1/admin/sos/map?minLatitude=22.9&maxLatitude=23.2&minLongitude=72.4&maxLongitude=72.7"))
            .ExpectAsync(HttpStatusCode.OK);

        var ids = map.GetProperty("points").EnumerateArray().Select(p => p.GetProperty("id").GetGuid()).ToList();
        Assert.Contains(near, ids);
        Assert.DoesNotContain(far, ids);
        Assert.False(map.GetProperty("truncated").GetBoolean());
    }

    [Fact]
    public async Task Invalid_device_tokens_are_deactivated_after_a_push()
    {
        var c = await NewCommunityAsync(withDevices: false);
        await factory.WithDbAsync(async db =>
        {
            db.UserDevices.Add(UserDevice.Register(c.SubAdmin.Id, $"dead-{Guid.NewGuid():N}", DevicePlatform.Ios, null, null, DateTime.UtcNow));
            await db.SaveChangesAsync();
        });

        await RaiseAsync(c.Member);
        await factory.Jobs.RunAllAsync();

        var active = await factory.WithDbAsync(db => db.UserDevices.Where(d => d.UserId == c.SubAdmin.Id).Select(d => d.IsActive).SingleAsync());
        Assert.False(active);
    }

    [Fact]
    public async Task Stale_sos_are_expired_by_the_recurring_job()
    {
        var c = await NewCommunityAsync(withDevices: false);
        var sosId = await RaiseAsync(c.Member);
        Assert.Equal(ExpireStaleSosJob.Cron, factory.Jobs.Recurring[ExpireStaleSosJob.RecurringId]);

        await factory.WithDbAsync(async db =>
        {
            var job = new ExpireStaleSosJob(
                db,
                Microsoft.Extensions.Options.Options.Create(new SosOptions()),
                factory.Jobs,
                new FixedTimeProvider(DateTimeOffset.UtcNow.AddHours(25)),
                NullLogger<ExpireStaleSosJob>.Instance);
            await job.RunAsync(CancellationToken.None);
        });

        Assert.Equal(SosStatus.Expired, await StatusOf(sosId));
        var lastEntry = await factory.WithDbAsync(db => db.SosStatusHistory.Where(h => h.SosRequestId == sosId).OrderByDescending(h => h.Id).FirstAsync());
        Assert.Null(lastEntry.ChangedByUserId); // system
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
