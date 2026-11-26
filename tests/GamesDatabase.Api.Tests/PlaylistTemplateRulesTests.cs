using System.Text.Json;
using GamesDatabase.Api.Application.Interfaces;
using GamesDatabase.Api.Application.Services;
using GamesDatabase.Api.Common;
using GamesDatabase.Api.Contracts;
using GamesDatabase.Api.Domain.Entities;
using GamesDatabase.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GamesDatabase.Api.Tests;

public sealed class ReleaseCalendarTests
{
    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("2026", false)]
    [InlineData("2026-02-31", false)]
    [InlineData("2026-11-40", false)]
    [InlineData("2026-12-01garbage", false)]
    [InlineData("2026-11-25", false)]
    [InlineData("2026-11-26", true)]
    [InlineData("2026-11-26T23:59:59-11:00", true)]
    [InlineData("2027-01-01", true)]
    [InlineData("2199-12-31", true)]
    public void Upcoming_requires_a_real_date_and_includes_today_without_an_upper_horizon(string? released, bool expected) =>
        Assert.Equal(expected, ReleaseDateMatching.OnOrAfter(released, "2026-11-26"));

    [Fact]
    public void Today_uses_the_rules_timezone_instead_of_utc_or_the_server_timezone()
    {
        var now = DateTimeOffset.Parse("2026-11-25T10:30:00Z");
        Assert.Equal("2026-11-26", ReleaseDateMatching.Today(ReleaseDateMatching.FindTimeZone("Pacific/Kiritimati")!, now));
        Assert.Equal("2026-11-24", ReleaseDateMatching.Today(ReleaseDateMatching.FindTimeZone("Pacific/Pago_Pago")!, now));
        Assert.Equal("2026-11-25", ReleaseDateMatching.Today(TimeZoneInfo.Utc, now));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Not/A_Timezone")]
    public void Upcoming_validation_never_silently_substitutes_a_timezone(string? zone)
    {
        Assert.NotNull(PlaylistService.ValidateRules(new() { UpcomingReleases = true, ReleaseTimeZone = zone }));
        Assert.Null(PlaylistService.ValidateRules(new() { UpcomingReleases = false, ReleaseTimeZone = zone }));
    }
}

public sealed class PlaylistTemplateRulesTests(GamesDatabaseApiFactory factory) : IClassFixture<GamesDatabaseApiFactory>
{
    private sealed class Clock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    [Fact]
    public async Task Release_rule_is_shared_by_preview_playlist_home_and_full_results_and_rolls_forward()
    {
        await factory.EnsureSeededAsync();
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<GamesDbContext>();
        var owner = await context.Games.SingleAsync(game => game.Id == factory.UserAGameId);
        var other = await context.Games.SingleAsync(game => game.Id == factory.UserBGameId);
        var prefix = "Release template " + Guid.NewGuid().ToString("N");
        Game New(string label, string? released, bool foreign = false) => new() { Name = prefix + label, UserId = foreign ? other.UserId : owner.UserId, StatusId = foreign ? other.StatusId : owner.StatusId, Released = released };
        var today = New(" today", "2026-11-26");
        var later = New(" later", "2027-01-01");
        var far = New(" far", "2199-12-31");
        context.Games.AddRange(today, later, far,
            New(" timestamp", "2026-11-26T00:00:00-11:00"), New(" past", "2026-11-25"), New(" missing", null),
            New(" partial", "2026"), New(" invalid", "2026-11-40"), New(" garbage", "2026-12-01garbage"), New(" foreign", "2027-01-01", true));
        await context.SaveChangesAsync();
        var clock = new Clock(DateTimeOffset.Parse("2026-11-25T10:30:00Z"));
        var games = scope.ServiceProvider.GetRequiredService<IGameService>();
        var service = new PlaylistService(context, games, clock);
        var rules = new PlaylistRulesDto { Search = prefix, UpcomingReleases = true, ReleaseTimeZone = "Pacific/Kiritimati", SortBy = "Released" };
        var preview = await service.PreviewAsync(new() { Rules = rules, PreviewCount = 1 }, owner.UserId);
        Assert.True(preview.Success);
        Assert.Equal(4, preview.Data!.TotalMatches);
        Assert.Equal(4, preview.Data.ReturnedCount);
        Assert.Equal(today.Id, Assert.Single(preview.Data.Games).Id);
        var page = await service.GetRulesPageAsync(rules, null, owner.UserId, 1, 200, null);
        Assert.Equal(4, page.Data!.TotalCount);
        Assert.Contains(page.Data.Data, game => game.Id == far.Id);

        var created = await service.CreatePlaylistAsync(new() { Name = prefix, IsAutomatic = true, Rules = rules }, owner.UserId);
        Assert.True(created.Success);
        var playlistId = created.Data!.Id;
        var storedRules = (await context.Playlists.AsNoTracking().SingleAsync(value => value.Id == playlistId)).RulesJson;
        Assert.Equal(4, (await service.GetPlaylistByIdAsync(playlistId, owner.UserId))!.Items.Count);
        Assert.Null(await service.GetPlaylistByIdAsync(playlistId, other.UserId));

        var home = new CollectionService(context, games, service);
        var row = new HomeRowDto { Id = "upcoming-template", Source = new() { Kind = "filter", Rules = rules }, PreviewCount = 1 };
        var config = await home.GetHomeAsync(owner.UserId);
        config.Rows = [row];
        Assert.True((await home.SaveHomeAsync(config, owner.UserId)).Success);
        var resolved = await home.ResolveHomeAsync(new() { Rows = [row] }, owner.UserId);
        Assert.Equal(4, Assert.Single(resolved.Data!).TotalMatches);
        Assert.Single(Assert.Single(resolved.Data!).Items);
        Assert.Equal(4, (await home.GetHomeRowGamesAsync(row.Id, owner.UserId, 1, 200, null))!.TotalCount);

        clock.Now = clock.Now.AddDays(1);
        var tomorrow = await service.GetPlaylistByIdAsync(playlistId, owner.UserId);
        Assert.Equal(new[] { later.Id, far.Id }, tomorrow!.Items.Select(item => item.GameId));
        Assert.Equal(storedRules, (await context.Playlists.AsNoTracking().SingleAsync(value => value.Id == playlistId)).RulesJson);
        Assert.False(await context.PlaylistItems.AnyAsync(item => item.PlaylistId == playlistId));
    }

    [Fact]
    public async Task Playing_rule_follows_the_semantic_role_not_its_name_or_focus_and_does_not_expand_when_missing()
    {
        await factory.EnsureSeededAsync();
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<GamesDbContext>();
        var owner = await context.Games.SingleAsync(game => game.Id == factory.UserAGameId);
        var prefix = "Playing template " + Guid.NewGuid().ToString("N");
        var real = new GameStatus { UserId = owner.UserId, Name = "En cours", StatusType = SpecialStatusType.Playing, IsActive = true };
        var fake = new GameStatus { UserId = owner.UserId, Name = "Playing", IsActive = true };
        context.GameStatuses.AddRange(real, fake);
        await context.SaveChangesAsync();
        var playing = new Game { UserId = owner.UserId, StatusId = real.Id, Name = prefix + " actual", Grade = 95 };
        var notPlaying = new Game { UserId = owner.UserId, StatusId = fake.Id, Name = prefix + " not playing", Grade = 85 };
        context.Games.AddRange(playing, notPlaying);
        await context.SaveChangesAsync();
        var games = scope.ServiceProvider.GetRequiredService<IGameService>();
        var service = new PlaylistService(context, games);
        var rules = new PlaylistRulesDto { Search = prefix, PlayingNow = true, MinGrade = 90, SortBy = "Name" };
        var created = await service.CreatePlaylistAsync(new() { Name = prefix, IsAutomatic = true, Rules = rules }, owner.UserId);
        Assert.Equal(playing.Id, Assert.Single(created.Data!.Items).GameId);
        var rejected = await service.AddItemAsync(created.Data.Id, new() { GameId = notPlaying.Id, StatusId = real.Id }, owner.UserId);
        Assert.False(rejected.Success);
        Assert.Equal(fake.Id, (await context.Games.AsNoTracking().SingleAsync(game => game.Id == notPlaying.Id)).StatusId);
        notPlaying.Grade = 95;
        await context.SaveChangesAsync();
        Assert.True((await service.AddItemAsync(created.Data.Id, new() { GameId = notPlaying.Id, StatusId = real.Id }, owner.UserId)).Success);

        real.StatusType = SpecialStatusType.None;
        await context.SaveChangesAsync();
        Assert.Empty((await service.GetPlaylistByIdAsync(created.Data.Id, owner.UserId))!.Items);
        fake.StatusType = SpecialStatusType.Playing;
        var reassigned = new Game { UserId = owner.UserId, StatusId = fake.Id, Name = prefix + " reassigned", Grade = 95 };
        context.Games.Add(reassigned);
        await context.SaveChangesAsync();
        Assert.Equal(reassigned.Id, Assert.Single((await service.GetPlaylistByIdAsync(created.Data.Id, owner.UserId))!.Items).GameId);
        Assert.True(JsonSerializer.Deserialize<PlaylistRulesDto>((await context.Playlists.AsNoTracking().SingleAsync(value => value.Id == created.Data.Id)).RulesJson!)!.PlayingNow);
    }
}
