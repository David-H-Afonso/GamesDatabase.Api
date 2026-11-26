using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using GamesDatabase.Api.Application.Interfaces;
using GamesDatabase.Api.Contracts;
using GamesDatabase.Api.Domain.Entities;
using GamesDatabase.Api.Infrastructure.Persistence;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace GamesDatabase.Api.Tests;

/// <summary>Real authorization/persistence with isolated, deterministic public-provider fixtures.</summary>
public sealed class GameMetadataTests(GamesDatabaseApiFactory factory) : IClassFixture<GamesDatabaseApiFactory>
{
    private sealed class PublicSources : IPublicGameMetadataProvider
    {
        public Dictionary<string, object> SteamFields { get; } = new() { ["name"] = "Verified title", ["developer"] = "Verified studio", ["released"] = "2024-02-29", ["critic"] = 90, ["grade"] = 100, ["story"] = 999, ["manualPlaytimeMinutes"] = 9999 };
        public Task<List<GameMetadataIdentityDto>> SearchSteamAsync(string name, CancellationToken token) => Task.FromResult(new List<GameMetadataIdentityDto> { new() { Source = "steam", Name = name, SteamAppId = 100, Url = "https://store.steampowered.com/app/100/" } });
        public Task<PublicGameMetadata?> SteamAsync(int appId, bool artwork, CancellationToken token) => Task.FromResult<PublicGameMetadata?>(new() { Identity = new() { Source = "steam", Name = "Verified title", SteamAppId = appId, Url = $"https://store.steampowered.com/app/{appId}/" }, Fields = SteamFields });
        public Task<List<PublicGameMetadata>> WikidataAsync(string name, int? appId, CancellationToken token) => Task.FromResult(new List<PublicGameMetadata> { new() { Identity = new() { Source = "wikidata", WikidataId = "Q7889", Name = name, Url = "https://www.wikidata.org/wiki/Q7889" }, Fields = new() { ["name"] = "Lower-priority title", ["developer"] = "Lower-priority studio", ["released"] = "2020-01-01" } } });
    }
    private sealed class Clock : TimeProvider { public DateTimeOffset Now { get; set; } = DateTimeOffset.UtcNow; public override DateTimeOffset GetUtcNow() => Now; }
    private async Task<(HttpClient Client, int GameId)> Owner(IServiceProvider services, HttpClient client)
    {
        await using var scope = services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<GamesDbContext>();
        var user = new User { Username = "Metadata-" + Guid.NewGuid().ToString("N"), Role = UserRole.Standard }; db.Users.Add(user); await db.SaveChangesAsync();
        var status = new GameStatus { Name = "My backlog", UserId = user.Id }; db.GameStatuses.Add(status); await db.SaveChangesAsync();
        var game = new Game { Name = "My custom title", UserId = user.Id, StatusId = status.Id, Comment = "My personal note", ManualPlaytimeMinutes = 0, Grade = 12 }; db.Games.Add(game); await db.SaveChangesAsync();
        var auth = scope.ServiceProvider.GetRequiredService<IAuthService>(); client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.GenerateToken(user));
        return (client, game.Id);
    }
    private static async Task<GameMetadataPreviewDto> Preview(HttpClient client, int id, bool overwrite = false)
    {
        var response = await client.PostAsJsonAsync($"/api/games/{id}/metadata/preview", new GameMetadataLookupRequest { Overwrite = overwrite, IncludeArtwork = false }); response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<GameMetadataPreviewDto>())!;
    }
    private static Task<HttpResponseMessage> Apply(HttpClient client, int id, GameMetadataPreviewDto preview, params string[] fields) => client.PostAsJsonAsync($"/api/games/{id}/metadata/apply", new GameMetadataApplyRequest { Token = preview.Token!, Fields = fields.ToList() });

    [Fact]
    public async Task Preview_is_read_only_prioritizes_Steam_and_never_proposes_personal_data_or_playtime_as_estimates()
    {
        var sources = new PublicSources(); using var host = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services => { services.RemoveAll<IPublicGameMetadataProvider>(); services.AddSingleton<IPublicGameMetadataProvider>(sources); }));
        var owner = await Owner(host.Services, host.CreateClient()); var before = (await owner.Client.GetFromJsonAsync<GameDto>($"/api/games/{owner.GameId}"))!;
        var preview = await Preview(owner.Client, owner.GameId); Assert.NotNull(preview.Token); Assert.DoesNotContain(preview.Fields, field => field.Field is "name" or "grade" or "story" or "manualPlaytimeMinutes");
        Assert.Equal("Verified studio", preview.Fields.Single(field => field.Field == "developer").Value.GetString()); Assert.Equal("steam", preview.Fields.Single(field => field.Field == "developer").Source); Assert.Contains("durationUnavailable", preview.Notices);
        var after = (await owner.Client.GetFromJsonAsync<GameDto>($"/api/games/{owner.GameId}"))!; Assert.Null(after.Developer); Assert.Equal(before.UpdatedAt, after.UpdatedAt); Assert.Equal("My personal note", after.Comment); Assert.Equal(0, after.ManualPlaytimeMinutes);
        await using var scope = host.Services.CreateAsyncScope(); Assert.Empty(await scope.ServiceProvider.GetRequiredService<GamesDbContext>().SteamAppCaches.ToListAsync());
    }

    [Fact]
    public async Task Apply_writes_only_checked_fields_and_preserves_notes_progress_personal_rating_and_App_ID()
    {
        using var host = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services => { services.RemoveAll<IPublicGameMetadataProvider>(); services.AddSingleton<IPublicGameMetadataProvider>(new PublicSources()); }));
        var owner = await Owner(host.Services, host.CreateClient()); var preview = await Preview(owner.Client, owner.GameId, true);
        var response = await Apply(owner.Client, owner.GameId, preview, "developer", "released"); response.EnsureSuccessStatusCode(); var game = (await response.Content.ReadFromJsonAsync<GameMetadataApplyDto>())!.Game;
        Assert.Equal("Verified studio", game.Developer); Assert.Equal("2024-02-29", game.Released); Assert.Equal("My custom title", game.Name); Assert.Equal("My personal note", game.Comment); Assert.Equal(12, game.Grade); Assert.Equal(0, game.ManualPlaytimeMinutes); Assert.Null(game.SteamAppId); Assert.Null(game.Critic);
    }

    [Fact]
    public async Task Missing_primary_facts_use_Wikidata_without_overriding_more_reliable_facts()
    {
        var sources = new PublicSources(); sources.SteamFields.Remove("released"); using var host = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services => { services.RemoveAll<IPublicGameMetadataProvider>(); services.AddSingleton<IPublicGameMetadataProvider>(sources); }));
        var owner = await Owner(host.Services, host.CreateClient()); var preview = await Preview(owner.Client, owner.GameId, true);
        Assert.Equal("steam", preview.Fields.Single(field => field.Field == "developer").Source); Assert.Equal("wikidata", preview.Fields.Single(field => field.Field == "released").Source); Assert.Equal("Verified title", preview.Fields.Single(field => field.Field == "name").Value.GetString());
    }

    [Fact]
    public async Task Zero_and_existing_values_are_filled_values_not_missing_metadata()
    {
        using var host = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services => { services.RemoveAll<IPublicGameMetadataProvider>(); services.AddSingleton<IPublicGameMetadataProvider>(new PublicSources()); }));
        var owner = await Owner(host.Services, host.CreateClient()); (await owner.Client.PutAsJsonAsync($"/api/games/{owner.GameId}", new { critic = 0, developer = "My studio" })).EnsureSuccessStatusCode();
        var preview = await Preview(owner.Client, owner.GameId); Assert.DoesNotContain(preview.Fields, field => field.Field is "critic" or "developer" or "name");
    }

    [Fact]
    public async Task A_concurrent_edit_rejects_the_whole_plan_before_changing_any_field()
    {
        using var host = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services => { services.RemoveAll<IPublicGameMetadataProvider>(); services.AddSingleton<IPublicGameMetadataProvider>(new PublicSources()); }));
        var owner = await Owner(host.Services, host.CreateClient()); var preview = await Preview(owner.Client, owner.GameId, true);
        (await owner.Client.PutAsJsonAsync($"/api/games/{owner.GameId}", new { comment = "Concurrent personal edit" })).EnsureSuccessStatusCode(); Assert.Equal(HttpStatusCode.Conflict, (await Apply(owner.Client, owner.GameId, preview, "developer", "name")).StatusCode);
        var game = (await owner.Client.GetFromJsonAsync<GameDto>($"/api/games/{owner.GameId}"))!; Assert.Null(game.Developer); Assert.Equal("My custom title", game.Name); Assert.Equal("Concurrent personal edit", game.Comment);
    }

    [Fact]
    public async Task Plans_are_owned_tamper_protected_expiring_and_cannot_write_unreviewed_fields()
    {
        var clock = new Clock(); using var host = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services => { services.RemoveAll<IPublicGameMetadataProvider>(); services.AddSingleton<IPublicGameMetadataProvider>(new PublicSources()); services.RemoveAll<TimeProvider>(); services.AddSingleton<TimeProvider>(clock); }));
        var owner = await Owner(host.Services, host.CreateClient()); var other = await Owner(host.Services, host.CreateClient()); var preview = await Preview(owner.Client, owner.GameId, true);
        Assert.Equal(HttpStatusCode.NotFound, (await other.Client.PostAsJsonAsync($"/api/games/{owner.GameId}/metadata/preview", new GameMetadataLookupRequest())).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Apply(other.Client, owner.GameId, preview, "developer")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Apply(owner.Client, owner.GameId, preview, "favorite")).StatusCode);
        var tampered = new GameMetadataPreviewDto { Token = "invalid-fixture-token" }; Assert.Equal(HttpStatusCode.BadRequest, (await Apply(owner.Client, owner.GameId, tampered, "developer")).StatusCode);
        clock.Now += TimeSpan.FromMinutes(11); Assert.Equal(HttpStatusCode.BadRequest, (await Apply(owner.Client, owner.GameId, preview, "developer")).StatusCode);
        var anonymous = host.CreateClient(); Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsJsonAsync($"/api/games/{owner.GameId}/metadata/preview", new GameMetadataLookupRequest())).StatusCode);
    }
}
