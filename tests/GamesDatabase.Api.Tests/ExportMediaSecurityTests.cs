using System.Globalization;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using CsvHelper;
using GamesDatabase.Api.Application.Interfaces;
using GamesDatabase.Api.Application.Services;
using GamesDatabase.Api.Common;
using GamesDatabase.Api.Configuration;
using GamesDatabase.Api.Domain.Entities;
using GamesDatabase.Api.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SkiaSharp;
using Xunit;

namespace GamesDatabase.Api.Tests;

// Real ZIP/CSV services + isolated SQLite. Only the remote media server is a
// deterministic outbound handler; these are not claims about a live NAS/network.
public sealed class ExportMediaSecurityTests(GamesDatabaseApiFactory factory) : IClassFixture<GamesDatabaseApiFactory>
{
    private const string PublicImage = "https://93.184.216.34/artwork/cover.html";

    [Fact]
    public async Task ZIP_generates_owner_CSV_directly_ignores_host_headers_and_never_sends_incoming_or_default_credentials()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<GamesDbContext>();
        var owned = await Seed(db, PublicImage);
        var foreign = await Seed(db, "https://93.184.216.35/foreign.png");
        var handler = new Outbound(_ => Image());
        using var http = Client(handler, new ExportMediaOptions());
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "TEST_ONLY_MEDIA_HEADER");
        http.DefaultRequestHeaders.Add("Proxy-Authorization", "Basic TEST_ONLY_PROXY_HEADER");
        http.DefaultRequestHeaders.Add("Cookie", "TEST_ONLY_COOKIE=not-a-session");
        var result = await Zip(scope.ServiceProvider, db, owned.UserId, http).BuildZipAsync("Bearer TEST_ONLY_INCOMING_HEADER", true);
        Assert.Equal(1, result.TotalGames);
        Assert.Equal(1, result.ImagesDownloaded);
        Assert.Equal(new[] { PublicImage }, handler.Requests.Select(uri => uri.AbsoluteUri));
        Assert.False(handler.SawCredentials);
        using var archive = new ZipArchive(new MemoryStream(result.ZipBytes));
        Assert.NotNull(archive.GetEntry($"Games Database/Games/{FolderNameHelper.MakeSafeFolderName(owned.Name)}/logo.png"));
        Assert.DoesNotContain(archive.Entries, entry => entry.FullName.EndsWith(".html", StringComparison.Ordinal));
        using var csvText = new StreamReader(Assert.Single(archive.Entries, entry => entry.FullName.EndsWith(".csv", StringComparison.Ordinal)).Open());
        using var csv = new CsvReader(csvText, CultureInfo.InvariantCulture);
        Assert.Equal(owned.Name, Assert.Single(csv.GetRecords<ExportRecord>(), record => record.Type == "Game").Name);
        await db.Entry(foreign).ReloadAsync();
        Assert.True(foreign.ModifiedSinceExport);
    }

    [Theory]
    [InlineData("http://127.0.0.1/image.png")]
    [InlineData("http://[::1]/image.png")]
    [InlineData("http://[::ffff:127.0.0.1]/image.png")]
    [InlineData("http://0.0.0.0/image.png")]
    [InlineData("http://169.254.169.254/latest/meta-data")]
    [InlineData("http://[fe80::1]/image.png")]
    [InlineData("http://10.23.0.1/image.png")]
    [InlineData("http://172.16.0.1/image.png")]
    [InlineData("http://192.168.42.10:8080/game-images/logo.png")]
    [InlineData("http://[fd00::10]/image.png")]
    [InlineData("http://100.64.0.1/image.png")]
    [InlineData("http://192.0.2.1/image.png")]
    [InlineData("http://224.0.0.1/image.png")]
    [InlineData("http://[ff02::1]/image.png")]
    [InlineData("file:///private/image.png")]
    [InlineData("http://fixture:password@93.184.216.34/image.png")]
    public async Task Unsafe_destinations_are_not_requested_or_archived_and_stored_artwork_is_unchanged(string url)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<GamesDbContext>();
        var game = await Seed(db, url);
        var handler = new Outbound(_ => Image());
        using var http = Client(handler, new ExportMediaOptions());
        var result = await Zip(scope.ServiceProvider, db, game.UserId, http).BuildZipAsync(null, true);
        Assert.Empty(handler.Requests);
        AssertNoArtwork(result);
        await db.Entry(game).ReloadAsync();
        Assert.Equal(url, game.Logo);
    }

    [Theory]
    [InlineData("http://127.0.0.1/image.png")]
    [InlineData("http://169.254.169.254/latest/meta-data")]
    [InlineData("http://192.168.42.10:8080/private/image.png")]
    [InlineData("http://[::1]/image.png")]
    public async Task Redirect_destination_is_revalidated_before_a_second_request(string destination)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<GamesDbContext>();
        var game = await Seed(db, PublicImage);
        var handler = new Outbound(_ => new HttpResponseMessage(HttpStatusCode.Found) { Headers = { Location = new Uri(destination) } });
        using var http = Client(handler, new ExportMediaOptions());
        AssertNoArtwork(await Zip(scope.ServiceProvider, db, game.UserId, http).BuildZipAsync(null, true));
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task Existing_configured_NAS_artwork_subtree_is_an_explicit_positive_exception_not_whole_network_access()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ImageSettings:BaseUrl"] = "http://192.168.42.10:8080",
            ["NetworkSync:ImageBaseUrl"] = "http://nas-fixture.invalid:8090/assets"
        }).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddExportMediaDownloads(config);
        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<ExportMediaOptions>>().Value;
        var policy = new ExportMediaDestinationPolicy(options);
        var url = "http://192.168.42.10:8080/game-images/7/Games/Fixture/logo";
        Assert.True(policy.IsAllowedUri(new Uri(url)));
        Assert.True(policy.IsAllowedAddress(new Uri("http://nas-fixture.invalid:8090/assets/game-images/7/logo.png"), IPAddress.Parse("192.168.42.11")));
        var spacedPrefix = new ExportMediaDestinationPolicy(new ExportMediaOptions { AllowedPrivateBaseUrls = ["http://192.168.42.10:8080/Media%20Library/game-images/"] });
        Assert.True(spacedPrefix.IsAllowedUri(new Uri("http://192.168.42.10:8080/Media%20Library/game-images/7/logo.png")));
        foreach (var denied in new[]
        {
            "http://192.168.42.10:8080/admin/logo.png", "http://192.168.42.10:8081/game-images/logo.png",
            "http://192.168.42.11:8080/game-images/logo.png", "http://192.168.42.10:8080/game-images-evil/logo.png",
            "http://192.168.42.10:8080/game-images/../admin/logo.png", "http://192.168.42.10:8080/game-images/%252e%252e/admin/logo.png"
        }) Assert.False(policy.IsAllowedUri(new Uri(denied)));
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<GamesDbContext>();
        var game = await Seed(db, url);
        var handler = new Outbound(_ => Image());
        using var http = Client(handler, options);
        var result = await Zip(scope.ServiceProvider, db, game.UserId, http, options).BuildZipAsync("Bearer TEST_ONLY_INCOMING_HEADER", true);
        Assert.Equal(1, result.ImagesDownloaded);
        Assert.False(handler.SawCredentials);
        Assert.Single(handler.Requests);
    }

    [Theory]
    [InlineData("http://127.0.0.1/game-images/logo.png")]
    [InlineData("http://169.254.169.254/game-images/logo.png")]
    [InlineData("http://[::1]/game-images/logo.png")]
    [InlineData("http://[fe80::1]/game-images/logo.png")]
    public void Private_server_configuration_never_allows_loopback_or_link_local(string url)
    {
        var uri = new Uri(url);
        var options = new ExportMediaOptions { AllowedPrivateBaseUrls = [uri.GetLeftPart(UriPartial.Authority) + "/game-images/"] };
        Assert.False(new ExportMediaDestinationPolicy(options).IsAllowedUri(uri));
    }

    [Theory]
    [InlineData("not-image")]
    [InlineData("remote-svg")]
    [InlineData("html-type")]
    [InlineData("truncated")]
    [InlineData("declared-oversize")]
    [InlineData("streamed-oversize")]
    public async Task Non_images_truncation_and_declared_or_streamed_oversize_are_never_archived(string kind)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<GamesDbContext>();
        var game = await Seed(db, PublicImage);
        var endless = new BoundedFixtureStream(ExportMediaDownloader.MaximumImageBytes * 2L);
        var handler = new Outbound(_ =>
        {
            var response = kind switch
            {
                "not-image" => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent("<html>not an image</html>"u8.ToArray()) },
                "remote-svg" => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent("<svg xmlns=\"http://www.w3.org/2000/svg\" onload=\"window.__gdbSvgMarker=1\"/>"u8.ToArray()) },
                "truncated" => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Png()[..20]) },
                "declared-oversize" or "streamed-oversize" => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(endless) },
                _ => Image()
            };
            response.Content.Headers.ContentType = new MediaTypeHeaderValue(kind == "html-type" ? "text/html" : "image/png");
            if (kind == "declared-oversize") response.Content.Headers.ContentLength = ExportMediaDownloader.MaximumImageBytes + 1L;
            return response;
        });
        using var http = Client(handler, new ExportMediaOptions());
        AssertNoArtwork(await Zip(scope.ServiceProvider, db, game.UserId, http).BuildZipAsync(null, true));
        if (kind == "declared-oversize") Assert.Equal(0, endless.ReadBytes);
        if (kind == "streamed-oversize") Assert.InRange(endless.ReadBytes, ExportMediaDownloader.MaximumImageBytes + 1L, ExportMediaDownloader.MaximumImageBytes + 64 * 1024L);
    }

    [Fact]
    public async Task Caller_cancellation_is_propagated_during_a_streamed_body()
    {
        var handler = new Outbound(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new WaitingFixtureStream()) });
        using var http = Client(handler, new ExportMediaOptions());
        var downloader = new ExportMediaDownloader(new Clients(http), Options.Create(new ExportMediaOptions()), NullLogger<ExportMediaDownloader>.Instance);
        using var cancelled = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => downloader.DownloadAsync(PublicImage, cancelled.Token));
    }

    [Fact]
    public async Task Per_image_deadline_also_bounds_a_body_that_never_finishes_after_headers()
    {
        var handler = new Outbound(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new WaitingFixtureStream()) });
        using var http = Client(handler, new ExportMediaOptions());
        var downloader = new ExportMediaDownloader(new Clients(http), Options.Create(new ExportMediaOptions()), NullLogger<ExportMediaDownloader>.Instance);
        var started = System.Diagnostics.Stopwatch.StartNew();
        Assert.Null(await downloader.DownloadAsync(PublicImage, CancellationToken.None));
        Assert.InRange(started.Elapsed.TotalSeconds, 8, 30);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Safe_relative_redirects_work_but_redirect_cycles_are_bounded(bool cycle)
    {
        var handler = new Outbound(request => cycle || request.RequestUri!.AbsolutePath.EndsWith(".html", StringComparison.Ordinal)
            ? new HttpResponseMessage(HttpStatusCode.Found) { Headers = { Location = new Uri("./verified.png", UriKind.Relative) } }
            : Image());
        using var http = Client(handler, new ExportMediaOptions());
        var downloader = new ExportMediaDownloader(new Clients(http), Options.Create(new ExportMediaOptions()), NullLogger<ExportMediaDownloader>.Instance);
        var result = await downloader.DownloadAsync(PublicImage, CancellationToken.None);
        if (cycle) { Assert.Null(result); Assert.Equal(4, handler.Requests.Count); }
        else { Assert.NotNull(result); Assert.Equal(".png", result.Extension); Assert.Equal(2, handler.Requests.Count); }
    }

    [Fact]
    public async Task Real_transport_disables_redirects_cookies_proxy_and_checks_resolved_loopback_before_connecting()
    {
        using var guard = Assert.IsType<ExportMediaHttpClient.CredentialFreeMediaHandler>(ExportMediaHttpClient.CreateHandler(new ExportMediaOptions()));
        var sockets = Assert.IsType<SocketsHttpHandler>(guard.InnerHandler);
        Assert.False(sockets.AllowAutoRedirect);
        Assert.False(sockets.UseCookies);
        Assert.False(sockets.UseProxy);
        Assert.Equal(0, sockets.MaxResponseDrainSize);
        Assert.Null(sockets.Credentials);
        Assert.NotNull(sockets.ConnectCallback);
        using var client = new HttpClient(guard, disposeHandler: false) { Timeout = TimeSpan.FromSeconds(5) };
        // The socket callback resolves localhost but rejects its addresses before a
        // connection. No listening service or real-network target is used here.
        var exception = await Assert.ThrowsAsync<HttpRequestException>(() => client.GetAsync("http://localhost:9/image.png"));
        Assert.Contains("not permitted", exception.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    private static void AssertNoArtwork(ZipExportResult result)
    {
        Assert.Equal(0, result.ImagesDownloaded);
        Assert.Equal(1, result.ImagesFailed);
        using var archive = new ZipArchive(new MemoryStream(result.ZipBytes));
        Assert.DoesNotContain(archive.Entries, entry => entry.FullName.EndsWith(".png", StringComparison.Ordinal) || entry.FullName.EndsWith(".html", StringComparison.Ordinal));
    }

    private static ZipExportService Zip(IServiceProvider services, GamesDbContext db, int ownerId, HttpClient http, ExportMediaOptions? options = null)
    {
        var context = new DefaultHttpContext();
        context.Items["UserId"] = ownerId;
        context.Request.Host = new HostString("169.254.169.254");
        context.Request.Scheme = "http";
        context.Request.Headers["X-Forwarded-Host"] = "127.0.0.1:9";
        context.Request.Headers["X-Forwarded-Proto"] = "http";
        var media = new ExportMediaDownloader(new Clients(http), Options.Create(options ?? new ExportMediaOptions()), NullLogger<ExportMediaDownloader>.Instance);
        return new ZipExportService(services.GetRequiredService<IGameImportExportService>(), media, Options.Create(new ExportSettings()), new HttpContextAccessor { HttpContext = context }, db, NullLogger<ZipExportService>.Instance);
    }

    private static async Task<Game> Seed(GamesDbContext db, string url)
    {
        var user = new User { Username = "Media-fixture-" + Guid.NewGuid().ToString("N"), Role = UserRole.Standard };
        db.Users.Add(user); await db.SaveChangesAsync();
        var status = new GameStatus { UserId = user.Id, Name = "Fixture backlog" };
        db.GameStatuses.Add(status); await db.SaveChangesAsync();
        var game = new Game { UserId = user.Id, StatusId = status.Id, Name = "Media fixture " + Guid.NewGuid().ToString("N"), Logo = url };
        db.Games.Add(game); await db.SaveChangesAsync();
        return game;
    }

    private static HttpClient Client(Outbound handler, ExportMediaOptions options) => new(new ExportMediaHttpClient.CredentialFreeMediaHandler(new ExportMediaDestinationPolicy(options)) { InnerHandler = handler });
    private static HttpResponseMessage Image()
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Png()) };
        response.Content.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        return response;
    }
    private static byte[] Png()
    {
        using var surface = SKSurface.Create(new SKImageInfo(4, 4));
        surface.Canvas.Clear(SKColors.Green);
        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }
    private sealed class Clients(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) { Assert.Equal(ExportMediaHttpClient.Name, name); return client; }
    }
    private sealed class Outbound(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<Uri> Requests { get; } = [];
        public bool SawCredentials { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Requests.Add(request.RequestUri!);
            SawCredentials |= request.Headers.Contains("Authorization") || request.Headers.Contains("Proxy-Authorization") || request.Headers.Contains("Cookie");
            return Task.FromResult(respond(request));
        }
    }
    private sealed class BoundedFixtureStream(long size) : Stream
    {
        public long ReadBytes { get; private set; }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => ReadBytes; set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var count = (int)Math.Min(buffer.Length, size - ReadBytes);
            buffer.Span[..count].Clear(); ReadBytes += count;
            return ValueTask.FromResult(count);
        }
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
    private sealed class WaitingFixtureStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => 0; set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) { await Task.Delay(Timeout.Infinite, cancellationToken); return 0; }
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
