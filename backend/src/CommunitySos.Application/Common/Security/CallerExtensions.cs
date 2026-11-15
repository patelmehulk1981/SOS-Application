using CommunitySos.Application.Abstractions;
using CommunitySos.Application.Common.Exceptions;
using CommunitySos.Domain.Enums;

namespace CommunitySos.Application.Common.Security;

/// <summary>
/// Defence in depth: controllers already enforce role policies, but use cases re-check the caller so
/// they are safe to reuse from other entry points (jobs, future admin web).
/// </summary>
public static class CallerExtensions
{
    public static (Guid UserId, UserRole Role) RequireUser(this ICurrentUser caller) =>
        caller is { UserId: { } id, Role: { } role }
            ? (id, role)
            : throw new AuthenticationFailedException("unauthenticated", "Sign in to continue.");

    public static (Guid UserId, UserRole Role) RequireAdmin(this ICurrentUser caller)
    {
        var (id, role) = caller.RequireUser();
        return role is UserRole.SubAdmin or UserRole.MasterAdmin
            ? (id, role)
            : throw new ForbiddenException("forbidden", "Only admins can do this.");
    }

    public static Guid RequireMasterAdmin(this ICurrentUser caller)
    {
        var (id, role) = caller.RequireUser();
        return role == UserRole.MasterAdmin
            ? id
            : throw new ForbiddenException("forbidden", "Only the Master Admin can do this.");
    }
}
