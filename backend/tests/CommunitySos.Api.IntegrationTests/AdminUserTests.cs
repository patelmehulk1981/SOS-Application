using System.Net;
using System.Text.Json;
using CommunitySos.Domain.Entities;
using CommunitySos.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace CommunitySos.Api.IntegrationTests;

public sealed class AdminUserTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static IEnumerable<Guid> Ids(JsonElement page) =>
        page.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetGuid());

    [Fact]
    public async Task Sub_admin_sees_only_users_they_referred()
    {
        var master = await factory.SeedMasterAdminAsync();
        var subA = await factory.SeedInviteeAsync(master, "Lead A");
        var subB = await factory.SeedInviteeAsync(master, "Lead B");
        var mine = await factory.SeedInviteeAsync(subA, "Mine");
        var theirs = await factory.SeedInviteeAsync(subB, "Theirs");
        var client = factory.CreateClientFor(subA);

        var page = await (await client.GetAsync("/api/v1/admin/users")).ExpectAsync(HttpStatusCode.OK);

        Assert.Equal([mine.Id], Ids(page));
        await (await client.GetAsync($"/api/v1/admin/users/{theirs.Id}")).ExpectAsync(HttpStatusCode.NotFound);
        await (await client.GetAsync($"/api/v1/admin/users/{subB.Id}")).ExpectAsync(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Master_sees_everyone_and_can_filter_by_referrer_role_and_phone()
    {
        var master = await factory.SeedMasterAdminAsync();
        var sub = await factory.SeedInviteeAsync(master, "Filter Lead");
        var member = await factory.SeedInviteeAsync(sub, "Filter Member");
        var client = factory.CreateClientFor(master);

        var byReferrer = await (await client.GetAsync($"/api/v1/admin/users?referredBy={sub.Id}")).ExpectAsync(HttpStatusCode.OK);
        Assert.Equal([member.Id], Ids(byReferrer));

        var subAdmins = await (await client.GetAsync("/api/v1/admin/users?role=SubAdmin&pageSize=100")).ExpectAsync(HttpStatusCode.OK);
        Assert.Contains(sub.Id, Ids(subAdmins));
        Assert.DoesNotContain(member.Id, Ids(subAdmins));

        var localDigits = member.PhoneNumber.Value[3..]; // "+91" stripped → 10-digit local number
        var byPhone = await (await client.GetAsync($"/api/v1/admin/users?search={localDigits}")).ExpectAsync(HttpStatusCode.OK);
        Assert.Equal([member.Id], Ids(byPhone));

        var byName = await (await client.GetAsync("/api/v1/admin/users?search=Filter%20Mem")).ExpectAsync(HttpStatusCode.OK);
        Assert.Equal([member.Id], Ids(byName));
    }

    [Fact]
    public async Task User_detail_shows_referral_and_counts_but_no_health_data()
    {
        var master = await factory.SeedMasterAdminAsync();
        var sub = await factory.SeedInviteeAsync(master, "Detail Lead");
        await factory.SeedInviteeAsync(sub, "One");
        await factory.SeedInviteeAsync(sub, "Two");

        var detail = await (await factory.CreateClientFor(master).GetAsync($"/api/v1/admin/users/{sub.Id}")).ExpectAsync(HttpStatusCode.OK);

        Assert.Equal(2, detail.GetProperty("user").GetProperty("referredUsersCount").GetInt32());
        Assert.Equal(master.Id, detail.GetProperty("user").GetProperty("referredBy").GetProperty("id").GetGuid());
        Assert.True(detail.TryGetProperty("referral", out _));
        Assert.False(detail.GetProperty("user").TryGetProperty("bloodGroup", out _));
    }

    [Fact]
    public async Task Master_suspends_user_which_revokes_sessions_and_reactivates()
    {
        var master = await factory.SeedMasterAdminAsync();
        var sub = await factory.SeedInviteeAsync(master, "Suspend Lead");
        var member = await factory.SeedInviteeAsync(sub, "Suspend Me");
        await factory.WithDbAsync(async db =>
        {
            db.RefreshTokens.Add(RefreshToken.Issue(member.Id, new string('a', 64), Guid.NewGuid(), null, DateTime.UtcNow, TimeSpan.FromDays(30)));
            await db.SaveChangesAsync();
        });
        var client = factory.CreateClientFor(master);

        var suspended = await (await client.PutJsonAsync($"/api/v1/admin/users/{member.Id}/status", new { status = "Suspended", reason = "Spam SOS" }))
            .ExpectAsync(HttpStatusCode.OK);
        Assert.Equal("Suspended", suspended.GetProperty("user").GetProperty("status").GetString());
        Assert.False(await factory.WithDbAsync(db => db.RefreshTokens.AnyAsync(t => t.UserId == member.Id && t.RevokedAt == null)));
        Assert.True(await factory.WithDbAsync(db => db.AuditLogs.AnyAsync(a => a.Action == "UserSuspended" && a.EntityId == member.Id.ToString())));

        var active = await (await client.PutJsonAsync($"/api/v1/admin/users/{member.Id}/status", new { status = "Active" })).ExpectAsync(HttpStatusCode.OK);
        Assert.Equal("Active", active.GetProperty("user").GetProperty("status").GetString());
    }

    [Fact]
    public async Task Suspension_requires_a_reason_and_master_role()
    {
        var master = await factory.SeedMasterAdminAsync();
        var sub = await factory.SeedInviteeAsync(master, "Reason Lead");
        var member = await factory.SeedInviteeAsync(sub, "Reason Member");

        var noReason = await (await factory.CreateClientFor(master).PutJsonAsync($"/api/v1/admin/users/{member.Id}/status", new { status = "Suspended" }))
            .ExpectAsync(HttpStatusCode.BadRequest);
        Assert.True(noReason.GetProperty("errors").TryGetProperty("reason", out _));

        await (await factory.CreateClientFor(sub).PutJsonAsync($"/api/v1/admin/users/{member.Id}/status", new { status = "Suspended", reason = "x" }))
            .ExpectAsync(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Master_cannot_suspend_itself()
    {
        var master = await factory.SeedMasterAdminAsync();

        var problem = await (await factory.CreateClientFor(master).PutJsonAsync($"/api/v1/admin/users/{master.Id}/status", new { status = "Suspended", reason = "x" }))
            .ExpectAsync(HttpStatusCode.UnprocessableEntity);

        Assert.Equal("user.cannot_change_own_status", problem.Code());
    }

    [Fact]
    public async Task Normal_user_cannot_access_admin_endpoints()
    {
        var master = await factory.SeedMasterAdminAsync();
        var member = await factory.SeedInviteeAsync(await factory.SeedInviteeAsync(master, "L"), "M");
        var client = factory.CreateClientFor(member);

        await (await client.GetAsync("/api/v1/admin/users")).ExpectAsync(HttpStatusCode.Forbidden);
        await (await client.GetAsync("/api/v1/admin/referrals")).ExpectAsync(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Referral_history_is_scoped_and_tree_summarises_each_sub_admin()
    {
        var master = await factory.SeedMasterAdminAsync();
        var sub = await factory.SeedInviteeAsync(master, "Tree Lead");
        var m1 = await factory.SeedInviteeAsync(sub, "Tree One");
        var m2 = await factory.SeedInviteeAsync(sub, "Tree Two");
        await factory.WithDbAsync(async db =>
        {
            (await db.Users.SingleAsync(u => u.Id == m2.Id)).Suspend();
            await db.SaveChangesAsync();
        });

        var history = await (await factory.CreateClientFor(sub).GetAsync("/api/v1/admin/referrals")).ExpectAsync(HttpStatusCode.OK);
        var referred = history.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("referredUser").GetProperty("id").GetGuid()).ToList();
        Assert.Equal(2, referred.Count);
        Assert.Contains(m1.Id, referred);

        await (await factory.CreateClientFor(sub).GetAsync("/api/v1/admin/referrals/tree")).ExpectAsync(HttpStatusCode.Forbidden);

        var tree = await (await factory.CreateClientFor(master).GetAsync("/api/v1/admin/referrals/tree")).ExpectAsync(HttpStatusCode.OK);
        var node = tree.GetProperty("subAdmins").EnumerateArray().Single(n => n.GetProperty("subAdmin").GetProperty("id").GetGuid() == sub.Id);
        Assert.Equal(2, node.GetProperty("referredUsers").GetInt32());
        Assert.Equal(1, node.GetProperty("activeUsers").GetInt32());
        Assert.Equal(master.Id, tree.GetProperty("root").GetProperty("id").GetGuid());
    }
}
