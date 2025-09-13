using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Globalization;
using System.Text;
using CsvHelper;
using CsvHelper.Configuration;
using GamesDatabase.Api.Data;
using GamesDatabase.Api.Models;
using GamesDatabase.Api.Configuration;

namespace GamesDatabase.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class DataExportController : ControllerBase
{
    private readonly GamesDbContext _context;
    private readonly ExportSettings _exportSettings;
    private readonly ILogger<DataExportController> _logger;

    public DataExportController(
        GamesDbContext context,
        IOptions<ExportSettings> exportSettings,
        ILogger<DataExportController> logger)
    {
        _context = context;
        _exportSettings = exportSettings.Value;
        _logger = logger;
    }

    [HttpGet("games/csv")]
    public async Task<IActionResult> ExportGamesAsCsv()
    {
        try
        {
            var games = await _context.Games
                .Include(g => g.Status)
                .Include(g => g.Platform)
                .Include(g => g.PlayWith)
                .Include(g => g.PlayedStatus)
                .OrderBy(g => EF.Functions.Collate(g.Name, "NOCASE"))
                .ToListAsync();

            var csvData = games.Select(g => new GameExportModel
            {
                Id = g.Id,
                Name = g.Name,
                Status = g.Status?.Name ?? "",
                Platform = g.Platform?.Name ?? "",
                PlayWith = g.PlayWith?.Name ?? "",
                PlayedStatus = g.PlayedStatus?.Name ?? "",
                Released = g.Released ?? "",
                Started = g.Started ?? "",
                Finished = g.Finished ?? "",
                Score = g.Score?.ToString() ?? "",
                Critic = g.Critic?.ToString() ?? "",
                Grade = g.Grade?.ToString() ?? "",
                Completion = g.Completion?.ToString() ?? "",
                Story = g.Story?.ToString() ?? "",
                Comment = g.Comment ?? "",
                Logo = g.Logo ?? "",
                Cover = g.Cover ?? ""
            }).ToList();

            using var memoryStream = new MemoryStream();
            using var writer = new StreamWriter(memoryStream, Encoding.UTF8);
            using var csv = new CsvWriter(writer, new CsvConfiguration(CultureInfo.InvariantCulture)
            {
                Delimiter = _exportSettings.CsvDelimiter
            });

            await csv.WriteRecordsAsync(csvData);
            await writer.FlushAsync();

            var fileName = $"games_export_{DateTime.Now:yyyyMMdd_HHmmss}.csv";
            return File(memoryStream.ToArray(), "text/csv", fileName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error exporting games to CSV");
            return StatusCode(500, new { message = "Error al exportar los juegos", details = ex.Message });
        }
    }

    [HttpPost("games/csv")]
    public async Task<IActionResult> ImportGamesFromCsv(IFormFile csvFile)
    {
        if (csvFile == null || csvFile.Length == 0)
        {
            return BadRequest(new { message = "No se proporcionó ningún archivo CSV" });
        }

        if (!csvFile.FileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest(new { message = "El archivo debe tener extensión .csv" });
        }

        try
        {
            var importResults = new List<string>();
            var errors = new List<string>();
            var gamesImported = 0;
            var gamesUpdated = 0;

            using var stream = csvFile.OpenReadStream();
            using var reader = new StreamReader(stream, Encoding.UTF8);
            using var csv = new CsvReader(reader, new CsvConfiguration(CultureInfo.InvariantCulture)
            {
                Delimiter = _exportSettings.CsvDelimiter,
                HeaderValidated = null,
                MissingFieldFound = null
            });

            var records = csv.GetRecords<GameImportModel>().ToList();

            foreach (var record in records)
            {
                try
                {
                    // Validar datos básicos
                    if (string.IsNullOrWhiteSpace(record.Name))
                    {
                        errors.Add($"Juego sin nombre en línea {csv.Parser.Row}");
                        continue;
                    }

                    // Buscar o crear entidades relacionadas
                    var status = await GetOrCreateStatus(record.Status);
                    var platform = await GetOrCreatePlatform(record.Platform);
                    var playWith = await GetOrCreatePlayWith(record.PlayWith);
                    var playedStatus = await GetOrCreatePlayedStatus(record.PlayedStatus);

                    // Buscar juego existente
                    var existingGame = await _context.Games
                        .FirstOrDefaultAsync(g => g.Name.ToLower() == record.Name.ToLower());

                    if (existingGame != null)
                    {
                        // Actualizar juego existente
                        if (status != null) existingGame.Status = status;
                        existingGame.Platform = platform;
                        existingGame.PlayWith = playWith;
                        existingGame.PlayedStatus = playedStatus;
                        existingGame.Released = record.Released;
                        existingGame.Started = record.Started;
                        existingGame.Finished = record.Finished;
                        existingGame.Critic = ParseInt(record.Critic);
                        existingGame.Grade = ParseInt(record.Grade);
                        existingGame.Completion = ParseInt(record.Completion);
                        existingGame.Story = ParseInt(record.Story);
                        existingGame.Comment = record.Comment;
                        existingGame.Logo = record.Logo;
                        existingGame.Cover = record.Cover;

                        // Recalcular score automáticamente
                        existingGame.CalculateScore();

                        gamesUpdated++;
                        importResults.Add($"Actualizado: {record.Name}");
                    }
                    else
                    {
                        // Crear nuevo juego - necesitamos un status válido
                        if (status == null)
                        {
                            errors.Add($"No se puede crear '{record.Name}' sin un status válido");
                            continue;
                        }

                        var newGame = new Game
                        {
                            Name = record.Name,
                            Status = status,
                            Platform = platform,
                            PlayWith = playWith,
                            PlayedStatus = playedStatus,
                            Released = record.Released,
                            Started = record.Started,
                            Finished = record.Finished,
                            Critic = ParseInt(record.Critic),
                            Grade = ParseInt(record.Grade),
                            Completion = ParseInt(record.Completion),
                            Story = ParseInt(record.Story),
                            Comment = record.Comment,
                            Logo = record.Logo,
                            Cover = record.Cover
                        };

                        // Calcular score automáticamente
                        newGame.CalculateScore();

                        _context.Games.Add(newGame);
                        gamesImported++;
                        importResults.Add($"Importado: {record.Name}");
                    }
                }
                catch (Exception ex)
                {
                    errors.Add($"Error procesando '{record.Name}': {ex.Message}");
                }
            }

            await _context.SaveChangesAsync();

            return Ok(new
            {
                message = "Importación completada",
                gamesImported,
                gamesUpdated,
                totalProcessed = gamesImported + gamesUpdated,
                errors = errors.Count > 0 ? errors : null,
                details = importResults
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error importing games from CSV");
            return StatusCode(500, new { message = "Error al importar los juegos", details = ex.Message });
        }
    }

    private async Task<GameStatus?> GetOrCreateStatus(string? statusName)
    {
        if (string.IsNullOrWhiteSpace(statusName)) return null;

        var status = await _context.GameStatuses
            .FirstOrDefaultAsync(s => s.Name.ToLower() == statusName.ToLower());

        if (status == null)
        {
            status = new GameStatus { Name = statusName, IsActive = true };
            _context.GameStatuses.Add(status);
            await _context.SaveChangesAsync();
        }

        return status;
    }

    private async Task<GamePlatform?> GetOrCreatePlatform(string? platformName)
    {
        if (string.IsNullOrWhiteSpace(platformName)) return null;

        var platform = await _context.GamePlatforms
            .FirstOrDefaultAsync(p => p.Name.ToLower() == platformName.ToLower());

        if (platform == null)
        {
            platform = new GamePlatform { Name = platformName, IsActive = true };
            _context.GamePlatforms.Add(platform);
            await _context.SaveChangesAsync();
        }

        return platform;
    }

    private async Task<GamePlayWith?> GetOrCreatePlayWith(string? playWithName)
    {
        if (string.IsNullOrWhiteSpace(playWithName)) return null;

        var playWith = await _context.GamePlayWiths
            .FirstOrDefaultAsync(p => p.Name.ToLower() == playWithName.ToLower());

        if (playWith == null)
        {
            playWith = new GamePlayWith { Name = playWithName, IsActive = true };
            _context.GamePlayWiths.Add(playWith);
            await _context.SaveChangesAsync();
        }

        return playWith;
    }

    private async Task<GamePlayedStatus?> GetOrCreatePlayedStatus(string? playedStatusName)
    {
        if (string.IsNullOrWhiteSpace(playedStatusName)) return null;

        var playedStatus = await _context.GamePlayedStatuses
            .FirstOrDefaultAsync(p => p.Name.ToLower() == playedStatusName.ToLower());

        if (playedStatus == null)
        {
            playedStatus = new GamePlayedStatus { Name = playedStatusName, IsActive = true };
            _context.GamePlayedStatuses.Add(playedStatus);
            await _context.SaveChangesAsync();
        }

        return playedStatus;
    }

    private static int? ParseInt(string? intString)
    {
        if (string.IsNullOrWhiteSpace(intString)) return null;
        return int.TryParse(intString, out var value) ? value : null;
    }
}

public class GameExportModel
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Status { get; set; } = "";
    public string Platform { get; set; } = "";
    public string PlayWith { get; set; } = "";
    public string PlayedStatus { get; set; } = "";
    public string Released { get; set; } = "";
    public string Started { get; set; } = "";
    public string Finished { get; set; } = "";
    public string Score { get; set; } = "";
    public string Critic { get; set; } = "";
    public string Grade { get; set; } = "";
    public string Completion { get; set; } = "";
    public string Story { get; set; } = "";
    public string Comment { get; set; } = "";
    public string Logo { get; set; } = "";
    public string Cover { get; set; } = "";
}

public class GameImportModel
{
    public string Name { get; set; } = "";
    public string Status { get; set; } = "";
    public string Platform { get; set; } = "";
    public string PlayWith { get; set; } = "";
    public string PlayedStatus { get; set; } = "";
    public string Released { get; set; } = "";
    public string Started { get; set; } = "";
    public string Finished { get; set; } = "";
    public string Score { get; set; } = "";
    public string Critic { get; set; } = "";
    public string Grade { get; set; } = "";
    public string Completion { get; set; } = "";
    public string Story { get; set; } = "";
    public string Comment { get; set; } = "";
    public string Logo { get; set; } = "";
    public string Cover { get; set; } = "";
}