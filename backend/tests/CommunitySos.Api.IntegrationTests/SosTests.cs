using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using CommunitySos.Domain.Entities;
using CommunitySos.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace CommunitySos.Api.IntegrationTests;

public sealed class SosTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private sealed record Community(User Master, User SubAdmin, User Member);

    private async Task<Community> NewCommunityAsync()
    {
        var master = await factory.SeedMasterAdminAsync();
        var sub = await factory.SeedInviteeAsync(master, "Ward Lead");
        var member = await factory.SeedInviteeAsync(sub, "Asha Patel");
        return new Community(master, sub, member);
    }

    private static object NewSos(Guid? clientRequestId = null) => new
    {
        clientRequestId = clientRequestId ?? Guid.NewGuid(),
        category = "Medical",
        description = "Elderly neighbour collapsed, needs help",
        latitude = 23.022505,
        longitude = 72.571365,
        accuracyMeters = 8.5,
        capturedAt = DateTime.UtcNow,
    };

    private static MultipartFormDataContent Photos(params byte[][] files)
    {
        var form = new MultipartFormDataContent();
        for (var i = 0; i < files.Length; i++)
        {
            var part = new ByteArrayContent(files[i]);
            part.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
            form.Add(part, "photos", $"photo{i}.jpg");
        }

        return form;
    }

    [Fact]
    public async Task User_sos_is_pending_assigned_to_their_sub_admin_and_geocoded_in_background()
    {
        var c = await NewCommunityAsync();

        var sos = await (await factory.CreateClientFor(c.Member).PostJsonAsync("/api/v1/sos", NewSos())).ExpectAsync(HttpStatusCode.Created);

        Assert.Equal("Pending", sos.GetProperty("status").GetString());
        Assert.False(sos.GetProperty("isAutoApproved").GetBoolean());
        Assert.Equal("Requester", sos.GetProperty("viewerRole").GetString());
        Assert.StartsWith("SOS-", sos.GetProperty("referenceNo").GetString(), StringComparison.Ordinal);
        Assert.Contains("google.com/maps", sos.GetProperty("location").GetProperty("mapsUrl").GetString(), StringComparison.Ordinal);
        var id = sos.GetProperty("id").GetGuid();
        Assert.Equal(c.SubAdmin.Id, await factory.WithDbAsync(db => db.SosRequests.Where(s => s.Id == id).Select(s => s.AssignedAdminId).SingleAsync()));

        await factory.Jobs.RunAllAsync();
        var refreshed = await (await factory.CreateClientFor(c.Member).GetAsync($"/api/v1/sos/{id}")).ExpectAsync(HttpStatusCode.OK);
        Assert.Equal(FakeGeocoder.Address, refreshed.GetProperty("address").GetString());
    }

    [Fact]
    public async Task Retried_submit_returns_the_same_sos()
    {
        var c = await NewCommunityAsync();
        var client = factory.CreateClientFor(c.Member);
        var body = NewSos(Guid.NewGuid());

        var first = await (await client.PostJsonAsync("/api/v1/sos", body)).ExpectAsync(HttpStatusCode.Created);
        var retry = await client.PostJsonAsync("/api/v1/sos", body);
        var replay = await retry.ExpectAsync(HttpStatusCode.OK);

        Assert.Equal(first.GetProperty("id").GetGuid(), replay.GetProperty("id").GetGuid());
        Assert.Equal("true", retry.Headers.GetValues("Idempotent-Replayed").Single());
        Assert.Equal(1, await factory.WithDbAsync(db => db.SosRequests.CountAsync(s => s.RequesterUserId == c.Member.Id)));
    }

    [Fact]
    public async Task Second_sos_while_one_is_open_points_to_the_existing_one()
    {
        var c = await NewCommunityAsync();
        var client = factory.CreateClientFor(c.Member);
        var first = await (await client.PostJsonAsync("/api/v1/sos", NewSos())).ExpectAsync(HttpStatusCode.Created);

        var problem = await (await client.PostJsonAsync("/api/v1/sos", NewSos())).ExpectAsync(HttpStatusCode.Conflict);

        Assert.Equal("sos.already_open", problem.Code());
        Assert.Equal(first.GetProperty("id").GetGuid(), problem.GetProperty("sosId").GetGuid());
    }

    [Fact]
    public async Task Sub_admin_sos_is_auto_approved_and_assigned_to_master()
    {
        var c = await NewCommunityAsync();

        var sos = await (await factory.CreateClientFor(c.SubAdmin).PostJsonAsync("/api/v1/sos", NewSos())).ExpectAsync(HttpStatusCode.Created);

        Assert.Equal("Approved", sos.GetProperty("status").GetString());
        Assert.True(sos.GetProperty("isAutoApproved").GetBoolean());
        var timeline = sos.GetProperty("timeline").EnumerateArray().ToList();
        Assert.Equal(2, timeline.Count);
        Assert.True(timeline[1].GetProperty("bySystem").GetBoolean());
        var id = sos.GetProperty("id").GetGuid();
        Assert.Equal(c.Master.Id, await factory.WithDbAsync(db => db.SosRequests.Where(s => s.Id == id).Select(s => s.AssignedAdminId).SingleAsync()));
    }

    [Fact]
    public async Task Users_sos_goes_to_master_when_their_sub_admin_is_suspended()
    {
        var c = await NewCommunityAsync();
        await factory.WithDbAsync(async db =>
        {
            (await db.Users.SingleAsync(u => u.Id == c.SubAdmin.Id)).Suspend();
            await db.SaveChangesAsync();
        });

        var sos = await (await factory.CreateClientFor(c.Member).PostJsonAsync("/api/v1/sos", NewSos())).ExpectAsync(HttpStatusCode.Created);

        // The SQL unique index guarantees a single Master Admin; the shared InMemory test DB holds one per test,
        // so assert on the role rather than a specific id.
        var id = sos.GetProperty("id").GetGuid();
        var assignedRole = await factory.WithDbAsync(db =>
            (from s in db.SosRequests
             join u in db.Users on s.AssignedAdminId equals u.Id
             where s.Id == id
             select u.Role).SingleAsync());
        Assert.Equal(UserRole.MasterAdmin, assignedRole);
    }

    [Fact]
    public async Task Details_are_shaped_for_requester_admin_and_alerted_neighbour()
    {
        var c = await NewCommunityAsync();
        await factory.WithDbAsync(async db =>
        {
            await db.UserConsents.AddAsync(UserConsent.Record(c.Member.Id, ConsentType.HealthData, ApiFactory.PolicyVersion, true, DateTime.UtcNow, null, null));
            (await db.Users.SingleAsync(u => u.Id == c.Member.Id)).UpdateProfile("Asha Patel", null, "O+", "Ravi", null);
            await db.SaveChangesAsync();
        });
        var otherSub = await factory.SeedInviteeAsync(c.Master, "Other Lead");
        var neighbour = await factory.SeedInviteeAsync(otherSub, "Kiran Shah");
        var stranger = await factory.SeedInviteeAsync(otherSub, "Not Alerted");

        // Make it Approved via an auto-approving requester? No: use the member's SOS and approve directly in the DB.
        var created = await (await factory.CreateClientFor(c.Member).PostJsonAsync("/api/v1/sos", NewSos())).ExpectAsync(HttpStatusCode.Created);
        var id = created.GetProperty("id").GetGuid();
        await factory.WithDbAsync(async db =>
        {
            (await db.SosRequests.SingleAsync(s => s.Id == id)).Approve(c.SubAdmin.Id, "Verified by call", DateTime.UtcNow);
            db.SosNotifications.Add(SosNotification.Create(id, neighbour.Id, 850, DateTime.UtcNow));
            await db.SaveChangesAsync();
        });

        var asRequester = await (await factory.CreateClientFor(c.Member).GetAsync($"/api/v1/sos/{id}")).ExpectAsync(HttpStatusCode.OK);
        Assert.False(asRequester.GetProperty("requester").TryGetProperty("phoneNumber", out _));
        Assert.Equal("Verified by call", asRequester.GetProperty("reviewRemarks").GetString());

        var asAdmin = await (await factory.CreateClientFor(c.SubAdmin).GetAsync($"/api/v1/sos/{id}")).ExpectAsync(HttpStatusCode.OK);
        Assert.Equal("Admin", asAdmin.GetProperty("viewerRole").GetString());
        Assert.Equal(c.Member.PhoneNumber.Value, asAdmin.GetProperty("requester").GetProperty("phoneNumber").GetString());
        Assert.Equal("O+", asAdmin.GetProperty("requester").GetProperty("bloodGroup").GetString());

        var asNeighbour = await (await factory.CreateClientFor(neighbour).GetAsync($"/api/v1/sos/{id}")).ExpectAsync(HttpStatusCode.OK);
        Assert.Equal("Recipient", asNeighbour.GetProperty("viewerRole").GetString());
        var requester = asNeighbour.GetProperty("requester");
        Assert.Equal("Asha", requester.GetProperty("firstName").GetString());
        Assert.Equal(["firstName"], requester.EnumerateObject().Select(p => p.Name));
        Assert.False(asNeighbour.TryGetProperty("reviewRemarks", out _));
        Assert.False(asNeighbour.TryGetProperty("timeline", out _));

        await (await factory.CreateClientFor(stranger).GetAsync($"/api/v1/sos/{id}")).ExpectAsync(HttpStatusCode.NotFound);
        await (await factory.CreateClientFor(otherSub).GetAsync($"/api/v1/sos/{id}")).ExpectAsync(HttpStatusCode.NotFound);

        // Once resolved, the alerted neighbour can no longer open it.
        await (await factory.CreateClientFor(c.SubAdmin).PostJsonAsync($"/api/v1/sos/{id}/resolve", new { remarks = "Ambulance reached" })).ExpectAsync(HttpStatusCode.OK);
        await (await factory.CreateClientFor(neighbour).GetAsync($"/api/v1/sos/{id}")).ExpectAsync(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Photos_are_sanitised_stored_privately_and_capped_at_five()
    {
        var c = await NewCommunityAsync();
        var client = factory.CreateClientFor(c.Member);
        var sos = await (await client.PostJsonAsync("/api/v1/sos", NewSos())).ExpectAsync(HttpStatusCode.Created);
        var id = sos.GetProperty("id").GetGuid();

        var withPhotos = await (await client.PostAsync($"/api/v1/sos/{id}/photos", Photos(TestImages.JpegWithExif(640, 480, 1), TestImages.Jpeg(800, 600))))
            .ExpectAsync(HttpStatusCode.OK);

        var photos = withPhotos.GetProperty("photos").EnumerateArray().ToList();
        Assert.Equal(2, photos.Count);
        Assert.All(photos, p => Assert.Contains("sig=", p.GetProperty("url").GetString(), StringComparison.Ordinal));
        var stored = factory.Blobs.Blobs.Where(b => b.Key.StartsWith($"sos/{id:N}/", StringComparison.Ordinal)).ToList();
        Assert.Equal(2, stored.Count);
        Assert.All(stored, b => Assert.False(TestImages.Contains(b.Value.Content, TestImages.SecretMarker)));

        var tooMany = await (await client.PostAsync($"/api/v1/sos/{id}/photos", Photos(Enumerable.Range(0, 4).Select(_ => TestImages.Jpeg(100, 100)).ToArray())))
            .ExpectAsync(HttpStatusCode.UnprocessableEntity);
        Assert.Equal("sos.too_many_attachments", tooMany.Code());
    }

    [Fact]
    public async Task Bad_photo_rejects_request_without_leaving_orphan_blobs()
    {
        var c = await NewCommunityAsync();
        var client = factory.CreateClientFor(c.Member);
        var id = (await (await client.PostJsonAsync("/api/v1/sos", NewSos())).ExpectAsync(HttpStatusCode.Created)).GetProperty("id").GetGuid();

        var problem = await (await client.PostAsync($"/api/v1/sos/{id}/photos", Photos(TestImages.Jpeg(200, 200), "not an image"u8.ToArray())))
            .ExpectAsync(HttpStatusCode.UnprocessableEntity);

        Assert.Equal("attachment.unsupported_format", problem.Code());
        Assert.DoesNotContain(factory.Blobs.Blobs.Keys, k => k.StartsWith($"sos/{id:N}/", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Only_requester_can_add_photos_or_cancel()
    {
        var c = await NewCommunityAsync();
        var id = (await (await factory.CreateClientFor(c.Member).PostJsonAsync("/api/v1/sos", NewSos())).ExpectAsync(HttpStatusCode.Created)).GetProperty("id").GetGuid();
        var admin = factory.CreateClientFor(c.SubAdmin);

        await (await admin.PostAsync($"/api/v1/sos/{id}/photos", Photos(TestImages.Jpeg(100, 100)))).ExpectAsync(HttpStatusCode.NotFound);
        await (await admin.PostJsonAsync($"/api/v1/sos/{id}/cancel", new { })).ExpectAsync(HttpStatusCode.NotFound);

        var cancelled = await (await factory.CreateClientFor(c.Member).PostJsonAsync($"/api/v1/sos/{id}/cancel", new { reason = "False alarm" }))
            .ExpectAsync(HttpStatusCode.OK);
        Assert.Equal("Cancelled", cancelled.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Resolving_a_pending_sos_is_an_invalid_transition()
    {
        var c = await NewCommunityAsync();
        var id = (await (await factory.CreateClientFor(c.Member).PostJsonAsync("/api/v1/sos", NewSos())).ExpectAsync(HttpStatusCode.Created)).GetProperty("id").GetGuid();

        var problem = await (await factory.CreateClientFor(c.Member).PostJsonAsync($"/api/v1/sos/{id}/resolve", new { })).ExpectAsync(HttpStatusCode.UnprocessableEntity);

        Assert.Equal("sos.invalid_transition", problem.Code());
    }

    [Fact]
    public async Task Stale_gps_fix_is_rejected()
    {
        var c = await NewCommunityAsync();
        var stale = new { clientRequestId = Guid.NewGuid(), category = "Fire", latitude = 23.02, longitude = 72.57, capturedAt = DateTime.UtcNow.AddHours(-2) };

        var problem = await (await factory.CreateClientFor(c.Member).PostJsonAsync("/api/v1/sos", stale)).ExpectAsync(HttpStatusCode.UnprocessableEntity);

        Assert.Equal("sos.invalid_capture_time", problem.Code());
    }

    [Fact]
    public async Task My_sos_list_is_newest_first_with_photo_counts()
    {
        var c = await NewCommunityAsync();
        var client = factory.CreateClientFor(c.Member);
        var first = (await (await client.PostJsonAsync("/api/v1/sos", NewSos())).ExpectAsync(HttpStatusCode.Created)).GetProperty("id").GetGuid();
        await client.PostAsync($"/api/v1/sos/{first}/photos", Photos(TestImages.Jpeg(100, 100)));
        await client.PostJsonAsync($"/api/v1/sos/{first}/cancel", new { });
        var second = (await (await client.PostJsonAsync("/api/v1/sos", NewSos())).ExpectAsync(HttpStatusCode.Created)).GetProperty("id").GetGuid();

        var page = await (await client.GetAsync("/api/v1/sos/mine")).ExpectAsync(HttpStatusCode.OK);
        var items = page.GetProperty("items").EnumerateArray().ToList();

        Assert.Equal([second, first], items.Select(i => i.GetProperty("id").GetGuid()));
        Assert.Equal(1, items[1].GetProperty("photoCount").GetInt32());
    }
}
