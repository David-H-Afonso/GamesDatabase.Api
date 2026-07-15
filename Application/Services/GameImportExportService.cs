using System.Globalization;
using System.Text;
using System.Text.Json;
using CsvHelper;
using CsvHelper.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using GamesDatabase.Api.Application.Interfaces;
using GamesDatabase.Api.Application.Services;
using GamesDatabase.Api.Common;
using GamesDatabase.Api.Configuration;
using GamesDatabase.Api.Contracts;
using GamesDatabase.Api.Controllers;
using GamesDatabase.Api.Domain.Entities;
using GamesDatabase.Api.Infrastructure.Persistence;

namespace GamesDatabase.Api.Application.Services;

public class GameImportExportService : IGameImportExportService
{
    private readonly GamesDbContext _context;
    private readonly ExportSettings _exportSettings;
    private readonly ILogger<GameImportExportService> _logger;
    private readonly INetworkSyncService _networkSyncService;

    private static readonly HttpClient _imageProbeClient = new(
        new HttpClientHandler { AllowAutoRedirect = false, ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator })
    {
        Timeout = TimeSpan.FromSeconds(5),
    };

    private static async Task<string?> DetectImageUrlViaHttpAsync(
        string imageBaseUrl, string imagePath, string[] extensions)
    {
        if (string.IsNullOrWhiteSpace(imageBaseUrl)) return null;

        var tasks = extensions.Select(async ext =>
        {
            var url = $"{imageBaseUrl}/{imagePath}{ext}";
            try
            {
                using var resp = await _imageProbeClient.SendAsync(
                    new HttpRequestMessage(HttpMethod.Head, url));
                return resp.IsSuccessStatusCode ? url : null;
            }
            catch { return null; }
        }).ToArray();

        var results = await Task.WhenAll(tasks);
        return results.FirstOrDefault(r => r != null);
    }

    public GameImportExportService(
        GamesDbContext context,
        IOptions<ExportSettings> exportSettings,
        ILogger<GameImportExportService> logger,
        INetworkSyncService networkSyncService)
    {
        _context = context;
        _exportSettings = exportSettings.Value;
        _logger = logger;
        _networkSyncService = networkSyncService;
    }

    public async Task<UpdateImageUrlsResult> UpdateImageUrlsAsync(int userId, string? imageBaseUrl, string? networkSyncPath, string? configImageBaseUrl, string? networkUsername, string? networkPassword)
    {
        var result = new UpdateImageUrlsResult();

        if (string.IsNullOrWhiteSpace(networkSyncPath))
        {
            throw new InvalidOperationException("NetworkSync:NetworkPath not configured");
        }

        var authError = NetworkPathHelper.EnsureAuthenticated(networkSyncPath, networkUsername, networkPassword);
        if (authError != null)
            _logger.LogWarning("Network path authentication warning: {Error}", authError);

        var userPath = Path.Combine(networkSyncPath, userId.ToString());
        var gamesPath = Path.Combine(userPath, "Games");

        var nasAccessible = Directory.Exists(networkSyncPath) && Directory.Exists(gamesPath);
        result.NasAccessible = nasAccessible;
        if (!nasAccessible)
        {
            var warning = $"NAS UNC path not accessible ({networkSyncPath}). Extension detection will use HTTP HEAD requests against the selected image URL.";
            if (authError != null) warning += $" Auth error: {authError}";
            result.NasWarning = warning;
            _logger.LogWarning("{Warning}", warning);
        }

        var effectiveImageBaseUrl = (!string.IsNullOrWhiteSpace(imageBaseUrl)
            ? imageBaseUrl
            : configImageBaseUrl ?? string.Empty).TrimEnd('/');

        var games = await _context.Games
            .Where(g => g.UserId == userId)
            .ToListAsync();

        result.TotalGames = games.Count;

        foreach (var game in games)
        {
            try
            {
                var folderName = FolderNameHelper.MakeSafeFolderName(game.Name);
                if (string.IsNullOrWhiteSpace(folderName))
                    folderName = "Unknown_Game";

                var gamePath = Path.Combine(gamesPath, folderName);

                if (nasAccessible && !Directory.Exists(gamePath))
                {
                    _logger.LogWarning("Folder not found for game '{GameName}'. Expected path: {ExpectedPath}", game.Name, gamePath);
                    result.SkippedGames++;
                    continue;
                }

                bool updated = false;
                bool alreadyCorrect = true;
                var extensions = new[] { ".jpg", ".jpeg", ".png", ".webp", ".gif", ".ico" };

                string? logoUrl = null;
                if (nasAccessible)
                {
                    foreach (var ext in extensions)
                    {
                        var logoPath = Path.Combine(gamePath, $"logo{ext}");
                        if (File.Exists(logoPath))
                        {
                            logoUrl = $"{effectiveImageBaseUrl}/game-images/{userId}/Games/{folderName}/logo{ext}";
                            break;
                        }
                    }
                }
                else
                {
                    logoUrl = await DetectImageUrlViaHttpAsync(
                        effectiveImageBaseUrl,
                        $"game-images/{userId}/Games/{folderName}/logo",
                        extensions);
                }
                logoUrl ??= $"{effectiveImageBaseUrl}/game-images/{userId}/Games/{folderName}/logo.png";

                if (game.Logo != logoUrl)
                {
                    game.Logo = logoUrl;
                    updated = true;
                    alreadyCorrect = false;
                }

                string? heroUrl = null;
                if (nasAccessible)
                {
                    foreach (var ext in extensions)
                    {
                        var heroPath = Path.Combine(gamePath, $"hero{ext}");
                        if (File.Exists(heroPath))
                        {
                            heroUrl = $"{effectiveImageBaseUrl}/game-images/{userId}/Games/{folderName}/hero{ext}";
                            break;
                        }
                    }

                    // Legacy exports stored the horizontal image as cover.* before Hero existed.
                    // Use it only as a fallback when no hero.* has been written yet.
                    if (heroUrl == null)
                    {
                        foreach (var ext in extensions)
                        {
                            var legacyCoverPath = Path.Combine(gamePath, $"cover{ext}");
                            if (File.Exists(legacyCoverPath))
                            {
                                heroUrl = $"{effectiveImageBaseUrl}/game-images/{userId}/Games/{folderName}/cover{ext}";
                                break;
                            }
                        }
                    }
                }
                else
                {
                    heroUrl = await DetectImageUrlViaHttpAsync(
                        effectiveImageBaseUrl,
                        $"game-images/{userId}/Games/{folderName}/hero",
                        extensions);

                    heroUrl ??= await DetectImageUrlViaHttpAsync(
                        effectiveImageBaseUrl,
                        $"game-images/{userId}/Games/{folderName}/cover",
                        extensions);
                }
                heroUrl ??= $"{effectiveImageBaseUrl}/game-images/{userId}/Games/{folderName}/hero.png";

                if (game.Hero != heroUrl)
                {
                    game.Hero = heroUrl;
                    updated = true;
                    alreadyCorrect = false;
                }

                string? coverUrl = null;
                if (nasAccessible)
                {
                    foreach (var ext in extensions)
                    {
                        var coverPath = Path.Combine(gamePath, $"cover{ext}");
                        if (File.Exists(coverPath))
                        {
                            coverUrl = $"{effectiveImageBaseUrl}/game-images/{userId}/Games/{folderName}/cover{ext}";
                            break;
                        }
                    }
                }
                else
                {
                    coverUrl = await DetectImageUrlViaHttpAsync(
                        effectiveImageBaseUrl,
                        $"game-images/{userId}/Games/{folderName}/cover",
                        extensions);
                }

                if (coverUrl != null && game.Cover != coverUrl)
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

        return result;
    }

    public async Task<FolderAnalysisResult> AnalyzeFoldersAsync(int userId)
    {
        return await _networkSyncService.AnalyzeFoldersAsync(userId);
    }

    public async Task<DatabaseDuplicatesResult> AnalyzeDuplicateGamesAsync(int userId)
    {
        return await _networkSyncService.AnalyzeDatabaseDuplicatesAsync(userId);
    }

    public async Task<byte[]> ExportFullDatabaseAsync(int userId)
    {
        var allRecords = new List<FullExportModel>();
        var platforms = await _context.GamePlatforms.Where(p => p.UserId == userId).OrderBy(p => p.SortOrder).ThenBy(p => EF.Functions.Collate(p.Name, "NOCASE")).ToListAsync();
        foreach (var p in platforms)
        {
            allRecords.Add(new FullExportModel { Type = "Platform", Name = p.Name, Color = p.Color, Logo = p.Logo ?? "", IsActive = p.IsActive.ToString(), SortOrder = p.SortOrder.ToString() });
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
            allRecords.Add(new FullExportModel { Type = "Game", Name = g.Name, Status = g.Status?.Name ?? "", Platform = g.Platform?.Name ?? "", PlayWith = playWithNames, PlayedStatus = g.PlayedStatus?.Name ?? "", Released = g.Released ?? "", Started = g.Started ?? "", Finished = g.Finished ?? "", Score = g.Score?.ToString() ?? "", Critic = g.Critic?.ToString() ?? "", CriticProvider = g.CriticProvider ?? "", Grade = g.Grade?.ToString() ?? "", Completion = g.Completion?.ToString() ?? "", Story = g.Story?.ToString() ?? "", Comment = g.Comment ?? "", Logo = g.Logo ?? "", Hero = g.Hero ?? "", Cover = g.Cover ?? "", IsCheaperByKey = g.IsCheaperByKey?.ToString() ?? "", KeyStoreUrl = g.KeyStoreUrl ?? "", SteamAppId = g.SteamAppId?.ToString() ?? "", SteamPlaytimeForever = g.SteamPlaytimeForever?.ToString() ?? "", SteamPlaytime2Weeks = g.SteamPlaytime2Weeks?.ToString() ?? "", SteamLastSynced = g.SteamLastSynced?.ToString("O") ?? "", ManualPlaytimeMinutes = g.ManualPlaytimeMinutes?.ToString() ?? "" });
        }

        var replayTypes = await _context.GameReplayTypes.Where(r => r.UserId == userId).OrderBy(r => r.SortOrder).ThenBy(r => EF.Functions.Collate(r.Name, "NOCASE")).ToListAsync();
        foreach (var rt in replayTypes)
        {
            allRecords.Add(new FullExportModel
            {
                Type = "ReplayType",
                Name = rt.Name,
                Color = rt.Color,
                IsActive = rt.IsActive.ToString(),
                SortOrder = rt.SortOrder.ToString(),
                IsDefault = rt.IsDefault.ToString(),
                StatusType = rt.ReplayType.ToString()
            });
        }

        var replays = await _context.GameReplays
            .Where(r => r.UserId == userId)
            .Include(r => r.Game)
            .Include(r => r.ReplayType)
            .OrderBy(r => r.Game.Name)
            .ThenBy(r => r.CreatedAt)
            .ToListAsync();
        foreach (var r in replays)
        {
            allRecords.Add(new FullExportModel
            {
                Type = "Replay",
                Name = r.Game?.Name ?? "",
                Status = r.ReplayType?.Name ?? "",
                Started = r.Started ?? "",
                Finished = r.Finished ?? "",
                Grade = r.Grade?.ToString() ?? "",
                Comment = r.Notes ?? ""
            });
        }

        var historyEntries = await _context.GameHistoryEntries
            .Where(h => h.UserId == userId)
            .OrderBy(h => h.GameName)
            .ThenBy(h => h.ChangedAt)
            .ToListAsync();
        foreach (var h in historyEntries)
        {
            allRecords.Add(new FullExportModel
            {
                Type = "History",
                Name = h.GameName,
                Status = h.ActionType,
                Started = h.ChangedAt.ToString("O"),
                HistoryField = h.Field,
                HistoryOldValue = h.OldValue ?? "",
                HistoryNewValue = h.NewValue ?? ""
            });
        }

        using var memoryStream = new MemoryStream();
        using var writer = new StreamWriter(memoryStream, Encoding.UTF8);
        using var csv = new CsvWriter(writer, new CsvConfiguration(CultureInfo.InvariantCulture) { Delimiter = _exportSettings.CsvDelimiter });
        await csv.WriteRecordsAsync(allRecords);
        await writer.FlushAsync();
        return memoryStream.ToArray();
    }

    public async Task<object> ImportFullDatabaseAsync(Stream csvStream, int userId)
    {
        var results = new { platformsImported = 0, platformsUpdated = 0, statusesImported = 0, statusesUpdated = 0, playWithsImported = 0, playWithsUpdated = 0, playedStatusesImported = 0, playedStatusesUpdated = 0, viewsImported = 0, viewsUpdated = 0, gamesImported = 0, gamesUpdated = 0, replayTypesImported = 0, replayTypesUpdated = 0, replaysImported = 0, replaysUpdated = 0, historyImported = 0, errors = new List<string>() };
        using var reader = new StreamReader(csvStream, Encoding.UTF8);
        using var csv = new CsvReader(reader, new CsvConfiguration(CultureInfo.InvariantCulture) { Delimiter = _exportSettings.CsvDelimiter, HeaderValidated = null, MissingFieldFound = null });
        csv.Read();
        csv.ReadHeader();
        var hasHeroHeader = csv.HeaderRecord?.Any(header => string.Equals(header, "Hero", StringComparison.OrdinalIgnoreCase)) == true;
        var allRecords = csv.GetRecords<FullExportModel>().ToList();
        var platforms = allRecords.Where(r => r.Type == "Platform").ToList();
        var statuses = allRecords.Where(r => r.Type == "Status").ToList();
        var playWiths = allRecords.Where(r => r.Type == "PlayWith").ToList();
        var playedStatuses = allRecords.Where(r => r.Type == "PlayedStatus").ToList();
        var views = allRecords.Where(r => r.Type == "View").ToList();
        var games = allRecords.Where(r => r.Type == "Game").ToList();
        var replayTypesRecords = allRecords.Where(r => r.Type == "ReplayType").ToList();
        var replaysRecords = allRecords.Where(r => r.Type == "Replay").ToList();
        var historyRecords = allRecords.Where(r => r.Type == "History").ToList();
        foreach (var record in platforms)
        {
            if (string.IsNullOrWhiteSpace(record.Name)) continue;
            var existing = await _context.GamePlatforms.FirstOrDefaultAsync(p => p.Name.ToLower() == record.Name.ToLower() && p.UserId == userId);
            if (existing != null) { existing.Color = record.Color; existing.Logo = string.IsNullOrWhiteSpace(record.Logo) ? null : record.Logo; existing.IsActive = bool.Parse(record.IsActive ?? "true"); existing.SortOrder = int.Parse(record.SortOrder ?? "0"); results = results with { platformsUpdated = results.platformsUpdated + 1 }; }
            else { _context.GamePlatforms.Add(new GamePlatform { UserId = userId, Name = record.Name, Color = record.Color, Logo = string.IsNullOrWhiteSpace(record.Logo) ? null : record.Logo, IsActive = bool.Parse(record.IsActive ?? "true"), SortOrder = int.Parse(record.SortOrder ?? "0") }); results = results with { platformsImported = results.platformsImported + 1 }; }
        }
        await _context.SaveChangesAsync();
        foreach (var record in statuses)
        {
            if (string.IsNullOrWhiteSpace(record.Name)) continue;

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
                existing.Name = record.Name;
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
                    existing.CriticProvider = record.CriticProvider;
                    existing.Grade = ParseNullableInt(record.Grade);
                    existing.Completion = ParseNullableInt(record.Completion);
                    existing.Story = ParseNullableInt(record.Story);
                    existing.Comment = record.Comment;
                    existing.Logo = record.Logo;
                    existing.Hero = ResolveLegacyHero(record.Hero, record.Cover, hasHeroHeader);
                    existing.Cover = ResolveLegacyCover(record.Hero, record.Cover, hasHeroHeader);
                    existing.IsCheaperByKey = bool.TryParse(record.IsCheaperByKey, out var existingCbk) ? existingCbk : (bool?)null;
                    existing.KeyStoreUrl = record.KeyStoreUrl;
                    existing.SteamAppId = ParseNullableInt(record.SteamAppId);
                    existing.SteamPlaytimeForever = ParseNullableInt(record.SteamPlaytimeForever);
                    existing.SteamPlaytime2Weeks = ParseNullableInt(record.SteamPlaytime2Weeks);
                    existing.SteamLastSynced = ParseNullableDateTime(record.SteamLastSynced);
                    existing.ManualPlaytimeMinutes = ParseNullableInt(record.ManualPlaytimeMinutes);
                    existing.CalculateScore();

                    var existingMappings = existing.GamePlayWiths.ToList();
                    foreach (var mapping in existingMappings)
                    {
                        _context.GamePlayWithMappings.Remove(mapping);
                    }
                    foreach (var pwId in playWithIds)
                    {
                        _context.GamePlayWithMappings.Add(new GamePlayWithMapping { GameId = existing.Id, PlayWithId = pwId });
                    }

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
                        CriticProvider = record.CriticProvider,
                        Grade = ParseNullableInt(record.Grade),
                        Completion = ParseNullableInt(record.Completion),
                        Story = ParseNullableInt(record.Story),
                        Comment = record.Comment,
                        Logo = record.Logo,
                        Hero = ResolveLegacyHero(record.Hero, record.Cover, hasHeroHeader),
                        Cover = ResolveLegacyCover(record.Hero, record.Cover, hasHeroHeader),
                        IsCheaperByKey = bool.TryParse(record.IsCheaperByKey, out var newCbk) ? newCbk : (bool?)null,
                        KeyStoreUrl = record.KeyStoreUrl,
                        SteamAppId = ParseNullableInt(record.SteamAppId),
                        SteamPlaytimeForever = ParseNullableInt(record.SteamPlaytimeForever),
                        SteamPlaytime2Weeks = ParseNullableInt(record.SteamPlaytime2Weeks),
                        SteamLastSynced = ParseNullableDateTime(record.SteamLastSynced),
                        ManualPlaytimeMinutes = ParseNullableInt(record.ManualPlaytimeMinutes)
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
                _context.ChangeTracker.Clear();
            }
        }

        // Import ReplayTypes
        foreach (var record in replayTypesRecords)
        {
            if (string.IsNullOrWhiteSpace(record.Name)) continue;
            SpecialReplayType specialReplayType = SpecialReplayType.None;
            if (!string.IsNullOrWhiteSpace(record.StatusType))
                Enum.TryParse<SpecialReplayType>(record.StatusType, out specialReplayType);

            GameReplayType? existing = null;
            if (specialReplayType != SpecialReplayType.None && bool.Parse(record.IsDefault ?? "false"))
            {
                existing = await _context.GameReplayTypes.FirstOrDefaultAsync(rt =>
                    rt.ReplayType == specialReplayType && rt.IsDefault && rt.UserId == userId);
            }
            else
            {
                existing = await _context.GameReplayTypes.FirstOrDefaultAsync(rt =>
                    rt.Name.ToLower() == record.Name.ToLower() && rt.UserId == userId);
            }

            if (existing != null)
            {
                existing.Name = record.Name;
                existing.Color = record.Color;
                existing.IsActive = bool.Parse(record.IsActive ?? "true");
                existing.SortOrder = int.Parse(record.SortOrder ?? "0");
                existing.IsDefault = bool.Parse(record.IsDefault ?? "false");
                existing.ReplayType = specialReplayType;
                results = results with { replayTypesUpdated = results.replayTypesUpdated + 1 };
            }
            else
            {
                _context.GameReplayTypes.Add(new GameReplayType
                {
                    UserId = userId,
                    Name = record.Name,
                    Color = record.Color,
                    IsActive = bool.Parse(record.IsActive ?? "true"),
                    SortOrder = int.Parse(record.SortOrder ?? "0"),
                    IsDefault = bool.Parse(record.IsDefault ?? "false"),
                    ReplayType = specialReplayType
                });
                results = results with { replayTypesImported = results.replayTypesImported + 1 };
            }
        }
        await _context.SaveChangesAsync();

        // Import Replays
        foreach (var record in replaysRecords)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(record.Name)) continue;
                var game = await _context.Games.FirstOrDefaultAsync(g => g.Name.ToLower() == record.Name.ToLower() && g.UserId == userId);
                if (game == null) { results.errors.Add($"Replay: Game '{record.Name}' not found"); continue; }

                var replayTypeName = record.Status ?? "";
                var replayType = string.IsNullOrWhiteSpace(replayTypeName)
                    ? await _context.GameReplayTypes.FirstOrDefaultAsync(rt => rt.IsDefault && rt.UserId == userId)
                    : await _context.GameReplayTypes.FirstOrDefaultAsync(rt => rt.Name.ToLower() == replayTypeName.ToLower() && rt.UserId == userId);
                if (replayType == null) { results.errors.Add($"Replay: ReplayType '{replayTypeName}' not found for game '{record.Name}'"); continue; }

                GameReplay? existingReplay = null;
                if (!string.IsNullOrWhiteSpace(record.Started))
                {
                    existingReplay = await _context.GameReplays.FirstOrDefaultAsync(r =>
                        r.GameId == game.Id && r.ReplayTypeId == replayType.Id &&
                        r.Started == record.Started && r.UserId == userId);
                }

                if (existingReplay != null)
                {
                    existingReplay.Finished = string.IsNullOrWhiteSpace(record.Finished) ? null : record.Finished;
                    existingReplay.Grade = ParseNullableInt(record.Grade);
                    existingReplay.Notes = string.IsNullOrWhiteSpace(record.Comment) ? null : record.Comment;
                    existingReplay.UpdatedAt = DateTime.UtcNow;
                    results = results with { replaysUpdated = results.replaysUpdated + 1 };
                }
                else
                {
                    _context.GameReplays.Add(new GameReplay
                    {
                        UserId = userId,
                        GameId = game.Id,
                        ReplayTypeId = replayType.Id,
                        Started = string.IsNullOrWhiteSpace(record.Started) ? null : record.Started,
                        Finished = string.IsNullOrWhiteSpace(record.Finished) ? null : record.Finished,
                        Grade = ParseNullableInt(record.Grade),
                        Notes = string.IsNullOrWhiteSpace(record.Comment) ? null : record.Comment,
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    });
                    results = results with { replaysImported = results.replaysImported + 1 };
                }
                await _context.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                results.errors.Add($"Error processing replay for '{record.Name}': {ex.Message}");
                _logger.LogError(ex, "Error processing replay for game: {GameName}", record.Name);
                _context.ChangeTracker.Clear();
            }
        }

        // Import History entries
        foreach (var record in historyRecords)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(record.Name)) continue;
                if (!DateTime.TryParse(record.Started, null, System.Globalization.DateTimeStyles.RoundtripKind, out var changedAt))
                    continue;

                var actionType = record.Status ?? "Updated";
                var field = record.HistoryField ?? "";
                var oldValue = string.IsNullOrWhiteSpace(record.HistoryOldValue) ? null : record.HistoryOldValue;
                var newValue = string.IsNullOrWhiteSpace(record.HistoryNewValue) ? null : record.HistoryNewValue;

                var exists = await _context.GameHistoryEntries.AnyAsync(h =>
                    h.GameName == record.Name && h.UserId == userId &&
                    h.ActionType == actionType && h.Field == field &&
                    h.OldValue == oldValue && h.NewValue == newValue &&
                    h.ChangedAt == changedAt);

                if (!exists)
                {
                    var game = await _context.Games.FirstOrDefaultAsync(g => g.Name.ToLower() == record.Name.ToLower() && g.UserId == userId);
                    _context.GameHistoryEntries.Add(new GameHistoryEntry
                    {
                        UserId = userId,
                        GameId = game?.Id,
                        GameName = record.Name,
                        ActionType = actionType,
                        Field = field,
                        OldValue = oldValue,
                        NewValue = newValue,
                        Description = $"{field}: {oldValue} → {newValue}",
                        ChangedAt = changedAt
                    });
                    results = results with { historyImported = results.historyImported + 1 };
                }

                if (results.historyImported % 50 == 0)
                    await _context.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                results.errors.Add($"Error processing history for '{record.Name}': {ex.Message}");
                _logger.LogError(ex, "Error processing history for game: {GameName}", record.Name);
                _context.ChangeTracker.Clear();
            }
        }
        await _context.SaveChangesAsync();

        return new
        {
            message = "Importación completa finalizada (modo MERGE)",
            catalogs = new
            {
                platforms = new { imported = results.platformsImported, updated = results.platformsUpdated },
                statuses = new { imported = results.statusesImported, updated = results.statusesUpdated },
                playWiths = new { imported = results.playWithsImported, updated = results.playWithsUpdated },
                playedStatuses = new { imported = results.playedStatusesImported, updated = results.playedStatusesUpdated },
                replayTypes = new { imported = results.replayTypesImported, updated = results.replayTypesUpdated }
            },
            views = new { imported = results.viewsImported, updated = results.viewsUpdated },
            games = new { imported = results.gamesImported, updated = results.gamesUpdated },
            replays = new { imported = results.replaysImported, updated = results.replaysUpdated },
            history = new { imported = results.historyImported },
            errors = results.errors.Count > 0 ? results.errors : null
        };
    }

    public async Task<byte[]> SelectiveExportGamesAsync(SelectiveExportRequest request, int userId)
    {
        var allRecords = new List<FullExportModel>();

        var games = await _context.Games
            .Where(g => g.UserId == userId && request.GameIds.Contains(g.Id))
            .Include(g => g.Status)
            .Include(g => g.Platform)
            .Include(g => g.GamePlayWiths).ThenInclude(gpw => gpw.PlayWith)
            .Include(g => g.PlayedStatus)
            .OrderBy(g => EF.Functions.Collate(g.Name, "NOCASE"))
            .ToListAsync();

        foreach (var g in games)
        {
            var effectiveConfig = request.PerGameConfig != null && request.PerGameConfig.TryGetValue(g.Id, out var pgCfg)
                ? pgCfg
                : request.GlobalConfig;

            var playWithNames = g.GamePlayWiths != null && g.GamePlayWiths.Any()
                ? string.Join(", ", g.GamePlayWiths.Select(gpw => gpw.PlayWith.Name))
                : "";

            allRecords.Add(new FullExportModel
            {
                Type = "Game",
                Name = g.Name,
                Status = ApplyExportString(g.Status?.Name ?? "", "status", effectiveConfig),
                Platform = ApplyExportString(g.Platform?.Name ?? "", "platform", effectiveConfig),
                PlayWith = ApplyExportString(playWithNames, "playWith", effectiveConfig),
                PlayedStatus = ApplyExportString(g.PlayedStatus?.Name ?? "", "playedStatus", effectiveConfig),
                Released = ApplyExportString(g.Released ?? "", "released", effectiveConfig),
                Started = ApplyExportString(g.Started ?? "", "started", effectiveConfig),
                Finished = ApplyExportString(g.Finished ?? "", "finished", effectiveConfig),
                Score = g.Score?.ToString() ?? "",
                Critic = ApplyExportString(g.Critic?.ToString() ?? "", "critic", effectiveConfig),
                CriticProvider = ApplyExportString(g.CriticProvider ?? "", "criticProvider", effectiveConfig),
                Grade = ApplyExportString(g.Grade?.ToString() ?? "", "grade", effectiveConfig),
                Completion = ApplyExportString(g.Completion?.ToString() ?? "", "completion", effectiveConfig),
                Story = ApplyExportString(g.Story?.ToString() ?? "", "story", effectiveConfig),
                Comment = ApplyExportString(g.Comment ?? "", "comment", effectiveConfig),
                Logo = ApplyExportString(g.Logo ?? "", "logo", effectiveConfig),
                Hero = ApplyExportString(g.Hero ?? "", "hero", effectiveConfig),
                Cover = ApplyExportString(g.Cover ?? "", "cover", effectiveConfig),
                IsCheaperByKey = ApplyExportString(g.IsCheaperByKey?.ToString() ?? "", "isCheaperByKey", effectiveConfig),
                KeyStoreUrl = ApplyExportString(g.KeyStoreUrl ?? "", "keyStoreUrl", effectiveConfig),
                SteamAppId = ApplyExportString(g.SteamAppId?.ToString() ?? "", "steamAppId", effectiveConfig),
                SteamPlaytimeForever = ApplyExportString(g.SteamPlaytimeForever?.ToString() ?? "", "steamPlaytimeForever", effectiveConfig),
                SteamPlaytime2Weeks = ApplyExportString(g.SteamPlaytime2Weeks?.ToString() ?? "", "steamPlaytime2Weeks", effectiveConfig),
                SteamLastSynced = ApplyExportString(g.SteamLastSynced?.ToString("O") ?? "", "steamLastSynced", effectiveConfig),
                ManualPlaytimeMinutes = ApplyExportString(g.ManualPlaytimeMinutes?.ToString() ?? "", "manualPlaytimeMinutes", effectiveConfig),
            });
        }

        using var memoryStream = new MemoryStream();
        using var writer = new StreamWriter(memoryStream, Encoding.UTF8);
        using var csvWriter = new CsvWriter(writer, new CsvConfiguration(CultureInfo.InvariantCulture) { Delimiter = _exportSettings.CsvDelimiter });
        await csvWriter.WriteRecordsAsync(allRecords);
        await writer.FlushAsync();
        return memoryStream.ToArray();
    }

    public async Task<SelectiveImportResult> SelectiveImportGamesAsync(Stream? csvFileStream, string? csvText, string? configJson, int userId)
    {
        var config = new SelectiveImportConfig();
        if (!string.IsNullOrWhiteSpace(configJson))
        {
            var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            config = JsonSerializer.Deserialize<SelectiveImportConfig>(configJson, opts) ?? config;
        }

        List<FullExportModel> gameRows;
        bool hasHeroHeader;
        var csvConfig = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            Delimiter = _exportSettings.CsvDelimiter,
            HeaderValidated = null,
            MissingFieldFound = null
        };

        if (csvFileStream != null)
        {
            using var reader = new StreamReader(csvFileStream, Encoding.UTF8);
            using var csvReader = new CsvReader(reader, csvConfig);
            csvReader.Read();
            csvReader.ReadHeader();
            hasHeroHeader = csvReader.HeaderRecord?.Any(header => string.Equals(header, "Hero", StringComparison.OrdinalIgnoreCase)) == true;
            gameRows = csvReader.GetRecords<FullExportModel>()
                .Where(r => r.Type == "Game" && !string.IsNullOrWhiteSpace(r.Name))
                .ToList();
        }
        else
        {
            using var reader = new StringReader(csvText!);
            using var csvReader = new CsvReader(reader, csvConfig);
            csvReader.Read();
            csvReader.ReadHeader();
            hasHeroHeader = csvReader.HeaderRecord?.Any(header => string.Equals(header, "Hero", StringComparison.OrdinalIgnoreCase)) == true;
            gameRows = csvReader.GetRecords<FullExportModel>()
                .Where(r => r.Type == "Game" && !string.IsNullOrWhiteSpace(r.Name))
                .ToList();
        }

        var notFulfilledStatus = await _context.GameStatuses
            .FirstOrDefaultAsync(s => s.UserId == userId && s.StatusType == SpecialStatusType.NotFulfilled && s.IsDefault);

        var result = new SelectiveImportResult();

        foreach (var record in gameRows)
        {
            try
            {
                var effectiveConfig = config.PerGameConfig != null
                    && config.PerGameConfig.TryGetValue(record.Name, out var pgCfg)
                    ? pgCfg
                    : config.GlobalConfig;

                if (string.Equals(effectiveConfig.Mode, "priceOnly", StringComparison.OrdinalIgnoreCase))
                {
                    var existingPriceGame = await _context.Games
                        .FirstOrDefaultAsync(g => g.Name.ToLower() == record.Name.ToLower() && g.UserId == userId);

                    if (existingPriceGame == null)
                        continue;

                    var resolvedIsCheaperByKeyOnly = ResolveImportString(record.IsCheaperByKey, "isCheaperByKey", effectiveConfig);
                    bool? priceOnlyIsCheaperByKey = null;
                    if (!string.IsNullOrWhiteSpace(resolvedIsCheaperByKeyOnly))
                        priceOnlyIsCheaperByKey = bool.TryParse(resolvedIsCheaperByKeyOnly, out var boolVal) ? boolVal : (bool?)null;

                    existingPriceGame.IsCheaperByKey = priceOnlyIsCheaperByKey;
                    existingPriceGame.KeyStoreUrl = ResolveImportString(record.KeyStoreUrl, "keyStoreUrl", effectiveConfig);
                    _context.Entry(existingPriceGame).State = EntityState.Modified;
                    result.Updated++;
                    await _context.SaveChangesAsync();
                    continue;
                }

                var resolvedStatus = ResolveImportString(record.Status, "status", effectiveConfig);
                var resolvedPlatform = ResolveImportString(record.Platform, "platform", effectiveConfig);
                var resolvedPlayWith = ResolveImportString(record.PlayWith, "playWith", effectiveConfig);
                var resolvedPlayedStatus = ResolveImportString(record.PlayedStatus, "playedStatus", effectiveConfig);
                var resolvedReleased = ResolveImportString(record.Released, "released", effectiveConfig);
                var resolvedStarted = ResolveImportString(record.Started, "started", effectiveConfig);
                var resolvedFinished = ResolveImportString(record.Finished, "finished", effectiveConfig);
                var resolvedCritic = ResolveImportString(record.Critic, "critic", effectiveConfig);
                var resolvedCriticProvider = ResolveImportString(record.CriticProvider, "criticProvider", effectiveConfig);
                var resolvedGrade = ResolveImportString(record.Grade, "grade", effectiveConfig);
                var resolvedCompletion = ResolveImportString(record.Completion, "completion", effectiveConfig);
                var resolvedStory = ResolveImportString(record.Story, "story", effectiveConfig);
                var resolvedComment = ResolveImportString(record.Comment, "comment", effectiveConfig);
                var resolvedLogo = ResolveImportString(record.Logo, "logo", effectiveConfig);
                var resolvedHero = ResolveImportString(record.Hero, "hero", effectiveConfig);
                var resolvedCover = ResolveImportString(record.Cover, "cover", effectiveConfig);
                var normalizedHero = ResolveLegacyHero(resolvedHero, resolvedCover, hasHeroHeader);
                var normalizedCover = ResolveLegacyCover(resolvedHero, resolvedCover, hasHeroHeader);
                var resolvedIsCheaperByKey = ResolveImportString(record.IsCheaperByKey, "isCheaperByKey", effectiveConfig);
                var resolvedKeyStoreUrl = ResolveImportString(record.KeyStoreUrl, "keyStoreUrl", effectiveConfig);
                var resolvedSteamAppId = ResolveImportString(record.SteamAppId, "steamAppId", effectiveConfig);
                var resolvedSteamPlaytimeForever = ResolveImportString(record.SteamPlaytimeForever, "steamPlaytimeForever", effectiveConfig);
                var resolvedSteamPlaytime2Weeks = ResolveImportString(record.SteamPlaytime2Weeks, "steamPlaytime2Weeks", effectiveConfig);
                var resolvedSteamLastSynced = ResolveImportString(record.SteamLastSynced, "steamLastSynced", effectiveConfig);
                var resolvedManualPlaytimeMinutes = ResolveImportString(record.ManualPlaytimeMinutes, "manualPlaytimeMinutes", effectiveConfig);

                var status = string.IsNullOrWhiteSpace(resolvedStatus)
                    ? notFulfilledStatus
                    : await _context.GameStatuses.FirstOrDefaultAsync(s => s.Name.ToLower() == resolvedStatus.ToLower() && s.UserId == userId)
                      ?? notFulfilledStatus;

                if (status == null)
                {
                    result.Errors.Add($"No status found and no Not Fulfilled fallback for '{record.Name}'");
                    continue;
                }

                var platform = string.IsNullOrWhiteSpace(resolvedPlatform)
                    ? null
                    : await _context.GamePlatforms.FirstOrDefaultAsync(p => p.Name.ToLower() == resolvedPlatform.ToLower() && p.UserId == userId);

                var playedStatus = string.IsNullOrWhiteSpace(resolvedPlayedStatus)
                    ? null
                    : await _context.GamePlayedStatuses.FirstOrDefaultAsync(p => p.Name.ToLower() == resolvedPlayedStatus.ToLower() && p.UserId == userId);

                var playWithIds = new List<int>();
                if (!string.IsNullOrWhiteSpace(resolvedPlayWith))
                {
                    var pwNames = resolvedPlayWith.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                    foreach (var pwName in pwNames)
                    {
                        var pw = await _context.GamePlayWiths.FirstOrDefaultAsync(p => p.Name.ToLower() == pwName.ToLower() && p.UserId == userId);
                        if (pw != null) playWithIds.Add(pw.Id);
                    }
                }

                bool? isCheaperByKey = null;
                if (!string.IsNullOrWhiteSpace(resolvedIsCheaperByKey))
                    isCheaperByKey = bool.TryParse(resolvedIsCheaperByKey, out var boolVal) ? boolVal : (bool?)null;

                var existing = await _context.Games
                    .Include(g => g.GamePlayWiths)
                    .FirstOrDefaultAsync(g => g.Name.ToLower() == record.Name.ToLower() && g.UserId == userId);

                if (existing != null)
                {
                    existing.StatusId = status.Id;
                    existing.PlatformId = platform?.Id;
                    existing.PlayedStatusId = playedStatus?.Id;
                    existing.Released = resolvedReleased;
                    existing.Started = resolvedStarted;
                    existing.Finished = resolvedFinished;
                    existing.Critic = ParseNullableInt(resolvedCritic);
                    existing.CriticProvider = resolvedCriticProvider;
                    existing.Grade = ParseNullableInt(resolvedGrade);
                    existing.Completion = ParseNullableInt(resolvedCompletion);
                    existing.Story = ParseNullableInt(resolvedStory);
                    existing.Comment = resolvedComment;
                    existing.Logo = resolvedLogo;
                    existing.Hero = normalizedHero;
                    existing.Cover = normalizedCover;
                    existing.IsCheaperByKey = isCheaperByKey;
                    existing.KeyStoreUrl = resolvedKeyStoreUrl;
                    existing.SteamAppId = ParseNullableInt(resolvedSteamAppId);
                    existing.SteamPlaytimeForever = ParseNullableInt(resolvedSteamPlaytimeForever);
                    existing.SteamPlaytime2Weeks = ParseNullableInt(resolvedSteamPlaytime2Weeks);
                    existing.SteamLastSynced = ParseNullableDateTime(resolvedSteamLastSynced);
                    existing.ManualPlaytimeMinutes = ParseNullableInt(resolvedManualPlaytimeMinutes);
                    existing.CalculateScore();

                    foreach (var mapping in existing.GamePlayWiths.ToList())
                        _context.GamePlayWithMappings.Remove(mapping);
                    foreach (var pwId in playWithIds)
                        _context.GamePlayWithMappings.Add(new GamePlayWithMapping { GameId = existing.Id, PlayWithId = pwId });

                    _context.Entry(existing).State = EntityState.Modified;
                    result.Updated++;
                }
                else
                {
                    var newGame = new Game
                    {
                        UserId = userId,
                        Name = record.Name,
                        StatusId = status.Id,
                        PlatformId = platform?.Id,
                        PlayedStatusId = playedStatus?.Id,
                        Released = resolvedReleased,
                        Started = resolvedStarted,
                        Finished = resolvedFinished,
                        Critic = ParseNullableInt(resolvedCritic),
                        CriticProvider = resolvedCriticProvider,
                        Grade = ParseNullableInt(resolvedGrade),
                        Completion = ParseNullableInt(resolvedCompletion),
                        Story = ParseNullableInt(resolvedStory),
                        Comment = resolvedComment,
                        Logo = resolvedLogo,
                        Hero = normalizedHero,
                        Cover = normalizedCover,
                        IsCheaperByKey = isCheaperByKey,
                        KeyStoreUrl = resolvedKeyStoreUrl,
                        SteamAppId = ParseNullableInt(resolvedSteamAppId),
                        SteamPlaytimeForever = ParseNullableInt(resolvedSteamPlaytimeForever),
                        SteamPlaytime2Weeks = ParseNullableInt(resolvedSteamPlaytime2Weeks),
                        SteamLastSynced = ParseNullableDateTime(resolvedSteamLastSynced),
                        ManualPlaytimeMinutes = ParseNullableInt(resolvedManualPlaytimeMinutes),
                    };
                    newGame.CalculateScore();
                    _context.Games.Add(newGame);
                    await _context.SaveChangesAsync();

                    foreach (var pwId in playWithIds)
                        _context.GamePlayWithMappings.Add(new GamePlayWithMapping { GameId = newGame.Id, PlayWithId = pwId });

                    result.Imported++;
                }

                await _context.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                result.Errors.Add($"Error processing '{record.Name}': {ex.Message}");
                _logger.LogError(ex, "Error in selective import for game: {GameName}", record.Name);
                _context.ChangeTracker.Clear();
            }
        }

        result.Message = $"Selective import complete. Imported: {result.Imported}, Updated: {result.Updated}, Errors: {result.Errors.Count}";
        return result;
    }

    public void ClearImageCache(string networkSyncPath)
    {
        var cacheDir = Path.Combine(Path.GetFullPath(networkSyncPath), "_proxy_cache");
        if (!Directory.Exists(cacheDir))
            return;

        foreach (var file in Directory.EnumerateFiles(cacheDir, "*.webp", SearchOption.AllDirectories))
        {
            try { File.Delete(file); }
            catch { /* ignore locked files */ }
        }
    }

    private static string? ApplyExportString(string? storedValue, string propertyKey, GameExportConfig config)
    {
        if (config.Mode == "simple") return storedValue;

        if (config.Mode == "customCleared")
        {
            return GameExportConfig.CustomClearedFields.Contains(propertyKey) ? null : storedValue;
        }

        if (config.Properties == null) return storedValue;
        if (config.Properties.TryGetValue(propertyKey, out var propOverride) && propOverride.Mode == "clean")
            return null;
        return storedValue;
    }

    private static string? ResolveImportString(string? csvValue, string propertyKey, GameImportConfig config)
    {
        if (config.Mode == "simple") return csvValue;

        if (config.Mode == "customCleared")
        {
            return GameImportConfig.CustomClearedFields.Contains(propertyKey) ? null : csvValue;
        }

        if (config.Properties == null) return csvValue;
        if (!config.Properties.TryGetValue(propertyKey, out var propOverride)) return csvValue;
        return propOverride.Mode switch
        {
            "clean" => null,
            "custom" => propOverride.CustomValue,
            _ => csvValue,
        };
    }

    private static string? ResolveLegacyHero(string? hero, string? cover, bool hasHeroHeader)
    {
        if (hasHeroHeader) return string.IsNullOrWhiteSpace(hero) ? null : hero;
        return string.IsNullOrWhiteSpace(hero) ? (string.IsNullOrWhiteSpace(cover) ? null : cover) : hero;
    }

    private static string? ResolveLegacyCover(string? hero, string? cover, bool hasHeroHeader)
    {
        if (hasHeroHeader) return string.IsNullOrWhiteSpace(cover) ? null : cover;
        return string.IsNullOrWhiteSpace(hero) ? null : (string.IsNullOrWhiteSpace(cover) ? null : cover);
    }

    private static int? ParseNullableInt(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        return int.TryParse(value, out var result) ? result : null;
    }

    private static DateTime? ParseNullableDateTime(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        return DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var result) ? result : null;
    }
}
