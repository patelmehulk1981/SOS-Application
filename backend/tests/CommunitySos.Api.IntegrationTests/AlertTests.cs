using System.Net;
using CommunitySos.Application.Abstractions;
using CommunitySos.Application.Common.Notifications;
using CommunitySos.Application.Features.Sos.Jobs;
using CommunitySos.Domain.Entities;
using CommunitySos.Domain.Enums;
using CommunitySos.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CommunitySos.Api.IntegrationTests;

/// <summary>
/// Module 5: 5 km neighbour alerts, acknowledgement and responses, responder updates and closure notices.
/// Recipient selection runs through the LINQ fallback here; the SQL procedure has the same rules (Module 7 adds
/// Testcontainers coverage for it).
/// </summary>
public sealed class AlertTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static int _area;

    private sealed record Community(User Master, User SubAdmin, User Requester, double Lat, double Lng);

    /// <summary>Each test works ~55 km away from the others, so neighbours never leak between tests.</summary>
    private async Task<Community> NewCommunityAsync()
    {
        var lat = 12.0 + (Interlocked.Increment(ref _area) * 0.5);
        const double lng = 77.0;
        var master = await factory.SeedMasterAdminAsync();
        var sub = await factory.SeedInviteeAsync(master, "Ward Lead");
        var requester = await factory.SeedInviteeAsync(sub, "Asha Patel");
        await factory.WithDbAsync(async db =>
        {
            foreach (var u in new[] { sub, requester })
            {
                db.UserDevices.Add(UserDevice.RegisterInstallation(u.Id, $"fid-{u.Id:N}", DevicePlatform.Android, "1.1.0", null, DateTime.UtcNow));
            }

            await db.SaveChangesAsync();
        });
        await factory.Jobs.RunAllAsync(); // drain anything left by earlier tests
        return new Community(master, sub, requester, lat, lng);
    }

    /// <summary>A member with a location, alerts and location sharing as given, and one FID-registered phone.</summary>
    private async Task<User> NeighbourAsync(
        Community c,
        string name,
        double northMeters,
        double? accuracy = 20,
        bool alerts = true,
        bool sharing = true,
        string? fid = null)
    {
        var user = await factory.SeedInviteeAsync(c.SubAdmin, name);
        var now = DateTime.UtcNow;
        await factory.WithDbAsync(async db =>
        {
            var tracked = await db.Users.SingleAsync(u => u.Id == user.Id);
            tracked.SetAlertsEnabled(alerts);
            db.UserConsents.Add(tracked.SetLocationSharing(sharing, ApiFactory.PolicyVersion, now, null, null));
            var point = new GeoCoordinate(c.Lat + (northMeters / 111_195.0), c.Lng);
            db.UserLocations.Add(UserLocation.Create(user.Id, point, accuracy, now.AddMinutes(-5), now));
            db.UserDevices.Add(UserDevice.RegisterInstallation(user.Id, fid ?? $"fid-{user.Id:N}", DevicePlatform.Android, "1.1.0", null, now));
            await db.SaveChangesAsync();
        });
        return user;
    }

    private async Task<Guid> RaiseAsync(User requester, Community c)
    {
        var body = new { clientRequestId = Guid.NewGuid(), category = "Medical", latitude = c.Lat, longitude = c.Lng, capturedAt = DateTime.UtcNow };
        var sos = await (await factory.CreateClientFor(requester).PostJsonAsync("/api/v1/sos", body)).ExpectAsync(HttpStatusCode.Created);
        return sos.GetProperty("id").GetGuid();
    }

    private async Task<Guid> RaiseAndApproveAsync(Community c)
    {
        var sosId = await RaiseAsync(c.Requester, c);
        await (await factory.CreateClientFor(c.SubAdmin).PostJsonAsync($"/api/v1/admin/sos/{sosId}/approve", new { })).ExpectAsync(HttpStatusCode.OK);
        await factory.Jobs.RunAllAsync();
        return sosId;
    }

    private async Task RunDispatchAsync(Guid sosId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<DispatchSosAlertsJob>().RunAsync(sosId, CancellationToken.None);
    }

    private IEnumerable<PushMessage> PushesFor(Guid sosId, string type) =>
        factory.Push.Sent.Where(p => p.Message.Type == type && p.Message.Data["sosId"] == sosId.ToString()).Select(p => p.Message);

    private bool Pushed(Guid sosId, string type, User to, string? address = null) =>
        factory.Push.Sent.Any(p => p.Message.Type == type
                                   && p.Message.Data["sosId"] == sosId.ToString()
                                   && p.Targets.Any(t => t.Address == (address ?? $"fid-{to.Id:N}")));

    [Fact]
    public async Task Approval_alerts_only_eligible_neighbours_within_5_km()
    {
        var c = await NewCommunityAsync();
        var near = await NeighbourAsync(c, "Near Neighbour", 2_000);
        var edge = await NeighbourAsync(c, "Edge Neighbour", 4_900);
        var far = await NeighbourAsync(c, "Far Neighbour", 8_000);
        var vague = await NeighbourAsync(c, "Vague Fix", 1_000, accuracy: 2_000);
        var muted = await NeighbourAsync(c, "Alerts Off", 1_000, alerts: false);
        var hidden = await NeighbourAsync(c, "Sharing Off", 1_000, sharing: false);
        var suspended = await NeighbourAsync(c, "Suspended Neighbour", 1_000);
        await factory.WithDbAsync(async db =>
        {
            (await db.Users.SingleAsync(u => u.Id == suspended.Id)).Suspend();
            await db.SaveChangesAsync();
        });

        var sosId = await RaiseAndApproveAsync(c);

        var alerted = await factory.WithDbAsync(db => db.SosNotifications.Where(n => n.SosRequestId == sosId).Select(n => n.RecipientUserId).ToListAsync());
        Assert.Equal(new[] { near.Id, edge.Id }.Order(), alerted.Order());
        Assert.DoesNotContain(far.Id, alerted);
        Assert.DoesNotContain(vague.Id, alerted);
        Assert.DoesNotContain(muted.Id, alerted);
        Assert.DoesNotContain(hidden.Id, alerted);
        Assert.DoesNotContain(suspended.Id, alerted);
        Assert.DoesNotContain(c.Requester.Id, alerted);

        Assert.True(Pushed(sosId, PushMessages.Alert, near));
        Assert.True(Pushed(sosId, PushMessages.Alert, edge));
        var recipientCount = await factory.WithDbAsync(db => db.SosRequests.Where(s => s.Id == sosId).Select(s => s.RecipientCount).SingleAsync());
        Assert.Equal(2, recipientCount);
    }

    [Fact]
    public async Task Alert_push_is_urgent_and_keeps_personal_data_off_the_lock_screen()
    {
        var c = await NewCommunityAsync();
        await NeighbourAsync(c, "Ravi Shah", 1_200);
        var sosId = await RaiseAndApproveAsync(c);

        var alert = Assert.Single(PushesFor(sosId, PushMessages.Alert));
        Assert.Equal("sos-alert", alert.AndroidChannelId);
        Assert.Equal("Medical SOS near you", alert.Title);
        Assert.StartsWith("About 1 km from you.", alert.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("Asha", alert.Title + alert.Body, StringComparison.Ordinal);
        Assert.Equal(TimeSpan.FromMinutes(30), alert.TimeToLive);
        Assert.Equal($"sos.alert:{sosId:N}", alert.CollapseKey);
    }

    [Fact]
    public async Task Dispatch_is_idempotent_and_never_alerts_anyone_twice()
    {
        var c = await NewCommunityAsync();
        var neighbour = await NeighbourAsync(c, "Once Only", 500);
        var sosId = await RaiseAndApproveAsync(c);

        // A Hangfire retry or a duplicate enqueue runs the job again.
        await RunDispatchAsync(sosId);

        Assert.Single(PushesFor(sosId, PushMessages.Alert));
        var rows = await factory.WithDbAsync(db => db.SosNotifications.CountAsync(n => n.SosRequestId == sosId && n.RecipientUserId == neighbour.Id));
        Assert.Equal(1, rows);
    }

    [Fact]
    public async Task Admins_own_sos_is_auto_approved_and_alerts_immediately()
    {
        var c = await NewCommunityAsync();
        var neighbour = await NeighbourAsync(c, "Quick Help", 800);

        var sosId = await RaiseAsync(c.SubAdmin, c);
        Assert.Contains("DispatchSosAlertsJob.RunAsync", factory.Jobs.Enqueued);
        await factory.Jobs.RunAllAsync();

        Assert.True(Pushed(sosId, PushMessages.Alert, neighbour));
    }

    [Fact]
    public async Task Unregistered_device_is_deactivated_and_the_recipient_marked_no_device()
    {
        var c = await NewCommunityAsync();
        var gone = await NeighbourAsync(c, "Old Phone", 900, fid: $"dead-{Guid.NewGuid():N}");

        var sosId = await RaiseAndApproveAsync(c);

        var (status, active) = await factory.WithDbAsync(async db => (
            await db.SosNotifications.Where(n => n.SosRequestId == sosId && n.RecipientUserId == gone.Id).Select(n => n.DeliveryStatus).SingleAsync(),
            await db.UserDevices.Where(d => d.UserId == gone.Id).Select(d => d.IsActive).SingleAsync()));
        Assert.Equal(DeliveryStatus.NoDevice, status);
        Assert.False(active);
    }

    [Fact]
    public async Task Transient_push_failure_is_retried_for_the_failed_recipient_only()
    {
        var c = await NewCommunityAsync();
        var ok = await NeighbourAsync(c, "Good Signal", 600);
        var flaky = await NeighbourAsync(c, "Bad Signal", 700, fid: $"flaky-{Guid.NewGuid():N}");
        var sosId = await RaiseAsync(c.Requester, c);
        await (await factory.CreateClientFor(c.SubAdmin).PostJsonAsync($"/api/v1/admin/sos/{sosId}/approve", new { })).ExpectAsync(HttpStatusCode.OK);

        await Assert.ThrowsAsync<PushDeliveryException>(() => factory.Jobs.RunAllAsync()); // Hangfire would retry
        await factory.Jobs.RunAllAsync(); // the rest of the queue

        var rows = await factory.WithDbAsync(db => db.SosNotifications.Where(n => n.SosRequestId == sosId).ToListAsync());
        Assert.Equal(DeliveryStatus.Sent, rows.Single(n => n.RecipientUserId == ok.Id).DeliveryStatus);
        var failed = rows.Single(n => n.RecipientUserId == flaky.Id);
        Assert.Equal(DeliveryStatus.Failed, failed.DeliveryStatus);
        Assert.Equal(1, failed.AttemptCount);

        // The retry pushes only the failed recipient.
        var before = PushesFor(sosId, PushMessages.Alert).Count();
        await Assert.ThrowsAsync<PushDeliveryException>(() => RunDispatchAsync(sosId));
        var retry = factory.Push.Sent.Where(p => p.Message.Type == PushMessages.Alert && p.Message.Data["sosId"] == sosId.ToString()).Skip(before).ToList();
        Assert.All(retry, p => Assert.All(p.Targets, t => Assert.StartsWith("flaky-", t.Address, StringComparison.Ordinal)));
    }

    [Fact]
    public async Task Recipient_sees_a_limited_view_acknowledges_and_responds()
    {
        var c = await NewCommunityAsync();
        var helper = await NeighbourAsync(c, "Ravi Shah", 1_500);
        var sosId = await RaiseAndApproveAsync(c);
        var client = factory.CreateClientFor(helper);

        var view = await (await client.GetAsync($"/api/v1/sos/{sosId}")).ExpectAsync(HttpStatusCode.OK);
        Assert.Equal("Recipient", view.GetProperty("viewerRole").GetString());
        Assert.Equal("Asha", view.GetProperty("requester").GetProperty("firstName").GetString());
        Assert.False(view.GetProperty("requester").TryGetProperty("phoneNumber", out _));
        Assert.False(view.TryGetProperty("responders", out _));
        var myAlert = view.GetProperty("myAlert");
        Assert.InRange(myAlert.GetProperty("distanceMeters").GetDouble(), 1_450, 1_550);
        Assert.Equal("None", myAlert.GetProperty("response").GetString());

        await (await client.PostAsync($"/api/v1/sos/{sosId}/acknowledge", null)).ExpectAsync(HttpStatusCode.NoContent);
        await (await client.PostAsync($"/api/v1/sos/{sosId}/acknowledge", null)).ExpectAsync(HttpStatusCode.NoContent); // idempotent

        var responded = await (await client.PutJsonAsync($"/api/v1/sos/{sosId}/response", new { response = "OnTheWay", note = "Nurse, 5 minutes away" }))
            .ExpectAsync(HttpStatusCode.OK);
        Assert.Equal("OnTheWay", responded.GetProperty("myAlert").GetProperty("response").GetString());

        await factory.Jobs.RunAllAsync();
        var update = Assert.Single(PushesFor(sosId, PushMessages.ResponderUpdate));
        Assert.Equal("Someone nearby is responding", update.Title);
        Assert.True(Pushed(sosId, PushMessages.ResponderUpdate, c.Requester));
        Assert.DoesNotContain("Ravi", update.Title + update.Body, StringComparison.Ordinal);

        // The requester sees who is coming (first name, distance, note) and the counts.
        var requesterView = await (await factory.CreateClientFor(c.Requester).GetAsync($"/api/v1/sos/{sosId}")).ExpectAsync(HttpStatusCode.OK);
        var responders = requesterView.GetProperty("responders");
        Assert.Equal(1, responders.GetProperty("alerted").GetInt32());
        Assert.Equal(1, responders.GetProperty("acknowledged").GetInt32());
        Assert.Equal(1, responders.GetProperty("onTheWay").GetInt32());
        var person = Assert.Single(responders.GetProperty("helping").EnumerateArray());
        Assert.Equal("Ravi", person.GetProperty("firstName").GetString());
        Assert.Equal("Nurse, 5 minutes away", person.GetProperty("note").GetString());
        Assert.False(person.TryGetProperty("phoneNumber", out _));
    }

    [Fact]
    public async Task Changing_a_response_only_pushes_the_requester_when_help_is_newly_offered_or_arrives()
    {
        var c = await NewCommunityAsync();
        var helper = await NeighbourAsync(c, "Meera Joshi", 1_000);
        var sosId = await RaiseAndApproveAsync(c);
        var client = factory.CreateClientFor(helper);

        async Task RespondAsync(string response)
        {
            await (await client.PutJsonAsync($"/api/v1/sos/{sosId}/response", new { response })).ExpectAsync(HttpStatusCode.OK);
            await factory.Jobs.RunAllAsync();
        }

        await RespondAsync("CanHelp");    // newly helping: push
        await RespondAsync("OnTheWay");   // still helping: no push
        await RespondAsync("Arrived");    // arrival: push
        await RespondAsync("CannotHelp"); // withdrawal: no push

        Assert.Equal(2, PushesFor(sosId, PushMessages.ResponderUpdate).Count());
    }

    [Fact]
    public async Task Only_alerted_people_can_respond_and_only_while_the_sos_is_open()
    {
        var c = await NewCommunityAsync();
        var helper = await NeighbourAsync(c, "Late Responder", 1_000);
        var stranger = await factory.SeedInviteeAsync(c.SubAdmin, "Not Alerted");
        var sosId = await RaiseAndApproveAsync(c);

        await (await factory.CreateClientFor(stranger).PutJsonAsync($"/api/v1/sos/{sosId}/response", new { response = "CanHelp" }))
            .ExpectAsync(HttpStatusCode.NotFound);
        await (await factory.CreateClientFor(stranger).PostAsync($"/api/v1/sos/{sosId}/acknowledge", null)).ExpectAsync(HttpStatusCode.NotFound);

        var invalid = await (await factory.CreateClientFor(helper).PutJsonAsync($"/api/v1/sos/{sosId}/response", new { response = "None" }))
            .ExpectAsync(HttpStatusCode.BadRequest);
        Assert.True(invalid.GetProperty("errors").TryGetProperty("response", out _));

        await (await factory.CreateClientFor(c.Requester).PostJsonAsync($"/api/v1/sos/{sosId}/resolve", new { remarks = "Ambulance arrived" }))
            .ExpectAsync(HttpStatusCode.OK);

        var closed = await (await factory.CreateClientFor(helper).PutJsonAsync($"/api/v1/sos/{sosId}/response", new { response = "OnTheWay" }))
            .ExpectAsync(HttpStatusCode.UnprocessableEntity);
        Assert.Equal("sos.not_active", closed.Code());
    }

    [Fact]
    public async Task Closing_an_approved_sos_tells_everyone_alerted_and_replaces_the_alert_banner()
    {
        var c = await NewCommunityAsync();
        var a = await NeighbourAsync(c, "First Alerted", 700);
        var b = await NeighbourAsync(c, "Second Alerted", 1_700);
        var sosId = await RaiseAndApproveAsync(c);

        await (await factory.CreateClientFor(c.Requester).PostJsonAsync($"/api/v1/sos/{sosId}/cancel", new { reason = "False alarm" }))
            .ExpectAsync(HttpStatusCode.OK);
        await factory.Jobs.RunAllAsync();

        Assert.True(Pushed(sosId, PushMessages.Closed, a));
        Assert.True(Pushed(sosId, PushMessages.Closed, b));
        var closed = PushesFor(sosId, PushMessages.Closed).First();
        Assert.Equal($"sos.alert:{sosId:N}", closed.CollapseKey); // same key as the alert

        // Neighbours can no longer open the closed SOS, and the feed hides its address.
        await (await factory.CreateClientFor(a).GetAsync($"/api/v1/sos/{sosId}")).ExpectAsync(HttpStatusCode.NotFound);
        var feed = await (await factory.CreateClientFor(a).GetAsync("/api/v1/sos/alerts")).ExpectAsync(HttpStatusCode.OK);
        var item = feed.GetProperty("items").EnumerateArray().Single(i => i.GetProperty("sosId").GetGuid() == sosId);
        Assert.Equal("Cancelled", item.GetProperty("status").GetString());
        Assert.False(item.TryGetProperty("address", out _));
    }

    [Fact]
    public async Task Alerts_feed_lists_my_alerts_and_can_show_open_ones_only()
    {
        var c = await NewCommunityAsync();
        var me = await NeighbourAsync(c, "Feed Reader", 1_000);
        var first = await RaiseAndApproveAsync(c);
        await (await factory.CreateClientFor(c.Requester).PostJsonAsync($"/api/v1/sos/{first}/resolve", new { })).ExpectAsync(HttpStatusCode.OK);
        var second = await RaiseAndApproveAsync(c);
        var client = factory.CreateClientFor(me);

        var all = await (await client.GetAsync("/api/v1/sos/alerts")).ExpectAsync(HttpStatusCode.OK);
        var ids = all.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("sosId").GetGuid()).ToList();
        Assert.Equal([second, first], ids);

        var open = await (await client.GetAsync("/api/v1/sos/alerts?openOnly=true")).ExpectAsync(HttpStatusCode.OK);
        var openItem = Assert.Single(open.GetProperty("items").EnumerateArray());
        Assert.Equal(second, openItem.GetProperty("sosId").GetGuid());
        Assert.Equal(FakeGeocoder.Address, openItem.GetProperty("address").GetString());
    }

    [Fact]
    public async Task Responders_are_listed_without_the_note_after_their_account_is_deleted()
    {
        var c = await NewCommunityAsync();
        var helper = await NeighbourAsync(c, "Soon Gone", 1_000);
        var sosId = await RaiseAndApproveAsync(c);
        var client = factory.CreateClientFor(helper);
        await (await client.PutJsonAsync($"/api/v1/sos/{sosId}/response", new { response = "CanHelp", note = "Call me on 98765 43210" }))
            .ExpectAsync(HttpStatusCode.OK);

        await (await client.DeleteAsync("/api/v1/me")).ExpectAsync(HttpStatusCode.NoContent);

        var view = await (await factory.CreateClientFor(c.Requester).GetAsync($"/api/v1/sos/{sosId}")).ExpectAsync(HttpStatusCode.OK);
        var person = Assert.Single(view.GetProperty("responders").GetProperty("helping").EnumerateArray());
        Assert.False(person.TryGetProperty("firstName", out _));
        Assert.False(person.TryGetProperty("note", out _));
        Assert.Equal("CanHelp", person.GetProperty("response").GetString());
    }
}
