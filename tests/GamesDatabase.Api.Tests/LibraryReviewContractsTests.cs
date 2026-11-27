using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using CsvHelper;
using GamesDatabase.Api.Application.Interfaces;
using GamesDatabase.Api.Application.Services;
using GamesDatabase.Api.Common;
using GamesDatabase.Api.Configuration;
using GamesDatabase.Api.Contracts;
using GamesDatabase.Api.Domain.Entities;
using GamesDatabase.Api.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace GamesDatabase.Api.Tests;

public sealed class LibraryReviewContractsTests(GamesDatabaseApiFactory factory) : IClassFixture<GamesDatabaseApiFactory>
{
    private async Task<(int Id, int Status, HttpClient Client)> Owner()
    {
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<GamesDbContext>();
        var user = new User { Username = "Review-" + Guid.NewGuid().ToString("N"), Role = UserRole.Standard }; db.Users.Add(user); await db.SaveChangesAsync();
        var status = new GameStatus { UserId = user.Id, Name = "Owned backlog" }; db.GameStatuses.Add(status); await db.SaveChangesAsync();
        var client = factory.CreateClient(); client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", scope.ServiceProvider.GetRequiredService<IAuthService>().GenerateToken(user));
        return (user.Id, status.Id, client);
    }

    [Theory]
    [InlineData(null, null, false, null)] [InlineData(0, 120, false, 120L)] [InlineData(0, 120, true, 120L)]
    [InlineData(90, null, true, 90L)] [InlineData(90, 120, false, 90L)] [InlineData(90, 120, true, 210L)]
    [InlineData(600000, 2147483647, true, 2148083647L)]
    public void Effective_time_preserves_raw_sources_and_handles_null_zero_addition_without_overflow(int? manual, int? steam, bool additive, long? expected)
    {
        Assert.Equal(expected, GamePlaytime.Effective(manual, steam, additive));
        Assert.Equal(expected, GamePlaytime.EffectiveExpression.Compile()(new Game { ManualPlaytimeMinutes = manual, SteamPlaytimeForever = steam, ManualPlaytimeIsAdditive = additive }));
    }

    [Fact]
    public async Task Game_edits_clear_manual_zero_and_preserve_both_raw_sources_when_adding()
    {
        var owner = await Owner(); var created = await owner.Client.PostAsJsonAsync("/api/games", new { name = "Time fixture", statusId = owner.Status, manualPlaytimeMinutes = 90, manualPlaytimeIsAdditive = true }); created.EnsureSuccessStatusCode(); var id = (await created.Content.ReadFromJsonAsync<GameDto>())!.Id;
        await using (var scope = factory.Services.CreateAsyncScope()) { var db = scope.ServiceProvider.GetRequiredService<GamesDbContext>(); var game = await db.Games.SingleAsync(game => game.Id == id); game.SteamPlaytimeForever = 120; await db.SaveChangesAsync(); }
        var original = (await owner.Client.GetFromJsonAsync<GameDto>($"/api/games/{id}"))!; Assert.True(original.ManualPlaytimeIsAdditive); Assert.Equal(90, original.ManualPlaytimeMinutes); Assert.Equal(120, original.SteamPlaytimeForever);
        Assert.Equal(210, (await owner.Client.GetFromJsonAsync<CollectionStatisticsDto>("/api/collection/statistics?year=2026"))!.Totals.EffectiveMinutes);
        (await owner.Client.PutAsJsonAsync($"/api/games/{id}", new { manualPlaytimeMinutes = 0 })).EnsureSuccessStatusCode();
        var automatic = (await owner.Client.GetFromJsonAsync<GameDto>($"/api/games/{id}"))!; Assert.Null(automatic.ManualPlaytimeMinutes); Assert.Equal(120, automatic.SteamPlaytimeForever); Assert.True(automatic.ManualPlaytimeIsAdditive);
        Assert.Equal(120, (await owner.Client.GetFromJsonAsync<CollectionStatisticsDto>("/api/collection/statistics?year=2026"))!.Totals.EffectiveMinutes);
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.Client.PutAsJsonAsync($"/api/games/{id}", new { manualPlaytimeMinutes = 600001 })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.Client.PutAsJsonAsync($"/api/games/{id}", new { manualPlaytimeIsAdditive = "yes" })).StatusCode);
    }

    [Fact]
    public async Task Invalid_foreign_PlayWith_is_rejected_before_a_created_game_can_be_left_behind()
    {
        var owner = await Owner(); var other = await Owner(); int foreignId;
        await using (var scope = factory.Services.CreateAsyncScope()) { var db = scope.ServiceProvider.GetRequiredService<GamesDbContext>(); var item = new GamePlayWith { UserId = other.Id, Name = "Foreign fixture" }; db.GamePlayWiths.Add(item); await db.SaveChangesAsync(); foreignId = item.Id; }
        var response = await owner.Client.PostAsJsonAsync("/api/games", new { name = "Rejected fixture", statusId = owner.Status, playWithIds = new[] { foreignId } }); Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await using var after = factory.Services.CreateAsyncScope(); Assert.False(await after.ServiceProvider.GetRequiredService<GamesDbContext>().Games.AnyAsync(game => game.UserId == owner.Id));
    }

    [Fact]
    public async Task A_foreign_view_configuration_cannot_be_changed_and_recent_usage_does_not_change_config_or_shuffle_shortcuts()
    {
        var owner = await Owner(); var other = await Owner(); int[] ids;
        await using (var scope = factory.Services.CreateAsyncScope()) { var db = scope.ServiceProvider.GetRequiredService<GamesDbContext>(); var views = Enumerable.Range(1, 4).Select(index => new GameView { UserId = owner.Id, Name = "View " + index, FiltersJson = "[]", SortOrder = index }).ToList(); db.GameViews.AddRange(views); await db.SaveChangesAsync(); ids = views.Select(view => view.Id).ToArray(); }
        Assert.Equal(HttpStatusCode.NotFound, (await other.Client.PutAsJsonAsync($"/api/gameviews/{ids[0]}/configuration", Array.Empty<object>())).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.Client.PostAsJsonAsync($"/api/gameviews/{ids[0]}/use", new { })).StatusCode);
        await using (var scope = factory.Services.CreateAsyncScope()) await scope.ServiceProvider.GetRequiredService<GamesDbContext>().GameViews.Where(view => view.UserId == owner.Id).ExecuteUpdateAsync(setters => setters.SetProperty(view => view.ModifiedSinceExport, false));
        var before = (await owner.Client.GetFromJsonAsync<GameViewDto>($"/api/gameviews/{ids[2]}"))!;
        (await owner.Client.PostAsJsonAsync($"/api/gameviews/{ids[2]}/use", new { })).EnsureSuccessStatusCode();
        var recent = (await owner.Client.GetFromJsonAsync<GameViewDto>($"/api/gameviews/{ids[2]}"))!; Assert.NotNull(recent.LastUsedAt); Assert.Equal(before.UpdatedAt, recent.UpdatedAt); Assert.Equal(before.SortOrder, recent.SortOrder);
        Assert.Equal(DateTimeKind.Utc, recent.LastUsedAt.Value.Kind); // SQLite must not turn persisted UTC into browser-local time.
        (await owner.Client.PostAsJsonAsync($"/api/gameviews/{ids[0]}/use", new { })).EnsureSuccessStatusCode(); Assert.NotNull((await owner.Client.GetFromJsonAsync<GameViewDto>($"/api/gameviews/{ids[0]}"))!.LastUsedAt);
        var summaries = (await owner.Client.PostAsJsonAsync($"/api/gameviews/{ids[2]}/use", new { })); summaries.EnsureSuccessStatusCode();
        Assert.Equal(ids, (await summaries.Content.ReadFromJsonAsync<List<GameViewSummaryDto>>())!.Select(view => view.Id));
        Assert.True((await owner.Client.GetFromJsonAsync<GameViewDto>($"/api/gameviews/{ids[2]}"))!.LastUsedAt >= recent.LastUsedAt);
        await using var after = factory.Services.CreateAsyncScope(); Assert.All(await after.ServiceProvider.GetRequiredService<GamesDbContext>().GameViews.Where(view => view.UserId == owner.Id).ToListAsync(), view => { Assert.Equal("[]", view.FiltersJson); Assert.False(view.ModifiedSinceExport); });
    }

    [Fact]
    public async Task Displayed_random_uses_exact_owned_ids_and_rejects_foreign_or_deleted_ids_without_fallback()
    {
        var owner = await Owner(); var other = await Owner(); var own = await owner.Client.PostAsJsonAsync("/api/games", new { name = "Shown", statusId = owner.Status }); own.EnsureSuccessStatusCode(); var id = (await own.Content.ReadFromJsonAsync<GameDto>())!.Id;
        var foreign = await other.Client.PostAsJsonAsync("/api/games", new { name = "Not shown", statusId = other.Status }); foreign.EnsureSuccessStatusCode(); var foreignId = (await foreign.Content.ReadFromJsonAsync<GameDto>())!.Id;
        var picked = await owner.Client.PostAsJsonAsync("/api/collection/random-game", new { gameIds = new[] { id } }); picked.EnsureSuccessStatusCode(); Assert.Equal(id, (await picked.Content.ReadFromJsonAsync<GameDto>())!.Id);
        Assert.Equal(HttpStatusCode.Conflict, (await owner.Client.PostAsJsonAsync("/api/collection/random-game", new { gameIds = new[] { id, foreignId } })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.Client.PostAsJsonAsync("/api/collection/random-game", new { gameIds = new[] { id, id } })).StatusCode);
        var empty = await owner.Client.PostAsJsonAsync("/api/collection/random-game", new { gameIds = Array.Empty<int>() }); Assert.Equal(HttpStatusCode.NotFound, empty.StatusCode); Assert.Contains("emptySelection", await empty.Content.ReadAsStringAsync());
    }

    private sealed class Handler(string csv) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) { Assert.EndsWith("/api/DataExport/full", request.RequestUri!.AbsolutePath); return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(csv) }); }
    }
    private sealed class Clients(HttpClient client) : IHttpClientFactory { public HttpClient CreateClient(string name) => client; }

    [Fact]
    public async Task ZIP_cache_and_export_dirty_flags_are_scoped_by_owner_even_for_identical_game_names()
    {
        var first = await Owner(); var second = await Owner(); await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<GamesDbContext>(); var name = "Shared title " + Guid.NewGuid().ToString("N");
        var a = new Game { UserId = first.Id, StatusId = first.Status, Name = name }; var b = new Game { UserId = second.Id, StatusId = second.Status, Name = name }; db.Games.AddRange(a, b); await db.SaveChangesAsync();
        using var text = new StringWriter(); using (var writer = new CsvWriter(text, CultureInfo.InvariantCulture)) writer.WriteRecords(new[] { new ExportRecord { Type = "Game", Name = name, ManualPlaytimeMinutes = "90", ManualPlaytimeIsAdditive = "True" } });
        using var http = new HttpClient(new Handler(text.ToString())); var context = new DefaultHttpContext(); context.Items["UserId"] = second.Id; context.Request.Scheme = "https"; context.Request.Host = new HostString("fixture.test");
        var media = new ExportMediaDownloader(new Clients(http), Options.Create(new ExportMediaOptions()), NullLogger<ExportMediaDownloader>.Instance);
        var service = new ZipExportService(scope.ServiceProvider.GetRequiredService<IGameImportExportService>(), media, Options.Create(new ExportSettings()), new HttpContextAccessor { HttpContext = context }, db, NullLogger<ZipExportService>.Instance);
        var result = await service.BuildZipAsync(null, true); Assert.Equal(1, result.GamesExported); Assert.Equal(b.Id, Assert.Single(await db.GameExportCaches.ToListAsync()).GameId);
        Assert.True(a.ModifiedSinceExport); Assert.False(b.ModifiedSinceExport); Assert.Equal(GameExportCache.CurrentMetadataVersion, (await db.GameExportCaches.SingleAsync()).MetadataVersion);
    }
}
