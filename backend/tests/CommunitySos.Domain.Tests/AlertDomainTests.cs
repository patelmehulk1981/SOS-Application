using CommunitySos.Domain.Common;
using CommunitySos.Domain.Entities;
using CommunitySos.Domain.Enums;
using CommunitySos.Domain.ValueObjects;

namespace CommunitySos.Domain.Tests;

public sealed class AlertDomainTests
{
    private static readonly DateTime Now = new(2026, 11, 15, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Haversine_distance_matches_known_values()
    {
        var lawGarden = new GeoCoordinate(23.0225, 72.5714);

        Assert.Equal(0, lawGarden.DistanceMetersTo(lawGarden), precision: 6);
        // One degree of latitude ≈ 111.2 km on the mean-radius sphere.
        Assert.InRange(lawGarden.DistanceMetersTo(new GeoCoordinate(24.0225, 72.5714)), 111_150, 111_250);
        // Ahmedabad → Mumbai is about 440 km in a straight line.
        Assert.InRange(lawGarden.DistanceMetersTo(new GeoCoordinate(19.0760, 72.8777)), 435_000, 445_000);
        // Symmetric.
        var gandhinagar = new GeoCoordinate(23.2156, 72.6369);
        Assert.Equal(lawGarden.DistanceMetersTo(gandhinagar), gandhinagar.DistanceMetersTo(lawGarden), precision: 6);
    }

    [Fact]
    public void Responding_reports_when_the_requester_should_be_told()
    {
        var alert = SosNotification.Create(Guid.NewGuid(), Guid.NewGuid(), 1_200, Now);

        Assert.True(alert.Respond(ResponseType.CanHelp, null, Now));        // newly helping
        Assert.False(alert.Respond(ResponseType.OnTheWay, null, Now));      // still helping
        Assert.True(alert.Respond(ResponseType.Arrived, "At the gate", Now)); // arrival is news
        Assert.False(alert.Respond(ResponseType.Arrived, null, Now));       // repeated arrival is not
        Assert.False(alert.Respond(ResponseType.CannotHelp, null, Now));    // withdrawal
        Assert.True(alert.Respond(ResponseType.OnTheWay, null, Now));       // helping again

        Assert.Equal(Now, alert.AcknowledgedAt); // responding implies having seen it
    }

    [Fact]
    public void Response_must_be_a_real_choice_and_the_note_is_bounded()
    {
        var alert = SosNotification.Create(Guid.NewGuid(), Guid.NewGuid(), 10, Now);

        Assert.Throws<DomainException>(() => alert.Respond(ResponseType.None, null, Now));
        Assert.Throws<DomainException>(() => alert.Respond((ResponseType)42, null, Now));
        Assert.Throws<DomainException>(() => alert.Respond(ResponseType.CanHelp, new string('x', SosNotification.NoteMaxLength + 1), Now));
    }

    [Fact]
    public void Acknowledgement_keeps_the_first_time()
    {
        var alert = SosNotification.Create(Guid.NewGuid(), Guid.NewGuid(), 10, Now);
        alert.Acknowledge(Now);
        alert.Acknowledge(Now.AddMinutes(5));
        Assert.Equal(Now, alert.AcknowledgedAt);
    }

    [Fact]
    public void Delivery_outcomes_are_tracked_per_attempt()
    {
        var alert = SosNotification.Create(Guid.NewGuid(), Guid.NewGuid(), 10, Now);

        alert.MarkFailed("Unavailable");
        Assert.Equal((DeliveryStatus.Failed, 1), (alert.DeliveryStatus, alert.AttemptCount));

        alert.MarkSent(Now);
        Assert.Equal((DeliveryStatus.Sent, 2, Now), (alert.DeliveryStatus, alert.AttemptCount, alert.SentAt));
        Assert.Null(alert.LastError);

        var unreachable = SosNotification.Create(Guid.NewGuid(), Guid.NewGuid(), 10, Now);
        unreachable.MarkNoDevice("All unregistered");
        Assert.Equal(DeliveryStatus.NoDevice, unreachable.DeliveryStatus);
    }

    [Fact]
    public void Clearing_the_note_keeps_the_response()
    {
        var alert = SosNotification.Create(Guid.NewGuid(), Guid.NewGuid(), 10, Now);
        alert.Respond(ResponseType.CanHelp, "Call me on 98765 43210", Now);
        alert.ClearNote();
        Assert.Null(alert.ResponseNote);
        Assert.Equal(ResponseType.CanHelp, alert.ResponseType);
    }

    [Fact]
    public void A_device_has_exactly_one_push_target()
    {
        var legacy = UserDevice.Register(Guid.NewGuid(), "token-1", DevicePlatform.Android, "1.0.0", null, Now);
        Assert.Equal(("token-1", (string?)null), (legacy.FcmToken, legacy.InstallationId));

        legacy.AttachInstallationId("fid-1");
        Assert.Equal(((string?)null, "fid-1"), (legacy.FcmToken, legacy.InstallationId));

        var modern = UserDevice.RegisterInstallation(Guid.NewGuid(), "fid-2", DevicePlatform.Ios, null, null, Now);
        Assert.Null(modern.FcmToken);
        Assert.True(modern.IsActive);

        Assert.Throws<DomainException>(() => UserDevice.RegisterInstallation(Guid.NewGuid(), " ", DevicePlatform.Ios, null, null, Now));
        Assert.Throws<DomainException>(() => UserDevice.RegisterInstallation(Guid.NewGuid(), new string('f', UserDevice.InstallationIdMaxLength + 1), DevicePlatform.Ios, null, null, Now));
    }
}
