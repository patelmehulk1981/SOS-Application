using CommunitySos.Application.Abstractions;
using CommunitySos.Application.Common.Exceptions;
using FirebaseAdmin.Auth;
using Microsoft.Extensions.Logging;

namespace CommunitySos.Infrastructure.Identity;

/// <summary>
/// Verifies Firebase ID tokens (signature, audience = project id, expiry, revocation) and requires a
/// phone-number sign-in.
/// </summary>
public sealed class FirebaseAuthService(FirebaseAppProvider firebase, ILogger<FirebaseAuthService> logger) : IFirebaseAuthService
{
    private const int MaxTokenLength = 4096;

    private FirebaseAuth Auth => FirebaseAuth.GetAuth(firebase.App);

    public async Task<FirebaseIdentity> VerifyAsync(string idToken, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(idToken) || idToken.Length > MaxTokenLength)
        {
            throw Invalid();
        }

        FirebaseToken token;
        try
        {
            token = await Auth.VerifyIdTokenAsync(idToken, checkRevoked: true, cancellationToken);
        }
        catch (FirebaseAuthException ex)
        {
            logger.LogInformation("Firebase ID token rejected: {Reason}", ex.AuthErrorCode);
            throw Invalid();
        }
        catch (ArgumentException)
        {
            throw Invalid(); // malformed token
        }

        if (!token.Claims.TryGetValue("phone_number", out var phone) || phone is not string phoneNumber || phoneNumber.Length == 0)
        {
            throw new AuthenticationFailedException("auth.phone_required", "Sign in with your mobile number.");
        }

        return new FirebaseIdentity(token.Uid, phoneNumber);
    }

    public async Task DeleteUserAsync(string uid, CancellationToken cancellationToken)
    {
        try
        {
            await Auth.DeleteUserAsync(uid, cancellationToken);
        }
        catch (FirebaseAuthException ex) when (ex.AuthErrorCode == AuthErrorCode.UserNotFound)
        {
            // Already gone - erasure is idempotent.
        }
    }

    private static AuthenticationFailedException Invalid() =>
        new("auth.invalid_firebase_token", "Phone verification failed or has expired. Please verify again.");
}
