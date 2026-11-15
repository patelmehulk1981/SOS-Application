using CommunitySos.Domain.Entities;
using CommunitySos.Domain.ValueObjects;

namespace CommunitySos.Domain.Tests;

internal static class TestData
{
    public static readonly DateTime Now = new(2026, 11, 14, 10, 0, 0, DateTimeKind.Utc);

    private static int _phoneSeq = 100000;

    public static PhoneNumber NextPhone() => PhoneNumber.Create($"+91987{Interlocked.Increment(ref _phoneSeq):D6}");

    public const string PolicyVersion = "2026-11";

    public static readonly RegistrationConsent Consent = new(PolicyVersion, ConfirmedAdult: true, "android/1.0.0");

    public static User Master() => User.CreateMasterAdmin(NextPhone(), "Master");

    public static User InviteFrom(User inviter)
    {
        var invitation = Invitation.Create(inviter, null, null, null, Now);
        return invitation.Redeem(NextPhone(), $"fb-{Guid.NewGuid():N}", Consent, Now, null, null).User;
    }

    public static (User Master, User SubAdmin, User User) Hierarchy()
    {
        var master = Master();
        var subAdmin = InviteFrom(master);
        var user = InviteFrom(subAdmin);
        return (master, subAdmin, user);
    }

    public static readonly GeoCoordinate Location = new(23.022505, 72.571365);
}
