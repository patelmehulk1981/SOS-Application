using System.Security.Cryptography;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace CommunitySos.Infrastructure.Identity;

/// <summary>Holds the RS256 key pair. Singleton; the same key signs (TokenService) and validates (JwtBearer).</summary>
public sealed class JwtSigningKeys : IDisposable
{
    private const int MinimumKeySizeBits = 2048;
    private readonly RSA _rsa;

    public JwtSigningKeys(IOptions<JwtOptions> options, IHostEnvironment environment, ILogger<JwtSigningKeys> logger)
    {
        _rsa = RSA.Create();
        var pem = options.Value.SigningKeyPem;

        if (!string.IsNullOrWhiteSpace(pem))
        {
            _rsa.ImportFromPem(pem);
            if (_rsa.KeySize < MinimumKeySizeBits)
            {
                throw new InvalidOperationException($"Jwt:SigningKeyPem must be at least {MinimumKeySizeBits} bits.");
            }
        }
        else if (environment.IsDevelopment())
        {
            _rsa.KeySize = MinimumKeySizeBits;
            logger.LogWarning("Jwt:SigningKeyPem is not configured; using an ephemeral development key. Tokens will be invalid after restart.");
        }
        else
        {
            throw new InvalidOperationException("Jwt:SigningKeyPem must be configured outside Development.");
        }

        SigningKey = new RsaSecurityKey(_rsa)
        {
            KeyId = ComputeKeyId(_rsa),
            // Own provider cache: the process-wide default caches providers by key id, so after this RSA
            // instance is disposed (host restart, key reload) a new key with the same id would be handed a
            // provider that wraps the disposed key.
            CryptoProviderFactory = new CryptoProviderFactory(),
        };
        SigningCredentials = new SigningCredentials(SigningKey, SecurityAlgorithms.RsaSha256);
    }

    public RsaSecurityKey SigningKey { get; }

    public SigningCredentials SigningCredentials { get; }

    public void Dispose() => _rsa.Dispose();

    /// <summary>Stable "kid" derived from the public key, so rotated keys are distinguishable.</summary>
    private static string ComputeKeyId(RSA rsa)
    {
        var hash = SHA256.HashData(rsa.ExportSubjectPublicKeyInfo());
        return Base64UrlEncoder.Encode(hash.AsSpan(0, 12).ToArray());
    }
}
