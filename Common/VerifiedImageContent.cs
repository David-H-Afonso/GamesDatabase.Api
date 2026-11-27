using SkiaSharp;

namespace GamesDatabase.Api.Common;

/// <summary>Image type comes from a bounded decoder, never from a URL or a declared MIME type.</summary>
public sealed record VerifiedImageContent(byte[] Bytes, string MediaType, string Extension)
{
    public static bool TryReadRaster(byte[] bytes, out VerifiedImageContent? image, long maximumPixels = 40_000_000)
    {
        image = null;
        if (bytes.Length == 0) return false;
        using var encoded = SKData.CreateCopy(bytes);
        using var codec = SKCodec.Create(encoded);
        if (codec is null) return false;
        var (mediaType, extension) = codec.EncodedFormat.ToString().ToLowerInvariant() switch
        {
            "png" => ("image/png", ".png"),
            "jpeg" => ("image/jpeg", ".jpg"),
            "webp" => ("image/webp", ".webp"),
            "gif" => ("image/gif", ".gif"),
            "ico" => ("image/x-icon", ".ico"),
            "bmp" => ("image/bmp", ".bmp"),
            "avif" => ("image/avif", ".avif"),
            _ => (null, null)
        };
        var info = codec.Info;
        if (mediaType is null || info.Width is <= 0 or > 16384 || info.Height is <= 0 or > 16384
            || (long)info.Width * info.Height > maximumPixels) return false;

        // Header sniffing alone accepts truncated files. Require a fully decodable first
        // frame, while retaining original bytes/animation instead of recompressing artwork.
        using var bitmap = new SKBitmap(new SKImageInfo(info.Width, info.Height, SKColorType.Bgra8888, SKAlphaType.Premul));
        if (bitmap.GetPixels() == IntPtr.Zero || codec.GetPixels(bitmap.Info, bitmap.GetPixels()) != SKCodecResult.Success) return false;
        image = new VerifiedImageContent(bytes, mediaType, extension!);
        return true;
    }
}
