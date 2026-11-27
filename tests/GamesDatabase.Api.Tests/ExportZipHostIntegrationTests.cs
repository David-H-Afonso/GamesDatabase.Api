using System.Globalization;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using CsvHelper;
using GamesDatabase.Api.Application.Interfaces;
using GamesDatabase.Api.Application.Services;
using GamesDatabase.Api.Configuration;
using GamesDatabase.Api.Domain.Entities;
using GamesDatabase.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GamesDatabase.Api.Tests;

// Actual Program/DI/authentication/controller/CSV/ZIP with the isolated SQLite
// factory. Artwork is absent, so no remote API, NAS, or owner server is contacted.
public sealed class ExportZipHostIntegrationTests(GamesDatabaseApiFactory factory) : IClassFixture<GamesDatabaseApiFactory>
{
    [Fact]
    public async Task Production_service_registration_resolves_the_dedicated_media_transport_and_ZIP_service()
    {
        await factory.EnsureSeededAsync();
        await using var scope = factory.Services.CreateAsyncScope();
        Assert.IsType<ZipExportService>(scope.ServiceProvider.GetRequiredService<IZipExportService>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<ExportMediaDownloader>());
        using var client = scope.ServiceProvider.GetRequiredService<IHttpClientFactory>().CreateClient(ExportMediaHttpClient.Name);
        Assert.Null(client.DefaultRequestHeaders.Authorization);
    }

    [Fact]
    public async Task Anonymous_ZIP_request_is_rejected_by_real_authentication_middleware()
    {
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/api/export/zip?full=true");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("HouseholdUserA", "Game A", "Game B")]
    [InlineData("HouseholdUserB", "Game B", "Game A")]
    public async Task Authenticated_ZIP_route_ignores_forged_host_and_exports_only_the_current_owner(string username, string ownedName, string foreignName)
    {
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await factory.CreateWebTokenAsync(username));
        await using var beforeScope = factory.Services.CreateAsyncScope();
        var db = beforeScope.ServiceProvider.GetRequiredService<GamesDbContext>();
        var foreignBefore = await db.Games.AsNoTracking().SingleAsync(game => game.Name == foreignName);

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/export/zip?full=true");
        request.Headers.Host = "169.254.169.254";
        request.Headers.Add("X-Forwarded-Host", "127.0.0.1:9");
        request.Headers.Add("X-Forwarded-Proto", "http");
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/zip", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("1", Assert.Single(response.Headers.GetValues("X-Export-Total-Games")));
        Assert.Equal("0", Assert.Single(response.Headers.GetValues("X-Export-Images-Downloaded")));

        using var archive = new ZipArchive(new MemoryStream(await response.Content.ReadAsByteArrayAsync()));
        var backup = Assert.Single(archive.Entries, entry => entry.FullName.EndsWith(".csv", StringComparison.Ordinal));
        using var text = new StreamReader(backup.Open());
        using var csv = new CsvReader(text, CultureInfo.InvariantCulture);
        var game = Assert.Single(csv.GetRecords<ExportRecord>(), record => record.Type == "Game");
        Assert.Equal(ownedName, game.Name);
        Assert.Contains(archive.Entries, entry => entry.FullName.EndsWith("/info.json", StringComparison.Ordinal));
        Assert.DoesNotContain(archive.Entries, entry => entry.FullName.Contains(foreignName.Replace(' ', '_'), StringComparison.Ordinal));

        var foreignAfter = await db.Games.AsNoTracking().SingleAsync(game => game.Id == foreignBefore.Id);
        Assert.Equal(foreignBefore.ModifiedSinceExport, foreignAfter.ModifiedSinceExport);
        Assert.Equal(foreignBefore.UpdatedAt, foreignAfter.UpdatedAt);
        Assert.Equal(foreignBefore.Name, foreignAfter.Name);
        Assert.Equal(foreignBefore.Cover, foreignAfter.Cover);
    }
}
