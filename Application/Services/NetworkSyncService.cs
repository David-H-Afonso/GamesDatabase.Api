using GamesDatabase.Api.Application.Interfaces;
using System.Globalization;
using System.Text;
using System.Text.Json;
using CsvHelper;
using GamesDatabase.Api.Configuration;
using GamesDatabase.Api.Infrastructure.Persistence;
using GamesDatabase.Api.Domain.Entities;
using GamesDatabase.Api.Application.Mapping;
using GamesDatabase.Api.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace GamesDatabase.Api.Application.Services;

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
    private static readonly HashSet<string> EditionTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        "anniversary", "complete", "cut", "definitive", "deluxe", "director", "edition",
        "enhanced", "final", "goty", "hd", "remake", "remaster", "remastered",
        "redux", "special", "ultimate"
    };

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

            // Authenticate to the UNC share if credentials are configured (Windows Samba)
            var authError = NetworkPathHelper.EnsureAuthenticated(
                _syncOptions.NetworkPath,
                _syncOptions.Username,
                _syncOptions.Password);
            if (authError != null)
                _logger.LogWarning("Network path authentication warning: {Error}", authError);

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
                Logo = r.Logo ?? "",
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
                Description = r.Description ?? "",
                FiltersJson = r.FiltersJson ?? "{}",
                SortingJson = r.SortingJson ?? "",
                IsPublic = ParseBool(r.IsPublic),
                CreatedBy = r.CreatedBy ?? "",
                SortOrder = ParseInt(r.SortOrder) ?? 999
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

            var folderName = FolderNameHelper.MakeSafeFolderName(game.Name);
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
                    CriticProvider = game.CriticProvider ?? "",
                    Grade = game.Grade ?? "",
                    Completion = game.Completion ?? "",
                    Story = game.Story ?? "",
                    Comment = game.Comment ?? "",
                    Description = game.Description ?? "",
                    SteamAppId = game.SteamAppId ?? "",
                    SteamPlaytimeForever = game.SteamPlaytimeForever ?? "",
                    SteamPlaytime2Weeks = game.SteamPlaytime2Weeks ?? "",
                    SteamLastSynced = game.SteamLastSynced ?? "",
                    ManualPlaytimeMinutes = game.ManualPlaytimeMinutes ?? ""
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
                bool urlChanged = cache?.LogoUrl != game.Logo;

                // Check if URL points to our own image proxy (self-referencing URL)
                // This includes localhost, LAN IPs, and any URL containing /game-images/ that
                // resolves to our own NAS storage
                bool isSelfReferencing = IsSelfReferencingUrl(game.Logo);

                if (isSelfReferencing)
                {
                    // Self-referencing URL: the image already lives on the NAS.
                    // Verify the file actually exists; if so, mark as downloaded.
                    // NEVER delete or re-download these.
                    bool fileExists = ImageFileExistsOnDisk(gamePath, "logo");
                    if (fileExists)
                    {
                        if (cache != null) cache.LogoDownloaded = true;
                        result.ImagesSynced++;
                        _logger.LogDebug("Skipping self-referencing logo for '{Name}' - file exists on NAS", game.Name);
                    }
                    else
                    {
                        if (cache != null) cache.LogoDownloaded = false;
                        result.ImagesFailed++;
                        failedImageTypes.Add("logo");
                        _logger.LogWarning("Self-referencing logo for '{Name}' but file missing on NAS", game.Name);
                    }
                }
                else
                {
                    // External URL: download FIRST, only delete old files on success
                    await ApplyRateLimitAsync(game.Logo, domainLastRequest, minDelayBetweenRequestsMs);

                    var logoBytes = await SafeDownloadAsync(game.Logo, attempt: 1, maxAttempts: 1);
                    if (logoBytes != null)
                    {
                        // Download succeeded - now safe to replace old files
                        if (urlChanged && cache?.LogoUrl != null)
                        {
                            DeleteOldImageFiles(gamePath, "logo");
                        }
                        var extension = GetExtensionFromUrl(game.Logo);
                        var logoPath = Path.Combine(gamePath, $"logo{extension}");
                        await File.WriteAllBytesAsync(logoPath, logoBytes);
                        if (cache != null) cache.LogoDownloaded = true;
                        result.ImagesSynced++;
                        result.FilesWritten++;
                        _logger.LogDebug("Downloaded logo for '{Name}'", game.Name);
                    }
                    else
                    {
                        // Download failed - preserve existing files, never delete
                        if (cache != null) cache.LogoDownloaded = false;
                        result.ImagesFailed++;
                        failedImageTypes.Add("logo");
                        if (game.Logo != null)
                        {
                            failedImageRetries.Add((game, dbGame.Id, gamePath, "logo", game.Logo, cache));
                        }
                        _logger.LogWarning("Failed to download logo for '{Name}' - existing files preserved", game.Name);
                    }
                }

                if (cache != null) cache.LogoUrl = game.Logo;
            }

            // Sync cover
            if (!string.IsNullOrWhiteSpace(game.Cover) && (needsSync || coverNeedsSync))
            {
                bool urlChanged = cache?.CoverUrl != game.Cover;

                // Check if URL points to our own image proxy (self-referencing URL)
                bool isSelfReferencing = IsSelfReferencingUrl(game.Cover);

                if (isSelfReferencing)
                {
                    // Self-referencing URL: the image already lives on the NAS.
                    // Verify the file actually exists; if so, mark as downloaded.
                    // NEVER delete or re-download these.
                    bool fileExists = ImageFileExistsOnDisk(gamePath, "cover");
                    if (fileExists)
                    {
                        if (cache != null) cache.CoverDownloaded = true;
                        result.ImagesSynced++;
                        _logger.LogDebug("Skipping self-referencing cover for '{Name}' - file exists on NAS", game.Name);
                    }
                    else
                    {
                        if (cache != null) cache.CoverDownloaded = false;
                        result.ImagesFailed++;
                        failedImageTypes.Add("cover");
                        _logger.LogWarning("Self-referencing cover for '{Name}' but file missing on NAS", game.Name);
                    }
                }
                else
                {
                    // External URL: download FIRST, only delete old files on success
                    await ApplyRateLimitAsync(game.Cover, domainLastRequest, minDelayBetweenRequestsMs);

                    var coverBytes = await SafeDownloadAsync(game.Cover, attempt: 1, maxAttempts: 1);
                    if (coverBytes != null)
                    {
                        // Download succeeded - now safe to replace old files
                        if (urlChanged && cache?.CoverUrl != null)
                        {
                            DeleteOldImageFiles(gamePath, "cover");
                        }
                        var extension = GetExtensionFromUrl(game.Cover);
                        var coverPath = Path.Combine(gamePath, $"cover{extension}");
                        await File.WriteAllBytesAsync(coverPath, coverBytes);
                        if (cache != null) cache.CoverDownloaded = true;
                        result.ImagesSynced++;
                        result.FilesWritten++;
                        _logger.LogDebug("Downloaded cover for '{Name}'", game.Name);
                    }
                    else
                    {
                        // Download failed - preserve existing files, never delete
                        if (cache != null) cache.CoverDownloaded = false;
                        result.ImagesFailed++;
                        failedImageTypes.Add("cover");
                        if (game.Cover != null)
                        {
                            failedImageRetries.Add((game, dbGame.Id, gamePath, "cover", game.Cover, cache));
                        }
                        _logger.LogWarning("Failed to download cover for '{Name}' - existing files preserved", game.Name);
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

    /// <summary>
    /// If the URL points to our own /game-images/ proxy endpoint, resolve the image
    /// path from the UNC network path directly so we never HTTP-loop back into ourselves.
    /// </summary>
    private async Task<byte[]?> TryReadFromNetworkPathAsync(string url)
    {
        const string marker = "/game-images/";
        var idx = url.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (idx < 0 || string.IsNullOrWhiteSpace(_syncOptions.NetworkPath))
            return null;

        // Extract the relative path after /game-images/  (e.g. "1/Games/Foo/logo.png")
        var relativePath = url[(idx + marker.Length)..];
        if (string.IsNullOrWhiteSpace(relativePath))
            return null;

        // Normalise separators and resolve the full path under the network share
        relativePath = relativePath.Replace('/', Path.DirectorySeparatorChar);
        var fullPath = Path.GetFullPath(Path.Combine(_syncOptions.NetworkPath, relativePath));

        // Security: ensure the resolved path is still inside the network path root
        var rootFull = Path.GetFullPath(_syncOptions.NetworkPath);
        if (!fullPath.StartsWith(rootFull + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            return null;

        if (!File.Exists(fullPath))
        {
            _logger.LogDebug("Local file not found for self-referencing URL, will try HTTP download: {Path}", fullPath);
            return null;
        }

        _logger.LogDebug("Reading image directly from network path instead of HTTP: {Path}", fullPath);
        return await File.ReadAllBytesAsync(fullPath);
    }

    private async Task<byte[]?> SafeDownloadAsync(string url, int attempt = 1, int maxAttempts = 3)
    {
        // If the URL points to our own /game-images/ proxy, read directly from the UNC
        // network path instead of making an HTTP round-trip that would loop back into us.
        var localBytes = await TryReadFromNetworkPathAsync(url);
        if (localBytes != null)
            return localBytes;

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
            var imageExtensions = new[] { ".png", ".jpg", ".jpeg", ".gif", ".webp", ".bmp", ".ico" };
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

    /// <summary>
    /// Detects whether a URL points to our own image proxy / NAS storage.
    /// These images already exist on disk and must NEVER be deleted or re-downloaded.
    /// Matches: localhost, LAN IPs, and any URL containing /game-images/ (our proxy path).
    /// </summary>
    private bool IsSelfReferencingUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return false;

        // Direct local references
        if (url.Contains("localhost", StringComparison.OrdinalIgnoreCase) ||
            url.Contains("192.168.0.32", StringComparison.OrdinalIgnoreCase) ||
            url.Contains("127.0.0.1", StringComparison.OrdinalIgnoreCase))
            return true;

        // Any URL containing /game-images/ is our own proxy endpoint.
        // This covers production domains (gamesdatabase.*, gdb.*) that reverse-proxy
        // back to the same NAS storage.
        if (url.Contains("/game-images/", StringComparison.OrdinalIgnoreCase))
            return true;

        // Check against configured ImageBaseUrl if available
        if (!string.IsNullOrWhiteSpace(_syncOptions.ImageBaseUrl) &&
            url.StartsWith(_syncOptions.ImageBaseUrl, StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
    }

    /// <summary>
    /// Checks whether any image file for the given prefix (logo/cover) exists on disk,
    /// regardless of file extension.
    /// </summary>
    private static bool ImageFileExistsOnDisk(string gamePath, string prefix)
    {
        var imageExtensions = new[] { ".png", ".jpg", ".jpeg", ".gif", ".webp", ".bmp", ".ico" };
        foreach (var ext in imageExtensions)
        {
            var filePath = Path.Combine(gamePath, $"{prefix}{ext}");
            if (File.Exists(filePath))
                return true;
        }
        return false;
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

    public async Task<FolderAnalysisResult> AnalyzeFoldersAsync(int userId)
    {
        var result = new FolderAnalysisResult();

        try
        {
            // Load all games once — used for both filesystem analysis and DB duplicate detection.
            var games = await _context.Games
                .Where(g => g.UserId == userId)
                .Select(g => new DuplicateCandidateGame
                {
                    Id = g.Id,
                    Name = g.Name,
                    StatusName = g.Status.Name,
                    PlatformName = g.Platform != null ? g.Platform.Name : null,
                    PlayedStatusName = g.PlayedStatus != null ? g.PlayedStatus.Name : null,
                    Released = g.Released,
                    Started = g.Started,
                    Finished = g.Finished,
                    Grade = g.Grade,
                    Critic = g.Critic,
                    Score = g.Score,
                    Story = g.Story,
                    Completion = g.Completion,
                    Logo = g.Logo,
                    Cover = g.Cover,
                    SteamAppId = g.SteamAppId,
                    SteamPlaytimeForever = g.SteamPlaytimeForever,
                    ModifiedSinceExport = g.ModifiedSinceExport,
                    CreatedAt = g.CreatedAt,
                    UpdatedAt = g.UpdatedAt
                })
                .ToListAsync();

            result.TotalGamesInDatabase = games.Count;

            // ── DB duplicate detection (always run, regardless of filesystem) ───────
            result.DatabaseDuplicates = await BuildDatabaseDuplicatesAsync(userId, games);

            // Get user's games path
            var userPath = Path.Combine(_syncOptions.NetworkPath!, userId.ToString(), "Games");

            if (!Directory.Exists(userPath))
            {
                result.TotalFoldersInFilesystem = 0;
                result.Difference = -result.TotalGamesInDatabase;
                result.MissingGameFolders = games.Select(g =>
                {
                    var expectedFolderName = FolderNameHelper.MakeSafeFolderName(g.Name);
                    return new MissingGameFolder
                    {
                        GameId = g.Id,
                        GameName = g.Name,
                        ExpectedFolderName = expectedFolderName,
                        ExpectedFullPath = Path.Combine(userPath, expectedFolderName)
                    };
                }).ToList();
                return result;
            }

            // Get folders from filesystem
            var folders = Directory.GetDirectories(userPath)
                .Select(Path.GetFileName)
                .Where(name => !string.IsNullOrEmpty(name))
                .ToList();

            result.TotalFoldersInFilesystem = folders.Count;
            result.Difference = result.TotalFoldersInFilesystem - result.TotalGamesInDatabase;

            var gameToFolderMap = games.ToDictionary(
                g => g.Id,
                g => FolderNameHelper.MakeSafeFolderName(g.Name)
            );

            var expectedFolders = gameToFolderMap.Values.ToHashSet(StringComparer.OrdinalIgnoreCase);

            // Find orphan folders (folders without corresponding game)
            foreach (var folder in folders)
            {
                if (!expectedFolders.Contains(folder!))
                {
                    result.OrphanFolders.Add(BuildOrphanFolder(folder!, Path.Combine(userPath, folder!)));
                }
            }

            var foldersOnDisk = folders.ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var game in games)
            {
                var expectedFolderName = gameToFolderMap[game.Id];
                if (!foldersOnDisk.Contains(expectedFolderName))
                {
                    result.MissingGameFolders.Add(new MissingGameFolder
                    {
                        GameId = game.Id,
                        GameName = game.Name,
                        ExpectedFolderName = expectedFolderName,
                        ExpectedFullPath = Path.Combine(userPath, expectedFolderName)
                    });
                }
            }

            // Find potential duplicates using word-set equality: folders whose names consist
            // of the same set of significant words (case-insensitive, punctuation ignored).
            // This correctly flags "God of War" == "GOD OF WAR" while NOT flagging
            // "Hollow Knight" vs "Hollow Knight Silksong" (different word sets).
            var folderGroups = folders
                .GroupBy(f => GetNormalizedWordSet(f!))
                .Where(g => g.Count() > 1)
                .ToList();

            foreach (var group in folderGroups)
            {
                var matchingGame = games.FirstOrDefault(g =>
                    GetNormalizedWordSet(FolderNameHelper.MakeSafeFolderName(g.Name)) == group.Key);
                result.PotentialDuplicates.Add(new PotentialDuplicate
                {
                    GameName = matchingGame?.Name ?? group.First() ?? "Unknown",
                    FolderNames = group.ToList()!,
                    Reason = "Folders share the same words (different casing or punctuation)"
                });
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error analyzing folders for user {UserId}", userId);
            throw;
        }
    }

    /// <summary>
    /// Builds an <see cref="OrphanFolder"/> descriptor, enriching it with the reason it is
    /// considered orphan plus creation/modification dates and total size when the filesystem
    /// exposes that information. Any IO failure is swallowed so analysis never breaks.
    /// </summary>
    private OrphanFolder BuildOrphanFolder(string folderName, string fullPath)
    {
        var orphan = new OrphanFolder
        {
            FolderName = folderName,
            FullPath = fullPath,
            Reason = "La carpeta existe en el almacenamiento pero su nombre no coincide con ningún juego de la base de datos."
        };

        try
        {
            var info = new DirectoryInfo(fullPath);
            if (info.Exists)
            {
                orphan.CreatedAt = info.CreationTimeUtc;
                orphan.ModifiedAt = info.LastWriteTimeUtc;

                long totalSize = 0;
                var fileCount = 0;
                foreach (var file in info.EnumerateFiles("*", SearchOption.AllDirectories))
                {
                    fileCount++;
                    try { totalSize += file.Length; }
                    catch { /* ignore unreadable file */ }
                }

                orphan.FileCount = fileCount;
                orphan.SizeBytes = totalSize;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not read metadata for orphan folder {Path}", fullPath);
        }

        return orphan;
    }

    public async Task<DatabaseDuplicatesResult> AnalyzeDatabaseDuplicatesAsync(int userId)
    {
        var games = await _context.Games
            .Where(g => g.UserId == userId)
            .Select(g => new DuplicateCandidateGame
            {
                Id = g.Id,
                Name = g.Name,
                StatusName = g.Status.Name,
                PlatformName = g.Platform != null ? g.Platform.Name : null,
                PlayedStatusName = g.PlayedStatus != null ? g.PlayedStatus.Name : null,
                Released = g.Released,
                Started = g.Started,
                Finished = g.Finished,
                Grade = g.Grade,
                Critic = g.Critic,
                Score = g.Score,
                Story = g.Story,
                Completion = g.Completion,
                Logo = g.Logo,
                Cover = g.Cover,
                SteamAppId = g.SteamAppId,
                SteamPlaytimeForever = g.SteamPlaytimeForever,
                ModifiedSinceExport = g.ModifiedSinceExport,
                CreatedAt = g.CreatedAt,
                UpdatedAt = g.UpdatedAt
            })
            .ToListAsync();
        return await BuildDatabaseDuplicatesAsync(userId, games);
    }

    public async Task<int> DismissDuplicateGamesAsync(int userId, IReadOnlyCollection<int> gameIds)
    {
        var distinctIds = gameIds
            .Distinct()
            .OrderBy(id => id)
            .ToArray();

        if (distinctIds.Length < 2)
            return 0;

        var validIds = await _context.Games
            .Where(g => g.UserId == userId && distinctIds.Contains(g.Id))
            .Select(g => g.Id)
            .ToListAsync();

        if (validIds.Count < 2)
            return 0;

        var validSet = validIds.ToHashSet();
        var pairs = BuildPairs(validSet.OrderBy(id => id).ToArray());
        var existingPairs = (await _context.DuplicateGameDismissals
            .Where(d => d.UserId == userId)
            .Select(d => new { d.GameIdA, d.GameIdB })
            .ToListAsync())
            .Select(d => $"{d.GameIdA}:{d.GameIdB}")
            .ToHashSet();

        var created = 0;
        foreach (var pair in pairs)
        {
            if (existingPairs.Contains($"{pair.A}:{pair.B}"))
                continue;

            _context.DuplicateGameDismissals.Add(new DuplicateGameDismissal
            {
                UserId = userId,
                GameIdA = pair.A,
                GameIdB = pair.B,
                CreatedAt = DateTime.UtcNow
            });
            created++;
        }

        if (created > 0)
            await _context.SaveChangesAsync();

        return created;
    }

    private async Task<DatabaseDuplicatesResult> BuildDatabaseDuplicatesAsync(int userId, List<DuplicateCandidateGame> games)
    {
        var result = new DatabaseDuplicatesResult { TotalGamesInDatabase = games.Count };
        if (games.Count < 2)
            return result;

        var dismissedPairs = await GetDismissedDuplicatePairsAsync(userId);
        var edges = new List<DuplicateEdge>();

        for (var i = 0; i < games.Count - 1; i++)
        {
            for (var j = i + 1; j < games.Count; j++)
            {
                var pair = NormalizePair(games[i].Id, games[j].Id);
                if (dismissedPairs.Contains($"{pair.A}:{pair.B}"))
                    continue;

                var edge = TryBuildDuplicateEdge(games[i], games[j]);
                if (edge != null)
                    edges.Add(edge);
            }
        }

        if (edges.Count == 0)
            return result;

        var parent = games.ToDictionary(g => g.Id, g => g.Id);
        foreach (var edge in edges)
        {
            Union(parent, edge.GameIdA, edge.GameIdB);
        }

        var edgesByRoot = edges
            .GroupBy(edge => Find(parent, edge.GameIdA))
            .ToDictionary(g => g.Key, g => g.ToList());

        var gamesByRoot = games
            .Where(g => edges.Any(edge => edge.GameIdA == g.Id || edge.GameIdB == g.Id))
            .GroupBy(g => Find(parent, g.Id))
            .Where(g => g.Count() > 1)
            .OrderBy(g => g.Min(game => game.Name));

        var duplicateGroups = gamesByRoot.ToList();
        var involvedGames = duplicateGroups.SelectMany(group => group).ToList();
        var enrichment = await BuildDuplicateEnrichmentAsync(userId, involvedGames);

        foreach (var group in duplicateGroups)
        {
            var groupEdges = edgesByRoot[group.Key];
            var hasFuzzy = groupEdges.Any(edge => edge.MatchType == "fuzzy");
            var minConfidence = groupEdges.Min(edge => edge.Confidence);

            result.DuplicateGroups.Add(new DatabaseDuplicateGroup
            {
                NormalizedKey = string.Join("|", group.Select(g => g.Id).OrderBy(id => id)),
                Games = group.OrderBy(g => g.Name)
                    .Select(g => ToDuplicateEntry(g, enrichment.GetValueOrDefault(g.Id)))
                    .ToList(),
                MatchType = hasFuzzy ? "fuzzy" : "exact",
                Confidence = minConfidence,
                Reason = hasFuzzy
                    ? "Nombres muy parecidos: posible typo o variante mínima. Puedes descartarlo si es un falso positivo."
                    : "Coincidencia exacta: mismo título normalizado o el mismo Steam App ID."
            });
        }

        return result;
    }

    /// <summary>
    /// Builds per-game enrichment data (expected export folder + persisted export status)
    /// for the games that take part in duplicate groups, using a single export-cache query.
    /// </summary>
    private async Task<Dictionary<int, DuplicateEnrichment>> BuildDuplicateEnrichmentAsync(
        int userId, IReadOnlyCollection<DuplicateCandidateGame> games)
    {
        var map = new Dictionary<int, DuplicateEnrichment>();
        if (games.Count == 0)
            return map;

        var ids = games.Select(g => g.Id).ToList();
        var caches = await _context.GameExportCaches
            .Where(c => ids.Contains(c.GameId))
            .ToDictionaryAsync(c => c.GameId);

        string? gamesRoot = null;
        var filesystemAvailable = false;
        if (!string.IsNullOrWhiteSpace(_syncOptions.NetworkPath))
        {
            gamesRoot = Path.Combine(_syncOptions.NetworkPath, userId.ToString(), "Games");
            try { filesystemAvailable = Directory.Exists(gamesRoot); }
            catch { filesystemAvailable = false; }
        }

        foreach (var game in games)
        {
            var folderName = FolderNameHelper.MakeSafeFolderName(game.Name);
            string? folderPath = gamesRoot != null && !string.IsNullOrEmpty(folderName)
                ? Path.Combine(gamesRoot, folderName)
                : null;

            var folderExists = false;
            if (filesystemAvailable && folderPath != null)
            {
                try { folderExists = Directory.Exists(folderPath); }
                catch { folderExists = false; }
            }

            caches.TryGetValue(game.Id, out var cache);
            map[game.Id] = new DuplicateEnrichment(folderName, folderPath, folderExists, filesystemAvailable, cache);
        }

        return map;
    }

    private async Task<HashSet<string>> GetDismissedDuplicatePairsAsync(int userId) =>
        (await _context.DuplicateGameDismissals
            .Where(d => d.UserId == userId)
            .Select(d => new { d.GameIdA, d.GameIdB })
            .ToListAsync())
        .Select(pair => $"{pair.GameIdA}:{pair.GameIdB}")
        .ToHashSet();

    private static DuplicateEdge? TryBuildDuplicateEdge(DuplicateCandidateGame a, DuplicateCandidateGame b)
    {
        // Strongest signal: a shared external Steam App ID means it is the same game,
        // even when the stored titles differ (e.g. "GOTY" vs base name).
        if (a.SteamAppId.HasValue && b.SteamAppId.HasValue &&
            a.SteamAppId.Value > 0 && a.SteamAppId.Value == b.SteamAppId.Value)
        {
            return new DuplicateEdge(a.Id, b.Id, "exact", 100);
        }

        var aWords = GetNormalizedWords(a.Name);
        var bWords = GetNormalizedWords(b.Name);
        if (aWords.Length == 0 || bWords.Length == 0)
            return null;

        var aWordSet = string.Join("|", aWords.OrderBy(w => w));
        var bWordSet = string.Join("|", bWords.OrderBy(w => w));
        if (aWordSet == bWordSet)
        {
            return new DuplicateEdge(a.Id, b.Id, "exact", 100);
        }

        if (IsLikelyEditionOrSequelDifference(aWords, bWords))
            return null;

        var compactA = string.Concat(aWords);
        var compactB = string.Concat(bWords);
        var maxLength = Math.Max(compactA.Length, compactB.Length);
        if (maxLength < 5)
            return null;

        if (DamerauLevenshteinDistance(compactA, compactB, 1) <= 1)
            return new DuplicateEdge(a.Id, b.Id, "fuzzy", 92);

        if (aWords.Length == bWords.Length)
        {
            var differentWords = aWords.Zip(bWords)
                .Where(pair => pair.First != pair.Second)
                .ToList();
            if (differentWords.Count == 1 && DamerauLevenshteinDistance(differentWords[0].First, differentWords[0].Second, 1) <= 1)
                return new DuplicateEdge(a.Id, b.Id, "fuzzy", 90);
        }

        if (Math.Abs(aWords.Length - bWords.Length) == 1)
        {
            var longer = aWords.Length > bWords.Length ? aWords : bWords;
            var shorter = aWords.Length > bWords.Length ? bWords : aWords;
            var extra = longer.Except(shorter).ToArray();
            if (extra.Length == 1 && extra[0].Length <= 2 && !IsVersionToken(extra[0]) && DamerauLevenshteinDistance(compactA, compactB, 1) <= 1)
                return new DuplicateEdge(a.Id, b.Id, "fuzzy", 86);
        }

        return null;
    }

    private static bool IsLikelyEditionOrSequelDifference(string[] aWords, string[] bWords)
    {
        if (aWords.Length == bWords.Length)
        {
            var differentWords = aWords.Zip(bWords)
                .Where(pair => pair.First != pair.Second)
                .ToList();
            return differentWords.Count == 1 &&
                IsVersionToken(differentWords[0].First) &&
                IsVersionToken(differentWords[0].Second);
        }

        var longer = aWords.Length > bWords.Length ? aWords : bWords;
        var shorter = aWords.Length > bWords.Length ? bWords : aWords;
        var remaining = longer.ToList();
        foreach (var word in shorter)
            remaining.Remove(word);

        return remaining.Count > 0 && remaining.All(word => IsVersionToken(word) || IsEditionToken(word));
    }

    private static bool IsVersionToken(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        return value.All(char.IsDigit) ||
            System.Text.RegularExpressions.Regex.IsMatch(value, @"^[ivxlcdm]+$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    }

    private static bool IsEditionToken(string value)
        => EditionTokens.Contains(value);

    private static string[] GetNormalizedWords(string name) =>
        System.Text.RegularExpressions.Regex
            .Matches(name.Normalize(NormalizationForm.FormD).ToLowerInvariant(), @"[a-z0-9]+")
            .Select(m => m.Value)
            .Where(word => !string.IsNullOrWhiteSpace(word))
            .ToArray();

    private static int DamerauLevenshteinDistance(string source, string target, int maxDistance)
    {
        if (Math.Abs(source.Length - target.Length) > maxDistance)
            return maxDistance + 1;

        var previous = new int[target.Length + 1];
        var current = new int[target.Length + 1];
        var beforePrevious = new int[target.Length + 1];

        for (var j = 0; j <= target.Length; j++)
            previous[j] = j;

        for (var i = 1; i <= source.Length; i++)
        {
            current[0] = i;
            var rowMin = current[0];

            for (var j = 1; j <= target.Length; j++)
            {
                var cost = source[i - 1] == target[j - 1] ? 0 : 1;
                current[j] = Math.Min(
                    Math.Min(current[j - 1] + 1, previous[j] + 1),
                    previous[j - 1] + cost);

                if (i > 1 && j > 1 && source[i - 1] == target[j - 2] && source[i - 2] == target[j - 1])
                    current[j] = Math.Min(current[j], beforePrevious[j - 2] + 1);

                rowMin = Math.Min(rowMin, current[j]);
            }

            if (rowMin > maxDistance)
                return maxDistance + 1;

            (beforePrevious, previous, current) = (previous, current, beforePrevious);
        }

        return previous[target.Length];
    }

    private static DatabaseDuplicateEntry ToDuplicateEntry(DuplicateCandidateGame game, DuplicateEnrichment? enrichment) => new()
    {
        Id = game.Id,
        Name = game.Name,
        StatusName = game.StatusName,
        PlatformName = game.PlatformName,
        PlayedStatusName = game.PlayedStatusName,
        Released = game.Released,
        Started = game.Started,
        Finished = game.Finished,
        Grade = game.Grade,
        Critic = game.Critic,
        Score = game.Score,
        Story = game.Story,
        Completion = game.Completion,
        Logo = game.Logo,
        Cover = game.Cover,
        SteamAppId = game.SteamAppId,
        SteamPlaytimeForever = game.SteamPlaytimeForever,
        CreatedAt = game.CreatedAt,
        UpdatedAt = game.UpdatedAt,
        ModifiedSinceExport = game.ModifiedSinceExport,
        FolderName = enrichment?.FolderName,
        FolderPath = enrichment?.FolderPath,
        FolderExists = enrichment?.FolderExists ?? false,
        FilesystemChecked = enrichment?.FilesystemAvailable ?? false,
        IsExported = enrichment?.Cache != null,
        LastExportedAt = enrichment?.Cache?.LastExportedAt,
        LogoDownloaded = enrichment?.Cache?.LogoDownloaded ?? false,
        CoverDownloaded = enrichment?.Cache?.CoverDownloaded ?? false
    };

    private static List<(int A, int B)> BuildPairs(IReadOnlyList<int> ids)
    {
        var pairs = new List<(int A, int B)>();
        for (var i = 0; i < ids.Count - 1; i++)
        {
            for (var j = i + 1; j < ids.Count; j++)
                pairs.Add(NormalizePair(ids[i], ids[j]));
        }
        return pairs;
    }

    private static (int A, int B) NormalizePair(int a, int b) => a < b ? (a, b) : (b, a);

    private static int Find(Dictionary<int, int> parent, int value)
    {
        if (parent[value] != value)
            parent[value] = Find(parent, parent[value]);
        return parent[value];
    }

    private static void Union(Dictionary<int, int> parent, int a, int b)
    {
        var rootA = Find(parent, a);
        var rootB = Find(parent, b);
        if (rootA != rootB)
            parent[rootB] = rootA;
    }

    private static string NormalizeFolderName(string folderName)
    {
        // Remove all special characters for comparison
        return new string(folderName
            .Normalize(NormalizationForm.FormD)
            .Where(c => char.IsLetterOrDigit(c))
            .ToArray())
            .ToLowerInvariant();
    }

    /// <summary>
    /// Returns a canonical key representing the SET of significant words in a name.
    /// Words are extracted (alphanumeric sequences), lowercased, sorted and joined.
    /// This allows semantic duplicate detection while avoiding false positives caused
    /// by subtitle differences ("Hollow Knight" vs "Hollow Knight: Silksong").
    /// </summary>
    private static string GetNormalizedWordSet(string name)
    {
        var words = System.Text.RegularExpressions.Regex
            .Matches(name.Normalize(NormalizationForm.FormD).ToLowerInvariant(), @"[a-z0-9]+")
            .Select(m => m.Value)
            .OrderBy(w => w)
            .ToArray();
        return string.Join("|", words);
    }

    private sealed class DuplicateCandidateGame
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? StatusName { get; set; }
        public string? PlatformName { get; set; }
        public string? PlayedStatusName { get; set; }
        public string? Released { get; set; }
        public string? Started { get; set; }
        public string? Finished { get; set; }
        public int? Grade { get; set; }
        public int? Critic { get; set; }
        public decimal? Score { get; set; }
        public int? Story { get; set; }
        public int? Completion { get; set; }
        public string? Logo { get; set; }
        public string? Cover { get; set; }
        public int? SteamAppId { get; set; }
        public int? SteamPlaytimeForever { get; set; }
        public bool ModifiedSinceExport { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }

    private sealed record DuplicateEdge(int GameIdA, int GameIdB, string MatchType, int Confidence);

    private sealed record DuplicateEnrichment(
        string FolderName,
        string? FolderPath,
        bool FolderExists,
        bool FilesystemAvailable,
        GameExportCache? Cache);
}
