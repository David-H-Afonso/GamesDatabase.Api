using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using GamesDatabase.Api.Application.Interfaces;
using GamesDatabase.Api.Contracts;
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
