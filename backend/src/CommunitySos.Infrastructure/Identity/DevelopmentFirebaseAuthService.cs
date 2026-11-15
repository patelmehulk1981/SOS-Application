using CommunitySos.Application.Abstractions;
using CommunitySos.Application.Common.Exceptions;
using CommunitySos.Domain.Common;
using CommunitySos.Domain.ValueObjects;
using Microsoft.Extensions.Logging;

namespace CommunitySos.Infrastructure.Identity;

/// <summary>
/// DEVELOPMENT ONLY. Accepts "dev:+919876543210" as a verified phone sign-in.
/// Registered only when the environment is Development AND Firebase:UseDevelopmentVerifier is true.
/// </summary>
internal sealed class DevelopmentFirebaseAuthService(ILogger<DevelopmentFirebaseAuthService> logger) : IFirebaseAuthService
{
    private const string Prefix = "dev:";

    public Task<FirebaseIdentity> VerifyAsync(string idToken, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(idToken) || !idToken.StartsWith(Prefix, StringComparison.Ordinal))
        {
            throw new AuthenticationFailedException("auth.invalid_firebase_token", "Development tokens look like dev:+919876543210.");
        }

        PhoneNumber phone;
        try
        {
            phone = PhoneNumber.Create(idToken[Prefix.Length..]);
        }
        catch (DomainException)
        {
            throw new AuthenticationFailedException("auth.invalid_firebase_token", "Development token contains an invalid phone number.");
        }

        logger.LogWarning("DEVELOPMENT Firebase verifier accepted {Phone}", phone.Masked);
        return Task.FromResult(new FirebaseIdentity($"dev-{phone.Value.TrimStart('+')}", phone.Value));
    }

    public Task DeleteUserAsync(string uid, CancellationToken cancellationToken)
    {
        logger.LogWarning("DEVELOPMENT Firebase verifier: skipping deletion of {Uid}", uid);
        return Task.CompletedTask;
    }
}
