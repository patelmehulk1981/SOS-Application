using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using CommunitySos.Application.Abstractions;
using CommunitySos.Application.Common.Security;
using CommunitySos.Domain.Entities;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace CommunitySos.Infrastructure.Identity;

public sealed class JwtTokenService(IOptions<JwtOptions> options, JwtSigningKeys keys, TimeProvider clock) : ITokenService
{
    private const int RefreshTokenBytes = 32; // 256 bits of entropy
    private static readonly JsonWebTokenHandler Handler = new();

    private readonly JwtOptions _options = options.Value;

    public TimeSpan RefreshTokenLifetime => TimeSpan.FromDays(_options.RefreshTokenDays);

    public AccessToken CreateAccessToken(User user)
    {
        ArgumentNullException.ThrowIfNull(user);

        var now = clock.GetUtcNow().UtcDateTime;
        var expires = now.AddMinutes(_options.AccessTokenMinutes);

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = _options.Issuer,
            Audience = _options.Audience,
            IssuedAt = now,
            NotBefore = now,
            Expires = expires,
            SigningCredentials = keys.SigningCredentials,
            Claims = new Dictionary<string, object>
            {
                [AppClaimTypes.Subject] = user.Id.ToString(),
                [AppClaimTypes.Role] = user.Role.ToString(),
                [AppClaimTypes.TokenId] = Guid.NewGuid().ToString("N"),
                [AppClaimTypes.PolicyVersion] = user.AcceptedPolicyVersion ?? string.Empty,
            },
        };

        return new AccessToken(Handler.CreateToken(descriptor), expires);
    }

    public RefreshTokenValue CreateRefreshToken()
    {
        var token = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(RefreshTokenBytes));
        return new RefreshTokenValue(token, HashRefreshToken(token));
    }

    public string HashRefreshToken(string refreshToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(refreshToken);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(refreshToken)));
    }
}
