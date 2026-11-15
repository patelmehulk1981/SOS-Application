using System.Net;
using System.Net.Http.Headers;
using Microsoft.EntityFrameworkCore;

namespace CommunitySos.Api.IntegrationTests;

public sealed class LocationAndPhotoTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private async Task<(HttpClient Client, Guid UserId)> NewMemberAsync()
    {
        var master = await factory.SeedMasterAdminAsync();
        var member = await factory.SeedInviteeAsync(await factory.SeedInviteeAsync(master, "Lead"), "Member");
        return (factory.CreateClientFor(member), member.Id);
    }

    private static object Location(DateTime capturedAt, double lat = 23.0225) => new { latitude = lat, longitude = 72.5714, accuracyMeters = 15, capturedAt };

    [Fact]
    public async Task Location_requires_consent_then_keeps_only_the_latest_fix()
    {
        var (client, userId) = await NewMemberAsync();
        var now = DateTime.UtcNow;

        var blocked = await (await client.PutJsonAsync("/api/v1/me/location", Location(now))).ExpectAsync(HttpStatusCode.UnprocessableEntity);
        Assert.Equal("consent.location_required", blocked.Code());

        await (await client.PutJsonAsync("/api/v1/me/consents", new { type = "LocationSharing", granted = true })).ExpectAsync(HttpStatusCode.OK);
        await (await client.PutJsonAsync("/api/v1/me/location", Location(now, lat: 23.03))).ExpectAsync(HttpStatusCode.NoContent);
        await (await client.PutJsonAsync("/api/v1/me/location", Location(now.AddMinutes(-5), lat: 10.0))).ExpectAsync(HttpStatusCode.NoContent); // older: ignored

        var rows = await factory.WithDbAsync(db => db.UserLocations.Where(l => l.UserId == userId).ToListAsync());
        Assert.Single(rows);
        Assert.Equal(23.03, rows[0].Location.Y, precision: 6);
    }

    [Fact]
    public async Task Invalid_coordinates_fail_validation()
    {
        var (client, _) = await NewMemberAsync();

        var problem = await (await client.PutJsonAsync("/api/v1/me/location", new { latitude = 123.0, longitude = 72.0, capturedAt = DateTime.UtcNow }))
            .ExpectAsync(HttpStatusCode.BadRequest);

        Assert.True(problem.GetProperty("errors").TryGetProperty("latitude", out _));
    }

    [Fact]
    public async Task Profile_photo_is_replaced_and_old_blob_deleted()
    {
        var (client, userId) = await NewMemberAsync();

        var first = await (await client.PostAsync("/api/v1/me/photo", PhotoForm(TestImages.Jpeg(300, 300)))).ExpectAsync(HttpStatusCode.OK);
        Assert.Contains("sig=", first.GetProperty("profilePhotoUrl").GetString(), StringComparison.Ordinal);
        await (await client.PostAsync("/api/v1/me/photo", PhotoForm(TestImages.Jpeg(200, 200)))).ExpectAsync(HttpStatusCode.OK);

        Assert.Single(factory.Blobs.Blobs.Keys, k => k.StartsWith($"profiles/{userId:N}/", StringComparison.Ordinal));

        var removed = await (await client.DeleteAsync("/api/v1/me/photo")).ExpectAsync(HttpStatusCode.OK);
        Assert.False(removed.TryGetProperty("profilePhotoUrl", out _));
        Assert.DoesNotContain(factory.Blobs.Blobs.Keys, k => k.StartsWith($"profiles/{userId:N}/", StringComparison.Ordinal));
    }

    private static MultipartFormDataContent PhotoForm(byte[] bytes)
    {
        var part = new ByteArrayContent(bytes);
        part.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        return new MultipartFormDataContent { { part, "photo", "me.jpg" } };
    }
}
