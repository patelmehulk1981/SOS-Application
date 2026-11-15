using System.Net;
using Microsoft.EntityFrameworkCore;

namespace CommunitySos.Api.IntegrationTests;

public sealed class InvitationTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Master_link_invites_sub_admins_and_sub_admin_link_invites_users()
    {
        var master = await factory.SeedMasterAdminAsync();
        var sub = await factory.SeedInviteeAsync(master, "Ward Lead");

        var masterLink = await (await factory.CreateClientFor(master).PostJsonAsync("/api/v1/invitations", new { label = "Leads" }))
            .ExpectAsync(HttpStatusCode.Created);
        var subLink = await (await factory.CreateClientFor(sub).PostJsonAsync("/api/v1/invitations", new { label = "Sector 21", maxUses = 50 }))
            .ExpectAsync(HttpStatusCode.Created);

        Assert.Equal("SubAdmin", masterLink.GetProperty("inviteeRole").GetString());
        Assert.Equal("User", subLink.GetProperty("inviteeRole").GetString());
        Assert.Equal("Active", subLink.GetProperty("status").GetString());
        var code = subLink.GetProperty("code").GetString()!;
        Assert.Equal($"https://sos.example.com/i/{code}", subLink.GetProperty("link").GetString());
    }

    [Fact]
    public async Task Normal_user_cannot_create_invitations()
    {
        var master = await factory.SeedMasterAdminAsync();
        var member = await factory.SeedInviteeAsync(await factory.SeedInviteeAsync(master, "Lead"), "Member");

        var response = await factory.CreateClientFor(member).PostJsonAsync("/api/v1/invitations", new { });

        await response.ExpectAsync(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Public_preview_shows_inviter_and_hides_it_once_revoked()
    {
        var master = await factory.SeedMasterAdminAsync();
        var sub = await factory.SeedInviteeAsync(master, "Mehta Ward Lead");
        var subClient = factory.CreateClientFor(sub);
        var created = await (await subClient.PostJsonAsync("/api/v1/invitations", new { })).ExpectAsync(HttpStatusCode.Created);
        var code = created.GetProperty("code").GetString()!;
        var anonymous = factory.CreateClient();

        var preview = await (await anonymous.GetAsync($"/api/v1/invitations/{code.ToLowerInvariant()}")).ExpectAsync(HttpStatusCode.OK);
        Assert.Equal("Active", preview.GetProperty("status").GetString());
        Assert.Equal("Mehta Ward Lead", preview.GetProperty("invitedBy").GetProperty("fullName").GetString());
        Assert.Equal("User", preview.GetProperty("inviteeRole").GetString());

        await (await subClient.DeleteAsync($"/api/v1/invitations/{created.GetProperty("id").GetGuid()}")).ExpectAsync(HttpStatusCode.NoContent);

        var revoked = await (await anonymous.GetAsync($"/api/v1/invitations/{code}")).ExpectAsync(HttpStatusCode.OK);
        Assert.Equal("Revoked", revoked.GetProperty("status").GetString());
        Assert.False(revoked.TryGetProperty("invitedBy", out _));
    }

    [Fact]
    public async Task Suspended_inviters_links_stop_working()
    {
        var master = await factory.SeedMasterAdminAsync();
        var sub = await factory.SeedInviteeAsync(master, "Soon Suspended");
        var created = await (await factory.CreateClientFor(sub).PostJsonAsync("/api/v1/invitations", new { })).ExpectAsync(HttpStatusCode.Created);

        await (await factory.CreateClientFor(master).PutJsonAsync($"/api/v1/admin/users/{sub.Id}/status", new { status = "Suspended", reason = "Misuse" }))
            .ExpectAsync(HttpStatusCode.OK);

        var preview = await (await factory.CreateClient().GetAsync($"/api/v1/invitations/{created.GetProperty("code").GetString()}"))
            .ExpectAsync(HttpStatusCode.OK);
        Assert.Equal("Revoked", preview.GetProperty("status").GetString());
    }

    [Theory]
    [InlineData("DOESNOTEXI")]
    [InlineData("bad!code")]
    public async Task Unknown_or_malformed_code_is_404(string code)
    {
        var problem = await (await factory.CreateClient().GetAsync($"/api/v1/invitations/{code}")).ExpectAsync(HttpStatusCode.NotFound);

        Assert.Equal("invitation.not_found", problem.Code());
    }

    [Fact]
    public async Task Sub_admin_cannot_revoke_someone_elses_link_but_master_can()
    {
        var master = await factory.SeedMasterAdminAsync();
        var subA = await factory.SeedInviteeAsync(master, "A");
        var subB = await factory.SeedInviteeAsync(master, "B");
        var link = await (await factory.CreateClientFor(subA).PostJsonAsync("/api/v1/invitations", new { })).ExpectAsync(HttpStatusCode.Created);
        var id = link.GetProperty("id").GetGuid();

        await (await factory.CreateClientFor(subB).DeleteAsync($"/api/v1/invitations/{id}")).ExpectAsync(HttpStatusCode.NotFound);
        await (await factory.CreateClientFor(master).DeleteAsync($"/api/v1/invitations/{id}")).ExpectAsync(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task List_returns_only_my_links_and_filters_by_status()
    {
        var master = await factory.SeedMasterAdminAsync();
        var sub = await factory.SeedInviteeAsync(master, "Lister");
        var client = factory.CreateClientFor(sub);
        var keep = await (await client.PostJsonAsync("/api/v1/invitations", new { label = "keep" })).ExpectAsync(HttpStatusCode.Created);
        var drop = await (await client.PostJsonAsync("/api/v1/invitations", new { label = "drop" })).ExpectAsync(HttpStatusCode.Created);
        await client.DeleteAsync($"/api/v1/invitations/{drop.GetProperty("id").GetGuid()}");
        await (await factory.CreateClientFor(master).PostJsonAsync("/api/v1/invitations", new { label = "not mine" })).ExpectAsync(HttpStatusCode.Created);

        var all = await (await client.GetAsync("/api/v1/invitations")).ExpectAsync(HttpStatusCode.OK);
        var active = await (await client.GetAsync("/api/v1/invitations?status=Active")).ExpectAsync(HttpStatusCode.OK);

        Assert.Equal(2, all.GetProperty("totalCount").GetInt32());
        Assert.Equal(1, active.GetProperty("totalCount").GetInt32());
        Assert.Equal(keep.GetProperty("id").GetGuid(), active.GetProperty("items")[0].GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task Past_expiry_fails_validation()
    {
        var master = await factory.SeedMasterAdminAsync();

        var problem = await (await factory.CreateClientFor(master).PostJsonAsync("/api/v1/invitations", new { expiresAt = DateTime.UtcNow.AddDays(-1) }))
            .ExpectAsync(HttpStatusCode.BadRequest);

        Assert.True(problem.GetProperty("errors").TryGetProperty("expiresAt", out _));
    }

    [Fact]
    public async Task Registration_through_created_link_is_recorded_against_that_link()
    {
        var master = await factory.SeedMasterAdminAsync();
        var sub = await factory.SeedInviteeAsync(master, "Recorder");
        var link = await (await factory.CreateClientFor(sub).PostJsonAsync("/api/v1/invitations", new { label = "Society gate" }))
            .ExpectAsync(HttpStatusCode.Created);

        await (await factory.CreateClient().PostJsonAsync("/api/v1/auth/login", new
        {
            firebaseIdToken = FakeFirebase.TokenFor($"r-{Guid.NewGuid():N}", ApiFactory.RandomPhone()),
            inviteCode = link.GetProperty("code").GetString(),
            consent = new { policyVersion = ApiFactory.PolicyVersion, confirmedAdult = true },
        })).ExpectAsync(HttpStatusCode.OK);

        var invitationId = link.GetProperty("id").GetGuid();
        var (used, referrals) = await factory.WithDbAsync(async db => (
            (await db.Invitations.SingleAsync(i => i.Id == invitationId)).UsedCount,
            await db.ReferralHistory.CountAsync(r => r.InvitationId == invitationId)));
        Assert.Equal(1, used);
        Assert.Equal(1, referrals);
    }
}
