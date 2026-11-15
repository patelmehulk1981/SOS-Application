using System.Security.Cryptography;
using CommunitySos.Application.Abstractions;
using CommunitySos.Application.Common.Exceptions;
using SkiaSharp;

namespace CommunitySos.Infrastructure.Media;

/// <summary>
/// Sanitises uploaded photos:
/// 1. size cap while reading; 2. magic-byte sniffing (JPEG/PNG/WebP only - the client's content type is ignored);
/// 3. pixel-count cap before decoding (decompression bombs); 4. EXIF orientation applied;
/// 5. downscale to ≤ 2048 px; 6. flatten onto white and re-encode as JPEG.
/// Re-encoding drops ALL metadata (EXIF GPS, device serials) and any payload appended to the file.
/// </summary>
public sealed class SkiaImageProcessor : IImageProcessor
{
    public const long MaxInputBytes = 15 * 1024 * 1024;
    private const long MaxPixels = 50_000_000;
    private const int MaxOutputDimension = 2048;
    private const int JpegQuality = 82;

    public async Task<ProcessedImage> ProcessAsync(Stream input, CancellationToken cancellationToken)
    {
        var bytes = await ReadCappedAsync(input, cancellationToken);

        if (!IsSupportedFormat(bytes))
        {
            throw new UnprocessableException("attachment.unsupported_format", "Only JPEG, PNG or WebP photos are accepted.");
        }

        using var data = SKData.CreateCopy(bytes);
        using var codec = SKCodec.Create(data) ?? throw Corrupt();

        if ((long)codec.Info.Width * codec.Info.Height > MaxPixels)
        {
            throw new UnprocessableException("attachment.too_large", "Photo resolution is too large.");
        }

        using var decoded = SKBitmap.Decode(codec) ?? throw Corrupt();
        using var oriented = Orient(decoded, codec.EncodedOrigin);
        using var resized = Downscale(oriented);
        using var image = SKImage.FromBitmap(resized);
        using var encoded = image.Encode(SKEncodedImageFormat.Jpeg, JpegQuality) ?? throw Corrupt();

        var output = encoded.ToArray();
        return new ProcessedImage(output, "image/jpeg", resized.Width, resized.Height, Convert.ToHexStringLower(SHA256.HashData(output)));
    }

    private static async Task<byte[]> ReadCappedAsync(Stream input, CancellationToken ct)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await input.ReadAsync(chunk, ct)) > 0)
        {
            if (buffer.Length + read > MaxInputBytes)
            {
                throw new UnprocessableException("attachment.too_large", $"Each photo must be under {MaxInputBytes / (1024 * 1024)} MB.");
            }

            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }

    internal static bool IsSupportedFormat(ReadOnlySpan<byte> b) =>
        (b.Length > 3 && b[0] == 0xFF && b[1] == 0xD8 && b[2] == 0xFF)                                   // JPEG
        || (b.Length > 8 && b[..8].SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A })) // PNG
        || (b.Length > 12 && b[..4].SequenceEqual("RIFF"u8) && b[8..12].SequenceEqual("WEBP"u8));          // WebP

    /// <summary>Draws the bitmap upright (per EXIF origin) onto an opaque white canvas.</summary>
    private static SKBitmap Orient(SKBitmap src, SKEncodedOrigin origin)
    {
        var swap = origin is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop or SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom;
        var (w, h) = swap ? (src.Height, src.Width) : (src.Width, src.Height);

        var dst = new SKBitmap(new SKImageInfo(w, h, SKColorType.Rgba8888, SKAlphaType.Opaque));
        using var canvas = new SKCanvas(dst);
        canvas.Clear(SKColors.White);

        switch (origin)
        {
            case SKEncodedOrigin.TopRight: // mirror horizontally
                canvas.Translate(w, 0);
                canvas.Scale(-1, 1);
                break;
            case SKEncodedOrigin.BottomRight: // 180°
                canvas.Translate(w, h);
                canvas.RotateDegrees(180);
                break;
            case SKEncodedOrigin.BottomLeft: // mirror vertically
                canvas.Translate(0, h);
                canvas.Scale(1, -1);
                break;
            case SKEncodedOrigin.LeftTop: // transpose
                canvas.RotateDegrees(90);
                canvas.Scale(1, -1);
                break;
            case SKEncodedOrigin.RightTop: // 90° clockwise
                canvas.Translate(w, 0);
                canvas.RotateDegrees(90);
                break;
            case SKEncodedOrigin.RightBottom: // transverse
                canvas.Translate(w, h);
                canvas.RotateDegrees(90);
                canvas.Scale(-1, 1);
                break;
            case SKEncodedOrigin.LeftBottom: // 90° counter-clockwise
                canvas.Translate(0, h);
                canvas.RotateDegrees(-90);
                break;
        }

        canvas.DrawBitmap(src, 0, 0);
        canvas.Flush();
        return dst;
    }

    private static SKBitmap Downscale(SKBitmap src)
    {
        var longest = Math.Max(src.Width, src.Height);
        if (longest <= MaxOutputDimension)
        {
            return src.Copy();
        }

        var scale = MaxOutputDimension / (double)longest;
        var info = new SKImageInfo(
            Math.Max(1, (int)Math.Round(src.Width * scale)),
            Math.Max(1, (int)Math.Round(src.Height * scale)),
            SKColorType.Rgba8888,
            SKAlphaType.Opaque);

        return src.Resize(info, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear)) ?? throw Corrupt();
    }

    private static UnprocessableException Corrupt() =>
        new("attachment.invalid_image", "The photo could not be read. Please try another one.");
}
