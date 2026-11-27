using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using GamesDatabase.Api.Application.Interfaces;
using GamesDatabase.Api.Application.Services;
using GamesDatabase.Api.Contracts;
using GamesDatabase.Api.Domain.Entities;
using GamesDatabase.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GamesDatabase.Api.Tests;

public sealed class OnboardingViewsTests(GamesDatabaseApiFactory factory) : IClassFixture<GamesDatabaseApiFactory>
{
    private async Task<(HttpClient Client, int UserId)> Owner()
    {
        var client = factory.CreateClient(); await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<GamesDbContext>();
        var user = new User { Username = "Guide-views-" + Guid.NewGuid().ToString("N"), Role = UserRole.Standard, TutorialEnabled = true }; db.Users.Add(user); await db.SaveChangesAsync();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", scope.ServiceProvider.GetRequiredService<IAuthService>().GenerateToken(user));
        return (client, user.Id);
    }
    private static async Task<OnboardingStateDto> State(HttpClient client) => (await client.GetFromJsonAsync<OnboardingStateDto>("/api/onboarding"))!;

    [Fact]
    public async Task Reads_do_not_seed_views_and_explicit_view_creation_is_owned_localized_and_idempotent()
    {
        var owner = await Owner(); var other = await Owner(); var before = await State(owner.Client); Assert.Empty(before.ExampleViews);
        var rejected = await owner.Client.PostAsJsonAsync("/api/onboarding/views", new { revision = before.Revision, confirm = false, locale = "es" }); Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode); Assert.Empty((await State(owner.Client)).ExampleViews);
        var response = await owner.Client.PostAsJsonAsync("/api/onboarding/views", new { revision = before.Revision, confirm = true, locale = "es" }); response.EnsureSuccessStatusCode(); var created = (await response.Content.ReadFromJsonAsync<OnboardingStateDto>())!;
        Assert.Equal(3, created.ExampleViews.Count); Assert.Contains(created.ExampleViews, view => view.Name == "Ejemplo · Favoritos"); Assert.Empty(created.SampleGames); Assert.False(created.HasSamples); Assert.Empty((await State(other.Client)).ExampleViews);
        var again = await owner.Client.PostAsJsonAsync("/api/onboarding/views", new { revision = created.Revision, confirm = true, locale = "en" }); again.EnsureSuccessStatusCode(); var same = (await again.Content.ReadFromJsonAsync<OnboardingStateDto>())!;
        Assert.Equal(created.Revision, same.Revision); Assert.Equal(created.ExampleViews.Select(view => view.Id), same.ExampleViews.Select(view => view.Id));
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<GamesDbContext>(); Assert.Equal(0, await db.Games.CountAsync(game => game.UserId == owner.UserId)); Assert.Equal(3, await db.GameViews.CountAsync(view => view.UserId == owner.UserId));
    }

    [Fact]
    public async Task A_name_collision_does_not_replace_an_existing_view_or_its_configuration()
    {
        var owner = await Owner(); int existingId; const string original = "[]";
        await using (var scope = factory.Services.CreateAsyncScope()) { var db = scope.ServiceProvider.GetRequiredService<GamesDbContext>(); var existing = new GameView { UserId = owner.UserId, Name = "Ejemplo · Favoritos", FiltersJson = original, CreatedBy = "Owner", SortOrder = 5 }; db.GameViews.Add(existing); await db.SaveChangesAsync(); existingId = existing.Id; }
        var response = await owner.Client.PostAsJsonAsync("/api/onboarding/views", new { revision = 0, confirm = true, locale = "es" }); response.EnsureSuccessStatusCode();
        await using var check = factory.Services.CreateAsyncScope(); var context = check.ServiceProvider.GetRequiredService<GamesDbContext>(); var kept = await context.GameViews.AsNoTracking().SingleAsync(view => view.Id == existingId);
        Assert.Equal(original, kept.FiltersJson); Assert.Equal("Owner", kept.CreatedBy); Assert.Equal(5, kept.SortOrder); Assert.Equal(4, await context.GameViews.CountAsync(view => view.UserId == owner.UserId));
    }

    [Fact]
    public async Task Sample_batch_includes_useful_views_and_removing_the_fictional_games_keeps_the_views()
    {
        var owner = await Owner(); var response = await owner.Client.PostAsJsonAsync("/api/onboarding/samples", new { revision = 0, confirm = true, locale = "en" }); response.EnsureSuccessStatusCode(); var state = (await response.Content.ReadFromJsonAsync<OnboardingStateDto>())!;
        Assert.Equal(6, state.SampleGames.Count); Assert.Equal(3, state.ExampleViews.Count);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<GamesDbContext>(); var filter = scope.ServiceProvider.GetRequiredService<IViewFilterService>();
            foreach (var summary in state.ExampleViews)
            {
                var view = (await owner.Client.GetFromJsonAsync<GameViewDto>($"/api/gameviews/{summary.Id}"))!;
                var count = await filter.ApplyFilters(db.Games.AsNoTracking(), view.Configuration, owner.UserId).CountAsync();
                Assert.Equal(view.Configuration.FilterGroups[0].Filters[0].Field switch { FilterField.Favorite => 1, FilterField.Grade => 6, FilterField.Story => 4, _ => -1 }, count);
            }
        }
        var remove = new HttpRequestMessage(HttpMethod.Delete, "/api/onboarding/samples") { Content = JsonContent.Create(new ChangeTutorialSamplesRequest { Revision = state.Revision, Confirm = true, ExpectedGames = state.SampleGames.Select(game => new ExpectedSampleGameDto { Id = game.Id, UpdatedAt = game.UpdatedAt }).ToList(), ExpectedPlaylists = state.SampleLists.Select(list => new ExpectedSampleGameDto { Id = list.Id, UpdatedAt = list.UpdatedAt }).ToList() }) };
        var removed = await owner.Client.SendAsync(remove); removed.EnsureSuccessStatusCode(); var remaining = (await removed.Content.ReadFromJsonAsync<TutorialSampleChangeDto>())!;
        Assert.Equal(6, remaining.RemovedGames); Assert.Equal(3, remaining.State.ExampleViews.Count); Assert.False(remaining.State.HasSamples);
    }

    [Fact]
    public async Task The_views_chapter_and_guide_completion_do_not_remove_saved_views()
    {
        var owner = await Owner(); var created = await owner.Client.PostAsJsonAsync("/api/onboarding/views", new { revision = 0, confirm = true }); created.EnsureSuccessStatusCode(); var state = (await created.Content.ReadFromJsonAsync<OnboardingStateDto>())!;
        var chapter = await owner.Client.PutAsJsonAsync("/api/onboarding", new { revision = state.Revision, step = "views", enabled = true }); chapter.EnsureSuccessStatusCode(); state = (await chapter.Content.ReadFromJsonAsync<OnboardingStateDto>())!; Assert.Equal("views", state.Step);
        var done = await owner.Client.PutAsJsonAsync("/api/onboarding", new { revision = state.Revision, step = "finish", complete = true, enabled = false }); done.EnsureSuccessStatusCode(); state = (await done.Content.ReadFromJsonAsync<OnboardingStateDto>())!;
        Assert.False(state.Enabled); Assert.Equal(3, state.ExampleViews.Count);
    }

    [Fact]
    public async Task Retrying_examples_never_overwrites_edited_views_or_recreates_a_partially_deleted_set()
    {
        var owner = await Owner(); var created = await owner.Client.PostAsJsonAsync("/api/onboarding/views", new { revision = 0, confirm = true, locale = "en" }); created.EnsureSuccessStatusCode(); var state = (await created.Content.ReadFromJsonAsync<OnboardingStateDto>())!;
        var keptId = state.ExampleViews[0].Id; var deletedId = state.ExampleViews[1].Id;
        (await owner.Client.PutAsJsonAsync($"/api/gameviews/{keptId}", new { name = "My renamed view", description = "Owner revision", isPublic = false })).EnsureSuccessStatusCode();
        (await owner.Client.DeleteAsync($"/api/gameviews/{deletedId}")).EnsureSuccessStatusCode();
        var retry = await owner.Client.PostAsJsonAsync("/api/onboarding/views", new { revision = state.Revision, confirm = true, locale = "es" }); retry.EnsureSuccessStatusCode(); var after = (await retry.Content.ReadFromJsonAsync<OnboardingStateDto>())!;
        Assert.Equal(2, after.ExampleViews.Count); Assert.Contains(after.ExampleViews, view => view.Id == keptId && view.Name == "My renamed view" && !view.IsPublic); Assert.DoesNotContain(after.ExampleViews, view => view.Id == deletedId); Assert.Equal(state.Revision, after.Revision);
    }

    [Theory]
    [InlineData(-1, "en")]
    [InlineData(0, "unsupported")]
    public async Task Invalid_view_seed_requests_and_anonymous_access_are_rejected(int revision, string locale)
    {
        var owner = await Owner(); var bad = await owner.Client.PostAsJsonAsync("/api/onboarding/views", new { revision, locale, confirm = true }); Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        var anonymous = factory.CreateClient(); Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsJsonAsync("/api/onboarding/views", new { revision = 0, confirm = true })).StatusCode);
    }
}
