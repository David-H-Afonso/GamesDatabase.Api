using Microsoft.AspNetCore.Mvc;
using GamesDatabase.Api.Services;

namespace GamesDatabase.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ExportController : BaseApiController
{
    private readonly IZipExportService _zipExportService;
    private readonly INetworkSyncService _networkSyncService;
    private readonly ILogger<ExportController> _logger;
    private readonly IConfiguration _configuration;

    public ExportController(
        IZipExportService zipExportService,
        INetworkSyncService networkSyncService,
        ILogger<ExportController> logger,
        IConfiguration configuration)
    {
        _zipExportService = zipExportService;
        _networkSyncService = networkSyncService;
        _logger = logger;
        _configuration = configuration;
    }

    /// <summary>
    /// Downloads a complete ZIP export of the Games Database
    /// </summary>
    /// <param name="full">If true, exports all games regardless of cache. If false, only exports modified games and retries failed images.</param>
    /// <returns>ZIP file containing games, settings, and backup CSV</returns>
    [HttpGet("zip")]
    public async Task<IActionResult> GetZip([FromQuery] bool full = false)
    {
        try
        {
            _logger.LogInformation("Starting ZIP export generation (full: {Full})", full);

            // Get the Authorization header from the current request
            var authHeader = Request.Headers["Authorization"].ToString();

            var result = await _zipExportService.BuildZipAsync(authHeader, full);
            var fileName = $"games_database_export_{DateTime.UtcNow:yyyy-MM-dd}.zip";

            _logger.LogInformation(
                "ZIP export completed successfully: {FileName} ({Size:N0} bytes) - " +
                "Time: {Time:F2}s, Games: {Exported}/{Total} exported, {Skipped} skipped, " +
                "Images: {ImagesDownloaded} downloaded ({ImagesRetried} retried, {ImagesFailed} failed)",
                fileName,
                result.ZipBytes.Length,
                result.ElapsedTime.TotalSeconds,
                result.GamesExported,
                result.TotalGames,
                result.GamesSkipped,
                result.ImagesDownloaded,
                result.ImagesRetried,
                result.ImagesFailed
            );

            // Add export stats as custom headers
            Response.Headers["X-Export-Time-Seconds"] = result.ElapsedTime.TotalSeconds.ToString("F2");
            Response.Headers["X-Export-Total-Games"] = result.TotalGames.ToString();
            Response.Headers["X-Export-Games-Exported"] = result.GamesExported.ToString();
            Response.Headers["X-Export-Games-Skipped"] = result.GamesSkipped.ToString();
            Response.Headers["X-Export-Images-Downloaded"] = result.ImagesDownloaded.ToString();
            Response.Headers["X-Export-Images-Retried"] = result.ImagesRetried.ToString();
            Response.Headers["X-Export-Images-Failed"] = result.ImagesFailed.ToString();

            return File(result.ZipBytes, "application/zip", fileName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate ZIP export");
            return StatusCode(500, new { message = "Failed to generate ZIP export", error = ex.Message });
        }
    }

    /// <summary>
    /// Synchronizes the Games Database to a network share
    /// </summary>
    /// <param name="request">Sync request with fullExport flag</param>
    /// <returns>Sync operation result with statistics</returns>
    [HttpPost("sync-to-network")]
    public async Task<IActionResult> SyncToNetwork([FromBody] SyncToNetworkRequest request)
    {
        try
        {
            _logger.LogInformation("Starting network sync (full: {Full})", request.FullExport);

            // Get the Authorization header from the current request
            var authHeader = Request.Headers["Authorization"].ToString();

            var result = await _networkSyncService.SyncToNetworkAsync(authHeader, request.FullExport);

            if (!result.Success)
            {
                return StatusCode(500, new
                {
                    message = "Network sync failed",
                    error = result.ErrorMessage,
                    stats = new
                    {
                        elapsedSeconds = result.ElapsedTime.TotalSeconds,
                        totalGames = result.TotalGames,
                        gamesSynced = result.GamesSynced,
                        gamesSkipped = result.GamesSkipped,
                        imagesSynced = result.ImagesSynced,
                        imagesRetried = result.ImagesRetried,
                        imagesFailed = result.ImagesFailed,
                        filesWritten = result.FilesWritten
                    }
                });
            }

            _logger.LogInformation(
                "Network sync completed successfully - " +
                "Time: {Time:F2}s, Games: {Synced}/{Total} synced, {Skipped} skipped, " +
                "Images: {ImagesSynced} synced ({ImagesRetried} retried, {ImagesFailed} failed), " +
                "Files: {FilesWritten} written",
                result.ElapsedTime.TotalSeconds,
                result.GamesSynced,
                result.TotalGames,
                result.GamesSkipped,
                result.ImagesSynced,
                result.ImagesRetried,
                result.ImagesFailed,
                result.FilesWritten
            );

            return Ok(new
            {
                message = "Network sync completed successfully",
                stats = new
                {
                    elapsedSeconds = result.ElapsedTime.TotalSeconds,
                    totalGames = result.TotalGames,
                    gamesSynced = result.GamesSynced,
                    gamesSkipped = result.GamesSkipped,
                    imagesSynced = result.ImagesSynced,
                    imagesRetried = result.ImagesRetried,
                    imagesFailed = result.ImagesFailed,
                    filesWritten = result.FilesWritten
                },
                failedImages = result.FailedImages.Select(f => new
                {
                    gameName = f.GameName,
                    imageTypes = f.ImageTypes
                }).ToList()
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Network sync failed with exception");
            return StatusCode(500, new { message = "Network sync failed", error = ex.Message });
        }
    }

    /// <summary>
    /// Automated network sync endpoint for scheduled tasks (uses API Key authentication)
    /// </summary>
    /// <param name="apiKey">API Key for authentication</param>
    /// <param name="full">If true, performs full sync. If false, performs partial sync (default: false)</param>
    /// <returns>Sync operation result</returns>
    [HttpPost("auto-sync")]
    public async Task<IActionResult> AutoSync([FromQuery] string apiKey, [FromQuery] bool full = false)
    {
        try
        {
            // Validate API Key from environment variable
            var configuredApiKey = Environment.GetEnvironmentVariable("AUTOMATION_API_KEY") 
                                ?? _configuration["AutomationSettings:ApiKey"];
            
            if (string.IsNullOrWhiteSpace(configuredApiKey))
            {
                _logger.LogWarning("Auto-sync attempted but API Key is not configured");
                return Unauthorized(new { message = "API Key not configured. Please set AUTOMATION_API_KEY environment variable" });
            }

            if (string.IsNullOrWhiteSpace(apiKey) || apiKey != configuredApiKey)
            {
                _logger.LogWarning("Auto-sync attempted with invalid API Key");
                return Unauthorized(new { message = "Invalid API Key" });
            }

            _logger.LogInformation("Starting automated network sync (full: {Full})", full);

            // No need for Authorization header in auto-sync (uses default user)
            var result = await _networkSyncService.SyncToNetworkAsync(null, full);

            if (!result.Success)
            {
                return StatusCode(500, new
                {
                    success = false,
                    message = result.ErrorMessage,
                    stats = new
                    {
                        elapsedSeconds = result.ElapsedTime.TotalSeconds,
                        totalGames = result.TotalGames,
                        gamesSynced = result.GamesSynced,
                        gamesSkipped = result.GamesSkipped,
                        imagesSynced = result.ImagesSynced,
                        imagesRetried = result.ImagesRetried,
                        imagesFailed = result.ImagesFailed,
                        filesWritten = result.FilesWritten
                    }
                });
            }

            _logger.LogInformation(
                "Automated sync completed - Time: {Time:F2}s, Games: {Synced}/{Total}, " +
                "Images: {ImagesSynced} synced ({ImagesRetried} retried, {ImagesFailed} failed)",
                result.ElapsedTime.TotalSeconds,
                result.GamesSynced,
                result.TotalGames,
                result.ImagesSynced,
                result.ImagesRetried,
                result.ImagesFailed
            );

            return Ok(new
            {
                success = true,
                message = $"Sync completed: {result.GamesSynced} games synced, {result.GamesSkipped} skipped",
                stats = new
                {
                    elapsedSeconds = result.ElapsedTime.TotalSeconds,
                    totalGames = result.TotalGames,
                    gamesSynced = result.GamesSynced,
                    gamesSkipped = result.GamesSkipped,
                    imagesSynced = result.ImagesSynced,
                    imagesRetried = result.ImagesRetried,
                    imagesFailed = result.ImagesFailed,
                    filesWritten = result.FilesWritten
                },
                failedImages = result.FailedImages.Any() ? result.FailedImages.Select(f => new
                {
                    gameName = f.GameName,
                    imageTypes = f.ImageTypes
                }).ToList() : null
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Automated sync failed with exception");
            return StatusCode(500, new { success = false, message = "Sync failed", error = ex.Message });
        }
    }
}

public record SyncToNetworkRequest(bool FullExport);
