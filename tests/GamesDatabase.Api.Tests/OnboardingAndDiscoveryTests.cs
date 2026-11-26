using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using GamesDatabase.Api.Application.Services;
using GamesDatabase.Api.Application.Interfaces;
using GamesDatabase.Api.Common;
using GamesDatabase.Api.Contracts;
using GamesDatabase.Api.Domain.Entities;
using GamesDatabase.Api.Infrastructure.Persistence;
using GamesDatabase.Api.Configuration;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GamesDatabase.Api.Tests;

public sealed class OnboardingAndDiscoveryTests(GamesDatabaseApiFactory factory) : IClassFixture<GamesDatabaseApiFactory>
{
    private sealed record Owner(HttpClient Client, int UserId, int Regular, int Special, int Original);
    private async Task<Owner> CreateOwner()
    {
        await factory.EnsureSeededAsync();
        var name = "Guide-" + Guid.NewGuid().ToString("N");
        int userId, regularId, specialId, originalId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<GamesDbContext>();
            var user = new User { Username = name, Role = UserRole.Standard }; db.Users.Add(user); await db.SaveChangesAsync(); userId = user.Id;
            var regular = new GameStatus { UserId = userId, Name = "Playing but regular", StatusType = SpecialStatusType.None };
            var special = new GameStatus { UserId = userId, Name = "Renamed active role", StatusType = SpecialStatusType.Playing, IsDefault = true };
            db.GameStatuses.AddRange(regular, special); await db.SaveChangesAsync(); regularId = regular.Id; specialId = special.Id;
            var original = new Game { UserId = userId, StatusId = regularId, Name = "Amber Trail", Comment = "My original note", ManualPlaytimeMinutes = 0 };
            db.Games.Add(original); await db.SaveChangesAsync(); originalId = original.Id;
        }
        var client = factory.CreateClient(); client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await factory.CreateWebTokenAsync(name));
        return new(client, userId, regularId, specialId, originalId);
    }
    private static ChangeTutorialSamplesRequest Review(OnboardingStateDto state, bool edited = false) => new()
    {
        Revision = state.Revision, Confirm = true, RemoveEditedSamples = edited,
        ExpectedGames = state.SampleGames.Select(game => new ExpectedSampleGameDto { Id = game.Id, UpdatedAt = game.UpdatedAt }).ToList(),
        ExpectedPlaylists = state.SampleLists.Select(list => new ExpectedSampleGameDto { Id = list.Id, UpdatedAt = list.UpdatedAt }).ToList()
    };
    private static async Task<OnboardingStateDto> Samples(Owner owner)
    {
        var current = (await owner.Client.GetFromJsonAsync<OnboardingStateDto>("/api/onboarding"))!;
        var response = await owner.Client.PostAsJsonAsync("/api/onboarding/samples", new CreateTutorialSamplesRequest { Revision = current.Revision, Confirm = true }); response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<OnboardingStateDto>())!;
    }
    private static async Task<HttpResponseMessage> Remove(Owner owner, ChangeTutorialSamplesRequest request) => await owner.Client.SendAsync(new HttpRequestMessage(HttpMethod.Delete, "/api/onboarding/samples") { Content = JsonContent.Create(request) });

    [Fact]
    public async Task New_routes_require_auth_and_users_cannot_enumerate_other_profiles()
    {
        await factory.EnsureSeededAsync(); var anonymous = factory.CreateClient();
        foreach (var path in new[] { "/api/onboarding", "/api/collection/random-game" }) Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(path)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsJsonAsync("/api/onboarding/samples", new { confirm = true })).StatusCode);
        var owner = await CreateOwner(); Assert.Equal(HttpStatusCode.Forbidden, (await owner.Client.GetAsync("/api/users")).StatusCode);
    }

    [Fact]
    public async Task Reads_and_guide_start_do_not_add_games_or_change_other_accounts()
    {
        var owner = await CreateOwner(); var other = await CreateOwner();
        var first = (await owner.Client.GetFromJsonAsync<OnboardingStateDto>("/api/onboarding"))!; Assert.False(first.Enabled); Assert.Empty(first.SampleGames);
        await using (var scope = factory.Services.CreateAsyncScope()) Assert.False(await scope.ServiceProvider.GetRequiredService<GamesDbContext>().OnboardingStates.AnyAsync(state => state.UserId == owner.UserId));
        (await owner.Client.PostAsJsonAsync("/api/onboarding/start", new { revision = 0, restart = false, userId = other.UserId })).EnsureSuccessStatusCode();
        Assert.True((await owner.Client.GetFromJsonAsync<OnboardingStateDto>("/api/onboarding"))!.Enabled);
        Assert.False((await other.Client.GetFromJsonAsync<OnboardingStateDto>("/api/onboarding"))!.Enabled);
        await using (var scope = factory.Services.CreateAsyncScope()) Assert.Equal(1, await scope.ServiceProvider.GetRequiredService<GamesDbContext>().Games.CountAsync(game => game.UserId == owner.UserId));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Administrator_user_creation_and_reactivation_honor_the_guide_checkbox_without_seeding_games(bool enabled)
    {
        var administrator = await CreateOwner();
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<GamesDbContext>(); var user = await db.Users.SingleAsync(user => user.Id == administrator.UserId); user.Role = UserRole.Admin; await db.SaveChangesAsync();
            administrator.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", scope.ServiceProvider.GetRequiredService<IAuthService>().GenerateToken(user));
        }
        var name = "Choice-" + Guid.NewGuid().ToString("N");
        var created = await administrator.Client.PostAsJsonAsync("/api/users", new CreateUserRequest { Username = name, TutorialEnabled = enabled }); created.EnsureSuccessStatusCode();
        var account = (await created.Content.ReadFromJsonAsync<UserDto>())!; Assert.Equal(enabled, account.TutorialEnabled);
        var client = factory.CreateClient(); client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await factory.CreateWebTokenAsync(name));
        var first = (await client.GetFromJsonAsync<OnboardingStateDto>("/api/onboarding"))!; Assert.Equal(enabled, first.Enabled); Assert.Empty(first.SampleGames); Assert.Equal(0, first.SamplePlaylists);
        var finished = await client.PutAsJsonAsync("/api/onboarding", new OnboardingProgressRequest { Revision = first.Revision, Step = "finish", Complete = true }); finished.EnsureSuccessStatusCode();
        var completed = (await finished.Content.ReadFromJsonAsync<OnboardingStateDto>())!; Assert.Equal("3.0.0", completed.CompletedVersion); Assert.False(completed.Enabled);
        (await administrator.Client.PutAsJsonAsync($"/api/users/{account.Id}", new UpdateUserRequest { TutorialEnabled = true })).EnsureSuccessStatusCode();
        var reactivated = (await client.GetFromJsonAsync<OnboardingStateDto>("/api/onboarding"))!; Assert.True(reactivated.Enabled); Assert.Equal("welcome", reactivated.Step); Assert.Null(reactivated.CompletedVersion); Assert.Equal(completed.Revision + 1, reactivated.Revision);
        Assert.Empty(reactivated.SampleGames); await using var after = factory.Services.CreateAsyncScope(); var context = after.ServiceProvider.GetRequiredService<GamesDbContext>();
        Assert.False(await context.Games.AnyAsync(game => game.UserId == account.Id)); Assert.False(await context.Playlists.AnyAsync(list => list.UserId == account.Id));
    }

    [Fact]
    public async Task Guide_progress_can_pause_resume_complete_and_restart_with_existing_data()
    {
        var owner = await CreateOwner();
        var saved = await owner.Client.PutAsJsonAsync("/api/onboarding", new OnboardingProgressRequest { Revision = 0, Step = "progress", Enabled = false }); saved.EnsureSuccessStatusCode();
        var paused = (await saved.Content.ReadFromJsonAsync<OnboardingStateDto>())!;
        Assert.False(paused.Enabled); Assert.Equal("progress", paused.Step);
        var response = await owner.Client.PostAsJsonAsync("/api/onboarding/start", new { revision = paused.Revision }); response.EnsureSuccessStatusCode();
        var resumed = (await response.Content.ReadFromJsonAsync<OnboardingStateDto>())!; Assert.Equal("progress", resumed.Step);
        (await owner.Client.PutAsJsonAsync("/api/onboarding", new OnboardingProgressRequest { Revision = resumed.Revision, Step = "finish", Complete = true })).EnsureSuccessStatusCode();
        var completed = (await owner.Client.GetFromJsonAsync<OnboardingStateDto>("/api/onboarding"))!; Assert.False(completed.Enabled); Assert.Equal("3.0.0", completed.CompletedVersion);
        var restarted = await owner.Client.PostAsJsonAsync("/api/onboarding/start", new { revision = completed.Revision, restart = true }); restarted.EnsureSuccessStatusCode();
        Assert.Equal("welcome", (await restarted.Content.ReadFromJsonAsync<OnboardingStateDto>())!.Step);
        Assert.Equal("My original note", (await owner.Client.GetFromJsonAsync<GameDto>($"/api/games/{owner.Original}"))!.Comment);
    }

    [Fact]
    public async Task Samples_need_confirmation_are_idempotent_and_never_link_Steam_or_overwrite_a_matching_title()
    {
        var owner = await CreateOwner(); Assert.Equal(HttpStatusCode.BadRequest, (await owner.Client.PostAsJsonAsync("/api/onboarding/samples", new { confirm = false })).StatusCode);
        var state = await Samples(owner); Assert.Equal(6, state.SampleGames.Count); Assert.Equal(1, state.SamplePlaylists); Assert.All(state.SampleGames, game => { Assert.True(game.IsSample); Assert.Null(game.SteamAppId); Assert.Equal(game.CreatedAt, game.UpdatedAt); });
        var retry = await owner.Client.PostAsJsonAsync("/api/onboarding/samples", new { revision = state.Revision, confirm = true }); retry.EnsureSuccessStatusCode();
        Assert.Equal(state.SampleGames.Select(game => game.Id), (await retry.Content.ReadFromJsonAsync<OnboardingStateDto>())!.SampleGames.Select(game => game.Id));
        Assert.Equal("My original note", (await owner.Client.GetFromJsonAsync<GameDto>($"/api/games/{owner.Original}"))!.Comment);
    }

    [Fact]
    public async Task Sample_removal_preserves_original_games_other_users_and_mixed_playlists()
    {
        var owner = await CreateOwner(); var other = await CreateOwner(); var state = await Samples(owner);
        (await owner.Client.PostAsJsonAsync($"/api/playlists/{state.SampleLists[0].Id}/items", new { gameId = owner.Original })).EnsureSuccessStatusCode();
        var reviewed = (await owner.Client.GetFromJsonAsync<OnboardingStateDto>("/api/onboarding"))!;
        var response = await Remove(owner, Review(reviewed)); response.EnsureSuccessStatusCode(); var result = (await response.Content.ReadFromJsonAsync<TutorialSampleChangeDto>())!;
        Assert.Equal(6, result.RemovedGames); Assert.Equal(1, result.KeptPlaylists); Assert.Empty(result.State.SampleGames); Assert.False(result.State.HasSamples);
        Assert.Equal("My original note", (await owner.Client.GetFromJsonAsync<GameDto>($"/api/games/{owner.Original}"))!.Comment);
        Assert.Equal("My original note", (await other.Client.GetFromJsonAsync<GameDto>($"/api/games/{other.Original}"))!.Comment);
        var list = (await owner.Client.GetFromJsonAsync<PlaylistDto>($"/api/playlists/{state.SampleLists[0].Id}"))!; Assert.False(list.IsSample); Assert.Equal(owner.Original, Assert.Single(list.Items).GameId); Assert.Equal(0, list.Items[0].Position);
    }

    [Fact]
    public async Task Default_removal_keeps_edited_examples_and_explicit_keep_adopts_without_deleting()
    {
        var owner = await CreateOwner(); var state = await Samples(owner); var edited = state.SampleGames[0];
        (await owner.Client.PutAsJsonAsync($"/api/games/{edited.Id}", new { comment = "My edited example" })).EnsureSuccessStatusCode();
        var fresh = (await owner.Client.GetFromJsonAsync<OnboardingStateDto>("/api/onboarding"))!;
        var response = await Remove(owner, Review(fresh)); response.EnsureSuccessStatusCode(); var changed = (await response.Content.ReadFromJsonAsync<TutorialSampleChangeDto>())!;
        Assert.Equal(5, changed.RemovedGames); Assert.Equal(1, changed.KeptGames); Assert.False((await owner.Client.GetFromJsonAsync<GameDto>($"/api/games/{edited.Id}"))!.IsSample);
        var newBatch = await Samples(owner); var keep = await owner.Client.PostAsJsonAsync("/api/onboarding/samples/keep", Review(newBatch)); keep.EnsureSuccessStatusCode();
        var kept = (await keep.Content.ReadFromJsonAsync<TutorialSampleChangeDto>())!; Assert.Equal(0, kept.RemovedGames); Assert.Equal(6, kept.KeptGames); Assert.False(kept.State.HasSamples);
    }

    [Fact]
    public async Task Concurrent_sample_and_playlist_edits_reject_a_stale_cleanup_before_any_deletion()
    {
        var owner = await CreateOwner(); var state = await Samples(owner);
        (await owner.Client.PutAsJsonAsync($"/api/games/{state.SampleGames[0].Id}", new { comment = "Concurrent edit" })).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Conflict, (await Remove(owner, Review(state, true))).StatusCode);
        var current = (await owner.Client.GetFromJsonAsync<OnboardingStateDto>("/api/onboarding"))!; Assert.Equal(6, current.SampleGames.Count);
        (await owner.Client.PutAsJsonAsync($"/api/playlists/{current.SampleLists[0].Id}", new { description = "Concurrent playlist edit" })).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Conflict, (await Remove(owner, Review(current, true))).StatusCode);
        Assert.Equal(6, (await owner.Client.GetFromJsonAsync<OnboardingStateDto>("/api/onboarding"))!.SampleGames.Count);
    }

    [Fact]
    public async Task Malformed_progress_and_foreign_sample_ids_fail_closed()
    {
        var owner = await CreateOwner(); var other = await CreateOwner(); var state = await Samples(owner);
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.Client.PutAsJsonAsync("/api/onboarding", new { revision = state.Revision, step = "sql" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.Client.PutAsJsonAsync("/api/onboarding", new { revision = state.Revision, step = "library", complete = true })).StatusCode);
        var wrong = Review(state); wrong.ExpectedGames[0].Id = other.Original;
        Assert.Equal(HttpStatusCode.Conflict, (await Remove(owner, wrong)).StatusCode);
        Assert.Equal(6, (await owner.Client.GetFromJsonAsync<OnboardingStateDto>("/api/onboarding"))!.SampleGames.Count);
    }

    [Fact]
    public async Task Random_reads_use_only_the_full_owned_library_and_never_mutate_a_game()
    {
        var owner = await CreateOwner(); var other = await CreateOwner(); var samples = await Samples(owner);
        var allowed = samples.SampleGames.Select(game => game.Id).Append(owner.Original).ToHashSet();
        for (var index = 0; index < 20; index++) { var game = (await owner.Client.GetFromJsonAsync<GameDto>("/api/collection/random-game"))!; Assert.Contains(game.Id, allowed); Assert.NotEqual(other.Original, game.Id); }
        Assert.Equal("My original note", (await owner.Client.GetFromJsonAsync<GameDto>($"/api/games/{owner.Original}"))!.Comment);
    }

    [Fact]
    public async Task Daily_default_filters_use_semantic_roles_and_explicit_exclusions_win()
    {
        var owner = await CreateOwner(); var other = await CreateOwner(); var samples = await Samples(owner);
        var response = await owner.Client.PostAsJsonAsync("/api/collection/recommendations/preview", new RecommendationFiltersDto()); response.EnsureSuccessStatusCode();
        var result = (await response.Content.ReadFromJsonAsync<DailyRecommendationsDto>())!; Assert.DoesNotContain(result.Items, game => game.StatusId == owner.Special); Assert.Contains(result.Items, game => game.Id == owner.Original);
        var again = await owner.Client.PostAsJsonAsync("/api/collection/recommendations/preview", new RecommendationFiltersDto()); again.EnsureSuccessStatusCode(); Assert.Equal(result.Items.Select(game => game.Id), (await again.Content.ReadFromJsonAsync<DailyRecommendationsDto>())!.Items.Select(game => game.Id));
        var exclude = await owner.Client.PostAsJsonAsync("/api/collection/recommendations/preview", new RecommendationFiltersDto { IncludedStatusIds = [owner.Special], ExcludedStatusIds = [owner.Special] }); exclude.EnsureSuccessStatusCode(); Assert.Empty((await exclude.Content.ReadFromJsonAsync<DailyRecommendationsDto>())!.Items);
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.Client.PostAsJsonAsync("/api/collection/recommendations/preview", new RecommendationFiltersDto { IncludedStatusIds = [other.Regular] })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.Client.PostAsJsonAsync("/api/collection/recommendations/preview?count=25", new RecommendationFiltersDto())).StatusCode);
    }

    [Fact]
    public async Task The_daily_order_is_stable_and_rotates_at_the_shared_server_day()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        await using var db = new GamesDbContext(new DbContextOptionsBuilder<GamesDbContext>().UseSqlite(connection).Options); await db.Database.EnsureCreatedAsync();
        var user = new User { Username = "Clock owner" }; db.Users.Add(user); await db.SaveChangesAsync();
        var status = new GameStatus { UserId = user.Id, Name = "Backlog" }; db.GameStatuses.Add(status); await db.SaveChangesAsync();
        db.Games.AddRange(Enumerable.Range(1, 12).Select(index => new Game { UserId = user.Id, StatusId = status.Id, Name = "Clock game " + index })); await db.SaveChangesAsync();
        var clock = new MutableClock { Value = new DateTimeOffset(2026, 11, 25, 23, 59, 59, TimeSpan.Zero) };
        var service = new GameDiscoveryService(db, clock); var first = await service.DailyAsync(user.Id, new(), 24);
        Assert.Equal(first.Items.Select(game => game.Id), (await service.DailyAsync(user.Id, new(), 24)).Items.Select(game => game.Id));
        Assert.Equal("2026-11-25", first.Day); Assert.Equal(new DateTimeOffset(2026, 11, 26, 0, 0, 0, TimeSpan.Zero), first.RefreshAt);
        clock.Value = first.RefreshAt;
        var tomorrow = await service.DailyAsync(user.Id, new(), 24); Assert.Equal("2026-11-26", tomorrow.Day); Assert.NotEqual(first.Items[0].Id, tomorrow.Items[0].Id);
        Assert.Equal(first.Items.Select(game => game.Id).Order(), tomorrow.Items.Select(game => game.Id).Order());
        Assert.Empty((await service.DailyAsync(user.Id, new() { ExcludedStatusIds = [status.Id] }, 24)).Items);
    }
    private sealed class MutableClock : TimeProvider { public DateTimeOffset Value { get; set; } public override DateTimeOffset GetUtcNow() => Value; }
}

public sealed class FreshDatabaseBootstrappingTests
{
    [Fact]
    public void Absent_environment_overrides_do_not_erase_typed_option_defaults()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.Configuration.Sources.Clear(); builder.Configuration.AddInMemoryCollection();
        builder.ApplyEnvironmentOverrides();
        var options = builder.Configuration.GetSection(HouseholdIntegrationOptions.SectionName).Get<HouseholdIntegrationOptions>() ?? new();
        Assert.Equal(15, options.AccessTokenMinutes); Assert.Equal(30, options.RefreshTokenDays); Assert.Equal(5, options.AuthorizationCodeMinutes);
    }

    [Fact]
    public async Task An_empty_database_bootstraps_current_schema_once_without_skipping_existing_migrations()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        await using var db = new GamesDbContext(new DbContextOptionsBuilder<GamesDbContext>().UseSqlite(connection).Options);
        Assert.True(await DatabaseInitialization.InitializeAsync(db));
        Assert.Equal(db.Database.GetMigrations().Order(), (await db.Database.GetAppliedMigrationsAsync()).Order());
        db.Users.Add(new User { Username = "Existing", TutorialEnabled = false }); await db.SaveChangesAsync();
        Assert.False(await DatabaseInitialization.InitializeAsync(db));
        Assert.False((await db.Users.SingleAsync()).TutorialEnabled); Assert.Empty(await db.OnboardingStates.ToListAsync());
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
    }
}
