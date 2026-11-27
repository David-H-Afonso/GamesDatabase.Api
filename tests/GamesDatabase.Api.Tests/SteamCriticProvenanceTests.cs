using GamesDatabase.Api.Application.Interfaces;
using GamesDatabase.Api.Contracts.Steam;
using GamesDatabase.Api.Domain.Entities;
using GamesDatabase.Api.Infrastructure.Persistence;
using GamesDatabase.Api.Tests.Fixtures;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GamesDatabase.Api.Tests;

public sealed class SteamCriticProvenanceTests
{
    [Theory]
    [InlineData("store", null)]
    [InlineData("library", null)]
    [InlineData("sync", null)]
    [InlineData("store", -1)]
    [InlineData("library", 101)]
    public async Task Missing_or_invalid_Metacritic_is_not_replaced_by_a_calculated_Steam_user_percentage_named_SteamDB(string flow, int? score)
    {
        using var root = new GamesDatabaseApiFactory(); var steam = new MetadataSteamFixture { MetacriticScore = score };
        using var factory = root.WithWebHostBuilder(builder => builder.ConfigureTestServices(services => { services.AddSingleton<ISteamApiService>(steam); services.AddSingleton<ISteamStoreService>(steam); }));
        using var client = factory.CreateClient(); await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<GamesDbContext>();
        var (ownerId, statusId) = await Seed(db); var service = scope.ServiceProvider.GetRequiredService<ISteamSyncService>();
        int gameId;
        if (flow == "store") gameId = (await service.AddStoreGameAsync(ownerId, MetadataSteamFixture.AppId)).GdbGameId!.Value;
        else if (flow == "library") gameId = Assert.Single((await service.ImportLibraryAsync(ownerId, new() { AppIds = [MetadataSteamFixture.AppId], CreateMissing = true })).ImportedGames).GdbGameId!.Value;
        else
        {
            var linked = new Game { UserId = ownerId, StatusId = statusId, Name = MetadataSteamFixture.GameName, SteamAppId = MetadataSteamFixture.AppId };
            db.Games.Add(linked); await db.SaveChangesAsync(); gameId = linked.Id; Assert.True((await service.SyncGameAsync(ownerId, gameId)).Success);
        }
        var game = await db.Games.AsNoTracking().SingleAsync(game => game.Id == gameId);
        Assert.Null(game.Critic); Assert.Null(game.CriticProvider); Assert.Equal(0, steam.ReviewSummaryCalls);
        Assert.Equal(MetadataSteamFixture.AppId, game.SteamAppId); Assert.Equal(120, game.SteamPlaytimeForever);
    }

    [Theory]
    [InlineData("Metacritic")]
    [InlineData("OpenCritic")]
    [InlineData("SteamDB")]
    public async Task Existing_curated_or_legacy_rating_is_never_rewritten_by_a_later_sync(string provider)
    {
        using var root = new GamesDatabaseApiFactory(); var steam = new MetadataSteamFixture();
        using var factory = root.WithWebHostBuilder(builder => builder.ConfigureTestServices(services => { services.AddSingleton<ISteamApiService>(steam); services.AddSingleton<ISteamStoreService>(steam); }));
        using var client = factory.CreateClient(); await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<GamesDbContext>();
        var (ownerId, statusId) = await Seed(db); var game = new Game { UserId = ownerId, StatusId = statusId, Name = MetadataSteamFixture.GameName, SteamAppId = MetadataSteamFixture.AppId, Critic = 83, CriticProvider = provider, Comment = "Keep curated fields" };
        db.Games.Add(game); await db.SaveChangesAsync(); Assert.True((await scope.ServiceProvider.GetRequiredService<ISteamSyncService>().SyncGameAsync(ownerId, game.Id)).Success);
        await db.Entry(game).ReloadAsync(); Assert.Equal(83, game.Critic); Assert.Equal(provider, game.CriticProvider); Assert.Equal("Keep curated fields", game.Comment); Assert.Equal(0, steam.ReviewSummaryCalls);
    }

    [Fact]
    public async Task A_valid_Steam_edition_Metacritic_score_keeps_its_real_provider()
    {
        using var root = new GamesDatabaseApiFactory(); var steam = new MetadataSteamFixture { MetacriticScore = 93 };
        using var factory = root.WithWebHostBuilder(builder => builder.ConfigureTestServices(services => { services.AddSingleton<ISteamApiService>(steam); services.AddSingleton<ISteamStoreService>(steam); }));
        using var client = factory.CreateClient(); await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<GamesDbContext>(); var (ownerId, _) = await Seed(db);
        var imported = await scope.ServiceProvider.GetRequiredService<ISteamSyncService>().AddStoreGameAsync(ownerId, MetadataSteamFixture.AppId);
        var game = await db.Games.AsNoTracking().SingleAsync(game => game.Id == imported.GdbGameId); Assert.Equal(93, game.Critic); Assert.Equal("Metacritic", game.CriticProvider); Assert.Equal(0, steam.ReviewSummaryCalls);
    }

    private static async Task<(int OwnerId, int StatusId)> Seed(GamesDbContext db)
    {
        var owner = new User { Username = "Critic-import-" + Guid.NewGuid().ToString("N"), SteamId = "76561198000000000", Role = UserRole.Standard };
        db.Users.Add(owner); await db.SaveChangesAsync(); var status = new GameStatus { UserId = owner.Id, Name = "Fixture backlog", StatusType = SpecialStatusType.NotFulfilled, IsDefault = true }; db.GameStatuses.Add(status); await db.SaveChangesAsync(); return (owner.Id, status.Id);
    }
}
