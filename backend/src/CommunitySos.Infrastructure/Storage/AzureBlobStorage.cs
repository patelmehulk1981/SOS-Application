using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Sas;
using CommunitySos.Application.Abstractions;
using Microsoft.Extensions.Options;

namespace CommunitySos.Infrastructure.Storage;

/// <summary>Bound from "BlobStorage". Use ServiceUri + Managed Identity in Azure; ConnectionString for Azurite/local.</summary>
public sealed class BlobStorageOptions
{
    public const string SectionName = "BlobStorage";

    /// <summary>e.g. "UseDevelopmentStorage=true" (Azurite). Takes precedence over <see cref="ServiceUri"/>.</summary>
    public string? ConnectionString { get; init; }

    /// <summary>e.g. https://communitysosmedia.blob.core.windows.net (authenticated with DefaultAzureCredential).</summary>
    public Uri? ServiceUri { get; init; }

    public string ContainerName { get; init; } = "sos-media";

    /// <summary>Lifetime of read URLs handed to the app.</summary>
    public int ReadSasMinutes { get; init; } = 5;
}

/// <summary>
/// Private container (no public access). Reads go through short-lived read-only SAS URLs:
/// account-key SAS when a connection string is used, user-delegation SAS with Managed Identity.
/// </summary>
public sealed class AzureBlobStorage(BlobServiceClient service, IOptions<BlobStorageOptions> options, TimeProvider clock)
    : IBlobStorage, IDisposable
{
    private static readonly TimeSpan DelegationKeyLifetime = TimeSpan.FromHours(6);

    private readonly BlobStorageOptions _options = options.Value;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private BlobContainerClient? _container;
    private UserDelegationKey? _delegationKey;

    public async Task UploadAsync(string blobName, Stream content, string contentType, CancellationToken cancellationToken)
    {
        var container = await GetContainerAsync(cancellationToken);
        await container.GetBlobClient(blobName).UploadAsync(
            content,
            new BlobUploadOptions
            {
                HttpHeaders = new BlobHttpHeaders { ContentType = contentType, CacheControl = "private, max-age=300" },
                Conditions = new BlobRequestConditions { IfNoneMatch = ETag.All }, // never overwrite
            },
            cancellationToken);
    }

    public async Task DeleteAsync(string blobName, CancellationToken cancellationToken)
    {
        var container = await GetContainerAsync(cancellationToken);
        await container.GetBlobClient(blobName).DeleteIfExistsAsync(DeleteSnapshotsOption.IncludeSnapshots, cancellationToken: cancellationToken);
    }

    public async Task<Uri> GetReadUriAsync(string blobName, CancellationToken cancellationToken)
    {
        var container = await GetContainerAsync(cancellationToken);
        var blob = container.GetBlobClient(blobName);
        var now = clock.GetUtcNow();
        var expiresOn = now.AddMinutes(_options.ReadSasMinutes);

        if (blob.CanGenerateSasUri)
        {
            return blob.GenerateSasUri(BlobSasPermissions.Read, expiresOn);
        }

        var builder = new BlobSasBuilder(BlobSasPermissions.Read, expiresOn)
        {
            BlobContainerName = container.Name,
            BlobName = blobName,
            Resource = "b",
            StartsOn = now.AddMinutes(-2), // tolerate clock skew
            Protocol = SasProtocol.Https,
        };
        var key = await GetDelegationKeyAsync(now, cancellationToken);
        var query = builder.ToSasQueryParameters(key, service.AccountName);
        return new UriBuilder(blob.Uri) { Query = query.ToString() }.Uri;
    }

    public void Dispose() => _gate.Dispose();

    private async Task<BlobContainerClient> GetContainerAsync(CancellationToken ct)
    {
        if (_container is not null)
        {
            return _container;
        }

        await _gate.WaitAsync(ct);
        try
        {
            if (_container is null)
            {
                var container = service.GetBlobContainerClient(_options.ContainerName);
                await container.CreateIfNotExistsAsync(PublicAccessType.None, cancellationToken: ct);
                _container = container;
            }

            return _container;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<UserDelegationKey> GetDelegationKeyAsync(DateTimeOffset now, CancellationToken ct)
    {
        // Refresh well before expiry so issued SAS URLs never outlive their signing key.
        if (_delegationKey is { } cached && cached.SignedExpiresOn > now.AddMinutes(_options.ReadSasMinutes + 5))
        {
            return cached;
        }

        await _gate.WaitAsync(ct);
        try
        {
            if (_delegationKey is null || _delegationKey.SignedExpiresOn <= now.AddMinutes(_options.ReadSasMinutes + 5))
            {
                _delegationKey = (await service.GetUserDelegationKeyAsync(now.AddMinutes(-2), now + DelegationKeyLifetime, ct)).Value;
            }

            return _delegationKey;
        }
        finally
        {
            _gate.Release();
        }
    }
}
