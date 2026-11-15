namespace CommunitySos.Infrastructure.Identity;

/// <summary>Bound from configuration section "Firebase".</summary>
public sealed class FirebaseOptions
{
    public const string SectionName = "Firebase";

    public string? ProjectId { get; init; }

    /// <summary>Service-account JSON (Key Vault secret). Needed to check token revocation.</summary>
    public string? ServiceAccountJson { get; init; }

    /// <summary>
    /// DEVELOPMENT ONLY: accept fake tokens of the form <c>dev:+919876543210</c> so the API can be
    /// exercised without the mobile app. Startup fails if this is enabled in any other environment.
    /// </summary>
    public bool UseDevelopmentVerifier { get; init; }
}
