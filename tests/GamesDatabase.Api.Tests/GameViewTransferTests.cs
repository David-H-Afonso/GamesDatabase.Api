using System.Globalization;
using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.Json;
using CsvHelper;
using GamesDatabase.Api.Application.Interfaces;
using GamesDatabase.Api.Application.Services;
using GamesDatabase.Api.Configuration;
using GamesDatabase.Api.Controllers;
using GamesDatabase.Api.Domain.Entities;
using GamesDatabase.Api.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace GamesDatabase.Api.Tests;

public sealed class GameViewTransferTests(GamesDatabaseApiFactory factory) : IClassFixture<GamesDatabaseApiFactory>
{
    private static readonly DateTime Used = new(2026, 11, 26, 12, 0, 0, DateTimeKind.Utc);
    private static async Task<User> User(GamesDbContext db)
    {
        var user = new User { Username = "View-transfer-" + Guid.NewGuid().ToString("N"), Role = UserRole.Standard };
        db.Users.Add(user); await db.SaveChangesAsync(); return user;
    }
    private static async Task<GameView> View(GamesDbContext db, int userId, string name, int? slot, int order = 8)
    {
        var view = new GameView { UserId = userId, Name = name, Description = "Owned filters", FiltersJson = "[]", SortingJson = "[]", IsPublic = true, PinnedSlot = slot, SortOrder = order, LastUsedAt = Used };
        db.GameViews.Add(view); await db.SaveChangesAsync(); return view;
    }
    private static byte[] Csv(params FullExportModel[] records)
    {
        using var text = new StringWriter(); using (var csv = new CsvWriter(text, CultureInfo.InvariantCulture)) csv.WriteRecords(records);
        return Encoding.UTF8.GetBytes(text.ToString());
    }
    private static JsonElement Json(object value) => JsonSerializer.SerializeToElement(value);

    [Fact]
    public async Task Full_CSV_round_trip_retains_pin_positions_recent_use_sort_and_criteria_without_cross_owner_effects()
    {
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<GamesDbContext>(); var service = scope.ServiceProvider.GetRequiredService<IGameImportExportService>();
        var source = await User(db); var destination = await User(db); var other = await User(db);
        var original = await View(db, source.Id, "Favorites", 2, 4); await View(db, source.Id, "Short games", 1, 7); var untouched = await View(db, other.Id, "Favorites", 1, 19); var untouchedAt = untouched.UpdatedAt;
        var bytes = await service.ExportFullDatabaseAsync(source.Id); using var stream = new MemoryStream(bytes); var result = Json(await service.ImportFullDatabaseAsync(stream, destination.Id)); Assert.Equal(JsonValueKind.Null, result.GetProperty("errors").ValueKind);
        var restored = await db.GameViews.AsNoTracking().Where(view => view.UserId == destination.Id).OrderBy(view => view.SortOrder).ToListAsync(); Assert.Equal(2, restored.Count); Assert.Equal(new int?[] { 2, 1 }, restored.Select(view => view.PinnedSlot)); Assert.Equal(new[] { 4, 7 }, restored.Select(view => view.SortOrder)); Assert.All(restored, view => Assert.Equal(Used, view.LastUsedAt)); Assert.Equal(original.FiltersJson, restored[0].FiltersJson);
        await db.Entry(untouched).ReloadAsync(); Assert.Equal(1, untouched.PinnedSlot); Assert.Equal(19, untouched.SortOrder); Assert.Equal(untouchedAt, untouched.UpdatedAt);
    }

    [Fact]
    public async Task Import_preserves_existing_pins_and_reports_conflicts_or_invalid_third_slots_without_eviction()
    {
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<GamesDbContext>(); var service = scope.ServiceProvider.GetRequiredService<IGameImportExportService>(); var owner = await User(db);
        var first = await View(db, owner.Id, "Existing one", 1); var second = await View(db, owner.Id, "Existing two", 2);
        var bytes = Csv(new() { Type = "View", Name = "Incoming one", FiltersJson = "[]", IsPublic = "true", ViewPinnedSlot = "1" }, new() { Type = "View", Name = "Incoming two", FiltersJson = "[]", IsPublic = "true", ViewPinnedSlot = "2" }, new() { Type = "View", Name = "Invalid third", FiltersJson = "[]", IsPublic = "true", ViewPinnedSlot = "3" });
        using var stream = new MemoryStream(bytes); var result = Json(await service.ImportFullDatabaseAsync(stream, owner.Id)); Assert.Equal(3, result.GetProperty("errors").GetArrayLength());
        var pinned = await db.GameViews.AsNoTracking().Where(view => view.UserId == owner.Id && view.PinnedSlot != null).OrderBy(view => view.PinnedSlot).ToListAsync(); Assert.Equal(new[] { first.Id, second.Id }, pinned.Select(view => view.Id)); Assert.Equal(5, await db.GameViews.CountAsync(view => view.UserId == owner.Id));
    }

    [Fact]
    public async Task Legacy_CSV_preserves_destination_preferences_and_newer_usage_never_regresses()
    {
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<GamesDbContext>(); var service = scope.ServiceProvider.GetRequiredService<IGameImportExportService>(); var owner = await User(db); var view = await View(db, owner.Id, "Legacy view", 2, 42);
        using (var legacy = new MemoryStream(Encoding.UTF8.GetBytes("Type,Name,FiltersJson,IsPublic\nView,Legacy view,[],true\n"))) await service.ImportFullDatabaseAsync(legacy, owner.Id);
        await db.Entry(view).ReloadAsync(); Assert.Equal(2, view.PinnedSlot); Assert.Equal(42, view.SortOrder); Assert.Equal(Used, view.LastUsedAt);
        using var older = new MemoryStream(Csv(new FullExportModel { Type = "View", Name = view.Name, FiltersJson = "[]", IsPublic = "true", ViewLastUsedAt = Used.AddDays(-1).ToString("O"), ViewPinnedSlot = "1" })); var result = Json(await service.ImportFullDatabaseAsync(older, owner.Id)); Assert.Single(result.GetProperty("errors").EnumerateArray()); await db.Entry(view).ReloadAsync(); Assert.Equal(2, view.PinnedSlot); Assert.Equal(Used, view.LastUsedAt);
    }

    [Fact]
    public async Task Invalid_optional_preferences_do_not_erase_existing_values_and_hidden_views_release_their_pin()
    {
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<GamesDbContext>(); var service = scope.ServiceProvider.GetRequiredService<IGameImportExportService>(); var owner = await User(db); var view = await View(db, owner.Id, "Validated view", 1, 21);
        using (var invalid = new MemoryStream(Csv(new FullExportModel { Type = "View", Name = view.Name, FiltersJson = "[]", IsPublic = "true", ViewLastUsedAt = "not-a-date", ViewPinnedSlot = "999", SortOrder = "not-an-order" })))
        {
            var result = Json(await service.ImportFullDatabaseAsync(invalid, owner.Id)); Assert.Equal(3, result.GetProperty("errors").GetArrayLength());
        }
        await db.Entry(view).ReloadAsync(); Assert.Equal(1, view.PinnedSlot); Assert.Equal(21, view.SortOrder); Assert.Equal(Used, view.LastUsedAt);
        using (var hidden = new MemoryStream(Csv(new FullExportModel { Type = "View", Name = view.Name, FiltersJson = "[]", IsPublic = "false", ViewPinnedSlot = "1" }))) await service.ImportFullDatabaseAsync(hidden, owner.Id);
        await db.Entry(view).ReloadAsync(); Assert.Null(view.PinnedSlot); Assert.False(view.IsPublic);
    }

    [Fact]
    public async Task ZIP_network_settings_and_one_shot_backup_include_real_pin_usage_and_order_without_starting_schedulers()
    {
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<GamesDbContext>(); var service = scope.ServiceProvider.GetRequiredService<IGameImportExportService>(); var owner = await User(db); await View(db, owner.Id, "Archived view", 2, 17); var bytes = await service.ExportFullDatabaseAsync(owner.Id);
        var root = Path.Combine(Path.GetTempPath(), "opencode", "gdb-view-transfer-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        try
        {
            using var http = new HttpClient(new Handler(bytes)); var clients = new Clients(http); var context = new DefaultHttpContext(); context.Items["UserId"] = owner.Id; context.Request.Scheme = "https"; context.Request.Host = new HostString("view-transfer.invalid");
            var media = new ExportMediaDownloader(clients, Options.Create(new ExportMediaOptions()), NullLogger<ExportMediaDownloader>.Instance);
            var zip = new ZipExportService(service, media, Options.Create(new ExportSettings()), new HttpContextAccessor { HttpContext = context }, db, NullLogger<ZipExportService>.Instance);
            using var archive = new ZipArchive(new MemoryStream((await zip.BuildZipAsync(null, true)).ZipBytes)); var entry = archive.GetEntry("Games Database/Settings/Views.json")!; using var document = JsonDocument.Parse(await new StreamReader(entry.Open()).ReadToEndAsync()); AssertMetadata(document.RootElement[0]);
            var network = new NetworkSyncService(clients, Options.Create(new DataExportOptions { FullExportUrl = "https://view-transfer.invalid/api/DataExport/full" }), Options.Create(new NetworkSyncOptions { Enabled = true, NetworkPath = root }), db, NullLogger<NetworkSyncService>.Instance);
            var synced = await network.SyncToNetworkAsync(owner.Id, null, false); Assert.True(synced.Success, synced.ErrorMessage); using var networkJson = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(root, owner.Id.ToString(CultureInfo.InvariantCulture), "Settings", "Views.json"))); AssertMetadata(networkJson.RootElement[0]);
            var schedule = new BackupSchedule { UserId = owner.Id, IsEnabled = false, DestinationPath = root, RetentionCount = 0 }; db.BackupSchedules.Add(schedule); await db.SaveChangesAsync();
            await new BackupScheduleService(factory.Services.GetRequiredService<IServiceScopeFactory>(), NullLogger<BackupScheduleService>.Instance).RunBackupNowAsync(schedule, db, CancellationToken.None); Assert.Equal("success", schedule.LastRunStatus); Assert.False(schedule.IsEnabled);
            using var reader = File.OpenText(Assert.Single(Directory.GetFiles(root, "*.csv"))); using var csv = new CsvReader(reader, CultureInfo.InvariantCulture); var record = Assert.Single(csv.GetRecords<ExportRecord>(), record => record.Type == "View"); Assert.Equal("2", record.ViewPinnedSlot); Assert.Equal("17", record.SortOrder); Assert.Equal(Used, DateTime.Parse(record.ViewLastUsedAt!, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind));
        }
        finally { Directory.Delete(root, true); }
    }

    private static void AssertMetadata(JsonElement view) { Assert.Equal(2, view.GetProperty("pinnedSlot").GetInt32()); Assert.Equal(17, view.GetProperty("sortOrder").GetInt32()); Assert.Equal(Used, view.GetProperty("lastUsedAt").GetDateTime()); }
    private sealed class Handler(byte[] csv) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Assert.Equal("/api/DataExport/full", request.RequestUri!.AbsolutePath);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(csv) });
        }
    }
    private sealed class Clients(HttpClient client) : IHttpClientFactory { public HttpClient CreateClient(string name) => client; }
}
