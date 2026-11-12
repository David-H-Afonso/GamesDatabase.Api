using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using GamesDatabase.Api.Common;
using GamesDatabase.Api.Contracts;
using GamesDatabase.Api.Contracts.Steam;
using GamesDatabase.Api.Domain.Entities;
using GamesDatabase.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GamesDatabase.Api.Tests;

public sealed class SteamAchievementIntegrationTests(GamesDatabaseApiFactory factory) : IClassFixture<GamesDatabaseApiFactory>
{
    [Fact]
    public async Task Progress_endpoint_is_additive_keeps_cached_completion_and_is_user_scoped()
    {
        var client = await ClientAsync("HouseholdUserA");
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<GamesDbContext>();
            var game = await db.Games.SingleAsync(game => game.Id == factory.UserAGameId);
            game.SteamAppId = 620;
            game.SteamAchievementsSource = "sets";
            game.SteamCompletionPreserved = true;
            db.SteamAchievements.AddRange(
                new() { UserId = game.UserId, GameId = game.Id, SteamAppId = 620, ApiName = "PUBLIC_ONE", DisplayName = "One", Achieved = true },
                new() { UserId = game.UserId, GameId = game.Id, SteamAppId = 620, ApiName = "PUBLIC_TWO", DisplayName = "Two" },
                new() { UserId = game.UserId, GameId = game.Id, SteamAppId = 620, ApiName = "RETIRED", DisplayName = "Retired", Archived = true });
            db.SteamAchievementSets.Add(new() { UserId = game.UserId, GameId = game.Id, SteamAppId = 620, GroupId = 0, DisplayName = "Base", TotalAchievements = 3, CompletionAchievements = 2, IsAchievable = true, IsCompleted = true });
            await db.SaveChangesAsync();
        }

        var detail = await client.GetFromJsonAsync<SteamAchievementProgressDto>($"/api/steam/achievements/{factory.UserAGameId}/progress");
        Assert.True(detail!.IsCompleted);
        Assert.False(detail.IsFullyCompleted);
        Assert.True(detail.CompletionPreserved);
        Assert.Equal(2, detail.TotalAchievements);
        Assert.Equal(2, detail.Achievements.Count);
        var legacy = await client.GetFromJsonAsync<List<SteamAchievementDto>>($"/api/steam/achievements/{factory.UserAGameId}");
        Assert.Equal(detail.Achievements.Select(achievement => achievement.ApiName), legacy!.Select(achievement => achievement.ApiName));
        var other = await ClientAsync("HouseholdUserB");
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/api/steam/achievements/{factory.UserAGameId}/progress")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().GetAsync($"/api/steam/achievements/{factory.UserAGameId}/progress")).StatusCode);
    }

    [Fact]
    public async Task Both_filters_have_identical_meaning_in_library_and_automatic_playlists()
    {
        var client = await ClientAsync("HouseholdUserA");
        var prefix = $"Completion-{Guid.NewGuid():N}";
        var ids = new List<int>();
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<GamesDbContext>();
            var owner = await db.Games.SingleAsync(game => game.Id == factory.UserAGameId);
            for (var index = 0; index < 5; index++)
            {
                var game = new Game { UserId = owner.UserId, StatusId = owner.StatusId, Name = $"{prefix}-{index}", SteamAppId = index == 4 ? null : 1000 + index, IsManuallyCompleted = index == 4, SteamCompletionPreserved = index == 2 };
                db.Games.Add(game);
                await db.SaveChangesAsync();
                ids.Add(game.Id);
                if (index == 4) continue;
                db.SteamAchievements.Add(new() { UserId = owner.UserId, GameId = game.Id, SteamAppId = game.SteamAppId!.Value, ApiName = "ONE", Achieved = index < 3 });
                if (index is 1 or 2) db.SteamAchievements.Add(new() { UserId = owner.UserId, GameId = game.Id, SteamAppId = game.SteamAppId.Value, ApiName = "TWO" });
                if (index == 1) db.SteamAchievementSets.Add(new() { UserId = owner.UserId, GameId = game.Id, SteamAppId = game.SteamAppId.Value, GroupId = 0, DisplayName = "Base", TotalAchievements = 2, CompletionAchievements = 2, IsCompleted = true });
                await db.SaveChangesAsync();
            }
        }
        var broad = await client.GetFromJsonAsync<PagedResult<GameDto>>($"/api/games?search={prefix}&fullCompletion=true&pageSize=100");
        var strict = await client.GetFromJsonAsync<PagedResult<GameDto>>($"/api/games?search={prefix}&allAchievements=true&pageSize=100");
        Assert.Equal(new[] { ids[0], ids[1], ids[2], ids[4] }.Order(), broad!.Data.Select(game => game.Id).Order());
        Assert.Equal(new[] { ids[0], ids[4] }.Order(), strict!.Data.Select(game => game.Id).Order());
        Assert.All(broad.Data, game => Assert.True(game.IsCompleted));
        Assert.All(strict.Data, game => Assert.True(game.IsFullyCompleted));

        foreach (var all in new[] { false, true })
        {
            var response = await client.PostAsJsonAsync("/api/playlists", new PlaylistCreateDto
            {
                Name = $"{prefix}-playlist-{all}", IsAutomatic = true,
                Rules = new() { Search = prefix, FullCompletion = all ? null : true, AllAchievements = all ? true : null }
            });
            response.EnsureSuccessStatusCode();
            var playlist = (await response.Content.ReadFromJsonAsync<PlaylistDto>())!;
            var refreshed = (await client.GetFromJsonAsync<PlaylistDto>($"/api/playlists/{playlist.Id}"))!;
            Assert.Equal((all ? strict : broad).Data.Select(game => game.Id).Order(), refreshed.Items.Select(item => item.GameId).Order());
            Assert.All(refreshed.Items, item => Assert.True(item.Game.IsCompleted));
            Assert.Equal(all ? true : (bool?)null, refreshed.Rules!.AllAchievements);
        }
    }

    [Fact]
    public async Task Csv_roundtrip_preserves_manual_and_historical_completion_and_missing_headers_do_not_clear_them()
    {
        var client = await ClientAsync("HouseholdUserA");
        await SetFlagsAsync(true);
        var csv = await client.GetStringAsync("/api/DataExport/full");
        Assert.Contains("IsManuallyCompleted", csv);
        Assert.Contains("SteamCompletionPreserved", csv);
        await SetFlagsAsync(false);
        using (var form = CsvForm(csv)) (await client.PostAsync("/api/DataExport/full", form)).EnsureSuccessStatusCode();
        var restored = await client.GetFromJsonAsync<GameDto>($"/api/games/{factory.UserAGameId}");
        Assert.True(restored!.IsManuallyCompleted);
        Assert.True(restored.SteamCompletionPreserved);
        using (var form = CsvForm("Type,Name,Status\nGame,Game A,Backlog A\n")) (await client.PostAsync("/api/DataExport/full", form)).EnsureSuccessStatusCode();
        var oldImport = await client.GetFromJsonAsync<GameDto>($"/api/games/{factory.UserAGameId}");
        Assert.True(oldImport!.IsManuallyCompleted);
        Assert.True(oldImport.SteamCompletionPreserved);
    }

    private async Task SetFlagsAsync(bool value)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<GamesDbContext>();
        var game = await db.Games.SingleAsync(game => game.Id == factory.UserAGameId);
        game.IsManuallyCompleted = value;
        game.SteamCompletionPreserved = value;
        await db.SaveChangesAsync();
    }

    private static MultipartFormDataContent CsvForm(string csv)
    {
        var form = new MultipartFormDataContent();
        form.Add(new StringContent(csv, Encoding.UTF8, "text/csv"), "csvFile", "completion.csv");
        return form;
    }

    private async Task<HttpClient> ClientAsync(string username)
    {
        var token = await factory.CreateWebTokenAsync(username);
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }
}
