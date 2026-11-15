using CommunitySos.Domain.Enums;
using Microsoft.AspNetCore.Authorization;

namespace CommunitySos.Api.Security;

/// <summary>Named authorization policies. Every endpoint requires an authenticated user unless marked [AllowAnonymous].</summary>
public static class Policies
{
    /// <summary>Sub-Admin or Master Admin.</summary>
    public const string Admin = nameof(Admin);

    public const string MasterAdmin = nameof(MasterAdmin);

    public static IServiceCollection AddApiAuthorization(this IServiceCollection services)
    {
        services.AddAuthorizationBuilder()
            // Secure by default: forgetting [Authorize] can never expose an endpoint.
            .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build())
            .AddPolicy(Admin, p => p.RequireAuthenticatedUser().RequireRole(nameof(UserRole.SubAdmin), nameof(UserRole.MasterAdmin)))
            .AddPolicy(MasterAdmin, p => p.RequireAuthenticatedUser().RequireRole(nameof(UserRole.MasterAdmin)));

        // Per-SOS (resource) rules live in Application/Features/Sos/SosAccess so every use case shares them.
        return services;
    }
}
