using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using GamesDatabase.Api.Application.Interfaces;
using GamesDatabase.Api.Application.Services.Steam;
using GamesDatabase.Api.Contracts.Steam;
using GamesDatabase.Api.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace GamesDatabase.Api.Tests;

public sealed class SteamSyncJobTests(GamesDatabaseApiFactory factory) : IClassFixture<GamesDatabaseApiFactory>
{
    [Fact]
    public async Task Start_is_202_before_sync_finishes_and_duplicate_clicks_return_the_same_job()
    {
        await LinkUsersAsync();
        var fake = new ControlledSync();
        await using var app = App(fake);
        using var client = await ClientAsync(app);
        var response = await client.PostAsync("/api/steam/sync/jobs", null).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var first = (await response.Content.ReadFromJsonAsync<SteamSyncJobDto>())!;
        Assert.NotNull(response.Headers.Location);
        await fake.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var duplicate = await client.PostAsync("/api/steam/sync/jobs", null);
        Assert.Equal(first.Id, (await duplicate.Content.ReadFromJsonAsync<SteamSyncJobDto>())!.Id);
        Assert.Equal(1, fake.Calls);
        var current = await client.GetFromJsonAsync<SteamSyncCurrentJobResponse>("/api/steam/sync/jobs/current");
        Assert.Equal(first.Id, current!.Job!.Id);
        Assert.Equal(1, current.Job.ProcessedGames);
        Assert.True((await client.GetAsync($"/api/steam/sync/jobs/{first.Id}")).Headers.CacheControl!.NoStore);

        fake.Release.TrySetResult();
        var completed = await TerminalAsync(client, first.Id);
        Assert.Equal("completed", completed.State);
        Assert.Equal(3, completed.ProcessedGames);
        Assert.NotNull(completed.FinishedAt);
        Assert.Null(completed.CurrentGameName);
    }

    [Fact]
    public async Task Status_is_owner_scoped_and_all_job_routes_require_authentication()
    {
        await LinkUsersAsync();
        var fake = new ControlledSync();
        await using var app = App(fake);
        using var owner = await ClientAsync(app);
        using var other = await ClientAsync(app, "HouseholdUserB");
        var response = await owner.PostAsync("/api/steam/sync/jobs", null);
        var job = (await response.Content.ReadFromJsonAsync<SteamSyncJobDto>())!;
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/api/steam/sync/jobs/{job.Id}")).StatusCode);
        Assert.Null((await other.GetFromJsonAsync<SteamSyncCurrentJobResponse>("/api/steam/sync/jobs/current"))!.Job);
        using var anonymous = app.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsync("/api/steam/sync/jobs", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/steam/sync/jobs/current")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync($"/api/steam/sync/jobs/{job.Id}")).StatusCode);
        fake.Release.TrySetResult();
        await TerminalAsync(owner, job.Id);
    }

    [Fact]
    public async Task Active_job_blocks_conflicting_legacy_sync_and_unlink_without_waiting()
    {
        await LinkUsersAsync();
        var fake = new ControlledSync();
        await using var app = App(fake);
        using var client = await ClientAsync(app);
        var response = await client.PostAsync("/api/steam/sync/jobs", null);
        var job = (await response.Content.ReadFromJsonAsync<SteamSyncJobDto>())!;
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsync("/api/steam/sync", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsync($"/api/steam/sync/{factory.UserAGameId}", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.DeleteAsync("/api/steam/link")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/api/steam/import", new SteamImportRequest { AppIds = [620] })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/api/steam/store/add", new SteamAddStoreGameRequest { AppId = 620 })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/api/steam/link-game", new SteamLinkGameRequest { AppId = 620, GameId = factory.UserAGameId })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/api/steam/link/manual", new SteamManualLinkRequest { SteamId = "76561198000000001" })).StatusCode);
        fake.Release.TrySetResult();
        await TerminalAsync(client, job.Id);
    }

    [Fact]
    public async Task A_new_client_can_recover_a_running_job_without_starting_another()
    {
        await LinkUsersAsync();
        var fake = new ControlledSync();
        await using var app = App(fake);
        var first = await ClientAsync(app);
        var response = await first.PostAsync("/api/steam/sync/jobs", null);
        var job = (await response.Content.ReadFromJsonAsync<SteamSyncJobDto>())!;
        first.Dispose();
        using var returned = await ClientAsync(app);
        Assert.Equal(job.Id, (await returned.GetFromJsonAsync<SteamSyncCurrentJobResponse>("/api/steam/sync/jobs/current"))!.Job!.Id);
        Assert.Equal(1, fake.Calls);
        fake.Release.TrySetResult();
        await TerminalAsync(returned, job.Id);
    }

    [Fact]
    public async Task Fatal_errors_are_sanitized_and_preserve_reported_committed_progress()
    {
        await LinkUsersAsync();
        var fake = new ControlledSync { Fail = true };
        await using var app = App(fake);
        using var client = await ClientAsync(app);
        var response = await client.PostAsync("/api/steam/sync/jobs", null);
        var job = (await response.Content.ReadFromJsonAsync<SteamSyncJobDto>())!;
        await fake.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        fake.Release.TrySetResult();
        var failed = await TerminalAsync(client, job.Id);
        Assert.Equal("failed", failed.State);
        Assert.Equal("syncFailed", failed.Error);
        Assert.Equal(1, failed.ProcessedGames);
        Assert.DoesNotContain("PRIVATE_DETAIL", await client.GetStringAsync($"/api/steam/sync/jobs/{job.Id}"));
    }

    [Fact]
    public async Task Shutdown_interrupts_work_without_an_automatic_restart_or_new_job()
    {
        await LinkUsersAsync();
        var fake = new ControlledSync();
        await using var app = App(fake);
        using var client = await ClientAsync(app);
        var response = await client.PostAsync("/api/steam/sync/jobs", null);
        var job = (await response.Content.ReadFromJsonAsync<SteamSyncJobDto>())!;
        await fake.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var jobs = app.Services.GetRequiredService<SteamSyncJobService>();
        await jobs.StopAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("interrupted", jobs.GetById(UserId(app), job.Id)!.State);
        Assert.Equal(1, jobs.GetCurrent(UserId(app))!.ProcessedGames);
        Assert.Equal("serviceStopping", (await jobs.StartAsync(UserId(app))).Error);
        Assert.Equal(1, fake.Calls);
    }

    [Fact]
    public async Task Unlinked_users_cannot_enqueue_work_and_status_queries_never_start_sync()
    {
        await factory.EnsureSeededAsync();
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<GamesDbContext>();
            var user = await db.Users.SingleAsync(user => user.Username == "HouseholdUserA");
            user.SteamId = null;
            await db.SaveChangesAsync();
        }
        var fake = new ControlledSync();
        await using var app = App(fake);
        using var client = await ClientAsync(app);
        Assert.Null((await client.GetFromJsonAsync<SteamSyncCurrentJobResponse>("/api/steam/sync/jobs/current"))!.Job);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/api/steam/sync/jobs", null)).StatusCode);
        Assert.Equal(0, fake.Calls);
    }

    private WebApplicationFactory<Program> App(ControlledSync fake) => factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
    {
        services.RemoveAll<ISteamSyncService>();
        services.AddSingleton<ISteamSyncService>(fake);
    }));

    [Fact]
    public async Task Different_users_have_distinct_jobs_and_the_queue_is_fifo()
    {
        await LinkUsersAsync();
        var fake = new ControlledSync();
        await using var app = App(fake);
        using var owner = await ClientAsync(app);
        using var other = await ClientAsync(app, "HouseholdUserB");
        var firstResponse = await owner.PostAsync("/api/steam/sync/jobs", null);
        var first = (await firstResponse.Content.ReadFromJsonAsync<SteamSyncJobDto>())!;
        await fake.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var secondResponse = await other.PostAsync("/api/steam/sync/jobs", null);
        var second = (await secondResponse.Content.ReadFromJsonAsync<SteamSyncJobDto>())!;
        Assert.NotEqual(first.Id, second.Id);
        Assert.Equal("queued", second.State);
        Assert.Equal(1, fake.Calls);
        fake.Release.TrySetResult();
        await TerminalAsync(owner, first.Id);
        await TerminalAsync(other, second.Id);
        Assert.Equal(2, fake.Calls);
    }

    [Fact]
    public async Task A_failed_job_is_not_retried_unattended_but_a_new_explicit_request_can_succeed()
    {
        await LinkUsersAsync();
        var fake = new ControlledSync { Fail = true };
        await using var app = App(fake);
        using var client = await ClientAsync(app);
        var response = await client.PostAsync("/api/steam/sync/jobs", null);
        var first = (await response.Content.ReadFromJsonAsync<SteamSyncJobDto>())!;
        fake.Release.TrySetResult();
        Assert.Equal("failed", (await TerminalAsync(client, first.Id)).State);
        Assert.Equal(1, fake.Calls);
        fake.Fail = false;
        var retry = await client.PostAsync("/api/steam/sync/jobs", null);
        var next = (await retry.Content.ReadFromJsonAsync<SteamSyncJobDto>())!;
        Assert.NotEqual(first.Id, next.Id);
        Assert.Equal("completed", (await TerminalAsync(client, next.Id)).State);
        Assert.Equal(2, fake.Calls);
    }

    [Fact]
    public async Task Full_queue_rejects_new_work_instead_of_waiting_for_a_free_slot()
    {
        await LinkUsersAsync();
        var fake = new ControlledSync();
        await using var app = App(fake);
        using var client = await ClientAsync(app);
        await client.PostAsync("/api/steam/sync/jobs", null);
        await fake.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var userIds = new List<int>();
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<GamesDbContext>();
            for (var index = 0; index < 17; index++)
            {
                var user = new GamesDatabase.Api.Domain.Entities.User { Username = $"Queue user {index}", SteamId = $"765611981000000{index:D2}" };
                db.Users.Add(user);
                await db.SaveChangesAsync();
                userIds.Add(user.Id);
            }
        }
        var jobs = app.Services.GetRequiredService<ISteamSyncJobService>();
        foreach (var id in userIds.Take(16)) Assert.NotNull((await jobs.StartAsync(id)).Job);
        var full = await jobs.StartAsync(userIds[^1]).WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal("queueFull", full.Error);
        Assert.Null(full.Job);
        fake.Release.TrySetResult();
    }

    private async Task LinkUsersAsync()
    {
        await factory.EnsureSeededAsync();
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<GamesDbContext>();
        foreach (var user in await db.Users.Where(user => user.Username == "HouseholdUserA" || user.Username == "HouseholdUserB").ToListAsync())
            user.SteamId = user.Username == "HouseholdUserA" ? "76561198000000001" : "76561198000000002";
        await db.SaveChangesAsync();
    }

    private static int UserId(WebApplicationFactory<Program> app)
    {
        using var scope = app.Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<GamesDbContext>().Users.Single(user => user.Username == "HouseholdUserA").Id;
    }

    private async Task<HttpClient> ClientAsync(WebApplicationFactory<Program> app, string username = "HouseholdUserA")
    {
        var token = await factory.CreateWebTokenAsync(username);
        var client = app.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static async Task<SteamSyncJobDto> TerminalAsync(HttpClient client, Guid id)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (true)
        {
            var job = (await client.GetFromJsonAsync<SteamSyncJobDto>($"/api/steam/sync/jobs/{id}", deadline.Token))!;
            if (job.State is not ("queued" or "running")) return job;
            await Task.Delay(10, deadline.Token);
        }
    }

    private sealed class ControlledSync : ISteamSyncService
    {
        public int Calls;
        public bool Fail;
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<SteamSyncResult> SyncAllUserGamesAsync(int userId, Action<SteamSyncProgress>? progress = null, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref Calls);
            progress?.Invoke(new() { TotalGames = 3, ProcessedGames = 1, AchievementsUpdated = 2, CurrentGameName = "Test game" });
            Entered.TrySetResult();
            await Release.Task.WaitAsync(cancellationToken);
            if (Fail) throw new InvalidOperationException("PRIVATE_DETAIL");
            progress?.Invoke(new() { TotalGames = 3, ProcessedGames = 3, AchievementsUpdated = 6 });
            return new() { Success = true, TotalGames = 3, ProcessedGames = 3, AchievementsUpdated = 6 };
        }
        public Task<SteamSyncResult> SyncGameAsync(int userId, int gameId) => throw new NotSupportedException();
        public Task<SteamImportResult> ImportLibraryAsync(int userId, SteamImportRequest request) => throw new NotSupportedException();
        public Task<SteamImportedGameDto> AddStoreGameAsync(int userId, int appId, string? logoUrl = null, string? heroUrl = null, string? coverUrl = null) => throw new NotSupportedException();
    }
}
