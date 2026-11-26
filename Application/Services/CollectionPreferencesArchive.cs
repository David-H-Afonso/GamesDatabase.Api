using System.Globalization;
using System.Text.Json;
using GamesDatabase.Api.Contracts;
using GamesDatabase.Api.Controllers;
using GamesDatabase.Api.Domain.Entities;
using GamesDatabase.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GamesDatabase.Api.Application.Services;

/// <summary>Versioned, owner-scoped references for full CSV/ZIP/backups. Never reuses another user's IDs.</summary>
internal static class CollectionPreferencesArchive
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private sealed class Archive
    {
        public int Version { get; set; } = 1;
        public HomeConfigDto? Home { get; set; }
        public Dictionary<int, int> Goty { get; set; } = [];
        public Dictionary<int, string> Games { get; set; } = [];
        public Dictionary<int, string> Playlists { get; set; } = [];
        public Dictionary<int, string> Views { get; set; } = [];
        public Dictionary<int, string> Statuses { get; set; } = [];
        public Dictionary<int, string> Platforms { get; set; } = [];
        public Dictionary<int, string> PlayedStatuses { get; set; } = [];
    }

    public static async Task<FullExportModel?> ExportAsync(GamesDbContext db, int userId, CancellationToken ct = default)
    {
        var stored = await db.CollectionPreferences.AsNoTracking().SingleOrDefaultAsync(value => value.UserId == userId, ct);
        if (stored is null) return null;
        var archive = new Archive
        {
            Home = stored.HomeConfigJson == "{}" ? null : JsonSerializer.Deserialize<HomeConfigDto>(stored.HomeConfigJson, JsonOptions),
            Goty = JsonSerializer.Deserialize<Dictionary<int, int>>(stored.GotySelectionsJson, JsonOptions) ?? [],
            Playlists = await db.Playlists.Where(value => value.UserId == userId).ToDictionaryAsync(value => value.Id, value => value.Name, ct),
            Views = await db.GameViews.Where(value => value.UserId == userId).ToDictionaryAsync(value => value.Id, value => value.Name, ct),
            Statuses = await db.GameStatuses.Where(value => value.UserId == userId).ToDictionaryAsync(value => value.Id, value => value.Name, ct),
            Platforms = await db.GamePlatforms.Where(value => value.UserId == userId).ToDictionaryAsync(value => value.Id, value => value.Name, ct),
            PlayedStatuses = await db.GamePlayedStatuses.Where(value => value.UserId == userId).ToDictionaryAsync(value => value.Id, value => value.Name, ct),
        };
        var referencedGames = archive.Goty.Values.Concat((archive.Home?.Rows ?? []).SelectMany(row => (row.Source.Rules?.OrderedGameIds ?? []).Concat(row.Source.Rules?.ExcludedGameIds ?? []))).Distinct().ToList();
        archive.Games = await db.Games.Where(value => value.UserId == userId && referencedGames.Contains(value.Id)).ToDictionaryAsync(value => value.Id, value => value.Name, ct);
        return new() { Type = "CollectionPreferences", Name = "Collection experience v1", CollectionPreferencesJson = JsonSerializer.Serialize(archive, JsonOptions) };
    }

    public static async Task ImportAsync(GamesDbContext db, int userId, string json)
    {
        if (json.Length > 2_000_000) throw new ArgumentException("Collection preferences archive is too large.");
        var archive = JsonSerializer.Deserialize<Archive>(json, JsonOptions) ?? throw new ArgumentException("Missing collection preferences.");
        if (archive.Version != 1 || archive.Home is { Version: not 1 } || archive.Goty is null || archive.Games is null || archive.Playlists is null || archive.Views is null || archive.Statuses is null || archive.Platforms is null || archive.PlayedStatuses is null) throw new ArgumentException("Unsupported collection preferences archive.");
        if (archive.Home is not null && CollectionService.ValidateRowStructure(archive.Home.Rows) is { } validation) throw new ArgumentException(validation);
        var games = await db.Games.Where(value => value.UserId == userId).Select(value => new { value.Id, value.Name }).ToDictionaryAsync(value => value.Id, value => value.Name);
        var playlists = await db.Playlists.Where(value => value.UserId == userId).ToDictionaryAsync(value => value.Id, value => value.Name);
        var views = await db.GameViews.Where(value => value.UserId == userId).ToDictionaryAsync(value => value.Id, value => value.Name);
        var statuses = await db.GameStatuses.Where(value => value.UserId == userId).ToDictionaryAsync(value => value.Id, value => value.Name);
        var platforms = await db.GamePlatforms.Where(value => value.UserId == userId).ToDictionaryAsync(value => value.Id, value => value.Name);
        var played = await db.GamePlayedStatuses.Where(value => value.UserId == userId).ToDictionaryAsync(value => value.Id, value => value.Name);
        static int Resolve(int id, Dictionary<int, string> exported, Dictionary<int, string> owned)
        {
            if (!exported.TryGetValue(id, out var name)) throw new ArgumentException("An archived collection reference is missing.");
            if (owned.TryGetValue(id, out var same) && same == name) return id;
            var matches = owned.Where(pair => pair.Value == name).Select(pair => pair.Key).Take(2).ToList();
            return matches.Count == 1 ? matches[0] : throw new ArgumentException("An archived collection reference is missing or ambiguous.");
        }
        foreach (var row in archive.Home?.Rows ?? [])
        {
            var source = row.Source;
            if (source.Kind == "playlist") source.PlaylistId = Resolve(source.PlaylistId!.Value, archive.Playlists, playlists);
            if (source.Kind == "view") source.ViewId = Resolve(source.ViewId!.Value, archive.Views, views);
            if (source.Recommendations is { } recommendations)
            {
                recommendations.IncludedStatusIds = recommendations.IncludedStatusIds.Select(id => Resolve(id, archive.Statuses, statuses)).ToList();
                recommendations.ExcludedStatusIds = recommendations.ExcludedStatusIds.Select(id => Resolve(id, archive.Statuses, statuses)).ToList();
            }
            if (source.Rules is not { } rules) continue;
            rules.StatusIds = rules.StatusIds.Select(id => Resolve(id, archive.Statuses, statuses)).ToList();
            rules.PlatformIds = rules.PlatformIds.Select(id => Resolve(id, archive.Platforms, platforms)).ToList();
            rules.PlayedStatusIds = rules.PlayedStatusIds.Select(id => Resolve(id, archive.PlayedStatuses, played)).ToList();
            rules.StatusPriorityIds = rules.StatusPriorityIds.Select(id => Resolve(id, archive.Statuses, statuses)).ToList();
            rules.OrderedGameIds = rules.OrderedGameIds.Select(id => Resolve(id, archive.Games, games)).ToList();
            rules.ExcludedGameIds = rules.ExcludedGameIds.Select(id => Resolve(id, archive.Games, games)).ToList();
        }
        var selections = archive.Goty.ToDictionary(pair => pair.Key, pair => Resolve(pair.Value, archive.Games, games));
        var selectedIds = selections.Values.Distinct().ToList();
        var candidates = await db.Games.AsNoTracking().Include(value => value.GameReplays).Where(value => value.UserId == userId && selectedIds.Contains(value.Id)).ToListAsync();
        foreach (var selection in selections)
        {
            if (selection.Key is < 1900 or > 2100) throw new ArgumentException("Invalid GOTY year.");
            var game = candidates.Single(value => value.Id == selection.Value);
            var year = selection.Key.ToString(CultureInfo.InvariantCulture) + "-";
            if (!new[] { game.Started, game.Finished }.Concat(game.GameReplays.SelectMany(value => new[] { value.Started, value.Finished })).Any(value => CollectionService.IsoDate(value)?.StartsWith(year, StringComparison.Ordinal) == true)) throw new ArgumentException("An archived GOTY has no qualifying stored date.");
        }
        // Validate every reference before changing either preference document. A shared revision detects other clients.
        var stored = await db.CollectionPreferences.SingleOrDefaultAsync(value => value.UserId == userId);
        if (stored is null) { stored = new CollectionPreferences { UserId = userId }; db.CollectionPreferences.Add(stored); }
        stored.HomeConfigJson = archive.Home is null ? "{}" : JsonSerializer.Serialize(new HomeConfigDto { Rows = archive.Home.Rows }, JsonOptions);
        stored.GotySelectionsJson = JsonSerializer.Serialize(selections, JsonOptions);
        stored.Revision++; stored.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
    }
}
