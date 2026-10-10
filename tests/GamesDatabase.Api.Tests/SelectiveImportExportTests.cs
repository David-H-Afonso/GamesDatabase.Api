using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using GamesDatabase.Api.Contracts;
using GamesDatabase.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GamesDatabase.Api.Tests;

public sealed class SelectiveImportExportTests(GamesDatabaseApiFactory factory) : IClassFixture<GamesDatabaseApiFactory>
{
    [Theory]
    [InlineData("{\"globalConfig\":null}")]
    [InlineData("{\"perGameConfig\":{\"GAME A\":null}}")]
    [InlineData("{\"globalConfig\":{\"properties\":{\"comment\":null}}}")]
    [InlineData("{\"globalConfig\":{\"mode\":null}}")]
    [InlineData("null")]
    [InlineData("{broken")]
    [InlineData("{\"perGameConfig\":{\"Game A\":{},\"GAME A\":{}}}")]
    public async Task Invalid_import_configuration_is_400_before_any_game_change(string config)
    {
        var client = await ClientAsync();
        var before = await client.GetFromJsonAsync<GameDto>($"/api/games/{factory.UserAGameId}");
        using var form = ImportForm(config, "Type,Name,Comment\nGame,Game A,Must not be saved\n");
        var response = await client.PostAsync("/api/DataExport/selective-games-import", form);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var after = await client.GetFromJsonAsync<GameDto>($"/api/games/{factory.UserAGameId}");
        Assert.Equal(before!.Comment, after!.Comment);
    }

    [Theory]
    [InlineData("\"globalConfig\":null")]
    [InlineData("\"perGameConfig\":{\"GAME_ID\":null}")]
    [InlineData("\"globalConfig\":{\"properties\":{\"comment\":null}}")]
    public async Task Null_export_configuration_is_400(string configuration)
    {
        var client = await ClientAsync();
        var json = $"{{\"gameIds\":[{factory.UserAGameId}],{configuration.Replace("GAME_ID", factory.UserAGameId.ToString())}}}";
        var response = await client.PostAsync("/api/DataExport/selective-games-export", new StringContent(json, System.Text.Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Import_overrides_match_game_names_case_insensitively()
    {
        var client = await ClientAsync();
        using var form = ImportForm("""
            {"perGameConfig":{"gAmE a":{"mode":"custom","properties":{"comment":{"mode":"custom","customValue":"Case-insensitive override"}}}}}
            """, "Type,Name,Status,Comment\nGame,GAME A,Backlog A,CSV comment\n");
        (await client.PostAsync("/api/DataExport/selective-games-import", form)).EnsureSuccessStatusCode();
        var game = await client.GetFromJsonAsync<GameDto>($"/api/games/{factory.UserAGameId}");
        Assert.Equal("Case-insensitive override", game!.Comment);
    }

    [Fact]
    public async Task Per_game_import_errors_do_not_expose_database_exception_details()
    {
        var client = await ClientAsync();
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<GamesDbContext>();
        await db.Database.ExecuteSqlRawAsync("CREATE TRIGGER selective_error_test BEFORE UPDATE ON game BEGIN SELECT RAISE(ABORT, 'PRIVATE_DATABASE_DETAIL'); END;");
        try
        {
            using var form = ImportForm("{}", "Type,Name,Status,Comment\nGame,Game A,Backlog A,Trigger error\n");
            var response = await client.PostAsync("/api/DataExport/selective-games-import", form);
            response.EnsureSuccessStatusCode();
            var result = await response.Content.ReadFromJsonAsync<SelectiveImportResult>();
            Assert.Single(result!.Errors);
            Assert.DoesNotContain("PRIVATE_DATABASE_DETAIL", await response.Content.ReadAsStringAsync());
            Assert.DoesNotContain("SQLite", await response.Content.ReadAsStringAsync());
        }
        finally
        {
            await db.Database.ExecuteSqlRawAsync("DROP TRIGGER selective_error_test;");
        }
    }

    [Fact]
    public async Task Full_export_import_roundtrip_preserves_critic_provider()
    {
        var client = await ClientAsync();
        (await client.PutAsJsonAsync($"/api/games/{factory.UserAGameId}", new { criticProvider = "OpenCritic" })).EnsureSuccessStatusCode();
        var exported = await client.GetAsync("/api/DataExport/full");
        exported.EnsureSuccessStatusCode();
        var csv = await exported.Content.ReadAsStringAsync();
        Assert.Contains("OpenCritic", csv);
        (await client.PutAsJsonAsync($"/api/games/{factory.UserAGameId}", new { criticProvider = "Metacritic" })).EnsureSuccessStatusCode();
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent(csv, System.Text.Encoding.UTF8, "text/csv"), "csvFile", "roundtrip.csv");
        (await client.PostAsync("/api/DataExport/full", form)).EnsureSuccessStatusCode();
        var game = await client.GetFromJsonAsync<GameDto>($"/api/games/{factory.UserAGameId}");
        Assert.Equal("OpenCritic", game!.CriticProvider);
    }

    private async Task<HttpClient> ClientAsync()
    {
        var token = await factory.CreateWebTokenAsync("HouseholdUserA");
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static MultipartFormDataContent ImportForm(string configuration, string csv)
    {
        var form = new MultipartFormDataContent();
        form.Add(new StringContent(csv), "csvText");
        form.Add(new StringContent(configuration), "configJson");
        return form;
    }
}
