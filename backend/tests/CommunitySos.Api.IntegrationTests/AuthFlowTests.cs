using System.Net;
using CommunitySos.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace CommunitySos.Api.IntegrationTests;

public sealed class AuthFlowTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static object LoginBody(string uid, string phone, string? inviteCode = null, bool withConsent = true, bool adult = true) => new
    {
        firebaseIdToken = FakeFirebase.TokenFor(uid, phone),
        inviteCode,
        consent = withConsent ? new { policyVersion = ApiFactory.PolicyVersion, confirmedAdult = adult } : null,
        deviceId = "test-device",
    };

    [Fact]
    public async Task Sub_admin_invite_registers_a_user_mapped_to_that_sub_admin()
    {
        var master = await factory.SeedMasterAdminAsync();
        var masterInvite = await factory.SeedInvitationAsync(master);
        var client = factory.CreateClient();

        // Sub-Admin joins through the Master Admin's link.
        var subPhone = ApiFactory.RandomPhone();
        var sub = await (await client.PostJsonAsync("/api/v1/auth/login", LoginBody("sub-1", subPhone, masterInvite.Code)))
            .ExpectAsync(HttpStatusCode.OK);
        Assert.True(sub.GetProperty("isNewUser").GetBoolean());
        Assert.Equal("SubAdmin", sub.GetProperty("user").GetProperty("role").GetString());
        Assert.False(sub.GetProperty("user").GetProperty("policyAcceptanceRequired").GetBoolean());
        var subId = sub.GetProperty("user").GetProperty("id").GetGuid();

        // User joins through the Sub-Admin's link.
        var subUser = await factory.WithDbAsync(db => db.Users.SingleAsync(u => u.Id == subId));
        var subInvite = await factory.SeedInvitationAsync(subUser);
        var member = await (await client.PostJsonAsync("/api/v1/auth/login", LoginBody("user-1", ApiFactory.RandomPhone(), subInvite.Code)))
            .ExpectAsync(HttpStatusCode.OK);
        Assert.Equal("User", member.GetProperty("user").GetProperty("role").GetString());

        var memberId = member.GetProperty("user").GetProperty("id").GetGuid();
        var (referredBy, referral, consentCount) = await factory.WithDbAsync(async db => (
            (await db.Users.SingleAsync(u => u.Id == memberId)).ReferredByAdminId,
            await db.ReferralHistory.SingleAsync(r => r.ReferredUserId == memberId),
            await db.UserConsents.CountAsync(c => c.UserId == memberId)));

        Assert.Equal(subId, referredBy);
        Assert.Equal(subId, referral.ReferrerAdminId);
        Assert.Equal(subInvite.Id, referral.InvitationId);
        Assert.Equal(1, consentCount);
    }

    [Fact]
    public async Task Existing_user_signs_in_and_keeps_original_referrer_even_with_another_invite()
    {
        var master = await factory.SeedMasterAdminAsync();
        var invite = await factory.SeedInvitationAsync(master);
        var otherInvite = await factory.SeedInvitationAsync(await factory.SeedMasterAdminAsyncSafe());
        var phone = ApiFactory.RandomPhone();
        var client = factory.CreateClient();

        await (await client.PostJsonAsync("/api/v1/auth/login", LoginBody("u-keep", phone, invite.Code))).ExpectAsync(HttpStatusCode.OK);
        var again = await (await client.PostJsonAsync("/api/v1/auth/login", LoginBody("u-keep", phone, otherInvite.Code)))
            .ExpectAsync(HttpStatusCode.OK);

        Assert.False(again.GetProperty("isNewUser").GetBoolean());
        var userId = again.GetProperty("user").GetProperty("id").GetGuid();
        Assert.Equal(master.Id, await factory.WithDbAsync(async db => (await db.Users.SingleAsync(u => u.Id == userId)).ReferredByAdminId));
    }

    [Fact]
    public async Task Unknown_number_without_invite_is_asked_to_register()
    {
        var problem = await (await factory.CreateClient().PostJsonAsync("/api/v1/auth/login", LoginBody("nobody", ApiFactory.RandomPhone(), withConsent: false)))
            .ExpectAsync(HttpStatusCode.UnprocessableEntity);

        Assert.Equal("auth.registration_required", problem.Code());
    }

    [Fact]
    public async Task Minor_cannot_register()
    {
        var invite = await factory.SeedInvitationAsync(await factory.SeedMasterAdminAsyncSafe());

        var problem = await (await factory.CreateClient().PostJsonAsync("/api/v1/auth/login", LoginBody("minor", ApiFactory.RandomPhone(), invite.Code, adult: false)))
            .ExpectAsync(HttpStatusCode.UnprocessableEntity);

        Assert.Equal("registration.adult_required", problem.Code());
    }

    [Fact]
    public async Task Exhausted_invitation_is_rejected()
    {
        var invite = await factory.SeedInvitationAsync(await factory.SeedMasterAdminAsyncSafe(), maxUses: 1);
        var client = factory.CreateClient();

        await (await client.PostJsonAsync("/api/v1/auth/login", LoginBody("first", ApiFactory.RandomPhone(), invite.Code))).ExpectAsync(HttpStatusCode.OK);
        var problem = await (await client.PostJsonAsync("/api/v1/auth/login", LoginBody("second", ApiFactory.RandomPhone(), invite.Code)))
            .ExpectAsync(HttpStatusCode.UnprocessableEntity);

        Assert.Equal("invitation.unusable", problem.Code());
    }

    [Fact]
    public async Task Malformed_invite_code_fails_validation()
    {
        var problem = await (await factory.CreateClient().PostJsonAsync("/api/v1/auth/login", LoginBody("x", ApiFactory.RandomPhone(), "bad!")))
            .ExpectAsync(HttpStatusCode.BadRequest);

        Assert.Equal("validation_failed", problem.Code());
        Assert.True(problem.GetProperty("errors").TryGetProperty("inviteCode", out _));
    }

    [Fact]
    public async Task Refresh_rotates_and_reuse_of_old_token_ends_the_session()
    {
        var invite = await factory.SeedInvitationAsync(await factory.SeedMasterAdminAsyncSafe());
        var client = factory.CreateClient();
        var login = await (await client.PostJsonAsync("/api/v1/auth/login", LoginBody("rot", ApiFactory.RandomPhone(), invite.Code)))
            .ExpectAsync(HttpStatusCode.OK);
        var first = login.GetProperty("refreshToken").GetString()!;

        var rotated = await (await client.PostJsonAsync("/api/v1/auth/refresh", new { refreshToken = first })).ExpectAsync(HttpStatusCode.OK);
        var second = rotated.GetProperty("refreshToken").GetString()!;
        Assert.NotEqual(first, second);

        // Replaying the first token = theft signal: 401 and the new token dies too.
        var reuse = await (await client.PostJsonAsync("/api/v1/auth/refresh", new { refreshToken = first })).ExpectAsync(HttpStatusCode.Unauthorized);
        Assert.Equal("auth.refresh_reused", reuse.Code());

        var afterReuse = await (await client.PostJsonAsync("/api/v1/auth/refresh", new { refreshToken = second })).ExpectAsync(HttpStatusCode.Unauthorized);
        Assert.Equal("auth.invalid_refresh_token", afterReuse.Code());
    }

    [Fact]
    public async Task Logout_revokes_refresh_token_and_device()
    {
        var invite = await factory.SeedInvitationAsync(await factory.SeedMasterAdminAsyncSafe());
        var anonymous = factory.CreateClient();
        var login = await (await anonymous.PostJsonAsync("/api/v1/auth/login", LoginBody("out", ApiFactory.RandomPhone(), invite.Code)))
            .ExpectAsync(HttpStatusCode.OK);
        var client = factory.CreateClientWithToken(login.GetProperty("accessToken").GetString()!);
        var refreshToken = login.GetProperty("refreshToken").GetString()!;

        await (await client.PostJsonAsync("/api/v1/me/devices", new { fcmToken = "fcm-out", platform = DevicePlatform.Android, appVersion = "1.0.0" }))
            .ExpectAsync(HttpStatusCode.NoContent);
        await (await client.PostJsonAsync("/api/v1/auth/logout", new { refreshToken, fcmToken = "fcm-out" })).ExpectAsync(HttpStatusCode.NoContent);

        await (await anonymous.PostJsonAsync("/api/v1/auth/refresh", new { refreshToken })).ExpectAsync(HttpStatusCode.Unauthorized);
        Assert.False(await factory.WithDbAsync(db => db.UserDevices.Where(d => d.FcmToken == "fcm-out").Select(d => d.IsActive).SingleAsync()));
    }

    [Fact]
    public async Task Logout_with_an_installation_id_deactivates_that_phone()
    {
        var invite = await factory.SeedInvitationAsync(await factory.SeedMasterAdminAsyncSafe());
        var anonymous = factory.CreateClient();
        var login = await (await anonymous.PostJsonAsync("/api/v1/auth/login", LoginBody("fid-out", ApiFactory.RandomPhone(), invite.Code)))
            .ExpectAsync(HttpStatusCode.OK);
        var client = factory.CreateClientWithToken(login.GetProperty("accessToken").GetString()!);
        var fid = $"fid-{Guid.NewGuid():N}";

        await (await client.PostJsonAsync("/api/v1/me/devices", new { installationId = fid, platform = DevicePlatform.Android }))
            .ExpectAsync(HttpStatusCode.NoContent);
        await (await client.PostJsonAsync("/api/v1/auth/logout", new { refreshToken = login.GetProperty("refreshToken").GetString(), installationId = fid }))
            .ExpectAsync(HttpStatusCode.NoContent);

        Assert.False(await factory.WithDbAsync(db => db.UserDevices.Where(d => d.InstallationId == fid).Select(d => d.IsActive).SingleAsync()));
    }

    [Fact]
    public async Task Seeded_master_admin_links_firebase_on_first_login_and_must_accept_policy()
    {
        var master = ApiFactory.NewMasterAdmin(); // has NOT accepted any policy yet
        await factory.WithDbAsync(async db => { db.Users.Add(master); await db.SaveChangesAsync(); });

        var login = await (await factory.CreateClient().PostJsonAsync("/api/v1/auth/login", LoginBody("master-uid", master.PhoneNumber.Value, withConsent: false)))
            .ExpectAsync(HttpStatusCode.OK);
        Assert.True(login.GetProperty("user").GetProperty("policyAcceptanceRequired").GetBoolean());

        var client = factory.CreateClientWithToken(login.GetProperty("accessToken").GetString()!);
        var blocked = await (await client.PutJsonAsync("/api/v1/me/preferences", new { alertsEnabled = true })).ExpectAsync(HttpStatusCode.Forbidden);
        Assert.Equal("policy.acceptance_required", blocked.Code());

        var accepted = await (await client.PostJsonAsync("/api/v1/me/policy-acceptance", new { policyVersion = ApiFactory.PolicyVersion, confirmedAdult = true }))
            .ExpectAsync(HttpStatusCode.OK);
        var upgraded = factory.CreateClientWithToken(accepted.GetProperty("accessToken").GetString()!);
        await (await upgraded.PutJsonAsync("/api/v1/me/preferences", new { alertsEnabled = true })).ExpectAsync(HttpStatusCode.OK);

        Assert.Equal("master-uid", await factory.WithDbAsync(async db => (await db.Users.SingleAsync(u => u.Id == master.Id)).FirebaseUid));
    }
}

internal static class FactorySeedExtensions
{
    /// <summary>
    /// The single-Master-Admin rule is a SQL filtered unique index, which InMemory does not enforce,
    /// so tests may seed several "masters" to isolate scenarios.
    /// </summary>
    public static Task<CommunitySos.Domain.Entities.User> SeedMasterAdminAsyncSafe(this ApiFactory factory) => factory.SeedMasterAdminAsync();
}
