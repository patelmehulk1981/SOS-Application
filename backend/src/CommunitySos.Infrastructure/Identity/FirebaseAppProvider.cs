using FirebaseAdmin;
using Google.Apis.Auth.OAuth2;
using Microsoft.Extensions.Options;

namespace CommunitySos.Infrastructure.Identity;

/// <summary>
/// One FirebaseApp per process, shared by Authentication (ID-token verification) and Cloud Messaging.
/// Created lazily so the API starts even when Firebase is unreachable; the first use surfaces misconfiguration.
/// </summary>
public sealed class FirebaseAppProvider(IOptions<FirebaseOptions> options)
{
    private const string AppName = "community-sos";

    private readonly Lazy<FirebaseApp> _app = new(() => Create(options.Value), LazyThreadSafetyMode.ExecutionAndPublication);

    public FirebaseApp App => _app.Value;

    private static FirebaseApp Create(FirebaseOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.ProjectId) || string.IsNullOrWhiteSpace(options.ServiceAccountJson))
        {
            throw new InvalidOperationException("Firebase:ProjectId and Firebase:ServiceAccountJson must be configured.");
        }

        return FirebaseApp.GetInstance(AppName) ?? FirebaseApp.Create(
            new AppOptions
            {
                ProjectId = options.ProjectId,
                Credential = CredentialFactory.FromJson<ServiceAccountCredential>(options.ServiceAccountJson).ToGoogleCredential(),
            },
            AppName);
    }
}
