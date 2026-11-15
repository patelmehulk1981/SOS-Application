using CommunitySos.Domain.Common;
using CommunitySos.Domain.Entities;
using CommunitySos.Domain.Enums;
using CommunitySos.Domain.ValueObjects;
using static CommunitySos.Domain.Tests.TestData;

namespace CommunitySos.Domain.Tests;

public sealed class InvitationTests
{
    [Fact]
    public void Master_admin_invites_become_sub_admins()
    {
        var master = Master();

        var invitation = Invitation.Create(master, "Ward 12 leads", null, null, Now);
        var result = invitation.Redeem(NextPhone(), "fb-1", Consent, Now, "10.0.0.1", "Pixel 9");

        Assert.Equal(UserRole.SubAdmin, invitation.InviteeRole);
        Assert.Equal(UserRole.SubAdmin, result.User.Role);
        Assert.Equal(master.Id, result.User.ReferredByAdminId);
        Assert.Equal(master.Id, result.Referral.ReferrerAdminId);
        Assert.Equal(result.User.Id, result.Referral.ReferredUserId);
        Assert.Equal(invitation.Id, result.Referral.InvitationId);
        Assert.Equal(1, invitation.UsedCount);
    }

    [Fact]
    public void Sub_admin_invites_become_normal_users()
    {
        var (_, subAdmin, user) = Hierarchy();

        Assert.Equal(UserRole.User, user.Role);
        Assert.Equal(subAdmin.Id, user.ReferredByAdminId);
    }

    [Fact]
    public void Normal_users_cannot_invite()
    {
        var (_, _, user) = Hierarchy();

        var ex = Assert.Throws<DomainException>(() => Invitation.Create(user, null, null, null, Now));
        Assert.Equal("invitation.forbidden", ex.Code);
    }

    [Fact]
    public void Suspended_sub_admin_cannot_invite()
    {
        var (_, subAdmin, _) = Hierarchy();
        subAdmin.Suspend();

        Assert.Throws<DomainException>(() => Invitation.Create(subAdmin, null, null, null, Now));
    }

    [Fact]
    public void Invitation_respects_max_uses()
    {
        var invitation = Invitation.Create(Master(), null, maxUses: 1, expiresAt: null, Now);
        invitation.Redeem(NextPhone(), "fb-a", Consent, Now, null, null);

        Assert.False(invitation.IsUsable(Now));
        var ex = Assert.Throws<DomainException>(() => invitation.Redeem(NextPhone(), "fb-b", Consent, Now, null, null));
        Assert.Equal("invitation.unusable", ex.Code);
    }

    [Fact]
    public void Invitation_expires()
    {
        var invitation = Invitation.Create(Master(), null, null, Now.AddDays(1), Now);

        Assert.True(invitation.IsUsable(Now));
        Assert.False(invitation.IsUsable(Now.AddDays(1)));
    }

    [Fact]
    public void Revoked_invitation_is_unusable()
    {
        var invitation = Invitation.Create(Master(), null, null, null, Now);
        invitation.Revoke(Now);

        Assert.False(invitation.IsUsable(Now));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(10_001)]
    public void Invalid_max_uses_is_rejected(int maxUses)
    {
        Assert.Throws<DomainException>(() => Invitation.Create(Master(), null, maxUses, null, Now));
    }

    [Fact]
    public void Generated_codes_are_well_formed()
    {
        for (var i = 0; i < 100; i++)
        {
            Assert.True(InvitationCode.IsWellFormed(InvitationCode.Generate()));
        }
    }
}
