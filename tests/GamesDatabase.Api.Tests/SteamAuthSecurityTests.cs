using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using GamesDatabase.Api.Application.Interfaces;
using GamesDatabase.Api.Configuration;
using GamesDatabase.Api.Contracts.Steam;
using GamesDatabase.Api.Domain.Entities;
using GamesDatabase.Api.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Primitives;
using Xunit;

namespace GamesDatabase.Api.Tests;

/// <summary>Real HTTP/auth/persistence; only Steam's external backchannel/profile use no-network fixtures.</summary>
public sealed class SteamAuthSecurityTests
{
    private const string Frontend = "https://games.test";
    private const string SteamId = "76561198000000001";
    private const string OpenIdEndpoint = "https://steamcommunity.com/openid/login";
    private const string SignedFields = "op_endpoint,claimed_id,identity,return_to,response_nonce,assoc_handle";

    private sealed class Backchannel(string body = "ns:http://specs.openid.net/auth/2.0\nis_valid:true\n", HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Assert.Equal(OpenIdEndpoint, request.RequestUri!.AbsoluteUri);
            Assert.Equal(HttpMethod.Post, request.Method);
            var form = QueryHelpers.ParseQuery(await request.Content!.ReadAsStringAsync(token));
            Assert.Equal("check_authentication", form["openid.mode"].ToString());
            Calls++;
            return new(status) { Content = new StringContent(body) };
        }
    }

    private sealed class Clients(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class SteamProfile : ISteamApiService
    {
        public Task<SteamPlayerSummaryDto?> GetPlayerSummaryAsync(string steamId) => Task.FromResult<SteamPlayerSummaryDto?>(new() { SteamId = steamId, Nickname = "Synthetic profile" });
        public Task<List<SteamOwnedGameDto>> GetOwnedGamesAsync(string steamId) => throw new InvalidOperationException("No library request is permitted in auth tests.");
        public Task<SteamPlayerAchievementsResult> GetPlayerAchievementsAsync(string steamId, int appId) => throw new InvalidOperationException("No achievement request is permitted in auth tests.");
        public Task<SteamGameSchemaDto?> GetGameSchemaAsync(int appId) => throw new InvalidOperationException("No schema request is permitted in auth tests.");
        public Task<SteamAchievementSnapshot> GetAchievementSnapshotAsync(string steamId, int appId) => throw new InvalidOperationException("No sync is permitted in auth tests.");
    }

    private static WebApplicationFactory<Program> Host(GamesDatabaseApiFactory factory, HttpClient backchannel, string configuredFrontend = Frontend) => factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
    {
        services.RemoveAll<IHttpClientFactory>();
        services.AddSingleton<IHttpClientFactory>(new Clients(backchannel));
        services.RemoveAll<ISteamApiService>();
        services.AddSingleton<ISteamApiService>(new SteamProfile());
        services.PostConfigure<SteamSettings>(settings => { settings.FrontendBaseUrl = configuredFrontend; settings.CallbackBaseUrl = "http://localhost:5011"; });
        services.PostConfigure<CorsSettings>(settings => settings.AllowedOrigins = [Frontend, "http://localhost:5173", "http://127.0.0.1:8088"]);
    }));

    private static HttpClient Client(WebApplicationFactory<Program> host) => host.CreateClient(new() { BaseAddress = new Uri(Frontend), AllowAutoRedirect = false });

    private static async Task<int> Owner(WebApplicationFactory<Program> host, HttpClient client, bool linked = true)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<GamesDbContext>();
        var owner = new User { Username = "Steam-auth-" + Guid.NewGuid().ToString("N"), Role = UserRole.Standard, SteamId = linked ? SteamId : null };
        db.Users.Add(owner); await db.SaveChangesAsync();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", scope.ServiceProvider.GetRequiredService<IAuthService>().GenerateToken(owner));
        return owner.Id;
    }

    private static async Task<Uri> Start(HttpClient client, string frontend = Frontend, string mode = "login")
    {
        var response = await client.GetAsync($"/api/auth/steam/login?mode={mode}&frontend_url={Uri.EscapeDataString(frontend)}");
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var login = response.Headers.Location!;
        Assert.Equal(OpenIdEndpoint, login.GetLeftPart(UriPartial.Path));
        return new Uri(QueryHelpers.ParseQuery(login.Query)["openid.return_to"].ToString());
    }

    private static Dictionary<string, StringValues> Assertion(Uri returnTo)
    {
        var query = QueryHelpers.ParseQuery(returnTo.Query);
        query["openid.ns"] = "http://specs.openid.net/auth/2.0";
        query["openid.mode"] = "id_res";
        query["openid.op_endpoint"] = OpenIdEndpoint;
        query["openid.claimed_id"] = "https://steamcommunity.com/openid/id/" + SteamId;
        query["openid.identity"] = query["openid.claimed_id"];
        query["openid.return_to"] = returnTo.AbsoluteUri;
        query["openid.response_nonce"] = DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'") + "synthetic";
        query["openid.assoc_handle"] = "synthetic-association";
        query["openid.signed"] = SignedFields;
        query["openid.sig"] = "synthetic-backchannel-fixture-not-a-real-signature";
        return query;
    }

    private static Task<HttpResponseMessage> Callback(HttpClient client, Dictionary<string, StringValues> query) => client.GetAsync(QueryHelpers.AddQueryString("/api/auth/steam/callback", query));

    private static async Task<int> RefreshCount(WebApplicationFactory<Program> host)
    {
        await using var scope = host.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<GamesDbContext>().RefreshTokens.CountAsync();
    }

    [Theory]
    [InlineData("hostile-frontend")]
    [InlineData("different-allowed-frontend")]
    [InlineData("signed-return-to-only")]
    [InlineData("signed-nonce")]
    [InlineData("missing-return-to")]
    [InlineData("unsigned-return-to")]
    [InlineData("unsigned-identity")]
    [InlineData("duplicate-frontend")]
    public async Task Callback_rejects_destination_or_signed_binding_tampering_before_issuing_credentials(string attack)
    {
        using var factory = new GamesDatabaseApiFactory(); var handler = new Backchannel(); using var http = new HttpClient(handler);
        using var host = Host(factory, http); using var client = Client(host); await Owner(host, client);
        var returnTo = await Start(client); var query = Assertion(returnTo); var before = await RefreshCount(host);
        switch (attack)
        {
            case "hostile-frontend":
            case "different-allowed-frontend":
                var frontend = attack == "hostile-frontend" ? "https://untrusted.invalid" : "http://localhost:5173";
                query["frontend_url"] = frontend;
                query["openid.return_to"] = QueryHelpers.AddQueryString(returnTo.GetLeftPart(UriPartial.Path), new Dictionary<string, string?> { ["nonce"] = query["nonce"].ToString(), ["frontend_url"] = frontend });
                break;
            case "signed-return-to-only": query["openid.return_to"] = "https://untrusted.invalid/api/auth/steam/callback"; break;
            case "signed-nonce": query["openid.return_to"] = returnTo.AbsoluteUri.Replace(query["nonce"].ToString(), Guid.NewGuid().ToString()); break;
            case "missing-return-to": query.Remove("openid.return_to"); break;
            case "unsigned-return-to": query["openid.signed"] = SignedFields.Replace(",return_to", ""); break;
            case "unsigned-identity": query["openid.signed"] = SignedFields.Replace("claimed_id,identity,", ""); break;
            case "duplicate-frontend": query["frontend_url"] = new StringValues([Frontend, "https://untrusted.invalid"]); break;
        }
        var response = await Callback(client, query);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(Frontend, response.Headers.Location!.GetLeftPart(UriPartial.Authority));
        Assert.False(response.Headers.Location.Fragment.Contains("code=", StringComparison.Ordinal));
        Assert.True(response.Headers.Location.Fragment.Contains("error=", StringComparison.Ordinal));
        Assert.Equal(0, handler.Calls); Assert.Equal(before, await RefreshCount(host));
    }

    [Theory]
    [InlineData(Frontend)]
    [InlineData("http://localhost:5173")]
    [InlineData("http://127.0.0.1:8088")]
    public async Task Valid_configured_destinations_keep_login_exchange_and_nonce_single_use(string frontend)
    {
        using var factory = new GamesDatabaseApiFactory(); var handler = new Backchannel(); using var http = new HttpClient(handler);
        using var host = Host(factory, http); using var client = Client(host); await Owner(host, client);
        var returnTo = await Start(client, frontend); var query = Assertion(returnTo);
        var response = await Callback(client, query); Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(frontend, response.Headers.Location!.GetLeftPart(UriPartial.Authority));
        var code = QueryHelpers.ParseQuery(response.Headers.Location.Fragment.Split('?', 2)[1])["code"].ToString(); Assert.True(Guid.TryParse(code, out _));
        using var exchange = await client.GetAsync("/api/auth/steam/exchange/" + code); exchange.EnsureSuccessStatusCode();
        var credentials = await exchange.Content.ReadFromJsonAsync<JsonElement>(); Assert.True(credentials.GetProperty("token").GetString()?.Length > 0);
        Assert.True(credentials.GetProperty("refreshToken").GetString()?.Length > 0);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/auth/steam/exchange/" + code)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Callback(client, query)).StatusCode);
        Assert.Equal(1, handler.Calls); Assert.Equal(1, await RefreshCount(host));
    }

    [Fact]
    public async Task Unknown_nonce_never_uses_an_untrusted_error_redirect()
    {
        using var factory = new GamesDatabaseApiFactory(); var handler = new Backchannel(); using var http = new HttpClient(handler);
        using var host = Host(factory, http); using var client = Client(host);
        var response = await client.GetAsync($"/api/auth/steam/callback?nonce={Guid.NewGuid()}&frontend_url=https%3A%2F%2Funtrusted.invalid");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode); Assert.Null(response.Headers.Location); Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task Initial_flow_rejects_an_unallowlisted_frontend_and_untrusted_referer_fallback()
    {
        using var factory = new GamesDatabaseApiFactory(); var handler = new Backchannel(); using var http = new HttpClient(handler);
        using var host = Host(factory, http, configuredFrontend: ""); using var client = Client(host);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/auth/steam/login?frontend_url=https%3A%2F%2Funtrusted.invalid")).StatusCode);
        client.DefaultRequestHeaders.Referrer = new Uri("https://untrusted.invalid/path");
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/auth/steam/login")).StatusCode);
        client.DefaultRequestHeaders.Referrer = new Uri("http://localhost:5173/library");
        Assert.Equal(HttpStatusCode.Redirect, (await client.GetAsync("/api/auth/steam/login")).StatusCode);
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task Link_uses_the_nonce_owner_and_destination_without_creating_a_login_session()
    {
        using var factory = new GamesDatabaseApiFactory(); var handler = new Backchannel(); using var http = new HttpClient(handler);
        using var host = Host(factory, http); using var client = Client(host); var ownerId = await Owner(host, client, linked: false);
        var link = await client.GetFromJsonAsync<JsonElement>("/api/auth/steam/link-url?frontend_url=http%3A%2F%2Flocalhost%3A5173");
        var returnTo = new Uri(QueryHelpers.ParseQuery(new Uri(link.GetProperty("url").GetString()!).Query)["openid.return_to"].ToString());
        client.DefaultRequestHeaders.Authorization = null;
        var response = await Callback(client, Assertion(returnTo)); Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("http://localhost:5173", response.Headers.Location!.GetLeftPart(UriPartial.Authority)); Assert.Contains("steamLinked=true", response.Headers.Location.Fragment);
        await using var scope = host.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<GamesDbContext>();
        Assert.Equal(SteamId, (await db.Users.SingleAsync(user => user.Id == ownerId)).SteamId);
        Assert.Empty(await db.RefreshTokens.ToListAsync()); Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task A_tampered_link_callback_cannot_change_the_nonce_owners_Steam_identity()
    {
        using var factory = new GamesDatabaseApiFactory(); var handler = new Backchannel(); using var http = new HttpClient(handler);
        using var host = Host(factory, http); using var client = Client(host); var ownerId = await Owner(host, client, linked: false);
        var returnTo = await Start(client, mode: "link"); var query = Assertion(returnTo);
        query["frontend_url"] = "https://untrusted.invalid";
        query["openid.return_to"] = QueryHelpers.AddQueryString(returnTo.GetLeftPart(UriPartial.Path), new Dictionary<string, string?> { ["nonce"] = query["nonce"].ToString(), ["frontend_url"] = "https://untrusted.invalid" });
        client.DefaultRequestHeaders.Authorization = null;
        var response = await Callback(client, query); Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(Frontend, response.Headers.Location!.GetLeftPart(UriPartial.Authority)); Assert.True(response.Headers.Location.Fragment.Contains("error=", StringComparison.Ordinal));
        await using var scope = host.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<GamesDbContext>();
        Assert.Null((await db.Users.SingleAsync(user => user.Id == ownerId)).SteamId); Assert.Empty(await db.RefreshTokens.ToListAsync()); Assert.Equal(0, handler.Calls);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/steam/link-url")).StatusCode);
    }

    [Theory]
    [InlineData("is_valid:false\n", HttpStatusCode.OK)]
    [InlineData("prefix_is_valid:true\n", HttpStatusCode.OK)]
    [InlineData("is_valid:true\n", HttpStatusCode.InternalServerError)]
    public async Task A_bound_callback_still_requires_a_successful_exact_backchannel_proof(string proof, HttpStatusCode status)
    {
        using var factory = new GamesDatabaseApiFactory(); var handler = new Backchannel(proof, status); using var http = new HttpClient(handler);
        using var host = Host(factory, http); using var client = Client(host); await Owner(host, client);
        var response = await Callback(client, Assertion(await Start(client))); Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(Frontend, response.Headers.Location!.GetLeftPart(UriPartial.Authority)); Assert.True(response.Headers.Location.Fragment.Contains("error=", StringComparison.Ordinal));
        Assert.Equal(0, await RefreshCount(host)); Assert.Equal(1, handler.Calls);
    }
}
