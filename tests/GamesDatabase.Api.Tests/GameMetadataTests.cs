using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using GamesDatabase.Api.Application.Interfaces;
using GamesDatabase.Api.Contracts;
using GamesDatabase.Api.Domain.Entities;
using GamesDatabase.Api.Infrastructure.Persistence;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.DataProtection;
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
        public string? WikidataName { get; set; }
        public bool NoSteam { get; set; }
        public bool NoWiki { get; set; }
        public Dictionary<string, object> SteamFields { get; } = new() { ["name"] = "Verified title", ["developer"] = "Verified studio", ["released"] = "2024-02-29", ["critic"] = 90, ["grade"] = 100, ["story"] = 999, ["manualPlaytimeMinutes"] = 9999 };
        public Task<List<GameMetadataIdentityDto>> SearchSteamAsync(string name, CancellationToken token) => Task.FromResult(new List<GameMetadataIdentityDto> { new() { Source = "steam", Name = name, SteamAppId = 100, Url = "https://store.steampowered.com/app/100/" } });
        public Task<PublicGameMetadata?> SteamAsync(int appId, bool artwork, CancellationToken token) => Task.FromResult<PublicGameMetadata?>(NoSteam ? null : new() { Identity = new() { Source = "steam", Name = "Verified title", SteamAppId = appId, Url = $"https://store.steampowered.com/app/{appId}/" }, Fields = SteamFields });
        public Task<List<PublicGameMetadata>> WikidataAsync(string name, int? appId, CancellationToken token, string? selectedId = null, bool forceRefresh = false) => Task.FromResult(NoWiki ? [] : new List<PublicGameMetadata> { new() { Identity = new() { Source = "wikidata", WikidataId = "Q7889", Name = WikidataName ?? name, Url = "https://www.wikidata.org/wiki/Q7889" }, Fields = new() { ["name"] = "Lower-priority title", ["developer"] = "Lower-priority studio", ["released"] = "2020-01-01" } } });
    }
    private sealed class Clock : TimeProvider { public DateTimeOffset Now { get; set; } = DateTimeOffset.UtcNow; public override DateTimeOffset GetUtcNow() => Now; }
    private sealed class DurationSource : IGameDurationProvider
    {
        public int Reads { get; private set; }
        public bool LastForceRefresh { get; private set; }
        public string? LastName { get; private set; }
        public string? RatingProvider { get; set; }
        public int Rating { get; set; } = 94;
        public Task<GameDurationLookup> LookupAsync(string name, string? selectedSlug, CancellationToken token, bool forceRefresh = false)
        {
            Reads++; LastName = name; LastForceRefresh = forceRefresh; token.ThrowIfCancellationRequested();
            var metadata = new PublicGameMetadata { Identity = new() { Source = "dekudeals", Name = name, DekuDealsSlug = "verified-fixture", Url = "https://www.dekudeals.com/items/verified-fixture" }, Fields = new() { ["story"] = 24, ["completion"] = 66 }, SourceHours = new() { ["story"] = 23.5m, ["completion"] = 65.5m } };
            if (RatingProvider is not null) { metadata.Fields["critic"] = Rating; metadata.CriticProvider = RatingProvider; metadata.CriticUrl = RatingProvider == "Metacritic" ? "https://www.metacritic.com/game/verified-fixture/" : "https://opencritic.com/game/42/verified-fixture"; metadata.CriticScope = "listed-platform"; }
            return Task.FromResult(new GameDurationLookup { Metadata = metadata });
        }
    }
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
    private static async Task<GameMetadataPreviewDto> Preview2(HttpClient client, int id, bool overwrite = false, bool durations = false, bool critic = true)
    {
        var response = await client.PostAsJsonAsync($"/api/games/{id}/metadata/preview", new GameMetadataLookupRequest { ReviewVersion = 2, Overwrite = overwrite, IncludeArtwork = false, IncludeDurations = durations, IncludeCritic = critic }); response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<GameMetadataPreviewDto>())!;
    }

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

    [Fact]
    public async Task Reviewed_durations_preserve_fractional_provenance_and_only_save_the_checked_whole_hour_field()
    {
        var durations = new DurationSource(); using var host = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services => { services.RemoveAll<IPublicGameMetadataProvider>(); services.AddSingleton<IPublicGameMetadataProvider>(new PublicSources()); services.RemoveAll<IGameDurationProvider>(); services.AddSingleton<IGameDurationProvider>(durations); }));
        var owner = await Owner(host.Services, host.CreateClient()); var preview = await Preview(owner.Client, owner.GameId);
        var story = Assert.Single(preview.Fields, field => field.Field == "story"); Assert.Equal("dekudeals", story.Source); Assert.Equal(23.5m, story.SourceHours); Assert.Equal(24, story.Value.GetInt32()); Assert.DoesNotContain("durationUnavailable", preview.Notices);
        var before = (await owner.Client.GetFromJsonAsync<GameDto>($"/api/games/{owner.GameId}"))!; Assert.Null(before.Story); Assert.Null(before.Completion);
        var response = await Apply(owner.Client, owner.GameId, preview, "story"); response.EnsureSuccessStatusCode(); var saved = (await response.Content.ReadFromJsonAsync<GameMetadataApplyDto>())!.Game;
        Assert.Equal(24, saved.Story); Assert.Null(saved.Completion); Assert.Equal(12, saved.Grade); Assert.Equal("My personal note", saved.Comment); Assert.Equal(0, saved.ManualPlaytimeMinutes); Assert.Equal(1, durations.Reads);
    }

    [Fact]
    public async Task Existing_durations_are_preserved_unless_overwrite_is_explicit_and_opt_out_never_calls_the_provider()
    {
        var durations = new DurationSource(); using var host = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services => { services.RemoveAll<IPublicGameMetadataProvider>(); services.AddSingleton<IPublicGameMetadataProvider>(new PublicSources()); services.RemoveAll<IGameDurationProvider>(); services.AddSingleton<IGameDurationProvider>(durations); }));
        var owner = await Owner(host.Services, host.CreateClient()); (await owner.Client.PutAsJsonAsync($"/api/games/{owner.GameId}", new { story = 0, completion = 100 })).EnsureSuccessStatusCode();
        var existing = await Preview(owner.Client, owner.GameId); Assert.DoesNotContain(existing.Fields, field => field.Field is "story" or "completion"); Assert.Equal(0, durations.Reads);
        var overwrite = await Preview(owner.Client, owner.GameId, true); Assert.Equal(2, overwrite.Fields.Count(field => field.Field is "story" or "completion")); Assert.Equal(1, durations.Reads);
        var optedOut = await owner.Client.PostAsJsonAsync($"/api/games/{owner.GameId}/metadata/preview", new GameMetadataLookupRequest { Overwrite = true, IncludeArtwork = false, IncludeDurations = false }); optedOut.EnsureSuccessStatusCode(); Assert.Equal(1, durations.Reads);
    }

    [Fact]
    public async Task A_chosen_Wikidata_identity_supplies_the_duration_search_name_not_the_custom_library_title()
    {
        var durations = new DurationSource(); using var host = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services => { services.RemoveAll<IPublicGameMetadataProvider>(); services.AddSingleton<IPublicGameMetadataProvider>(new PublicSources { WikidataName = "Canonical edition fixture" }); services.RemoveAll<IGameDurationProvider>(); services.AddSingleton<IGameDurationProvider>(durations); }));
        var owner = await Owner(host.Services, host.CreateClient()); var response = await owner.Client.PostAsJsonAsync($"/api/games/{owner.GameId}/metadata/preview", new GameMetadataLookupRequest { WikidataId = "Q7889", IncludeArtwork = false }); response.EnsureSuccessStatusCode();
        Assert.Equal("Canonical edition fixture", durations.LastName); Assert.Equal(1, durations.Reads); Assert.Equal("My custom title", (await owner.Client.GetFromJsonAsync<GameDto>($"/api/games/{owner.GameId}"))!.Name);
    }

    [Fact]
    public async Task An_unresolved_explicit_identity_never_falls_back_to_durations_for_an_unrelated_custom_title()
    {
        var durations = new DurationSource(); using var host = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services => { services.RemoveAll<IPublicGameMetadataProvider>(); services.AddSingleton<IPublicGameMetadataProvider>(new PublicSources { WikidataName = "Different identity fixture" }); services.RemoveAll<IGameDurationProvider>(); services.AddSingleton<IGameDurationProvider>(durations); }));
        var owner = await Owner(host.Services, host.CreateClient()); var response = await owner.Client.PostAsJsonAsync($"/api/games/{owner.GameId}/metadata/preview", new GameMetadataLookupRequest { WikidataId = "Q99999", IncludeArtwork = false }); response.EnsureSuccessStatusCode(); var preview = (await response.Content.ReadFromJsonAsync<GameMetadataPreviewDto>())!;
        Assert.Equal(0, durations.Reads); Assert.DoesNotContain(preview.Fields, field => field.Field is "story" or "completion"); Assert.Null(preview.Token);
    }

    [Fact]
    public async Task Background_best_match_does_not_use_a_custom_title_when_the_existing_Steam_identity_cannot_be_confirmed()
    {
        var durations = new DurationSource();
        using var host = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IPublicGameMetadataProvider>(); services.AddSingleton<IPublicGameMetadataProvider>(new PublicSources { NoSteam = true, NoWiki = true });
            services.RemoveAll<IGameDurationProvider>(); services.AddSingleton<IGameDurationProvider>(durations);
        }));
        var owner = await Owner(host.Services, host.CreateClient());
        (await owner.Client.PutAsJsonAsync($"/api/games/{owner.GameId}", new { steamAppId = 100 })).EnsureSuccessStatusCode();
        var response = await owner.Client.PostAsJsonAsync($"/api/games/{owner.GameId}/metadata/preview", new GameMetadataLookupRequest { ReviewVersion = 2, Overwrite = true, BestMatch = true }); response.EnsureSuccessStatusCode();
        var preview = (await response.Content.ReadFromJsonAsync<GameMetadataPreviewDto>())!;
        Assert.Contains("chooseIdentity", preview.Notices); Assert.Equal(0, durations.Reads); Assert.Empty(preview.Fields);
    }

    [Theory]
    [InlineData("Metacritic", 93)]
    [InlineData("OpenCritic", 94)]
    public async Task Critic_fallback_does_not_depend_on_requesting_durations_and_apply_binds_the_actual_provider(string provider, int score)
    {
        var sources = new PublicSources(); sources.SteamFields.Remove("critic"); var supplemental = new DurationSource { RatingProvider = provider, Rating = score };
        using var host = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services => { services.RemoveAll<IPublicGameMetadataProvider>(); services.AddSingleton<IPublicGameMetadataProvider>(sources); services.RemoveAll<IGameDurationProvider>(); services.AddSingleton<IGameDurationProvider>(supplemental); }));
        var owner = await Owner(host.Services, host.CreateClient()); var preview = await Preview2(owner.Client, owner.GameId);
        var critic = Assert.Single(preview.Fields, field => field.Field == "critic"); Assert.Equal(score, critic.Value.GetInt32()); Assert.Equal(provider, critic.CriticProvider); Assert.Equal("dekudeals", critic.Source); Assert.Equal("listed-platform", critic.CriticScope); Assert.NotNull(critic.CriticUrl); Assert.Equal(1, supplemental.Reads);
        Assert.DoesNotContain(preview.Fields, field => field.Field is "story" or "completion"); Assert.Equal(provider == "OpenCritic", preview.Notices.Contains("steamDbNotAutomated"));
        var response = await Apply(owner.Client, owner.GameId, preview, "critic"); response.EnsureSuccessStatusCode(); var saved = (await response.Content.ReadFromJsonAsync<GameMetadataApplyDto>())!.Game;
        Assert.Equal(score, saved.Critic); Assert.Equal(provider, saved.CriticProvider); Assert.Equal(12, saved.Grade); Assert.Equal(0, saved.ManualPlaytimeMinutes); Assert.Equal("My personal note", saved.Comment); Assert.Null(saved.SteamAppId); Assert.Null(saved.Story); Assert.Null(saved.Completion);
    }

    [Fact]
    public async Task Steam_edition_Metacritic_remains_preferred_over_a_different_listed_platform_on_Deku()
    {
        var supplemental = new DurationSource { RatingProvider = "Metacritic", Rating = 93 };
        using var host = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services => { services.RemoveAll<IPublicGameMetadataProvider>(); services.AddSingleton<IPublicGameMetadataProvider>(new PublicSources()); services.RemoveAll<IGameDurationProvider>(); services.AddSingleton<IGameDurationProvider>(supplemental); }));
        var owner = await Owner(host.Services, host.CreateClient()); var preview = await Preview2(owner.Client, owner.GameId, durations: true);
        var critic = Assert.Single(preview.Fields, field => field.Field == "critic"); Assert.Equal(90, critic.Value.GetInt32()); Assert.Equal("steam", critic.Source); Assert.Equal("Metacritic", critic.CriticProvider); Assert.Equal("steam-edition", critic.CriticScope); Assert.Equal(1, supplemental.Reads);
    }

    [Fact]
    public async Task Old_clients_never_receive_an_OpenCritic_candidate_they_would_label_as_Metacritic()
    {
        var sources = new PublicSources(); sources.SteamFields.Remove("critic"); var supplemental = new DurationSource { RatingProvider = "OpenCritic" };
        using var host = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services => { services.RemoveAll<IPublicGameMetadataProvider>(); services.AddSingleton<IPublicGameMetadataProvider>(sources); services.RemoveAll<IGameDurationProvider>(); services.AddSingleton<IGameDurationProvider>(supplemental); }));
        var owner = await Owner(host.Services, host.CreateClient()); var preview = await Preview(owner.Client, owner.GameId);
        Assert.Equal(1, preview.ReviewVersion); Assert.DoesNotContain(preview.Fields, field => field.Field == "critic"); Assert.Contains(preview.Fields, field => field.Field == "story"); Assert.False(supplemental.LastForceRefresh);
    }

    [Fact]
    public async Task New_provider_bound_plans_are_not_decryptable_by_the_old_API_purpose_after_a_binary_rollback()
    {
        var sources = new PublicSources(); sources.SteamFields.Remove("critic");
        using var host = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services => { services.RemoveAll<IPublicGameMetadataProvider>(); services.AddSingleton<IPublicGameMetadataProvider>(sources); services.RemoveAll<IGameDurationProvider>(); services.AddSingleton<IGameDurationProvider>(new DurationSource { RatingProvider = "OpenCritic" }); }));
        var owner = await Owner(host.Services, host.CreateClient()); var preview = await Preview2(owner.Client, owner.GameId);
        var old = host.Services.GetRequiredService<IDataProtectionProvider>().CreateProtector("GamesDatabase.Metadata.v1");
        Assert.Throws<CryptographicException>(() => old.Unprotect(preview.Token!));
        (await Apply(owner.Client, owner.GameId, preview, "critic")).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Replace_searches_filled_fields_fresh_and_same_value_confirmation_never_forges_an_edit_or_history()
    {
        var supplemental = new DurationSource();
        using var host = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services => { services.RemoveAll<IPublicGameMetadataProvider>(); services.AddSingleton<IPublicGameMetadataProvider>(new PublicSources()); services.RemoveAll<IGameDurationProvider>(); services.AddSingleton<IGameDurationProvider>(supplemental); }));
        var owner = await Owner(host.Services, host.CreateClient());
        (await owner.Client.PutAsJsonAsync($"/api/games/{owner.GameId}", new { name = "Verified title", developer = "Verified studio", released = "2024-02-29", critic = 90, criticProvider = "Metacritic", story = 24, completion = 66 })).EnsureSuccessStatusCode();
        var before = (await owner.Client.GetFromJsonAsync<GameDto>($"/api/games/{owner.GameId}"))!;
        await using var scope = host.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<GamesDbContext>(); var historyBefore = await db.GameHistoryEntries.CountAsync();
        var preview = await Preview2(owner.Client, owner.GameId, overwrite: true, durations: true);
        Assert.True(supplemental.LastForceRefresh); Assert.Equal(6, preview.Fields.Count); Assert.All(preview.Fields, field => Assert.True(field.Unchanged));
        var selected = preview.Fields.Select(field => field.Field).ToArray(); var response = await Apply(owner.Client, owner.GameId, preview, selected); response.EnsureSuccessStatusCode(); var result = (await response.Content.ReadFromJsonAsync<GameMetadataApplyDto>())!;
        Assert.Equal(selected, result.AppliedFields); Assert.Equal(selected, result.UnchangedFields); Assert.Equal(before.UpdatedAt, result.Game.UpdatedAt); Assert.Equal(before.Score, result.Game.Score); Assert.Equal(historyBefore, await db.GameHistoryEntries.CountAsync());
    }

    [Fact]
    public async Task Unchecked_critic_and_duration_groups_do_not_request_Deku_or_propose_those_fields()
    {
        var supplemental = new DurationSource { RatingProvider = "OpenCritic" };
        using var host = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services => { services.RemoveAll<IPublicGameMetadataProvider>(); services.AddSingleton<IPublicGameMetadataProvider>(new PublicSources()); services.RemoveAll<IGameDurationProvider>(); services.AddSingleton<IGameDurationProvider>(supplemental); }));
        var owner = await Owner(host.Services, host.CreateClient()); var preview = await Preview2(owner.Client, owner.GameId, overwrite: true, critic: false);
        Assert.Equal(0, supplemental.Reads); Assert.DoesNotContain(preview.Fields, field => field.Field is "critic" or "story" or "completion" or "logo" or "hero" or "cover");
    }
}
