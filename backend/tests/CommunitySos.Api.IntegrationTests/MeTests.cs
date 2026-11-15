using System.Net;
using System.Text.Json;
using CommunitySos.Domain.Entities;
using CommunitySos.Domain.Enums;
using CommunitySos.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;

namespace CommunitySos.Api.IntegrationTests;

public sealed class MeTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    /// <summary>Registers a fresh User (via a Sub-Admin) and returns an authenticated client.</summary>
    private async Task<(HttpClient Client, Guid UserId, string Uid, User SubAdmin)> NewMemberAsync()
    {
        var master = await factory.SeedMasterAdminAsync();
        var now = DateTime.UtcNow;
        var sub = Invitation.Create(master, null, null, null, now)
            .Redeem(PhoneNumber.Create(ApiFactory.RandomPhone()), $"sub-{Guid.NewGuid():N}", new RegistrationConsent(ApiFactory.PolicyVersion, true, null), now, null, null);
        await factory.WithDbAsync(async db =>
        {
            db.Users.Add(sub.User);
            db.ReferralHistory.Add(sub.Referral);
            db.UserConsents.Add(sub.TermsConsent);
            await db.SaveChangesAsync();
        });
        sub.User.GetType(); // keep reference
        var invite = await factory.SeedInvitationAsync(sub.User);

        var uid = $"m-{Guid.NewGuid():N}";
        var login = await (await factory.CreateClient().PostJsonAsync("/api/v1/auth/login", new
        {
            firebaseIdToken = FakeFirebase.TokenFor(uid, ApiFactory.RandomPhone()),
            inviteCode = invite.Code,
            consent = new { policyVersion = ApiFactory.PolicyVersion, confirmedAdult = true },
        })).ExpectAsync(HttpStatusCode.OK);

        return (
            factory.CreateClientWithToken(login.GetProperty("accessToken").GetString()!),
            login.GetProperty("user").GetProperty("id").GetGuid(),
            uid,
            sub.User);
    }

    [Fact]
    public async Task Profile_shows_referrer_name_but_not_their_phone()
    {
        var (client, _, _, subAdmin) = await NewMemberAsync();

        var profile = await (await client.GetAsync("/api/v1/me")).ExpectAsync(HttpStatusCode.OK);

        var referredBy = profile.GetProperty("referredBy");
        Assert.Equal(subAdmin.Id, referredBy.GetProperty("id").GetGuid());
        Assert.Equal("SubAdmin", referredBy.GetProperty("role").GetString());
        Assert.False(referredBy.TryGetProperty("phoneNumber", out _));
        Assert.True(profile.GetProperty("consents").GetProperty("termsAndPrivacy").GetBoolean());
        Assert.False(profile.GetProperty("consents").GetProperty("healthData").GetBoolean());
    }

    [Fact]
    public async Task Health_data_requires_consent_and_is_erased_on_withdrawal()
    {
        var (client, userId, _, _) = await NewMemberAsync();
        var profile = new { fullName = "Asha Patel", email = "asha@example.com", bloodGroup = "B+", emergencyContactName = "Ravi", emergencyContactPhone = "98765 43210" };

        var blocked = await (await client.PutJsonAsync("/api/v1/me/profile", profile)).ExpectAsync(HttpStatusCode.UnprocessableEntity);
        Assert.Equal("consent.health_data_required", blocked.Code());

        await (await client.PutJsonAsync("/api/v1/me/consents", new { type = ConsentType.HealthData, granted = true })).ExpectAsync(HttpStatusCode.OK);
        var saved = await (await client.PutJsonAsync("/api/v1/me/profile", profile)).ExpectAsync(HttpStatusCode.OK);
        Assert.Equal("B+", saved.GetProperty("bloodGroup").GetString());
        Assert.Equal("+919876543210", saved.GetProperty("emergencyContactPhone").GetString()); // Indian 10-digit normalised

        await (await client.PutJsonAsync("/api/v1/me/consents", new { type = ConsentType.HealthData, granted = false })).ExpectAsync(HttpStatusCode.OK);
        var user = await factory.WithDbAsync(db => db.Users.AsNoTracking().SingleAsync(u => u.Id == userId));
        Assert.Null(user.BloodGroup);
        Assert.Null(user.EmergencyContactPhone);
        Assert.Equal("Asha Patel", user.FullName);
    }

    [Fact]
    public async Task Audit_log_never_contains_health_values()
    {
        var (client, userId, _, _) = await NewMemberAsync();
        await (await client.PutJsonAsync("/api/v1/me/consents", new { type = ConsentType.HealthData, granted = true })).ExpectAsync(HttpStatusCode.OK);
        await (await client.PutJsonAsync("/api/v1/me/profile", new { fullName = "Secret Name", bloodGroup = "AB-" })).ExpectAsync(HttpStatusCode.OK);

        var audit = await factory.WithDbAsync(db => db.AuditLogs.Where(a => a.EntityId == userId.ToString()).Select(a => a.NewValues ?? "").ToListAsync());

        Assert.DoesNotContain(audit, v => v.Contains("AB-", StringComparison.Ordinal) || v.Contains("Secret Name", StringComparison.Ordinal));
        Assert.Contains(audit, v => v.Contains("\"BloodGroup\":\"***\"", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Withdrawing_location_consent_deletes_stored_location()
    {
        var (client, userId, _, _) = await NewMemberAsync();
        await (await client.PutJsonAsync("/api/v1/me/consents", new { type = ConsentType.LocationSharing, granted = true })).ExpectAsync(HttpStatusCode.OK);
        await factory.WithDbAsync(async db =>
        {
            db.UserLocations.Add(UserLocation.Create(userId, new GeoCoordinate(23.02, 72.57), 10, DateTime.UtcNow, DateTime.UtcNow));
            await db.SaveChangesAsync();
        });

        var state = await (await client.PutJsonAsync("/api/v1/me/consents", new { type = ConsentType.LocationSharing, granted = false })).ExpectAsync(HttpStatusCode.OK);

        Assert.False(state.GetProperty("locationSharing").GetBoolean());
        Assert.False(await factory.WithDbAsync(db => db.UserLocations.AnyAsync(l => l.UserId == userId)));
    }

    [Fact]
    public async Task Terms_consent_cannot_be_withdrawn_separately()
    {
        var (client, _, _, _) = await NewMemberAsync();

        var problem = await (await client.PutJsonAsync("/api/v1/me/consents", new { type = ConsentType.TermsAndPrivacy, granted = false }))
            .ExpectAsync(HttpStatusCode.UnprocessableEntity);

        Assert.Equal("consent.terms_not_withdrawable", problem.Code());
    }

    [Fact]
    public async Task Device_token_moves_to_the_latest_account_on_shared_phone()
    {
        var (first, firstId, _, _) = await NewMemberAsync();
        var (second, secondId, _, _) = await NewMemberAsync();
        var device = new { fcmToken = $"shared-{Guid.NewGuid():N}", platform = DevicePlatform.Ios, appVersion = "1.0.0", deviceModel = "iPhone 16" };

        await (await first.PostJsonAsync("/api/v1/me/devices", device)).ExpectAsync(HttpStatusCode.NoContent);
        await (await second.PostJsonAsync("/api/v1/me/devices", device)).ExpectAsync(HttpStatusCode.NoContent);

        var owners = await factory.WithDbAsync(db => db.UserDevices.Where(d => d.FcmToken == device.fcmToken).Select(d => d.UserId).ToListAsync());
        Assert.Equal([secondId], owners);
        Assert.NotEqual(firstId, secondId);
    }

    [Fact]
    public async Task Installation_id_registers_and_an_upgraded_app_moves_its_token_row_to_the_fid()
    {
        var (client, userId, _, _) = await NewMemberAsync();
        var token = $"legacy-{Guid.NewGuid():N}";
        var fid = $"fid-{Guid.NewGuid():N}";

        await (await client.PostJsonAsync("/api/v1/me/devices", new { fcmToken = token, platform = DevicePlatform.Android, appVersion = "1.0.0" }))
            .ExpectAsync(HttpStatusCode.NoContent);
        var legacyId = await factory.WithDbAsync(db => db.UserDevices.Where(d => d.FcmToken == token).Select(d => d.Id).SingleAsync());

        // The upgraded build sends its FID plus the old token once.
        await (await client.PostJsonAsync("/api/v1/me/devices", new { installationId = fid, fcmToken = token, platform = DevicePlatform.Android, appVersion = "1.1.0" }))
            .ExpectAsync(HttpStatusCode.NoContent);
        // Later starts send only the FID.
        await (await client.PostJsonAsync("/api/v1/me/devices", new { installationId = fid, platform = DevicePlatform.Android, appVersion = "1.1.0" }))
            .ExpectAsync(HttpStatusCode.NoContent);

        var devices = await factory.WithDbAsync(db => db.UserDevices.Where(d => d.UserId == userId).ToListAsync());
        var device = Assert.Single(devices);
        Assert.Equal(legacyId, device.Id);
        Assert.Equal(fid, device.InstallationId);
        Assert.Null(device.FcmToken);
        Assert.Equal("1.1.0", device.AppVersion);

        await (await client.PostJsonAsync("/api/v1/me/devices/unregister", new { installationId = fid })).ExpectAsync(HttpStatusCode.NoContent);
        Assert.False(await factory.WithDbAsync(db => db.UserDevices.Where(d => d.InstallationId == fid).Select(d => d.IsActive).SingleAsync()));
    }

    [Fact]
    public async Task Installation_id_follows_the_latest_account_on_a_shared_phone()
    {
        var (first, _, _, _) = await NewMemberAsync();
        var (second, secondId, _, _) = await NewMemberAsync();
        var device = new { installationId = $"fid-{Guid.NewGuid():N}", platform = DevicePlatform.Ios };

        await (await first.PostJsonAsync("/api/v1/me/devices", device)).ExpectAsync(HttpStatusCode.NoContent);
        await (await second.PostJsonAsync("/api/v1/me/devices", device)).ExpectAsync(HttpStatusCode.NoContent);

        var owners = await factory.WithDbAsync(db => db.UserDevices.Where(d => d.InstallationId == device.installationId).Select(d => d.UserId).ToListAsync());
        Assert.Equal([secondId], owners);
    }

    [Fact]
    public async Task Device_registration_needs_an_installation_id_or_token()
    {
        var (client, _, _, _) = await NewMemberAsync();
        var problem = await (await client.PostJsonAsync("/api/v1/me/devices", new { platform = DevicePlatform.Android })).ExpectAsync(HttpStatusCode.BadRequest);
        Assert.True(problem.GetProperty("errors").TryGetProperty("installationId", out _));
    }

    [Fact]
    public async Task Data_export_is_a_json_download_with_referral_and_consents()
    {
        var (client, _, _, subAdmin) = await NewMemberAsync();

        var response = await client.GetAsync("/api/v1/me/data-export");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("attachment", response.Content.Headers.ContentDisposition?.DispositionType);
        using var export = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(subAdmin.Id, export.RootElement.GetProperty("referral").GetProperty("invitedByUserId").GetGuid());
        Assert.Equal("TermsAndPrivacy", export.RootElement.GetProperty("consents")[0].GetProperty("type").GetString());
    }

    [Fact]
    public async Task Delete_account_anonymises_and_removes_sessions_devices_and_firebase_identity()
    {
        var (client, userId, uid, _) = await NewMemberAsync();
        await (await client.PostJsonAsync("/api/v1/me/devices", new { fcmToken = $"del-{Guid.NewGuid():N}", platform = DevicePlatform.Android })).ExpectAsync(HttpStatusCode.NoContent);

        await (await client.DeleteAsync("/api/v1/me")).ExpectAsync(HttpStatusCode.NoContent);

        var (user, devices, tokens, referralKept, withdrawals) = await factory.WithDbAsync(async db => (
            await db.Users.AsNoTracking().SingleAsync(u => u.Id == userId),
            await db.UserDevices.CountAsync(d => d.UserId == userId),
            await db.RefreshTokens.CountAsync(t => t.UserId == userId),
            await db.ReferralHistory.AnyAsync(r => r.ReferredUserId == userId),
            await db.UserConsents.CountAsync(c => c.UserId == userId && !c.Granted)));

        Assert.Equal(UserStatus.Deleted, user.Status);
        Assert.StartsWith("deleted:", user.PhoneNumber.Value, StringComparison.Ordinal);
        Assert.Null(user.FullName);
        Assert.Equal(0, devices);
        Assert.Equal(0, tokens);
        Assert.True(referralKept);
        Assert.Equal(3, withdrawals);
        Assert.Contains(uid, factory.Firebase.DeletedUids);
    }

    [Fact]
    public async Task Cannot_delete_account_with_an_open_sos()
    {
        var (client, userId, _, subAdmin) = await NewMemberAsync();
        await factory.WithDbAsync(async db =>
        {
            var member = await db.Users.SingleAsync(u => u.Id == userId);
            var sos = SosRequest.Create(member, subAdmin.Id, Guid.NewGuid(), SosCategory.Medical, null, new GeoCoordinate(23.02, 72.57), null, DateTime.UtcNow, DateTime.UtcNow);
            db.SosRequests.Add(sos);
            db.Entry(sos).Property(s => s.ReferenceNo).CurrentValue = "SOS-00000001"; // SQL computed column; InMemory cannot compute it
            await db.SaveChangesAsync();
        });

        var problem = await (await client.DeleteAsync("/api/v1/me")).ExpectAsync(HttpStatusCode.Conflict);

        Assert.Equal("account.open_sos", problem.Code());
    }
}
