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
    private readonly HttpClient _imageHttpClient;
    private readonly DataExportOptions _exportOptions;
    private readonly NetworkSyncOptions _syncOptions;
    private readonly GamesDbContext _context;
    private readonly ILogger<NetworkSyncService> _logger;
    private readonly Dictionary<string, bool> _cdnHealthCache = new();
    private readonly TimeSpan _cdnHealthCacheDuration = TimeSpan.FromMinutes(5);
    private DateTime _cdnHealthCacheExpiry = DateTime.MinValue;

    public NetworkSyncService(
        IHttpClientFactory httpClientFactory,
        IOptions<DataExportOptions> exportOptions,
        IOptions<NetworkSyncOptions> syncOptions,
        GamesDbContext context,
        ILogger<NetworkSyncService> logger)
    {
        _httpClient = httpClientFactory.CreateClient("TrustAllCerts");
        _imageHttpClient = httpClientFactory.CreateClient("ImageDownloader");
        _exportOptions = exportOptions.Value;
        _syncOptions = syncOptions.Value;
        _context = context;
        _logger = logger;
    }

    public async Task<NetworkSyncResult> SyncToNetworkAsync(int userId, string? authorizationHeader, bool fullSync = false)
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

            _logger.LogInformation("Starting network sync for user {UserId} to {Path} (fullSync: {FullSync})",
                userId, _syncOptions.NetworkPath, fullSync);

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
            await SyncBackupCsvAsync(userId, csvBytes);
            result.FilesWritten++;

            // 4. Sync Settings
            var settingsWritten = await SyncSettingsAsync(userId, records);
            result.FilesWritten += settingsWritten;

            // 5. Sync Games
            await SyncGamesAsync(userId, records, fullSync, result);

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

    private async Task SyncBackupCsvAsync(int userId, byte[] csvBytes)
    {
        var userPath = Path.Combine(_syncOptions.NetworkPath, userId.ToString());
        var backupsPath = Path.Combine(userPath, "Backups");
        Directory.CreateDirectory(backupsPath);

        var today = DateTime.UtcNow.ToString("yyyy-MM-dd");
        var baseFileName = $"database_full_export_{today}";

        // Find existing backups for today and determine next version number
        var existingFiles = Directory.GetFiles(backupsPath, $"{baseFileName}*.csv")
            .Select(Path.GetFileName)
            .ToList();

        int version = 1;
        string fileName;

        if (existingFiles.Count > 0)
        {
            // Find highest version number
            foreach (var file in existingFiles)
            {
                if (file == null) continue;

                // Match patterns: database_full_export_2025-11-30.csv or database_full_export_2025-11-30_v2.csv
                var match = System.Text.RegularExpressions.Regex.Match(file, @"_v(\d+)\.csv$");
                if (match.Success)
                {
                    var fileVersion = int.Parse(match.Groups[1].Value);
                    if (fileVersion >= version)
                        version = fileVersion + 1;
                }
                else if (file == $"{baseFileName}.csv")
                {
                    // First version exists without _v suffix
                    version = 2;
                }
            }

            fileName = $"{baseFileName}_v{version}.csv";
        }
        else
        {
            fileName = $"{baseFileName}.csv";
        }

        var filePath = Path.Combine(backupsPath, fileName);
        await File.WriteAllBytesAsync(filePath, csvBytes);
        _logger.LogInformation("Synced backup CSV: {FileName}", fileName);
    }

    private async Task<int> SyncSettingsAsync(int userId, List<ExportRecord> records)
    {
        var userPath = Path.Combine(_syncOptions.NetworkPath, userId.ToString());
        var settingsPath = Path.Combine(userPath, "Settings");
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
            _logger.LogDebug("All settings files are up to date");

        return settingsSynced;
    }

    private async Task SyncGamesAsync(int userId, List<ExportRecord> records, bool fullSync, NetworkSyncResult result)
    {
        var games = records.Where(r => r.Type == "Game").ToList();
        result.TotalGames = games.Count;

        _logger.LogInformation("Syncing {Count} games for user {UserId} (fullSync: {FullSync})", games.Count, userId, fullSync);

        // Load cache info
        var gameNames = games.Select(g => g.Name).ToList();
        var dbGames = await _context.Games
            .Where(g => gameNames.Contains(g.Name) && g.UserId == userId)
            .Select(g => new { g.Id, g.Name, g.ModifiedSinceExport, g.Logo, g.Cover })
            .ToListAsync();

        var exportCaches = await _context.GameExportCaches
            .Where(ec => dbGames.Select(g => g.Id).Contains(ec.GameId))
            .ToDictionaryAsync(ec => ec.GameId);

        var userPath = Path.Combine(_syncOptions.NetworkPath, userId.ToString());
        var gamesPath = Path.Combine(userPath, "Games");
        Directory.CreateDirectory(gamesPath);

        // Track failed images for retry at the end
        var failedImageRetries = new List<(ExportRecord game, int gameId, string gamePath, string imageType, string url, GameExportCache? cache)>();

        // Rate limiting: track requests per domain
        var domainLastRequest = new Dictionary<string, DateTime>();
        const int minDelayBetweenRequestsMs = 200; // 200ms between requests to same domain

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

            // Track failed images for this game
            var failedImageTypes = new List<string>();

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

                if (urlChanged && cache?.LogoUrl != null)
                {
                    // Delete old logo files when URL changes
                    DeleteOldImageFiles(gamePath, "logo");
                    _logger.LogInformation("Logo URL changed for '{Name}', downloading new image", game.Name);
                }

                // Check if URL is local (localhost or 192.168.0.32) - skip download
                bool isLocalUrl = game.Logo.Contains("localhost", StringComparison.OrdinalIgnoreCase) ||
                                  game.Logo.Contains("192.168.0.32", StringComparison.OrdinalIgnoreCase);

                if (isLocalUrl)
                {
                    // Mark as downloaded without actually downloading
                    if (cache != null) cache.LogoDownloaded = true;
                    result.ImagesSynced++;
                    _logger.LogDebug("Skipping local logo download for '{Name}'", game.Name);
                }
                else
                {
                    // Apply rate limiting
                    await ApplyRateLimitAsync(game.Logo, domainLastRequest, minDelayBetweenRequestsMs);

                    var logoBytes = await SafeDownloadAsync(game.Logo, attempt: 1, maxAttempts: 1);
                    if (logoBytes != null)
                    {
                        var extension = GetExtensionFromUrl(game.Logo);
                        var logoPath = Path.Combine(gamePath, $"logo{extension}");
                        await File.WriteAllBytesAsync(logoPath, logoBytes);
                        if (cache != null) cache.LogoDownloaded = true;
                        result.ImagesSynced++;
                        result.FilesWritten++;
                    }
                    else
                    {
                        if (cache != null) cache.LogoDownloaded = false;
                        result.ImagesFailed++;
                        failedImageTypes.Add("logo");
                        // Track for retry at the end
                        if (game.Logo != null)
                        {
                            failedImageRetries.Add((game, dbGame.Id, gamePath, "logo", game.Logo, cache));
                        }
                    }
                }

                if (cache != null) cache.LogoUrl = game.Logo;
            }

            // Sync cover
            if (!string.IsNullOrWhiteSpace(game.Cover) && (needsSync || coverNeedsSync))
            {
                bool isRetry = cache?.CoverUrl == game.Cover && !cache.CoverDownloaded;
                bool urlChanged = cache?.CoverUrl != game.Cover;

                if (urlChanged && cache?.CoverUrl != null)
                {
                    // Delete old cover files when URL changes
                    DeleteOldImageFiles(gamePath, "cover");
                    _logger.LogInformation("Cover URL changed for '{Name}', downloading new image", game.Name);
                }

                // Check if URL is local (localhost or 192.168.0.32) - skip download
                bool isLocalUrl = game.Cover.Contains("localhost", StringComparison.OrdinalIgnoreCase) ||
                                  game.Cover.Contains("192.168.0.32", StringComparison.OrdinalIgnoreCase);

                if (isLocalUrl)
                {
                    // Mark as downloaded without actually downloading
                    if (cache != null) cache.CoverDownloaded = true;
                    result.ImagesSynced++;
                    _logger.LogDebug("Skipping local cover download for '{Name}'", game.Name);
                }
                else
                {
                    // Apply rate limiting
                    await ApplyRateLimitAsync(game.Cover, domainLastRequest, minDelayBetweenRequestsMs);

                    var coverBytes = await SafeDownloadAsync(game.Cover, attempt: 1, maxAttempts: 1);
                    if (coverBytes != null)
                    {
                        var extension = GetExtensionFromUrl(game.Cover);
                        var coverPath = Path.Combine(gamePath, $"cover{extension}");
                        await File.WriteAllBytesAsync(coverPath, coverBytes);
                        if (cache != null) cache.CoverDownloaded = true;
                        result.ImagesSynced++;
                        result.FilesWritten++;
                    }
                    else
                    {
                        if (cache != null) cache.CoverDownloaded = false;
                        result.ImagesFailed++;
                        failedImageTypes.Add("cover");
                        // Track for retry at the end
                        if (game.Cover != null)
                        {
                            failedImageRetries.Add((game, dbGame.Id, gamePath, "cover", game.Cover, cache));
                        }
                    }
                }

                if (cache != null) cache.CoverUrl = game.Cover;
            }

            // Add to failed images list if any images failed
            if (failedImageTypes.Count > 0)
            {
                result.FailedImages.Add(new FailedImageInfo
                {
                    GameName = game.Name,
                    ImageTypes = failedImageTypes
                });
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

            // Save changes after each game to prevent long transactions
            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateException ex) when (ex.InnerException?.Message?.Contains("UNIQUE constraint") == true)
            {
                _logger.LogWarning("Skipping duplicate cache entry for game '{Name}'", game.Name);
                _context.ChangeTracker.Clear(); // Clear tracked entities to avoid further errors
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to save changes for game '{Name}': {Message}", game.Name, ex.Message);
                _context.ChangeTracker.Clear();
            }
        }

        // Retry failed images at the end with multiple passes
        if (failedImageRetries.Count > 0)
        {
            const int maxRetryPasses = 2; // 2 rondas de reintentos (reducido para evitar loops infinitos)
            const int retryDelayMs = 3000; // 3 segundos entre cada reintento

            var currentFailedRetries = failedImageRetries.ToList();

            for (int pass = 1; pass <= maxRetryPasses && currentFailedRetries.Count > 0; pass++)
            {
                _logger.LogInformation("Retry pass {Pass}/{MaxPasses} - Attempting {Count} failed images...",
                    pass, maxRetryPasses, currentFailedRetries.Count);

                var nextPassFailures = new List<(ExportRecord game, int gameId, string gamePath, string imageType, string url, GameExportCache? cache)>();

                foreach (var (game, gameId, gamePath, imageType, url, cache) in currentFailedRetries)
                {
                    _logger.LogInformation("Retrying {ImageType} for '{Name}' (pass {Pass})", imageType, game.Name, pass);
                    result.ImagesRetried++;

                    await Task.Delay(retryDelayMs); // Wait before each retry

                    var imageBytes = await SafeDownloadAsync(url, attempt: 1, maxAttempts: 1); // 1 intento por cada reintento
                    if (imageBytes != null)
                    {
                        var extension = GetExtensionFromUrl(url);
                        var imagePath = Path.Combine(gamePath, $"{imageType}{extension}");
                        await File.WriteAllBytesAsync(imagePath, imageBytes);

                        // Update cache
                        if (imageType == "logo")
                        {
                            if (cache != null) cache.LogoDownloaded = true;
                        }
                        else if (imageType == "cover")
                        {
                            if (cache != null) cache.CoverDownloaded = true;
                        }

                        // Update statistics - move from failed to synced
                        result.ImagesFailed--;
                        result.ImagesSynced++;
                        result.FilesWritten++;

                        // Remove from failed images report
                        var failedImage = result.FailedImages.FirstOrDefault(f => f.GameName == game.Name);
                        if (failedImage != null)
                        {
                            failedImage.ImageTypes.Remove(imageType);
                            if (failedImage.ImageTypes.Count == 0)
                            {
                                result.FailedImages.Remove(failedImage);
                            }
                        }

                        _logger.LogInformation("✓ Successfully retried {ImageType} for '{Name}' on pass {Pass}", imageType, game.Name, pass);
                    }
                    else
                    {
                        // Falló, guardar para la siguiente ronda
                        nextPassFailures.Add((game, gameId, gamePath, imageType, url, cache));
                        _logger.LogWarning("✗ Failed {ImageType} for '{Name}' on pass {Pass}, will retry in next pass", imageType, game.Name, pass);
                    }
                }

                // Preparar para la siguiente ronda
                currentFailedRetries = nextPassFailures;

                if (currentFailedRetries.Count > 0 && pass < maxRetryPasses)
                {
                    _logger.LogInformation("Pass {Pass} complete. {Count} images still failing. Waiting before next pass...",
                        pass, currentFailedRetries.Count);

                    // Save changes after each retry pass
                    try
                    {
                        await _context.SaveChangesAsync();
                        _logger.LogDebug("Saved changes after retry pass {Pass}", pass);
                    }
                    catch (DbUpdateException ex) when (ex.InnerException?.Message?.Contains("UNIQUE constraint") == true)
                    {
                        _logger.LogWarning("Skipping duplicate cache entries in retry pass {Pass}", pass);
                        _context.ChangeTracker.Clear();
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to save changes after retry pass {Pass}: {Message}", pass, ex.Message);
                        _context.ChangeTracker.Clear();
                    }
                    await Task.Delay(3000); // 3 segundos entre rondas completas
                }
            }

            if (currentFailedRetries.Count > 0)
            {
                _logger.LogWarning("After {MaxPasses} retry passes, {Count} images still failed permanently",
                    maxRetryPasses, currentFailedRetries.Count);
            }
            else
            {
                _logger.LogInformation("All failed images successfully recovered after retry passes!");
            }
        }

        // Final save to ensure all changes are persisted
        try
        {
            await _context.SaveChangesAsync();
            _logger.LogInformation("Final save completed successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save final changes: {Message}", ex.Message);
            throw;
        }
    }

    private async Task ApplyRateLimitAsync(string url, Dictionary<string, DateTime> domainLastRequest, int minDelayMs)
    {
        try
        {
            var uri = new Uri(url);
            var domain = uri.Host;

            if (domainLastRequest.TryGetValue(domain, out var lastRequest))
            {
                var timeSinceLastRequest = DateTime.UtcNow - lastRequest;
                var remainingDelay = minDelayMs - (int)timeSinceLastRequest.TotalMilliseconds;

                if (remainingDelay > 0)
                {
                    await Task.Delay(remainingDelay);
                }
            }

            domainLastRequest[domain] = DateTime.UtcNow;
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Error applying rate limit for URL '{Url}': {Error}", url, ex.Message);
        }
    }

    private async Task<bool> WriteJsonToFileIfChangedAsync<T>(string filePath, T data)
    {
        var jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            // Ensure consistent ordering
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        var json = JsonSerializer.Serialize(data, jsonOptions);

        // Check if file exists and compare content
        if (File.Exists(filePath))
        {
            var existingJson = await File.ReadAllTextAsync(filePath, Encoding.UTF8);

            // Normalize both strings by removing BOM and trimming whitespace
            var normalizedNew = json.Trim().TrimStart('\uFEFF');
            var normalizedExisting = existingJson.Trim().TrimStart('\uFEFF');

            // Compare content hash to detect changes
            var newHash = ComputeHash(normalizedNew);
            var existingHash = ComputeHash(normalizedExisting);

            if (newHash == existingHash)
            {
                _logger.LogDebug("Skipping {FileName} - no changes detected", Path.GetFileName(filePath));
                return false;
            }

            _logger.LogDebug("Updating {FileName} - content changed", Path.GetFileName(filePath));
        }
        else
        {
            _logger.LogDebug("Creating new {FileName}", Path.GetFileName(filePath));
        }

        await File.WriteAllTextAsync(filePath, json, Encoding.UTF8);
        return true;
    }

    private static string ComputeHash(string input)
    {
        using var sha256 = System.Security.Cryptography.SHA256.Create();
        var bytes = Encoding.UTF8.GetBytes(input);
        var hash = sha256.ComputeHash(bytes);
        return Convert.ToBase64String(hash);
    }

    private async Task CheckCdnHealthAsync()
    {
        // Check if cache is still valid
        if (DateTime.UtcNow < _cdnHealthCacheExpiry)
        {
            return;
        }

        _logger.LogInformation("Checking CDN health...");
        _cdnHealthCache.Clear();

        // List of CDNs to check
        var cdnsToCheck = new[]
        {
            "cdn2.steamgriddb.com",
            "cdn.steamgriddb.com",
            "images.igdb.com"
        };

        foreach (var cdn in cdnsToCheck)
        {
            try
            {
                // Try a quick HEAD request to check if CDN is reachable
                var testUrl = $"https://{cdn}/favicon.ico";
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                var request = new HttpRequestMessage(HttpMethod.Head, testUrl);
                var response = await _imageHttpClient.SendAsync(request, cts.Token);

                _cdnHealthCache[cdn] = response.IsSuccessStatusCode || response.StatusCode == System.Net.HttpStatusCode.NotFound;

                if (_cdnHealthCache[cdn])
                {
                    _logger.LogInformation("CDN {Cdn} is reachable", cdn);
                }
                else
                {
                    _logger.LogWarning("CDN {Cdn} returned error: {StatusCode}", cdn, response.StatusCode);
                }
            }
            catch (Exception ex)
            {
                _cdnHealthCache[cdn] = false;
                _logger.LogWarning(ex, "CDN {Cdn} is unreachable or timed out", cdn);
            }
        }

        _cdnHealthCacheExpiry = DateTime.UtcNow.Add(_cdnHealthCacheDuration);

        var healthyCdns = _cdnHealthCache.Count(x => x.Value);
        var totalCdns = _cdnHealthCache.Count;
        _logger.LogInformation("CDN health check complete: {Healthy}/{Total} CDNs available", healthyCdns, totalCdns);
    }

    private bool IsCdnHealthy(string url)
    {
        try
        {
            var uri = new Uri(url);
            var host = uri.Host;

            // If we have health info for this CDN, use it
            if (_cdnHealthCache.TryGetValue(host, out bool isHealthy))
            {
                return isHealthy;
            }

            // If no health info, assume it's healthy (will fail on first attempt and get cached)
            return true;
        }
        catch
        {
            return true; // Assume healthy if we can't parse URL
        }
    }

    private async Task<byte[]?> SafeDownloadAsync(string url, int attempt = 1, int maxAttempts = 3)
    {
        // Check if CDN is healthy before attempting download
        if (!IsCdnHealthy(url))
        {
            _logger.LogWarning("Skipping download from unhealthy CDN: {Url}", url);
            return null;
        }

        int maxRetries = maxAttempts;

        for (int currentAttempt = attempt; currentAttempt <= maxRetries; currentAttempt++)
        {
            try
            {
                _logger.LogDebug("Downloading image from {Url} (attempt {Attempt}/{MaxRetries})", url, currentAttempt, maxRetries);

                var response = await _imageHttpClient.GetAsync(url);

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning(
                        "Failed to download image from {Url} - HTTP {StatusCode} {Reason}",
                        url,
                        (int)response.StatusCode,
                        response.ReasonPhrase
                    );

                    // Don't retry on 404 or 403
                    if (response.StatusCode == System.Net.HttpStatusCode.NotFound ||
                        response.StatusCode == System.Net.HttpStatusCode.Forbidden)
                    {
                        return null;
                    }

                    // Retry on server errors or timeouts
                    if (currentAttempt < maxRetries)
                    {
                        await Task.Delay(TimeSpan.FromSeconds(currentAttempt * 2));
                        continue;
                    }

                    return null;
                }

                var bytes = await response.Content.ReadAsByteArrayAsync();
                _logger.LogDebug("Successfully downloaded {Size} bytes from {Url}", bytes.Length, url);
                return bytes;
            }
            catch (HttpRequestException ex)
            {
                _logger.LogWarning(
                    ex,
                    "HTTP request failed for {Url} (attempt {Attempt}/{MaxRetries}): {Message}",
                    url,
                    currentAttempt,
                    maxRetries,
                    ex.Message
                );

                if (currentAttempt < maxRetries)
                {
                    await Task.Delay(TimeSpan.FromSeconds(currentAttempt * 2));
                    continue;
                }
            }
            catch (TaskCanceledException ex)
            {
                _logger.LogWarning(
                    ex,
                    "Request timeout for {Url} (attempt {Attempt}/{MaxRetries})",
                    url,
                    currentAttempt,
                    maxRetries
                );

                // Mark CDN as unhealthy after timeout
                if (currentAttempt >= maxRetries)
                {
                    try
                    {
                        var uri = new Uri(url);
                        _cdnHealthCache[uri.Host] = false;
                        _logger.LogWarning("Marking CDN {Host} as unhealthy due to repeated timeouts", uri.Host);
                    }
                    catch { }
                }

                if (currentAttempt < maxRetries)
                {
                    await Task.Delay(TimeSpan.FromSeconds(currentAttempt * 2));
                    continue;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Unexpected error downloading from {Url} (attempt {Attempt}/{MaxRetries}): {Message}",
                    url,
                    currentAttempt,
                    maxRetries,
                    ex.Message
                );

                if (currentAttempt < maxRetries)
                {
                    await Task.Delay(TimeSpan.FromSeconds(currentAttempt * 2));
                    continue;
                }
            }
        }

        return null;
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

        // First, normalize Unicode characters and remove accents/diacritics
        var normalizedString = name.Normalize(System.Text.NormalizationForm.FormD);
        var stringBuilder = new System.Text.StringBuilder();

        foreach (var c in normalizedString)
        {
            var unicodeCategory = System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c);
            // Keep only letters, digits, spaces, and some safe punctuation (including underscore)
            if (unicodeCategory != System.Globalization.UnicodeCategory.NonSpacingMark)
            {
                if (char.IsLetterOrDigit(c) || c == ' ' || c == '-' || c == '_' || c == '.' || c == '(' || c == ')')
                {
                    stringBuilder.Append(c);
                }
            }
        }

        var safeName = stringBuilder.ToString().Normalize(System.Text.NormalizationForm.FormC);

        // Remove any remaining invalid filename characters
        var invalidChars = Path.GetInvalidFileNameChars();
        foreach (var invalidChar in invalidChars)
        {
            safeName = safeName.Replace(invalidChar, '_');
        }

        // Replace spaces with underscores
        safeName = safeName.Replace(" ", "_");

        // Remove multiple consecutive underscores
        safeName = System.Text.RegularExpressions.Regex.Replace(safeName, "_+", "_");

        // Remove leading/trailing underscores
        safeName = safeName.Trim('_');

        // Limit length to avoid Windows MAX_PATH issues
        if (safeName.Length > 200)
            safeName = safeName.Substring(0, 200).TrimEnd('_');

        return safeName;
    }

    private static string GetExtensionFromUrl(string url)
    {
        try
        {
            var uri = new Uri(url);
            var path = uri.AbsolutePath;

            // Remove size suffixes like :large, :medium, :small from SteamGridDB URLs
            // Example: /icon/abc123.png:large -> /icon/abc123.png
            var colonIndex = path.LastIndexOf(':');
            if (colonIndex > 0)
            {
                var afterColon = path.Substring(colonIndex + 1);
                // Check if it's a size suffix (not a drive letter like C:)
                if (afterColon.Equals("large", StringComparison.OrdinalIgnoreCase) ||
                    afterColon.Equals("medium", StringComparison.OrdinalIgnoreCase) ||
                    afterColon.Equals("small", StringComparison.OrdinalIgnoreCase))
                {
                    path = path.Substring(0, colonIndex);
                }
            }

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
