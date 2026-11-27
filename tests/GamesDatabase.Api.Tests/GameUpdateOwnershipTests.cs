using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using GamesDatabase.Api.Application.Interfaces;
using GamesDatabase.Api.Contracts;
using GamesDatabase.Api.Domain.Entities;
using GamesDatabase.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GamesDatabase.Api.Tests;

public sealed class GameUpdateOwnershipTests
{
    private sealed record Catalog(int Status, int Platform, int PlayedStatus, int PlayWith);
    private sealed record Fixture(HttpClient Client, int OwnerId, int GameId, Catalog Owned, Catalog Alternate, Catalog Foreign);

    private static async Task<Fixture> Seed(GamesDatabaseApiFactory factory)
    {
        var client = factory.CreateClient(); await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<GamesDbContext>();
        var owner = new User { Username = "PUT-owner-" + Guid.NewGuid().ToString("N"), Role = UserRole.Standard };
        var other = new User { Username = "PUT-other-" + Guid.NewGuid().ToString("N"), Role = UserRole.Standard };
        db.Users.AddRange(owner, other); await db.SaveChangesAsync();
        async Task<Catalog> CreateCatalog(int userId, string label)
        {
            var status = new GameStatus { UserId = userId, Name = label + " status" };
            var platform = new GamePlatform { UserId = userId, Name = label + " platform" };
            var played = new GamePlayedStatus { UserId = userId, Name = label + " played" };
            var mode = new GamePlayWith { UserId = userId, Name = label + " mode" };
            db.AddRange(status, platform, played, mode); await db.SaveChangesAsync();
            return new(status.Id, platform.Id, played.Id, mode.Id);
        }
        var owned = await CreateCatalog(owner.Id, "Owned"); var alternate = await CreateCatalog(owner.Id, "Alternate"); var foreign = await CreateCatalog(other.Id, "Private foreign");
        var game = new Game { UserId = owner.Id, Name = "Original synthetic game", StatusId = owned.Status, PlatformId = owned.Platform, PlayedStatusId = owned.PlayedStatus, Comment = "Retain this note" };
        game.GamePlayWiths.Add(new() { PlayWithId = owned.PlayWith }); db.Games.Add(game); await db.SaveChangesAsync();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", scope.ServiceProvider.GetRequiredService<IAuthService>().GenerateToken(owner));
        return new(client, owner.Id, game.Id, owned, alternate, foreign);
    }

    private static Dictionary<string, object?> MixedPatch(Fixture fixture, string field, object? invalid)
    {
        var patch = new Dictionary<string, object?> { ["name"] = "Must not be saved", ["comment"] = "Must not replace note", ["favorite"] = true,
            ["statusId"] = fixture.Alternate.Status, ["platformId"] = fixture.Alternate.Platform, ["playedStatusId"] = fixture.Alternate.PlayedStatus, ["playWithIds"] = new[] { fixture.Alternate.PlayWith } };
        patch[field] = invalid; return patch;
    }

    private static int CatalogId(Catalog catalog, string field) => field switch { "statusId" => catalog.Status, "platformId" => catalog.Platform, "playedStatusId" => catalog.PlayedStatus, _ => catalog.PlayWith };

    [Theory]
    [InlineData("statusId", false)]
    [InlineData("platformId", false)]
    [InlineData("playedStatusId", false)]
    [InlineData("playWithIds", false)]
    [InlineData("statusId", true)]
    [InlineData("platformId", true)]
    [InlineData("playedStatusId", true)]
    [InlineData("playWithIds", true)]
    public async Task Mixed_HTTP_patch_rejects_foreign_or_missing_catalogs_without_any_partial_write(string field, bool missing)
    {
        using var factory = new GamesDatabaseApiFactory(); var fixture = await Seed(factory); using var client = fixture.Client;
        var before = (await client.GetFromJsonAsync<GameDto>($"/api/games/{fixture.GameId}"))!;
        var invalidId = missing ? int.MaxValue : CatalogId(fixture.Foreign, field);
        object invalid = field == "playWithIds" ? new[] { fixture.Alternate.PlayWith, invalidId } : invalidId;
        var response = await client.PutAsJsonAsync($"/api/games/{fixture.GameId}", MixedPatch(fixture, field, invalid));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var after = (await client.GetFromJsonAsync<GameDto>($"/api/games/{fixture.GameId}"))!;
        Assert.Equal(JsonSerializer.Serialize(before), JsonSerializer.Serialize(after));
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<GamesDbContext>();
        Assert.Equal(new[] { fixture.Owned.PlayWith }, await db.GamePlayWithMappings.Where(value => value.GameId == fixture.GameId).Select(value => value.PlayWithId).ToArrayAsync());
        Assert.Empty(await db.GameHistoryEntries.Where(value => value.GameId == fixture.GameId).ToListAsync());
    }

    [Theory]
    [InlineData("statusId")]
    [InlineData("platformId")]
    [InlineData("playedStatusId")]
    [InlineData("playWithIds")]
    public async Task Foreign_catalog_rejection_leaves_no_pending_tracked_mutations_or_history(string field)
    {
        using var factory = new GamesDatabaseApiFactory(); var fixture = await Seed(factory); using var client = fixture.Client;
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<GamesDbContext>();
        var service = scope.ServiceProvider.GetRequiredService<IGameService>();
        object invalid = field == "playWithIds" ? new[] { fixture.Alternate.PlayWith, fixture.Foreign.PlayWith } : CatalogId(fixture.Foreign, field);
        var result = await service.UpdateGameAsync(fixture.GameId, JsonSerializer.SerializeToElement(MixedPatch(fixture, field, invalid)), fixture.OwnerId);
        Assert.False(result.Success); Assert.False(db.ChangeTracker.HasChanges()); Assert.Equal(0, await db.SaveChangesAsync());
        Assert.Empty(await db.GameHistoryEntries.Where(value => value.GameId == fixture.GameId).ToListAsync());
    }

    [Theory]
    [InlineData("statusId", "\"not-an-id\"")]
    [InlineData("platformId", "true")]
    [InlineData("playedStatusId", "1.5")]
    [InlineData("playWithIds", "{}")]
    [InlineData("playWithIds", "[null]")]
    [InlineData("playWithIds", "[0]")]
    public async Task Malformed_catalog_values_reject_the_entire_HTTP_patch(string field, string json)
    {
        using var factory = new GamesDatabaseApiFactory(); var fixture = await Seed(factory); using var client = fixture.Client;
        var before = (await client.GetFromJsonAsync<GameDto>($"/api/games/{fixture.GameId}"))!;
        var response = await client.PutAsJsonAsync($"/api/games/{fixture.GameId}", MixedPatch(fixture, field, JsonSerializer.Deserialize<JsonElement>(json)));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(JsonSerializer.Serialize(before), JsonSerializer.Serialize(await client.GetFromJsonAsync<GameDto>($"/api/games/{fixture.GameId}")));
    }

    [Fact]
    public async Task Owned_relations_still_update_and_omitted_versus_null_optional_fields_keep_their_semantics()
    {
        using var factory = new GamesDatabaseApiFactory(); var fixture = await Seed(factory); using var client = fixture.Client;
        (await client.PutAsJsonAsync($"/api/games/{fixture.GameId}", new { name = "Valid owned update", statusId = fixture.Alternate.Status,
            platformId = fixture.Alternate.Platform, playedStatusId = fixture.Alternate.PlayedStatus, playWithIds = new[] { fixture.Alternate.PlayWith } })).EnsureSuccessStatusCode();
        var game = (await client.GetFromJsonAsync<GameDto>($"/api/games/{fixture.GameId}"))!;
        Assert.Equal(fixture.Alternate.Status, game.StatusId); Assert.Equal(fixture.Alternate.Platform, game.PlatformId); Assert.Equal(fixture.Alternate.PlayedStatus, game.PlayedStatusId); Assert.Equal(new[] { fixture.Alternate.PlayWith }, game.PlayWithIds);
        (await client.PutAsJsonAsync($"/api/games/{fixture.GameId}", new { comment = "Optional fields omitted" })).EnsureSuccessStatusCode();
        var omitted = (await client.GetFromJsonAsync<GameDto>($"/api/games/{fixture.GameId}"))!; Assert.Equal(game.PlatformId, omitted.PlatformId); Assert.Equal(game.PlayedStatusId, omitted.PlayedStatusId); Assert.Equal(game.PlayWithIds, omitted.PlayWithIds);
        (await client.PutAsJsonAsync($"/api/games/{fixture.GameId}", new { statusId = (int?)null, platformId = (int?)null, playedStatusId = (int?)null, playWithIds = (int[]?)null })).EnsureSuccessStatusCode();
        var cleared = (await client.GetFromJsonAsync<GameDto>($"/api/games/{fixture.GameId}"))!;
        Assert.Equal(fixture.Alternate.Status, cleared.StatusId); Assert.Null(cleared.PlatformId); Assert.Null(cleared.PlayedStatusId); Assert.Empty(cleared.PlayWithIds);
    }

    [Fact]
    public async Task Ordinary_PUT_still_requires_a_real_session_and_an_owned_game()
    {
        using var factory = new GamesDatabaseApiFactory(); var fixture = await Seed(factory); using var client = fixture.Client; using var anonymous = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PutAsJsonAsync($"/api/games/{fixture.GameId}", new { name = "Denied" })).StatusCode);
        var otherToken = await factory.CreateWebTokenAsync("HouseholdUserB"); using var other = factory.CreateClient(); other.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", otherToken);
        Assert.Equal(HttpStatusCode.NotFound, (await other.PutAsJsonAsync($"/api/games/{fixture.GameId}", new { statusId = fixture.Foreign.Status })).StatusCode);
    }

    [Fact]
    public async Task Repeated_owned_PlayWith_ids_are_normalized_to_a_single_owned_mapping()
    {
        using var factory = new GamesDatabaseApiFactory(); var fixture = await Seed(factory); using var client = fixture.Client;
        (await client.PutAsJsonAsync($"/api/games/{fixture.GameId}", new { playWithIds = new[] { fixture.Owned.PlayWith, fixture.Owned.PlayWith } })).EnsureSuccessStatusCode();
        var game = (await client.GetFromJsonAsync<GameDto>($"/api/games/{fixture.GameId}"))!;
        Assert.Equal(new[] { fixture.Owned.PlayWith }, game.PlayWithIds); Assert.Equal(fixture.Owned.Status, game.StatusId);
    }
}
