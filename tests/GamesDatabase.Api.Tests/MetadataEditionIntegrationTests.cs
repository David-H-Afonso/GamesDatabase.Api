using System.Net.Http.Headers;
using System.Net.Http.Json;
using GamesDatabase.Api.Application.Interfaces;
using GamesDatabase.Api.Common;
using GamesDatabase.Api.Contracts;
using GamesDatabase.Api.Domain.Entities;
using GamesDatabase.Api.Infrastructure.Persistence;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace GamesDatabase.Api.Tests;

/// <summary>Owned HTTP review/apply and SQLite, with deterministic public-provider evidence.</summary>
public sealed class MetadataEditionIntegrationTests(GamesDatabaseApiFactory factory) : IClassFixture<GamesDatabaseApiFactory>
{
    private const string Ocarina = "The Legend of Zelda: Ocarina of Time";
    private sealed class Facts : IPublicGameMetadataProvider
    {
        public List<PublicGameMetadata> Items { get; set; } = [];
        public bool Failed { get; set; }
        public Task<List<GameMetadataIdentityDto>> SearchSteamAsync(string name, CancellationToken token) => Task.FromResult(new List<GameMetadataIdentityDto>());
        public Task<PublicGameMetadata?> SteamAsync(int id, bool images, CancellationToken token) => throw new InvalidOperationException("These fixtures have no Steam release.");
        public Task<List<PublicGameMetadata>> WikidataAsync(string name, int? appId, CancellationToken token, string? selectedId = null, bool forceRefresh = false) => Failed
            ? throw new MetadataSourceUnavailableException("Wikidata", 90)
            : Task.FromResult(Items.Where(item => selectedId is null || item.Identity.WikidataId == selectedId).ToList());
    }
    private sealed class Supplemental(PublicGameMetadata value) : IGameDurationProvider
    {
        public int Reads { get; private set; }
        public Task<GameDurationLookup> LookupAsync(string name, string? selectedSlug, CancellationToken token, bool forceRefresh = false)
        {
            Reads++; return Task.FromResult(new GameDurationLookup { Metadata = value, Matches = [value.Identity], IdentityConfirmed = true });
        }
    }
    private sealed class Artwork(bool available = true) : IGameArtworkProvider
    {
        public int Reads { get; private set; }
        public GameMetadataIdentityDto? Edition { get; private set; }
        public Task<GameArtworkLookup> LookupAsync(IReadOnlyList<PublicGameMetadata> evidence, IReadOnlyCollection<string> fields, CancellationToken token, bool forceRefresh = false, int? selectedLaunchBoxId = null)
        {
            Reads++; Edition = evidence[0].Identity;
            var source = new PublicGameMetadata { Identity = new() { Source = "launchbox", Name = Edition.Name, LaunchBoxId = 10, Url = "https://gamesdb.launchbox-app.com/games/details/10-verified-edition", ReleaseYear = Edition.ReleaseYear, Platforms = Edition.Platforms } };
            var index = 1;
            foreach (var role in fields) source.Fields[role] = $"https://images.launchbox-app.com/{index++:00000000}-1111-4111-8111-111111111111.png";
            // An artwork provider cannot smuggle catalog/critic mutations into its
            // successful response, even when those fields are otherwise allowed.
            source.Fields["developer"] = "Do not save artwork-provider catalog data";
            return Task.FromResult(new GameArtworkLookup { Sources = available ? [source] : [] });
        }
    }
    private static PublicGameMetadata Wiki(string name, string id, int year, params string[] platforms) => new()
    {
        Identity = new() { Source = "wikidata", Name = name, WikidataId = id, Url = "https://www.wikidata.org/wiki/" + id, ReleaseYear = year, Platforms = platforms.ToList() },
        Fields = new() { ["name"] = name, ["released"] = $"{year}-11-05", ["developer"] = "Verified studio " + year }
    };
    private static PublicGameMetadata Deku(string name, int year, params string[] platforms) => new()
    {
        Identity = new() { Source = "dekudeals", Name = name, DekuDealsSlug = "verified-edition", Url = "https://www.dekudeals.com/items/verified-edition", ReleaseYear = year, Platforms = platforms.ToList() },
        Fields = new() { ["released"] = $"{year}-11-05", ["critic"] = 95, ["story"] = 20, ["completion"] = 28 }, SourceHours = new() { ["story"] = 20, ["completion"] = 28 },
        CriticProvider = "Metacritic", CriticUrl = "https://www.metacritic.com/game/verified-edition/", CriticScope = "listed-platform"
    };
    private Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> Host(Facts facts, Supplemental deku, Artwork artwork) => factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
    {
        services.RemoveAll<IPublicGameMetadataProvider>(); services.AddSingleton<IPublicGameMetadataProvider>(facts);
        services.RemoveAll<IGameDurationProvider>(); services.AddSingleton<IGameDurationProvider>(deku);
        services.RemoveAll<IGameArtworkProvider>(); services.AddSingleton<IGameArtworkProvider>(artwork);
    }));
    private static async Task<(int Id, HttpClient Client)> Seed(IServiceProvider services, HttpClient client, string name, string? released = null, string? platform = null, bool filled = false)
    {
        await using var scope = services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<GamesDbContext>();
        var user = new User { Username = "EditionFixture-" + Guid.NewGuid().ToString("N") }; db.Users.Add(user); await db.SaveChangesAsync();
        var status = new GameStatus { UserId = user.Id, Name = "Fixture backlog" }; db.GameStatuses.Add(status);
        var hardware = platform is null ? null : new GamePlatform { UserId = user.Id, Name = platform }; if (hardware is not null) db.GamePlatforms.Add(hardware); await db.SaveChangesAsync();
        var game = new Game { Name = name, Released = released, PlatformId = hardware?.Id, UserId = user.Id, StatusId = status.Id, Comment = "Keep this personal note", Grade = 87, Favorite = true, ManualPlaytimeMinutes = 125, Story = filled ? 20 : null, Completion = filled ? 28 : null, Critic = filled ? 77 : null, CriticProvider = filled ? "OpenCritic" : null };
        db.Games.Add(game); await db.SaveChangesAsync();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", scope.ServiceProvider.GetRequiredService<IAuthService>().GenerateToken(user));
        return (game.Id, client);
    }
    private static async Task<GameMetadataPreviewDto> Preview((int Id, HttpClient Client) owner, GameMetadataLookupRequest? request = null)
    {
        using var response = await owner.Client.PostAsJsonAsync($"/api/games/{owner.Id}/metadata/preview", request ?? new() { ReviewVersion = 2, Overwrite = true, BestMatch = true }); response.EnsureSuccessStatusCode(); return (await response.Content.ReadFromJsonAsync<GameMetadataPreviewDto>())!;
    }

    [Fact]
    public async Task Identically_named_1998_and_2026_games_require_a_choice_even_in_background_best_match()
    {
        var facts = new Facts { Items = [Wiki(Ocarina, "Q213911", 1998, "Nintendo 64"), Wiki(Ocarina, "Q140140365", 2026, "Switch 2")] }; var deku = new Supplemental(Deku(Ocarina, 2026, "Switch 2")); var images = new Artwork();
        using var host = Host(facts, deku, images); var owner = await Seed(host.Services, host.CreateClient(), Ocarina);
        var preview = await Preview(owner);
        Assert.Empty(preview.Fields); Assert.Null(preview.Token); Assert.Contains("chooseIdentity", preview.Notices); Assert.Equal(0, images.Reads);
        Assert.Contains(preview.Matches, identity => identity.WikidataId == "Q213911" && identity.ReleaseYear == 1998 && identity.Platforms.Contains("Nintendo 64"));
        Assert.Contains(preview.Matches, identity => identity.WikidataId == "Q140140365" && identity.ReleaseYear == 2026);
    }

    [Fact]
    public async Task A_reviewed_2026_identity_uses_2026_facts_and_three_images_and_apply_preserves_all_personal_state()
    {
        var facts = new Facts { Items = [Wiki(Ocarina, "Q213911", 1998, "Nintendo 64"), Wiki(Ocarina, "Q140140365", 2026, "Switch 2")] }; var deku = new Supplemental(Deku(Ocarina, 2026, "Switch 2")); var images = new Artwork();
        using var host = Host(facts, deku, images); var owner = await Seed(host.Services, host.CreateClient(), Ocarina, "1998-11-21");
        var preview = await Preview(owner, new() { ReviewVersion = 2, Overwrite = true, WikidataId = "Q140140365" });
        Assert.DoesNotContain("editionMismatch", preview.Notices); Assert.Equal(2026, images.Edition!.ReleaseYear); Assert.Equal(3, preview.Fields.Count(field => field.Field is "logo" or "hero" or "cover"));
        Assert.Equal("Verified studio 2026", preview.Fields.Single(field => field.Field == "developer").Value.GetString()); Assert.DoesNotContain(preview.Fields, field => field.Value.ToString().Contains("1998", StringComparison.Ordinal));
        using var saved = await owner.Client.PostAsJsonAsync($"/api/games/{owner.Id}/metadata/apply", new GameMetadataApplyRequest { Token = preview.Token!, Fields = ["released", "developer", "critic", "logo", "hero", "cover"] }); saved.EnsureSuccessStatusCode();
        var receipt = (await saved.Content.ReadFromJsonAsync<GameMetadataApplyDto>())!; Assert.Equal("2026-11-05", receipt.Game.Released); Assert.Equal(95, receipt.Game.Critic); Assert.Equal("Metacritic", receipt.Game.CriticProvider);
        Assert.Equal("Keep this personal note", receipt.Game.Comment); Assert.Equal(87, receipt.Game.Grade); Assert.True(receipt.Game.Favorite); Assert.Equal(125, receipt.Game.ManualPlaytimeMinutes); Assert.Null(receipt.Game.SteamAppId); Assert.Null(receipt.Game.Story);
    }

    [Fact]
    public async Task A_known_1998_N64_edition_never_borrows_2026_Deku_critic_or_hours()
    {
        var facts = new Facts { Items = [Wiki(Ocarina, "Q213911", 1998, "Nintendo 64"), Wiki(Ocarina, "Q140140365", 2026, "Switch 2")] }; var deku = new Supplemental(Deku(Ocarina, 2026, "Switch 2")); var images = new Artwork();
        using var host = Host(facts, deku, images); var owner = await Seed(host.Services, host.CreateClient(), Ocarina, "1998-11-21", "N64");
        var preview = await Preview(owner);
        Assert.Contains("editionMismatch", preview.Notices); Assert.DoesNotContain(preview.Fields, field => field.Source == "dekudeals" || field.Field is "critic" or "story" or "completion"); Assert.Equal(1998, images.Edition!.ReleaseYear); Assert.Equal(new[] { "N64" }, images.Edition.Platforms);
    }

    [Theory]
    [InlineData("Tomodachi Life: Living the Dream", "Nintendo Switch")]
    [InlineData("Marvel’s Wolverine", "PlayStation 5")]
    [InlineData("Grand Theft Auto VI", "PlayStation 5")]
    public async Task Missing_date_and_artwork_still_query_Deku_when_critic_and_hours_are_already_filled(string name, string platform)
    {
        var wiki = Wiki(name, "Q12345", 2026, platform); wiki.Fields.Remove("released");
        var facts = new Facts { Items = [wiki] }; var deku = new Supplemental(Deku(name, 2026, platform)); var images = new Artwork();
        using var host = Host(facts, deku, images); var owner = await Seed(host.Services, host.CreateClient(), name, platform: platform, filled: true);
        var preview = await Preview(owner, new() { ReviewVersion = 2, IncludeCritic = false, IncludeDurations = false });
        Assert.Equal(1, deku.Reads); Assert.Equal("dekudeals", preview.Fields.Single(field => field.Field == "released").Source); Assert.Equal(3, preview.Fields.Count(field => field.Field is "logo" or "hero" or "cover"));
        Assert.DoesNotContain(preview.Fields, field => field.Field is "critic" or "story" or "completion"); Assert.DoesNotContain(preview.Fields, field => field.Field == "developer" && field.Value.GetString()!.Contains("artwork-provider", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Owned_PS5_platform_narrows_multiplatform_artwork_without_changing_the_account_catalog()
    {
        const string name = "Grand Theft Auto VI"; var facts = new Facts { Items = [Wiki(name, "Q23648408", 2026, "PlayStation 5", "Xbox Series X|S")] }; var deku = new Supplemental(Deku(name, 2026, "PlayStation 5", "Xbox Series X|S")); var images = new Artwork();
        using var host = Host(facts, deku, images); var owner = await Seed(host.Services, host.CreateClient(), name, platform: "PS5");
        await Preview(owner); Assert.Equal(new[] { "PS5" }, images.Edition!.Platforms);
    }

    [Fact]
    public async Task A_provider_502_is_visible_with_its_retry_window_even_when_other_sources_succeed()
    {
        const string name = "Verified fixture title"; var facts = new Facts { Failed = true }; var deku = new Supplemental(Deku(name, 2026, "PS5")); var images = new Artwork(false);
        using var host = Host(facts, deku, images); var owner = await Seed(host.Services, host.CreateClient(), name, platform: "PS5");
        var preview = await Preview(owner, new() { ReviewVersion = 2, IncludeArtwork = false });
        Assert.Contains("wikidataUnavailable", preview.Notices); Assert.DoesNotContain("noChanges", preview.Notices); Assert.Equal(90, preview.RetryAfterSeconds); Assert.Contains(preview.Fields, field => field.Field == "critic" && field.Value.GetInt32() == 95);
    }
}
