using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using GamesDatabase.Api.Application.Interfaces;
using GamesDatabase.Api.Application.Services;
using GamesDatabase.Api.Contracts;
using GamesDatabase.Api.Domain.Entities;
using GamesDatabase.Api.Infrastructure.Persistence;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace GamesDatabase.Api.Tests;

// Real Program/JWT/metadata controller, concrete Deku parser, signed review and
// SQLite apply. Only public remote HTML/facts are deterministic fixtures; no
// external network, existing library, environment file or production host.
public sealed class DekuDealsDurationIntegrationTests(GamesDatabaseApiFactory factory) : IClassFixture<GamesDatabaseApiFactory>
{
    [Theory]
    [InlineData("Grand Theft Auto VI", "grand-theft-auto-vi", "48½ hours", 49, 48.5, 99, false)]
    [InlineData("CONTROL Resonant", "control-resonant", "24½ hours", 25, 24.5, 83, true)]
    public async Task Cross_platform_discovery_reaches_the_owned_review_and_only_the_explicit_fields_are_applied(string name, string slug, string sourceStory, int story, double exactHours, int critic, bool completionAvailable)
    {
        var reads = new List<string>();
        using var remote = new HttpClient(new HtmlTransport(request =>
        {
            reads.Add(request.RequestUri!.AbsoluteUri);
            Assert.Equal("https", request.RequestUri.Scheme); Assert.Equal("www.dekudeals.com", request.RequestUri.Host);
            Assert.False(request.Headers.Contains("Cookie")); Assert.False(request.Headers.Contains("Authorization"));
            if (request.RequestUri.AbsolutePath == "/search")
            {
                var query = QueryHelpers.ParseQuery(request.RequestUri.Query);
                Assert.Equal(name, query["q"].ToString());
                // A missing public preference reproduces the site's Nintendo-only
                // catalog rather than handing the provider a pre-parsed result.
                var all = query.TryGetValue("platforms", out var platforms) && platforms.ToString() == "switch_2,switch,ps5,ps4,xbox_series,xbox_one,steam";
                return all ? $"<main><a class='main-link' href='/items/{slug}'>{name}</a><a class='main-link' href='/items/{slug}-deluxe'>{name} Deluxe</a></main>" : "<main>No items matched your search</main>";
            }
            Assert.Equal("/items/" + slug, request.RequestUri.AbsolutePath);
            var completion = completionAvailable ? "<li><strong>Completionist:</strong>44 hours</li>" : "";
            return $"<html><head><meta property='og:title' content='{name} | Deku Deals'><link rel='canonical' href='https://www.dekudeals.com/items/{slug}'></head><body><main><ul><li><strong><a href='https://howlongtobeat.com/game.php?id=12345'>How Long To Beat</a>:</strong><ul><li><strong>Main Story:</strong>{sourceStory}</li><li><strong>Main + Extra:</strong>71½ hours</li>{completion}</ul></li><li><strong>Metacritic:</strong><a class='metacritic' href='https://www.metacritic.com/game/{slug}/'><span class='text-white'>{critic}</span><span>9.9</span></a></li></ul></main></body></html>";
        }));
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var deku = new DekuDealsDurationProvider(new NamedClient(remote), cache, TimeProvider.System);
        using var host = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IPublicGameMetadataProvider>(); services.AddSingleton<IPublicGameMetadataProvider>(new EmptyFacts());
            services.RemoveAll<IGameDurationProvider>(); services.AddSingleton<IGameDurationProvider>(deku);
        }));
        using var client = host.CreateClient();
        await using var scope = host.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<GamesDbContext>();
        var user = new User { Username = "Deku-review-" + Guid.NewGuid().ToString("N"), Role = UserRole.Standard };
        db.Users.Add(user); await db.SaveChangesAsync();
        var status = new GameStatus { UserId = user.Id, Name = "Owned fixture backlog" };
        db.GameStatuses.Add(status); await db.SaveChangesAsync();
        var game = new Game { UserId = user.Id, StatusId = status.Id, Name = name, Comment = "Retain this note", Grade = 88, Started = "2024-01-02", ManualPlaytimeMinutes = 125 };
        var retained = new Game { UserId = user.Id, StatusId = status.Id, Name = "Unselected retained fixture", Critic = 77, CriticProvider = "OpenCritic", Story = 12 };
        db.Games.AddRange(game, retained); await db.SaveChangesAsync();
        var unselectedUpdatedAt = retained.UpdatedAt;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", scope.ServiceProvider.GetRequiredService<IAuthService>().GenerateToken(user));

        using var previewResponse = await client.PostAsJsonAsync($"/api/games/{game.Id}/metadata/preview", new GameMetadataLookupRequest { ReviewVersion = 2, IncludeArtwork = false });
        previewResponse.EnsureSuccessStatusCode();
        var preview = (await previewResponse.Content.ReadFromJsonAsync<GameMetadataPreviewDto>())!;
        Assert.NotNull(preview.Token); Assert.Equal(2, preview.ReviewVersion);
        var storyField = Assert.Single(preview.Fields, field => field.Field == "story");
        Assert.Equal(story, storyField.Value.GetInt32()); Assert.Equal((decimal)exactHours, storyField.SourceHours);
        var criticField = Assert.Single(preview.Fields, field => field.Field == "critic");
        Assert.Equal(critic, criticField.Value.GetInt32()); Assert.Equal("Metacritic", criticField.CriticProvider); Assert.Equal("listed-platform", criticField.CriticScope);
        Assert.All(preview.Fields, field => { Assert.Equal("dekudeals", field.Source); Assert.Equal("https://www.dekudeals.com/items/" + slug, field.Url); });
        Assert.Equal(completionAvailable, preview.Fields.Any(field => field.Field == "completion"));
        Assert.Contains(preview.Matches, match => match.DekuDealsSlug == slug);
        Assert.DoesNotContain("chooseDurationIdentity", preview.Notices);
        var before = (await client.GetFromJsonAsync<GameDto>($"/api/games/{game.Id}"))!;
        Assert.Null(before.Story); Assert.Null(before.Critic); // Preview is still read-only.

        using var applied = await client.PostAsJsonAsync($"/api/games/{game.Id}/metadata/apply", new GameMetadataApplyRequest { Token = preview.Token!, Fields = ["story", "critic"] });
        applied.EnsureSuccessStatusCode();
        var receipt = (await applied.Content.ReadFromJsonAsync<GameMetadataApplyDto>())!;
        Assert.Equal(new[] { "story", "critic" }, receipt.AppliedFields); Assert.Empty(receipt.UnchangedFields);
        Assert.Equal(game.Id, receipt.Game.Id); Assert.Equal(story, receipt.Game.Story); Assert.Equal(critic, receipt.Game.Critic); Assert.Equal("Metacritic", receipt.Game.CriticProvider);
        Assert.Null(receipt.Game.Completion); // Even a real proposal is not permission to save an unchecked field.
        Assert.Equal("Retain this note", receipt.Game.Comment); Assert.Equal(88, receipt.Game.Grade); Assert.Equal("2024-01-02", receipt.Game.Started);
        Assert.Equal(125, receipt.Game.ManualPlaytimeMinutes); Assert.Null(receipt.Game.SteamAppId);
        var unchanged = await db.Games.AsNoTracking().SingleAsync(row => row.Id == retained.Id);
        Assert.Equal(unselectedUpdatedAt, unchanged.UpdatedAt); Assert.Equal(77, unchanged.Critic); Assert.Equal("OpenCritic", unchanged.CriticProvider); Assert.Equal(12, unchanged.Story);
        Assert.Equal(2, reads.Count);
    }

    private sealed class HtmlTransport(Func<HttpRequestMessage, string> html) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(html(request), System.Text.Encoding.UTF8, "text/html") });
    }
    private sealed class NamedClient(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => name == "game-durations" ? client : throw new InvalidOperationException("Unexpected named client.");
    }
    private sealed class EmptyFacts : IPublicGameMetadataProvider
    {
        public Task<List<GameMetadataIdentityDto>> SearchSteamAsync(string name, CancellationToken token) => Task.FromResult(new List<GameMetadataIdentityDto>());
        public Task<PublicGameMetadata?> SteamAsync(int appId, bool artwork, CancellationToken token) => throw new InvalidOperationException("This fixture has no Steam identity.");
        public Task<List<PublicGameMetadata>> WikidataAsync(string name, int? appId, CancellationToken token, string? selectedId = null, bool forceRefresh = false) => Task.FromResult(new List<PublicGameMetadata>());
    }
}
