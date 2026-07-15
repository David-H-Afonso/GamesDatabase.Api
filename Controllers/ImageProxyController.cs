using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.Processing;

namespace GamesDatabase.Api.Controllers;

[ApiController]
[Route("game-images")]
[AllowAnonymous]
public class ImageProxyController : ControllerBase
{
    private static readonly WebpEncoder _encoder = new() { Quality = 75 };
    private static readonly string[] _supportedImageExtensions = [".jpg", ".jpeg", ".png", ".webp", ".gif", ".ico", ".bmp"];

    // Used when the UNC network path is inaccessible — falls back to fetching
    // the raw image from the NAS HTTP server configured in ImageSettings:BaseUrl.
    private static readonly HttpClient _nasHttpFallback = new(
        new HttpClientHandler { AllowAutoRedirect = true })
    {
        Timeout = TimeSpan.FromSeconds(10),
    };

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
        // Prevent infinite proxy loop: if this request came from our own NAS fallback,
        // don't attempt another fallback — just 404 immediately.
        if (Request.Headers.ContainsKey("X-Image-Proxy"))
            return NotFound();

        if (!IsSafeRelativeImagePath(imagePath))
            return BadRequest("Invalid image path.");

        if (string.IsNullOrWhiteSpace(networkSyncPath))
            return await ProxyFromNasHttpAsync(imagePath, ct);

        var rootFull = Path.GetFullPath(networkSyncPath);
        var requestedFull = Path.GetFullPath(Path.Combine(networkSyncPath, imagePath));
        if (!requestedFull.StartsWith(rootFull + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(requestedFull, rootFull, StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest("Invalid image path.");
        }

        requestedFull = ResolveExistingImagePath(requestedFull) ?? requestedFull;

        if (!System.IO.File.Exists(requestedFull))
            return await ProxyFromNasHttpAsync(imagePath, ct);

        w = w > 0 ? Math.Clamp(w, 1, 2560) : 0;
        h = h > 0 ? Math.Clamp(h, 1, 2560) : 0;

        // ETag is derived from the source file's modification time, size and requested
        // dimensions. Including the file size catches uploads that preserve timestamps.
        var sourceInfo = new System.IO.FileInfo(requestedFull);
        var sourceLastModified = sourceInfo.LastWriteTimeUtc;
        var eTag = $"\"{sourceLastModified.Ticks}-{sourceInfo.Length}-{w}-{h}\"";

        // Respond with 304 if the browser already has the current version.
        var ifNoneMatch = Request.Headers["If-None-Match"].FirstOrDefault();
        if (ifNoneMatch == eTag)
            return StatusCode(304);

        var resolvedRelativePath = Path.GetRelativePath(rootFull, requestedFull);
        var cacheDir = Path.Combine(rootFull, "_proxy_cache", Path.GetDirectoryName(resolvedRelativePath) ?? "");
        // Include ticks + file size in the cache filename so a replaced file (even with
        // preserved timestamp) always gets a fresh cache entry without timestamp comparisons.
        var cacheFile = $"{Path.GetFileNameWithoutExtension(resolvedRelativePath)}_{sourceLastModified.Ticks}_{sourceInfo.Length}_{w}x{h}.webp";
        var cachePath = Path.Combine(cacheDir, cacheFile);

        if (System.IO.File.Exists(cachePath))
        {
            SetCacheHeaders(eTag, sourceLastModified);
            return File(System.IO.File.OpenRead(cachePath), "image/webp");
        }

        // Delete any stale cache entries for this image (different ticks/size).
        if (Directory.Exists(cacheDir))
        {
            var baseName = Path.GetFileNameWithoutExtension(resolvedRelativePath) + "_";
            foreach (var stale in Directory.EnumerateFiles(cacheDir, $"{baseName}*.webp"))
                try { System.IO.File.Delete(stale); } catch { }
        }

        // ICO files (Vista+ PNG-compressed frames) fail ImageSharp's default loader;
        // extract the largest embedded frame first.
        byte[]? rawBytes = null;
        if (Path.GetExtension(requestedFull).Equals(".ico", StringComparison.OrdinalIgnoreCase))
            rawBytes = await ExtractLargestIcoFrameAsync(requestedFull, ct);

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
                    image.Mutate(x => x.Resize(new ResizeOptions
                    {
                        Mode = ResizeMode.Max,
                        Size = new Size(w > 0 ? w : int.MaxValue, h > 0 ? h : int.MaxValue),
                    }));
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
                    _logger.LogWarning(cacheEx, "Could not write image cache for {Path}", cachePath);
                }

                SetCacheHeaders(eTag, sourceLastModified);
                return File(ms, "image/webp");
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Image optimisation failed for {Path}; falling back to original", resolvedRelativePath);
            SetCacheHeaders(eTag, sourceLastModified);
            var mimeType = GetMimeType(requestedFull);
            return PhysicalFile(requestedFull, mimeType);
        }
    }

    // Falls back to the configured NAS HTTP server when the UNC path is inaccessible
    // (e.g. credential conflict error 1219 on Windows prevents direct file access).
    private async Task<IActionResult> ProxyFromNasHttpAsync(string imagePath, CancellationToken ct)
    {
        var nasHttpBase = _configuration["ImageSettings:BaseUrl"]?.TrimEnd('/');
        if (string.IsNullOrWhiteSpace(nasHttpBase))
            return NotFound();

        if (!IsSafeRelativeImagePath(imagePath))
            return BadRequest("Invalid image path.");

        var candidatePaths = GetImagePathCandidates(imagePath);
        try
        {
            var addLoopPreventionHeader = IsSameRequestHost(nasHttpBase);
            foreach (var candidatePath in candidatePaths)
            {
                var url = $"{nasHttpBase}/game-images/{ToUrlPath(candidatePath)}";
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                if (addLoopPreventionHeader)
                    request.Headers.TryAddWithoutValidation("X-Image-Proxy", "true");
                using var resp = await _nasHttpFallback.SendAsync(request, ct);
                if (!resp.IsSuccessStatusCode)
                    continue;

                var bytes = await resp.Content.ReadAsByteArrayAsync(ct);
                var contentType = resp.Content.Headers.ContentType?.MediaType ?? "application/octet-stream";
                return File(bytes, contentType);
            }

            return NotFound();
        }
        catch (Exception ex)
        {
            _logger.LogWarning("NAS HTTP fallback failed for {Path}: {Message}", imagePath, ex.Message);
            return NotFound();
        }
    }

    private bool IsSameRequestHost(string baseUrl) =>
        Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri)
        && string.Equals(uri.Host, Request.Host.Host, StringComparison.OrdinalIgnoreCase)
        && uri.Port == Request.Host.Port;

    private static string? ResolveExistingImagePath(string requestedFull)
    {
        if (System.IO.File.Exists(requestedFull))
            return requestedFull;

        foreach (var candidate in GetImagePathCandidates(requestedFull).Skip(1))
        {
            if (System.IO.File.Exists(candidate))
                return candidate;
        }

        return null;
    }

    private static IEnumerable<string> GetImagePathCandidates(string path)
    {
        yield return path;

        var directory = Path.GetDirectoryName(path);
        var fileName = Path.GetFileNameWithoutExtension(path);
        if (string.IsNullOrWhiteSpace(fileName))
            yield break;

        foreach (var extension in _supportedImageExtensions)
        {
            var candidate = string.IsNullOrEmpty(directory)
                ? fileName + extension
                : Path.Combine(directory, fileName + extension);

            if (!string.Equals(candidate, path, StringComparison.OrdinalIgnoreCase))
                yield return candidate;
        }
    }

    private static bool IsSafeRelativeImagePath(string imagePath)
    {
        if (string.IsNullOrWhiteSpace(imagePath) || Path.IsPathRooted(imagePath))
            return false;

        return imagePath
            .Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries)
            .All(segment => segment != "." && segment != "..");
    }

    private static string ToUrlPath(string imagePath) =>
        string.Join('/', imagePath
            .Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries)
            .Select(Uri.EscapeDataString));

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

    private void SetCacheHeaders(string eTag, DateTime lastModified)
    {
        // max-age=60: browser serves from cache with zero requests for the first minute
        //             (covers the typical "go to details and come back" session pattern).
        // stale-while-revalidate=604800: after 60 s the cached image is served instantly
        //             without blocking render, while a background conditional GET (If-None-Match)
        //             refreshes the cache entry. Changed images appear on the very next visit
        //             after the background revalidation completes — no 200-request waterfall.
        Response.Headers["Cache-Control"] = "public, max-age=60, stale-while-revalidate=604800";
        Response.Headers["ETag"] = eTag;
        Response.Headers["Last-Modified"] = lastModified.ToString("R");
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
