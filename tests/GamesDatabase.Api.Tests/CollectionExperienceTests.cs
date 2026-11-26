using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using GamesDatabase.Api.Application.Services;
using GamesDatabase.Api.Common;
using GamesDatabase.Api.Contracts;
using GamesDatabase.Api.Domain.Entities;
using GamesDatabase.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GamesDatabase.Api.Tests;

public sealed class NameSearchContractTests
{
    [Fact]
    public void Local_test_backups_cannot_use_an_imported_production_destination()
    {
        var schedule = new BackupSchedule { UserId = 7, DestinationPath = "\\\\server\\production-backups" };
        var root = Path.Combine(Path.GetTempPath(), "gdb-local-exports");
        Assert.Equal(Path.Combine(Path.GetFullPath(root), "backups", "7"), BackupScheduleService.ResolveDestination(schedule, true, root));
        Assert.Equal(schedule.DestinationPath, BackupScheduleService.ResolveDestination(schedule, false, null));
        Assert.Throws<InvalidOperationException>(() => BackupScheduleService.ResolveDestination(schedule, true, "\\\\server\\share"));
        Assert.Throws<InvalidOperationException>(() => BackupScheduleService.ResolveDestination(schedule, true, null));
    }

    [Theory]
    [InlineData("Pokémon Écarlate", "pokemon", false, false, true)]
    [InlineData("Pokémon Écarlate", "pokemon", false, true, false)]
    [InlineData("Pokémon", "Pokemon", true, false, true)]
    [InlineData("Pokémon", "pokemon", true, false, false)]
    [InlineData("Poke\u0301mon", "Pokémon", true, true, true)]
    [InlineData("日本語のゲーム", "日本語", false, false, true)]
    [InlineData("Literal .* ? [term]", ".* ? [term]", false, false, true)]
    public void Literal_normalization_contract(string title, string phrase, bool caseSensitive, bool accentSensitive, bool expected) =>
        Assert.Equal(expected, NameSearchMatching.Matches(title, new() { IncludeTerms = [phrase], CaseSensitive = caseSensitive, AccentSensitive = accentSensitive }));

    [Fact]
    public void Any_all_exclusions_empty_and_exact_are_explicit()
    {
        var rule = new NameSearchDto { IncludeTerms = ["metal gear", "solid"], MatchMode = "all" };
        Assert.True(NameSearchMatching.Matches("METAL GEAR SOLID", rule));
        rule.IncludeTerms.Add("zelda"); Assert.False(NameSearchMatching.Matches("METAL GEAR SOLID", rule));
        rule.MatchMode = "any"; Assert.True(NameSearchMatching.Matches("METAL GEAR SOLID", rule));
        rule.ExcludeTerms = ["solid", "other"]; Assert.False(NameSearchMatching.Matches("METAL GEAR SOLID", rule));
        rule.IncludeTerms = []; Assert.False(NameSearchMatching.Matches("METAL GEAR SOLID", rule));
        Assert.True(NameSearchMatching.Matches("unrelated", rule));
        Assert.False(NameSearchMatching.Matches("Factorio: Space Age", new() { IncludeTerms = ["Factorio"], MatchType = "exact" }));
        Assert.True(NameSearchMatching.Matches("Factorio", new() { IncludeTerms = ["factorio"], MatchType = "exact" }));
    }

    [Theory]
    [InlineData("2026-02-31", null)]
    [InlineData("2026-03-01garbage", null)]
    [InlineData("2026-03-01", "2026-03-01")]
    [InlineData("2026-03-01 12:00:00", "2026-03-01")]
    public void Only_valid_stored_dates_are_activity(string input, string? expected) => Assert.Equal(expected, CollectionService.IsoDate(input));

    [Fact]
    public void Invalid_search_payloads_fail_validation()
    {
        Assert.NotNull(NameSearchMatching.Validate(new() { Version = 2 }));
        Assert.NotNull(NameSearchMatching.Validate(new() { MatchMode = "sql" }));
        Assert.NotNull(NameSearchMatching.Validate(new() { IncludeTerms = Enumerable.Repeat("x", 21).ToList() }));
        Assert.NotNull(NameSearchMatching.Validate(new() { ExcludeTerms = [new string('x', 251)] }));
        Assert.Throws<ArgumentException>(() => NameSearchMatching.Read("null"));
        Assert.Throws<ArgumentException>(() => NameSearchMatching.Read(new string(' ', 16001)));
    }
}

public sealed class CollectionExperienceTests(GamesDatabaseApiFactory factory) : IClassFixture<GamesDatabaseApiFactory>
{
    private async Task<HttpClient> Client(string username = "HouseholdUserA")
    {
        var token = await factory.CreateWebTokenAsync(username);
        var client = factory.CreateClient(); client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token); return client;
    }

    private async Task<GameDto> CreateGame(HttpClient client, string title)
    {
        var response = await client.PostAsJsonAsync("/api/games", new { name = title + " " + Guid.NewGuid().ToString("N"), statusId = factory.UserAInitialStatusId });
        response.EnsureSuccessStatusCode(); return (await response.Content.ReadFromJsonAsync<GameDto>())!;
    }

    [Fact]
    public async Task All_new_collection_routes_require_authentication()
    {
        await factory.EnsureSeededAsync(); var client = factory.CreateClient();
        foreach (var route in new[] { "/api/home/config", "/api/collection/statistics", "/api/collection/games", "/api/collection/goty" }) Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(route)).StatusCode);
    }

    [Fact]
    public async Task Reading_home_does_not_create_preferences_and_empty_rows_are_preserved()
    {
        var client = await Client();
        int before;
        await using (var scope = factory.Services.CreateAsyncScope()) before = await scope.ServiceProvider.GetRequiredService<GamesDbContext>().CollectionPreferences.CountAsync();
        var home = await client.GetFromJsonAsync<HomeConfigDto>("/api/home/config"); Assert.Equal(1, home!.Version);
        await using (var scope = factory.Services.CreateAsyncScope()) Assert.Equal(before, await scope.ServiceProvider.GetRequiredService<GamesDbContext>().CollectionPreferences.CountAsync());
        var response = await client.PutAsJsonAsync("/api/home/config", new HomeConfigDto { Revision = home.Revision, Rows = [] }); response.EnsureSuccessStatusCode();
        Assert.Empty((await client.GetFromJsonAsync<HomeConfigDto>("/api/home/config"))!.Rows);
    }

    [Fact]
    public async Task Home_save_uses_revision_and_rejects_foreign_sources_without_overwriting_other_users()
    {
        var owner = await Client(); var other = await Client("HouseholdUserB");
        var foreignResponse = await other.PostAsJsonAsync("/api/playlists", new PlaylistCreateDto { Name = "Foreign " + Guid.NewGuid() }); foreignResponse.EnsureSuccessStatusCode();
        var foreign = (await foreignResponse.Content.ReadFromJsonAsync<PlaylistDto>())!;
        var home = (await owner.GetFromJsonAsync<HomeConfigDto>("/api/home/config"))!;
        var invalid = await owner.PutAsJsonAsync("/api/home/config", new HomeConfigDto { Revision = home.Revision, Rows = [new() { Id = "foreign", Source = new() { Kind = "playlist", PlaylistId = foreign.Id } }] });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        var request = new HomeConfigDto { Revision = home.Revision, Rows = [new() { Id = "summary-owned", Source = new() { Kind = "summary" }, Title = "My summary" }] };
        var saved = await owner.PutAsJsonAsync("/api/home/config", request); saved.EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Conflict, (await owner.PutAsJsonAsync("/api/home/config", request)).StatusCode);
        Assert.DoesNotContain((await other.GetFromJsonAsync<HomeConfigDto>("/api/home/config"))!.Rows, row => row.Id == "summary-owned");
    }

    [Fact]
    public async Task Row_resolution_is_read_only_keeps_sources_independent_and_does_not_change_capacity()
    {
        var client = await Client(); var first = await CreateGame(client, "First row game"); var second = await CreateGame(client, "Second row game");
        var response = await client.PostAsJsonAsync("/api/playlists", new PlaylistCreateDto { Name = "Row source " + Guid.NewGuid() }); response.EnsureSuccessStatusCode();
        var list = (await response.Content.ReadFromJsonAsync<PlaylistDto>())!;
        (await client.PostAsJsonAsync($"/api/playlists/{list.Id}/items", new { gameId = first.Id })).EnsureSuccessStatusCode();
        (await client.PostAsJsonAsync($"/api/playlists/{list.Id}/items", new { gameId = second.Id })).EnsureSuccessStatusCode();
        var resolved = await client.PostAsJsonAsync("/api/home/rows/resolve", new HomeResolveRequest { Rows = [new() { Id = "existing", PreviewCount = 1, Source = new() { Kind = "playlist", PlaylistId = list.Id } }, new() { Id = "missing", Source = new() { Kind = "playlist", PlaylistId = int.MaxValue } }] });
        resolved.EnsureSuccessStatusCode(); var rows = (await resolved.Content.ReadFromJsonAsync<List<HomeRowResultDto>>())!;
        Assert.Equal(2, rows[0].TotalMatches); Assert.Equal(first.Id, Assert.Single(rows[0].Items).Id); Assert.True(rows[0].HasMore);
        Assert.Equal("unavailable", rows[1].State); Assert.Empty(rows[1].Items);
        var after = (await client.GetFromJsonAsync<PlaylistDto>($"/api/playlists/{list.Id}"))!; Assert.Null(after.MaxLength); Assert.Equal(2, after.Items.Count);
    }

    [Fact]
    public async Task Metrics_search_the_complete_owned_set_and_GOTY_includes_only_eligible_years()
    {
        var client = await Client();
        var prefix = "Metric Pokémon " + Guid.NewGuid().ToString("N");
        var first = await CreateGame(client, prefix); var second = await CreateGame(client, prefix); var third = await CreateGame(client, prefix);
        (await client.PutAsJsonAsync($"/api/games/{first.Id}", new { finished = "2026-03-01", manualPlaytimeMinutes = 0 })).EnsureSuccessStatusCode();
        (await client.PutAsJsonAsync($"/api/games/{second.Id}", new { started = "2026-02-01", manualPlaytimeMinutes = 60 })).EnsureSuccessStatusCode();
        (await client.PutAsJsonAsync($"/api/games/{third.Id}", new { finished = "2025-03-01", manualPlaytimeMinutes = 120 })).EnsureSuccessStatusCode();
        var search = Uri.EscapeDataString(prefix.Replace("Pokémon", "Pokemon"));
        var timed = (await client.GetFromJsonAsync<PagedResult<GameDto>>($"/api/collection/games?metric=timed&search={search}&pageSize=1"))!;
        var next = (await client.GetFromJsonAsync<PagedResult<GameDto>>($"/api/collection/games?metric=timed&search={search}&pageSize=1&page=2"))!;
        Assert.Equal(2, timed.TotalCount); Assert.Single(timed.Data); Assert.Single(next.Data);
        Assert.Equal(new[] { third.Id, second.Id }, timed.Data.Concat(next.Data).Select(value => value.Id));
        var missing = (await client.GetFromJsonAsync<PagedResult<GameDto>>($"/api/collection/games?metric=missingTime&search={search}"))!;
        Assert.Equal(first.Id, Assert.Single(missing.Data).Id); // Manual zero without Steam is unknown, not a timed zero override.
        var candidates = (await client.GetFromJsonAsync<PagedResult<GameDto>>($"/api/collection/games?metric=goty&year=2026&month=12&search={search}"))!;
        Assert.Equal(2, candidates.TotalCount); Assert.Contains(candidates.Data, value => value.Id == first.Id); Assert.Contains(candidates.Data, value => value.Id == second.Id); Assert.DoesNotContain(candidates.Data, value => value.Id == third.Id);
        var other = await Client("HouseholdUserB");
        Assert.Empty((await other.GetFromJsonAsync<PagedResult<GameDto>>($"/api/collection/games?metric=all&search={search}"))!.Data);
    }

    [Fact]
    public async Task A_foreign_or_missing_archive_reference_does_not_overwrite_preferences()
    {
        var client = await Client(); var other = await Client("HouseholdUserB");
        var before = (await client.GetFromJsonAsync<HomeConfigDto>("/api/home/config"))!;
        var foreign = (await other.GetFromJsonAsync<GameDto>($"/api/games/{factory.UserBGameId}"))!;
        var archive = JsonSerializer.Serialize(new
        {
            version = 1,
            home = new HomeConfigDto { Rows = [new HomeRowDto { Id = "invalid-archived-row", Source = new() { Kind = "filter", Rules = new() { OrderedGameIds = [foreign.Id] } } }] },
            goty = new Dictionary<int, int>(), games = new Dictionary<int, string> { [foreign.Id] = foreign.Name },
            playlists = new Dictionary<int, string>(), views = new Dictionary<int, string>(), statuses = new Dictionary<int, string>(), platforms = new Dictionary<int, string>(), playedStatuses = new Dictionary<int, string>()
        });
        var csv = "Type,Name,CollectionPreferencesJson\nCollectionPreferences,Invalid archive,\"" + archive.Replace("\"", "\"\"") + "\"\n";
        using var form = new MultipartFormDataContent(); form.Add(new StringContent(csv, System.Text.Encoding.UTF8, "text/csv"), "csvFile", "invalid-preferences.csv");
        var response = await client.PostAsync("/api/DataExport/full", form); response.EnsureSuccessStatusCode();
        using var result = JsonDocument.Parse(await response.Content.ReadAsStringAsync()); Assert.False(result.RootElement.GetProperty("collectionPreferences").GetProperty("restored").GetBoolean());
        Assert.Single(result.RootElement.GetProperty("errors").EnumerateArray());
        var after = (await client.GetFromJsonAsync<HomeConfigDto>("/api/home/config"))!;
        Assert.Equal(before.Revision, after.Revision); Assert.Equal(JsonSerializer.Serialize(before.Rows), JsonSerializer.Serialize(after.Rows));
    }

    [Fact]
    public async Task Full_backups_restore_Home_and_GOTY_with_owned_reference_remapping()
    {
        var client = await Client(); var other = await Client("HouseholdUserB");
        var game = await CreateGame(client, "Archive game");
        (await client.PutAsJsonAsync($"/api/games/{game.Id}", new { finished = "2026-03-01", developer = "Explicit studio" })).EnsureSuccessStatusCode();
        var before = (await client.GetFromJsonAsync<HomeConfigDto>("/api/home/config"))!;
        var saved = await client.PutAsJsonAsync("/api/home/config", new HomeConfigDto { Revision = before.Revision, Rows = [new HomeRowDto { Id = "archived-filter", Title = "Archived home row", Source = new() { Kind = "filter", Rules = new() { OrderedGameIds = [game.Id], StatusIds = [factory.UserAInitialStatusId] } } }] }); saved.EnsureSuccessStatusCode();
        var goty = (await client.GetFromJsonAsync<GotySelectionsDto>("/api/collection/goty"))!;
        (await client.PutAsJsonAsync("/api/collection/goty/2026", new GotySelectionRequest { Revision = goty.Revision, GameId = game.Id })).EnsureSuccessStatusCode();
        var exported = await client.GetAsync("/api/DataExport/full"); exported.EnsureSuccessStatusCode(); var csv = await exported.Content.ReadAsStringAsync();
        Assert.Contains("CollectionPreferences", csv); Assert.Contains("Explicit studio", csv);
        using var form = new MultipartFormDataContent(); form.Add(new StringContent(csv, System.Text.Encoding.UTF8, "text/csv"), "csvFile", "collection-backup.csv");
        var imported = await other.PostAsync("/api/DataExport/full", form); imported.EnsureSuccessStatusCode();
        using var response = JsonDocument.Parse(await imported.Content.ReadAsStringAsync()); Assert.True(response.RootElement.GetProperty("collectionPreferences").GetProperty("restored").GetBoolean());
        var restored = (await other.GetFromJsonAsync<HomeConfigDto>("/api/home/config"))!;
        var row = Assert.Single(restored.Rows); Assert.Equal("Archived home row", row.Title);
        var newId = Assert.Single(row.Source.Rules!.OrderedGameIds); Assert.NotEqual(game.Id, newId);
        Assert.Equal(game.Name, (await other.GetFromJsonAsync<GameDto>($"/api/games/{newId}"))!.Name);
        Assert.Equal(newId, (await other.GetFromJsonAsync<GotySelectionsDto>("/api/collection/goty"))!.Selections[2026]);
        Assert.Equal(game.Id, (await client.GetFromJsonAsync<GotySelectionsDto>("/api/collection/goty"))!.Selections[2026]);
        Assert.Equal("Explicit studio", (await other.GetFromJsonAsync<GameDto>($"/api/games/{newId}"))!.Developer);
    }

    [Fact]
    public async Task Full_row_browsing_pages_and_searches_all_members_independently_of_preview_count()
    {
        var client = await Client(); var other = await Client("HouseholdUserB");
        var prefix = "Full row " + Guid.NewGuid().ToString("N");
        var first = await CreateGame(client, prefix); await CreateGame(client, prefix); await CreateGame(client, prefix);
        var current = (await client.GetFromJsonAsync<HomeConfigDto>("/api/home/config"))!;
        var row = new HomeRowDto { Id = "paged-filter-row", PreviewCount = 1, Source = new() { Kind = "filter", Rules = new() { NameSearch = new() { IncludeTerms = [prefix] } } } };
        (await client.PutAsJsonAsync("/api/home/config", new HomeConfigDto { Revision = current.Revision, Rows = [row] })).EnsureSuccessStatusCode();
        var resolved = await client.PostAsJsonAsync("/api/home/rows/resolve", new HomeResolveRequest { Rows = [row] }); resolved.EnsureSuccessStatusCode();
        Assert.Single(Assert.Single((await resolved.Content.ReadFromJsonAsync<List<HomeRowResultDto>>())!).Items);
        var page1 = (await client.GetFromJsonAsync<PagedResult<GameDto>>("/api/home/rows/paged-filter-row/games?pageSize=2"))!;
        var page2 = (await client.GetFromJsonAsync<PagedResult<GameDto>>("/api/home/rows/paged-filter-row/games?pageSize=2&page=2"))!;
        Assert.Equal(3, page1.TotalCount); Assert.Equal(2, page1.Data.Count()); Assert.Single(page2.Data);
        Assert.Equal(3, page1.Data.Concat(page2.Data).Select(value => value.Id).Distinct().Count());
        var searched = (await client.GetFromJsonAsync<PagedResult<GameDto>>($"/api/home/rows/paged-filter-row/games?search={Uri.EscapeDataString(first.Name)}"))!;
        Assert.Equal(first.Id, Assert.Single(searched.Data).Id);
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync("/api/home/rows/paged-filter-row/games")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/home/rows/missing-row/games")).StatusCode);
    }

    [Fact]
    public async Task SQLite_structured_search_matches_preview_while_legacy_automatic_search_remains_sensitive()
    {
        var client = await Client(); var game = await CreateGame(client, "Pokémon Écarlate");
        var term = game.Name.Split(' ').Last();
        var rule = new NameSearchDto { IncludeTerms = ["pokemon", term], MatchMode = "all" };
        var query = Uri.EscapeDataString(JsonSerializer.Serialize(rule, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        var result = (await client.GetFromJsonAsync<PagedResult<GameDto>>($"/api/games?nameSearchJson={query}"))!; Assert.Equal(game.Id, Assert.Single(result.Data).Id);
        var preview = await client.PostAsJsonAsync("/api/playlists/preview", new PlaylistPreviewRequest { Rules = new() { NameSearch = rule } }); preview.EnsureSuccessStatusCode();
        Assert.Equal(game.Id, Assert.Single((await preview.Content.ReadFromJsonAsync<PlaylistPreviewDto>())!.Games).Id);
        var legacy = await client.PostAsJsonAsync("/api/playlists/preview", new PlaylistPreviewRequest { Rules = new() { Search = "pokémon " + term } }); legacy.EnsureSuccessStatusCode();
        Assert.Equal(0, (await legacy.Content.ReadFromJsonAsync<PlaylistPreviewDto>())!.TotalMatches);
        rule.ExcludeTerms = [term];
        var excluded = await client.PostAsJsonAsync("/api/playlists/preview", new PlaylistPreviewRequest { Rules = new() { Search = "not-a-match", NameSearch = rule } }); excluded.EnsureSuccessStatusCode();
        Assert.Equal(0, (await excluded.Content.ReadFromJsonAsync<PlaylistPreviewDto>())!.TotalMatches);
    }

    [Fact]
    public async Task Preview_reports_matching_and_limited_counts_and_never_changes_a_game()
    {
        var client = await Client(); var prefix = "Preview " + Guid.NewGuid().ToString("N");
        var first = await CreateGame(client, prefix); await CreateGame(client, prefix); await CreateGame(client, prefix);
        var response = await client.PostAsJsonAsync("/api/playlists/preview", new PlaylistPreviewRequest { Rules = new() { NameSearch = new() { IncludeTerms = [prefix] }, Limit = 2 }, MaxLength = 1, PreviewCount = 1 }); response.EnsureSuccessStatusCode();
        var preview = (await response.Content.ReadFromJsonAsync<PlaylistPreviewDto>())!; Assert.Equal(3, preview.TotalMatches); Assert.Equal(1, preview.ReturnedCount); Assert.Single(preview.Games);
        Assert.Equal(first.StatusId, (await client.GetFromJsonAsync<GameDto>($"/api/games/{first.Id}"))!.StatusId);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/playlists/preview", new PlaylistPreviewRequest { Rules = new() { NameSearch = new() { MatchMode = "unknown" } } })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/games?nameSearchJson=%7Bbad-json%7D")).StatusCode);
    }

    [Fact]
    public async Task Statistics_keep_replay_notes_grades_dates_and_unique_games_separate()
    {
        var client = await Client(); var game = await CreateGame(client, "Dated record");
        (await client.PutAsJsonAsync($"/api/games/{game.Id}", new { grade = 60, finished = "2026-03-01", manualPlaytimeMinutes = 0 })).EnsureSuccessStatusCode();
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<GamesDbContext>(); var entity = await db.Games.SingleAsync(value => value.Id == game.Id);
            var type = await db.GameReplayTypes.FirstOrDefaultAsync(value => value.UserId == entity.UserId);
            if (type is null) { type = new GameReplayType { UserId = entity.UserId, Name = "Test Replay" }; db.GameReplayTypes.Add(type); await db.SaveChangesAsync(); }
            db.GameReplays.AddRange(new GameReplay { UserId = entity.UserId, GameId = game.Id, ReplayTypeId = type.Id, Finished = "2026-03-03", Grade = 90 }, new GameReplay { UserId = entity.UserId, GameId = game.Id, ReplayTypeId = type.Id, Finished = "2026-03-04", Grade = null }); await db.SaveChangesAsync();
        }
        var stats = (await client.GetFromJsonAsync<CollectionStatisticsDto>("/api/collection/statistics?year=2026&month=3"))!;
        var ownRecords = stats.Records.Where(value => value.Game.Id == game.Id).ToList(); Assert.Equal(3, ownRecords.Count); Assert.Contains(ownRecords, value => value.Source == "replay" && value.Grade is null);
        Assert.Equal(90, Assert.Single(stats.Ranking, value => value.Game.Id == game.Id).Grade);
        Assert.Equal(stats.Records.Select(value => value.Game.Id).Distinct().Count(), stats.DistinctGames);
        Assert.Equal("accumulated-only", stats.TimeBasis); Assert.Equal("stored-records-not-sessions", stats.DateBasis);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/collection/statistics?month=13")).StatusCode);
    }

    [Fact]
    public async Task Month_artwork_uses_the_best_owned_dated_grade_even_when_another_month_is_selected()
    {
        var client = await Client(); var other = await Client("HouseholdUserB");
        var earlier = await CreateGame(client, "Month earlier");
        var latest = await CreateGame(client, "Month latest");
        (await client.PutAsJsonAsync($"/api/games/{earlier.Id}", new { finished = "2094-03-01", grade = 90 })).EnsureSuccessStatusCode();
        (await client.PutAsJsonAsync($"/api/games/{latest.Id}", new { finished = "2094-03-30", grade = 40 })).EnsureSuccessStatusCode();
        var stats = (await client.GetFromJsonAsync<CollectionStatisticsDto>("/api/collection/statistics?year=2094&month=8"))!;
        Assert.Equal(earlier.Id, stats.Months.Single(value => value.Month == 3).ArtworkGame!.Id);
        Assert.Null(stats.Months.Single(value => value.Month == 8).ArtworkGame);
        var foreign = (await other.GetFromJsonAsync<CollectionStatisticsDto>("/api/collection/statistics?year=2094"))!;
        Assert.DoesNotContain(foreign.Months, value => value.ArtworkGame?.Id == latest.Id || value.ArtworkGame?.Id == earlier.Id);
    }

    [Fact]
    public async Task Achievement_dates_and_current_completion_use_the_existing_required_policy()
    {
        var client = await Client(); var game = await CreateGame(client, "Achievement record");
        var appId = 1_000_000 + game.Id;
        (await client.PutAsJsonAsync($"/api/games/{game.Id}", new { steamAppId = appId, manualPlaytimeMinutes = 0, isManuallyCompleted = true })).EnsureSuccessStatusCode();
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<GamesDbContext>(); var owner = (await db.Games.SingleAsync(value => value.Id == game.Id)).UserId;
            for (var index = 0; index < 4; index++) db.SteamAchievements.Add(new() { UserId = owner, GameId = game.Id, SteamAppId = appId, ApiName = "current-" + index, Achieved = index < 3, UnlockTime = index < 2 ? new DateTime(2026, 8, index + 1, 12, 0, 0, DateTimeKind.Utc) : null });
            db.SteamAchievements.Add(new() { UserId = owner, GameId = game.Id, SteamAppId = appId, ApiName = "retired", Archived = true, Achieved = true }); await db.SaveChangesAsync();
        }
        var stats = (await client.GetFromJsonAsync<CollectionStatisticsDto>("/api/collection/statistics?year=2026&month=8"))!;
        Assert.Equal(2, Assert.Single(stats.UnlocksByGame, value => value.Game.Id == game.Id).Count);
        var detail = (await client.GetFromJsonAsync<GameDto>($"/api/games/{game.Id}"))!; Assert.Equal(4, detail.SteamAchievementsTotal); Assert.Equal(3, detail.SteamAchievementsUnlocked); Assert.True(detail.IsCompleted); Assert.False(detail.IsFullyCompleted);
        Assert.True(stats.Totals.UnknownAchievementDates >= 1);
        var december = (await client.GetFromJsonAsync<CollectionStatisticsDto>("/api/collection/statistics?year=2026&month=12"))!;
        Assert.DoesNotContain(december.UnlocksByGame, value => value.Game.Id == game.Id);
    }

    [Fact]
    public async Task Goty_is_an_explicit_owned_dated_choice_and_developer_patch_preserves_other_fields()
    {
        var owner = await Client(); var other = await Client("HouseholdUserB"); var game = await CreateGame(owner, "Personal selection");
        (await owner.PutAsJsonAsync($"/api/games/{game.Id}", new { finished = "2042-02-01", grade = 0, story = 176, developer = "Assigned studio" })).EnsureSuccessStatusCode();
        var selections = (await owner.GetFromJsonAsync<GotySelectionsDto>("/api/collection/goty"))!;
        Assert.False(selections.Selections.ContainsKey(2042));
        var response = await owner.PutAsJsonAsync("/api/collection/goty/2042", new GotySelectionRequest { Revision = selections.Revision, GameId = game.Id }); response.EnsureSuccessStatusCode();
        var saved = (await response.Content.ReadFromJsonAsync<GotySelectionsDto>())!; Assert.Equal(game.Id, saved.Selections[2042]);
        var otherSelections = (await other.GetFromJsonAsync<GotySelectionsDto>("/api/collection/goty"))!;
        Assert.Equal(HttpStatusCode.NotFound, (await other.PutAsJsonAsync("/api/collection/goty/2042", new GotySelectionRequest { Revision = otherSelections.Revision, GameId = game.Id })).StatusCode);
        (await owner.PutAsJsonAsync($"/api/games/{game.Id}", new { developer = (string?)null })).EnsureSuccessStatusCode();
        var detail = (await owner.GetFromJsonAsync<GameDto>($"/api/games/{game.Id}"))!; Assert.Null(detail.Developer); Assert.Equal(0, detail.Grade); Assert.Equal(176, detail.Story);
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PutAsJsonAsync($"/api/games/{game.Id}", new { developer = new string('x', 151) })).StatusCode);
    }
}
