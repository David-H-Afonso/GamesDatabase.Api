using System.Globalization;
using System.Text;
using System.Text.Json;
using CsvHelper;
using GamesDatabase.Api.Configuration;
using GamesDatabase.Api.Data;
using GamesDatabase.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace GamesDatabase.Api.Services;

public class NetworkSyncService : INetworkSyncService
{
    private readonly HttpClient _httpClient;
    private readonly DataExportOptions _exportOptions;
    private readonly NetworkSyncOptions _syncOptions;
    private readonly GamesDbContext _context;
    private readonly ILogger<NetworkSyncService> _logger;

    public NetworkSyncService(
        IHttpClientFactory httpClientFactory,
        IOptions<DataExportOptions> exportOptions,
        IOptions<NetworkSyncOptions> syncOptions,
        GamesDbContext context,
        ILogger<NetworkSyncService> logger)
    {
        _httpClient = httpClientFactory.CreateClient("TrustAllCerts");
        _exportOptions = exportOptions.Value;
        _syncOptions = syncOptions.Value;
        _context = context;
        _logger = logger;
    }

    public async Task<NetworkSyncResult> SyncToNetworkAsync(string? authorizationHeader, bool fullSync = false)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var result = new NetworkSyncResult { Success = false };

        try
        {
            // Validate configuration
            if (!_syncOptions.Enabled)
            {
                result.ErrorMessage = "Network sync is disabled in configuration";
                _logger.LogWarning("Network sync attempted but it's disabled");
                return result;
            }

            if (string.IsNullOrWhiteSpace(_syncOptions.NetworkPath))
            {
                result.ErrorMessage = "Network path is not configured";
                _logger.LogError("Network path is empty");
                return result;
            }

            _logger.LogInformation("Starting network sync to {Path} (fullSync: {FullSync})", 
                _syncOptions.NetworkPath, fullSync);

            // Verify network path is accessible
            if (!Directory.Exists(_syncOptions.NetworkPath))
            {
                result.ErrorMessage = $"Network path not accessible: {_syncOptions.NetworkPath}";
                _logger.LogError("Cannot access network path: {Path}", _syncOptions.NetworkPath);
                return result;
            }

            // 1. Download CSV
            _logger.LogInformation("Downloading full export CSV");
            if (!string.IsNullOrWhiteSpace(authorizationHeader))
            {
                _httpClient.DefaultRequestHeaders.Remove("Authorization");
                _httpClient.DefaultRequestHeaders.Add("Authorization", authorizationHeader);
            }

            var csvBytes = await _httpClient.GetByteArrayAsync(_exportOptions.FullExportUrl);
            var csvContent = Encoding.UTF8.GetString(csvBytes);

            // Remove BOM if present
            if (csvContent.StartsWith("\uFEFF"))
            {
                csvContent = csvContent.Substring(1);
            }

            // 2. Parse CSV
            var records = ParseCsv(csvContent);
            _logger.LogInformation("Parsed {Count} records from CSV", records.Count);

            // 3. Sync Backup CSV
            await SyncBackupCsvAsync(csvBytes);
            result.FilesWritten++;

            // 4. Sync Settings
            await SyncSettingsAsync(records);
            result.FilesWritten += 5; // 5 settings files

            // 5. Sync Games
            await SyncGamesAsync(records, fullSync, result);

            stopwatch.Stop();
            result.Success = true;
            result.ElapsedTime = stopwatch.Elapsed;

            _logger.LogInformation(
                "Network sync completed in {ElapsedSeconds:F2}s - " +
                "Total: {Total}, Synced: {Synced}, Skipped: {Skipped}, " +
                "Images: {ImagesSynced} synced ({ImagesRetried} retried, {ImagesFailed} failed), " +
                "Files written: {FilesWritten}",
                result.ElapsedTime.TotalSeconds,
                result.TotalGames,
                result.GamesSynced,
                result.GamesSkipped,
                result.ImagesSynced,
                result.ImagesRetried,
                result.ImagesFailed,
                result.FilesWritten
            );
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            result.Success = false;
            result.ErrorMessage = ex.Message;
            result.ElapsedTime = stopwatch.Elapsed;
            _logger.LogError(ex, "Network sync failed");
        }

        return result;
    }

    private List<ExportRecord> ParseCsv(string csvContent)
    {
        using var reader = new StringReader(csvContent);
        using var csv = new CsvReader(reader, CultureInfo.InvariantCulture);
        return csv.GetRecords<ExportRecord>().ToList();
    }

    private async Task SyncBackupCsvAsync(byte[] csvBytes)
    {
        var backupsPath = Path.Combine(_syncOptions.NetworkPath, "Backups");
        Directory.CreateDirectory(backupsPath);

        var fileName = $"database_full_export_{DateTime.UtcNow:yyyy-MM-dd}.csv";
        var filePath = Path.Combine(backupsPath, fileName);

        await File.WriteAllBytesAsync(filePath, csvBytes);
        _logger.LogInformation("Synced backup CSV: {FileName}", fileName);
    }

    private async Task SyncSettingsAsync(List<ExportRecord> records)
    {
        var settingsPath = Path.Combine(_syncOptions.NetworkPath, "Settings");
        Directory.CreateDirectory(settingsPath);

        int settingsSynced = 0;

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
        if (await WriteJsonToFileIfChangedAsync(Path.Combine(settingsPath, "Platforms.json"), platforms))
            settingsSynced++;

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
        if (await WriteJsonToFileIfChangedAsync(Path.Combine(settingsPath, "Status.json"), statuses))
            settingsSynced++;

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
        if (await WriteJsonToFileIfChangedAsync(Path.Combine(settingsPath, "PlayWith.json"), playWiths))
            settingsSynced++;

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
        if (await WriteJsonToFileIfChangedAsync(Path.Combine(settingsPath, "PlayedStatus.json"), playedStatuses))
            settingsSynced++;

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
        if (await WriteJsonToFileIfChangedAsync(Path.Combine(settingsPath, "Views.json"), views))
            settingsSynced++;

        if (settingsSynced > 0)
            _logger.LogInformation("Synced {Count} settings files that changed", settingsSynced);
        else
            _logger.LogDebug("No settings changes detected, skipping sync");
    }

    private async Task SyncGamesAsync(List<ExportRecord> records, bool fullSync, NetworkSyncResult result)
    {
        var games = records.Where(r => r.Type == "Game").ToList();
        result.TotalGames = games.Count;

        _logger.LogInformation("Syncing {Count} games (fullSync: {FullSync})", games.Count, fullSync);

        // Load cache info
        var gameNames = games.Select(g => g.Name).ToList();
        var dbGames = await _context.Games
            .Where(g => gameNames.Contains(g.Name))
            .Select(g => new { g.Id, g.Name, g.ModifiedSinceExport, g.Logo, g.Cover })
            .ToListAsync();

        var exportCaches = await _context.GameExportCaches
            .Where(ec => dbGames.Select(g => g.Id).Contains(ec.GameId))
            .ToDictionaryAsync(ec => ec.GameId);

        var gamesPath = Path.Combine(_syncOptions.NetworkPath, "Games");
        Directory.CreateDirectory(gamesPath);

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

            var gamePath = Path.Combine(gamesPath, folderName);
            Directory.CreateDirectory(gamePath);

            // Check if sync needed
            var cache = exportCaches.GetValueOrDefault(dbGame.Id);
            bool needsSync = fullSync || dbGame.ModifiedSinceExport || cache == null;

            // Check images - sync if URL changed or if previous download failed
            bool logoNeedsSync = !string.IsNullOrWhiteSpace(game.Logo) &&
                                 (cache == null || cache.LogoUrl != game.Logo || !cache.LogoDownloaded);
            bool coverNeedsSync = !string.IsNullOrWhiteSpace(game.Cover) &&
                                  (cache == null || cache.CoverUrl != game.Cover || !cache.CoverDownloaded);

            if (!needsSync && !logoNeedsSync && !coverNeedsSync)
            {
                _logger.LogDebug("Skipping '{Name}' - no changes since last sync", game.Name);
                result.GamesSkipped++;
                continue;
            }

            result.GamesSynced++;

            // Sync info.json
            if (needsSync)
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
                if (await WriteJsonToFileIfChangedAsync(Path.Combine(gamePath, "info.json"), gameInfo))
                {
                    result.FilesWritten++;
                }
            }

            // Initialize or update cache
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

            // Sync logo
            if (!string.IsNullOrWhiteSpace(game.Logo) && (needsSync || logoNeedsSync))
            {
                bool isRetry = cache?.LogoUrl == game.Logo && !cache.LogoDownloaded;
                bool urlChanged = cache?.LogoUrl != game.Logo;
                
                if (isRetry)
                {
                    _logger.LogInformation("Retrying logo download for '{Name}'", game.Name);
                    result.ImagesRetried++;
                }
                else if (urlChanged && cache?.LogoUrl != null)
                {
                    // Delete old logo files when URL changes
                    DeleteOldImageFiles(gamePath, "logo");
                    _logger.LogInformation("Logo URL changed for '{Name}', downloading new image", game.Name);
                }

                var logoBytes = await SafeDownloadAsync(game.Logo);
                if (logoBytes != null)
                {
                    var extension = GetExtensionFromUrl(game.Logo);
                    var logoPath = Path.Combine(gamePath, $"logo{extension}");
                    // Overwrite existing file
                    await File.WriteAllBytesAsync(logoPath, logoBytes);
                    cache.LogoDownloaded = true;
                    result.ImagesSynced++;
                    result.FilesWritten++;
                }
                else
                {
                    cache.LogoDownloaded = false;
                    result.ImagesFailed++;
                }

                cache.LogoUrl = game.Logo;
            }

            // Sync cover
            if (!string.IsNullOrWhiteSpace(game.Cover) && (needsSync || coverNeedsSync))
            {
                bool isRetry = cache?.CoverUrl == game.Cover && !cache.CoverDownloaded;
                bool urlChanged = cache?.CoverUrl != game.Cover;
                
                if (isRetry)
                {
                    _logger.LogInformation("Retrying cover download for '{Name}'", game.Name);
                    result.ImagesRetried++;
                }
                else if (urlChanged && cache?.CoverUrl != null)
                {
                    // Delete old cover files when URL changes
                    DeleteOldImageFiles(gamePath, "cover");
                    _logger.LogInformation("Cover URL changed for '{Name}', downloading new image", game.Name);
                }

                var coverBytes = await SafeDownloadAsync(game.Cover);
                if (coverBytes != null)
                {
                    var extension = GetExtensionFromUrl(game.Cover);
                    var coverPath = Path.Combine(gamePath, $"cover{extension}");
                    // Overwrite existing file
                    await File.WriteAllBytesAsync(coverPath, coverBytes);
                    cache.CoverDownloaded = true;
                    result.ImagesSynced++;
                    result.FilesWritten++;
                }
                else
                {
                    cache.CoverDownloaded = false;
                    result.ImagesFailed++;
                }

                cache.CoverUrl = game.Cover;
            }

            // Mark as synced
            if (needsSync)
            {
                var gameToUpdate = await _context.Games.FindAsync(dbGame.Id);
                if (gameToUpdate != null)
                {
                    _context.Entry(gameToUpdate).Property(g => g.ModifiedSinceExport).CurrentValue = false;
                    _context.Entry(gameToUpdate).Property(g => g.ModifiedSinceExport).IsModified = true;
                }
            }
        }

        await _context.SaveChangesAsync();
    }

    private async Task<bool> WriteJsonToFileIfChangedAsync<T>(string filePath, T data)
    {
        var json = JsonSerializer.Serialize(data, new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        // Check if file exists and compare content
        if (File.Exists(filePath))
        {
            var existingJson = await File.ReadAllTextAsync(filePath, Encoding.UTF8);
            
            // Compare content hash to detect changes
            var newHash = ComputeHash(json);
            var existingHash = ComputeHash(existingJson);
            
            if (newHash == existingHash)
            {
                _logger.LogDebug("Skipping {FileName} - no changes detected", Path.GetFileName(filePath));
                return false;
            }
        }

        await File.WriteAllTextAsync(filePath, json, Encoding.UTF8);
        _logger.LogDebug("Updated {FileName}", Path.GetFileName(filePath));
        return true;
    }

    private static string ComputeHash(string input)
    {
        using var sha256 = System.Security.Cryptography.SHA256.Create();
        var bytes = Encoding.UTF8.GetBytes(input);
        var hash = sha256.ComputeHash(bytes);
        return Convert.ToBase64String(hash);
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

    private static void DeleteOldImageFiles(string gamePath, string prefix)
    {
        try
        {
            var imageExtensions = new[] { ".png", ".jpg", ".jpeg", ".gif", ".webp", ".bmp" };
            foreach (var ext in imageExtensions)
            {
                var filePath = Path.Combine(gamePath, $"{prefix}{ext}");
                if (File.Exists(filePath))
                {
                    File.Delete(filePath);
                }
            }
        }
        catch
        {
            // Ignore deletion errors
        }
    }

    private static string MakeSafeFolderName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return string.Empty;

        var invalidChars = Path.GetInvalidFileNameChars();
        var safeName = string.Join("_", name.Split(invalidChars, StringSplitOptions.RemoveEmptyEntries));
        safeName = safeName.Replace(" ", "_");
        safeName = System.Text.RegularExpressions.Regex.Replace(safeName, "_+", "_");
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

        return ".png";
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
