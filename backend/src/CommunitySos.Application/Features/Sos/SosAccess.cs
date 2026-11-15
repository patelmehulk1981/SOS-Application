using CommunitySos.Domain.Entities;
using CommunitySos.Domain.Enums;

namespace CommunitySos.Application.Features.Sos;

/// <summary>
/// Who may do what with a specific SOS. Pure rules, shared by every use case (and unit-testable):
/// <list type="bullet">
/// <item>Review (approve/reject): the assigned Sub-Admin, or the Master Admin (covers escalations).</item>
/// <item>View: requester, reviewers, or a user alerted about it while it is Approved (checked against the DB by the caller).</item>
/// <item>Cancel / add photos: requester only. Resolve: requester or a reviewer.</item>
/// </list>
/// </summary>
public static class SosAccess
{
    public static bool IsRequester(Guid userId, SosRequest sos) => sos.RequesterUserId == userId;

    public static bool CanReview(Guid userId, UserRole role, SosRequest sos) =>
        role == UserRole.MasterAdmin || (role == UserRole.SubAdmin && sos.AssignedAdminId == userId);

    public static bool CanCancel(Guid userId, SosRequest sos) => IsRequester(userId, sos);

    public static bool CanResolve(Guid userId, UserRole role, SosRequest sos) =>
        IsRequester(userId, sos) || CanReview(userId, role, sos);

    /// <summary>Alerted users may see an SOS only while help is still needed - no browsing past emergencies.</summary>
    public static bool RecipientMayView(SosRequest sos) => sos.Status == SosStatus.Approved;
}
