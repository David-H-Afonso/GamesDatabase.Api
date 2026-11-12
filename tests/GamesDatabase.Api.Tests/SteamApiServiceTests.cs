using System.Net;
using System.Text;
using System.Text.Json;
using GamesDatabase.Api.Application.Services.Steam;
using GamesDatabase.Api.Configuration;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace GamesDatabase.Api.Tests;

public sealed class SteamApiServiceTests
{
    [Fact]
    public async Task Reads_public_sets_sparse_unlocks_zero_internal_key_and_large_schema_hash()
    {
        var snapshot = SteamAchievementFixtures.Snapshot([2, 1], [1, 0], 0);
        snapshot.Schema!.SchemaHash = snapshot.Player!.SchemaHash = 4_000_000_000;
        using var api = new ApiHarness(path => Json(path.Contains("GetGameAchievements") ? snapshot.Schema : snapshot.Player));

        var result = await api.Service.GetAchievementSnapshotAsync("76561198000000000", 620);

        Assert.True(result.Success);
        Assert.True(result.SupportsSets);
        Assert.Equal(3, result.Schema!.Achievements!.Count);
        Assert.Equal(0, Assert.Single(result.Player!.Achievements!).InternalKey);
        Assert.True(result.Player.Groups![0].IsCompleted);
        Assert.All(api.Paths, path => Assert.DoesNotContain("key=", path, StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData(15, "profilePrivate")]
    [InlineData(2, "steamUnavailable")]
    public async Task Http_200_does_not_override_a_failed_eresult(int resultCode, string error)
    {
        var snapshot = SteamAchievementFixtures.Snapshot([1], [1]);
        using var api = new ApiHarness(path => path.Contains("GetGameAchievements") ? Json(snapshot.Schema) : Json(snapshot.Player, resultCode));
        var result = await api.Service.GetAchievementSnapshotAsync("76561198000000000", 620);
        Assert.False(result.Success);
        Assert.Equal(error, result.Error);
    }

    [Fact]
    public async Task An_empty_player_envelope_is_not_a_zero_progress_snapshot()
    {
        var snapshot = SteamAchievementFixtures.Snapshot([2], [0]);
        using var api = new ApiHarness(path => path.Contains("GetGameAchievements") ? Json(snapshot.Schema) : Json(new { }));
        var result = await api.Service.GetAchievementSnapshotAsync("76561198000000000", 620);
        Assert.False(result.Success);
        Assert.Equal("playerUnavailable", result.Error);
    }

    [Fact]
    public async Task An_incomplete_schema_cannot_retire_cached_achievements()
    {
        var snapshot = SteamAchievementFixtures.Snapshot([2], [0]);
        snapshot.Schema!.Achievements!.Clear();
        using var api = new ApiHarness(_ => Json(snapshot.Schema));
        var result = await api.Service.GetAchievementSnapshotAsync("76561198000000000", 620);
        Assert.False(result.Success);
        Assert.Single(api.Paths);
    }

    [Fact]
    public async Task Explicit_no_schema_result_is_a_confirmed_no_achievements_case()
    {
        using var api = new ApiHarness(_ => Json(new { }, 42));
        var result = await api.Service.GetAchievementSnapshotAsync("76561198000000000", 10);
        Assert.True(result.Success);
        Assert.True(result.NoAchievements);
        Assert.Single(api.Paths);
    }

    [Fact]
    public async Task Refreshes_a_cached_schema_once_when_the_player_hash_changes()
    {
        var old = SteamAchievementFixtures.Snapshot([1], [1]);
        var current = SteamAchievementFixtures.Snapshot([2], [1], 0);
        current.Schema!.SchemaVersion = current.Player!.SchemaVersion = 2;
        current.Schema.SchemaHash = current.Player.SchemaHash = 200;
        var schemaRequests = 0;
        using var api = new ApiHarness(path => path.Contains("GetGameAchievements") ? Json(++schemaRequests == 1 ? old.Schema : current.Schema) : Json(current.Player));

        var result = await api.Service.GetAchievementSnapshotAsync("76561198000000000", 620);

        Assert.True(result.Success);
        Assert.Equal(2, schemaRequests);
        Assert.Equal(2, result.Schema!.Achievements!.Count);
    }

    [Fact]
    public async Task Persistent_schema_mismatch_fails_without_infinite_retries()
    {
        var snapshot = SteamAchievementFixtures.Snapshot([1], [1]);
        snapshot.Player!.SchemaHash = 999;
        using var api = new ApiHarness(path => Json(path.Contains("GetGameAchievements") ? snapshot.Schema : snapshot.Player));
        var result = await api.Service.GetAchievementSnapshotAsync("76561198000000000", 620);
        Assert.False(result.Success);
        Assert.Equal("schemaChanged", result.Error);
        Assert.Equal(4, api.Paths.Count);
    }

    [Fact]
    public async Task Rejects_unlock_keys_that_are_not_in_the_matching_schema()
    {
        var snapshot = SteamAchievementFixtures.Snapshot([1], [1]);
        snapshot.Player!.Achievements![0].InternalKey = 999;
        using var api = new ApiHarness(path => Json(path.Contains("GetGameAchievements") ? snapshot.Schema : snapshot.Player));
        var result = await api.Service.GetAchievementSnapshotAsync("76561198000000000", 620);
        Assert.False(result.Success);
        Assert.Equal("invalidPlayerSnapshot", result.Error);
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests, "rateLimited")]
    [InlineData(HttpStatusCode.Forbidden, "profilePrivate")]
    [InlineData(HttpStatusCode.ServiceUnavailable, "steamUnavailable")]
    public async Task Upstream_errors_are_explicit_and_do_not_trigger_unsafe_fallback(HttpStatusCode status, string error)
    {
        using var api = new ApiHarness(_ => new(status));
        var result = await api.Service.GetAchievementSnapshotAsync("76561198000000000", 620);
        Assert.False(result.Success);
        Assert.Equal(error, result.Error);
        Assert.Single(api.Paths);
    }

    [Fact]
    public async Task Unsupported_methods_fall_back_to_the_documented_legacy_endpoints()
    {
        using var api = new ApiHarness(path =>
        {
            if (path.Contains("IPlayerService")) return new(HttpStatusCode.NotFound);
            if (path.Contains("GetPlayerAchievements")) return Raw("""{"playerstats":{"success":true,"achievements":[{"apiname":"OLD","achieved":1,"unlocktime":1700000000}]}}""");
            return Raw("""{"game":{"availableGameStats":{"achievements":[{"name":"OLD","displayName":"Old achievement","hidden":0}]}}}""");
        });
        var result = await api.Service.GetAchievementSnapshotAsync("76561198000000000", 620);
        Assert.True(result.Success);
        Assert.False(result.SupportsSets);
        Assert.Equal("OLD", Assert.Single(result.LegacyAchievements).ApiName);
        Assert.Equal(3, api.Paths.Count);
    }

    [Fact]
    public async Task Malformed_json_is_not_interpreted_as_an_empty_schema()
    {
        using var api = new ApiHarness(_ => Raw("not-json"));
        var result = await api.Service.GetAchievementSnapshotAsync("76561198000000000", 620);
        Assert.False(result.Success);
    }

    private static HttpResponseMessage Json(object? response, int result = 1)
    {
        var message = Raw(JsonSerializer.Serialize(new { response }));
        message.Headers.Add("x-eresult", result.ToString());
        return message;
    }

    private static HttpResponseMessage Raw(string json) => new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private sealed class ApiHarness : IDisposable, IHttpClientFactory
    {
        private readonly HttpClient client;
        private readonly MemoryCache cache = new(new MemoryCacheOptions());
        public List<string> Paths { get; } = [];
        public SteamApiService Service { get; }
        public ApiHarness(Func<string, HttpResponseMessage> respond)
        {
            client = new HttpClient(new Handler(request =>
            {
                var path = request.RequestUri!.PathAndQuery;
                Paths.Add(path);
                return respond(path);
            }));
            Service = new(this, Options.Create(new SteamSettings { ApiBaseUrl = "https://steam.test", ApiKey = "test-only-key" }), NullLogger<SteamApiService>.Instance, cache);
        }
        public HttpClient CreateClient(string name) => client;
        public void Dispose() { client.Dispose(); cache.Dispose(); }
    }

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(respond(request));
    }
}
