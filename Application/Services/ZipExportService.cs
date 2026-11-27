using GamesDatabase.Api.Application.Interfaces;
using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using CsvHelper;
using CsvHelper.Configuration;
using GamesDatabase.Api.Configuration;
using GamesDatabase.Api.Infrastructure.Persistence;
using GamesDatabase.Api.Domain.Entities;
using GamesDatabase.Api.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace GamesDatabase.Api.Application.Services;

public class ZipExportService : IZipExportService
{
    private readonly IGameImportExportService _importExportService;
    private readonly ExportMediaDownloader _mediaDownloader;
    private readonly ExportSettings _exportSettings;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly GamesDbContext _context;
    private readonly ILogger<ZipExportService> _logger;

    public ZipExportService(
        IGameImportExportService importExportService,
        ExportMediaDownloader mediaDownloader,
        IOptions<ExportSettings> exportSettings,
        IHttpContextAccessor httpContextAccessor,
        GamesDbContext context,
        ILogger<ZipExportService> logger)
    {
        _importExportService = importExportService;
        _mediaDownloader = mediaDownloader;
        _exportSettings = exportSettings.Value;
        _httpContextAccessor = httpContextAccessor;
        _context = context;
        _logger = logger;
    }

    public async Task<ZipExportResult> BuildZipAsync(string? authorizationHeader, bool fullExport = false)
    {
        var userId = _httpContextAccessor.HttpContext?.GetUserId();
        if (!userId.HasValue) throw new UnauthorizedAccessException("An authenticated owner is required to export games.");
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var stats = new ZipExportResult();

        _logger.LogInformation("Starting ZIP export (fullExport: {FullExport})", fullExport);

        // Generate the owner-scoped CSV directly. Neither the incoming credential
        // nor request Host/forwarded headers is ever used for an outbound request.
        var csvBytes = await _importExportService.ExportFullDatabaseAsync(userId.Value);
        var csvContent = Encoding.UTF8.GetString(csvBytes);

        // 2. Parse CSV
        _logger.LogInformation("Parsing CSV content");
        var records = ParseCsv(csvContent);

        // 3. Build ZIP in memory
        _logger.LogInformation("Building ZIP archive with {Count} records", records.Count);
        using var memoryStream = new MemoryStream();
        using (var archive = new ZipArchive(memoryStream, ZipArchiveMode.Create, true))
        {
            // Add backup CSV
            await AddBackupCsvAsync(archive, csvBytes);

            // Add settings JSONs
            await AddSettingsAsync(archive, records);

            // Add games
            await AddGamesAsync(archive, records, fullExport, stats, userId.Value);

            // Add playlists and their ordered game references/assets.
            await AddPlaylistsAsync(archive, records, fullExport);
        }

        stopwatch.Stop();
        stats.ZipBytes = memoryStream.ToArray();
        stats.ElapsedTime = stopwatch.Elapsed;

        _logger.LogInformation(
            "ZIP export completed in {ElapsedSeconds:F2}s - Total: {Total}, Exported: {Exported}, Skipped: {Skipped}, Images: {Downloaded} downloaded ({Retried} retried, {Failed} failed)",
            stats.ElapsedTime.TotalSeconds,
            stats.TotalGames,
            stats.GamesExported,
            stats.GamesSkipped,
            stats.ImagesDownloaded,
            stats.ImagesRetried,
            stats.ImagesFailed
        );

        return stats;
    }

    private List<ExportRecord> ParseCsv(string csvContent)
    {
        // Remove BOM if present
        if (csvContent.StartsWith("\uFEFF", StringComparison.Ordinal))
        {
            csvContent = csvContent.Substring(1);
        }

        using var reader = new StringReader(csvContent);
        using var csv = new CsvReader(reader, new CsvConfiguration(CultureInfo.InvariantCulture) { Delimiter = _exportSettings.CsvDelimiter });

        return csv.GetRecords<ExportRecord>().ToList();
    }

    private async Task AddBackupCsvAsync(ZipArchive archive, byte[] csvBytes)
    {
        var fileName = $"database_full_export_{DateTime.UtcNow:yyyy-MM-dd}.csv";
        var entry = archive.CreateEntry($"Games Database/Backups/{fileName}");

        using var entryStream = entry.Open();
        await entryStream.WriteAsync(csvBytes);

        _logger.LogInformation("Added backup CSV: {FileName}", fileName);
    }

    private async Task AddSettingsAsync(ZipArchive archive, List<ExportRecord> records)
    {
        // Platforms
        var platforms = records
            .Where(r => r.Type == "Platform")
            .Select(r => new
            {
                Name = r.Name,
                Color = r.Color ?? "",
                Logo = r.Logo ?? "",
                IsActive = ParseBool(r.IsActive),
                SortOrder = ParseInt(r.SortOrder) ?? 0,
                IsDefault = ParseBool(r.IsDefault)
            })
            .ToList();
        await AddJsonToZip(archive, "Games Database/Settings/Platforms.json", platforms);

        // Status
        var statuses = records
            .Where(r => r.Type == "Status")
            .Select(r => new
            {
                Name = r.Name,
                Color = r.Color ?? "",
                StatusType = r.StatusType ?? "",
                IsActive = ParseBool(r.IsActive),
                SortOrder = ParseInt(r.SortOrder) ?? 0,
                IsDefault = ParseBool(r.IsDefault)
            })
            .ToList();
        await AddJsonToZip(archive, "Games Database/Settings/Status.json", statuses);

        // PlayWith
        var playWiths = records
            .Where(r => r.Type == "PlayWith")
            .Select(r => new
            {
                Name = r.Name,
                Color = r.Color ?? "",
                IsActive = ParseBool(r.IsActive),
                SortOrder = ParseInt(r.SortOrder) ?? 0
            })
            .ToList();
        await AddJsonToZip(archive, "Games Database/Settings/PlayWith.json", playWiths);

        // PlayedStatus
        var playedStatuses = records
            .Where(r => r.Type == "PlayedStatus")
            .Select(r => new
            {
                Name = r.Name,
                Color = r.Color ?? "",
                IsActive = ParseBool(r.IsActive),
                SortOrder = ParseInt(r.SortOrder) ?? 0
            })
            .ToList();
        await AddJsonToZip(archive, "Games Database/Settings/PlayedStatus.json", playedStatuses);

        // Views
        var views = records
            .Where(r => r.Type == "View")
            .Select(r => new
            {
                Name = r.Name,
                Description = r.Description ?? "",
                FiltersJson = r.FiltersJson ?? "{}",
                SortingJson = r.SortingJson ?? "",
                IsPublic = ParseBool(r.IsPublic),
                CreatedBy = r.CreatedBy ?? "",
                SortOrder = ParseInt(r.SortOrder) ?? 999,
                PinnedSlot = ParseInt(r.ViewPinnedSlot),
                LastUsedAt = r.ViewLastUsedAt ?? ""
            })
            .ToList();
        await AddJsonToZip(archive, "Games Database/Settings/Views.json", views);

        var playlists = records.Where(r => r.Type == "Playlist").Select(playlist => new
        {
            Id = ParseInt(playlist.PlaylistId),
            Name = playlist.Name,
            Description = playlist.Description ?? "",
            HeroUrl = playlist.PlaylistHeroUrl ?? "",
            CoverUrl = playlist.PlaylistCoverUrl ?? "",
            LogoUrl = playlist.PlaylistLogoUrl ?? "",
            SortOrder = ParseInt(playlist.SortOrder) ?? 0,
            Games = records.Where(item => item.Type == "PlaylistItem"
                    && (string.Equals(item.PlaylistId, playlist.PlaylistId, StringComparison.Ordinal)
                        || string.Equals(item.PlaylistName, playlist.Name, StringComparison.Ordinal)))
                .OrderBy(item => ParseInt(item.Position) ?? int.MaxValue)
                .Select(item => new { GameId = ParseInt(item.GameId), Name = item.Name, Position = ParseInt(item.Position) ?? 0 })
                .ToList()
        }).ToList();
        await AddJsonToZip(archive, "Games Database/Settings/Playlists.json", playlists);
        var preferences = records.SingleOrDefault(record => record.Type == "CollectionPreferences")?.CollectionPreferencesJson;
        if (!string.IsNullOrWhiteSpace(preferences)) await AddJsonToZip(archive, "Games Database/Settings/CollectionPreferences.json", JsonSerializer.Deserialize<JsonElement>(preferences));

        _logger.LogInformation("Added settings files");
    }

    private async Task AddPlaylistsAsync(ZipArchive archive, List<ExportRecord> records, bool fullExport)
    {
        var playlists = records.Where(record => record.Type == "Playlist").ToList();
        var games = records.Where(record => record.Type == "Game").ToDictionary(record => record.Name, StringComparer.Ordinal);
        foreach (var playlist in playlists)
        {
            var folderName = MakeSafeFolderName(playlist.Name);
            var basePath = $"Games Database/Playlists/{folderName}";
            var items = records.Where(item => item.Type == "PlaylistItem"
                    && (string.Equals(item.PlaylistId, playlist.PlaylistId, StringComparison.Ordinal)
                        || string.Equals(item.PlaylistName, playlist.Name, StringComparison.Ordinal)))
                .OrderBy(item => ParseInt(item.Position) ?? int.MaxValue)
                .ToList();
            var firstGame = items.Select(item => games.GetValueOrDefault(item.Name)).FirstOrDefault(game => game is not null);
            var imageSources = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
            {
                ["hero"] = string.IsNullOrWhiteSpace(playlist.PlaylistHeroUrl) ? firstGame?.Hero : playlist.PlaylistHeroUrl,
                ["cover"] = string.IsNullOrWhiteSpace(playlist.PlaylistCoverUrl) ? firstGame?.Cover : playlist.PlaylistCoverUrl,
                ["logo"] = string.IsNullOrWhiteSpace(playlist.PlaylistLogoUrl) ? firstGame?.Logo : playlist.PlaylistLogoUrl
            };
            await AddJsonToZip(archive, $"{basePath}/info.json", new
            {
                Name = playlist.Name,
                Description = playlist.Description ?? "",
                SortOrder = ParseInt(playlist.SortOrder) ?? 0,
                HeroUrl = playlist.PlaylistHeroUrl ?? "",
                CoverUrl = playlist.PlaylistCoverUrl ?? "",
                LogoUrl = playlist.PlaylistLogoUrl ?? "",
                Games = items.Select(item => new { GameId = ParseInt(item.GameId), Name = item.Name, Position = ParseInt(item.Position) ?? 0 }).ToList()
            });

            foreach (var (imageType, url) in imageSources)
            {
                if (string.IsNullOrWhiteSpace(url)) continue;
                var image = await SafeDownloadAsync(url);
                if (image is null) continue;
                var entry = archive.CreateEntry($"{basePath}/{imageType}{image.Extension}");
                using var stream = entry.Open();
                await stream.WriteAsync(image.Bytes);
            }
        }
    }

    private async Task AddGamesAsync(ZipArchive archive, List<ExportRecord> records, bool fullExport, ZipExportResult stats, int userId)
    {
        var games = records.Where(r => r.Type == "Game").ToList();
        stats.TotalGames = games.Count;

        _logger.LogInformation("Processing {Count} games (fullExport: {FullExport})", games.Count, fullExport);

        // Load all game IDs and their cache status
        var gameNames = games.Select(g => g.Name).ToList();
        var dbGames = await _context.Games
            .Where(g => g.UserId == userId && gameNames.Contains(g.Name))
            .Select(g => new { g.Id, g.Name, g.ModifiedSinceExport, g.Logo, g.Hero, g.Cover })
            .ToListAsync();

        var exportCaches = await _context.GameExportCaches
            .Where(ec => dbGames.Select(g => g.Id).Contains(ec.GameId))
            .ToDictionaryAsync(ec => ec.GameId);

        foreach (var game in games)
        {
            var dbGame = dbGames.FirstOrDefault(g => g.Name == game.Name);
            if (dbGame == null)
            {
                _logger.LogWarning("Game '{Name}' not found in database, skipping", game.Name);
                continue;
            }

            var folderName = MakeSafeFolderName(game.Name);
            if (string.IsNullOrWhiteSpace(folderName))
            {
                folderName = "Unknown_Game";
            }

            var basePath = $"Games Database/Games/{folderName}";

            // Check if we can skip this game
            var cache = exportCaches.GetValueOrDefault(dbGame.Id);
            bool needsExport = fullExport || dbGame.ModifiedSinceExport || cache == null || cache.MetadataVersion < GameExportCache.CurrentMetadataVersion;

            // Check if images need retry
            bool logoNeedsRetry = !string.IsNullOrWhiteSpace(game.Logo) &&
                                  (cache == null || (!cache.LogoDownloaded && cache.LogoUrl == game.Logo));
            bool heroNeedsRetry = !string.IsNullOrWhiteSpace(game.Hero) &&
                                  (cache == null || (!cache.HeroDownloaded && cache.HeroUrl == game.Hero));
            bool coverNeedsRetry = !string.IsNullOrWhiteSpace(game.Cover) &&
                                   (cache == null || (!cache.CoverDownloaded && cache.CoverUrl == game.Cover));

            if (!needsExport && !logoNeedsRetry && !heroNeedsRetry && !coverNeedsRetry)
            {
                _logger.LogDebug("Skipping '{Name}' - no changes since last export", game.Name);
                stats.GamesSkipped++;
                continue;
            }

            stats.GamesExported++;

            // Add info.json (always add if exporting)
            if (needsExport)
            {
                var gameInfo = new
                {
                    Name = game.Name,
                    Developer = game.Developer,
                    Status = game.Status ?? "",
                    Platform = game.Platform ?? "",
                    PlayWith = game.PlayWith ?? "",
                    PlayedStatus = game.PlayedStatus ?? "",
                    Released = game.Released ?? "",
                    Started = game.Started ?? "",
                    Finished = game.Finished ?? "",
                    Score = game.Score ?? "",
                    Critic = game.Critic ?? "",
                    CriticProvider = game.CriticProvider ?? "",
                    Grade = game.Grade ?? "",
                    Completion = game.Completion ?? "",
                    Story = game.Story ?? "",
                    Comment = game.Comment ?? "",
                    Description = game.Description ?? "",
                    Favorite = game.Favorite ?? "",
                    SteamAppId = game.SteamAppId ?? "",
                    SteamPlaytimeForever = game.SteamPlaytimeForever ?? "",
                    SteamPlaytime2Weeks = game.SteamPlaytime2Weeks ?? "",
                    SteamLastSynced = game.SteamLastSynced ?? "",
                    ManualPlaytimeMinutes = game.ManualPlaytimeMinutes ?? "",
                    ManualPlaytimeIsAdditive = game.ManualPlaytimeIsAdditive ?? "",
                    IsManuallyCompleted = game.IsManuallyCompleted ?? "",
                    SteamCompletionPreserved = game.SteamCompletionPreserved ?? ""
                };
                await AddJsonToZip(archive, $"{basePath}/info.json", gameInfo);
            }

            // Initialize or get cache entry
            if (cache == null)
            {
                cache = new GameExportCache
                {
                    GameId = dbGame.Id,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow,
                    LastExportedAt = DateTime.UtcNow
                };
                _context.GameExportCaches.Add(cache);
            }
            else
            {
                cache.UpdatedAt = DateTime.UtcNow;
                cache.LastExportedAt = DateTime.UtcNow;
            }

            // Download and add logo
            cache.MetadataVersion = GameExportCache.CurrentMetadataVersion;
            bool logoDownloaded = false;
            if (!string.IsNullOrWhiteSpace(game.Logo) && (needsExport || logoNeedsRetry))
            {
                if (logoNeedsRetry)
                {
                    _logger.LogInformation("Retrying logo download for '{Name}'", game.Name);
                    stats.ImagesRetried++;
                }

                var logoBytes = await SafeDownloadAsync(game.Logo);
                if (logoBytes != null)
                {
                    var entry = archive.CreateEntry($"{basePath}/logo{logoBytes.Extension}");
                    using var entryStream = entry.Open();
                    await entryStream.WriteAsync(logoBytes.Bytes);
                    logoDownloaded = true;
                    stats.ImagesDownloaded++;
                }
                else
                {
                    stats.ImagesFailed++;
                }

                cache.LogoUrl = game.Logo;
                cache.LogoDownloaded = logoDownloaded;
            }

            // Download and add hero
            bool heroDownloaded = false;
            if (!string.IsNullOrWhiteSpace(game.Hero) && (needsExport || heroNeedsRetry))
            {
                if (heroNeedsRetry)
                {
                    _logger.LogInformation("Retrying hero download for '{Name}'", game.Name);
                    stats.ImagesRetried++;
                }

                var heroBytes = await SafeDownloadAsync(game.Hero);
                if (heroBytes != null)
                {
                    var entry = archive.CreateEntry($"{basePath}/hero{heroBytes.Extension}");
                    using var entryStream = entry.Open();
                    await entryStream.WriteAsync(heroBytes.Bytes);
                    heroDownloaded = true;
                    stats.ImagesDownloaded++;
                }
                else
                {
                    stats.ImagesFailed++;
                }

                cache.HeroUrl = game.Hero;
                cache.HeroDownloaded = heroDownloaded;
            }

            // Download and add vertical cover art
            bool coverDownloaded = false;
            if (!string.IsNullOrWhiteSpace(game.Cover) && (needsExport || coverNeedsRetry))
            {
                if (coverNeedsRetry)
                {
                    _logger.LogInformation("Retrying cover download for '{Name}'", game.Name);
                    stats.ImagesRetried++;
                }

                var coverBytes = await SafeDownloadAsync(game.Cover);
                if (coverBytes != null)
                {
                    var entry = archive.CreateEntry($"{basePath}/cover{coverBytes.Extension}");
                    using var entryStream = entry.Open();
                    await entryStream.WriteAsync(coverBytes.Bytes);
                    coverDownloaded = true;
                    stats.ImagesDownloaded++;
                }
                else
                {
                    stats.ImagesFailed++;
                }

                cache.CoverUrl = game.Cover;
                cache.CoverDownloaded = coverDownloaded;
            }

            // Mark game as exported in database
            if (needsExport)
            {
                var gameToUpdate = await _context.Games.FindAsync(dbGame.Id);
                if (gameToUpdate != null)
                {
                    // Use explicit property update to avoid triggering UpdateTimestamps logic
                    _context.Entry(gameToUpdate).Property(g => g.ModifiedSinceExport).CurrentValue = false;
                    _context.Entry(gameToUpdate).Property(g => g.ModifiedSinceExport).IsModified = true;
                }
            }
        }

        // Save all cache changes
        await _context.SaveChangesAsync();
    }

    private async Task AddJsonToZip<T>(ZipArchive archive, string path, T data)
    {
        var json = JsonSerializer.Serialize(data, new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        var entry = archive.CreateEntry(path);
        using var entryStream = entry.Open();
        using var writer = new StreamWriter(entryStream, Encoding.UTF8);
        await writer.WriteAsync(json);
    }

    private Task<VerifiedImageContent?> SafeDownloadAsync(string url) =>
        _mediaDownloader.DownloadAsync(url, _httpContextAccessor.HttpContext?.RequestAborted ?? CancellationToken.None);

    private static string MakeSafeFolderName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return string.Empty;

        // Remove invalid characters
        var invalidChars = Path.GetInvalidFileNameChars();
        var safeName = string.Join("_", name.Split(invalidChars, StringSplitOptions.RemoveEmptyEntries));

        // Replace spaces with underscores
        safeName = safeName.Replace(" ", "_");

        // Remove multiple consecutive underscores
        safeName = Regex.Replace(safeName, "_+", "_");

        // Trim underscores from start and end
        return safeName.Trim('_');
    }

    private static bool ParseBool(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var lower = value.ToLowerInvariant().Trim();
        return lower == "true" || lower == "1" || lower == "yes";
    }

    private static int? ParseInt(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        if (int.TryParse(value, out var result))
            return result;

        return null;
    }
}
