using System.ComponentModel.DataAnnotations;

namespace CommunitySos.Infrastructure.Identity;

/// <summary>Bound from configuration section "Jwt". <see cref="SigningKeyPem"/> must come from Key Vault in production.</summary>
public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    [Required]
    public string Issuer { get; init; } = null!;

    [Required]
    public string Audience { get; init; } = null!;

    [Range(5, 60)]
    public int AccessTokenMinutes { get; init; } = 15;

    [Range(1, 90)]
    public int RefreshTokenDays { get; init; } = 30;

    /// <summary>
    /// RSA private key (PKCS#1 or PKCS#8 PEM, ≥ 2048 bit) used for RS256 signing.
    /// Generate: <c>openssl genpkey -algorithm RSA -pkeyopt rsa_keygen_bits:3072 -out jwt.pem</c>.
    /// Optional in Development only (an ephemeral key is generated; tokens die on restart).
    /// </summary>
    public string? SigningKeyPem { get; init; }
}
