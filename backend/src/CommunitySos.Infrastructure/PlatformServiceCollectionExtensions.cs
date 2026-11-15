using Azure.Identity;
using Azure.Storage.Blobs;
using CommunitySos.Application.Abstractions;
using CommunitySos.Infrastructure.Geo;
using CommunitySos.Infrastructure.Jobs;
using CommunitySos.Infrastructure.Media;
using CommunitySos.Infrastructure.Notifications;
using CommunitySos.Infrastructure.Persistence;
using CommunitySos.Infrastructure.Storage;
using Hangfire;
using Hangfire.SqlServer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace CommunitySos.Infrastructure;

/// <summary>Blob storage, image processing, geocoding and background jobs.</summary>
public static class PlatformServiceCollectionExtensions
{
    public static IServiceCollection AddPlatformServices(this IServiceCollection services, IConfiguration configuration)
    {
        // ---- Blob storage ----
        services.AddOptions<BlobStorageOptions>().Bind(configuration.GetSection(BlobStorageOptions.SectionName));
        services.AddSingleton(sp =>
        {
            var o = sp.GetRequiredService<IOptions<BlobStorageOptions>>().Value;
            if (!string.IsNullOrWhiteSpace(o.ConnectionString))
            {
                return new BlobServiceClient(o.ConnectionString);
            }

            return o.ServiceUri is not null
                ? new BlobServiceClient(o.ServiceUri, new DefaultAzureCredential())
                : throw new InvalidOperationException("Configure BlobStorage:ServiceUri (Managed Identity) or BlobStorage:ConnectionString.");
        });
        services.AddSingleton<IBlobStorage, AzureBlobStorage>();

        // ---- Images ----
        services.AddSingleton<IImageProcessor, SkiaImageProcessor>();

        // ---- Geocoding ----
        services.AddOptions<GoogleMapsOptions>().Bind(configuration.GetSection(GoogleMapsOptions.SectionName));
        if (string.IsNullOrWhiteSpace(configuration[$"{GoogleMapsOptions.SectionName}:{nameof(GoogleMapsOptions.ApiKey)}"]))
        {
            services.AddSingleton<IGeocoder, NullGeocoder>();
        }
        else
        {
            services.AddHttpClient<IGeocoder, GoogleGeocoder>(client =>
            {
                client.BaseAddress = new Uri("https://maps.googleapis.com/");
                client.Timeout = TimeSpan.FromSeconds(5);
            });
        }

        // ---- Background jobs (Hangfire) ----
        var storage = configuration["Jobs:Storage"] ?? "SqlServer";
        services.AddHangfire(config =>
        {
            config
                .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
                .UseSimpleAssemblyNameTypeSerializer()
                .UseRecommendedSerializerSettings();

            if (string.Equals(storage, "InMemory", StringComparison.OrdinalIgnoreCase))
            {
                config.UseInMemoryStorage();
            }
            else
            {
                config.UseSqlServerStorage(
                    configuration.GetConnectionString(DependencyInjection.ConnectionStringName),
                    new SqlServerStorageOptions
                    {
                        SchemaName = DatabaseInitializer.JobsSchema,
                        // Azure: false. The deploy step (--migrate) installs the schema, so the app's database
                        // user needs no DDL rights (docs/07). Local development: true.
                        PrepareSchemaIfNecessary = configuration.GetValue("Jobs:PrepareSchema", defaultValue: true),
                        QueuePollInterval = TimeSpan.Zero, // 1.8 default: near-instant pickup without polling cost
                        DisableGlobalLocks = true,
                    });
            }
        });

        if (configuration.GetValue("Jobs:RunServer", defaultValue: true))
        {
            services.AddHangfireServer(options =>
            {
                options.Queues = [HangfireJobScheduler.CriticalQueue, HangfireJobScheduler.DefaultQueue]; // critical first
                options.WorkerCount = configuration.GetValue("Jobs:WorkerCount", Math.Max(4, Environment.ProcessorCount * 2));
            });
        }

        services.AddSingleton<IJobScheduler, HangfireJobScheduler>();

        // ---- Push (FCM). Development with the fake Firebase verifier logs pushes instead of sending. ----
        if (configuration.GetValue<bool>("Firebase:UseDevelopmentVerifier"))
        {
            services.AddSingleton<IPushSender, LoggingPushSender>();
        }
        else
        {
            services.AddSingleton<IPushSender, FcmPushSender>();
        }

        // ---- Read models ----
        services.AddScoped<IDashboardReader, DashboardReader>();
        services.AddScoped<IAlertRecipientSelector, AlertRecipientSelector>();
        services.AddScoped<IAuditLogPurger, AuditLogPurger>();

        return services;
    }
}
