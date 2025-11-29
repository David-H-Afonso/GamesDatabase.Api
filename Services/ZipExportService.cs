using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using CsvHelper;
using GamesDatabase.Api.Configuration;
using GamesDatabase.Api.Data;
using GamesDatabase.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace GamesDatabase.Api.Services;

public class ZipExportService : IZipExportService
{
    private readonly HttpClient _httpClient;
    private readonly DataExportOptions _options;
    private readonly GamesDbContext _context;
    private readonly ILogger<ZipExportService> _logger;

    public ZipExportService(
        IHttpClientFactory httpClientFactory,
        IOptions<DataExportOptions> options,
        GamesDbContext context,
        ILogger<ZipExportService> logger)
    {
        _httpClient = httpClientFactory.CreateClient("TrustAllCerts");
        _options = options.Value;
        _context = context;
        _logger = logger;
    }

    public async Task<ZipExportResult> BuildZipAsync(string? authorizationHeader, bool fullExport = false)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var stats = new ZipExportResult();

        _logger.LogInformation("Starting ZIP export (fullExport: {FullExport})", fullExport);

        // 1. Download CSV
        _logger.LogInformation("Downloading full export CSV from {Url}", _options.FullExportUrl);

        // Add authorization header if provided
        if (!string.IsNullOrWhiteSpace(authorizationHeader))
        {
            _httpClient.DefaultRequestHeaders.Remove("Authorization");
            _httpClient.DefaultRequestHeaders.Add("Authorization", authorizationHeader);
        }

        var csvBytes = await _httpClient.GetByteArrayAsync(_options.FullExportUrl);
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
            await AddGamesAsync(archive, records, fullExport, stats);
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
        if (csvContent.StartsWith("\uFEFF"))
        {
            csvContent = csvContent.Substring(1);
        }

        using var reader = new StringReader(csvContent);
        using var csv = new CsvReader(reader, CultureInfo.InvariantCulture);

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
                Color = r.Color ?? "",
                FiltersJson = r.FiltersJson ?? "",
                SortingJson = r.SortingJson ?? "",
                IsDefault = ParseBool(r.IsDefault),
                IsPublic = ParseBool(r.IsPublic),
                CreatedBy = r.CreatedBy ?? ""
            })
            .ToList();
        await AddJsonToZip(archive, "Games Database/Settings/Views.json", views);

        _logger.LogInformation("Added settings files");
    }

    private async Task AddGamesAsync(ZipArchive archive, List<ExportRecord> records, bool fullExport, ZipExportResult stats)
    {
        var games = records.Where(r => r.Type == "Game").ToList();
        stats.TotalGames = games.Count;

        _logger.LogInformation("Processing {Count} games (fullExport: {FullExport})", games.Count, fullExport);

        // Load all game IDs and their cache status
        var gameNames = games.Select(g => g.Name).ToList();
        var dbGames = await _context.Games
            .Where(g => gameNames.Contains(g.Name))
            .Select(g => new { g.Id, g.Name, g.ModifiedSinceExport, g.Logo, g.Cover })
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
            bool needsExport = fullExport || dbGame.ModifiedSinceExport || cache == null;

            // Check if images need retry
            bool logoNeedsRetry = !string.IsNullOrWhiteSpace(game.Logo) &&
                                  (cache == null || (!cache.LogoDownloaded && cache.LogoUrl == game.Logo));
            bool coverNeedsRetry = !string.IsNullOrWhiteSpace(game.Cover) &&
                                   (cache == null || (!cache.CoverDownloaded && cache.CoverUrl == game.Cover));

            if (!needsExport && !logoNeedsRetry && !coverNeedsRetry)
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
                    Status = game.Status ?? "",
                    Platform = game.Platform ?? "",
                    PlayWith = game.PlayWith ?? "",
                    PlayedStatus = game.PlayedStatus ?? "",
                    Released = game.Released ?? "",
                    Started = game.Started ?? "",
                    Finished = game.Finished ?? "",
                    Score = game.Score ?? "",
                    Critic = game.Critic ?? "",
                    Grade = game.Grade ?? "",
                    Completion = game.Completion ?? "",
                    Story = game.Story ?? "",
                    Comment = game.Comment ?? "",
                    Description = game.Description ?? ""
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
                    var extension = GetExtensionFromUrl(game.Logo);
                    var entry = archive.CreateEntry($"{basePath}/logo{extension}");
                    using var entryStream = entry.Open();
                    await entryStream.WriteAsync(logoBytes);
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

            // Download and add cover
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
                    var extension = GetExtensionFromUrl(game.Cover);
                    var entry = archive.CreateEntry($"{basePath}/cover{extension}");
                    using var entryStream = entry.Open();
                    await entryStream.WriteAsync(coverBytes);
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

    private async Task<byte[]?> SafeDownloadAsync(string url)
    {
        try
        {
            _logger.LogDebug("Downloading image from {Url}", url);
            return await _httpClient.GetByteArrayAsync(url);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to download image from {Url}", url);
            return null;
        }
    }

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

    private static string GetExtensionFromUrl(string url)
    {
        try
        {
            var uri = new Uri(url);
            var path = uri.AbsolutePath;
            var extension = Path.GetExtension(path);

            if (!string.IsNullOrWhiteSpace(extension))
                return extension;
        }
        catch
        {
            // Ignore parsing errors
        }

        return ".png"; // Default extension
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
