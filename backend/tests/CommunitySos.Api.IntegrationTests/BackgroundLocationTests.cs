using System.Net;
using CommunitySos.Application.Abstractions;
using CommunitySos.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CommunitySos.Api.IntegrationTests;

/// <summary>Location keys and PUT /location/background (docs/06-background-location.md).</summary>
public sealed class BackgroundLocationTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string Background = "/api/v1/location/background";

    private sealed record Member(HttpClient Client, User User, string DeviceId, string RefreshToken);

    /// <summary>A member with location sharing on and a live session on one phone.</summary>
    private async Task<Member> NewMemberAsync(bool shareLocation = true)
    {
        var master = await factory.SeedMasterAdminAsync();
        var user = await factory.SeedInviteeAsync(await factory.SeedInviteeAsync(master, "Lead"), "Member");
        var client = factory.CreateClientFor(user);
        var deviceId = $"install-{Guid.NewGuid():N}";
        var refresh = await SeedSessionAsync(user.Id, deviceId, TimeSpan.FromDays(30));
        if (shareLocation)
        {
            await (await client.PutJsonAsync("/api/v1/me/consents", new { type = "LocationSharing", granted = true })).ExpectAsync(HttpStatusCode.OK);
        }

        return new Member(client, user, deviceId, refresh);
    }

    private async Task<string> SeedSessionAsync(Guid userId, string deviceId, TimeSpan lifetime, DateTime? issuedAt = null)
    {
        using var scope = factory.Services.CreateScope();
        var value = scope.ServiceProvider.GetRequiredService<ITokenService>().CreateRefreshToken();
        await factory.WithDbAsync(async db =>
        {
            db.RefreshTokens.Add(RefreshToken.Issue(userId, value.Hash, Guid.NewGuid(), deviceId, issuedAt ?? DateTime.UtcNow, lifetime));
            await db.SaveChangesAsync();
        });
        return value.Token;
    }

    private static async Task<string> IssueKeyAsync(Member member)
    {
        var body = await (await member.Client.PostJsonAsync("/api/v1/me/location-key", new { deviceId = member.DeviceId })).ExpectAsync(HttpStatusCode.OK);
        return body.GetProperty("key").GetString()!;
    }

    /// <summary>Background work has no session: a fresh client with only the location key header.</summary>
    private Task<HttpResponseMessage> ReportAsync(string? key, double latitude = 23.0225, DateTime? capturedAt = null)
    {
        var client = factory.CreateClient();
        if (key is not null)
        {
            client.DefaultRequestHeaders.Add("X-Location-Key", key);
        }

        return client.PutJsonAsync(Background, new { latitude, longitude = 72.5714, accuracyMeters = 35, capturedAt = capturedAt ?? DateTime.UtcNow });
    }

    private Task<LocationKey> KeyRowAsync(Guid userId) =>
        factory.WithDbAsync(db => db.LocationKeys.Where(k => k.UserId == userId).OrderByDescending(k => k.CreatedAt).FirstAsync());

    [Fact]
    public async Task Key_reports_location_without_a_session_token()
    {
        var member = await NewMemberAsync();
        var key = await IssueKeyAsync(member);
        Assert.StartsWith("lk_", key, StringComparison.Ordinal);

        await (await ReportAsync(key, latitude: 23.05)).ExpectAsync(HttpStatusCode.NoContent);

        var location = await factory.WithDbAsync(db => db.UserLocations.SingleAsync(l => l.UserId == member.User.Id));
        Assert.Equal(23.05, location.Location.Y, precision: 6);
        var row = await KeyRowAsync(member.User.Id);
        Assert.NotNull(row.LastUsedAt);
        Assert.NotEqual(key, row.KeyHash); // only the hash is stored
        Assert.Equal(64, row.KeyHash.Length);
    }

    [Fact]
    public async Task Older_background_fix_does_not_replace_a_newer_one()
    {
        var member = await NewMemberAsync();
        var key = await IssueKeyAsync(member);
        var now = DateTime.UtcNow;

        await (await ReportAsync(key, latitude: 23.05, capturedAt: now)).ExpectAsync(HttpStatusCode.NoContent);
        await (await ReportAsync(key, latitude: 10.0, capturedAt: now.AddMinutes(-10))).ExpectAsync(HttpStatusCode.NoContent);

        var location = await factory.WithDbAsync(db => db.UserLocations.SingleAsync(l => l.UserId == member.User.Id));
        Assert.Equal(23.05, location.Location.Y, precision: 6);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("lk_not-a-real-key")]
    [InlineData("a-refresh-token-is-not-a-location-key")]
    public async Task Missing_or_unknown_key_is_rejected(string? key)
    {
        var problem = await (await ReportAsync(key)).ExpectAsync(HttpStatusCode.Unauthorized);
        Assert.Equal("location_key.invalid", problem.Code());
    }

    [Fact]
    public async Task A_session_token_alone_cannot_report_in_the_background()
    {
        var member = await NewMemberAsync();
        var problem = await (await member.Client.PutJsonAsync(Background, new { latitude = 23.0, longitude = 72.5, capturedAt = DateTime.UtcNow }))
            .ExpectAsync(HttpStatusCode.Unauthorized);
        Assert.Equal("location_key.invalid", problem.Code());
    }

    [Fact]
    public async Task Issuing_needs_location_sharing_and_a_session_on_that_phone()
    {
        var member = await NewMemberAsync(shareLocation: false);

        var noConsent = await (await member.Client.PostJsonAsync("/api/v1/me/location-key", new { deviceId = member.DeviceId }))
            .ExpectAsync(HttpStatusCode.UnprocessableEntity);
        Assert.Equal("consent.location_required", noConsent.Code());

        await (await member.Client.PutJsonAsync("/api/v1/me/consents", new { type = "LocationSharing", granted = true })).ExpectAsync(HttpStatusCode.OK);
        var otherPhone = await (await member.Client.PostJsonAsync("/api/v1/me/location-key", new { deviceId = "never-signed-in" }))
            .ExpectAsync(HttpStatusCode.UnprocessableEntity);
        Assert.Equal("location_key.unknown_device", otherPhone.Code());

        var invalid = await (await member.Client.PostJsonAsync("/api/v1/me/location-key", new { deviceId = "" })).ExpectAsync(HttpStatusCode.BadRequest);
        Assert.True(invalid.GetProperty("errors").TryGetProperty("deviceId", out _));
    }

    [Fact]
    public async Task A_new_key_replaces_the_old_one_for_that_phone()
    {
        var member = await NewMemberAsync();
        var first = await IssueKeyAsync(member);
        var second = await IssueKeyAsync(member);

        await (await ReportAsync(first)).ExpectAsync(HttpStatusCode.Unauthorized);
        await (await ReportAsync(second)).ExpectAsync(HttpStatusCode.NoContent);

        var rows = await factory.WithDbAsync(db => db.LocationKeys.Where(k => k.UserId == member.User.Id).ToListAsync());
        Assert.Equal(2, rows.Count);
        Assert.Single(rows, k => k.IsActive);
        Assert.Contains(rows, k => k.RevokedReason == "replaced");
    }

    [Fact]
    public async Task Each_phone_has_its_own_key()
    {
        var member = await NewMemberAsync();
        var secondPhone = $"install-{Guid.NewGuid():N}";
        await SeedSessionAsync(member.User.Id, secondPhone, TimeSpan.FromDays(30));

        var phoneA = await IssueKeyAsync(member);
        var phoneB = await IssueKeyAsync(member with { DeviceId = secondPhone });

        await (await member.Client.PostJsonAsync("/api/v1/me/location-key/revoke", new { deviceId = secondPhone })).ExpectAsync(HttpStatusCode.NoContent);

        await (await ReportAsync(phoneA)).ExpectAsync(HttpStatusCode.NoContent);
        await (await ReportAsync(phoneB)).ExpectAsync(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Turning_it_off_revokes_the_key_and_is_idempotent()
    {
        var member = await NewMemberAsync();
        var key = await IssueKeyAsync(member);

        await (await member.Client.PostJsonAsync("/api/v1/me/location-key/revoke", new { deviceId = member.DeviceId })).ExpectAsync(HttpStatusCode.NoContent);
        await (await member.Client.PostJsonAsync("/api/v1/me/location-key/revoke", new { deviceId = member.DeviceId })).ExpectAsync(HttpStatusCode.NoContent);

        await (await ReportAsync(key)).ExpectAsync(HttpStatusCode.Unauthorized);
        Assert.Equal("turned_off", (await KeyRowAsync(member.User.Id)).RevokedReason);
    }

    [Fact]
    public async Task Withdrawing_location_sharing_stops_background_reports()
    {
        var member = await NewMemberAsync();
        var key = await IssueKeyAsync(member);
        await (await ReportAsync(key)).ExpectAsync(HttpStatusCode.NoContent);

        await (await member.Client.PutJsonAsync("/api/v1/me/consents", new { type = "LocationSharing", granted = false })).ExpectAsync(HttpStatusCode.OK);

        await (await ReportAsync(key)).ExpectAsync(HttpStatusCode.Unauthorized);
        Assert.Equal("consent_withdrawn", (await KeyRowAsync(member.User.Id)).RevokedReason);
        Assert.False(await factory.WithDbAsync(db => db.UserLocations.AnyAsync(l => l.UserId == member.User.Id)));
    }

    [Fact]
    public async Task Signing_out_that_phone_revokes_its_key()
    {
        var member = await NewMemberAsync();
        var key = await IssueKeyAsync(member);

        await (await member.Client.PostJsonAsync("/api/v1/auth/logout", new { refreshToken = member.RefreshToken })).ExpectAsync(HttpStatusCode.NoContent);

        await (await ReportAsync(key)).ExpectAsync(HttpStatusCode.Unauthorized);
        Assert.Equal("logout", (await KeyRowAsync(member.User.Id)).RevokedReason);
    }

    [Fact]
    public async Task Key_stops_working_when_the_phone_session_has_expired()
    {
        var member = await NewMemberAsync();
        var key = await IssueKeyAsync(member);

        // The phone was not opened for the whole refresh-token lifetime: every session on it expires.
        await factory.WithDbAsync(async db =>
        {
            foreach (var token in await db.RefreshTokens.Where(t => t.UserId == member.User.Id).ToListAsync())
            {
                token.Revoke(DateTime.UtcNow, "expired_in_test");
            }

            await db.SaveChangesAsync();
        });

        await (await ReportAsync(key)).ExpectAsync(HttpStatusCode.Unauthorized);
        Assert.Equal("signed_out", (await KeyRowAsync(member.User.Id)).RevokedReason);
    }

    [Fact]
    public async Task Stale_background_fix_is_rejected_without_revoking_the_key()
    {
        var member = await NewMemberAsync();
        var key = await IssueKeyAsync(member);

        var stale = await ReportAsync(key, capturedAt: DateTime.UtcNow.AddHours(-13));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, stale.StatusCode);

        await (await ReportAsync(key)).ExpectAsync(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Export_lists_background_location_without_the_key_and_deletion_removes_it()
    {
        var member = await NewMemberAsync();
        var key = await IssueKeyAsync(member);
        await (await ReportAsync(key)).ExpectAsync(HttpStatusCode.NoContent);

        var exportResponse = await member.Client.GetAsync("/api/v1/me/data-export");
        var raw = await exportResponse.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, exportResponse.StatusCode);
        Assert.DoesNotContain(key, raw, StringComparison.Ordinal);
        var export = await exportResponse.ReadJsonAsync();
        var keys = export.GetProperty("backgroundLocationKeys");
        Assert.Equal(1, keys.GetArrayLength());
        Assert.True(keys[0].TryGetProperty("lastUsedAt", out _));

        await (await member.Client.DeleteAsync("/api/v1/me")).ExpectAsync(HttpStatusCode.NoContent);

        Assert.False(await factory.WithDbAsync(db => db.LocationKeys.AnyAsync(k => k.UserId == member.User.Id)));
        await (await ReportAsync(key)).ExpectAsync(HttpStatusCode.Unauthorized);
    }
}
