using System.Security.Cryptography;
using CommunitySos.Application.Abstractions;
using CommunitySos.Domain.Entities;
using CommunitySos.Domain.ValueObjects;
using CommunitySos.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Testcontainers.MsSql;

namespace CommunitySos.Api.IntegrationTests.SqlServer;

/// <summary>
/// Where the SqlServer tests get a database:
///   SQL_TESTS_CONNECTION  an existing SQL Server (a throwaway database is created on it), or
///   Docker                a disposable SQL Server 2022 container (Testcontainers).
/// Without either they are skipped locally. CI sets SQL_TESTS_REQUIRED=1 so a missing Docker fails instead.
/// </summary>
public static class SqlServerAvailability
{
    public const string Category = "SqlServer";

    public static string? ExistingServer => Environment.GetEnvironmentVariable("SQL_TESTS_CONNECTION") is { Length: > 0 } cs ? cs : null;

    public static bool Required => Environment.GetEnvironmentVariable("SQL_TESTS_REQUIRED") == "1";

    public static bool CanRun => Required || ExistingServer is not null || DockerLooksAvailable();

    private static bool DockerLooksAvailable()
    {
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DOCKER_HOST")))
        {
            return true;
        }

        try
        {
            return OperatingSystem.IsWindows()
                ? Directory.GetFiles(@"\\.\pipe\").Any(p => p.EndsWith("docker_engine", StringComparison.OrdinalIgnoreCase))
                : File.Exists("/var/run/docker.sock");
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }
}

/// <summary>A test that needs real SQL Server; skipped (not failed) when none is available, unless SQL_TESTS_REQUIRED=1.</summary>
public sealed class SqlServerFactAttribute : FactAttribute
{
    public SqlServerFactAttribute()
    {
        if (!SqlServerAvailability.CanRun)
        {
            Skip = "No SQL Server: start Docker, or set SQL_TESTS_CONNECTION.";
        }
    }
}

[CollectionDefinition(Name)]
public sealed class SqlServerCollection : ICollectionFixture<SqlServerFixture>
{
    public const string Name = "SqlServer";
}

/// <summary>One database for the whole collection: migrated with the production initializer (migrations + T-SQL scripts).</summary>
public sealed class SqlServerFixture : IAsyncLifetime
{
    /// <summary>Override with SQL_TESTS_IMAGE to test against another CU.</summary>
    private static readonly string Image = Environment.GetEnvironmentVariable("SQL_TESTS_IMAGE") ?? "mcr.microsoft.com/mssql/server:2022-latest";

    private MsSqlContainer? _container;
    private SqlServerApiFactory? _factory;

    public SqlServerApiFactory Factory => _factory ?? throw new InvalidOperationException("SQL Server is not available.");

    /// <summary>The single Master Admin (the database allows only one).</summary>
    public User Master { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        if (!SqlServerAvailability.CanRun)
        {
            return;
        }

        string connectionString;
        if (SqlServerAvailability.ExistingServer is { } server)
        {
            var builder = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(server) { InitialCatalog = $"CommunitySosTests_{Guid.NewGuid():N}" };
            connectionString = builder.ConnectionString;
        }
        else
        {
            _container = new MsSqlBuilder(Image).Build();
            await _container.StartAsync();
            var builder = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(_container.GetConnectionString()) { InitialCatalog = "CommunitySos" };
            connectionString = builder.ConnectionString;
        }

        _factory = new SqlServerApiFactory(connectionString);
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<DatabaseInitializer>().InitializeAsync();
        }

        Master = User.CreateMasterAdmin(PhoneNumber.Create(ApiFactory.RandomPhone()), "Sql Master");
        var consent = Master.AcceptPolicy(ApiFactory.PolicyVersion, true, DateTime.UtcNow, null, null);
        await _factory.WithDbAsync(async db =>
        {
            db.Users.Add(Master);
            db.UserConsents.Add(consent);
            await db.SaveChangesAsync();
        });
    }

    public async Task DisposeAsync()
    {
        if (_factory is not null)
        {
            if (SqlServerAvailability.ExistingServer is not null)
            {
                await _factory.WithDbAsync(db => db.Database.EnsureDeletedAsync());
            }

            await _factory.DisposeAsync();
        }

        if (_container is not null)
        {
            await _container.DisposeAsync();
        }
    }
}

/// <summary>The real API composition on SQL Server; only outward-facing services (Firebase, FCM, Blob, geocoding, Hangfire) are faked.</summary>
public sealed class SqlServerApiFactory(string connectionString) : WebApplicationFactory<Program>
{
    private static readonly string SigningKeyPem = CreateKeyPem();

    public FakePushSender Push { get; } = new();

    public InMemoryBlobStorage Blobs { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Sql", connectionString);
        builder.UseSetting("Jwt:SigningKeyPem", SigningKeyPem);
        builder.UseSetting("Serilog:MinimumLevel:Default", "Error");
        builder.UseSetting("Jobs:Storage", "InMemory");
        builder.UseSetting("Jobs:RunServer", "false");

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IFirebaseAuthService>();
            services.AddSingleton<IFirebaseAuthService, FakeFirebase>();
            services.RemoveAll<IBlobStorage>();
            services.AddSingleton<IBlobStorage>(Blobs);
            services.RemoveAll<IPushSender>();
            services.AddSingleton<IPushSender>(Push);
            services.RemoveAll<IGeocoder>();
            services.AddSingleton<IGeocoder, FakeGeocoder>();
            services.RemoveAll<IJobScheduler>();
            services.AddSingleton<RecordingJobScheduler>();
            services.AddSingleton<IJobScheduler>(sp => sp.GetRequiredService<RecordingJobScheduler>());
        });
    }

    public async Task<T> WithDbAsync<T>(Func<AppDbContext, Task<T>> action)
    {
        await using var scope = Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    public Task WithDbAsync(Func<AppDbContext, Task> action) => WithDbAsync(async db => { await action(db); return 0; });

    public async Task<T> WithServiceAsync<TService, T>(Func<TService, Task<T>> action)
        where TService : notnull
    {
        await using var scope = Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<TService>());
    }

    private static string CreateKeyPem()
    {
        using var rsa = RSA.Create(2048);
        return rsa.ExportPkcs8PrivateKeyPem();
    }
}
