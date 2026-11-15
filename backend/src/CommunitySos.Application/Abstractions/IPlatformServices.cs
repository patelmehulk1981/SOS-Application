using System.Linq.Expressions;
using CommunitySos.Domain.ValueObjects;

namespace CommunitySos.Application.Abstractions;

/// <summary>Private object storage (Azure Blob in production). Blobs are never publicly readable.</summary>
public interface IBlobStorage
{
    Task UploadAsync(string blobName, Stream content, string contentType, CancellationToken cancellationToken);

    Task DeleteAsync(string blobName, CancellationToken cancellationToken);

    /// <summary>Short-lived, read-only URL for one blob (SAS).</summary>
    Task<Uri> GetReadUriAsync(string blobName, CancellationToken cancellationToken);
}

/// <summary>Validates and sanitises user images: real-format check, size/pixel limits, orientation fix, resize, re-encode (strips EXIF/GPS).</summary>
public interface IImageProcessor
{
    /// <exception cref="Common.Exceptions.UnprocessableException">Not a supported image, corrupt, or too large.</exception>
    Task<ProcessedImage> ProcessAsync(Stream input, CancellationToken cancellationToken);
}

/// <summary>A sanitised JPEG ready to store.</summary>
public sealed record ProcessedImage(byte[] Content, string ContentType, int Width, int Height, string Sha256);

/// <summary>Coordinates → human-readable address. Returns null when unavailable; never throws for lookup failures.</summary>
public interface IGeocoder
{
    Task<string?> ReverseGeocodeAsync(GeoCoordinate coordinate, CancellationToken cancellationToken);
}

public enum JobQueue
{
    /// <summary>Life-safety work (alert dispatch, escalation): processed first.</summary>
    Critical,
    Default,
}

/// <summary>
/// Durable background jobs (Hangfire in production). Jobs are resolved from DI; pass
/// <see cref="CancellationToken.None"/> in the expression - the runtime substitutes the job's token.
/// </summary>
public interface IJobScheduler
{
    void Enqueue<TJob>(Expression<Func<TJob, Task>> job, JobQueue queue = JobQueue.Default)
        where TJob : class;

    void Schedule<TJob>(Expression<Func<TJob, Task>> job, TimeSpan delay, JobQueue queue = JobQueue.Default)
        where TJob : class;

    /// <summary>Creates or updates a recurring job (cron, UTC).</summary>
    void AddOrUpdateRecurring<TJob>(string jobId, Expression<Func<TJob, Task>> job, string cron)
        where TJob : class;
}
