using SkiaSharp;

namespace GamesDatabase.Api.Common;

/// <summary>Bounded static thumbnails; animated images retain their original format.</summary>
public static class ImageThumbnailEncoder
{
    public const int Quality = 90;
    public const string CacheVersion = "skia-webp-v1-q90";
    private const long MaximumDecodedPixels = 40_000_000;
    private const long MaximumOutputPixels = 16_000_000;

    public static byte[] EncodeWebp(byte[] source, int requestedWidth, int requestedHeight)
    {
        using var encoded = SKData.CreateCopy(source);
        using var codec = SKCodec.Create(encoded) ?? throw new InvalidDataException("Unsupported image.");
        var info = codec.Info;
        if (info.Width <= 0 || info.Height <= 0 || (long)info.Width * info.Height > MaximumDecodedPixels)
            throw new InvalidDataException("Image exceeds the decoded pixel budget.");
        if (codec.FrameCount > 1)
            throw new NotSupportedException("Animated images are served unchanged.");

        var width = Math.Clamp(requestedWidth, 0, 2560);
        var height = Math.Clamp(requestedHeight, 0, 2560);
        var ratio = width == 0 && height == 0 ? 1d : Math.Min(
            width > 0 ? (double)width / info.Width : double.PositiveInfinity,
            height > 0 ? (double)height / info.Height : double.PositiveInfinity);
        var outputWidth = Math.Max(1, (int)Math.Round(info.Width * ratio));
        var outputHeight = Math.Max(1, (int)Math.Round(info.Height * ratio));
        if (outputWidth > 8192 || outputHeight > 8192 || (long)outputWidth * outputHeight > MaximumOutputPixels)
            throw new InvalidDataException("Thumbnail exceeds the output pixel budget.");

        using var bitmap = SKBitmap.Decode(codec) ?? throw new InvalidDataException("Image could not be decoded.");
        using var image = SKImage.FromBitmap(bitmap);
        using var colorSpace = SKColorSpace.CreateSrgb();
        using var surface = SKSurface.Create(new SKImageInfo(outputWidth, outputHeight, SKColorType.Bgra8888, SKAlphaType.Premul, colorSpace))
            ?? throw new InvalidDataException("Image surface could not be allocated.");
        surface.Canvas.Clear(SKColors.Transparent);
        surface.Canvas.DrawImage(image, new SKRect(0, 0, outputWidth, outputHeight), new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
        using var result = surface.Snapshot();
        using var webp = result.Encode(SKEncodedImageFormat.Webp, Quality) ?? throw new InvalidDataException("Image could not be encoded.");
        return webp.ToArray();
    }
}
