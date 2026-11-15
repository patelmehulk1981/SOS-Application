using System.Text;
using CommunitySos.Application.Common.Exceptions;
using CommunitySos.Infrastructure.Media;

namespace CommunitySos.Api.IntegrationTests;

public sealed class ImageProcessorTests
{
    private readonly SkiaImageProcessor _processor = new();

    private Task<CommunitySos.Application.Abstractions.ProcessedImage> Process(byte[] bytes) =>
        _processor.ProcessAsync(new MemoryStream(bytes), CancellationToken.None);

    [Fact]
    public async Task Exif_and_embedded_text_are_stripped_and_orientation_is_applied()
    {
        var input = TestImages.JpegWithExif(400, 200, orientation: 6); // stored landscape, displayed portrait
        Assert.True(TestImages.Contains(input, "Exif"));
        Assert.True(TestImages.Contains(input, TestImages.SecretMarker));

        var result = await Process(input);

        Assert.Equal("image/jpeg", result.ContentType);
        Assert.False(TestImages.Contains(result.Content, "Exif"));
        Assert.False(TestImages.Contains(result.Content, TestImages.SecretMarker));
        Assert.Equal((200, 400), (result.Width, result.Height)); // rotated upright
        Assert.Equal(64, result.Sha256.Length);
    }

    [Fact]
    public async Task Large_images_are_downscaled_to_2048px()
    {
        var result = await Process(TestImages.Jpeg(4000, 3000));

        Assert.Equal(2048, result.Width);
        Assert.Equal(1536, result.Height);
    }

    [Fact]
    public async Task Transparent_png_is_flattened_to_jpeg()
    {
        var result = await Process(TestImages.Png(300, 300));

        Assert.Equal("image/jpeg", result.ContentType);
        Assert.Equal(0xFF, result.Content[0]);
        Assert.Equal(0xD8, result.Content[1]);
    }

    [Fact]
    public async Task Non_images_are_rejected_regardless_of_extension()
    {
        var html = Encoding.UTF8.GetBytes("<html><script>alert(1)</script></html>");
        var gif = "GIF89a"u8.ToArray().Concat(new byte[32]).ToArray();

        Assert.Equal("attachment.unsupported_format", (await Assert.ThrowsAsync<UnprocessableException>(() => Process(html))).Code);
        Assert.Equal("attachment.unsupported_format", (await Assert.ThrowsAsync<UnprocessableException>(() => Process(gif))).Code);
    }

    [Fact]
    public async Task Truncated_jpeg_is_rejected()
    {
        var truncated = TestImages.Jpeg(300, 300)[..40];

        var ex = await Assert.ThrowsAsync<UnprocessableException>(() => Process(truncated));

        Assert.Equal("attachment.invalid_image", ex.Code);
    }
}
