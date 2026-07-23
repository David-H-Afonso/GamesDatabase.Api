using GamesDatabase.Api.Application.Interfaces;
using GamesDatabase.Api.Domain.Entities;
using GamesDatabase.Api.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace GamesDatabase.Api.Tests;

public sealed class GamesDatabaseApiFactory : WebApplicationFactory<Program>
{
    public const string RedirectUri = "https://household.test/callback";
    private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"gamesdatabase-household-tests-{Guid.NewGuid():N}.db");
    private readonly SemaphoreSlim _seedLock = new(1, 1);
    private bool _seeded;

    public int UserAGameId { get; private set; }
    public int UserBGameId { get; private set; }
    public int UserAAlternateStatusId { get; private set; }

    public GamesDatabaseApiFactory()
    {
        // Program reads deployment overrides before WebApplicationFactory's late
        // ConfigureAppConfiguration callback. Test-only process environment values
        // keep startup isolated from local configuration and the real database.
        Environment.SetEnvironmentVariable("GAMESDATABASE_DB_PATH", _databasePath);
        Environment.SetEnvironmentVariable("JWT_SECRET_KEY", "TEST_ONLY_NOT_A_DEPLOYMENT_SECRET_32_CHARS_MINIMUM");
        Environment.SetEnvironmentVariable("JWT_ISSUER", "GamesDatabase.Api.Tests");
        Environment.SetEnvironmentVariable("JWT_AUDIENCE", "GamesDatabase.Api.Tests.Client");
        Environment.SetEnvironmentVariable("HOUSEHOLD_CLIENT_ID", "household");
        Environment.SetEnvironmentVariable("HOUSEHOLD_REDIRECT_URIS", RedirectUri);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DatabaseSettings:DatabasePath"] = _databasePath,
                ["DatabaseSettings:EnableSensitiveDataLogging"] = "false",
                ["JwtSettings:SecretKey"] = "TEST_ONLY_NOT_A_DEPLOYMENT_SECRET_32_CHARS_MINIMUM",
                ["JwtSettings:Issuer"] = "GamesDatabase.Api.Tests",
                ["JwtSettings:Audience"] = "GamesDatabase.Api.Tests.Client",
                ["HouseholdIntegration:ClientId"] = "household",
                ["HouseholdIntegration:RedirectUris"] = RedirectUri,
                ["HouseholdIntegration:AccessTokenMinutes"] = "15",
                ["HouseholdIntegration:RefreshTokenDays"] = "30",
                ["HouseholdIntegration:AuthorizationCodeMinutes"] = "5"
            });
        });
    }

    public async Task EnsureSeededAsync()
    {
        await _seedLock.WaitAsync();
        try
        {
            if (_seeded)
            {
                return;
            }

            _ = CreateClient();
            await using var scope = Services.CreateAsyncScope();
            var context = scope.ServiceProvider.GetRequiredService<GamesDbContext>();

            var userA = new User { Username = "HouseholdUserA", Role = UserRole.Standard };
            var userB = new User { Username = "HouseholdUserB", Role = UserRole.Standard };
            context.Users.AddRange(userA, userB);
            await context.SaveChangesAsync();

            var userAStatus = new GameStatus { Name = "Backlog A", UserId = userA.Id };
            var userAAlternateStatus = new GameStatus { Name = "Playing A", UserId = userA.Id };
            var userBStatus = new GameStatus { Name = "Backlog B", UserId = userB.Id };
            context.GameStatuses.AddRange(userAStatus, userAAlternateStatus, userBStatus);
            await context.SaveChangesAsync();

            var gameA = new Game { Name = "Game A", UserId = userA.Id, StatusId = userAStatus.Id };
            var gameB = new Game { Name = "Game B", UserId = userB.Id, StatusId = userBStatus.Id };
            context.Games.AddRange(gameA, gameB);
            await context.SaveChangesAsync();

            UserAGameId = gameA.Id;
            UserBGameId = gameB.Id;
            UserAAlternateStatusId = userAAlternateStatus.Id;
            _seeded = true;
        }
        finally
        {
            _seedLock.Release();
        }
    }

    public async Task<string> CreateWebTokenAsync(string username)
    {
        await EnsureSeededAsync();
        await using var scope = Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<GamesDbContext>();
        var authService = scope.ServiceProvider.GetRequiredService<IAuthService>();
        var user = await context.Users.SingleAsync(item => item.Username == username);
        return authService.GenerateToken(user);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        _seedLock.Dispose();
        TryDelete(_databasePath);
        TryDelete(_databasePath + "-shm");
        TryDelete(_databasePath + "-wal");
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (IOException)
        {
            // Test cleanup is best effort on Windows, where SQLite may release handles late.
        }
    }
}
