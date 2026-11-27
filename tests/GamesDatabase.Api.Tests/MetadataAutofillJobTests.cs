using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using GamesDatabase.Api.Application.Interfaces;
using GamesDatabase.Api.Application.Services;
using GamesDatabase.Api.Contracts;
using GamesDatabase.Api.Domain.Entities;
using GamesDatabase.Api.Infrastructure.Persistence;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace GamesDatabase.Api.Tests;

public sealed class MetadataAutofillJobTests(GamesDatabaseApiFactory factory) : IClassFixture<GamesDatabaseApiFactory>, IAsyncLifetime
{
    private const string Route = "/api/metadata-autofill/jobs";
    private readonly List<string> databasePaths = [];
    public Task InitializeAsync() => Task.CompletedTask;
    public Task DisposeAsync()
    {
        foreach (var path in databasePaths)
            foreach (var suffix in new[] { "", "-wal", "-shm" })
                try { File.Delete(path + suffix); } catch (IOException) { /* Exact synthetic fixture only; Windows pools may release later. */ }
        return Task.CompletedTask;
    }
    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => Now;
    }
    private sealed class Sources : IPublicGameMetadataProvider
    {
        public bool Ambiguous { get; set; }
        public Task<List<GameMetadataIdentityDto>> SearchSteamAsync(string name, CancellationToken token) => Task.FromResult(new List<GameMetadataIdentityDto>
        {
            new() { Source = "steam", Name = name, SteamAppId = 100, Url = "https://store.steampowered.com/app/100/" }
        });
        public Task<PublicGameMetadata?> SteamAsync(int id, bool artwork, CancellationToken token) => Task.FromResult<PublicGameMetadata?>(new()
        {
            Identity = new() { Source = "steam", Name = "Verified fixture", SteamAppId = id, Url = $"https://store.steampowered.com/app/{id}/" },
            Fields = new() { ["name"] = "Verified fixture", ["developer"] = "Verified studio", ["released"] = "2025-01-01" }
        });
        public Task<List<PublicGameMetadata>> WikidataAsync(string name, int? id, CancellationToken token) => Task.FromResult(new List<PublicGameMetadata>());
    }
    private sealed class Durations : IGameDurationProvider
    {
        public int Reads { get; private set; }
        public bool Unavailable { get; set; }
        public bool Ambiguous { get; set; }
        public bool NoData { get; set; }
        public bool UnexpectedFailure { get; set; }
        public TaskCompletionSource? Entered { get; set; }
        public TaskCompletionSource? Continue { get; set; }
        public async Task<GameDurationLookup> LookupAsync(string name, string? selectedSlug, CancellationToken token, bool forceRefresh = false)
        {
            Reads++; Entered?.TrySetResult();
            if (UnexpectedFailure) throw new InvalidOperationException("Synthetic unexpected provider result");
            if (Continue is not null) await Continue.Task.WaitAsync(token);
            if (Unavailable) return new() { Unavailable = true, RetryAfterSeconds = 120 };
            var identity = new GameMetadataIdentityDto { Source = "dekudeals", Name = name, DekuDealsSlug = "verified-fixture", Url = "https://www.dekudeals.com/items/verified-fixture" };
            if (Ambiguous) return new() { Matches = [identity] };
            if (NoData) return new() { Matches = [identity], IdentityConfirmed = true };
            return new()
            {
                IdentityConfirmed = true, Matches = [identity], Metadata = new()
                {
                    Identity = identity, Fields = new() { ["story"] = 24, ["completion"] = 50, ["critic"] = 92 },
                    SourceHours = new() { ["story"] = 23.5m, ["completion"] = 50m }, CriticProvider = "Metacritic",
                    CriticScope = "listed-platform", CriticUrl = "https://www.metacritic.com/game/verified-fixture/"
                }
            };
        }
    }
    private sealed record Owner(HttpClient Client, int UserId, List<int> Ids);
    private static async Task<Owner> Seed(IServiceProvider services, HttpClient client, int count = 2, bool complete = false)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<GamesDbContext>();
        var user = new User { Username = "JobFixture-" + Guid.NewGuid().ToString("N") }; db.Users.Add(user); await db.SaveChangesAsync();
        var status = new GameStatus { UserId = user.Id, Name = "Fixture backlog" }; db.GameStatuses.Add(status); await db.SaveChangesAsync();
        var games = Enumerable.Range(0, count).Select(_ => new Game
        {
            UserId = user.Id, StatusId = status.Id, Name = "Verified fixture", Comment = "Retain personal fixture note", Favorite = true, Grade = 12,
            Critic = complete ? 92 : null, CriticProvider = complete ? "Metacritic" : null, Story = complete ? 24 : null,
            Completion = complete ? 50 : null, Developer = complete ? "Verified studio" : null, Released = complete ? "2025-01-01" : null
        }).ToList();
        db.Games.AddRange(games); await db.SaveChangesAsync();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", scope.ServiceProvider.GetRequiredService<IAuthService>().GenerateToken(user));
        return new(client, user.Id, games.Select(game => game.Id).ToList());
    }
    private Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> Host(Durations durations, Clock? clock = null, string? existingPath = null, bool hosted = false)
    {
        // A worker processes the global queue. Give each test its OWN database,
        // rather than accidentally consuming another test's still-pending job.
        var path = existingPath ?? Path.Combine(Path.GetTempPath(), $"gdb-metadata-job-fixture-{Guid.NewGuid():N}.db");
        if (!databasePaths.Contains(path)) databasePaths.Add(path);
        return factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<DbContextOptions<GamesDbContext>>(); services.RemoveAll<GamesDbContext>();
            services.AddDbContext<GamesDbContext>(options => options.UseSqlite($"Data Source={path}"));
            services.RemoveAll<IPublicGameMetadataProvider>(); services.AddSingleton<IPublicGameMetadataProvider>(new Sources());
            services.RemoveAll<IGameDurationProvider>(); services.AddSingleton<IGameDurationProvider>(durations);
            if (clock is not null) { services.RemoveAll<TimeProvider>(); services.AddSingleton<TimeProvider>(clock); }
            if (hosted) services.AddHostedService<MetadataAutofillWorker>();
        }));
    }
    private static async Task<MetadataAutofillJobDto> Start(Owner owner, bool overwrite = true, bool accept = true, Guid? requestId = null)
    {
        var response = await owner.Client.PostAsJsonAsync(Route, new MetadataAutofillStartRequest { ClientRequestId = requestId ?? Guid.NewGuid(), GameIds = owner.Ids, Overwrite = overwrite, AcceptListedPlatformCritic = accept });
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<MetadataAutofillJobDto>())!;
    }
    private static async Task<bool> Step(IServiceProvider services, string worker = "fixture-worker")
    {
        await using var scope = services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IMetadataAutofillJobService>().ProcessNextAsync(worker);
    }
    private static async Task<MetadataAutofillJobDto> Read(Owner owner, Guid id) => (await owner.Client.GetFromJsonAsync<MetadataAutofillJobDto>($"{Route}/{id}"))!;

    [Fact]
    public async Task Durable_job_survives_disconnected_client_and_processes_frozen_145_ID_scope_without_personal_writes()
    {
        var source = new Durations(); using var host = Host(source);
        var owner = await Seed(host.Services, host.CreateClient(), 145); var job = await Start(owner);
        Assert.Equal(145, job.TotalGames); Assert.Equal(0, source.Reads);
        var authorization = owner.Client.DefaultRequestHeaders.Authorization; owner.Client.Dispose();
        var resumedClient = host.CreateClient(); resumedClient.DefaultRequestHeaders.Authorization = authorization;
        owner = owner with { Client = resumedClient };
        for (var index = 0; index < 145; index++) Assert.True(await Step(host.Services));
        var result = await Read(owner, job.Id);
        Assert.Equal("completed", result.State); Assert.False(result.Active); Assert.Equal(145, result.ProcessedGames);
        Assert.Equal(145, result.UpdatedGames); Assert.Equal(0, result.ReviewGames); Assert.False(await Step(host.Services));
        await using var scope = host.Services.CreateAsyncScope();
        var games = await scope.ServiceProvider.GetRequiredService<GamesDbContext>().Games.Where(game => owner.Ids.Contains(game.Id)).ToListAsync();
        Assert.All(games, game => { Assert.Equal(24, game.Story); Assert.Equal(50, game.Completion); Assert.Equal(92, game.Critic); Assert.Equal("Metacritic", game.CriticProvider); Assert.Equal("Retain personal fixture note", game.Comment); Assert.Equal(12, game.Grade); Assert.True(game.Favorite); Assert.Null(game.SteamAppId); });
    }

    [Fact]
    public async Task Start_is_owned_idempotent_and_rejects_changed_payload_or_a_second_active_scope()
    {
        using var host = Host(new()); var owner = await Seed(host.Services, host.CreateClient()); var key = Guid.NewGuid();
        var first = await Start(owner, requestId: key); var second = await Start(owner, requestId: key); Assert.Equal(first.Id, second.Id);
        Assert.Equal(HttpStatusCode.Conflict, (await owner.Client.PostAsJsonAsync(Route, new MetadataAutofillStartRequest { ClientRequestId = key, GameIds = owner.Ids, Overwrite = false })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await owner.Client.PostAsJsonAsync(Route, new MetadataAutofillStartRequest { ClientRequestId = Guid.NewGuid(), GameIds = owner.Ids })).StatusCode);
        var foreign = await Seed(host.Services, host.CreateClient());
        Assert.Equal(HttpStatusCode.NotFound, (await foreign.Client.GetAsync($"{Route}/{first.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await foreign.Client.PostAsync($"{Route}/{first.Id}/cancel", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await foreign.Client.PostAsync($"{Route}/{first.Id}/retry", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await foreign.Client.PostAsync($"{Route}/{first.Id}/dismiss", null)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await foreign.Client.PostAsJsonAsync(Route, new MetadataAutofillStartRequest { ClientRequestId = Guid.NewGuid(), GameIds = [owner.Ids[0], foreign.Ids[0]] })).StatusCode);
        var anonymous = host.CreateClient(); Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(Route + "/current")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsJsonAsync(Route, new MetadataAutofillStartRequest { ClientRequestId = Guid.NewGuid(), GameIds = owner.Ids })).StatusCode);
    }

    [Theory]
    [InlineData("empty")]
    [InlineData("duplicate")]
    [InlineData("negative")]
    [InlineData("over-limit")]
    [InlineData("empty-key")]
    public async Task Invalid_scope_never_creates_work(string invalid)
    {
        using var host = Host(new()); var owner = await Seed(host.Services, host.CreateClient());
        var ids = invalid switch { "empty" => new List<int>(), "duplicate" => [owner.Ids[0], owner.Ids[0]], "negative" => [-1], "over-limit" => Enumerable.Range(1, 1001).ToList(), _ => owner.Ids };
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.Client.PostAsJsonAsync(Route, new MetadataAutofillStartRequest { ClientRequestId = invalid == "empty-key" ? Guid.Empty : Guid.NewGuid(), GameIds = ids })).StatusCode);
        Assert.Null(await owner.Client.GetFromJsonAsync<MetadataAutofillJobDto>(Route + "/current"));
    }

    [Fact]
    public async Task Unchanged_listed_platform_rating_never_forces_review_or_forges_timestamp_history()
    {
        using var host = Host(new()); var owner = await Seed(host.Services, host.CreateClient(), 1, complete: true);
        var before = (await owner.Client.GetFromJsonAsync<GameDto>($"/api/games/{owner.Ids[0]}"))!;
        var job = await Start(owner, accept: false); await Step(host.Services);
        var result = await Read(owner, job.Id); Assert.Equal(0, result.ReviewGames); Assert.Equal("unchanged", Assert.Single(result.Items).Status);
        var after = (await owner.Client.GetFromJsonAsync<GameDto>($"/api/games/{owner.Ids[0]}"))!; Assert.Equal(before.UpdatedAt, after.UpdatedAt);
        await using var scope = host.Services.CreateAsyncScope(); Assert.Empty(await scope.ServiceProvider.GetRequiredService<GamesDbContext>().GameHistoryEntries.Where(entry => entry.GameId == before.Id).ToListAsync());
    }

    [Fact]
    public async Task One_batch_critic_choice_preserves_attribution_and_only_real_ambiguity_needs_review()
    {
        var source = new Durations(); using var host = Host(source); var owner = await Seed(host.Services, host.CreateClient(), 1);
        var job = await Start(owner, accept: false); await Step(host.Services);
        var result = await Read(owner, job.Id); var item = Assert.Single(result.Items);
        Assert.Equal("review", item.Status); Assert.Contains("story", item.AppliedFields); Assert.DoesNotContain("critic", item.AppliedFields); Assert.Contains("criticPlatformScope", item.Notices);
        source.Ambiguous = true; var other = await Seed(host.Services, host.CreateClient(), 1); var ambiguous = await Start(other); await Step(host.Services);
        Assert.Contains("chooseDurationIdentity", Assert.Single((await Read(other, ambiguous.Id)).Items).Notices);
    }

    [Fact]
    public async Task A_confirmed_edition_with_no_duration_data_is_not_an_identity_review()
    {
        using var host = Host(new() { NoData = true }); var owner = await Seed(host.Services, host.CreateClient(), 1); var job = await Start(owner); await Step(host.Services);
        var result = await Read(owner, job.Id); Assert.Equal(0, result.ReviewGames); Assert.Contains("durationUnavailable", result.Items[0].Notices); Assert.DoesNotContain("chooseDurationIdentity", result.Items[0].Notices);
    }

    [Fact]
    public async Task Cancellation_keeps_committed_results_and_never_touches_remaining_games()
    {
        using var host = Host(new()); var owner = await Seed(host.Services, host.CreateClient(), 3); var job = await Start(owner); await Step(host.Services);
        (await owner.Client.PostAsync($"{Route}/{job.Id}/cancel", null)).EnsureSuccessStatusCode();
        var result = await Read(owner, job.Id); Assert.Equal("cancelled", result.State); Assert.Equal(1, result.UpdatedGames); Assert.Equal(2, result.CancelledGames); Assert.False(await Step(host.Services));
        Assert.Null((await owner.Client.GetFromJsonAsync<GameDto>($"/api/games/{owner.Ids[1]}"))!.Story);
    }

    [Fact]
    public async Task Cancellation_during_lookup_blocks_the_not_yet_started_save()
    {
        var source = new Durations { Entered = new(TaskCreationOptions.RunContinuationsAsynchronously), Continue = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        using var host = Host(source); var owner = await Seed(host.Services, host.CreateClient(), 1); var job = await Start(owner);
        var work = Step(host.Services); await source.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        (await owner.Client.PostAsync($"{Route}/{job.Id}/cancel", null)).EnsureSuccessStatusCode();
        source.Continue.TrySetResult(); await work;
        Assert.Equal("cancelled", (await Read(owner, job.Id)).State); Assert.Null((await owner.Client.GetFromJsonAsync<GameDto>($"/api/games/{owner.Ids[0]}"))!.Story);
    }

    [Fact]
    public async Task Concurrent_user_edit_is_preserved_instead_of_silently_overwritten_by_the_queue()
    {
        using var host = Host(new()); var owner = await Seed(host.Services, host.CreateClient(), 1); var job = await Start(owner);
        (await owner.Client.PutAsJsonAsync($"/api/games/{owner.Ids[0]}", new { comment = "New explicit user edit" })).EnsureSuccessStatusCode();
        await Step(host.Services); var result = await Read(owner, job.Id); Assert.Contains("gameChanged", result.Items[0].Notices); Assert.Equal(1, result.ReviewGames);
        var game = (await owner.Client.GetFromJsonAsync<GameDto>($"/api/games/{owner.Ids[0]}"))!; Assert.Equal("New explicit user edit", game.Comment); Assert.Null(game.Story);
    }

    [Fact]
    public async Task Cooldown_is_persisted_and_does_not_burn_through_the_rest_of_the_batch()
    {
        var clock = new Clock(); var source = new Durations { Unavailable = true }; using var host = Host(source, clock);
        var owner = await Seed(host.Services, host.CreateClient(), 3); var job = await Start(owner); await Step(host.Services);
        var waiting = await Read(owner, job.Id); Assert.Equal("waiting", waiting.State); Assert.NotNull(waiting.NextAttemptAt); Assert.Equal(0, waiting.ProcessedGames); Assert.Equal(1, source.Reads);
        Assert.False(await Step(host.Services, "new-worker-after-restart")); Assert.Equal(1, source.Reads);
        clock.Now += TimeSpan.FromSeconds(121); source.Unavailable = false; await Step(host.Services, "new-worker-after-restart");
        var resumed = await Read(owner, job.Id); Assert.Equal(1, resumed.UpdatedGames); Assert.Equal(0, resumed.FailedGames);
    }

    [Fact]
    public async Task Expired_process_lease_resumes_but_a_live_other_worker_lease_is_never_stolen()
    {
        var clock = new Clock(); using var host = Host(new(), clock); var owner = await Seed(host.Services, host.CreateClient(), 1); var job = await Start(owner);
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<GamesDbContext>(); var stored = await db.MetadataAutofillJobs.SingleAsync(value => value.Id == job.Id);
            stored.LeaseOwner = "previous-process"; stored.LeaseUntil = clock.Now.UtcDateTime.AddMinutes(2); await db.SaveChangesAsync();
        }
        Assert.False(await Step(host.Services, "replacement-process"));
        clock.Now += TimeSpan.FromMinutes(3); Assert.True(await Step(host.Services, "replacement-process")); Assert.Equal("completed", (await Read(owner, job.Id)).State);
    }

    [Fact]
    public async Task Failed_checkpoint_rolls_back_the_game_and_recovery_cannot_duplicate_a_committed_save()
    {
        var clock = new Clock(); using var host = Host(new(), clock); var owner = await Seed(host.Services, host.CreateClient(), 1); var job = await Start(owner);
        await using (var scope = host.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<GamesDbContext>().Database.ExecuteSqlRawAsync("CREATE TRIGGER fixture_fail_checkpoint BEFORE UPDATE OF items_json ON metadata_autofill_job WHEN instr(NEW.items_json, '\"status\":\"updated\"') > 0 BEGIN SELECT RAISE(ABORT, 'fixture checkpoint fault'); END;");
        }
        await Step(host.Services); Assert.Null((await owner.Client.GetFromJsonAsync<GameDto>($"/api/games/{owner.Ids[0]}"))!.Story);
        await using (var scope = host.Services.CreateAsyncScope()) await scope.ServiceProvider.GetRequiredService<GamesDbContext>().Database.ExecuteSqlRawAsync("DROP TRIGGER fixture_fail_checkpoint;");
        clock.Now += TimeSpan.FromMinutes(3); await Step(host.Services, "recovered-process");
        var finished = await Read(owner, job.Id); Assert.Equal(1, finished.UpdatedGames); var game = (await owner.Client.GetFromJsonAsync<GameDto>($"/api/games/{owner.Ids[0]}"))!;
        Assert.False(await Step(host.Services, "another-process")); Assert.Equal(game.UpdatedAt, (await owner.Client.GetFromJsonAsync<GameDto>($"/api/games/{owner.Ids[0]}"))!.UpdatedAt);
    }

    [Fact]
    public async Task Results_remain_available_until_explicitly_dismissed_and_summary_can_omit_items()
    {
        using var host = Host(new()); var owner = await Seed(host.Services, host.CreateClient(), 1); var job = await Start(owner);
        await Step(host.Services);
        var summary = (await owner.Client.GetFromJsonAsync<MetadataAutofillJobDto>(Route + "/current?includeItems=false"))!; Assert.Empty(summary.Items); Assert.Equal(1, summary.TotalGames);
        (await owner.Client.PostAsync($"{Route}/{job.Id}/dismiss", null)).EnsureSuccessStatusCode();
        Assert.Null(await owner.Client.GetFromJsonAsync<MetadataAutofillJobDto>(Route + "/current"));
        Assert.Single((await Read(owner, job.Id)).Items);
    }

    [Fact]
    public async Task Hiding_active_work_does_not_cancel_it_and_review_results_stay_recoverable_without_resurrecting_old_banners()
    {
        using var host = Host(new() { Ambiguous = true }); var owner = await Seed(host.Services, host.CreateClient(), 1);
        var old = await Start(owner); await Step(host.Services); Assert.Equal(1, (await Read(owner, old.Id)).ReviewGames);
        var job = await Start(owner);
        (await owner.Client.PostAsync($"{Route}/{job.Id}/dismiss", null)).EnsureSuccessStatusCode();
        var hidden = await Read(owner, job.Id); Assert.True(hidden.Active); Assert.True(hidden.Dismissed); Assert.NotEqual("cancelled", hidden.State);
        await Step(host.Services); var complete = await Read(owner, job.Id);
        Assert.False(complete.Active); Assert.True(complete.Dismissed); Assert.Equal(1, complete.ReviewGames); Assert.Single(complete.Items);
        Assert.Null(await owner.Client.GetFromJsonAsync<MetadataAutofillJobDto>(Route + "/current"));
        var history = (await owner.Client.GetFromJsonAsync<List<MetadataAutofillJobDto>>(Route))!;
        Assert.Contains(history, value => value.Id == old.Id); Assert.Contains(history, value => value.Id == job.Id && value.Dismissed);
    }

    [Fact]
    public async Task Real_hosted_worker_keeps_running_after_the_starting_http_client_closes()
    {
        var durations = new Durations { Entered = new(TaskCreationOptions.RunContinuationsAsynchronously), Continue = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        using var host = Host(durations, hosted: true);
        var owner = await Seed(host.Services, host.CreateClient(), 1); var job = await Start(owner);
        await durations.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var authorization = owner.Client.DefaultRequestHeaders.Authorization; owner.Client.Dispose();
        durations.Continue.TrySetResult();
        var observer = host.CreateClient(); observer.DefaultRequestHeaders.Authorization = authorization;
        owner = owner with { Client = observer };
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        MetadataAutofillJobDto status;
        do { status = await Read(owner, job.Id); if (status.Active) await Task.Delay(50, deadline.Token); } while (status.Active);
        Assert.Equal(1, status.UpdatedGames); Assert.Equal(24, (await observer.GetFromJsonAsync<GameDto>($"/api/games/{owner.Ids[0]}"))!.Story);
    }

    [Fact]
    public async Task A_new_application_host_recovers_the_same_sqlite_job_and_does_not_repeat_committed_items()
    {
        var path = Path.Combine(Path.GetTempPath(), $"gdb-metadata-job-restart-{Guid.NewGuid():N}.db");
        var durations = new Durations(); Guid jobId; Owner owner; AuthenticationHeaderValue? authorization;
        using (var first = Host(durations, existingPath: path))
        {
            owner = await Seed(first.Services, first.CreateClient(), 3); var job = await Start(owner); jobId = job.Id;
            await Step(first.Services); Assert.Equal(1, (await Read(owner, job.Id)).UpdatedGames);
            authorization = owner.Client.DefaultRequestHeaders.Authorization;
        }
        using var restarted = Host(durations, existingPath: path, hosted: true);
        var newClient = restarted.CreateClient(); newClient.DefaultRequestHeaders.Authorization = authorization; owner = owner with { Client = newClient };
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        MetadataAutofillJobDto resumed;
        do { resumed = await Read(owner, jobId); if (resumed.Active) await Task.Delay(50, deadline.Token); } while (resumed.Active);
        Assert.Equal(jobId, resumed.Id); Assert.Equal(3, resumed.UpdatedGames); Assert.Equal(3, durations.Reads);
        Assert.Equal(3, (await newClient.GetFromJsonAsync<List<MetadataAutofillJobDto>>(Route))!.Single().TotalGames);
    }

    [Fact]
    public async Task A_single_explicit_platform_acceptance_resolves_all_critic_only_reviews_without_redoing_other_fields()
    {
        using var host = Host(new()); var owner = await Seed(host.Services, host.CreateClient(), 3); var job = await Start(owner, accept: false);
        for (var index = 0; index < 3; index++) await Step(host.Services);
        Assert.Equal(3, (await Read(owner, job.Id)).ReviewGames);
        var before = (await owner.Client.GetFromJsonAsync<GameDto>($"/api/games/{owner.Ids[0]}"))!;
        (await owner.Client.PostAsync($"{Route}/{job.Id}/accept-listed-critics", null)).EnsureSuccessStatusCode();
        for (var index = 0; index < 3; index++) await Step(host.Services);
        var result = await Read(owner, job.Id); Assert.Equal(0, result.ReviewGames); Assert.Equal(3, result.UpdatedGames); Assert.True(result.AcceptListedPlatformCritic);
        var after = (await owner.Client.GetFromJsonAsync<GameDto>($"/api/games/{owner.Ids[0]}"))!;
        Assert.Equal(92, after.Critic); Assert.Equal(before.Story, after.Story); Assert.Equal(before.Completion, after.Completion); Assert.Equal(before.Comment, after.Comment);
    }

    [Fact]
    public async Task Two_manual_job_reviews_are_independent_and_a_forged_or_other_owner_plan_cannot_resolve_either()
    {
        var source = new Durations { Ambiguous = true }; using var host = Host(source); var owner = await Seed(host.Services, host.CreateClient(), 2); var job = await Start(owner);
        await Step(host.Services); await Step(host.Services); Assert.Equal(2, (await Read(owner, job.Id)).ReviewGames);
        var other = await Seed(host.Services, host.CreateClient(), 1);
        Assert.Equal(HttpStatusCode.NotFound, (await other.Client.PostAsJsonAsync($"{Route}/{job.Id}/items/{owner.Ids[0]}/review", new GameMetadataApplyRequest { Token = "forged", Fields = ["story"] })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.Client.PostAsJsonAsync($"{Route}/{job.Id}/items/{owner.Ids[0]}/review", new GameMetadataApplyRequest { Token = "forged", Fields = ["story"] })).StatusCode);
        source.Ambiguous = false;
        foreach (var id in owner.Ids)
        {
            var previewResponse = await owner.Client.PostAsJsonAsync($"/api/games/{id}/metadata/preview", new GameMetadataLookupRequest { ReviewVersion = 2, Overwrite = true }); previewResponse.EnsureSuccessStatusCode();
            var preview = (await previewResponse.Content.ReadFromJsonAsync<GameMetadataPreviewDto>())!;
            (await owner.Client.PostAsJsonAsync($"{Route}/{job.Id}/items/{id}/review", new GameMetadataApplyRequest { Token = preview.Token!, Fields = ["story", "completion", "critic"] })).EnsureSuccessStatusCode();
            Assert.Equal(owner.Ids.Count - owner.Ids.IndexOf(id) - 1, (await Read(owner, job.Id)).ReviewGames);
        }
        Assert.All((await Read(owner, job.Id)).Items, item => Assert.Equal("updated", item.Status));
    }

    [Fact]
    public async Task Previous_results_and_choices_are_recoverable_from_history_after_another_batch_starts()
    {
        using var host = Host(new() { Ambiguous = true }); var owner = await Seed(host.Services, host.CreateClient(), 1); var first = await Start(owner); await Step(host.Services);
        var second = await Start(owner); Assert.NotEqual(first.Id, second.Id);
        var history = (await owner.Client.GetFromJsonAsync<List<MetadataAutofillJobDto>>(Route))!;
        Assert.Contains(history, item => item.Id == first.Id && item.ReviewGames == 1); Assert.Contains(history, item => item.Id == second.Id);
        Assert.Equal(1, (await Read(owner, first.Id)).ReviewGames);
    }

    [Fact]
    public async Task An_unexpected_source_failure_is_a_retryable_item_not_an_endless_global_queue_loop()
    {
        var source = new Durations { UnexpectedFailure = true }; using var host = Host(source);
        var owner = await Seed(host.Services, host.CreateClient(), 1); var job = await Start(owner); await Step(host.Services);
        var failed = await Read(owner, job.Id); Assert.False(failed.Active); Assert.Equal(1, failed.FailedGames); Assert.False(await Step(host.Services));
        source.UnexpectedFailure = false; (await owner.Client.PostAsync($"{Route}/{job.Id}/retry", null)).EnsureSuccessStatusCode(); await Step(host.Services);
        Assert.Equal(1, (await Read(owner, job.Id)).UpdatedGames);
    }
}
