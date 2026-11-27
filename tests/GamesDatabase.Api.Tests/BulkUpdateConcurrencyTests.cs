using System.Data.Common;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using GamesDatabase.Api.Application.Interfaces;
using GamesDatabase.Api.Application.Services;
using GamesDatabase.Api.Contracts;
using GamesDatabase.Api.Domain.Entities;
using GamesDatabase.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GamesDatabase.Api.Tests;

public sealed class BulkUpdateConcurrencyTests(GamesDatabaseApiFactory factory) : IClassFixture<GamesDatabaseApiFactory>
{
    private async Task<(HttpClient Client, int UserId, int GameId, int StatusId)> Owner()
    {
        var client = factory.CreateClient(); await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<GamesDbContext>();
        var user = new User { Username = "Bulk-concurrency-" + Guid.NewGuid().ToString("N"), Role = UserRole.Standard }; db.Users.Add(user); await db.SaveChangesAsync();
        var status = new GameStatus { UserId = user.Id, Name = "Owned status" }; db.GameStatuses.Add(status); await db.SaveChangesAsync();
        var game = new Game { UserId = user.Id, StatusId = status.Id, Name = "Steam settings fixture", SteamAppId = 620, SteamPlaytimeForever = 90 }; db.Games.Add(game); await db.SaveChangesAsync();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", scope.ServiceProvider.GetRequiredService<IAuthService>().GenerateToken(user));
        return (client, user.Id, game.Id, status.Id);
    }

    [Fact]
    public async Task Reapplying_import_settings_acknowledges_the_row_without_a_fake_edit_or_unnecessary_write()
    {
        var owner = await Owner(); var before = (await owner.Client.GetFromJsonAsync<GameDto>($"/api/games/{owner.GameId}"))!;
        var response = await owner.Client.PatchAsJsonAsync("/api/games/bulk", new { gameIds = new[] { owner.GameId }, statusId = owner.StatusId }); response.EnsureSuccessStatusCode(); Assert.Equal(1, (await response.Content.ReadFromJsonAsync<BulkUpdateResult>())!.UpdatedCount);
        var after = (await owner.Client.GetFromJsonAsync<GameDto>($"/api/games/{owner.GameId}"))!; Assert.Equal(before.UpdatedAt, after.UpdatedAt); Assert.Equal(before.SteamPlaytimeForever, after.SteamPlaytimeForever);
    }

    [Fact]
    public async Task Bulk_settings_validate_catalog_ownership_before_changing_any_row()
    {
        var owner = await Owner(); var other = await Owner();
        var rejected = await owner.Client.PatchAsJsonAsync("/api/games/bulk", new { gameIds = new[] { owner.GameId }, statusId = other.StatusId, favorite = true }); Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        var game = (await owner.Client.GetFromJsonAsync<GameDto>($"/api/games/{owner.GameId}"))!; Assert.Equal(owner.StatusId, game.StatusId); Assert.False(game.Favorite);
    }

    [Fact]
    public async Task Bulk_reads_and_save_share_one_immediate_transaction_instead_of_read_then_upgrade()
    {
        var owner = await Owner(); await using var scope = factory.Services.CreateAsyncScope(); var existing = scope.ServiceProvider.GetRequiredService<GamesDbContext>();
        var probe = new TransactionProbe(); var options = new DbContextOptionsBuilder<GamesDbContext>().UseSqlite(existing.Database.GetConnectionString()).AddInterceptors(probe).Options;
        await using var db = new GamesDbContext(options); var service = new GameService(db, null!, null!);
        var result = await service.BulkUpdateGamesAsync(new BulkUpdateGameDto { GameIds = [owner.GameId], Favorite = true }, owner.UserId);
        Assert.True(result.Success); Assert.True(probe.SawRead); Assert.True(probe.AllReadsTransactional); Assert.Null(db.Database.CurrentTransaction);
    }

    [Fact]
    public async Task A_competing_Steam_cache_writer_finishes_before_the_first_settings_request_without_retry_or_lost_playtime()
    {
        var owner = await Owner(); await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<GamesDbContext>(); await db.Database.OpenConnectionAsync();
        await using (var journal = db.Database.GetDbConnection().CreateCommand()) { journal.CommandText = "PRAGMA journal_mode=DELETE"; Assert.Equal("delete", await journal.ExecuteScalarAsync()); }
        await using var writer = await db.Database.BeginTransactionAsync();
        await db.Games.Where(game => game.Id == owner.GameId && game.UserId == owner.UserId).ExecuteUpdateAsync(setters => setters.SetProperty(game => game.SteamPlaytimeForever, 4321));
        var pending = Task.Run(() => owner.Client.PatchAsJsonAsync("/api/games/bulk", new { gameIds = new[] { owner.GameId }, favorite = true }));
        try { await Task.Delay(150); Assert.False(pending.IsCompleted); }
        finally { await writer.CommitAsync(); }
        var response = await pending.WaitAsync(TimeSpan.FromSeconds(10)); response.EnsureSuccessStatusCode(); Assert.Equal(1, (await response.Content.ReadFromJsonAsync<BulkUpdateResult>())!.UpdatedCount);
        var after = (await owner.Client.GetFromJsonAsync<GameDto>($"/api/games/{owner.GameId}"))!; Assert.True(after.Favorite); Assert.Equal(4321, after.SteamPlaytimeForever);
    }

    [Fact]
    public async Task Relationship_only_bulk_edits_mark_a_real_edit_and_replaying_the_same_membership_is_a_no_op()
    {
        var owner = await Owner(); int playWithId;
        await using (var scope = factory.Services.CreateAsyncScope()) { var db = scope.ServiceProvider.GetRequiredService<GamesDbContext>(); var mode = new GamePlayWith { UserId = owner.UserId, Name = "Owned mode" }; db.GamePlayWiths.Add(mode); await db.SaveChangesAsync(); playWithId = mode.Id; }
        var before = (await owner.Client.GetFromJsonAsync<GameDto>($"/api/games/{owner.GameId}"))!;
        (await owner.Client.PatchAsJsonAsync("/api/games/bulk", new { gameIds = new[] { owner.GameId }, playWithIds = new[] { playWithId, playWithId } })).EnsureSuccessStatusCode();
        var after = (await owner.Client.GetFromJsonAsync<GameDto>($"/api/games/{owner.GameId}"))!; Assert.True(after.UpdatedAt > before.UpdatedAt); Assert.Single(after.PlayWithIds); Assert.Equal(playWithId, after.PlayWithIds[0]);
        (await owner.Client.PatchAsJsonAsync("/api/games/bulk", new { gameIds = new[] { owner.GameId }, playWithIds = new[] { playWithId } })).EnsureSuccessStatusCode(); Assert.Equal(after.UpdatedAt, (await owner.Client.GetFromJsonAsync<GameDto>($"/api/games/{owner.GameId}"))!.UpdatedAt);
    }

    private sealed class TransactionProbe : DbCommandInterceptor
    {
        public bool SawRead { get; private set; }
        public bool AllReadsTransactional { get; private set; } = true;
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken token = default)
        {
            if (command.CommandText.TrimStart().StartsWith("SELECT", StringComparison.OrdinalIgnoreCase)) { SawRead = true; AllReadsTransactional &= command.Transaction != null; }
            return ValueTask.FromResult(result);
        }
    }
}
