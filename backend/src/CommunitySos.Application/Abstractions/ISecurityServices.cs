using CommunitySos.Domain.Entities;

namespace CommunitySos.Application.Abstractions;

/// <summary>Issues our own JWT access tokens and opaque refresh tokens.</summary>
public interface ITokenService
{
    TimeSpan RefreshTokenLifetime { get; }

    AccessToken CreateAccessToken(User user);

    /// <summary>A new random refresh token and its SHA-256 hash (only the hash is stored).</summary>
    RefreshTokenValue CreateRefreshToken();

    string HashRefreshToken(string refreshToken);
}

public sealed record AccessToken(string Token, DateTime ExpiresAt);

public sealed record RefreshTokenValue(string Token, string Hash);

/// <summary>Firebase Authentication operations (phone OTP happens on the device; we only verify and clean up).</summary>
public interface IFirebaseAuthService
{
    /// <summary>Verifies an ID token obtained by the app after phone OTP.</summary>
    /// <exception cref="Common.Exceptions.AuthenticationFailedException">Token is invalid, expired, revoked or not a phone sign-in.</exception>
    Task<FirebaseIdentity> VerifyAsync(string idToken, CancellationToken cancellationToken);

    /// <summary>Deletes the Firebase account (phone number) when the user erases their account.</summary>
    Task DeleteUserAsync(string uid, CancellationToken cancellationToken);
}

/// <param name="Uid">Firebase user id.</param>
/// <param name="PhoneNumber">Verified phone number in E.164.</param>
public sealed record FirebaseIdentity(string Uid, string PhoneNumber);

/// <summary>
/// Answers "is this account still allowed in?" for every authenticated request, so a suspension
/// takes effect within seconds rather than when the access token expires. Implementations cache briefly.
/// </summary>
public interface IUserStatusReader
{
    Task<bool> IsActiveAsync(Guid userId, CancellationToken cancellationToken);

    void Invalidate(Guid userId);
}
