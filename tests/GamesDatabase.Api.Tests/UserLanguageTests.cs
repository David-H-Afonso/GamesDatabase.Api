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

public sealed class UserLanguageTests(GamesDatabaseApiFactory factory) : IClassFixture<GamesDatabaseApiFactory>
{
    private sealed record Account(HttpClient Client, int Id, string Username);

    private async Task<Account> CreateAccount(UserRole role = UserRole.Standard, string? language = null)
    {
        await factory.EnsureSeededAsync();
        var username = "Language-" + Guid.NewGuid().ToString("N");
        int id; string token;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<GamesDbContext>();
            var user = new User { Username = username, Role = role, PreferredLanguage = language };
            db.Users.Add(user); await db.SaveChangesAsync(); id = user.Id;
            token = scope.ServiceProvider.GetRequiredService<IAuthService>().GenerateToken(user);
        }
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return new(client, id, username);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("es")]
    public async Task Admin_creation_exposes_the_assigned_language_in_login_profile_and_user_list_without_adding_examples(string language)
    {
        var admin = await CreateAccount(UserRole.Admin);
        var username = "Created-language-" + Guid.NewGuid().ToString("N");
        var created = await admin.Client.PostAsJsonAsync("/api/users", new CreateUserRequest { Username = username, PreferredLanguage = language, TutorialEnabled = true });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var user = (await created.Content.ReadFromJsonAsync<UserDto>())!;
        Assert.Equal(language, user.PreferredLanguage); Assert.True(user.TutorialEnabled);
        var login = await factory.CreateClient().PostAsJsonAsync("/api/users/login", new { username });
        login.EnsureSuccessStatusCode(); var session = (await login.Content.ReadFromJsonAsync<LoginResponse>())!;
        Assert.Equal(language, session.PreferredLanguage);
        var client = factory.CreateClient(); client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session.Token);
        Assert.Equal(language, (await client.GetFromJsonAsync<UserDto>($"/api/users/{user.Id}"))!.PreferredLanguage);
        Assert.Equal(language, (await admin.Client.GetFromJsonAsync<List<UserDto>>("/api/users"))!.Single(value => value.Id == user.Id).PreferredLanguage);
        var guide = (await client.GetFromJsonAsync<OnboardingStateDto>("/api/onboarding"))!;
        Assert.True(guide.Enabled); Assert.Equal("welcome", guide.Step); Assert.Empty(guide.SampleGames);
        var catalog = await client.GetFromJsonAsync<JsonElement>("/api/gamestatus/active");
        var labels = catalog.EnumerateArray().Select(value => value.GetProperty("name").GetString()).ToArray();
        Assert.Contains(language == "es" ? "Jugando" : "Playing", labels);
        (await client.PutAsJsonAsync($"/api/users/{user.Id}", new { preferredLanguage = language == "es" ? "en" : "es" })).EnsureSuccessStatusCode();
        var unchangedCatalog = await client.GetFromJsonAsync<JsonElement>("/api/gamestatus/active");
        Assert.Equal(labels, unchangedCatalog.EnumerateArray().Select(value => value.GetProperty("name").GetString()).ToArray());
        await using var after = factory.Services.CreateAsyncScope();
        var db = after.ServiceProvider.GetRequiredService<GamesDbContext>();
        Assert.False(await db.Games.AnyAsync(game => game.UserId == user.Id));
        Assert.False(await db.Playlists.AnyAsync(list => list.UserId == user.Id));
    }

    [Theory]
    [InlineData("en", "es")]
    [InlineData("es", "en")]
    public async Task Owner_and_admin_can_override_each_other_and_unrelated_edits_do_not_reapply_an_old_language(string first, string second)
    {
        var admin = await CreateAccount(UserRole.Admin); var owner = await CreateAccount(language: first); var other = await CreateAccount(language: first);
        (await owner.Client.PutAsJsonAsync($"/api/users/{owner.Id}", new UpdateUserRequest { PreferredLanguage = second })).EnsureSuccessStatusCode();
        Assert.Equal(second, (await admin.Client.GetFromJsonAsync<UserDto>($"/api/users/{owner.Id}"))!.PreferredLanguage);
        (await admin.Client.PutAsJsonAsync($"/api/users/{owner.Id}", new UpdateUserRequest { PreferredLanguage = first })).EnsureSuccessStatusCode();
        Assert.Equal(first, (await owner.Client.GetFromJsonAsync<UserDto>($"/api/users/{owner.Id}"))!.PreferredLanguage);
        (await owner.Client.PutAsJsonAsync($"/api/users/{owner.Id}", new UpdateUserRequest { PreferredLanguage = second })).EnsureSuccessStatusCode();
        (await admin.Client.PutAsJsonAsync($"/api/users/{owner.Id}", new UpdateUserRequest { TutorialEnabled = true })).EnsureSuccessStatusCode();
        Assert.Equal(second, (await owner.Client.GetFromJsonAsync<UserDto>($"/api/users/{owner.Id}"))!.PreferredLanguage);
        Assert.Equal(first, (await other.Client.GetFromJsonAsync<UserDto>($"/api/users/{other.Id}"))!.PreferredLanguage);
        var login = await factory.CreateClient().PostAsJsonAsync("/api/users/login", new { username = owner.Username });
        login.EnsureSuccessStatusCode(); Assert.Equal(second, (await login.Content.ReadFromJsonAsync<LoginResponse>())!.PreferredLanguage);
    }

    [Theory]
    [InlineData("")]
    [InlineData("fr")]
    [InlineData("ES")]
    [InlineData("es-ES")]
    [InlineData("<script>")]
    public async Task Unsupported_language_rejects_create_and_update_without_partial_user_changes(string invalid)
    {
        var admin = await CreateAccount(UserRole.Admin); var owner = await CreateAccount(language: "es");
        var create = await admin.Client.PostAsJsonAsync("/api/users", new { username = "Rejected-language-" + Guid.NewGuid().ToString("N"), preferredLanguage = invalid });
        Assert.Equal(HttpStatusCode.BadRequest, create.StatusCode);
        var update = await admin.Client.PutAsJsonAsync($"/api/users/{owner.Id}", new { username = "Should not be saved", preferredLanguage = invalid, tutorialEnabled = true });
        Assert.Equal(HttpStatusCode.BadRequest, update.StatusCode);
        var after = (await owner.Client.GetFromJsonAsync<UserDto>($"/api/users/{owner.Id}"))!;
        Assert.Equal(owner.Username, after.Username); Assert.Equal("es", after.PreferredLanguage); Assert.False(after.TutorialEnabled);
    }

    [Fact]
    public async Task A_standard_user_cannot_create_accounts_or_change_another_users_language_and_anonymous_updates_are_rejected()
    {
        var owner = await CreateAccount(language: "en"); var other = await CreateAccount(language: "es");
        Assert.Equal(HttpStatusCode.Forbidden, (await owner.Client.PutAsJsonAsync($"/api/users/{other.Id}", new { preferredLanguage = "en" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await owner.Client.PostAsJsonAsync("/api/users", new { username = "Forbidden-language-create", preferredLanguage = "en" })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().PutAsJsonAsync($"/api/users/{other.Id}", new { preferredLanguage = "en" })).StatusCode);
        Assert.Equal("es", (await other.Client.GetFromJsonAsync<UserDto>($"/api/users/{other.Id}"))!.PreferredLanguage);
    }

    [Fact]
    public async Task Existing_accounts_without_an_assignment_keep_null_until_an_explicit_choice()
    {
        var owner = await CreateAccount();
        var response = await owner.Client.GetAsync($"/api/users/{owner.Id}"); response.EnsureSuccessStatusCode();
        Assert.True(response.Headers.CacheControl?.NoStore);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(json.TryGetProperty("preferredLanguage", out var language)); Assert.Equal(JsonValueKind.Null, language.ValueKind);
        Assert.Null((await owner.Client.GetFromJsonAsync<UserDto>($"/api/users/{owner.Id}"))!.PreferredLanguage);
        (await owner.Client.PutAsJsonAsync($"/api/users/{owner.Id}", new { useScoreColors = true })).EnsureSuccessStatusCode();
        Assert.Null((await owner.Client.GetFromJsonAsync<UserDto>($"/api/users/{owner.Id}"))!.PreferredLanguage);
        (await owner.Client.PutAsJsonAsync($"/api/users/{owner.Id}", new { preferredLanguage = "es" })).EnsureSuccessStatusCode();
        var profile = (await owner.Client.GetFromJsonAsync<UserDto>($"/api/users/{owner.Id}"))!;
        Assert.Equal("es", profile.PreferredLanguage); Assert.True(profile.UseScoreColors);
    }
}
