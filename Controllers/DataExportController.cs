using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Globalization;
using System.Text;
using CsvHelper;
using CsvHelper.Configuration;
using GamesDatabase.Api.Data;
using GamesDatabase.Api.Models;
using GamesDatabase.Api.Configuration;
using GamesDatabase.Api.Services;

namespace GamesDatabase.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class DataExportController : BaseApiController
{
    private readonly GamesDbContext _context;
    private readonly ExportSettings _exportSettings;
    private readonly ILogger<DataExportController> _logger;
    private readonly IConfiguration _configuration;
    private readonly INetworkSyncService _networkSyncService;

    public DataExportController(
        GamesDbContext context,
        IOptions<ExportSettings> exportSettings,
        ILogger<DataExportController> logger,
        IConfiguration configuration,
        INetworkSyncService networkSyncService)
    {
        _context = context;
        _exportSettings = exportSettings.Value;
        _logger = logger;
        _configuration = configuration;
        _networkSyncService = networkSyncService;
    }

    [HttpPost("update-image-urls")]
    [Authorize]
    public async Task<ActionResult<UpdateImageUrlsResult>> UpdateImageUrls()
    {
        // Only allow from localhost or specific local IP
        var host = Request.Host.Host;
        var isLocalHost = host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
                          host.Equals("127.0.0.1", StringComparison.OrdinalIgnoreCase) ||
                          host.Equals("192.168.0.32", StringComparison.OrdinalIgnoreCase);

        if (!isLocalHost)
        {
            return StatusCode(403, new { message = "Image URL update is only available on local installations" });
        }

        var result = new UpdateImageUrlsResult();
        var networkSyncPath = _configuration["NetworkSync:NetworkPath"];

        if (string.IsNullOrWhiteSpace(networkSyncPath))
        {
            return BadRequest("NetworkSync:NetworkPath not configured");
        }

        if (!Directory.Exists(networkSyncPath))
        {
            return BadRequest($"NetworkSync path does not exist: {networkSyncPath}");
        }

        var userId = GetCurrentUserIdOrDefault(1);
        var userPath = Path.Combine(networkSyncPath, userId.ToString());
        var gamesPath = Path.Combine(userPath, "Games");
        if (!Directory.Exists(gamesPath))
        {
            return BadRequest($"Games path does not exist: {gamesPath}");
        }

        var games = await _context.Games
            .Where(g => g.UserId == userId)
            .ToListAsync();

        result.TotalGames = games.Count;

        foreach (var game in games)
        {
            try
            {
                var folderName = MakeSafeFolderName(game.Name);
                if (string.IsNullOrWhiteSpace(folderName))
                    folderName = "Unknown_Game";

                var gamePath = Path.Combine(gamesPath, folderName);

                if (!Directory.Exists(gamePath))
                {
                    _logger.LogWarning("Folder not found for game '{GameName}'. Expected path: {ExpectedPath}", game.Name, gamePath);
                    result.SkippedGames++;
                    continue;
                }

                bool updated = false;
                bool alreadyCorrect = true;
                var extensions = new[] { ".jpg", ".jpeg", ".png", ".webp", ".gif" };

                // Check for logo
                string? logoUrl = null;
                var imageBaseUrl = _configuration["ImageSettings:BaseUrl"] ?? string.Empty;
                foreach (var ext in extensions)
                {
                    var logoPath = Path.Combine(gamePath, $"logo{ext}");
                    if (System.IO.File.Exists(logoPath))
                    {
                        logoUrl = $"{imageBaseUrl}/game-images/{userId}/Games/{folderName}/logo{ext}";
                        break;
                    }
                }

                // Update logo - if found use actual file, otherwise use default logo.png
                if (logoUrl == null)
                {
                    logoUrl = $"{imageBaseUrl}/game-images/{userId}/Games/{folderName}/logo.png";
                }

                if (game.Logo != logoUrl)
                {
                    game.Logo = logoUrl;
                    updated = true;
                    alreadyCorrect = false;
                }

                // Check for cover
                string? coverUrl = null;
                foreach (var ext in extensions)
                {
                    var coverPath = Path.Combine(gamePath, $"cover{ext}");
                    if (System.IO.File.Exists(coverPath))
                    {
                        coverUrl = $"{imageBaseUrl}/game-images/{userId}/Games/{folderName}/cover{ext}";
                        break;
                    }
                }

                // Update cover - if found use actual file, otherwise use default cover.png
                if (coverUrl == null)
                {
                    coverUrl = $"{imageBaseUrl}/game-images/{userId}/Games/{folderName}/cover.png";
                }

                if (game.Cover != coverUrl)
                {
                    game.Cover = coverUrl;
                    updated = true;
                    alreadyCorrect = false;
                }

                if (updated)
                {
                    result.UpdatedGames++;
                }
                else if (alreadyCorrect)
                {
                    result.AlreadyCorrect++;
                }
                else
                {
                    result.NoImagesFound++;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating image URLs for game '{GameName}'", game.Name);
                result.Errors.Add($"{game.Name}: {ex.Message}");
            }
        }

        if (result.UpdatedGames > 0)
        {
            await _context.SaveChangesAsync();
            _logger.LogInformation("Updated image URLs for {Count} games", result.UpdatedGames);
        }

        return Ok(result);
    }

    [HttpGet("analyze-folders")]
    [Authorize]
    public async Task<ActionResult<FolderAnalysisResult>> AnalyzeFolders()
    {
        // Only allow from localhost or specific local IP
        if (!IsLocalRequest())
        {
            return StatusCode(403, new { message = "Folder analysis is only available on localhost or 192.168.0.32" });
        }

        try
        {
            var userId = GetCurrentUserIdOrDefault(1);
            var result = await _networkSyncService.AnalyzeFoldersAsync(userId);
            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error analyzing folders");
            return StatusCode(500, new { message = "Error analyzing folders", error = ex.Message });
        }
    }

    private bool IsLocalRequest()
    {
        var host = Request.Host.Host.ToLowerInvariant();
        return host == "localhost" || host == "127.0.0.1" || host == "192.168.0.32";
    }

    private static string MakeSafeFolderName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return string.Empty;

        // Normalize to remove accents/diacritics (é→e, ó→o, á→a, etc.)
        var normalized = name.Normalize(System.Text.NormalizationForm.FormD);
        var stringBuilder = new System.Text.StringBuilder();

        foreach (var c in normalized)
        {
            // Skip diacritical marks (accent marks)
            var category = System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c);
            if (category == System.Globalization.UnicodeCategory.NonSpacingMark)
            {
                continue; // Skip accent marks
            }

            // Only allow: letters, digits, space, hyphen, underscore, dot, parentheses
            // Everything else (apostrophes, commas, symbols like ™®, etc.) is removed
            if (char.IsLetterOrDigit(c) || c == ' ' || c == '-' || c == '_' || c == '.' || c == '(' || c == ')')
            {
                stringBuilder.Append(c);
            }
        }

        var safeName = stringBuilder.ToString();

        // Remove any remaining invalid filename characters (but keep underscores)
        var invalidChars = Path.GetInvalidFileNameChars();
        foreach (var invalidChar in invalidChars)
        {
            if (invalidChar != '_') // Don't replace underscores
            {
                safeName = safeName.Replace(invalidChar, '_');
            }
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

    [HttpGet("full")]
    public async Task<IActionResult> ExportFullDatabase()
    {
        try
        {
            var userId = GetCurrentUserIdOrDefault(1);
            var allRecords = new List<FullExportModel>();
            var platforms = await _context.GamePlatforms.Where(p => p.UserId == userId).OrderBy(p => p.SortOrder).ThenBy(p => EF.Functions.Collate(p.Name, "NOCASE")).ToListAsync();
            foreach (var p in platforms)
            {
                allRecords.Add(new FullExportModel { Type = "Platform", Name = p.Name, Color = p.Color, IsActive = p.IsActive.ToString(), SortOrder = p.SortOrder.ToString() });
            }
            var statuses = await _context.GameStatuses.Where(s => s.UserId == userId).OrderBy(s => s.SortOrder).ThenBy(s => EF.Functions.Collate(s.Name, "NOCASE")).ToListAsync();
            foreach (var s in statuses)
            {
                allRecords.Add(new FullExportModel { Type = "Status", Name = s.Name, Color = s.Color, IsActive = s.IsActive.ToString(), SortOrder = s.SortOrder.ToString(), IsDefault = s.IsDefault.ToString(), StatusType = s.StatusType.ToString() });
            }
            var playWiths = await _context.GamePlayWiths.Where(p => p.UserId == userId).OrderBy(p => p.SortOrder).ThenBy(p => EF.Functions.Collate(p.Name, "NOCASE")).ToListAsync();
            foreach (var p in playWiths)
            {
                allRecords.Add(new FullExportModel { Type = "PlayWith", Name = p.Name, Color = p.Color, IsActive = p.IsActive.ToString(), SortOrder = p.SortOrder.ToString() });
            }
            var playedStatuses = await _context.GamePlayedStatuses.Where(p => p.UserId == userId).OrderBy(p => p.SortOrder).ThenBy(p => EF.Functions.Collate(p.Name, "NOCASE")).ToListAsync();
            foreach (var p in playedStatuses)
            {
                allRecords.Add(new FullExportModel { Type = "PlayedStatus", Name = p.Name, Color = p.Color, IsActive = p.IsActive.ToString(), SortOrder = p.SortOrder.ToString() });
            }
            var views = await _context.GameViews.Where(v => v.UserId == userId).OrderBy(v => EF.Functions.Collate(v.Name, "NOCASE")).ToListAsync();
            foreach (var v in views)
            {
                allRecords.Add(new FullExportModel
                {
                    Type = "View",
                    Name = v.Name,
                    Description = v.Description ?? "",
                    FiltersJson = v.FiltersJson,
                    SortingJson = v.SortingJson ?? "",
                    IsPublic = v.IsPublic.ToString(),
                    CreatedBy = v.CreatedBy ?? ""
                });
            }
            var games = await _context.Games
                .Where(g => g.UserId == userId)
                .Include(g => g.Status)
                .Include(g => g.Platform)
                .Include(g => g.GamePlayWiths)
                    .ThenInclude(gpw => gpw.PlayWith)
                .Include(g => g.PlayedStatus)
                .OrderBy(g => EF.Functions.Collate(g.Name, "NOCASE"))
                .ToListAsync();
            foreach (var g in games)
            {
                var playWithNames = g.GamePlayWiths != null && g.GamePlayWiths.Any()
                    ? string.Join(", ", g.GamePlayWiths.Select(gpw => gpw.PlayWith.Name))
                    : "";
                allRecords.Add(new FullExportModel { Type = "Game", Name = g.Name, Status = g.Status?.Name ?? "", Platform = g.Platform?.Name ?? "", PlayWith = playWithNames, PlayedStatus = g.PlayedStatus?.Name ?? "", Released = g.Released ?? "", Started = g.Started ?? "", Finished = g.Finished ?? "", Score = g.Score?.ToString() ?? "", Critic = g.Critic?.ToString() ?? "", Grade = g.Grade?.ToString() ?? "", Completion = g.Completion?.ToString() ?? "", Story = g.Story?.ToString() ?? "", Comment = g.Comment ?? "", Logo = g.Logo ?? "", Cover = g.Cover ?? "" });
            }
            using var memoryStream = new MemoryStream();
            using var writer = new StreamWriter(memoryStream, Encoding.UTF8);
            using var csv = new CsvWriter(writer, new CsvConfiguration(CultureInfo.InvariantCulture) { Delimiter = _exportSettings.CsvDelimiter });
            await csv.WriteRecordsAsync(allRecords);
            await writer.FlushAsync();
            var fileName = $"games_database_full_{DateTime.Now:yyyyMMdd_HHmmss}.csv";
            return File(memoryStream.ToArray(), "text/csv", fileName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error exporting full database");
            return StatusCode(500, new { message = "Error al exportar la base de datos completa", details = ex.Message });
        }
    }

    [HttpPost("full")]
    public async Task<IActionResult> ImportFullDatabase(IFormFile csvFile)
    {
        if (csvFile == null || csvFile.Length == 0) return BadRequest(new { message = "No se proporcionó ningún archivo CSV" });
        if (!csvFile.FileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase)) return BadRequest(new { message = "El archivo debe tener extensión .csv" });
        try
        {
            var userId = GetCurrentUserIdOrDefault(1);
            var results = new { platformsImported = 0, platformsUpdated = 0, statusesImported = 0, statusesUpdated = 0, playWithsImported = 0, playWithsUpdated = 0, playedStatusesImported = 0, playedStatusesUpdated = 0, viewsImported = 0, viewsUpdated = 0, gamesImported = 0, gamesUpdated = 0, errors = new List<string>() };
            using var stream = csvFile.OpenReadStream();
            using var reader = new StreamReader(stream, Encoding.UTF8);
            using var csv = new CsvReader(reader, new CsvConfiguration(CultureInfo.InvariantCulture) { Delimiter = _exportSettings.CsvDelimiter, HeaderValidated = null, MissingFieldFound = null });
            var allRecords = csv.GetRecords<FullExportModel>().ToList();
            var platforms = allRecords.Where(r => r.Type == "Platform").ToList();
            var statuses = allRecords.Where(r => r.Type == "Status").ToList();
            var playWiths = allRecords.Where(r => r.Type == "PlayWith").ToList();
            var playedStatuses = allRecords.Where(r => r.Type == "PlayedStatus").ToList();
            var views = allRecords.Where(r => r.Type == "View").ToList();
            var games = allRecords.Where(r => r.Type == "Game").ToList();
            foreach (var record in platforms)
            {
                if (string.IsNullOrWhiteSpace(record.Name)) continue;
                var existing = await _context.GamePlatforms.FirstOrDefaultAsync(p => p.Name.ToLower() == record.Name.ToLower() && p.UserId == userId);
                if (existing != null) { existing.Color = record.Color; existing.IsActive = bool.Parse(record.IsActive ?? "true"); existing.SortOrder = int.Parse(record.SortOrder ?? "0"); results = results with { platformsUpdated = results.platformsUpdated + 1 }; }
                else { _context.GamePlatforms.Add(new GamePlatform { UserId = userId, Name = record.Name, Color = record.Color, IsActive = bool.Parse(record.IsActive ?? "true"), SortOrder = int.Parse(record.SortOrder ?? "0") }); results = results with { platformsImported = results.platformsImported + 1 }; }
            }
            await _context.SaveChangesAsync();
            foreach (var record in statuses)
            {
                if (string.IsNullOrWhiteSpace(record.Name)) continue;

                // Parse the StatusType from CSV
                SpecialStatusType statusType = SpecialStatusType.None;
                if (!string.IsNullOrWhiteSpace(record.StatusType))
                {
                    Enum.TryParse<SpecialStatusType>(record.StatusType, out statusType);
                }

                GameStatus? existing = null;
                if (statusType != SpecialStatusType.None && bool.Parse(record.IsDefault ?? "false"))
                {
                    existing = await _context.GameStatuses.FirstOrDefaultAsync(s =>
                        s.StatusType == statusType && s.IsDefault && s.UserId == userId);
                }
                else
                {
                    existing = await _context.GameStatuses.FirstOrDefaultAsync(s =>
                        s.Name.ToLower() == record.Name.ToLower() && s.UserId == userId);
                }

                if (existing != null)
                {
                    existing.Name = record.Name; // Update name too in case it changed
                    existing.Color = record.Color;
                    existing.IsActive = bool.Parse(record.IsActive ?? "true");
                    existing.SortOrder = int.Parse(record.SortOrder ?? "0");
                    existing.IsDefault = bool.Parse(record.IsDefault ?? "false");
                    existing.StatusType = statusType;
                    results = results with { statusesUpdated = results.statusesUpdated + 1 };
                }
                else
                {
                    var newStatus = new GameStatus
                    {
                        UserId = userId,
                        Name = record.Name,
                        Color = record.Color,
                        IsActive = bool.Parse(record.IsActive ?? "true"),
                        SortOrder = int.Parse(record.SortOrder ?? "0"),
                        IsDefault = bool.Parse(record.IsDefault ?? "false"),
                        StatusType = statusType
                    };
                    _context.GameStatuses.Add(newStatus);
                    results = results with { statusesImported = results.statusesImported + 1 };
                }
            }
            await _context.SaveChangesAsync();
            foreach (var record in playWiths)
            {
                if (string.IsNullOrWhiteSpace(record.Name)) continue;
                var existing = await _context.GamePlayWiths.FirstOrDefaultAsync(p => p.Name.ToLower() == record.Name.ToLower() && p.UserId == userId);
                if (existing != null) { existing.Color = record.Color; existing.IsActive = bool.Parse(record.IsActive ?? "true"); existing.SortOrder = int.Parse(record.SortOrder ?? "0"); results = results with { playWithsUpdated = results.playWithsUpdated + 1 }; }
                else { _context.GamePlayWiths.Add(new GamePlayWith { UserId = userId, Name = record.Name, Color = record.Color, IsActive = bool.Parse(record.IsActive ?? "true"), SortOrder = int.Parse(record.SortOrder ?? "0") }); results = results with { playWithsImported = results.playWithsImported + 1 }; }
            }
            await _context.SaveChangesAsync();
            foreach (var record in playedStatuses)
            {
                if (string.IsNullOrWhiteSpace(record.Name)) continue;
                var existing = await _context.GamePlayedStatuses.FirstOrDefaultAsync(p => p.Name.ToLower() == record.Name.ToLower() && p.UserId == userId);
                if (existing != null) { existing.Color = record.Color; existing.IsActive = bool.Parse(record.IsActive ?? "true"); existing.SortOrder = int.Parse(record.SortOrder ?? "0"); results = results with { playedStatusesUpdated = results.playedStatusesUpdated + 1 }; }
                else { _context.GamePlayedStatuses.Add(new GamePlayedStatus { UserId = userId, Name = record.Name, Color = record.Color, IsActive = bool.Parse(record.IsActive ?? "true"), SortOrder = int.Parse(record.SortOrder ?? "0") }); results = results with { playedStatusesImported = results.playedStatusesImported + 1 }; }
            }
            await _context.SaveChangesAsync();

            foreach (var record in views)
            {
                if (string.IsNullOrWhiteSpace(record.Name)) continue;
                var existing = await _context.GameViews.FirstOrDefaultAsync(v => v.Name.ToLower() == record.Name.ToLower() && v.UserId == userId);
                if (existing != null)
                {
                    // Overwrite with imported data
                    existing.Description = record.Description;
                    existing.FiltersJson = record.FiltersJson ?? "{}";
                    existing.SortingJson = record.SortingJson;
                    existing.IsPublic = bool.Parse(record.IsPublic ?? "true");
                    existing.CreatedBy = record.CreatedBy;
                    existing.UpdatedAt = DateTime.UtcNow;
                    results = results with { viewsUpdated = results.viewsUpdated + 1 };
                }
                else
                {
                    var newView = new GameView
                    {
                        UserId = userId,
                        Name = record.Name,
                        Description = record.Description,
                        FiltersJson = record.FiltersJson ?? "{}",
                        SortingJson = record.SortingJson,
                        IsPublic = bool.Parse(record.IsPublic ?? "true"),
                        CreatedBy = record.CreatedBy,
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    };
                    _context.GameViews.Add(newView);
                    results = results with { viewsImported = results.viewsImported + 1 };
                }
            }
            await _context.SaveChangesAsync();

            foreach (var record in games)
            {
                try
                {
                    if (string.IsNullOrWhiteSpace(record.Name)) { results.errors.Add("Juego sin nombre encontrado"); continue; }
                    var status = string.IsNullOrWhiteSpace(record.Status) ? null : await _context.GameStatuses.FirstOrDefaultAsync(s => s.Name.ToLower() == record.Status.ToLower() && s.UserId == userId);
                    var platform = string.IsNullOrWhiteSpace(record.Platform) ? null : await _context.GamePlatforms.FirstOrDefaultAsync(p => p.Name.ToLower() == record.Platform.ToLower() && p.UserId == userId);

                    var playWithIds = new List<int>();
                    if (!string.IsNullOrWhiteSpace(record.PlayWith))
                    {
                        var playWithNames = record.PlayWith.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                        foreach (var pwName in playWithNames)
                        {
                            var pw = await _context.GamePlayWiths.FirstOrDefaultAsync(p => p.Name.ToLower() == pwName.ToLower() && p.UserId == userId);
                            if (pw != null) playWithIds.Add(pw.Id);
                        }
                    }

                    var playedStatus = string.IsNullOrWhiteSpace(record.PlayedStatus) ? null : await _context.GamePlayedStatuses.FirstOrDefaultAsync(p => p.Name.ToLower() == record.PlayedStatus.ToLower() && p.UserId == userId);
                    var existing = await _context.Games.Include(g => g.GamePlayWiths).FirstOrDefaultAsync(g => g.Name.ToLower() == record.Name.ToLower() && g.UserId == userId);

                    if (existing != null)
                    {
                        if (status != null) existing.StatusId = status.Id;
                        existing.PlatformId = platform?.Id;
                        existing.PlayedStatusId = playedStatus?.Id;
                        existing.Released = record.Released;
                        existing.Started = record.Started;
                        existing.Finished = record.Finished;
                        existing.Critic = ParseNullableInt(record.Critic);
                        existing.Grade = ParseNullableInt(record.Grade);
                        existing.Completion = ParseNullableInt(record.Completion);
                        existing.Story = ParseNullableInt(record.Story);
                        existing.Comment = record.Comment;
                        existing.Logo = record.Logo;
                        existing.Cover = record.Cover;
                        existing.CalculateScore();

                        // Update PlayWith relationships
                        var existingMappings = existing.GamePlayWiths.ToList();
                        foreach (var mapping in existingMappings)
                        {
                            _context.GamePlayWithMappings.Remove(mapping);
                        }
                        foreach (var pwId in playWithIds)
                        {
                            _context.GamePlayWithMappings.Add(new GamePlayWithMapping { GameId = existing.Id, PlayWithId = pwId });
                        }

                        // Marcar el juego como modificado para actualizar UpdatedAt
                        _context.Entry(existing).State = EntityState.Modified;

                        results = results with { gamesUpdated = results.gamesUpdated + 1 };
                    }
                    else
                    {
                        if (status == null) { results.errors.Add($"No se puede crear '{record.Name}' sin un status válido"); continue; }
                        var newGame = new Game
                        {
                            UserId = userId,
                            Name = record.Name,
                            StatusId = status.Id,
                            PlatformId = platform?.Id,
                            PlayedStatusId = playedStatus?.Id,
                            Released = record.Released,
                            Started = record.Started,
                            Finished = record.Finished,
                            Critic = ParseNullableInt(record.Critic),
                            Grade = ParseNullableInt(record.Grade),
                            Completion = ParseNullableInt(record.Completion),
                            Story = ParseNullableInt(record.Story),
                            Comment = record.Comment,
                            Logo = record.Logo,
                            Cover = record.Cover
                        };
                        newGame.CalculateScore();
                        _context.Games.Add(newGame);
                        await _context.SaveChangesAsync();

                        foreach (var pwId in playWithIds)
                        {
                            _context.GamePlayWithMappings.Add(new GamePlayWithMapping { GameId = newGame.Id, PlayWithId = pwId });
                        }

                        results = results with { gamesImported = results.gamesImported + 1 };
                    }

                    // Save changes after each game to avoid accumulating errors
                    await _context.SaveChangesAsync();
                }
                catch (Exception ex)
                {
                    var errorMessage = ex.Message;
                    if (ex.InnerException != null)
                    {
                        errorMessage += $" | Inner: {ex.InnerException.Message}";
                        if (ex.InnerException.InnerException != null)
                        {
                            errorMessage += $" | Inner2: {ex.InnerException.InnerException.Message}";
                        }
                    }
                    results.errors.Add($"Error procesando '{record.Name}': {errorMessage}");
                    _logger.LogError(ex, "Error processing game: {GameName}", record.Name);
                    // Clear the context to avoid corrupted state
                    _context.ChangeTracker.Clear();
                }
            }
            return Ok(new { message = "Importación completa finalizada (modo MERGE)", catalogs = new { platforms = new { imported = results.platformsImported, updated = results.platformsUpdated }, statuses = new { imported = results.statusesImported, updated = results.statusesUpdated }, playWiths = new { imported = results.playWithsImported, updated = results.playWithsUpdated }, playedStatuses = new { imported = results.playedStatusesImported, updated = results.playedStatusesUpdated } }, views = new { imported = results.viewsImported, updated = results.viewsUpdated }, games = new { imported = results.gamesImported, updated = results.gamesUpdated }, errors = results.errors.Count > 0 ? results.errors : null });
        }
        catch (Exception ex)
        {
            var fullError = ex.Message;
            if (ex.InnerException != null)
            {
                fullError += $" | Inner: {ex.InnerException.Message}";
                if (ex.InnerException.InnerException != null)
                {
                    fullError += $" | Inner2: {ex.InnerException.InnerException.Message}";
                }
            }
            _logger.LogError(ex, "Error importing full database");
            return StatusCode(500, new { message = "Error al importar la base de datos completa", details = fullError });
        }
    }

    private static int? ParseNullableInt(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        return int.TryParse(value, out var result) ? result : null;
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
    public string? Grade { get; set; }
    public string? Completion { get; set; }
    public string? Story { get; set; }
    public string? Comment { get; set; }
    public string? Logo { get; set; }
    public string? Cover { get; set; }
    // View fields
    public string? Description { get; set; }
    public string? FiltersJson { get; set; }
    public string? SortingJson { get; set; }
    public string? IsPublic { get; set; }
    public string? CreatedBy { get; set; }
}

public class UpdateImageUrlsResult
{
    public int TotalGames { get; set; }
    public int UpdatedGames { get; set; }
    public int SkippedGames { get; set; }
    public int AlreadyCorrect { get; set; }
    public int NoImagesFound { get; set; }
    public List<string> Errors { get; set; } = new();
}
