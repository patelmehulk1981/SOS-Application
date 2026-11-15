using System.Text;
using SkiaSharp;

namespace CommunitySos.Api.IntegrationTests;

internal static class TestImages
{
    public const string SecretMarker = "GPS-LAT-23.0225-LNG-72.5714-SECRET";

    public static byte[] Jpeg(int width, int height)
    {
        using var bitmap = new SKBitmap(width, height);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.SteelBlue);
            using var paint = new SKPaint { Color = SKColors.OrangeRed };
            canvas.DrawRect(0, 0, width / 2f, height / 4f, paint); // asymmetric so orientation is observable
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Jpeg, 90);
        return data.ToArray();
    }

    public static byte[] Png(int width, int height)
    {
        using var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));
        bitmap.Erase(SKColors.Transparent);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    /// <summary>
    /// A JPEG carrying an EXIF Orientation tag (e.g. 6 = rotate 90° clockwise to display) and a comment
    /// segment with fake GPS text - both must be gone after sanitising.
    /// </summary>
    public static byte[] JpegWithExif(int width, int height, ushort orientation)
    {
        var jpeg = Jpeg(width, height);

        byte[] tiff =
        [
            0x49, 0x49, 0x2A, 0x00, 0x08, 0x00, 0x00, 0x00, // "II*\0", IFD0 at offset 8
            0x01, 0x00,                                     // 1 entry
            0x12, 0x01, 0x03, 0x00, 0x01, 0x00, 0x00, 0x00, // tag 0x0112 Orientation, SHORT, count 1
            (byte)orientation, 0x00, 0x00, 0x00,            // value
            0x00, 0x00, 0x00, 0x00,                         // no next IFD
        ];
        var exifPayload = "Exif\0\0"u8.ToArray().Concat(tiff).ToArray();
        var app1 = Segment(0xE1, exifPayload);
        var comment = Segment(0xFE, Encoding.ASCII.GetBytes(SecretMarker));

        // SOI + APP1 + COM + rest of the original JPEG (after its SOI).
        return [.. jpeg[..2], .. app1, .. comment, .. jpeg[2..]];
    }

    public static bool Contains(byte[] haystack, string text) =>
        haystack.AsSpan().IndexOf(Encoding.ASCII.GetBytes(text)) >= 0;

    private static byte[] Segment(byte marker, byte[] payload)
    {
        var length = payload.Length + 2;
        return [0xFF, marker, (byte)(length >> 8), (byte)(length & 0xFF), .. payload];
    }
}
