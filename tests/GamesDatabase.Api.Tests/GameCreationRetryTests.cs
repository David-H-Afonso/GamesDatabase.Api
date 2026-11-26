using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using GamesDatabase.Api.Application.Interfaces;
using GamesDatabase.Api.Contracts;
using GamesDatabase.Api.Domain.Entities;
using GamesDatabase.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GamesDatabase.Api.Tests;

public sealed class GameCreationRetryTests(GamesDatabaseApiFactory factory) : IClassFixture<GamesDatabaseApiFactory>
{
    private async Task<(int Id, int Status, HttpClient Client)> Owner()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<GamesDbContext>();
        var owner = new User { Username = "Create-retry-" + Guid.NewGuid().ToString("N"), Role = UserRole.Standard };
        db.Users.Add(owner); await db.SaveChangesAsync();
        var status = new GameStatus { UserId = owner.Id, Name = "Fixture backlog" };
        db.GameStatuses.Add(status); await db.SaveChangesAsync();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", scope.ServiceProvider.GetRequiredService<IAuthService>().GenerateToken(owner));
        return (owner.Id, status.Id, client);
    }

    [Fact]
    public async Task Repeating_a_draft_row_returns_its_game_without_another_game_or_history_write()
    {
        var owner = await Owner();
        var request = new { name = "Retry-safe fixture", statusId = owner.Status, creationRequestId = Guid.NewGuid() };
        var first = await owner.Client.PostAsJsonAsync("/api/games", request); Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        var created = (await first.Content.ReadFromJsonAsync<GameDto>())!;
        int[] originalHistory;
        await using (var before = factory.Services.CreateAsyncScope())
        {
            var history = await before.ServiceProvider.GetRequiredService<GamesDbContext>().GameHistoryEntries.Where(entry => entry.GameId == created.Id).OrderBy(entry => entry.Id).ToListAsync();
            Assert.Equal(new[] { "Name", "Status" }, history.Select(entry => entry.Field));
            originalHistory = history.Select(entry => entry.Id).ToArray();
        }
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var replay = await owner.Client.PostAsJsonAsync("/api/games", request); Assert.Equal(HttpStatusCode.Created, replay.StatusCode);
            Assert.Equal(created.Id, (await replay.Content.ReadFromJsonAsync<GameDto>())!.Id);
        }
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<GamesDbContext>();
        Assert.Equal(1, await db.Games.CountAsync(game => game.UserId == owner.Id));
        Assert.Equal(originalHistory, await db.GameHistoryEntries.Where(history => history.GameId == created.Id).OrderBy(history => history.Id).Select(history => history.Id).ToArrayAsync());
        Assert.DoesNotContain("creationRequestId", await first.Content.ReadAsStringAsync());
        Assert.DoesNotContain("creationRequestHash", await first.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_reused_key_with_changed_fields_conflicts_instead_of_creating_or_overwriting()
    {
        var owner = await Owner(); var key = Guid.NewGuid();
        var original = await owner.Client.PostAsJsonAsync("/api/games", new { name = "Original fixture", statusId = owner.Status, creationRequestId = key }); original.EnsureSuccessStatusCode();
        var response = await owner.Client.PostAsJsonAsync("/api/games", new { name = "Changed after an uncertain response", statusId = owner.Status, creationRequestId = key });
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode); Assert.Contains("creationRequestConflict", await response.Content.ReadAsStringAsync());
        await using var scope = factory.Services.CreateAsyncScope();
        var game = Assert.Single(await scope.ServiceProvider.GetRequiredService<GamesDbContext>().Games.Where(game => game.UserId == owner.Id).ToListAsync());
        Assert.Equal("Original fixture", game.Name);
    }

    [Fact]
    public async Task Keys_are_owned_and_invalid_rows_do_not_consume_them()
    {
        var first = await Owner(); var other = await Owner(); var key = Guid.NewGuid();
        Assert.Equal(HttpStatusCode.BadRequest, (await first.Client.PostAsJsonAsync("/api/games", new { name = "Invalid fixture", statusId = other.Status, creationRequestId = key })).StatusCode);
        var a = await first.Client.PostAsJsonAsync("/api/games", new { name = "First owner's fixture", statusId = first.Status, creationRequestId = key }); a.EnsureSuccessStatusCode();
        var b = await other.Client.PostAsJsonAsync("/api/games", new { name = "Other owner's fixture", statusId = other.Status, creationRequestId = key }); b.EnsureSuccessStatusCode();
        Assert.NotEqual((await a.Content.ReadFromJsonAsync<GameDto>())!.Id, (await b.Content.ReadFromJsonAsync<GameDto>())!.Id);
    }

    [Fact]
    public async Task Concurrent_replays_cannot_create_two_rows()
    {
        var owner = await Owner(); var request = new { name = "Concurrent fixture", statusId = owner.Status, creationRequestId = Guid.NewGuid() };
        var responses = await Task.WhenAll(owner.Client.PostAsJsonAsync("/api/games", request), owner.Client.PostAsJsonAsync("/api/games", request));
        Assert.All(responses, response => Assert.Equal(HttpStatusCode.Created, response.StatusCode));
        Assert.Equal((await responses[0].Content.ReadFromJsonAsync<GameDto>())!.Id, (await responses[1].Content.ReadFromJsonAsync<GameDto>())!.Id);
        await using var scope = factory.Services.CreateAsyncScope(); Assert.Equal(1, await scope.ServiceProvider.GetRequiredService<GamesDbContext>().Games.CountAsync(game => game.UserId == owner.Id));
    }

    [Theory]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    [InlineData("not-a-uuid")]
    public async Task Malformed_or_empty_keys_fail_before_persistence(string key)
    {
        var owner = await Owner();
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.Client.PostAsJsonAsync("/api/games", new { name = "Rejected fixture", statusId = owner.Status, creationRequestId = key })).StatusCode);
        await using var scope = factory.Services.CreateAsyncScope(); Assert.False(await scope.ServiceProvider.GetRequiredService<GamesDbContext>().Games.AnyAsync(game => game.UserId == owner.Id));
    }
}
