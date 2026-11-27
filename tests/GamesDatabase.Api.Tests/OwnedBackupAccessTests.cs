using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using GamesDatabase.Api.Application.Interfaces;
using GamesDatabase.Api.Configuration;
using GamesDatabase.Api.Contracts;
using GamesDatabase.Api.Domain.Entities;
using GamesDatabase.Api.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SkiaSharp;
using Xunit;

namespace GamesDatabase.Api.Tests;

/// <summary>Real Program/JWT/CSV/SQLite and owned filesystem backup. Only external media is a fixture.</summary>
public sealed class OwnedBackupAccessTests(GamesDatabaseApiFactory factory) : IClassFixture<GamesDatabaseApiFactory>
{
    private sealed class Transport(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => send(request, token);
    }
    private sealed class Fixture : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "gdb-owned-backup-" + Guid.NewGuid().ToString("N"));
        public WebApplicationFactory<Program> Host { get; set; } = null!;
        public TaskCompletionSource? CsvEntered { get; set; }
        public TaskCompletionSource? ContinueCsv { get; set; }
        public void Dispose() { Host.Dispose(); if (Directory.Exists(Root)) Directory.Delete(Root, true); }
    }
    private Fixture Host(bool mediaFixture = true)
    {
        var fixture = new Fixture(); Directory.CreateDirectory(fixture.Root);
        fixture.Host = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["NetworkSync:Enabled"] = "true", ["NetworkSync:NetworkPath"] = fixture.Root,
                ["DataExport:FullExportUrl"] = "https://backup-fixture.invalid/api/DataExport/full"
            }));
            builder.ConfigureTestServices(services =>
            {
                services.PostConfigure<NetworkSyncOptions>(options => { options.Enabled = true; options.NetworkPath = fixture.Root; });
                services.PostConfigure<DataExportOptions>(options => options.FullExportUrl = "https://backup-fixture.invalid/api/DataExport/full");
                services.AddHttpClient("TrustAllCerts").ConfigurePrimaryHttpMessageHandler(() => new Transport(async (request, token) =>
                {
                    Assert.Equal("backup-fixture.invalid", request.RequestUri!.Host);
                    fixture.CsvEntered?.TrySetResult();
                    if (fixture.ContinueCsv is not null) await fixture.ContinueCsv.Task.WaitAsync(token);
                    using var client = fixture.Host.CreateClient(); using var forwarded = new HttpRequestMessage(HttpMethod.Get, "/api/DataExport/full");
                    forwarded.Headers.Authorization = request.Headers.Authorization;
                    return await client.SendAsync(forwarded, token);
                }));
                if (mediaFixture) services.AddHttpClient(ExportMediaHttpClient.Name).ConfigurePrimaryHttpMessageHandler(() => new Transport((request, _) =>
                {
                    Assert.Equal("images.backup-fixture.invalid", request.RequestUri!.Host); Assert.Null(request.Headers.Authorization); Assert.False(request.Headers.Contains("Cookie"));
                    using var bitmap = new SKBitmap(32, 32); bitmap.Erase(SKColors.CadetBlue); using var image = SKImage.FromBitmap(bitmap); using var data = image.Encode(SKEncodedImageFormat.Png, 100);
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(data.ToArray()) { Headers = { ContentType = new MediaTypeHeaderValue("image/png") } } });
                }));
            });
        });
        return fixture;
    }
    private sealed record Owner(HttpClient Client, int Id, int GameId, int PlaylistId, string Name);
    private static async Task<Owner> Seed(Fixture fixture, string label)
    {
        using var scope = fixture.Host.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<GamesDbContext>();
        var name = label + Guid.NewGuid().ToString("N"); var user = new User { Username = name, Role = UserRole.Standard }; db.Users.Add(user); await db.SaveChangesAsync();
        var status = new GameStatus { UserId = user.Id, Name = name + " backlog" }; db.GameStatuses.Add(status); await db.SaveChangesAsync();
        var game = new Game { UserId = user.Id, StatusId = status.Id, Name = name, Comment = label + "_PRIVATE_NOTE", Grade = 87, ManualPlaytimeMinutes = 125, Critic = 77, CriticProvider = "OpenCritic", Logo = "https://images.backup-fixture.invalid/logo.png", Hero = "https://images.backup-fixture.invalid/hero.png", Cover = "https://images.backup-fixture.invalid/cover.png" };
        var playlist = new Playlist { UserId = user.Id, Name = name + " list", HeroUrl = "https://images.backup-fixture.invalid/playlist.png" }; db.AddRange(game, playlist); await db.SaveChangesAsync();
        var client = fixture.Host.CreateClient(); client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", scope.ServiceProvider.GetRequiredService<IAuthService>().GenerateToken(user));
        return new(client, user.Id, game.Id, playlist.Id, name);
    }
    private static string GamePath(Fixture fixture, Owner owner) => Path.Combine(fixture.Root, owner.Id.ToString(), "Games", owner.Name);
    private static Task<GameDto?> Game(Owner owner) => owner.Client.GetFromJsonAsync<GameDto>($"/api/games/{owner.GameId}");

    [Fact]
    public async Task Standard_owner_can_export_and_import_CSV_without_changing_or_exporting_another_account()
    {
        using var fixture = Host(); var owner = await Seed(fixture, "A"); var foreign = await Seed(fixture, "B"); var before = (await Game(foreign))!;
        var csv = await owner.Client.GetStringAsync("/api/DataExport/full"); Assert.Contains("A_PRIVATE_NOTE", csv); Assert.DoesNotContain("B_PRIVATE_NOTE", csv);
        using var form = new MultipartFormDataContent(); form.Add(new StringContent(csv.Replace("A_PRIVATE_NOTE", "A_UPDATED_NOTE", StringComparison.Ordinal)), "csvFile", "owned-backup.csv");
        (await owner.Client.PostAsync("/api/DataExport/full", form)).EnsureSuccessStatusCode(); Assert.Equal("A_UPDATED_NOTE", (await Game(owner))!.Comment);
        var after = (await Game(foreign))!; Assert.Equal(before.Comment, after.Comment); Assert.Equal(before.UpdatedAt, after.UpdatedAt); Assert.Equal(before.Cover, after.Cover);
    }

    [Fact]
    public async Task Standard_owner_can_sync_full_and_incremental_backups_and_relink_own_images_only()
    {
        using var fixture = Host(); var owner = await Seed(fixture, "A"); var foreign = await Seed(fixture, "B"); var before = (await Game(foreign))!;
        owner.Client.DefaultRequestHeaders.Add("X-User-Id", foreign.Id.ToString());
        foreach (var full in new[] { true, false })
        {
            var response = await owner.Client.PostAsJsonAsync($"/api/export/sync-to-network?userId={foreign.Id}", new { fullExport = full }); response.EnsureSuccessStatusCode();
            using var result = JsonDocument.Parse(await response.Content.ReadAsStringAsync()); Assert.Equal(1, result.RootElement.GetProperty("stats").GetProperty("totalGames").GetInt32());
        }
        var backup = Directory.GetFiles(Path.Combine(fixture.Root, owner.Id.ToString(), "Backups"), "*.csv"); Assert.Equal(2, backup.Length);
        Assert.All(backup, file => { var csv = File.ReadAllText(file); Assert.Contains("A_PRIVATE_NOTE", csv); Assert.DoesNotContain("B_PRIVATE_NOTE", csv); });
        foreach (var role in new[] { "logo", "hero", "cover" }) Assert.True(File.Exists(Path.Combine(GamePath(fixture, owner), role + ".png")));
        Assert.False(Directory.Exists(Path.Combine(fixture.Root, foreign.Id.ToString())));
        (await owner.Client.PostAsync($"/api/DataExport/update-image-urls?imageBaseUrl=https%3A%2F%2Fbackup.example&userId={foreign.Id}", null)).EnsureSuccessStatusCode();
        var game = (await Game(owner))!; Assert.Equal($"https://backup.example/game-images/{owner.Id}/Games/{owner.Name}/logo.png", game.Logo);
        Assert.Equal(87, game.Grade); Assert.Equal(125, game.ManualPlaytimeMinutes); Assert.Equal("A_PRIVATE_NOTE", game.Comment); Assert.Equal("OpenCritic", game.CriticProvider);
        var after = (await Game(foreign))!; Assert.Equal(before.UpdatedAt, after.UpdatedAt); Assert.Equal(before.Logo, after.Logo); Assert.Equal(before.Comment, after.Comment);
    }

    [Fact]
    public async Task Standard_owner_can_inspect_duplicates_and_folders_and_clear_only_their_own_cache()
    {
        using var fixture = Host(); var owner = await Seed(fixture, "A"); var foreign = await Seed(fixture, "B");
        Directory.CreateDirectory(Path.Combine(GamePath(fixture, owner), "..", "Own_orphan")); Directory.CreateDirectory(GamePath(fixture, foreign));
        var ownCache = Path.Combine(fixture.Root, "_proxy_cache", owner.Id.ToString()); var foreignCache = Path.Combine(fixture.Root, "_proxy_cache", foreign.Id.ToString()); Directory.CreateDirectory(ownCache); Directory.CreateDirectory(foreignCache);
        await File.WriteAllTextAsync(Path.Combine(ownCache, "own.webp"), "owned fixture"); await File.WriteAllTextAsync(Path.Combine(foreignCache, "foreign.webp"), "foreign fixture");
        var folders = await owner.Client.GetStringAsync($"/api/DataExport/analyze-folders?userId={foreign.Id}"); Assert.Contains("Own_orphan", folders); Assert.DoesNotContain(foreign.Name, folders);
        var duplicates = await owner.Client.GetStringAsync("/api/DataExport/analyze-duplicate-games"); Assert.DoesNotContain(foreign.Name, duplicates);
        (await owner.Client.PostAsync($"/api/DataExport/clear-image-cache?userId={foreign.Id}", null)).EnsureSuccessStatusCode();
        Assert.False(File.Exists(Path.Combine(ownCache, "own.webp"))); Assert.True(File.Exists(Path.Combine(foreignCache, "foreign.webp")));
        using var traversal = new HttpRequestMessage(HttpMethod.Delete, "/api/DataExport/orphan-folder") { Content = JsonContent.Create(new { folderName = $"../../{foreign.Id}/Games/{foreign.Name}" }) };
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.Client.SendAsync(traversal)).StatusCode); Assert.True(Directory.Exists(GamePath(fixture, foreign)));
        using var own = new HttpRequestMessage(HttpMethod.Delete, "/api/DataExport/orphan-folder") { Content = JsonContent.Create(new { folderName = "Own_orphan" }) }; (await owner.Client.SendAsync(own)).EnsureSuccessStatusCode();
        Assert.False(Directory.Exists(Path.Combine(fixture.Root, owner.Id.ToString(), "Games", "Own_orphan")));
        Assert.Equal(HttpStatusCode.Forbidden, (await owner.Client.GetAsync("/api/BackupSchedule/admin/users")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await owner.Client.GetAsync("/api/users")).StatusCode);
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("file:///private")]
    [InlineData("https://user:password@backup.example")]
    [InlineData("https://backup.example?redirect=http://127.0.0.1")]
    public async Task Image_relinking_rejects_unsafe_base_URLs_before_mutation(string url)
    {
        using var fixture = Host(); var owner = await Seed(fixture, "A"); var before = (await Game(owner))!;
        var response = await owner.Client.PostAsync("/api/DataExport/update-image-urls?imageBaseUrl=" + Uri.EscapeDataString(url), null); Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(before.Logo, (await Game(owner))!.Logo);
    }

    [Fact]
    public async Task Unavailable_storage_and_a_private_probe_target_preserve_saved_URLs_without_connecting()
    {
        using var fixture = Host(mediaFixture: false); var owner = await Seed(fixture, "A"); var before = (await Game(owner))!;
        var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0); listener.Start();
        try
        {
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            var response = await owner.Client.PostAsync("/api/DataExport/update-image-urls?imageBaseUrl=" + Uri.EscapeDataString($"http://127.0.0.1:{port}"), null); response.EnsureSuccessStatusCode();
            Assert.False(listener.Pending()); Assert.Equal(before.Logo, (await Game(owner))!.Logo); Assert.DoesNotContain(fixture.Root, await response.Content.ReadAsStringAsync());
        }
        finally { listener.Stop(); }
    }

    [Fact]
    public async Task Anonymous_requests_cannot_use_account_backup_tools()
    {
        using var fixture = Host(); using var client = fixture.Host.CreateClient();
        foreach (var path in new[] { "/api/DataExport/full", "/api/DataExport/analyze-folders", "/api/DataExport/analyze-duplicate-games" }) Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(path)).StatusCode);
        foreach (var path in new[] { "/api/DataExport/update-image-urls", "/api/DataExport/clear-image-cache" }) Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsync(path, null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/export/sync-to-network", new { fullExport = true })).StatusCode);
    }

    [Fact]
    public async Task A_second_backup_for_the_same_account_is_rejected_without_writing_a_competing_backup()
    {
        using var fixture = Host(); var owner = await Seed(fixture, "A");
        fixture.CsvEntered = new(TaskCreationOptions.RunContinuationsAsynchronously); fixture.ContinueCsv = new(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var first = owner.Client.PostAsJsonAsync("/api/export/sync-to-network", new { fullExport = true }, cancellation.Token);
        try
        {
            await fixture.CsvEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var second = await owner.Client.PostAsJsonAsync("/api/export/sync-to-network", new { fullExport = true }, cancellation.Token);
            Assert.Equal(HttpStatusCode.Conflict, second.StatusCode); Assert.Contains("syncAlreadyRunning", await second.Content.ReadAsStringAsync());
        }
        finally { fixture.ContinueCsv.TrySetResult(); }
        (await first).EnsureSuccessStatusCode(); Assert.Single(Directory.GetFiles(Path.Combine(fixture.Root, owner.Id.ToString(), "Backups"), "*.csv"));
    }

    [Fact]
    public async Task An_authenticated_token_without_a_positive_owner_cannot_fall_back_to_another_account()
    {
        using var fixture = Host(); using var scope = fixture.Host.Services.CreateScope(); using var client = fixture.Host.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", scope.ServiceProvider.GetRequiredService<IAuthService>().GenerateToken(new User { Id = 0, Username = "Invalid-owner-fixture", Role = UserRole.Standard }));
        client.DefaultRequestHeaders.Add("X-User-Id", "1");
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsync("/api/DataExport/update-image-urls?imageBaseUrl=https%3A%2F%2Fbackup.example", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/export/sync-to-network", new { fullExport = true })).StatusCode);
    }
}
