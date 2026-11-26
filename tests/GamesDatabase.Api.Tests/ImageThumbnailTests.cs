using GamesDatabase.Api.Common;
using GamesDatabase.Api.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using SkiaSharp;
using Xunit;

namespace GamesDatabase.Api.Tests;

public sealed class ImageThumbnailTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "opencode", "gdb-thumbnail-" + Guid.NewGuid().ToString("N"));

    [Theory]
    [InlineData(80, 0, 80, 40)]
    [InlineData(0, 30, 60, 30)]
    [InlineData(80, 30, 60, 30)]
    [InlineData(0, 0, 200, 100)]
    public void Static_thumbnails_retain_aspect_ratio_transparency_and_webp_contract(int width, int height, int expectedWidth, int expectedHeight)
    {
        var result = ImageThumbnailEncoder.EncodeWebp(Png(200, 100), width, height);
        Assert.Equal("RIFF", System.Text.Encoding.ASCII.GetString(result, 0, 4));
        Assert.Equal("WEBP", System.Text.Encoding.ASCII.GetString(result, 8, 4));
        using var decoded = SKBitmap.Decode(result);
        Assert.Equal(expectedWidth, decoded.Width);
        Assert.Equal(expectedHeight, decoded.Height);
        Assert.InRange(decoded.GetPixel(0, 0).Alpha, (byte)118, (byte)122);
    }

    [Fact]
    public void Invalid_payload_and_extreme_aspect_ratio_allocation_are_rejected()
    {
        Assert.Throws<InvalidDataException>(() => ImageThumbnailEncoder.EncodeWebp([1, 2, 3], 40, 40));
        Assert.Throws<InvalidDataException>(() => ImageThumbnailEncoder.EncodeWebp(Png(1, 2000), 2560, 0));
    }

    [Theory]
    [InlineData("cover.png")]
    [InlineData("cover")]
    public async Task Isolated_read_only_artwork_does_not_create_cache_on_the_source_root(string requested)
    {
        Directory.CreateDirectory(root);
        await File.WriteAllBytesAsync(Path.Combine(root, "cover.png"), Png(200, 100));
        var controller = Controller();
        var response = Assert.IsType<FileStreamResult>(await controller.GetOptimizedImage(requested, 80));
        Assert.Equal("image/webp", response.ContentType);
        await using var stream = response.FileStream;
        Assert.False(Directory.Exists(Path.Combine(root, "_proxy_cache")));
        var etag = controller.Response.Headers.ETag.ToString();
        Assert.Contains(ImageThumbnailEncoder.CacheVersion, etag);
        var source = new FileInfo(Path.Combine(root, "cover.png"));
        controller.Request.Headers.IfNoneMatch = $"\"{source.LastWriteTimeUtc.Ticks}-{source.Length}-80-0\"";
        var refreshed = Assert.IsType<FileStreamResult>(await controller.GetOptimizedImage(requested, 80));
        await refreshed.FileStream.DisposeAsync();
        controller.Request.Headers.IfNoneMatch = etag;
        Assert.Equal(304, Assert.IsType<StatusCodeResult>(await controller.GetOptimizedImage(requested, 80)).StatusCode);
    }

    [Theory]
    [InlineData("../cover.png")]
    [InlineData("settings.json")]
    [InlineData("gamesdatabase.db")]
    [InlineData("C:\\private\\cover.png")]
    public async Task Image_route_cannot_serve_parent_paths_or_non_image_files(string path) =>
        Assert.IsType<BadRequestObjectResult>(await Controller().GetOptimizedImage(path, 80));

    [Fact]
    public async Task Animated_gif_remains_original_instead_of_silently_losing_frames()
    {
        byte[] header = [71, 73, 70, 56, 57, 97, 1, 0, 1, 0, 128, 0, 0, 0, 0, 0, 255, 255, 255];
        byte[] frame = [33, 249, 4, 0, 10, 0, 0, 0, 44, 0, 0, 0, 0, 1, 0, 1, 0, 0, 2, 2, 68, 1, 0];
        var gif = header.Concat(frame).Concat(frame).Append((byte)59).ToArray();
        Assert.Throws<NotSupportedException>(() => ImageThumbnailEncoder.EncodeWebp(gif, 40, 40));
        Directory.CreateDirectory(root);
        var file = Path.Combine(root, "hero.gif");
        await File.WriteAllBytesAsync(file, gif);
        var response = Assert.IsType<PhysicalFileResult>(await Controller().GetOptimizedImage("hero.gif", 40));
        Assert.Equal(file, response.FileName);
        Assert.Equal("image/gif", response.ContentType);
        Assert.Equal(gif, await File.ReadAllBytesAsync(file));
    }

    private ImageProxyController Controller() => new(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["NetworkSync:NetworkPath"] = root,
        ["LocalDevelopment:DisableScheduledBackups"] = "true",
    }).Build(), NullLogger<ImageProxyController>.Instance)
    {
        ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
    };

    private static byte[] Png(int width, int height)
    {
        using var surface = SKSurface.Create(new SKImageInfo(width, height));
        surface.Canvas.Clear(new SKColor(30, 80, 120, 120));
        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
}
