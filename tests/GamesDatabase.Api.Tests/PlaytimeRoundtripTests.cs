using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using CsvHelper;
using CsvHelper.Configuration;
using GamesDatabase.Api.Application.Interfaces;
using GamesDatabase.Api.Contracts;
using GamesDatabase.Api.Common;
using GamesDatabase.Api.Domain.Entities;
using GamesDatabase.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GamesDatabase.Api.Tests;

public sealed class PlaytimeRoundtripTests(GamesDatabaseApiFactory factory) : IClassFixture<GamesDatabaseApiFactory>
{
    private async Task<(int Id, int Status, HttpClient Client)> Owner()
    {
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<GamesDbContext>();
        var user = new User { Username = "Time-roundtrip-" + Guid.NewGuid().ToString("N"), Role = UserRole.Standard }; db.Users.Add(user); await db.SaveChangesAsync();
        var status = new GameStatus { UserId = user.Id, Name = "Owned backlog" }; db.GameStatuses.Add(status); await db.SaveChangesAsync();
        var client = factory.CreateClient(); client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", scope.ServiceProvider.GetRequiredService<IAuthService>().GenerateToken(user));
        return (user.Id, status.Id, client);
    }
    private static async Task<GameDto> Create(HttpClient client, int status, string name, int? manual = 90, bool additive = true)
    {
        var response = await client.PostAsJsonAsync("/api/games", new { name, statusId = status, manualPlaytimeMinutes = manual, manualPlaytimeIsAdditive = additive }); response.EnsureSuccessStatusCode(); return (await response.Content.ReadFromJsonAsync<GameDto>())!;
    }
    private static MultipartFormDataContent Form(string csv, bool selective, string config = "{}")
    {
        var form = new MultipartFormDataContent();
        if (selective) { form.Add(new StringContent(csv), "csvText"); form.Add(new StringContent(config), "configJson"); }
        else form.Add(new StringContent(csv, System.Text.Encoding.UTF8, "text/csv"), "csvFile", "isolated.csv");
        return form;
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Raw_sources_and_additive_mode_roundtrip_and_missing_legacy_mode_does_not_clear_it(bool selective)
    {
        var owner = await Owner(); var game = await Create(owner.Client, owner.Status, "Roundtrip fixture");
        await using (var scope = factory.Services.CreateAsyncScope()) { var db = scope.ServiceProvider.GetRequiredService<GamesDbContext>(); var stored = await db.Games.SingleAsync(value => value.Id == game.Id); stored.SteamPlaytimeForever = 600; await db.SaveChangesAsync(); }
        var export = selective ? await owner.Client.PostAsJsonAsync("/api/DataExport/selective-games-export", new { gameIds = new[] { game.Id } }) : await owner.Client.GetAsync("/api/DataExport/full"); export.EnsureSuccessStatusCode();
        var csv = await export.Content.ReadAsStringAsync();
        using (var reader = new CsvReader(new StringReader(csv), new CsvConfiguration(CultureInfo.InvariantCulture) { HeaderValidated = null, MissingFieldFound = null }))
        {
            var record = Assert.Single(reader.GetRecords<ExportRecord>(), record => record.Type == "Game");
            Assert.Equal("90", record.ManualPlaytimeMinutes); Assert.Equal("600", record.SteamPlaytimeForever); Assert.Equal("True", record.ManualPlaytimeIsAdditive);
        }
        (await owner.Client.PutAsJsonAsync($"/api/games/{game.Id}", new { manualPlaytimeMinutes = 20, manualPlaytimeIsAdditive = false })).EnsureSuccessStatusCode();
        using (var restore = Form(csv, selective)) (await owner.Client.PostAsync(selective ? "/api/DataExport/selective-games-import" : "/api/DataExport/full", restore)).EnsureSuccessStatusCode();
        var restored = (await owner.Client.GetFromJsonAsync<GameDto>($"/api/games/{game.Id}"))!;
        Assert.Equal(90, restored.ManualPlaytimeMinutes); Assert.Equal(600, restored.SteamPlaytimeForever); Assert.True(restored.ManualPlaytimeIsAdditive);
        var legacy = "Type,Name,Status,ManualPlaytimeMinutes,SteamPlaytimeForever\nGame,Roundtrip fixture,Owned backlog,90,600\n";
        using (var import = Form(legacy, selective)) (await owner.Client.PostAsync(selective ? "/api/DataExport/selective-games-import" : "/api/DataExport/full", import)).EnsureSuccessStatusCode();
        Assert.True((await owner.Client.GetFromJsonAsync<GameDto>($"/api/games/{game.Id}"))!.ManualPlaytimeIsAdditive);
    }

    [Theory]
    [InlineData(false, "-1", "True")] [InlineData(true, "-1", "True")]
    [InlineData(false, "600001", "True")] [InlineData(true, "600001", "True")]
    [InlineData(false, "90", "not-a-boolean")] [InlineData(true, "90", "not-a-boolean")]
    public async Task Invalid_import_time_or_mode_does_not_partially_rewrite_the_game(bool selective, string minutes, string mode)
    {
        var owner = await Owner(); var game = await Create(owner.Client, owner.Status, "Reject fixture");
        using var form = Form($"Type,Name,Status,ManualPlaytimeMinutes,ManualPlaytimeIsAdditive,Comment\nGame,Reject fixture,Owned backlog,{minutes},{mode},Must not be saved\n", selective);
        var response = await owner.Client.PostAsync(selective ? "/api/DataExport/selective-games-import" : "/api/DataExport/full", form); response.EnsureSuccessStatusCode();
        var after = (await owner.Client.GetFromJsonAsync<GameDto>($"/api/games/{game.Id}"))!;
        Assert.Null(after.Comment); Assert.Equal(90, after.ManualPlaytimeMinutes); Assert.True(after.ManualPlaytimeIsAdditive);
    }

    [Fact]
    public async Task Selective_overrides_can_enable_addition_clear_it_or_restore_automatic_without_changing_raw_Steam()
    {
        var owner = await Owner(); var game = await Create(owner.Client, owner.Status, "Override fixture", 90, false);
        const string csv = "Type,Name,Status,ManualPlaytimeMinutes,SteamPlaytimeForever\nGame,Override fixture,Owned backlog,90,600\n";
        using (var additive = Form(csv, true, """{"globalConfig":{"mode":"custom","properties":{"manualPlaytimeIsAdditive":{"mode":"custom","customValue":"True"}}}}""")) (await owner.Client.PostAsync("/api/DataExport/selective-games-import", additive)).EnsureSuccessStatusCode();
        var added = (await owner.Client.GetFromJsonAsync<GameDto>($"/api/games/{game.Id}"))!; Assert.True(added.ManualPlaytimeIsAdditive); Assert.Equal(600, added.SteamPlaytimeForever);
        using (var cleared = Form(csv, true, """{"globalConfig":{"mode":"custom","properties":{"manualPlaytimeIsAdditive":{"mode":"customCleared"},"manualPlaytimeMinutes":{"mode":"custom","customValue":"0"}}}}""")) (await owner.Client.PostAsync("/api/DataExport/selective-games-import", cleared)).EnsureSuccessStatusCode();
        var automatic = (await owner.Client.GetFromJsonAsync<GameDto>($"/api/games/{game.Id}"))!; Assert.False(automatic.ManualPlaytimeIsAdditive); Assert.Null(automatic.ManualPlaytimeMinutes); Assert.Equal(600, automatic.SteamPlaytimeForever);
    }

    [Fact]
    public async Task SQL_sorting_and_saved_view_numeric_filters_use_effective_long_minutes_not_raw_Steam_or_int_overflow()
    {
        var owner = await Owner(); var a = await Create(owner.Client, owner.Status, "Addition", 90, true); var b = await Create(owner.Client, owner.Status, "Replacement", 150, false); var c = await Create(owner.Client, owner.Status, "Automatic", 0, true); var huge = await Create(owner.Client, owner.Status, "Long addition", 600000, true);
        await using (var scope = factory.Services.CreateAsyncScope()) { var db = scope.ServiceProvider.GetRequiredService<GamesDbContext>(); foreach (var stored in await db.Games.Where(game => game.UserId == owner.Id).ToListAsync()) stored.SteamPlaytimeForever = stored.Id == huge.Id ? int.MaxValue : 120; await db.SaveChangesAsync(); }
        var sorted = (await owner.Client.GetFromJsonAsync<PagedResult<GameDto>>("/api/games?sortBy=steamPlaytimeForever&sortDescending=true"))!;
        Assert.Equal(new[] { huge.Id, a.Id, b.Id, c.Id }, sorted.Data.Select(game => game.Id));
        foreach (var op in new[] { FilterOperator.Equals, FilterOperator.GreaterThan })
        {
            var view = await owner.Client.PostAsJsonAsync("/api/gameviews", new GameViewCreateDto { Name = "Long minutes " + op, Configuration = new ViewConfiguration { FilterGroups = [new() { Filters = [new() { Field = FilterField.SteamPlaytimeForever, Operator = op, Value = op == FilterOperator.Equals ? 2148083647L : 2147483647L }] }] } }); view.EnsureSuccessStatusCode();
            var id = (await view.Content.ReadFromJsonAsync<GameViewDto>())!.Id;
            var filtered = await owner.Client.GetAsync($"/api/games?viewId={id}"); Assert.True(filtered.StatusCode == HttpStatusCode.OK, await filtered.Content.ReadAsStringAsync());
            Assert.Equal(huge.Id, Assert.Single((await filtered.Content.ReadFromJsonAsync<PagedResult<GameDto>>())!.Data).Id);
        }
    }
}
