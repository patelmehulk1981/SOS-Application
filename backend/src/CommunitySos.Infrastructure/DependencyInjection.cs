using CommunitySos.Application.Abstractions;
using CommunitySos.Infrastructure.Identity;
using CommunitySos.Infrastructure.Persistence;
using CommunitySos.Infrastructure.Persistence.Interceptors;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace CommunitySos.Infrastructure;

public static class DependencyInjection
{
    public const string ConnectionStringName = "Sql";

    /// <summary>Everything Infrastructure provides: persistence, identity (JWT + Firebase), and later storage/push/jobs.</summary>
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        services.AddPersistence(configuration);
        services.AddIdentityServices(configuration, environment);
        services.AddPlatformServices(configuration);
        return services;
    }

    public static IServiceCollection AddPersistence(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString(ConnectionStringName);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException($"Connection string '{ConnectionStringName}' is not configured.");
        }

        services.TryAddSingleton(TimeProvider.System);
        // The API registers an HTTP-based ICurrentUser before calling this; TryAdd keeps it.
        services.TryAddScoped<ICurrentUser, SystemCurrentUser>();

        services.AddScoped<TimestampInterceptor>();
        services.AddScoped<AuditInterceptor>();

        services.AddDbContext<AppDbContext>((sp, options) =>
        {
            options.UseSqlServer(connectionString, sql =>
            {
                sql.UseNetTopologySuite();
                // Transient-fault retries (Azure SQL). Explicit transactions must be wrapped in
                // db.Database.CreateExecutionStrategy().ExecuteAsync(...).
                sql.EnableRetryOnFailure(maxRetryCount: 5, maxRetryDelay: TimeSpan.FromSeconds(10), errorNumbersToAdd: null);
                sql.CommandTimeout(30);
            });

            // Order matters: timestamps are set before the audit snapshot is taken.
            options.AddInterceptors(
                sp.GetRequiredService<TimestampInterceptor>(),
                sp.GetRequiredService<AuditInterceptor>());
        });

        services.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<AppDbContext>());

        services.AddOptions<SeedOptions>().Bind(configuration.GetSection(SeedOptions.SectionName));
        services.AddScoped<DatabaseInitializer>();

        return services;
    }
}
