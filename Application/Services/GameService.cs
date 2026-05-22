using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using GamesDatabase.Api.Application.Interfaces;
using GamesDatabase.Api.Application.Mapping;
using GamesDatabase.Api.Common;
using GamesDatabase.Api.Contracts;
using GamesDatabase.Api.Domain.Entities;
using GamesDatabase.Api.Infrastructure.Persistence;

namespace GamesDatabase.Api.Application.Services;

public class GameService : IGameService
{
    private const int MaxManualPlaytimeMinutes = 600_000;
    private readonly GamesDbContext _context;
    private readonly IViewFilterService _viewFilterService;
    private readonly IGameHistoryService _historyService;

    public GameService(
        GamesDbContext context,
        IViewFilterService viewFilterService,
        IGameHistoryService historyService)
    {
        _context = context;
        _viewFilterService = viewFilterService;
        _historyService = historyService;
    }

    public async Task<GameServiceResult<PagedResult<GameDto>>> GetGamesAsync(GameQueryParameters parameters, int userId)
    {
        var requestedViewName = parameters.ViewName?.Trim();
        requestedViewName = string.Equals(requestedViewName, "default", StringComparison.OrdinalIgnoreCase)
            ? null
            : requestedViewName;

        var query = _context.Games
            .Where(g => g.UserId == userId)
            .Include(g => g.Status)
            .Include(g => g.Platform)
            .Include(g => g.GamePlayWiths)
                .ThenInclude(gpw => gpw.PlayWith)
            .Include(g => g.PlayedStatus)
            .AsQueryable();

        // Verificar si se debe aplicar una vista
        ViewConfiguration? viewConfiguration = null;
        if (parameters.ViewId.HasValue || !string.IsNullOrEmpty(requestedViewName))
        {
            GameView? gameView = null;

            if (parameters.ViewId.HasValue)
            {
                gameView = await _context.GameViews.AsNoTracking().FirstOrDefaultAsync(v => v.Id == parameters.ViewId.Value && v.UserId == userId);
            }
            else if (!string.IsNullOrEmpty(requestedViewName))
            {
                gameView = await _context.GameViews
                    .AsNoTracking()
                    .FirstOrDefaultAsync(v => v.Name == requestedViewName && v.UserId == userId);
            }

            if (gameView == null)
            {
                return GameServiceResult<PagedResult<GameDto>>.BadRequest($"Vista no encontrada: {parameters.ViewId?.ToString() ?? requestedViewName}");
            }

            try
            {
                if (!string.IsNullOrEmpty(gameView.FiltersJson))
                {
                    var trimmed = gameView.FiltersJson.TrimStart();
                    if (trimmed.StartsWith("{"))
                    {
                        var fullConfig = JsonSerializer.Deserialize<ViewConfiguration>(gameView.FiltersJson);
                        if (fullConfig != null)
                        {
                            viewConfiguration = fullConfig;
                        }
                    }
                    else if (trimmed.StartsWith("["))
                    {
                        var filters = JsonSerializer.Deserialize<List<ViewFilter>>(gameView.FiltersJson);
                        if (filters != null && filters.Any())
                        {
                            viewConfiguration = new ViewConfiguration
                            {
                                FilterGroups = new List<FilterGroup>
                                {
                                    new FilterGroup { Filters = filters }
                                }
                            };
                        }
                    }
                }

                if (!string.IsNullOrEmpty(gameView.SortingJson))
                {
                    var sorting = JsonSerializer.Deserialize<List<ViewSort>>(gameView.SortingJson);
                    if (sorting != null)
                    {
                        if (viewConfiguration == null) viewConfiguration = new ViewConfiguration();
                        viewConfiguration.Sorting = sorting;
                    }
                }
            }
            catch (JsonException ex)
            {
                return GameServiceResult<PagedResult<GameDto>>.BadRequest($"Error procesando configuración de vista: {ex.Message}");
            }
        }

        if (viewConfiguration != null)
        {
            try
            {
                query = _viewFilterService.ApplyFilters(query, viewConfiguration, userId);
            }
            catch (Exception ex)
            {
                return GameServiceResult<PagedResult<GameDto>>.BadRequest($"Error aplicando filtros de vista: {ex.Message}");
            }
        }
        else
        {
            query = ApplyTraditionalFilters(query, parameters, userId);
            query = ApplyTraditionalSorting(query, parameters);
        }

        var totalCount = await query.CountAsync();

        var games = await query
            .Skip(parameters.Skip)
            .Take(parameters.Take)
            .ToListAsync();

        var gameDtos = games.Select(g => g.ToDto()).ToList();
        await FillSteamAchievementStatsAsync(gameDtos, userId);

        return GameServiceResult<PagedResult<GameDto>>.Ok(new PagedResult<GameDto>
        {
            Data = gameDtos,
            TotalCount = totalCount,
            Page = parameters.Page,
            PageSize = parameters.PageSize
        });
    }

    public async Task<GameDto?> GetGameByIdAsync(int id, int userId)
    {
        var game = await _context.Games
            .Where(g => g.UserId == userId)
            .Include(g => g.Status)
            .Include(g => g.Platform)
            .Include(g => g.GamePlayWiths)
                .ThenInclude(gpw => gpw.PlayWith)
            .Include(g => g.PlayedStatus)
            .FirstOrDefaultAsync(g => g.Id == id);

        if (game == null)
            return null;

        var gameDto = game.ToDto();
        await FillSteamAchievementStatsAsync(new[] { gameDto }, userId);

        return gameDto;
    }

    public async Task<GameServiceResult<GameDto>> CreateGameAsync(GameCreateDto gameDto, int userId)
    {
        var statusExists = await _context.GameStatuses
            .AnyAsync(s => s.Id == gameDto.StatusId && s.UserId == userId);
        if (!statusExists)
            return GameServiceResult<GameDto>.BadRequest("Invalid StatusId for current user");

        if (gameDto.PlatformId.HasValue)
        {
            var platformExists = await _context.GamePlatforms
                .AnyAsync(p => p.Id == gameDto.PlatformId.Value && p.UserId == userId);
            if (!platformExists)
                return GameServiceResult<GameDto>.BadRequest("Invalid PlatformId for current user");
        }

        if (gameDto.PlayedStatusId.HasValue)
        {
            var playedStatusExists = await _context.GamePlayedStatuses
                .AnyAsync(ps => ps.Id == gameDto.PlayedStatusId.Value && ps.UserId == userId);
            if (!playedStatusExists)
                return GameServiceResult<GameDto>.BadRequest("Invalid PlayedStatusId for current user");
        }

        var manualPlaytimeValidation = ValidateManualPlaytime(gameDto.ManualPlaytimeMinutes);
        if (manualPlaytimeValidation != null)
        {
            return GameServiceResult<GameDto>.BadRequest(manualPlaytimeValidation);
        }

        var game = gameDto.ToEntity();
        game.UserId = userId;
        _context.Games.Add(game);
        await _context.SaveChangesAsync();

        if (gameDto.PlayWithIds != null && gameDto.PlayWithIds.Any())
        {
            var validPlayWithIds = await _context.GamePlayWiths
                .Where(pw => gameDto.PlayWithIds.Contains(pw.Id) && pw.UserId == userId)
                .Select(pw => pw.Id)
                .ToListAsync();

            if (validPlayWithIds.Count != gameDto.PlayWithIds.Count)
                return GameServiceResult<GameDto>.BadRequest("One or more PlayWithIds are invalid for current user");

            foreach (var playWithId in validPlayWithIds)
            {
                _context.GamePlayWithMappings.Add(new GamePlayWithMapping
                {
                    GameId = game.Id,
                    PlayWithId = playWithId
                });
            }
            await _context.SaveChangesAsync();
        }

        // Cargar las relaciones para el DTO de respuesta
        await _context.Entry(game)
            .Reference(g => g.Status)
            .LoadAsync();
        await _context.Entry(game)
            .Reference(g => g.Platform)
            .LoadAsync();
        await _context.Entry(game)
            .Collection(g => g.GamePlayWiths)
            .LoadAsync();
        await _context.Entry(game)
            .Reference(g => g.PlayedStatus)
            .LoadAsync();

        foreach (var gpw in game.GamePlayWiths)
        {
            await _context.Entry(gpw)
                .Reference(m => m.PlayWith)
                .LoadAsync();
        }

        await _historyService.RecordCreatedAsync(game, userId);

        return GameServiceResult<GameDto>.Ok(game.ToDto());
    }

    public async Task<GameServiceResult> UpdateGameAsync(int id, JsonElement gameDto, int userId)
    {
        var game = await _context.Games
            .Include(g => g.Status)
            .Include(g => g.Platform)
            .Include(g => g.PlayedStatus)
            .FirstOrDefaultAsync(g => g.Id == id && g.UserId == userId);

        if (game == null)
            return GameServiceResult.NotFoundResult();

        // Capturar nombres legibles ANTES de aplicar cambios (para historial)
        var oldStatusName = game.Status?.Name;
        var oldPlatformName = game.Platform?.Name;
        var oldPlayedStatusName = game.PlayedStatus?.Name;

        string? newStatusName = oldStatusName;
        string? newPlatformName = oldPlatformName;
        string? newPlayedStatusName = oldPlayedStatusName;

        if (gameDto.TryGetProperty("statusId", out var statusIdForHistory) && statusIdForHistory.ValueKind != JsonValueKind.Null)
        {
            var newStatusId = statusIdForHistory.GetInt32();
            if (newStatusId != game.StatusId)
            {
                var newStatus = await _context.GameStatuses.FirstOrDefaultAsync(s => s.Id == newStatusId && s.UserId == userId);
                newStatusName = newStatus?.Name;
            }
        }

        if (gameDto.TryGetProperty("platformId", out var platformIdForHistory))
        {
            var newPlatformId = platformIdForHistory.ValueKind == JsonValueKind.Null ? (int?)null : platformIdForHistory.GetInt32();
            if (newPlatformId != game.PlatformId)
            {
                newPlatformName = newPlatformId.HasValue
                    ? (await _context.GamePlatforms.FirstOrDefaultAsync(p => p.Id == newPlatformId.Value && p.UserId == userId))?.Name
                    : null;
            }
        }

        if (gameDto.TryGetProperty("playedStatusId", out var playedStatusIdForHistory))
        {
            var newPlayedStatusId = playedStatusIdForHistory.ValueKind == JsonValueKind.Null ? (int?)null : playedStatusIdForHistory.GetInt32();
            if (newPlayedStatusId != game.PlayedStatusId)
            {
                newPlayedStatusName = newPlayedStatusId.HasValue
                    ? (await _context.GamePlayedStatuses.FirstOrDefaultAsync(ps => ps.Id == newPlayedStatusId.Value && ps.UserId == userId))?.Name
                    : null;
            }
        }

        // Snapshot inmutable para el historial (antes de mutar game)
        var snapshot = new Game
        {
            Id = game.Id,
            Name = game.Name,
            StatusId = game.StatusId,
            Grade = game.Grade,
            Critic = game.Critic,
            CriticProvider = game.CriticProvider,
            Story = game.Story,
            Completion = game.Completion,
            PlatformId = game.PlatformId,
            Released = game.Released,
            Started = game.Started,
            Finished = game.Finished,
            Comment = game.Comment,
            PlayedStatusId = game.PlayedStatusId,
            Logo = game.Logo,
            Cover = game.Cover,
            IsCheaperByKey = game.IsCheaperByKey,
            KeyStoreUrl = game.KeyStoreUrl,
            ManualPlaytimeMinutes = game.ManualPlaytimeMinutes,
            IsManuallyCompleted = game.IsManuallyCompleted
        };

        // Actualizar solo los campos que están presentes en el JSON
        if (gameDto.TryGetProperty("statusId", out var statusIdElement) && statusIdElement.ValueKind != JsonValueKind.Null)
        {
            game.StatusId = statusIdElement.GetInt32();
        }

        if (gameDto.TryGetProperty("name", out var nameElement) && nameElement.ValueKind != JsonValueKind.Null)
        {
            game.Name = nameElement.GetString() ?? string.Empty;
        }

        if (gameDto.TryGetProperty("grade", out var gradeElement))
        {
            game.Grade = gradeElement.ValueKind == JsonValueKind.Null ? null : gradeElement.GetInt32();
        }

        if (gameDto.TryGetProperty("critic", out var criticElement))
        {
            game.Critic = criticElement.ValueKind == JsonValueKind.Null ? null : criticElement.GetInt32();
        }

        if (gameDto.TryGetProperty("criticProvider", out var criticProviderElement))
        {
            game.CriticProvider = criticProviderElement.ValueKind == JsonValueKind.Null ? null : criticProviderElement.GetString();
        }

        if (gameDto.TryGetProperty("story", out var storyElement))
        {
            game.Story = storyElement.ValueKind == JsonValueKind.Null ? null : storyElement.GetInt32();
        }

        if (gameDto.TryGetProperty("completion", out var completionElement))
        {
            game.Completion = completionElement.ValueKind == JsonValueKind.Null ? null : completionElement.GetInt32();
        }

        if (gameDto.TryGetProperty("platformId", out var platformIdElement))
        {
            game.PlatformId = platformIdElement.ValueKind == JsonValueKind.Null ? null : platformIdElement.GetInt32();
        }

        if (gameDto.TryGetProperty("released", out var releasedElement))
        {
            game.Released = releasedElement.ValueKind == JsonValueKind.Null ? null : releasedElement.GetString();
        }

        if (gameDto.TryGetProperty("started", out var startedElement))
        {
            game.Started = startedElement.ValueKind == JsonValueKind.Null ? null : startedElement.GetString();
        }

        if (gameDto.TryGetProperty("finished", out var finishedElement))
        {
            var finishedValue = finishedElement.ValueKind == JsonValueKind.Null ? null : finishedElement.GetString();
            if (game.Finished != finishedValue)
            {
                game.SteamFinishedSource = string.IsNullOrWhiteSpace(finishedValue) ? null : "manual";
            }
            game.Finished = finishedValue;
        }

        if (gameDto.TryGetProperty("comment", out var commentElement))
        {
            game.Comment = commentElement.ValueKind == JsonValueKind.Null ? null : commentElement.GetString();
        }

        if (gameDto.TryGetProperty("playWithIds", out var playWithIdsElement))
        {
            var existingMappings = await _context.GamePlayWithMappings
                .Where(gpw => gpw.GameId == id)
                .ToListAsync();
            _context.GamePlayWithMappings.RemoveRange(existingMappings);

            if (playWithIdsElement.ValueKind == JsonValueKind.Array)
            {
                var playWithIds = playWithIdsElement.EnumerateArray()
                    .Select(e => e.GetInt32())
                    .ToList();

                foreach (var playWithId in playWithIds)
                {
                    _context.GamePlayWithMappings.Add(new GamePlayWithMapping
                    {
                        GameId = id,
                        PlayWithId = playWithId
                    });
                }
            }

            _context.Entry(game).State = EntityState.Modified;
        }

        if (gameDto.TryGetProperty("playedStatusId", out var playedStatusIdElement))
        {
            game.PlayedStatusId = playedStatusIdElement.ValueKind == JsonValueKind.Null ? null : playedStatusIdElement.GetInt32();
        }

        if (gameDto.TryGetProperty("logo", out var logoElement))
        {
            var logoUrl = logoElement.ValueKind == JsonValueKind.Null ? null : logoElement.GetString();
            if (!string.IsNullOrWhiteSpace(logoUrl) && !IsValidUrl(logoUrl))
            {
                return GameServiceResult.BadRequest("Invalid logo URL format");
            }
            game.Logo = logoUrl;
        }

        if (gameDto.TryGetProperty("cover", out var coverElement))
        {
            var coverUrl = coverElement.ValueKind == JsonValueKind.Null ? null : coverElement.GetString();
            if (!string.IsNullOrWhiteSpace(coverUrl) && !IsValidUrl(coverUrl))
            {
                return GameServiceResult.BadRequest("Invalid cover URL format");
            }
            game.Cover = coverUrl;
        }

        if (gameDto.TryGetProperty("isCheaperByKey", out var isCheaperByKeyElement))
        {
            game.IsCheaperByKey = isCheaperByKeyElement.ValueKind == JsonValueKind.Null ? null : isCheaperByKeyElement.GetBoolean();
        }

        if (gameDto.TryGetProperty("keyStoreUrl", out var keyStoreUrlElement))
        {
            var keyStoreUrl = keyStoreUrlElement.ValueKind == JsonValueKind.Null ? null : keyStoreUrlElement.GetString();
            if (!string.IsNullOrWhiteSpace(keyStoreUrl) && !IsValidUrl(keyStoreUrl))
            {
                return GameServiceResult.BadRequest("Invalid key store URL format");
            }
            game.KeyStoreUrl = keyStoreUrl;
        }

        if (gameDto.TryGetProperty("steamAppId", out var steamAppIdElement))
        {
            game.SteamAppId = steamAppIdElement.ValueKind == JsonValueKind.Null ? null : steamAppIdElement.GetInt32();
            if (game.SteamAppId == null)
            {
                game.SteamFinishedSource = string.IsNullOrWhiteSpace(game.Finished) ? null : "manual";
                game.SteamFinishedLastValue = null;
                game.SteamFinishedSyncedAt = null;
                game.SteamFinishedRejectedValue = null;
            }
        }

        if (gameDto.TryGetProperty("manualPlaytimeMinutes", out var manualPlaytimeElement))
        {
            var manualPlaytime = manualPlaytimeElement.ValueKind == JsonValueKind.Null ? (int?)null : manualPlaytimeElement.GetInt32();
            var validationError = ValidateManualPlaytime(manualPlaytime);
            if (validationError != null)
            {
                return GameServiceResult.BadRequest(validationError);
            }

            game.ManualPlaytimeMinutes = manualPlaytime;
        }

        if (gameDto.TryGetProperty("isManuallyCompleted", out var isManuallyCompletedElement))
        {
            game.IsManuallyCompleted = isManuallyCompletedElement.ValueKind != JsonValueKind.Null && isManuallyCompletedElement.GetBoolean();
        }

        game.CalculateScore();

        try
        {
            await _context.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            if (!GameExists(id, userId))
            {
                return GameServiceResult.NotFoundResult();
            }
            else
            {
                throw;
            }
        }

        await _historyService.RecordUpdatedAsync(
            snapshot, gameDto, userId,
            oldStatusName, newStatusName,
            oldPlatformName, newPlatformName,
            oldPlayedStatusName, newPlayedStatusName);

        return GameServiceResult.Ok();
    }

    public async Task<bool> DeleteGameAsync(int id, int userId)
    {
        var game = await _context.Games
            .FirstOrDefaultAsync(g => g.Id == id && g.UserId == userId);

        if (game == null)
            return false;

        await _historyService.RecordDeletedAsync(game, userId);

        _context.Games.Remove(game);
        await _context.SaveChangesAsync();

        return true;
    }

    public async Task<GameServiceResult<BulkUpdateResult>> BulkUpdateGamesAsync(BulkUpdateGameDto bulkUpdate, int userId)
    {
        if (bulkUpdate.GameIds == null || bulkUpdate.GameIds.Length == 0)
        {
            return GameServiceResult<BulkUpdateResult>.BadRequest("No game IDs provided");
        }

        var games = await _context.Games
            .Where(g => bulkUpdate.GameIds.Contains(g.Id) && g.UserId == userId)
            .ToListAsync();

        if (games.Count == 0)
        {
            return GameServiceResult<BulkUpdateResult>.NotFoundResult("No games found with provided IDs");
        }

        int updatedCount = 0;

        foreach (var game in games)
        {
            bool updated = false;

            if (bulkUpdate.StatusId.HasValue)
            {
                game.StatusId = bulkUpdate.StatusId.Value;
                updated = true;
            }

            if (bulkUpdate.PlatformId.HasValue)
            {
                game.PlatformId = bulkUpdate.PlatformId.Value;
                updated = true;
            }

            if (bulkUpdate.PlayedStatusId.HasValue)
            {
                game.PlayedStatusId = bulkUpdate.PlayedStatusId.Value;
                updated = true;
            }

            if (bulkUpdate.IsCheaperByKey.HasValue)
            {
                game.IsCheaperByKey = bulkUpdate.IsCheaperByKey.Value;
                updated = true;
            }

            if (bulkUpdate.PlayWithIds != null)
            {
                var existingMappings = await _context.GamePlayWithMappings
                    .Where(gpw => gpw.GameId == game.Id)
                    .ToListAsync();
                _context.GamePlayWithMappings.RemoveRange(existingMappings);

                foreach (var playWithId in bulkUpdate.PlayWithIds)
                {
                    _context.GamePlayWithMappings.Add(new GamePlayWithMapping
                    {
                        GameId = game.Id,
                        PlayWithId = playWithId
                    });
                }
                updated = true;
            }

            if (updated)
            {
                game.UpdatedAt = DateTime.UtcNow;
                updatedCount++;
            }
        }

        await _context.SaveChangesAsync();

        return GameServiceResult<BulkUpdateResult>.Ok(new BulkUpdateResult
        {
            UpdatedCount = updatedCount,
            TotalRequested = bulkUpdate.GameIds.Length
        });
    }

    // ─── Private helpers ──────────────────────────────────────────────────────

    private IQueryable<Game> ApplyTraditionalFilters(IQueryable<Game> query, GameQueryParameters parameters, int userId)
    {
        var includeReplayDates = parameters.IncludeReplayDates != false;

        if (!string.IsNullOrEmpty(parameters.Search))
        {
            var searchLower = parameters.Search.ToLower();
            var searchNoAccents = RemoveDiacritics(searchLower);

            var searchWithE = searchNoAccents.Replace("e", "é");
            var searchWithO = searchNoAccents.Replace("o", "ó");
            var searchWithA = searchNoAccents.Replace("a", "á");
            var searchWithI = searchNoAccents.Replace("i", "í");
            var searchWithU = searchNoAccents.Replace("u", "ú");

            query = query.Where(g =>
                EF.Functions.Like(EF.Functions.Collate(g.Name, "NOCASE"), $"%{searchLower}%") ||
                EF.Functions.Like(EF.Functions.Collate(g.Name, "NOCASE"), $"%{searchNoAccents}%") ||
                EF.Functions.Like(EF.Functions.Collate(g.Name, "NOCASE"), $"%{searchWithE}%") ||
                EF.Functions.Like(EF.Functions.Collate(g.Name, "NOCASE"), $"%{searchWithO}%") ||
                EF.Functions.Like(EF.Functions.Collate(g.Name, "NOCASE"), $"%{searchWithA}%") ||
                EF.Functions.Like(EF.Functions.Collate(g.Name, "NOCASE"), $"%{searchWithI}%") ||
                EF.Functions.Like(EF.Functions.Collate(g.Name, "NOCASE"), $"%{searchWithU}%") ||
                (g.Comment != null && (
                    EF.Functions.Like(EF.Functions.Collate(g.Comment, "NOCASE"), $"%{searchLower}%") ||
                    EF.Functions.Like(EF.Functions.Collate(g.Comment, "NOCASE"), $"%{searchNoAccents}%") ||
                    EF.Functions.Like(EF.Functions.Collate(g.Comment, "NOCASE"), $"%{searchWithE}%") ||
                    EF.Functions.Like(EF.Functions.Collate(g.Comment, "NOCASE"), $"%{searchWithO}%") ||
                    EF.Functions.Like(EF.Functions.Collate(g.Comment, "NOCASE"), $"%{searchWithA}%") ||
                    EF.Functions.Like(EF.Functions.Collate(g.Comment, "NOCASE"), $"%{searchWithI}%") ||
                    EF.Functions.Like(EF.Functions.Collate(g.Comment, "NOCASE"), $"%{searchWithU}%")
                )));
        }

        if (parameters.StatusId.HasValue)
        {
            query = query.Where(g => g.StatusId == parameters.StatusId.Value);
        }

        if (parameters.ExcludeStatusIds?.Length > 0)
        {
            query = query.Where(g => !parameters.ExcludeStatusIds.Contains(g.StatusId));
        }

        if (parameters.PlatformId.HasValue)
        {
            query = query.Where(g => g.PlatformId == parameters.PlatformId.Value);
        }

        if (parameters.PlayWithId.HasValue)
        {
            query = query.Where(g => g.GamePlayWiths.Any(gpw => gpw.PlayWithId == parameters.PlayWithId.Value));
        }

        if (parameters.PlayedStatusId.HasValue)
        {
            query = query.Where(g => g.PlayedStatusId == parameters.PlayedStatusId.Value);
        }

        if (parameters.MinGrade.HasValue)
        {
            query = query.Where(g => g.Grade >= parameters.MinGrade.Value);
        }

        if (parameters.MaxGrade.HasValue)
        {
            query = query.Where(g => g.Grade <= parameters.MaxGrade.Value);
        }

        if (!string.IsNullOrEmpty(parameters.Released))
        {
            query = includeReplayDates
                ? query.Where(g =>
                    (g.Released != null && g.Released.Contains(parameters.Released)) ||
                    g.GameReplays.Any(r => r.Released != null && r.Released.Contains(parameters.Released)))
                : query.Where(g => g.Released != null && g.Released.Contains(parameters.Released));
        }

        if (parameters.ReleasedYear.HasValue)
        {
            var yearPrefix = parameters.ReleasedYear.Value.ToString();
            query = includeReplayDates
                ? query.Where(g =>
                    (g.Released != null && g.Released.StartsWith(yearPrefix)) ||
                    g.GameReplays.Any(r => r.Released != null && r.Released.StartsWith(yearPrefix)))
                : query.Where(g => g.Released != null && g.Released.StartsWith(yearPrefix));
        }

        if (!string.IsNullOrEmpty(parameters.Started))
        {
            query = includeReplayDates
                ? query.Where(g =>
                    (g.Started != null && g.Started.Contains(parameters.Started)) ||
                    g.GameReplays.Any(r => r.Started != null && r.Started.Contains(parameters.Started)))
                : query.Where(g => g.Started != null && g.Started.Contains(parameters.Started));
        }

        if (parameters.StartedYear.HasValue)
        {
            var yearPrefix = parameters.StartedYear.Value.ToString();
            query = includeReplayDates
                ? query.Where(g =>
                    (g.Started != null && g.Started.StartsWith(yearPrefix)) ||
                    g.GameReplays.Any(r => r.Started != null && r.Started.StartsWith(yearPrefix)))
                : query.Where(g => g.Started != null && g.Started.StartsWith(yearPrefix));
        }

        if (!string.IsNullOrEmpty(parameters.Finished))
        {
            query = includeReplayDates
                ? query.Where(g =>
                    (g.Finished != null && g.Finished.Contains(parameters.Finished)) ||
                    g.GameReplays.Any(r => r.Finished != null && r.Finished.Contains(parameters.Finished)))
                : query.Where(g => g.Finished != null && g.Finished.Contains(parameters.Finished));
        }

        if (parameters.FinishedYear.HasValue)
        {
            var yearPrefix = parameters.FinishedYear.Value.ToString();
            query = includeReplayDates
                ? query.Where(g =>
                    (g.Finished != null && g.Finished.StartsWith(yearPrefix)) ||
                    g.GameReplays.Any(r => r.Finished != null && r.Finished.StartsWith(yearPrefix)))
                : query.Where(g => g.Finished != null && g.Finished.StartsWith(yearPrefix));
        }

        if (parameters.IsCheaperByKey.HasValue)
        {
            query = query.Where(g => g.IsCheaperByKey == parameters.IsCheaperByKey.Value);
        }

        if (parameters.ShowIncomplete == true)
        {
            query = query.Where(g =>
                g.Status != null && g.Status.StatusType == SpecialStatusType.NotFulfilled ||
                string.IsNullOrEmpty(g.Cover) ||
                string.IsNullOrEmpty(g.Logo) ||
                g.PlatformId == null
            );
        }

        if (!string.IsNullOrEmpty(parameters.CriticProvider))
        {
            query = query.Where(g =>
                parameters.CriticProvider.Equals("Default", StringComparison.OrdinalIgnoreCase)
                    ? g.CriticProvider == null
                    : g.CriticProvider == parameters.CriticProvider
            );
        }

        if (parameters.HasReplays.HasValue)
        {
            query = parameters.HasReplays.Value
                ? query.Where(g => g.GameReplays.Any())
                : query.Where(g => !g.GameReplays.Any());
        }

        if (parameters.HasSteamApp.HasValue)
        {
            query = parameters.HasSteamApp.Value
                ? query.Where(g => g.SteamAppId.HasValue)
                : query.Where(g => !g.SteamAppId.HasValue);
        }

        if (parameters.FullCompletion == true)
        {
            query = query.Where(g =>
                g.IsManuallyCompleted ||
                (
                    _context.SteamAchievements.Any(a => a.UserId == userId && a.GameId == g.Id) &&
                    !_context.SteamAchievements.Any(a => a.UserId == userId && a.GameId == g.Id && !a.Achieved)
                ));
        }

        if (!string.IsNullOrWhiteSpace(parameters.MissingDuration))
        {
            query = parameters.MissingDuration.ToLowerInvariant() switch
            {
                "story" => query.Where(g => !g.Story.HasValue || g.Story <= 0),
                "completion" => query.Where(g => !g.Completion.HasValue || g.Completion <= 0),
                "both" => query.Where(g => (!g.Story.HasValue || g.Story <= 0) && (!g.Completion.HasValue || g.Completion <= 0)),
                "any" => query.Where(g => (!g.Story.HasValue || g.Story <= 0) || (!g.Completion.HasValue || g.Completion <= 0)),
                _ => query
            };
        }

        // ─── Replay filters ─────────────────────────────────────────────
        var hasReplayFilter =
            !string.IsNullOrEmpty(parameters.ReplayStartedFrom) ||
            !string.IsNullOrEmpty(parameters.ReplayStartedTo) ||
            !string.IsNullOrEmpty(parameters.ReplayFinishedFrom) ||
            !string.IsNullOrEmpty(parameters.ReplayFinishedTo) ||
            parameters.ReplayTypeId.HasValue ||
            parameters.ReplayGradeMin.HasValue ||
            parameters.ReplayGradeMax.HasValue;

        if (hasReplayFilter)
        {
            var matchAll = string.Equals(parameters.ReplayMatchMode, "all", StringComparison.OrdinalIgnoreCase);

            if (matchAll)
            {
                query = query.Where(g => g.GameReplays.Any() && g.GameReplays.All(r =>
                    (string.IsNullOrEmpty(parameters.ReplayStartedFrom) || string.Compare(r.Started, parameters.ReplayStartedFrom, StringComparison.Ordinal) >= 0) &&
                    (string.IsNullOrEmpty(parameters.ReplayStartedTo) || string.Compare(r.Started, parameters.ReplayStartedTo, StringComparison.Ordinal) <= 0) &&
                    (string.IsNullOrEmpty(parameters.ReplayFinishedFrom) || string.Compare(r.Finished, parameters.ReplayFinishedFrom, StringComparison.Ordinal) >= 0) &&
                    (string.IsNullOrEmpty(parameters.ReplayFinishedTo) || string.Compare(r.Finished, parameters.ReplayFinishedTo, StringComparison.Ordinal) <= 0) &&
                    (!parameters.ReplayTypeId.HasValue || r.ReplayTypeId == parameters.ReplayTypeId.Value) &&
                    (!parameters.ReplayGradeMin.HasValue || (r.Grade.HasValue && r.Grade >= parameters.ReplayGradeMin.Value)) &&
                    (!parameters.ReplayGradeMax.HasValue || (r.Grade.HasValue && r.Grade <= parameters.ReplayGradeMax.Value))
                ));
            }
            else
            {
                query = query.Where(g => g.GameReplays.Any(r =>
                    (string.IsNullOrEmpty(parameters.ReplayStartedFrom) || string.Compare(r.Started, parameters.ReplayStartedFrom, StringComparison.Ordinal) >= 0) &&
                    (string.IsNullOrEmpty(parameters.ReplayStartedTo) || string.Compare(r.Started, parameters.ReplayStartedTo, StringComparison.Ordinal) <= 0) &&
                    (string.IsNullOrEmpty(parameters.ReplayFinishedFrom) || string.Compare(r.Finished, parameters.ReplayFinishedFrom, StringComparison.Ordinal) >= 0) &&
                    (string.IsNullOrEmpty(parameters.ReplayFinishedTo) || string.Compare(r.Finished, parameters.ReplayFinishedTo, StringComparison.Ordinal) <= 0) &&
                    (!parameters.ReplayTypeId.HasValue || r.ReplayTypeId == parameters.ReplayTypeId.Value) &&
                    (!parameters.ReplayGradeMin.HasValue || (r.Grade.HasValue && r.Grade >= parameters.ReplayGradeMin.Value)) &&
                    (!parameters.ReplayGradeMax.HasValue || (r.Grade.HasValue && r.Grade <= parameters.ReplayGradeMax.Value))
                ));
            }
        }

        return query;
    }

    private IQueryable<Game> ApplyTraditionalSorting(IQueryable<Game> query, GameQueryParameters parameters)
    {
        if (!string.IsNullOrEmpty(parameters.SortBy))
        {
            query = parameters.SortBy.ToLower() switch
            {
                "name" => parameters.SortDescending ? query.OrderByDescending(g => EF.Functions.Collate(g.Name, "NOCASE")) : query.OrderBy(g => EF.Functions.Collate(g.Name, "NOCASE")),
                "grade" => parameters.SortDescending ? query.OrderByDescending(g => g.Grade).ThenBy(g => EF.Functions.Collate(g.Name, "NOCASE")) : query.OrderBy(g => g.Grade).ThenBy(g => EF.Functions.Collate(g.Name, "NOCASE")),
                "critic" => parameters.SortDescending ? query.OrderByDescending(g => g.Critic).ThenBy(g => EF.Functions.Collate(g.Name, "NOCASE")) : query.OrderBy(g => g.Critic).ThenBy(g => EF.Functions.Collate(g.Name, "NOCASE")),
                "story" => parameters.SortDescending ? query.OrderByDescending(g => g.Story).ThenBy(g => EF.Functions.Collate(g.Name, "NOCASE")) : query.OrderBy(g => g.Story).ThenBy(g => EF.Functions.Collate(g.Name, "NOCASE")),
                "storyduration" => parameters.SortDescending ? query.OrderByDescending(g => (double?)g.Story).ThenBy(g => EF.Functions.Collate(g.Name, "NOCASE")) : query.OrderBy(g => (double?)g.Story).ThenBy(g => EF.Functions.Collate(g.Name, "NOCASE")),
                "completion" => parameters.SortDescending ? query.OrderByDescending(g => g.Completion).ThenBy(g => EF.Functions.Collate(g.Name, "NOCASE")) : query.OrderBy(g => g.Completion).ThenBy(g => EF.Functions.Collate(g.Name, "NOCASE")),
                "completionduration" => parameters.SortDescending ? query.OrderByDescending(g => (double?)g.Completion).ThenBy(g => EF.Functions.Collate(g.Name, "NOCASE")) : query.OrderBy(g => (double?)g.Completion).ThenBy(g => EF.Functions.Collate(g.Name, "NOCASE")),
                "status" => parameters.SortDescending ? query.OrderByDescending(g => g.Status.SortOrder).ThenByDescending(g => EF.Functions.Collate(g.Name, "NOCASE")) : query.OrderBy(g => g.Status.SortOrder).ThenBy(g => EF.Functions.Collate(g.Name, "NOCASE")),
                "score" => parameters.SortDescending ? query.OrderByDescending(g => (double?)g.Score).ThenBy(g => EF.Functions.Collate(g.Name, "NOCASE")) : query.OrderBy(g => (double?)g.Score).ThenBy(g => EF.Functions.Collate(g.Name, "NOCASE")),
                "released" => parameters.SortDescending ? query.OrderByDescending(g => g.Released).ThenBy(g => EF.Functions.Collate(g.Name, "NOCASE")) : query.OrderBy(g => g.Released).ThenBy(g => EF.Functions.Collate(g.Name, "NOCASE")),
                "started" => parameters.SortDescending ? query.OrderByDescending(g => g.Started).ThenBy(g => EF.Functions.Collate(g.Name, "NOCASE")) : query.OrderBy(g => g.Started).ThenBy(g => EF.Functions.Collate(g.Name, "NOCASE")),
                "finished" => parameters.SortDescending ? query.OrderByDescending(g => g.Finished).ThenBy(g => EF.Functions.Collate(g.Name, "NOCASE")) : query.OrderBy(g => g.Finished).ThenBy(g => EF.Functions.Collate(g.Name, "NOCASE")),
                "createdat" or "created" => parameters.SortDescending ? query.OrderByDescending(g => g.CreatedAt).ThenBy(g => EF.Functions.Collate(g.Name, "NOCASE")) : query.OrderBy(g => g.CreatedAt).ThenBy(g => EF.Functions.Collate(g.Name, "NOCASE")),
                "updatedat" or "updated" or "lastedited" => parameters.SortDescending ? query.OrderByDescending(g => g.UpdatedAt).ThenBy(g => EF.Functions.Collate(g.Name, "NOCASE")) : query.OrderBy(g => g.UpdatedAt).ThenBy(g => EF.Functions.Collate(g.Name, "NOCASE")),
                "steamplaytime" or "steamplaytimeforever" or "steamhours" => parameters.SortDescending ? query.OrderByDescending(g => g.ManualPlaytimeMinutes ?? g.SteamPlaytimeForever ?? 0).ThenBy(g => EF.Functions.Collate(g.Name, "NOCASE")) : query.OrderBy(g => g.ManualPlaytimeMinutes ?? g.SteamPlaytimeForever ?? 0).ThenBy(g => EF.Functions.Collate(g.Name, "NOCASE")),
                "creation" or "id" => parameters.SortDescending ? query.OrderByDescending(g => g.Id) : query.OrderBy(g => g.Id),
                _ => query.OrderBy(g => g.Status.SortOrder).ThenBy(g => EF.Functions.Collate(g.Status.Name, "NOCASE")).ThenBy(g => EF.Functions.Collate(g.Name, "NOCASE"))
            };
        }
        else
        {
            query = query.OrderBy(g => g.Status.SortOrder).ThenBy(g => EF.Functions.Collate(g.Status.Name, "NOCASE")).ThenBy(g => EF.Functions.Collate(g.Name, "NOCASE"));
        }

        return query;
    }

    private async Task FillSteamAchievementStatsAsync(IEnumerable<GameDto> gameDtos, int userId)
    {
        var dtoList = gameDtos.ToList();
        if (dtoList.Count == 0) return;

        var gameIds = dtoList.Select(g => g.Id).ToList();
        var stats = await _context.SteamAchievements
            .Where(a => a.UserId == userId && a.GameId.HasValue && gameIds.Contains(a.GameId.Value))
            .GroupBy(a => a.GameId!.Value)
            .Select(g => new
            {
                GameId = g.Key,
                Total = g.Count(),
                Unlocked = g.Count(a => a.Achieved)
            })
            .ToDictionaryAsync(g => g.GameId);

        foreach (var dto in dtoList)
        {
            if (!stats.TryGetValue(dto.Id, out var stat)) continue;
            dto.SteamAchievementsTotal = stat.Total;
            dto.SteamAchievementsUnlocked = stat.Unlocked;
        }
    }

    private bool GameExists(int id, int userId)
    {
        return _context.Games.Any(e => e.Id == id && e.UserId == userId);
    }

    private static bool IsValidUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return false;

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uriResult))
            return false;

        return uriResult.Scheme == Uri.UriSchemeHttp || uriResult.Scheme == Uri.UriSchemeHttps;
    }

    private static string? ValidateManualPlaytime(int? minutes)
    {
        if (!minutes.HasValue)
        {
            return null;
        }

        if (minutes.Value < 0)
        {
            return "Manual playtime cannot be negative";
        }

        if (minutes.Value > MaxManualPlaytimeMinutes)
        {
            return "Manual playtime is too large";
        }

        return null;
    }

    private static string RemoveDiacritics(string text)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        var normalizedString = text.Normalize(System.Text.NormalizationForm.FormD);
        var stringBuilder = new System.Text.StringBuilder();

        foreach (var c in normalizedString)
        {
            var unicodeCategory = System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c);
            if (unicodeCategory != System.Globalization.UnicodeCategory.NonSpacingMark)
            {
                stringBuilder.Append(c);
            }
        }

        return stringBuilder.ToString().Normalize(System.Text.NormalizationForm.FormC);
    }
}
