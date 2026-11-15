using CommunitySos.Domain.Common;
using CommunitySos.Domain.Entities;
using CommunitySos.Domain.Enums;
using static CommunitySos.Domain.Tests.TestData;

namespace CommunitySos.Domain.Tests;

public sealed class SosRequestTests
{
    private static SosRequest Raise(User requester, Guid assignedAdminId) =>
        SosRequest.Create(requester, assignedAdminId, Guid.NewGuid(), SosCategory.Medical, "Chest pain", Location, 12, Now, Now);

    [Fact]
    public void User_sos_starts_pending_and_needs_review()
    {
        var (_, subAdmin, user) = Hierarchy();

        var sos = Raise(user, subAdmin.Id);

        Assert.Equal(SosStatus.Pending, sos.Status);
        Assert.False(sos.IsAutoApproved);
        Assert.Equal(subAdmin.Id, sos.AssignedAdminId);
        Assert.Single(sos.StatusHistory);
    }

    [Fact]
    public void Sub_admin_sos_is_auto_approved()
    {
        var (master, subAdmin, _) = Hierarchy();

        var sos = Raise(subAdmin, master.Id);

        Assert.Equal(SosStatus.Approved, sos.Status);
        Assert.True(sos.IsAutoApproved);
        Assert.NotNull(sos.ReviewedAt);
        Assert.Null(sos.ReviewedByUserId);
        Assert.Collection(
            sos.StatusHistory,
            h => Assert.Equal(SosStatus.Pending, h.ToStatus),
            h => Assert.Equal(SosStatus.Approved, h.ToStatus));
    }

    [Fact]
    public void Approve_records_reviewer_and_remarks()
    {
        var (_, subAdmin, user) = Hierarchy();
        var sos = Raise(user, subAdmin.Id);

        sos.Approve(subAdmin.Id, "  Verified by phone  ", Now.AddMinutes(1));

        Assert.Equal(SosStatus.Approved, sos.Status);
        Assert.Equal(subAdmin.Id, sos.ReviewedByUserId);
        Assert.Equal("Verified by phone", sos.ReviewRemarks);
    }

    [Fact]
    public void Cannot_approve_twice()
    {
        var (_, subAdmin, user) = Hierarchy();
        var sos = Raise(user, subAdmin.Id);
        sos.Approve(subAdmin.Id, null, Now);

        var ex = Assert.Throws<DomainException>(() => sos.Approve(subAdmin.Id, null, Now));
        Assert.Equal("sos.already_reviewed", ex.Code);
    }

    [Fact]
    public void Reject_requires_remarks()
    {
        var (_, subAdmin, user) = Hierarchy();
        var sos = Raise(user, subAdmin.Id);

        Assert.Throws<DomainException>(() => sos.Reject(subAdmin.Id, "   ", Now));

        sos.Reject(subAdmin.Id, "Duplicate request", Now);
        Assert.Equal(SosStatus.Rejected, sos.Status);
        Assert.NotNull(sos.ClosedAt);
    }

    [Fact]
    public void Escalated_sos_can_still_be_approved()
    {
        var (master, subAdmin, user) = Hierarchy();
        var sos = Raise(user, subAdmin.Id);

        sos.Escalate(Now.AddMinutes(3));
        sos.Escalate(Now.AddMinutes(4)); // idempotent
        sos.Approve(master.Id, "Approved by Master Admin", Now.AddMinutes(5));

        Assert.Equal(SosStatus.Approved, sos.Status);
        // Pending -> Escalated -> Approved; the second Escalate() was a no-op.
        Assert.Equal(
            new[] { SosStatus.Pending, SosStatus.Escalated, SosStatus.Approved },
            sos.StatusHistory.Select(h => h.ToStatus));
    }

    [Fact]
    public void Only_requester_can_cancel()
    {
        var (_, subAdmin, user) = Hierarchy();
        var sos = Raise(user, subAdmin.Id);

        Assert.Throws<DomainException>(() => sos.Cancel(subAdmin.Id, null, Now));

        sos.Cancel(user.Id, "False alarm", Now);
        Assert.Equal(SosStatus.Cancelled, sos.Status);
    }

    [Fact]
    public void Closed_sos_cannot_transition()
    {
        var (_, subAdmin, user) = Hierarchy();
        var sos = Raise(user, subAdmin.Id);
        sos.Cancel(user.Id, null, Now);

        var ex = Assert.Throws<DomainException>(() => sos.Resolve(user.Id, null, Now));
        Assert.Equal("sos.invalid_transition", ex.Code);
    }

    [Fact]
    public void Stale_location_is_rejected()
    {
        var (_, subAdmin, user) = Hierarchy();

        Assert.Throws<DomainException>(() =>
            SosRequest.Create(user, subAdmin.Id, Guid.NewGuid(), SosCategory.Fire, null, Location, null, Now.AddHours(-1), Now));
    }

    [Fact]
    public void At_most_five_attachments()
    {
        var (_, subAdmin, user) = Hierarchy();
        var sos = Raise(user, subAdmin.Id);
        var sha = new string('a', 64);

        for (var i = 0; i < SosRequest.MaxAttachments; i++)
        {
            sos.AddAttachment(user.Id, $"sos/{i}.jpg", "image/jpeg", 1000, sha, 800, 600, Now);
        }

        Assert.Throws<DomainException>(() => sos.AddAttachment(user.Id, "sos/6.jpg", "image/jpeg", 1000, sha, 800, 600, Now));
    }
}
