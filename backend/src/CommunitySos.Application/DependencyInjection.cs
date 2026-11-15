using CommunitySos.Application.Common.Notifications;
using CommunitySos.Application.Features.Admin;
using CommunitySos.Application.Features.Auth;
using CommunitySos.Application.Features.Invitations;
using CommunitySos.Application.Features.Me;
using CommunitySos.Application.Features.Sos;
using CommunitySos.Application.Features.Sos.Jobs;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;

namespace CommunitySos.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        // Picks up every AbstractValidator<T> in this assembly; the API runs them before each action.
        services.AddValidatorsFromAssembly(typeof(DependencyInjection).Assembly, includeInternalTypes: true);

        // Use cases (one scoped service per feature area).
        services.AddScoped<AuthService>();
        services.AddScoped<ProfileService>();
        services.AddScoped<DeviceService>();
        services.AddScoped<AccountService>();
        services.AddScoped<InvitationService>();
        services.AddScoped<AdminUserService>();
        services.AddScoped<ReferralService>();
        services.AddScoped<SosService>();
        services.AddScoped<LocationService>();
        services.AddScoped<LocationKeyService>();
        services.AddScoped<SosReviewService>();
        services.AddScoped<AlertService>();
        services.AddScoped<UserNotifier>();

        // Background jobs (resolved by the job runner in their own DI scope).
        services.AddScoped<GeocodeSosJob>();
        services.AddScoped<NotifyAdminOfNewSosJob>();
        services.AddScoped<EscalateSosJob>();
        services.AddScoped<NotifyRequesterOfReviewJob>();
        services.AddScoped<ExpireStaleSosJob>();
        services.AddScoped<DispatchSosAlertsJob>();
        services.AddScoped<NotifyRequesterOfResponseJob>();
        services.AddScoped<NotifyRespondersOfClosureJob>();
        services.AddScoped<Features.Retention.RetentionPurgeJob>();

        return services;
    }
}
