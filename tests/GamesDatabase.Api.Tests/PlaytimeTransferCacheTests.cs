using System.Globalization;
using System.IO.Compression;
using System.Net;
using System.Text.Json;
using CsvHelper;
using GamesDatabase.Api.Application.Services;
using GamesDatabase.Api.Common;
using GamesDatabase.Api.Configuration;
using GamesDatabase.Api.Domain.Entities;
using GamesDatabase.Api.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace GamesDatabase.Api.Tests;

public sealed class PlaytimeTransferCacheTests(GamesDatabaseApiFactory factory) : IClassFixture<GamesDatabaseApiFactory>
{
    private static async Task<Game> Seed(GamesDbContext db, bool additive)
    {
        var user = new User { Username = "Transfer-" + Guid.NewGuid().ToString("N"), Role = UserRole.Standard };
        db.Users.Add(user); await db.SaveChangesAsync();
        var status = new GameStatus { UserId = user.Id, Name = "Owned backlog" };
        db.GameStatuses.Add(status); await db.SaveChangesAsync();
        var game = new Game { UserId = user.Id, StatusId = status.Id, Name = "Transfer fixture " + Guid.NewGuid().ToString("N"), ManualPlaytimeMinutes = 90, SteamPlaytimeForever = 600, ManualPlaytimeIsAdditive = additive, ModifiedSinceExport = false };
        db.Games.Add(game); await db.SaveChangesAsync();
        db.GameExportCaches.Add(new GameExportCache { GameId = game.Id, MetadataVersion = 0 }); await db.SaveChangesAsync();
        return game;
    }

    private static string Csv(Game game)
    {
        using var text = new StringWriter();
        using (var csv = new CsvWriter(text, CultureInfo.InvariantCulture)) csv.WriteRecords(new[] { new ExportRecord { Type = "Game", Name = game.Name, ManualPlaytimeMinutes = game.ManualPlaytimeMinutes?.ToString(CultureInfo.InvariantCulture), SteamPlaytimeForever = game.SteamPlaytimeForever?.ToString(CultureInfo.InvariantCulture), ManualPlaytimeIsAdditive = game.ManualPlaytimeIsAdditive.ToString() } });
        return text.ToString();
    }

    private sealed class Handler(string csv) : HttpMessageHandler
    {
        public string CsvData { get; set; } = csv;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Assert.Equal("/api/DataExport/full", request.RequestUri!.AbsolutePath);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(CsvData) });
        }
    }
    private sealed class Clients(HttpClient client) : IHttpClientFactory { public HttpClient CreateClient(string name) => client; }
    private static ZipExportService Zip(Game game, GamesDbContext db, HttpClient http)
    {
        var context = new DefaultHttpContext(); context.Items["UserId"] = game.UserId; context.Request.Scheme = "https"; context.Request.Host = new HostString("transfer-fixture.invalid");
        return new ZipExportService(new Clients(http), Options.Create(new DataExportOptions()), new HttpContextAccessor { HttpContext = context }, db, NullLogger<ZipExportService>.Instance);
    }
    private static void AssertSources(JsonElement info, bool additive)
    {
        Assert.Equal("90", info.GetProperty("manualPlaytimeMinutes").GetString());
        Assert.Equal("600", info.GetProperty("steamPlaytimeForever").GetString());
        Assert.Equal(additive.ToString(), info.GetProperty("manualPlaytimeIsAdditive").GetString());
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Incremental_ZIP_refreshes_old_metadata_without_a_fake_game_edit_then_skips_current_cache(bool additive)
    {
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<GamesDbContext>(); var game = await Seed(db, additive); var updatedAt = game.UpdatedAt;
        using var http = new HttpClient(new Handler(Csv(game))); var service = Zip(game, db, http);
        var result = await service.BuildZipAsync(null, false); Assert.Equal(1, result.GamesExported);
        using var archive = new ZipArchive(new MemoryStream(result.ZipBytes), ZipArchiveMode.Read);
        var entry = Assert.Single(archive.Entries, entry => entry.FullName.StartsWith("Games Database/Games/", StringComparison.Ordinal) && entry.FullName.EndsWith("/info.json", StringComparison.Ordinal));
        using var info = JsonDocument.Parse(await new StreamReader(entry.Open()).ReadToEndAsync()); AssertSources(info.RootElement, additive);
        Assert.Equal(updatedAt, game.UpdatedAt); Assert.False(game.ModifiedSinceExport); Assert.Equal(GameExportCache.CurrentMetadataVersion, (await db.GameExportCaches.SingleAsync(cache => cache.GameId == game.Id)).MetadataVersion);
        var unchanged = await service.BuildZipAsync(null, false); Assert.Equal(0, unchanged.GamesExported); Assert.Equal(1, unchanged.GamesSkipped);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Network_metadata_upgrade_is_not_consumed_by_a_previous_ZIP_export(bool zipFirst)
    {
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<GamesDbContext>(); var game = await Seed(db, true); var updatedAt = game.UpdatedAt;
        var root = Path.Combine(Path.GetTempPath(), "gdb-network-transfer-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        try
        {
            var folder = Path.Combine(root, game.UserId.ToString(CultureInfo.InvariantCulture), "Games", FolderNameHelper.MakeSafeFolderName(game.Name)); Directory.CreateDirectory(folder);
            var path = Path.Combine(folder, "info.json"); await File.WriteAllTextAsync(path, "{\"name\":\"legacy metadata\",\"manualPlaytimeMinutes\":\"90\",\"steamPlaytimeForever\":\"600\"}");
            var handler = new Handler(Csv(game)); using var http = new HttpClient(handler);
            if (zipFirst) Assert.Equal(1, (await Zip(game, db, http).BuildZipAsync(null, false)).GamesExported);
            var service = new NetworkSyncService(new Clients(http), Options.Create(new DataExportOptions { FullExportUrl = "https://transfer-fixture.invalid/api/DataExport/full" }), Options.Create(new NetworkSyncOptions { Enabled = true, NetworkPath = root }), db, NullLogger<NetworkSyncService>.Instance);
            var result = await service.SyncToNetworkAsync(game.UserId, null, false); Assert.True(result.Success, result.ErrorMessage); Assert.Equal(1, result.GamesSynced);
            using var info = JsonDocument.Parse(await File.ReadAllTextAsync(path)); AssertSources(info.RootElement, true);
            Assert.Equal(updatedAt, game.UpdatedAt); Assert.False(game.ModifiedSinceExport);
            var current = await service.SyncToNetworkAsync(game.UserId, null, false); Assert.True(current.Success, current.ErrorMessage); Assert.Equal(0, current.GamesSynced); Assert.Equal(1, current.GamesSkipped);

            // This must also work on later edits, not just a one-time version bump.
            game.ManualPlaytimeMinutes = 180; game.ManualPlaytimeIsAdditive = false; await db.SaveChangesAsync(); var editTime = game.UpdatedAt; handler.CsvData = Csv(game);
            Assert.Equal(1, (await Zip(game, db, http).BuildZipAsync(null, false)).GamesExported); Assert.False(game.ModifiedSinceExport);
            var changed = await service.SyncToNetworkAsync(game.UserId, null, false); Assert.True(changed.Success, changed.ErrorMessage); Assert.Equal(1, changed.GamesSynced);
            using var revised = JsonDocument.Parse(await File.ReadAllTextAsync(path)); Assert.Equal("180", revised.RootElement.GetProperty("manualPlaytimeMinutes").GetString()); Assert.Equal("600", revised.RootElement.GetProperty("steamPlaytimeForever").GetString()); Assert.Equal("False", revised.RootElement.GetProperty("manualPlaytimeIsAdditive").GetString()); Assert.Equal(editTime, game.UpdatedAt);
            File.Delete(path); var missing = await service.SyncToNetworkAsync(game.UserId, null, false); Assert.True(missing.Success, missing.ErrorMessage); Assert.Equal(1, missing.GamesSynced); Assert.True(File.Exists(path));
        }
        finally { Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task One_shot_local_backup_retains_raw_sources_and_mode_without_starting_a_scheduler(bool additive)
    {
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<GamesDbContext>(); var game = await Seed(db, additive);
        var root = Path.Combine(Path.GetTempPath(), "gdb-backup-transfer-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        try
        {
            var schedule = new BackupSchedule { UserId = game.UserId, IsEnabled = false, DestinationPath = root, RetentionCount = 0 }; db.BackupSchedules.Add(schedule); await db.SaveChangesAsync();
            var service = new BackupScheduleService(factory.Services.GetRequiredService<IServiceScopeFactory>(), NullLogger<BackupScheduleService>.Instance);
            await service.RunBackupNowAsync(schedule, db, CancellationToken.None); Assert.Equal("success", schedule.LastRunStatus); Assert.False(schedule.IsEnabled);
            using var reader = File.OpenText(Assert.Single(Directory.GetFiles(root, "*.csv"))); using var csv = new CsvReader(reader, CultureInfo.InvariantCulture);
            var record = Assert.Single(csv.GetRecords<ExportRecord>(), record => record.Type == "Game");
            Assert.Equal("90", record.ManualPlaytimeMinutes); Assert.Equal("600", record.SteamPlaytimeForever); Assert.Equal(additive.ToString(), record.ManualPlaytimeIsAdditive);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task Export_bookkeeping_and_view_usage_do_not_advance_edit_timestamps_but_real_edits_do()
    {
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<GamesDbContext>(); var game = await Seed(db, true);
        var view = new GameView { UserId = game.UserId, Name = "Bookkeeping view", FiltersJson = "[]" };
        var playlist = new Playlist { UserId = game.UserId, Name = "Bookkeeping playlist" }; db.AddRange(view, playlist); await db.SaveChangesAsync();
        var gameTime = game.UpdatedAt; var viewTime = view.UpdatedAt; var playlistTime = playlist.UpdatedAt;
        db.Entry(game).Property(value => value.ModifiedSinceExport).IsModified = true; game.ModifiedSinceExport = false;
        view.ModifiedSinceExport = false; playlist.ModifiedSinceExport = false; await db.SaveChangesAsync();
        view.LastUsedAt = DateTime.UtcNow; await db.SaveChangesAsync();
        Assert.Equal(gameTime, game.UpdatedAt); Assert.Equal(viewTime, view.UpdatedAt); Assert.Equal(playlistTime, playlist.UpdatedAt); Assert.False(view.ModifiedSinceExport);
        game.Comment = "A real edit"; view.Description = "A real edit"; playlist.Description = "A real edit"; await db.SaveChangesAsync();
        Assert.True(game.UpdatedAt > gameTime); Assert.True(view.UpdatedAt > viewTime); Assert.True(playlist.UpdatedAt > playlistTime);
        Assert.True(game.ModifiedSinceExport); Assert.True(view.ModifiedSinceExport); Assert.True(playlist.ModifiedSinceExport);
    }
}
