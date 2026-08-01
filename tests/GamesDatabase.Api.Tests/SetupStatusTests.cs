using System.Net;
using System.Net.Http.Json;
using GamesDatabase.Api.Domain.Entities;
using GamesDatabase.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GamesDatabase.Api.Tests;

public sealed class SetupStatusTests : IClassFixture<GamesDatabaseApiFactory>
{
    private readonly GamesDatabaseApiFactory _factory;

    public SetupStatusTests(GamesDatabaseApiFactory factory)
    {
        _factory = factory;
    }

    private sealed record SetupStatusResponse(bool DefaultCredentialsAvailable, string? DefaultUsername);

    [Fact]
    public async Task SetupStatus_reflects_default_admin_password_state()
    {
        var client = _factory.CreateClient();

        // Fresh install: the seeded default admin has no password, so the
        // bootstrap credentials may be offered and the username is exposed.
        var fresh = await client.GetFromJsonAsync<SetupStatusResponse>("/api/users/setup-status");
        Assert.NotNull(fresh);
        Assert.True(fresh!.DefaultCredentialsAvailable);
        Assert.False(string.IsNullOrEmpty(fresh.DefaultUsername));

        // Configure the installation by giving the default admin a password.
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<GamesDbContext>();
            var admin = await context.Users.FirstAsync(u => u.IsDefault && u.Role == UserRole.Admin);
            admin.PasswordHash = "$2a$12$abcdefghijklmnopqrstuv"; // any non-null hash
            await context.SaveChangesAsync();
        }

        // Configured install: no bootstrap credentials, no username disclosure.
        var configured = await client.GetFromJsonAsync<SetupStatusResponse>("/api/users/setup-status");
        Assert.NotNull(configured);
        Assert.False(configured!.DefaultCredentialsAvailable);
        Assert.Null(configured.DefaultUsername);
    }

    [Fact]
    public async Task SetupStatus_is_anonymous()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/users/setup-status");
        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
