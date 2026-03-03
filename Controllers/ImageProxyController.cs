using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.Processing;

namespace GamesDatabase.Api.Controllers;

/// <summary>
/// Serves game images resized to the requested dimensions and encoded as WebP.
/// Replaces the static-file middleware for /game-images so that all requests to the
/// existing URL format are automatically optimised (resize + WebP) and disk-cached.
///
/// Usage: GET /game-images/{relative-path}?w=700&amp;h=400
/// Both w and h are optional. If omitted the image is only converted to WebP.
/// </summary>
[ApiController]
[Route("game-images")]
[AllowAnonymous]
public class ImageProxyController : ControllerBase
{
    private static readonly WebpEncoder _encoder = new() { Quality = 75 };

    private readonly IConfiguration _configuration;
    private readonly ILogger<ImageProxyController> _logger;

    public ImageProxyController(IConfiguration configuration, ILogger<ImageProxyController> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    [HttpGet("{**imagePath}")]
    public async Task<IActionResult> GetOptimizedImage(
        string imagePath,
        [FromQuery] int w = 0,
        [FromQuery] int h = 0,
        CancellationToken ct = default)
    {
        var networkSyncPath = _configuration["NetworkSync:NetworkPath"];
        if (string.IsNullOrWhiteSpace(networkSyncPath))
            return NotFound();

        // ── Security: reject path-traversal attempts ──────────────────────────
        var rootFull = Path.GetFullPath(networkSyncPath);
        var requestedFull = Path.GetFullPath(Path.Combine(networkSyncPath, imagePath));
        if (!requestedFull.StartsWith(rootFull + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(requestedFull, rootFull, StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest("Invalid image path.");
        }

        if (!System.IO.File.Exists(requestedFull))
            return NotFound();

        // ── Clamp requested dimensions to sane limits ─────────────────────────
        w = w > 0 ? Math.Clamp(w, 1, 2560) : 0;
        h = h > 0 ? Math.Clamp(h, 1, 2560) : 0;

        // ── Build cache path ──────────────────────────────────────────────────
        var cacheDir = Path.Combine(rootFull, "_proxy_cache", Path.GetDirectoryName(imagePath) ?? "");
        var cacheFile = $"{Path.GetFileName(imagePath)}_{w}x{h}.webp";
        var cachePath = Path.Combine(cacheDir, cacheFile);

        // ── Serve from disk cache if available ───────────────────────────────
        if (System.IO.File.Exists(cachePath))
        {
            SetCacheHeaders();
            return File(System.IO.File.OpenRead(cachePath), "image/webp");
        }

        byte[]? rawBytes = null;
        var ext = Path.GetExtension(requestedFull).ToLowerInvariant();

        if (ext == ".ico")
        {
            rawBytes = await ExtractLargestIcoFrameAsync(requestedFull, ct);
        }

        // ── Process image into a MemoryStream ─────────────────────────────────
        // IMPORTANT: we write to memory first and serve from there.
        // Writing to the disk cache is attempted afterwards as a best-effort
        // operation. If the cache directory isn't writable (e.g. wrong Docker
        // volume permissions) we still serve the optimised image rather than
        // falling back to the original.
        try
        {
            Image image;
            if (rawBytes != null)
            {
                image = await Image.LoadAsync(new MemoryStream(rawBytes), ct);
            }
            else
            {
                image = await Image.LoadAsync(requestedFull, ct);
            }

            using (image)
            {
                if (w > 0 || h > 0)
                {
                    var options = new ResizeOptions
                    {
                        Mode = ResizeMode.Max,
                        Size = new Size(
                            w > 0 ? w : int.MaxValue,
                            h > 0 ? h : int.MaxValue),
                    };
                    image.Mutate(x => x.Resize(options));
                }

                var ms = new MemoryStream();
                await image.SaveAsync(ms, _encoder, ct);
                ms.Position = 0;

                try
                {
                    Directory.CreateDirectory(cacheDir);
                    await System.IO.File.WriteAllBytesAsync(cachePath, ms.ToArray(), ct);
                    ms.Position = 0;
                }
                catch (Exception cacheEx)
                {
                    _logger.LogWarning(cacheEx, "Could not write image cache for {Path} (cache disabled for this image)", cachePath);
                }

                SetCacheHeaders();
                return File(ms, "image/webp");
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Image optimisation failed for {Path}; falling back to original", imagePath);
            SetCacheHeaders();
            var mimeType = GetMimeType(requestedFull);
            return PhysicalFile(requestedFull, mimeType);
        }
    }

    private static async Task<byte[]?> ExtractLargestIcoFrameAsync(string path, CancellationToken ct)
    {
        try
        {
            var bytes = await System.IO.File.ReadAllBytesAsync(path, ct);
            if (bytes.Length < 22) return null;

            if (bytes[0] != 0 || bytes[1] != 0 || bytes[2] != 1 || bytes[3] != 0)
                return null;

            int count = bytes[4] | (bytes[5] << 8);
            if (count <= 0 || count > 256) return null;

            byte[]? bestFrame = null;
            int bestSize = 0;

            for (int i = 0; i < count; i++)
            {
                int entry = 6 + i * 16;
                if (entry + 16 > bytes.Length) break;

                int size = BitConverter.ToInt32(bytes, entry + 8);
                int offset = BitConverter.ToInt32(bytes, entry + 12);

                if (size <= 0 || offset < 0 || offset + size > bytes.Length) continue;

                if (size > bestSize)
                {
                    bestSize = size;
                    bestFrame = bytes[offset..(offset + size)];
                }
            }

            return bestFrame;
        }
        catch
        {
            return null;
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private void SetCacheHeaders()
    {
        // 7-day public cache with ETag revalidation (longer than original 1-day
        // because the variant is keyed by dimensions and won't change unless
        // the source image is replaced).
        Response.Headers["Cache-Control"] = "public, max-age=604800, must-revalidate";
    }

    private static string GetMimeType(string path) =>
        Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            ".ico" => "image/x-icon",
            ".bmp" => "image/bmp",
            _ => "application/octet-stream",
        };
}
