using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GamesDatabase.Api.Contracts;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.WebUtilities;
using Xunit;

namespace GamesDatabase.Api.Tests;

public sealed class HouseholdIntegrationTests : IClassFixture<GamesDatabaseApiFactory>
{
    private const string Verifier = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789-._~";
    private readonly GamesDatabaseApiFactory _factory;

    public HouseholdIntegrationTests(GamesDatabaseApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Authorization_code_requires_pkce_s256()
    {
        var code = await AuthorizeAsync("HouseholdUserA", new[] { "profile.read" });

        var wrong = await ExchangeCodeAsync(code, new string('x', 43));
        Assert.Equal(HttpStatusCode.BadRequest, wrong.StatusCode);

        var valid = await ExchangeCodeAsync(code, Verifier);
        Assert.Equal(HttpStatusCode.OK, valid.StatusCode);
    }

    [Fact]
    public async Task Redirect_uri_matching_is_exact_and_never_redirects_to_unregistered_uri()
    {
        var client = CreateClient();
        var token = await _factory.CreateWebTokenAsync("HouseholdUserA");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.PostAsJsonAsync("/api/integrations/household/v1/authorize", BuildAuthorizeRequest(
            new[] { "profile.read" },
            GamesDatabaseApiFactory.RedirectUri + "/"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.False(body.RootElement.TryGetProperty("redirectUrl", out _));
    }

    [Fact]
    public async Task Denial_preserves_state_and_never_issues_a_code()
    {
        var client = CreateClient();
        var token = await _factory.CreateWebTokenAsync("HouseholdUserA");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.PostAsJsonAsync(
            "/api/integrations/household/v1/authorize",
            BuildAuthorizeRequest(new[] { "profile.read" }, GamesDatabaseApiFactory.RedirectUri, approved: false));
        response.EnsureSuccessStatusCode();

        var result = (await response.Content.ReadFromJsonAsync<HouseholdAuthorizeResponse>())!;
        var query = QueryHelpers.ParseQuery(new Uri(result.RedirectUrl).Query);
        Assert.Equal("access_denied", query["error"]);
        Assert.Equal("state-value", query["state"]);
        Assert.False(query.ContainsKey("code"));
    }

    [Fact]
    public async Task Authorization_code_is_single_use()
    {
        var code = await AuthorizeAsync("HouseholdUserA", new[] { "profile.read", "games.read" });
        Assert.Equal(HttpStatusCode.OK, (await ExchangeCodeAsync(code, Verifier)).StatusCode);

        var reused = await ExchangeCodeAsync(code, Verifier);
        Assert.Equal(HttpStatusCode.BadRequest, reused.StatusCode);
        var error = await reused.Content.ReadFromJsonAsync<OAuthErrorResponse>();
        Assert.Equal("invalid_grant", error!.Error);
    }

    [Fact]
    public async Task Refresh_rotates_and_reuse_revokes_only_that_family()
    {
        var pair = await AuthorizeAndExchangeAsync("HouseholdUserA", new[] { "profile.read", "games.read" });
        var rotatedResponse = await RefreshAsync(pair.RefreshToken);
        Assert.Equal(HttpStatusCode.OK, rotatedResponse.StatusCode);
        var rotated = (await rotatedResponse.Content.ReadFromJsonAsync<HouseholdTokenResponse>())!;
        Assert.NotEqual(pair.RefreshToken, rotated.RefreshToken);
        Assert.NotEqual(pair.AccessToken, rotated.AccessToken);

        var reuse = await RefreshAsync(pair.RefreshToken);
        Assert.Equal(HttpStatusCode.BadRequest, reuse.StatusCode);

        Assert.Equal(HttpStatusCode.BadRequest, (await RefreshAsync(rotated.RefreshToken)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await GetMeAsync(rotated.AccessToken)).StatusCode);
    }

    [Fact]
    public async Task Revoke_is_idempotent_and_revokes_the_identified_connection()
    {
        var pair = await AuthorizeAndExchangeAsync("HouseholdUserA", new[] { "profile.read" });
        var client = CreateClient();

        var first = await client.PostAsJsonAsync("/api/integrations/household/v1/revoke", new
        {
            token = pair.RefreshToken,
            tokenTypeHint = "refresh_token"
        });
        var second = await client.PostAsJsonAsync("/api/integrations/household/v1/revoke", new
        {
            token = pair.RefreshToken,
            tokenTypeHint = "refresh_token"
        });

        Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, second.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await GetMeAsync(pair.AccessToken)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await RefreshAsync(pair.RefreshToken)).StatusCode);
    }

    [Fact]
    public async Task Integration_access_is_limited_by_scope_and_endpoint_allowlist()
    {
        var readOnly = await AuthorizeAndExchangeAsync("HouseholdUserA", new[] { "games.read" });
        var client = CreateClient(readOnly.AccessToken);

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/games/summary")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PatchAsJsonAsync(
            $"/api/games/{_factory.UserAGameId}/status",
            new { statusId = _factory.UserAAlternateStatusId })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/games")).StatusCode);

        var writeOnly = await AuthorizeAndExchangeAsync("HouseholdUserA", new[] { "games.status.write" });
        var writeClient = CreateClient(writeOnly.AccessToken);
        Assert.Equal(HttpStatusCode.Forbidden, (await writeClient.GetAsync("/api/games/summary")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await writeClient.PatchAsJsonAsync(
            $"/api/games/{_factory.UserAGameId}/status",
            new { statusId = _factory.UserAAlternateStatusId })).StatusCode);
    }

    [Fact]
    public async Task Connection_identity_prevents_user_a_from_accessing_user_b_data()
    {
        var pair = await AuthorizeAndExchangeAsync(
            "HouseholdUserA",
            new[] { "profile.read", "games.read", "games.status.write" });
        var client = CreateClient(pair.AccessToken);

        var summary = await client.GetFromJsonAsync<GameSummaryDto>("/api/games/summary");
        Assert.Equal(1, summary!.TotalGames);

        var otherUserPatch = await client.PatchAsJsonAsync(
            $"/api/games/{_factory.UserBGameId}/status",
            new { statusId = _factory.UserAAlternateStatusId });
        Assert.Equal(HttpStatusCode.NotFound, otherUserPatch.StatusCode);

        var me = await (await GetMeAsync(pair.AccessToken)).Content.ReadFromJsonAsync<HouseholdMeResponse>();
        Assert.Equal("HouseholdUserA", me!.Account.DisplayName);
        Assert.Equal(pair.ConnectionId, me.ConnectionId);
    }

    private async Task<HouseholdTokenResponse> AuthorizeAndExchangeAsync(string username, string[] scopes)
    {
        var code = await AuthorizeAsync(username, scopes);
        var response = await ExchangeCodeAsync(code, Verifier);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<HouseholdTokenResponse>())!;
    }

    private async Task<string> AuthorizeAsync(string username, string[] scopes)
    {
        var client = CreateClient();
        var token = await _factory.CreateWebTokenAsync(username);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.PostAsJsonAsync(
            "/api/integrations/household/v1/authorize",
            BuildAuthorizeRequest(scopes, GamesDatabaseApiFactory.RedirectUri));
        response.EnsureSuccessStatusCode();
        var result = (await response.Content.ReadFromJsonAsync<HouseholdAuthorizeResponse>())!;
        var query = QueryHelpers.ParseQuery(new Uri(result.RedirectUrl).Query);
        return query["code"].Single()!;
    }

    private static object BuildAuthorizeRequest(string[] scopes, string redirectUri, bool approved = true) => new
    {
        clientId = "household",
        redirectUri,
        state = "state-value",
        codeChallenge = WebEncoders.Base64UrlEncode(SHA256.HashData(Encoding.ASCII.GetBytes(Verifier))),
        codeChallengeMethod = "S256",
        scopes,
        approved
    };

    private async Task<HttpResponseMessage> ExchangeCodeAsync(string code, string verifier) =>
        await CreateClient().PostAsJsonAsync("/api/integrations/household/v1/token", new
        {
            grantType = "authorization_code",
            clientId = "household",
            redirectUri = GamesDatabaseApiFactory.RedirectUri,
            code,
            codeVerifier = verifier
        });

    private async Task<HttpResponseMessage> RefreshAsync(string refreshToken) =>
        await CreateClient().PostAsJsonAsync("/api/integrations/household/v1/token", new
        {
            grantType = "refresh_token",
            clientId = "household",
            refreshToken
        });

    private async Task<HttpResponseMessage> GetMeAsync(string accessToken) =>
        await CreateClient(accessToken).GetAsync("/api/integrations/household/v1/me");

    private HttpClient CreateClient(string? bearer = null)
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        if (bearer is not null)
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        }
        return client;
    }
}
