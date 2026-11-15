using System.Data;
using System.Text.RegularExpressions;
using CommunitySos.Domain.Entities;
using CommunitySos.Domain.Enums;
using CommunitySos.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CommunitySos.Infrastructure.Persistence;

/// <summary>
/// Brings a database to the current version:
///   1. EF Core migrations (tables, keys, indexes, constraints, sequence)
///   2. Embedded T-SQL scripts from /database/scripts, in file-name order (spatial indexes, triggers,
///      stored procedures). Every script is idempotent (IF NOT EXISTS / CREATE OR ALTER).
///   3. Seeds the single Master Admin from configuration if none exists.
/// Run it as a deployment step (e.g. `CommunitySos.Api --migrate`), not on every app start in production.
/// </summary>
public sealed partial class DatabaseInitializer(
    AppDbContext db,
    IOptions<SeedOptions> seedOptions,
    IConfiguration configuration,
    ILogger<DatabaseInitializer> logger)
{
    /// <summary>Hangfire's tables live in their own schema.</summary>
    public const string JobsSchema = "jobs";

    private const string ScriptResourcePrefix = "SqlScripts.";

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Applying EF Core migrations");
        await db.Database.MigrateAsync(cancellationToken);

        await ApplyScriptsAsync(cancellationToken);
        await InstallJobsSchemaAsync(cancellationToken);
        await GrantAppPrincipalAsync(cancellationToken);
        await SeedMasterAdminAsync(cancellationToken);
    }

    /// <summary>Hangfire's schema, installed here so the running app never needs DDL rights (Jobs:PrepareSchema=false in Azure).</summary>
    private async Task InstallJobsSchemaAsync(CancellationToken cancellationToken)
    {
        // Whenever the database is SQL Server, even if this process uses in-memory job storage: the schema is
        // harmless when unused, and the deployed app must always find it (it may not create it).
        if (!db.Database.IsSqlServer())
        {
            return;
        }

        var connection = db.Database.GetDbConnection();
        var openedHere = connection.State != ConnectionState.Open;
        if (openedHere)
        {
            await connection.OpenAsync(cancellationToken);
        }

        try
        {
            Hangfire.SqlServer.SqlServerObjectsInstaller.Install(connection, JobsSchema, enableHeavyMigrations: true);
            logger.LogInformation("Hangfire schema [{Schema}] is up to date", JobsSchema);
        }
        finally
        {
            if (openedHere)
            {
                await connection.CloseAsync();
            }
        }
    }

    /// <summary>
    /// Azure: creates the API's contained database user for its managed identity (Database:AppPrincipal:Name and
    /// :ObjectId, from Bicep) with least privilege: read/write data and execute procedures; no DDL, no ownership.
    /// Runs as the deploy identity, which is the server's Entra admin.
    /// </summary>
    private async Task GrantAppPrincipalAsync(CancellationToken cancellationToken)
    {
        var name = configuration["Database:AppPrincipal:Name"];
        var objectIdText = configuration["Database:AppPrincipal:ObjectId"];
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(objectIdText))
        {
            return;
        }

        if (!PrincipalName().IsMatch(name) || !Guid.TryParse(objectIdText, out var objectId))
        {
            throw new InvalidOperationException("Database:AppPrincipal:Name must be letters, digits, '-' or '_', and :ObjectId a GUID.");
        }

        // Identifiers cannot be parameters; both values are validated above.
        var sql = $"""
            IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N'{name}')
                CREATE USER [{name}] FROM EXTERNAL PROVIDER WITH OBJECT_ID = '{objectId}';
            ALTER ROLE db_datareader ADD MEMBER [{name}];
            ALTER ROLE db_datawriter ADD MEMBER [{name}];
            GRANT EXECUTE ON SCHEMA::dbo TO [{name}];
            IF SCHEMA_ID(N'{JobsSchema}') IS NOT NULL
                EXEC(N'GRANT SELECT, INSERT, UPDATE, DELETE, EXECUTE ON SCHEMA::[{JobsSchema}] TO [{name}]');
            """;
        await db.Database.ExecuteSqlRawAsync(sql, cancellationToken);
        logger.LogInformation("Granted least-privilege database access to {Principal}", name);
    }

    private async Task ApplyScriptsAsync(CancellationToken cancellationToken)
    {
        var assembly = typeof(DatabaseInitializer).Assembly;
        var scripts = assembly.GetManifestResourceNames()
            .Where(n => n.StartsWith(ScriptResourcePrefix, StringComparison.Ordinal)
                        && n.EndsWith(".sql", StringComparison.OrdinalIgnoreCase))
            .Order(StringComparer.Ordinal)
            .ToList();

        if (scripts.Count == 0)
        {
            throw new InvalidOperationException("No embedded SQL scripts were found. Check the EmbeddedResource item in the Infrastructure project.");
        }

        var connection = db.Database.GetDbConnection();
        var openedHere = connection.State != ConnectionState.Open;
        if (openedHere)
        {
            await connection.OpenAsync(cancellationToken);
        }

        try
        {
            foreach (var script in scripts)
            {
                await using var stream = assembly.GetManifestResourceStream(script)
                    ?? throw new InvalidOperationException($"Embedded script {script} could not be opened.");
                using var reader = new StreamReader(stream);
                var sql = await reader.ReadToEndAsync(cancellationToken);

                foreach (var batch in BatchSeparator().Split(sql).Select(b => b.Trim()).Where(b => b.Length > 0))
                {
                    await using var command = connection.CreateCommand();
                    command.CommandText = batch;
                    command.CommandTimeout = 300;
                    await command.ExecuteNonQueryAsync(cancellationToken);
                }

                logger.LogInformation("Applied SQL script {Script}", script[ScriptResourcePrefix.Length..]);
            }
        }
        finally
        {
            if (openedHere)
            {
                await connection.CloseAsync();
            }
        }
    }

    private async Task SeedMasterAdminAsync(CancellationToken cancellationToken)
    {
        if (await db.Users.AnyAsync(u => u.Role == UserRole.MasterAdmin, cancellationToken))
        {
            return;
        }

        var options = seedOptions.Value.MasterAdmin;
        if (string.IsNullOrWhiteSpace(options.PhoneNumber))
        {
            logger.LogWarning("No Master Admin exists and Seed:MasterAdmin:PhoneNumber is not configured; skipping seed.");
            return;
        }

        var admin = User.CreateMasterAdmin(PhoneNumber.Create(options.PhoneNumber), options.FullName);
        db.Users.Add(admin);
        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Seeded Master Admin {Phone}", admin.PhoneNumber.Masked);
    }

    [GeneratedRegex("^[A-Za-z0-9_-]{1,128}$", RegexOptions.CultureInvariant)]
    private static partial Regex PrincipalName();

    /// <summary>Splits on lines containing only "GO" (SSMS/sqlcmd batch separator).</summary>
    [GeneratedRegex(@"^\s*GO\s*;?\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex BatchSeparator();
}
