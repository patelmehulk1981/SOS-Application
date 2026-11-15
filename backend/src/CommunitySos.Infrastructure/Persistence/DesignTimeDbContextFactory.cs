using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace CommunitySos.Infrastructure.Persistence;

/// <summary>
/// Used only by `dotnet ef` tooling. Connection string comes from the COMMUNITYSOS_SQL environment
/// variable, falling back to LocalDB for local development.
/// </summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    private const string LocalDb =
        "Server=(localdb)\\MSSQLLocalDB;Database=CommunitySos;Trusted_Connection=True;TrustServerCertificate=True";

    public AppDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("COMMUNITYSOS_SQL") ?? LocalDb;

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(connectionString, sql => sql.UseNetTopologySuite())
            .Options;

        return new AppDbContext(options);
    }
}
