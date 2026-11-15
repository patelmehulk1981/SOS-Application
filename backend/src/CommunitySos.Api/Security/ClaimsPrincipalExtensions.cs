using System.Security.Claims;
using CommunitySos.Application.Common.Security;
using CommunitySos.Domain.Enums;

namespace CommunitySos.Api.Security;

public static class ClaimsPrincipalExtensions
{
    public static Guid? GetUserId(this ClaimsPrincipal principal) =>
        Guid.TryParse(principal.FindFirstValue(AppClaimTypes.Subject), out var id) ? id : null;

    public static UserRole? GetRole(this ClaimsPrincipal principal) =>
        Enum.TryParse<UserRole>(principal.FindFirstValue(AppClaimTypes.Role), ignoreCase: false, out var role)
        && Enum.IsDefined(role)
            ? role
            : null;
}
