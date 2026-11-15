namespace CommunitySos.Infrastructure.Persistence;

/// <summary>
/// Bound from configuration section "Seed". Example (appsettings / Key Vault / env vars):
/// <code>
/// "Seed": { "MasterAdmin": { "PhoneNumber": "+919876543210", "FullName": "Community Master Admin" } }
/// </code>
/// </summary>
public sealed class SeedOptions
{
    public const string SectionName = "Seed";

    public MasterAdminSeedOptions MasterAdmin { get; init; } = new();
}

public sealed class MasterAdminSeedOptions
{
    public string? PhoneNumber { get; init; }

    public string FullName { get; init; } = "Master Admin";
}
