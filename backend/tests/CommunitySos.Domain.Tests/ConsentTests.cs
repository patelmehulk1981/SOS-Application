using CommunitySos.Domain.Common;
using CommunitySos.Domain.Entities;
using CommunitySos.Domain.Enums;
using static CommunitySos.Domain.Tests.TestData;

namespace CommunitySos.Domain.Tests;

public sealed class ConsentTests
{
    [Fact]
    public void Registration_records_terms_consent_and_adult_declaration()
    {
        var invitation = Invitation.Create(Master(), null, null, null, Now);

        var result = invitation.Redeem(NextPhone(), "fb-1", Consent, Now, "10.0.0.1", null);

        Assert.Equal(ConsentType.TermsAndPrivacy, result.TermsConsent.Type);
        Assert.True(result.TermsConsent.Granted);
        Assert.Equal(PolicyVersion, result.TermsConsent.PolicyVersion);
        Assert.Equal(result.User.Id, result.TermsConsent.UserId);
        Assert.True(result.User.HasAcceptedPolicy(PolicyVersion));
        Assert.NotNull(result.User.AdultConfirmedAt);
    }

    [Fact]
    public void Minors_cannot_register()
    {
        var invitation = Invitation.Create(Master(), null, null, null, Now);
        var minor = Consent with { ConfirmedAdult = false };

        var ex = Assert.Throws<DomainException>(() => invitation.Redeem(NextPhone(), "fb-1", minor, Now, null, null));

        Assert.Equal("registration.adult_required", ex.Code);
        Assert.Equal(0, invitation.UsedCount);
    }

    [Fact]
    public void Seeded_master_admin_must_accept_policy_on_first_login()
    {
        var master = Master();
        Assert.False(master.HasAcceptedPolicy(PolicyVersion));

        var consent = master.AcceptPolicy(PolicyVersion, confirmedAdult: true, Now, null, "android/1.0.0");

        Assert.True(master.HasAcceptedPolicy(PolicyVersion));
        Assert.Equal(master.Id, consent.UserId);
    }

    [Fact]
    public void Policy_update_requires_re_acceptance()
    {
        var (_, _, user) = Hierarchy();

        Assert.False(user.HasAcceptedPolicy("2027-01"));
        user.AcceptPolicy("2027-01", confirmedAdult: false, Now, null, null); // adult already declared
        Assert.True(user.HasAcceptedPolicy("2027-01"));
    }

    [Fact]
    public void Location_sharing_grant_and_withdrawal_are_both_recorded()
    {
        var (_, _, user) = Hierarchy();

        var granted = user.SetLocationSharing(true, PolicyVersion, Now, null, null);
        var withdrawn = user.SetLocationSharing(false, PolicyVersion, Now.AddDays(1), null, null);

        Assert.True(granted.Granted);
        Assert.False(withdrawn.Granted);
        Assert.Equal(ConsentType.LocationSharing, withdrawn.Type);
        Assert.False(user.LocationSharingEnabled);
    }
}
