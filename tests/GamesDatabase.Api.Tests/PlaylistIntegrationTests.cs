using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using GamesDatabase.Api.Application.Interfaces;
using GamesDatabase.Api.Contracts;
using GamesDatabase.Api.Domain.Entities;
using GamesDatabase.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GamesDatabase.Api.Tests;

public sealed class PlaylistIntegrationTests : IClassFixture<GamesDatabaseApiFactory>
{
    private readonly GamesDatabaseApiFactory factory;

    public PlaylistIntegrationTests(GamesDatabaseApiFactory factory)
    {
        this.factory = factory;
    }

    [Fact]
    public async Task Playlist_Uses_first_game_images_and_reorders_playlists_and_items()
    {
        var client = await CreateClientAsync("HouseholdUserA");
        var gameA = await GetGameAsync(client, factory.UserAGameId);
        gameA.Hero = "https://images.test/game-a-hero.jpg";
        gameA.Cover = "https://images.test/game-a-cover.jpg";
        gameA.Logo = "https://images.test/game-a-logo.png";
        await client.PutAsJsonAsync($"/api/games/{gameA.Id}", new { hero = gameA.Hero, cover = gameA.Cover, logo = gameA.Logo });

        var secondGame = await CreateGameAsync(client, "Game C");
        var playlist = await CreatePlaylistAsync(client, "Pokemon order");
        var secondPlaylist = await CreatePlaylistAsync(client, "Yakuza order");

        var firstAdded = await client.PostAsJsonAsync($"/api/playlists/{playlist.Id}/items", new AddPlaylistItemDto { GameId = gameA.Id });
        firstAdded.EnsureSuccessStatusCode();
        var secondAdded = await client.PostAsJsonAsync($"/api/playlists/{playlist.Id}/items", new AddPlaylistItemDto { GameId = secondGame.Id });
        secondAdded.EnsureSuccessStatusCode();

        var detail = await client.GetFromJsonAsync<PlaylistDto>($"/api/playlists/{playlist.Id}");
        Assert.NotNull(detail);
        Assert.Equal(gameA.Hero, detail.HeroUrl);
        Assert.Equal(gameA.Cover, detail.CoverUrl);
        Assert.Equal(gameA.Logo, detail.LogoUrl);
        Assert.Null(detail.HeroUrlOverride);

        var itemIds = detail.Items.Select(item => item.Id).ToList();
        var reorderItems = await client.PostAsJsonAsync($"/api/playlists/{playlist.Id}/items/reorder", new ReorderPlaylistItemsDto { OrderedIds = itemIds.AsEnumerable().Reverse().ToList() });
        reorderItems.EnsureSuccessStatusCode();

        var playlists = await client.GetFromJsonAsync<List<PlaylistSummaryDto>>("/api/playlists");
        Assert.NotNull(playlists);
        var playlistIds = playlists.Select(item => item.Id).ToList();
        var reorderPlaylists = await client.PostAsJsonAsync("/api/playlists/reorder", new ReorderPlaylistsDto { OrderedIds = playlistIds.AsEnumerable().Reverse().ToList() });
        reorderPlaylists.EnsureSuccessStatusCode();

        var reorderedDetail = await client.GetFromJsonAsync<PlaylistDto>($"/api/playlists/{playlist.Id}");
        Assert.Equal(secondGame.Id, Assert.Single(reorderedDetail!.Items.Take(1)).GameId);
    }

    [Fact]
    public async Task Playlist_capacity_blocks_addition_preserves_items_and_can_be_cleared()
    {
        var client = await CreateClientAsync("HouseholdUserA");
        var first = await CreateGameAsync(client, "Capacity first");
        var second = await CreateGameAsync(client, "Capacity second");
        var third = await CreateGameAsync(client, "Capacity third");
        var playlist = await CreatePlaylistAsync(client, "Capacity test");
        Assert.Null(playlist.MaxLength);
        (await client.PostAsJsonAsync($"/api/playlists/{playlist.Id}/items", new { gameId = first.Id })).EnsureSuccessStatusCode();
        (await client.PostAsJsonAsync($"/api/playlists/{playlist.Id}/items", new { gameId = second.Id })).EnsureSuccessStatusCode();
        (await client.PutAsJsonAsync($"/api/playlists/{playlist.Id}", new { maxLength = 1 })).EnsureSuccessStatusCode();
        Assert.Equal(2, (await client.GetFromJsonAsync<PlaylistDto>($"/api/playlists/{playlist.Id}"))!.Items.Count);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync($"/api/playlists/{playlist.Id}/items", new { gameId = third.Id })).StatusCode);
        (await client.PutAsJsonAsync($"/api/playlists/{playlist.Id}", new { name = "Capacity renamed" })).EnsureSuccessStatusCode();
        Assert.Equal(1, (await client.GetFromJsonAsync<PlaylistDto>($"/api/playlists/{playlist.Id}"))!.MaxLength);
        var transfer = await client.GetFromJsonAsync<PlaylistTransferDto>($"/api/playlists/{playlist.Id}/export");
        Assert.Equal(1, transfer!.MaxLength);
        (await client.PutAsJsonAsync($"/api/playlists/{playlist.Id}", new { maxLength = (int?)null })).EnsureSuccessStatusCode();
        (await client.PostAsJsonAsync($"/api/playlists/{playlist.Id}/items", new { gameId = third.Id })).EnsureSuccessStatusCode();
        Assert.Null((await client.GetFromJsonAsync<PlaylistDto>($"/api/playlists/{playlist.Id}"))!.MaxLength);
    }

    [Fact]
    public async Task Playlist_includes_steam_achievement_stats_on_every_detail_request()
    {
        var client = await CreateClientAsync("HouseholdUserA");
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<GamesDbContext>();
            var game = await context.Games.SingleAsync(item => item.Id == factory.UserAGameId);
            context.SteamAchievements.AddRange(
                new SteamAchievement { UserId = game.UserId, GameId = game.Id, SteamAppId = 100, ApiName = "achievement-one", DisplayName = "Achievement One", Achieved = true },
                new SteamAchievement { UserId = game.UserId, GameId = game.Id, SteamAppId = 100, ApiName = "achievement-two", DisplayName = "Achievement Two", Achieved = true },
                new SteamAchievement { UserId = game.UserId, GameId = game.Id, SteamAppId = 100, ApiName = "achievement-three", DisplayName = "Achievement Three", Achieved = false });
            await context.SaveChangesAsync();
        }

        var playlist = await CreatePlaylistAsync(client, "Achievement stats");
        (await client.PostAsJsonAsync($"/api/playlists/{playlist.Id}/items", new AddPlaylistItemDto { GameId = factory.UserAGameId })).EnsureSuccessStatusCode();

        var firstRequest = await client.GetFromJsonAsync<PlaylistDto>($"/api/playlists/{playlist.Id}");
        var firstGame = Assert.Single(firstRequest!.Items).Game;
        Assert.Equal(2, firstGame.SteamAchievementsUnlocked);
        Assert.Equal(3, firstGame.SteamAchievementsTotal);

        var refreshedRequest = await client.GetFromJsonAsync<PlaylistDto>($"/api/playlists/{playlist.Id}");
        var refreshedGame = Assert.Single(refreshedRequest!.Items).Game;
        Assert.Equal(firstGame.SteamAchievementsUnlocked, refreshedGame.SteamAchievementsUnlocked);
        Assert.Equal(firstGame.SteamAchievementsTotal, refreshedGame.SteamAchievementsTotal);
    }

    [Fact]
    public async Task Playlist_cannot_reference_another_users_game_or_playlist()
    {
        var owner = await CreateClientAsync("HouseholdUserA");
        var other = await CreateClientAsync("HouseholdUserB");
        var playlist = await CreatePlaylistAsync(owner, "Private order");

        var foreignGame = await other.GetFromJsonAsync<GameDto>($"/api/games/{factory.UserBGameId}");
        var response = await owner.PostAsJsonAsync($"/api/playlists/{playlist.Id}/items", new AddPlaylistItemDto { GameId = foreignGame!.Id });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/api/playlists/{playlist.Id}")).StatusCode);
    }

    [Fact]
    public async Task Automatic_playlist_preserves_score_order_and_supports_story_order()
    {
        var client = await CreateClientAsync("HouseholdUserA");
        var lowScoreGame = await CreateGameAsync(client, "Sort test low");
        var highScoreGame = await CreateGameAsync(client, "Sort test high");

        (await client.PutAsJsonAsync($"/api/games/{lowScoreGame.Id}", new { critic = 80, story = 20 })).EnsureSuccessStatusCode();
        (await client.PutAsJsonAsync($"/api/games/{highScoreGame.Id}", new { critic = 80, story = 1 })).EnsureSuccessStatusCode();
        lowScoreGame = await GetGameAsync(client, lowScoreGame.Id);
        highScoreGame = await GetGameAsync(client, highScoreGame.Id);
        Assert.True(lowScoreGame.Score < highScoreGame.Score, $"Expected low score game to sort first, got {lowScoreGame.Score} and {highScoreGame.Score}.");

        var playlistResponse = await client.PostAsJsonAsync("/api/playlists", new PlaylistCreateDto
        {
            Name = "Automatic sorting",
            IsAutomatic = true,
            Rules = new PlaylistRulesDto
            {
                StatusIds = [factory.UserAInitialStatusId],
                Search = "Sort test",
                SortBy = "Score"
            }
        });
        playlistResponse.EnsureSuccessStatusCode();
        var playlist = (await playlistResponse.Content.ReadFromJsonAsync<PlaylistDto>())!;

        var scoreOrdered = await client.GetFromJsonAsync<PlaylistDto>($"/api/playlists/{playlist.Id}");
        Assert.Equal(lowScoreGame.Id, scoreOrdered!.Items[0].GameId);

        playlist.Rules!.SortBy = "Story";
        var update = await client.PutAsJsonAsync($"/api/playlists/{playlist.Id}", new PlaylistUpdateDto { Rules = playlist.Rules });
        update.EnsureSuccessStatusCode();

        var storyOrdered = await client.GetFromJsonAsync<PlaylistDto>($"/api/playlists/{playlist.Id}");
        Assert.Equal(highScoreGame.Id, storyOrdered!.Items[0].GameId);
    }

    [Fact]
    public async Task Full_csv_export_contains_playlists_and_old_csv_import_remains_compatible()
    {
        var client = await CreateClientAsync("HouseholdUserA");
        var playlist = await CreatePlaylistAsync(client, "Backup order");
        (await client.PostAsJsonAsync($"/api/playlists/{playlist.Id}/items", new AddPlaylistItemDto { GameId = factory.UserAGameId })).EnsureSuccessStatusCode();

        var export = await client.GetAsync("/api/DataExport/full");
        export.EnsureSuccessStatusCode();
        var csv = await export.Content.ReadAsStringAsync();
        Assert.Contains("Playlist", csv, StringComparison.Ordinal);
        Assert.Contains("PlaylistItem", csv, StringComparison.Ordinal);
        Assert.Contains(factory.UserAGameId.ToString(), csv, StringComparison.Ordinal);

        using var oldCsv = new MultipartFormDataContent();
        oldCsv.Add(new StringContent("Type,Name\nPlatform,Legacy platform\n", Encoding.UTF8, "text/csv"), "csvFile", "legacy.csv");
        var import = await client.PostAsync("/api/DataExport/full", oldCsv);
        Assert.Equal(HttpStatusCode.OK, import.StatusCode);
    }

    [Fact]
    public async Task Playlist_json_transfer_supports_exact_names_and_internal_ids()
    {
        var client = await CreateClientAsync("HouseholdUserA");
        var source = await CreatePlaylistAsync(client, "JSON source");
        (await client.PostAsJsonAsync($"/api/playlists/{source.Id}/items", new AddPlaylistItemDto { GameId = factory.UserAGameId })).EnsureSuccessStatusCode();

        var byName = await client.GetFromJsonAsync<PlaylistTransferDto>($"/api/playlists/{source.Id}/export?reference=Name");
        Assert.Null(Assert.Single(byName!.Games).GameId);
        Assert.Equal("Game A", byName.Games[0].Name);

        byName.Name = "JSON imported by name";
        byName.Games[0].GameId = null;
        var importedByName = await client.PostAsJsonAsync("/api/playlists/import", byName);
        importedByName.EnsureSuccessStatusCode();

        var byId = await client.GetFromJsonAsync<PlaylistTransferDto>($"/api/playlists/{source.Id}/export?reference=Id");
        byId!.Name = "JSON imported by id";
        var importedById = await client.PostAsJsonAsync("/api/playlists/import", byId);
        importedById.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Automatic_list_add_validates_rules_before_changing_status_and_supports_exclusion()
    {
        var client = await CreateClientAsync("HouseholdUserA");
        var game = await CreateGameAsync(client, "Guided intake test");
        var statusResponse = await client.PostAsJsonAsync("/api/gamestatus", new { name = "Must intake test", isActive = true });
        statusResponse.EnsureSuccessStatusCode();
        var status = (await statusResponse.Content.ReadFromJsonAsync<GameStatusDto>())!;
        var create = await client.PostAsJsonAsync("/api/playlists", new PlaylistCreateDto
        {
            Name = "Guided automatic intake", IsAutomatic = true,
            Rules = new PlaylistRulesDto { StatusIds = [status.Id], MinGrade = 90 }
        });
        create.EnsureSuccessStatusCode();
        var playlist = (await create.Content.ReadFromJsonAsync<PlaylistDto>())!;
        var rejected = await client.PostAsJsonAsync($"/api/playlists/{playlist.Id}/items", new AddPlaylistItemDto { GameId = game.Id, StatusId = status.Id });
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        Assert.Equal(game.StatusId, (await GetGameAsync(client, game.Id)).StatusId);
        (await client.PutAsJsonAsync($"/api/games/{game.Id}", new { grade = 95 })).EnsureSuccessStatusCode();
        (await client.PostAsJsonAsync($"/api/playlists/{playlist.Id}/items", new AddPlaylistItemDto { GameId = game.Id, StatusId = status.Id })).EnsureSuccessStatusCode();
        Assert.Equal(status.Id, (await GetGameAsync(client, game.Id)).StatusId);
        var detail = (await client.GetFromJsonAsync<PlaylistDto>($"/api/playlists/{playlist.Id}"))!;
        Assert.Contains(detail.Items, item => item.GameId == game.Id);
        (await client.DeleteAsync($"/api/playlists/{playlist.Id}/items/{game.Id}")).EnsureSuccessStatusCode();
        Assert.DoesNotContain((await client.GetFromJsonAsync<PlaylistDto>($"/api/playlists/{playlist.Id}"))!.Items, item => item.GameId == game.Id);
        Assert.Equal(status.Id, (await GetGameAsync(client, game.Id)).StatusId);
        (await client.PostAsJsonAsync($"/api/playlists/{playlist.Id}/items", new AddPlaylistItemDto { GameId = game.Id })).EnsureSuccessStatusCode();
        Assert.Contains((await client.GetFromJsonAsync<PlaylistDto>($"/api/playlists/{playlist.Id}"))!.Items, item => item.GameId == game.Id);
    }

    [Fact]
    public async Task Automatic_list_groups_status_priorities_and_allows_free_override()
    {
        var client = await CreateClientAsync("HouseholdUserA");
        var must = await CreateGameAsync(client, "Priority must test");
        var goal = await CreateGameAsync(client, "Priority goal test");
        var response = await client.PostAsJsonAsync("/api/gamestatus", new { name = "Goal priority test", isActive = true });
        response.EnsureSuccessStatusCode();
        var status = (await response.Content.ReadFromJsonAsync<GameStatusDto>())!;
        (await client.PatchAsJsonAsync($"/api/games/{goal.Id}/status", new { statusId = status.Id })).EnsureSuccessStatusCode();
        var rules = new PlaylistRulesDto { Search = "Priority", StatusIds = [must.StatusId, status.Id], GroupByStatus = true, StatusPriorityIds = [must.StatusId, status.Id], OrderedGameIds = [goal.Id, must.Id] };
        var created = await client.PostAsJsonAsync("/api/playlists", new PlaylistCreateDto { Name = "Status priority test", IsAutomatic = true, Rules = rules });
        created.EnsureSuccessStatusCode();
        var playlist = (await created.Content.ReadFromJsonAsync<PlaylistDto>())!;
        Assert.Equal(must.Id, playlist.Items[0].GameId);
        (await client.PostAsJsonAsync($"/api/playlists/{playlist.Id}/items/reorder", new ReorderPlaylistItemsDto { OrderedIds = [goal.Id, must.Id] })).EnsureSuccessStatusCode();
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<GamesDbContext>();
            Assert.False(await context.PlaylistItems.AnyAsync(item => item.PlaylistId == playlist.Id));
        }
        rules.GroupByStatus = false;
        (await client.PutAsJsonAsync($"/api/playlists/{playlist.Id}", new PlaylistUpdateDto { Rules = rules })).EnsureSuccessStatusCode();
        Assert.Equal(goal.Id, (await client.GetFromJsonAsync<PlaylistDto>($"/api/playlists/{playlist.Id}"))!.Items[0].GameId);
    }

    [Fact]
    public async Task Playlist_groups_are_private_and_deletion_preserves_playlists()
    {
        var owner = await CreateClientAsync("HouseholdUserA");
        var other = await CreateClientAsync("HouseholdUserB");
        var response = await owner.PostAsJsonAsync("/api/playlists/groups", new { name = "Sagas" });
        response.EnsureSuccessStatusCode();
        var group = (await response.Content.ReadFromJsonAsync<PlaylistGroupDto>())!;
        var secondResponse = await owner.PostAsJsonAsync("/api/playlists/groups", new { name = "Backlog group" });
        secondResponse.EnsureSuccessStatusCode();
        var secondGroup = (await secondResponse.Content.ReadFromJsonAsync<PlaylistGroupDto>())!;
        var groupIds = (await owner.GetFromJsonAsync<List<PlaylistGroupDto>>("/api/playlists/groups"))!.Select(item => item.Id).Reverse().ToList();
        (await owner.PostAsJsonAsync("/api/playlists/groups/reorder", new { orderedIds = groupIds })).EnsureSuccessStatusCode();
        Assert.Equal(groupIds, (await owner.GetFromJsonAsync<List<PlaylistGroupDto>>("/api/playlists/groups"))!.Select(item => item.Id).ToList());
        var withUngrouped = new List<int>(groupIds);
        withUngrouped.Insert(1, 0);
        (await owner.PostAsJsonAsync("/api/playlists/groups/reorder", new { orderedIds = withUngrouped })).EnsureSuccessStatusCode();
        var remembered = (await owner.GetFromJsonAsync<List<PlaylistGroupDto>>("/api/playlists/groups"))!;
        Assert.True(remembered[0].SortOrder < 0);
        Assert.True(remembered[1].SortOrder > 0);
        Assert.Equal(HttpStatusCode.BadRequest, (await other.PostAsJsonAsync("/api/playlists/groups/reorder", new { orderedIds = groupIds })).StatusCode);
        (await owner.DeleteAsync($"/api/playlists/groups/{secondGroup.Id}")).EnsureSuccessStatusCode();
        var playlist = await CreatePlaylistAsync(owner, "Grouped saga test");
        (await owner.PostAsJsonAsync($"/api/playlists/{playlist.Id}/move", new { groupId = group.Id })).EnsureSuccessStatusCode();
        Assert.Equal(group.Id, (await owner.GetFromJsonAsync<PlaylistDto>($"/api/playlists/{playlist.Id}"))!.GroupId);
        Assert.DoesNotContain((await other.GetFromJsonAsync<List<PlaylistGroupDto>>("/api/playlists/groups"))!, candidate => candidate.Id == group.Id);
        Assert.Equal(HttpStatusCode.NotFound, (await other.DeleteAsync($"/api/playlists/groups/{group.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await owner.PostAsJsonAsync("/api/playlists/groups", new { name = "sagas" })).StatusCode);
        (await owner.PutAsJsonAsync($"/api/playlists/groups/{group.Id}", new { name = "Series" })).EnsureSuccessStatusCode();
        (await owner.DeleteAsync($"/api/playlists/groups/{group.Id}")).EnsureSuccessStatusCode();
        Assert.Null((await owner.GetFromJsonAsync<PlaylistDto>($"/api/playlists/{playlist.Id}"))!.GroupId);
    }

    private async Task<HttpClient> CreateClientAsync(string username)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await factory.CreateWebTokenAsync(username));
        return client;
    }

    private static async Task<PlaylistDto> CreatePlaylistAsync(HttpClient client, string name)
    {
        var response = await client.PostAsJsonAsync("/api/playlists", new PlaylistCreateDto { Name = name });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<PlaylistDto>())!;
    }

    private static async Task<GameDto> GetGameAsync(HttpClient client, int id) => (await client.GetFromJsonAsync<GameDto>($"/api/games/{id}"))!;

    private static async Task<GameDto> CreateGameAsync(HttpClient client, string name)
    {
        var source = await GetGameAsync(client, 1);
        var response = await client.PostAsJsonAsync("/api/games", new GameCreateDto { Name = name, StatusId = source.StatusId });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<GameDto>())!;
    }
}
