using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using CsvHelper.Configuration.Attributes;
using GamesDatabase.Api.Application.Interfaces;
using GamesDatabase.Api.Application.Services;
using GamesDatabase.Api.Contracts;
using GamesDatabase.Api.Common;

namespace GamesDatabase.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class DataExportController : BaseApiController
{
    private readonly IGameImportExportService _importExportService;
    private readonly ILogger<DataExportController> _logger;
    private readonly IConfiguration _configuration;
    private readonly INetworkSyncService _networkSyncService;
    private readonly IGameService _gameService;

    public DataExportController(
        IGameImportExportService importExportService,
        ILogger<DataExportController> logger,
        IConfiguration configuration,
        INetworkSyncService networkSyncService,
        IGameService gameService)
    {
        _importExportService = importExportService;
        _logger = logger;
        _configuration = configuration;
        _networkSyncService = networkSyncService;
        _gameService = gameService;
    }

    [HttpPost("update-image-urls")]
    [Authorize]
    public async Task<ActionResult<UpdateImageUrlsResult>> UpdateImageUrls([FromQuery] string? imageBaseUrl = null)
    {
        var role = User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value;
        if (role != "Admin")
            return StatusCode(403, new { message = "Image URL update requires admin privileges" });

        var networkSyncPath = _configuration["NetworkSync:NetworkPath"];
        if (string.IsNullOrWhiteSpace(networkSyncPath))
            return BadRequest("NetworkSync:NetworkPath not configured");

        var userId = GetCurrentUserIdOrDefault(1);
        var result = await _importExportService.UpdateImageUrlsAsync(
            userId,
            imageBaseUrl,
            networkSyncPath,
            _configuration["ImageSettings:BaseUrl"],
            _configuration["NetworkSync:Username"],
            _configuration["NetworkSync:Password"]);

        return Ok(result);
    }

    [HttpGet("analyze-folders")]
    [Authorize]
    public async Task<ActionResult<FolderAnalysisResult>> AnalyzeFolders()
    {
        var role = User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value;
        if (role != "Admin")
            return StatusCode(403, new { message = "Folder analysis requires admin privileges" });

        try
        {
            var userId = GetCurrentUserIdOrDefault(1);
            var result = await _networkSyncService.AnalyzeFoldersAsync(userId);
            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error analyzing folders");
            return StatusCode(500, new { message = "Error analyzing folders" });
        }
    }

    [HttpGet("analyze-duplicate-games")]
    [Authorize]
    public async Task<ActionResult<DatabaseDuplicatesResult>> AnalyzeDuplicateGames()
    {
        try
        {
            var userId = GetCurrentUserIdOrDefault(1);
            var result = await _networkSyncService.AnalyzeDatabaseDuplicatesAsync(userId);
            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error analyzing database duplicates for user {UserId}", GetCurrentUserIdOrDefault(1));
            return StatusCode(500, new { message = "Error analyzing database duplicates" });
        }
    }

    [HttpDelete("orphan-folder")]
    [Authorize]
    public IActionResult DeleteOrphanFolder([FromBody] DeleteOrphanFolderRequest request)
    {
        var role = User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value;
        if (role != "Admin")
            return StatusCode(403, new { message = "Orphan folder deletion requires admin privileges" });

        if (request == null || string.IsNullOrWhiteSpace(request.FolderName))
            return BadRequest(new { message = "Folder name is required" });

        var networkSyncPath = _configuration["NetworkSync:NetworkPath"];
        if (string.IsNullOrWhiteSpace(networkSyncPath))
            return StatusCode(500, new { message = "NetworkSync:NetworkPath is not configured" });

        var userId = GetCurrentUserIdOrDefault(1);
        var gamesRoot = Path.GetFullPath(Path.Combine(networkSyncPath, userId.ToString(), "Games"));
        var folderPath = Path.GetFullPath(Path.Combine(gamesRoot, request.FolderName));

        if (!folderPath.StartsWith(gamesRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { message = "Invalid folder path" });

        if (!Directory.Exists(folderPath))
            return NotFound(new { message = "Folder does not exist" });

        try
        {
            DeleteDirectoryRobust(folderPath);
            _logger.LogInformation("Deleted orphan folder {FolderPath} for user {UserId}", folderPath, userId);
            return Ok(new { folderName = request.FolderName, deleted = true, message = $"Carpeta eliminada: {request.FolderName}" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting orphan folder {FolderPath} for user {UserId}", folderPath, userId);
            return StatusCode(500, new { message = "Error deleting orphan folder" });
        }
    }

    [HttpPost("duplicate-games/dismiss")]
    [Authorize]
    public async Task<IActionResult> DismissDuplicateGames([FromBody] DismissDuplicateGamesRequest request)
    {
        if (request == null || request.GameIds == null || request.GameIds.Count < 2)
            return BadRequest(new { message = "At least two game IDs are required" });

        var userId = GetCurrentUserIdOrDefault(1);
        var dismissed = await _networkSyncService.DismissDuplicateGamesAsync(userId, request.GameIds);

        return Ok(new { dismissed, message = dismissed > 0 ? $"Duplicado descartado ({dismissed} pareja/s)." : "No había parejas nuevas para descartar." });
    }

    [HttpDelete("duplicate-games/{id:int}")]
    [Authorize]
    public async Task<IActionResult> DeleteDuplicateGame(int id)
    {
        var userId = GetCurrentUserIdOrDefault(1);
        var deleted = await _gameService.DeleteGameAsync(id, userId);

        if (!deleted)
            return NotFound(new { message = "Game not found" });

        _logger.LogInformation("Deleted duplicate game candidate {GameId} for user {UserId}", id, userId);
        return Ok(new { gameId = id, deleted = true, message = $"Juego eliminado: #{id}" });
    }

    private static void DeleteDirectoryRobust(string folderPath)
    {
        foreach (var file in Directory.EnumerateFiles(folderPath, "*", SearchOption.AllDirectories))
        {
            TryWithRetries(() =>
            {
                System.IO.File.SetAttributes(file, FileAttributes.Normal);
                System.IO.File.Delete(file);
            });
        }

        var directories = Directory.EnumerateDirectories(folderPath, "*", SearchOption.AllDirectories)
            .OrderByDescending(path => path.Length)
            .ToList();

        foreach (var directory in directories)
        {
            TryWithRetries(() => Directory.Delete(directory, recursive: false));
        }

        TryWithRetries(() => Directory.Delete(folderPath, recursive: false));
    }

    private static void TryWithRetries(Action action)
    {
        const int maxAttempts = 4;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                action();
                return;
            }
            catch when (attempt < maxAttempts)
            {
                Thread.Sleep(100 * attempt);
            }
        }

        action();
    }

    [HttpGet("full")]
    public async Task<IActionResult> ExportFullDatabase()
    {
        try
        {
            var userId = GetCurrentUserIdOrDefault(1);
            var csvBytes = await _importExportService.ExportFullDatabaseAsync(userId);
            var fileName = $"games_database_full_{DateTime.Now:yyyyMMdd_HHmmss}.csv";
            return File(csvBytes, "text/csv", fileName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error exporting full database");
            return StatusCode(500, new { message = "Error al exportar la base de datos completa" });
        }
    }

    [HttpPost("full")]
    public async Task<IActionResult> ImportFullDatabase(IFormFile csvFile)
    {
        if (csvFile == null || csvFile.Length == 0)
            return BadRequest(new { message = "No se proporcionó ningún archivo CSV" });
        if (!csvFile.FileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { message = "El archivo debe tener extensión .csv" });

        try
        {
            var userId = GetCurrentUserIdOrDefault(1);
            using var stream = csvFile.OpenReadStream();
            var result = await _importExportService.ImportFullDatabaseAsync(stream, userId);
            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error importing full database");
            return StatusCode(500, new { message = "Error al importar la base de datos completa" });
        }
    }

    [HttpPost("selective-games-export")]
    public async Task<IActionResult> SelectiveExportGames([FromBody] SelectiveExportRequest request)
    {
        if (request.GameIds == null || request.GameIds.Count == 0)
            return BadRequest(new { message = "No game IDs provided" });

        try
        {
            var userId = GetCurrentUserIdOrDefault(1);
            var csvBytes = await _importExportService.SelectiveExportGamesAsync(request, userId);
            var fileName = $"games_selective_export_{DateTime.Now:yyyyMMdd_HHmmss}.csv";
            return File(csvBytes, "text/csv", fileName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in selective export");
            return StatusCode(500, new { message = "Error generating selective export" });
        }
    }

    [HttpPost("selective-games-import")]
    public async Task<IActionResult> SelectiveImportGames(
        [FromForm] IFormFile? csvFile,
        [FromForm] string? csvText,
        [FromForm] string? configJson)
    {
        if (csvFile == null && string.IsNullOrWhiteSpace(csvText))
            return BadRequest(new { message = "No CSV source provided. Supply csvFile or csvText." });

        try
        {
            var userId = GetCurrentUserIdOrDefault(1);
            Stream? fileStream = csvFile?.OpenReadStream();
            try
            {
                var result = await _importExportService.SelectiveImportGamesAsync(fileStream, csvText, configJson, userId);
                return Ok(result);
            }
            finally
            {
                fileStream?.Dispose();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in selective import");
            return StatusCode(500, new { message = "Error during selective import" });
        }
    }

    [HttpPost("clear-image-cache")]
    [Authorize]
    public IActionResult ClearImageCache()
    {
        var role = User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value;
        if (role != "Admin")
            return StatusCode(403, new { message = "Image cache clear requires admin privileges" });

        var networkSyncPath = _configuration["NetworkSync:NetworkPath"];
        if (string.IsNullOrWhiteSpace(networkSyncPath))
            return StatusCode(500, new { message = "NetworkSync:NetworkPath is not configured" });

        var cacheDir = Path.Combine(Path.GetFullPath(networkSyncPath), "_proxy_cache");
        if (!Directory.Exists(cacheDir))
            return Ok(new { deletedFiles = 0, message = "Cache directory does not exist, nothing to clear." });

        var deleted = 0;
        foreach (var file in Directory.EnumerateFiles(cacheDir, "*.webp", SearchOption.AllDirectories))
        {
            try { System.IO.File.Delete(file); deleted++; }
            catch { /* ignore locked files */ }
        }

        _logger.LogInformation("Image proxy cache cleared: {Count} files deleted", deleted);
        return Ok(new { deletedFiles = deleted, message = $"Cache limpiado: {deleted} archivos eliminados." });
    }
}

public class FullExportModel
{
    public string Type { get; set; } = "";
    public string Name { get; set; } = "";
    public string Color { get; set; } = "";
    public string? IsActive { get; set; }
    public string? SortOrder { get; set; }
    public string? IsDefault { get; set; }
    public string? StatusType { get; set; }
    public string? Status { get; set; }
    public string? Platform { get; set; }
    public string? PlayWith { get; set; }
    public string? PlayedStatus { get; set; }
    public string? Released { get; set; }
    public string? Started { get; set; }
    public string? Finished { get; set; }
    public string? Score { get; set; }
    public string? Critic { get; set; }
    public string? CriticProvider { get; set; }
    public string? Grade { get; set; }
    public string? Completion { get; set; }
    public string? Story { get; set; }
    public string? Comment { get; set; }
    public string? Logo { get; set; }
    public string? Cover { get; set; }
    [Name("IsCheaperByKey")]
    public string? IsCheaperByKey { get; set; }
    [Name("KeyStoreUrl")]
    public string? KeyStoreUrl { get; set; }
    [Name("SteamAppId")]
    public string? SteamAppId { get; set; }
    [Name("SteamPlaytimeForever")]
    public string? SteamPlaytimeForever { get; set; }
    [Name("SteamPlaytime2Weeks")]
    public string? SteamPlaytime2Weeks { get; set; }
    [Name("SteamLastSynced")]
    public string? SteamLastSynced { get; set; }
    [Name("ManualPlaytimeMinutes")]
    public string? ManualPlaytimeMinutes { get; set; }
    public string? Description { get; set; }
    public string? FiltersJson { get; set; }
    public string? SortingJson { get; set; }
    public string? IsPublic { get; set; }
    public string? CreatedBy { get; set; }
    public string? HistoryField { get; set; }
    public string? HistoryOldValue { get; set; }
    public string? HistoryNewValue { get; set; }
}

public class UpdateImageUrlsResult
{
    public int TotalGames { get; set; }
    public int UpdatedGames { get; set; }
    public int SkippedGames { get; set; }
    public int AlreadyCorrect { get; set; }
    public int NoImagesFound { get; set; }
    public List<string> Errors { get; set; } = new();
    public bool NasAccessible { get; set; }
    public string? NasWarning { get; set; }
}
