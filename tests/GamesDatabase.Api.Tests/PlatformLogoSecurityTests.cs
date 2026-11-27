using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using CsvHelper;
using GamesDatabase.Api.Application.Interfaces;
using GamesDatabase.Api.Common;
using GamesDatabase.Api.Contracts;
using GamesDatabase.Api.Controllers;
using GamesDatabase.Api.Domain.Entities;
using GamesDatabase.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SkiaSharp;
using Xunit;

namespace GamesDatabase.Api.Tests;

public sealed class PlatformLogoSecurityTests(GamesDatabaseApiFactory factory) : IClassFixture<GamesDatabaseApiFactory>
{
    private const string PassiveSvg = "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"24\" height=\"24\" viewBox=\"0 0 24 24\"><style>.logo{fill:#20a050}</style><defs><path id=\"mark\" d=\"M2 2h20v20H2z\"/></defs><use class=\"logo\" href=\"#mark\"/></svg>";
    public static IEnumerable<object[]> ActiveLogos()
    {
        foreach (var svg in new[]
        {
            "<svg xmlns=\"http://www.w3.org/2000/svg\"><script>localStorage.setItem('gdb-media-marker','executed')</script></svg>",
            "<svg xmlns=\"http://www.w3.org/2000/svg\" onload=\"window.__gdbSvgMarker='executed'\"/>",
            "<svg xmlns=\"http://www.w3.org/2000/svg\"><foreignObject><div xmlns=\"http://www.w3.org/1999/xhtml\" onload=\"window.__gdbSvgMarker=1\"/></foreignObject></svg>",
            "<svg xmlns=\"http://www.w3.org/2000/svg\"><a href=\"javascript:window.__gdbSvgMarker=1\">link</a></svg>",
            "<svg xmlns=\"http://www.w3.org/2000/svg\"><use href=\"https://external-fixture.invalid/image.svg#x\"/></svg>",
            "<svg xmlns=\"http://www.w3.org/2000/svg\"><set attributeName=\"onload\" to=\"window.__gdbSvgMarker=1\"/></svg>",
            "<!DOCTYPE svg [<!ENTITY test 'expansion'>]><svg xmlns=\"http://www.w3.org/2000/svg\">&test;</svg>",
            "<?xml-stylesheet type=\"text/xsl\" href=\"https://external-fixture.invalid/transform.xsl\"?><svg xmlns=\"http://www.w3.org/2000/svg\"/>"
        }) yield return [Svg(svg)];
        yield return ["data:image/png;base64," + Convert.ToBase64String(Encoding.UTF8.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\" onload=\"window.__gdbSvgMarker=1\"/>"))];
    }

    [Theory]
    [MemberData(nameof(ActiveLogos))]
    public async Task CRUD_rejects_active_or_misdeclared_SVG_and_does_not_replace_a_legitimate_logo(string malicious)
    {
        var (ownerId, client) = await Owner();
        using (client)
        {
            var safe = await client.PostAsJsonAsync("/api/gameplatforms", new { name = "Kept fixture logo", logo = Svg(PassiveSvg), isActive = true });
            Assert.Equal(HttpStatusCode.Created, safe.StatusCode);
            var saved = (await safe.Content.ReadFromJsonAsync<GamePlatformDto>())!;
            var create = await client.PostAsJsonAsync("/api/gameplatforms", new { name = "Rejected fixture logo", logo = malicious, isActive = true });
            Assert.Equal(HttpStatusCode.BadRequest, create.StatusCode);
            var update = await client.PutAsJsonAsync($"/api/gameplatforms/{saved.Id}", new { name = "Kept fixture logo", logo = malicious, isActive = true });
            Assert.Equal(HttpStatusCode.BadRequest, update.StatusCode);
            await using var scope = factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<GamesDbContext>();
            var platform = Assert.Single(await db.GamePlatforms.Where(item => item.UserId == ownerId).ToListAsync());
            Assert.Equal(Svg(PassiveSvg), platform.Logo);
        }
    }

    [Fact]
    public async Task Full_CSV_import_uses_same_validator_and_preserves_existing_logo_when_unsafe_rows_are_skipped()
    {
        var (ownerId, client) = await Owner();
        using (client)
        {
            var created = await client.PostAsJsonAsync("/api/gameplatforms", new { name = "Existing fixture", logo = Svg(PassiveSvg), isActive = true });
            created.EnsureSuccessStatusCode();
            using var text = new StringWriter();
            using (var csv = new CsvWriter(text, CultureInfo.InvariantCulture))
                csv.WriteRecords(new[]
                {
                    new FullExportModel { Type = "Platform", Name = "Existing fixture", Logo = (string)ActiveLogos().First()[0], IsActive = "True", SortOrder = "0" },
                    new FullExportModel { Type = "Platform", Name = "Unsafe new fixture", Logo = (string)ActiveLogos().ElementAt(1)[0], IsActive = "True", SortOrder = "0" },
                    new FullExportModel { Type = "Platform", Name = "Passive imported fixture", Logo = Svg(PassiveSvg), IsActive = "True", SortOrder = "1" }
                });
            using var form = new MultipartFormDataContent();
            form.Add(new ByteArrayContent(Encoding.UTF8.GetBytes(text.ToString())), "csvFile", "platform-fixtures.csv");
            var imported = await client.PostAsync("/api/DataExport/full", form);
            imported.EnsureSuccessStatusCode();
            using var result = JsonDocument.Parse(await imported.Content.ReadAsStringAsync());
            Assert.Equal(2, result.RootElement.GetProperty("errors").GetArrayLength());
            var platformsResult = result.RootElement.GetProperty("catalogs").GetProperty("platforms");
            Assert.Equal(1, platformsResult.GetProperty("imported").GetInt32());
            Assert.Equal(0, platformsResult.GetProperty("updated").GetInt32());
            await using var scope = factory.Services.CreateAsyncScope();
            var platforms = await scope.ServiceProvider.GetRequiredService<GamesDbContext>().GamePlatforms.Where(item => item.UserId == ownerId).ToListAsync();
            Assert.Equal(2, platforms.Count);
            Assert.All(platforms, platform => Assert.Equal(Svg(PassiveSvg), platform.Logo));
        }
    }

    [Theory]
    [MemberData(nameof(ActiveLogos))]
    public async Task Anonymous_logo_serving_revalidates_legacy_malicious_values_without_rewriting_them_or_returning_500(string malicious)
    {
        var id = await StoreLegacyLogo(malicious);
        using var anonymous = factory.CreateClient();
        var response = await anonymous.GetAsync($"/api/gameplatforms/{id}/logo");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("nosniff", Assert.Single(response.Headers.GetValues("X-Content-Type-Options")));
        Assert.Contains("sandbox", Assert.Single(response.Headers.GetValues("Content-Security-Policy")), StringComparison.Ordinal);
        Assert.DoesNotContain("gdb-media-marker", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        await using var scope = factory.Services.CreateAsyncScope();
        Assert.Equal(malicious, (await scope.ServiceProvider.GetRequiredService<GamesDbContext>().GamePlatforms.SingleAsync(item => item.Id == id)).Logo);
    }

    [Theory]
    [InlineData("base64")]
    [InlineData("url-encoded")]
    [InlineData("legacy-unmarked-base64")]
    public async Task Legitimate_SVG_remains_viewable_and_is_served_with_inert_document_headers(string encoding)
    {
        var logo = encoding switch
        {
            "url-encoded" => "data:image/svg+xml," + Uri.EscapeDataString(PassiveSvg),
            "legacy-unmarked-base64" => "data:image/svg+xml," + Convert.ToBase64String(Encoding.UTF8.GetBytes(PassiveSvg)),
            _ => Svg(PassiveSvg)
        };
        var (ownerId, client) = await Owner();
        using (client)
        {
            var created = await client.PostAsJsonAsync("/api/gameplatforms", new { name = "Safe SVG fixture", logo, isActive = true });
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            var id = (await created.Content.ReadFromJsonAsync<GamePlatformDto>())!.Id;
            using var anonymous = factory.CreateClient();
            var served = await anonymous.GetAsync($"/api/gameplatforms/{id}/logo");
            Assert.Equal(HttpStatusCode.OK, served.StatusCode);
            Assert.Equal("image/svg+xml", served.Content.Headers.ContentType!.MediaType);
            Assert.Equal(PassiveSvg, await served.Content.ReadAsStringAsync());
            var csp = Assert.Single(served.Headers.GetValues("Content-Security-Policy"));
            Assert.Equal(PlatformLogoContent.ContentSecurityPolicy, csp);
            Assert.Contains("sandbox;", csp, StringComparison.Ordinal);
            Assert.Contains("script-src 'none'", csp, StringComparison.Ordinal);
            Assert.Contains("default-src 'none'", csp, StringComparison.Ordinal);
            Assert.Equal("nosniff", Assert.Single(served.Headers.GetValues("X-Content-Type-Options")));
            Assert.True(served.Headers.CacheControl!.NoStore);
        }
    }

    [Fact]
    public async Task Legitimate_raster_and_bundled_presets_remain_supported_but_MIME_spoofing_does_not()
    {
        using var surface = SKSurface.Create(new SKImageInfo(4, 4));
        surface.Canvas.Clear(SKColors.Green);
        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        var png = data.ToArray();
        var logo = "data:image/png;base64," + Convert.ToBase64String(png);
        Assert.Null(PlatformLogoContent.Validate(logo));
        Assert.NotNull(PlatformLogoContent.Validate("data:image/jpeg;base64," + Convert.ToBase64String(png)));
        Assert.Null(PlatformLogoContent.Validate("/platform-icons/steam.png"));
        var id = await StoreLegacyLogo(logo);
        using var anonymous = factory.CreateClient();
        var served = await anonymous.GetAsync($"/api/gameplatforms/{id}/logo");
        Assert.Equal(HttpStatusCode.OK, served.StatusCode);
        Assert.Equal("image/png", served.Content.Headers.ContentType!.MediaType);
        Assert.Equal(png, await served.Content.ReadAsByteArrayAsync());
    }

    [Theory]
    [InlineData("broken-base64")]
    [InlineData("malformed-xml")]
    [InlineData("deep-xml")]
    [InlineData("too-long")]
    [InlineData("javascript-reference")]
    [InlineData("mime-confusion")]
    public async Task Malformed_or_oversized_saved_values_fail_closed_without_500(string kind)
    {
        var value = kind switch
        {
            "broken-base64" => "data:image/png;base64,%%!",
            "malformed-xml" => Svg("<svg xmlns=\"http://www.w3.org/2000/svg\"><g>"),
            "deep-xml" => Svg("<svg xmlns=\"http://www.w3.org/2000/svg\">" + string.Concat(Enumerable.Repeat("<g>", 80)) + string.Concat(Enumerable.Repeat("</g>", 80)) + "</svg>"),
            "too-long" => "data:image/png;base64," + new string('A', PlatformLogoContent.MaximumLength),
            "javascript-reference" => "javascript:window.__gdbSvgMarker=1",
            _ => "data:image/html;base64," + Convert.ToBase64String("<script>window.__gdbSvgMarker=1</script>"u8.ToArray())
        };
        Assert.NotNull(PlatformLogoContent.Validate(value));
        var id = await StoreLegacyLogo(value);
        using var anonymous = factory.CreateClient();
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync($"/api/gameplatforms/{id}/logo")).StatusCode);
    }

    private static string Svg(string svg) => "data:image/svg+xml;base64," + Convert.ToBase64String(Encoding.UTF8.GetBytes(svg));
    private async Task<(int OwnerId, HttpClient Client)> Owner()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<GamesDbContext>();
        var user = new User { Username = "Logo-fixture-" + Guid.NewGuid().ToString("N"), Role = UserRole.Standard };
        db.Users.Add(user); await db.SaveChangesAsync();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", scope.ServiceProvider.GetRequiredService<IAuthService>().GenerateToken(user));
        return (user.Id, client);
    }
    private async Task<int> StoreLegacyLogo(string logo)
    {
        var (ownerId, client) = await Owner(); client.Dispose();
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<GamesDbContext>();
        var platform = new GamePlatform { UserId = ownerId, Name = "Legacy saved fixture", Logo = logo, IsActive = true };
        db.GamePlatforms.Add(platform); await db.SaveChangesAsync();
        return platform.Id;
    }
}
