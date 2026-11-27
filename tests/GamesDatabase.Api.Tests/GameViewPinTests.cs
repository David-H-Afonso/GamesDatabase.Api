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

public sealed class GameViewPinTests(GamesDatabaseApiFactory factory) : IClassFixture<GamesDatabaseApiFactory>
{
    private async Task<(HttpClient Client, int UserId, int[] Ids)> Owner()
    {
        var client = factory.CreateClient(); await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<GamesDbContext>();
        var user = new User { Username = "Pin-" + Guid.NewGuid().ToString("N"), Role = UserRole.Standard }; db.Users.Add(user); await db.SaveChangesAsync();
        var views = Enumerable.Range(1, 3).Select(index => new GameView { UserId = user.Id, Name = "View " + index, FiltersJson = "[]", IsPublic = true, SortOrder = index }).ToArray(); db.GameViews.AddRange(views); await db.SaveChangesAsync();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", scope.ServiceProvider.GetRequiredService<IAuthService>().GenerateToken(user));
        return (client, user.Id, views.Select(view => view.Id).ToArray());
    }
    private static Task<HttpResponseMessage> Pin(HttpClient client, int id, bool pinned = true) => client.PostAsJsonAsync($"/api/gameviews/{id}/pin", new { pinned });

    [Fact]
    public async Task Pins_up_to_two_owned_views_and_never_changes_criteria_order_usage_or_edit_timestamp()
    {
        var owner = await Owner(); var before = (await owner.Client.GetFromJsonAsync<GameViewDto>($"/api/gameviews/{owner.Ids[0]}"))!;
        var raw = await owner.Client.GetFromJsonAsync<JsonElement>($"/api/gameviews/{owner.Ids[0]}"); Assert.Equal(JsonValueKind.Null, raw.GetProperty("pinnedSlot").ValueKind);
        (await Pin(owner.Client, owner.Ids[0])).EnsureSuccessStatusCode(); (await Pin(owner.Client, owner.Ids[1])).EnsureSuccessStatusCode();
        var rejected = await Pin(owner.Client, owner.Ids[2]); Assert.Equal(HttpStatusCode.Conflict, rejected.StatusCode); Assert.Contains("pinLimit", await rejected.Content.ReadAsStringAsync());
        var after = (await owner.Client.GetFromJsonAsync<GameViewDto>($"/api/gameviews/{owner.Ids[0]}"))!; Assert.Equal(1, after.PinnedSlot); Assert.Equal(before.UpdatedAt, after.UpdatedAt); Assert.Equal(before.CreatedAt, after.CreatedAt); Assert.Equal(before.LastUsedAt, after.LastUsedAt); Assert.Equal(before.SortOrder, after.SortOrder); Assert.Equal(JsonSerializer.Serialize(before.Configuration), JsonSerializer.Serialize(after.Configuration));
        var all = (await owner.Client.GetFromJsonAsync<List<GameViewSummaryDto>>("/api/gameviews"))!; Assert.Equal(2, all.Count(view => view.PinnedSlot.HasValue)); Assert.Equal(new[] { 1, 2 }, all.Where(view => view.PinnedSlot.HasValue).Select(view => view.PinnedSlot!.Value).Order());
    }

    [Fact]
    public async Task Repeated_pin_is_idempotent_and_unpin_releases_capacity()
    {
        var owner = await Owner(); (await Pin(owner.Client, owner.Ids[0])).EnsureSuccessStatusCode(); var repeated = await Pin(owner.Client, owner.Ids[0]); repeated.EnsureSuccessStatusCode();
        Assert.Single((await repeated.Content.ReadFromJsonAsync<List<GameViewSummaryDto>>())!, view => view.PinnedSlot.HasValue);
        (await Pin(owner.Client, owner.Ids[1])).EnsureSuccessStatusCode(); (await Pin(owner.Client, owner.Ids[0], false)).EnsureSuccessStatusCode(); (await Pin(owner.Client, owner.Ids[2])).EnsureSuccessStatusCode();
        Assert.Null((await owner.Client.GetFromJsonAsync<GameViewDto>($"/api/gameviews/{owner.Ids[0]}"))!.PinnedSlot);
    }

    [Fact]
    public async Task Preferred_second_slot_is_retained_and_deleting_a_pin_frees_capacity_without_reordering_the_other()
    {
        var owner = await Owner(); (await owner.Client.PostAsJsonAsync($"/api/gameviews/{owner.Ids[1]}/pin", new { pinned = true, slot = 2 })).EnsureSuccessStatusCode();
        (await Pin(owner.Client, owner.Ids[0])).EnsureSuccessStatusCode(); Assert.Equal(2, (await owner.Client.GetFromJsonAsync<GameViewDto>($"/api/gameviews/{owner.Ids[1]}"))!.PinnedSlot);
        (await owner.Client.DeleteAsync($"/api/gameviews/{owner.Ids[0]}")).EnsureSuccessStatusCode(); (await Pin(owner.Client, owner.Ids[2])).EnsureSuccessStatusCode();
        Assert.Equal(1, (await owner.Client.GetFromJsonAsync<GameViewDto>($"/api/gameviews/{owner.Ids[2]}"))!.PinnedSlot); Assert.Equal(2, (await owner.Client.GetFromJsonAsync<GameViewDto>($"/api/gameviews/{owner.Ids[1]}"))!.PinnedSlot);
    }

    [Theory]
    [InlineData(0)] [InlineData(3)]
    public async Task Invalid_slot_is_rejected_without_writing(int slot)
    {
        var owner = await Owner(); Assert.Equal(HttpStatusCode.BadRequest, (await owner.Client.PostAsJsonAsync($"/api/gameviews/{owner.Ids[0]}/pin", new { pinned = true, slot })).StatusCode);
        Assert.All((await owner.Client.GetFromJsonAsync<List<GameViewSummaryDto>>("/api/gameviews"))!, view => Assert.Null(view.PinnedSlot));
    }

    [Fact]
    public async Task Pin_preference_marks_transfers_dirty_but_an_idempotent_pin_does_not_and_ordinary_edits_retain_the_pin()
    {
        var owner = await Owner();
        await using (var scope = factory.Services.CreateAsyncScope()) { var db = scope.ServiceProvider.GetRequiredService<GamesDbContext>(); await db.GameViews.Where(view => view.Id == owner.Ids[0]).ExecuteUpdateAsync(setters => setters.SetProperty(view => view.ModifiedSinceExport, false)); }
        (await Pin(owner.Client, owner.Ids[0])).EnsureSuccessStatusCode();
        await using (var scope = factory.Services.CreateAsyncScope()) { var db = scope.ServiceProvider.GetRequiredService<GamesDbContext>(); Assert.True((await db.GameViews.SingleAsync(view => view.Id == owner.Ids[0])).ModifiedSinceExport); await db.GameViews.Where(view => view.Id == owner.Ids[0]).ExecuteUpdateAsync(setters => setters.SetProperty(view => view.ModifiedSinceExport, false)); }
        (await Pin(owner.Client, owner.Ids[0])).EnsureSuccessStatusCode();
        await using (var scope = factory.Services.CreateAsyncScope()) { var db = scope.ServiceProvider.GetRequiredService<GamesDbContext>(); Assert.False((await db.GameViews.SingleAsync(view => view.Id == owner.Ids[0])).ModifiedSinceExport); }
        (await owner.Client.PutAsJsonAsync($"/api/gameviews/{owner.Ids[0]}", new { name = "Edited pinned view", isPublic = true, pinnedSlot = 2 })).EnsureSuccessStatusCode();
        Assert.Equal(1, (await owner.Client.GetFromJsonAsync<GameViewDto>($"/api/gameviews/{owner.Ids[0]}"))!.PinnedSlot);
    }

    [Fact]
    public async Task A_tracked_pin_only_update_is_transfer_dirty_without_changing_edit_or_usage_timestamps()
    {
        var owner = await Owner(); await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<GamesDbContext>(); var view = await db.GameViews.SingleAsync(view => view.Id == owner.Ids[0]);
        view.ModifiedSinceExport = false; await db.SaveChangesAsync(); var updated = view.UpdatedAt; var used = view.LastUsedAt;
        view.PinnedSlot = 2; await db.SaveChangesAsync(); Assert.True(view.ModifiedSinceExport); Assert.Equal(updated, view.UpdatedAt); Assert.Equal(used, view.LastUsedAt);
    }

    [Fact]
    public async Task Hidden_views_cannot_be_pinned_and_hiding_a_pinned_view_releases_its_slot()
    {
        var owner = await Owner(); (await Pin(owner.Client, owner.Ids[0])).EnsureSuccessStatusCode();
        (await owner.Client.PutAsJsonAsync($"/api/gameviews/{owner.Ids[0]}", new { isPublic = false })).EnsureSuccessStatusCode();
        Assert.Null((await owner.Client.GetFromJsonAsync<GameViewDto>($"/api/gameviews/{owner.Ids[0]}"))!.PinnedSlot); Assert.Equal(HttpStatusCode.BadRequest, (await Pin(owner.Client, owner.Ids[0])).StatusCode);
    }

    [Fact]
    public async Task Pinning_is_authenticated_owner_scoped_and_requires_an_explicit_boolean()
    {
        var owner = await Owner(); var other = await Owner();
        Assert.Equal(HttpStatusCode.NotFound, (await Pin(other.Client, owner.Ids[0])).StatusCode); Assert.Equal(HttpStatusCode.Unauthorized, (await Pin(factory.CreateClient(), owner.Ids[0])).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.Client.PostAsJsonAsync($"/api/gameviews/{owner.Ids[0]}/pin", new { })).StatusCode);
    }

    [Fact]
    public async Task Concurrent_requests_never_persist_more_than_two_slots()
    {
        var owner = await Owner(); var responses = await Task.WhenAll(owner.Ids.Select(id => Pin(owner.Client, id)));
        Assert.Equal(2, responses.Count(response => response.StatusCode == HttpStatusCode.OK)); Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Conflict);
        var all = (await owner.Client.GetFromJsonAsync<List<GameViewSummaryDto>>("/api/gameviews"))!; Assert.Equal(2, all.Count(view => view.PinnedSlot.HasValue));
    }
}
