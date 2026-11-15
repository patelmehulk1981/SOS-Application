using CommunitySos.Domain.Common;

namespace CommunitySos.Domain.Entities;

/// <summary>A sanitised (re-encoded, EXIF-stripped) photo stored in a private Blob container.</summary>
public sealed class SosAttachment : Entity<Guid>, IAuditedEntity
{
    public const int BlobNameMaxLength = 300;
    public const int ContentTypeMaxLength = 50;
    public const int Sha256Length = 64;
    public const long MaxSizeBytes = 5 * 1024 * 1024;

    private SosAttachment()
    {
    }

    public Guid SosRequestId { get; private set; }

    public string BlobName { get; private set; } = null!;

    public string ContentType { get; private set; } = null!;

    public long SizeBytes { get; private set; }

    public string Sha256 { get; private set; } = null!;

    public int Width { get; private set; }

    public int Height { get; private set; }

    public DateTime UploadedAt { get; private set; }

    internal static SosAttachment Create(Guid sosRequestId, string blobName, string contentType, long sizeBytes, string sha256, int width, int height, DateTime now)
    {
        if (sizeBytes is <= 0 or > MaxSizeBytes)
        {
            throw new DomainException("attachment.invalid_size", "Photo size is not valid.");
        }

        if (width <= 0 || height <= 0)
        {
            throw new DomainException("attachment.invalid_dimensions", "Photo dimensions are not valid.");
        }

        if (sha256.Length != Sha256Length)
        {
            throw new ArgumentException("SHA-256 must be hex encoded.", nameof(sha256));
        }

        return new SosAttachment
        {
            Id = SequentialGuid.NewGuid(),
            SosRequestId = sosRequestId,
            BlobName = Text.Required(blobName, BlobNameMaxLength, nameof(BlobName)),
            ContentType = Text.Required(contentType, ContentTypeMaxLength, nameof(ContentType)),
            SizeBytes = sizeBytes,
            Sha256 = sha256.ToLowerInvariant(),
            Width = width,
            Height = height,
            UploadedAt = now,
        };
    }
}
