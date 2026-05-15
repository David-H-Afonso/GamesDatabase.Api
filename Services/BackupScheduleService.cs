using System.Globalization;
using System.Text;
using CsvHelper;
using CsvHelper.Configuration;
using Microsoft.EntityFrameworkCore;
using GamesDatabase.Api.Controllers;
using GamesDatabase.Api.Data;
using GamesDatabase.Api.Models;

namespace GamesDatabase.Api.Services;

/// <summary>
/// Background service that checks every minute whether a scheduled backup is due
/// and executes it. Configuration is read from the BackupSchedules table at runtime,
/// so changes made through the admin panel take effect on the next minute tick.
/// </summary>
public class BackupScheduleService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<BackupScheduleService> _logger;

    public BackupScheduleService(IServiceScopeFactory scopeFactory, ILogger<BackupScheduleService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("BackupScheduleService started");

        // Align to the next full minute, then tick every 60 s
        var now = DateTime.UtcNow;
        var msUntilNextMinute = (60 - now.Second) * 1000 - now.Millisecond;
        await Task.Delay(msUntilNextMinute, stoppingToken);

        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        do
        {
            try
            {
                await CheckAndRunBackupsAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error in BackupScheduleService tick");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task CheckAndRunBackupsAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GamesDbContext>();

        var now = DateTime.UtcNow;
        var schedules = await db.BackupSchedules
            .Where(s => s.IsEnabled)
            .ToListAsync(ct);

        foreach (var schedule in schedules)
        {
            if (schedule.BackupHour != now.Hour || schedule.BackupMinute != now.Minute)
                continue;

            // Skip if already ran within the last 2 minutes (prevents double-fire on timer drift
            // within the same minute tick, while allowing manual runs not to block auto-backups)
            if (schedule.LastRunAt.HasValue &&
                (now - schedule.LastRunAt.Value).TotalMinutes < 2)
            {
                _logger.LogDebug(
                    "Skipping scheduled backup for user {UserId}: ran {MinutesAgo:F1} min ago",
                    schedule.UserId, (now - schedule.LastRunAt.Value).TotalMinutes);
                continue;
            }

            _logger.LogInformation(
                "Running scheduled backup for user {UserId}: type={Type} dest={Dest}",
                schedule.UserId, schedule.BackupType, schedule.DestinationPath);

            await RunBackupNowAsync(schedule, db, ct);
        }
    }

    /// <summary>Public entry point so the controller can trigger an immediate manual backup.</summary>
    public async Task RunBackupNowAsync(BackupSchedule schedule, GamesDbContext db, CancellationToken ct)
    {
        await RunBackupAsync(db, schedule, DateTime.UtcNow, ct);
    }

    private async Task RunBackupAsync(GamesDbContext db, BackupSchedule schedule, DateTime now, CancellationToken ct)
    {
        // Mark as running
        schedule.LastRunAt = now;
        schedule.LastRunStatus = "running";
        schedule.LastRunMessage = null;
        await db.SaveChangesAsync(ct);

        try
        {
            // Ensure destination directory exists
            var dest = schedule.DestinationPath.Trim();
            if (!Directory.Exists(dest))
                Directory.CreateDirectory(dest);

            var userId = schedule.UserId;
            var allRecords = new List<FullExportModel>();

            // ── Catalogs ────────────────────────────────────────────────────────
            var platforms = await db.GamePlatforms.Where(p => p.UserId == userId).OrderBy(p => p.SortOrder).ToListAsync(ct);
            foreach (var p in platforms)
                allRecords.Add(new FullExportModel { Type = "Platform", Name = p.Name, Color = p.Color, Logo = p.Logo ?? "", IsActive = p.IsActive.ToString(), SortOrder = p.SortOrder.ToString() });

            var statuses = await db.GameStatuses.Where(s => s.UserId == userId).OrderBy(s => s.SortOrder).ToListAsync(ct);
            foreach (var s in statuses)
                allRecords.Add(new FullExportModel { Type = "Status", Name = s.Name, Color = s.Color, IsActive = s.IsActive.ToString(), SortOrder = s.SortOrder.ToString(), IsDefault = s.IsDefault.ToString(), StatusType = s.StatusType.ToString() });

            var playWiths = await db.GamePlayWiths.Where(p => p.UserId == userId).OrderBy(p => p.SortOrder).ToListAsync(ct);
            foreach (var p in playWiths)
                allRecords.Add(new FullExportModel { Type = "PlayWith", Name = p.Name, Color = p.Color, IsActive = p.IsActive.ToString(), SortOrder = p.SortOrder.ToString() });

            var playedStatuses = await db.GamePlayedStatuses.Where(p => p.UserId == userId).OrderBy(p => p.SortOrder).ToListAsync(ct);
            foreach (var p in playedStatuses)
                allRecords.Add(new FullExportModel { Type = "PlayedStatus", Name = p.Name, Color = p.Color, IsActive = p.IsActive.ToString(), SortOrder = p.SortOrder.ToString() });

            var views = await db.GameViews.Where(v => v.UserId == userId).OrderBy(v => v.Name).ToListAsync(ct);
            foreach (var v in views)
                allRecords.Add(new FullExportModel { Type = "View", Name = v.Name, Description = v.Description ?? "", FiltersJson = v.FiltersJson, SortingJson = v.SortingJson ?? "", IsPublic = v.IsPublic.ToString(), CreatedBy = v.CreatedBy ?? "" });

            // ── Games (partial = only ModifiedSinceExport, full = all) ──────────
            var gamesQuery = db.Games
                .Where(g => g.UserId == userId)
                .Include(g => g.Status)
                .Include(g => g.Platform)
                .Include(g => g.GamePlayWiths).ThenInclude(gpw => gpw.PlayWith)
                .Include(g => g.PlayedStatus)
                .OrderBy(g => g.Name);

            if (schedule.BackupType == "partial")
                gamesQuery = (IOrderedQueryable<Game>)gamesQuery.Where(g => g.ModifiedSinceExport == true);

            var games = await gamesQuery.ToListAsync(ct);
            foreach (var g in games)
            {
                var playWithNames = g.GamePlayWiths?.Any() == true
                    ? string.Join(", ", g.GamePlayWiths.Select(gpw => gpw.PlayWith.Name))
                    : "";
                allRecords.Add(new FullExportModel
                {
                    Type = "Game",
                    Name = g.Name,
                    Status = g.Status?.Name ?? "",
                    Platform = g.Platform?.Name ?? "",
                    PlayWith = playWithNames,
                    PlayedStatus = g.PlayedStatus?.Name ?? "",
                    Released = g.Released ?? "",
                    Started = g.Started ?? "",
                    Finished = g.Finished ?? "",
                    Score = g.Score?.ToString() ?? "",
                    Critic = g.Critic?.ToString() ?? "",
                    CriticProvider = g.CriticProvider ?? "",
                    Grade = g.Grade?.ToString() ?? "",
                    Completion = g.Completion?.ToString() ?? "",
                    Story = g.Story?.ToString() ?? "",
                    Comment = g.Comment ?? "",
                    Logo = g.Logo ?? "",
                    Cover = g.Cover ?? "",
                    IsCheaperByKey = g.IsCheaperByKey?.ToString() ?? "",
                    KeyStoreUrl = g.KeyStoreUrl ?? "",
                    SteamAppId = g.SteamAppId?.ToString() ?? "",
                    SteamPlaytimeForever = g.SteamPlaytimeForever?.ToString() ?? "",
                    SteamPlaytime2Weeks = g.SteamPlaytime2Weeks?.ToString() ?? "",
                    SteamLastSynced = g.SteamLastSynced?.ToString("O") ?? "",
                    ManualPlaytimeMinutes = g.ManualPlaytimeMinutes?.ToString() ?? ""
                });
            }

            // ── ReplayTypes ──────────────────────────────────────────────────────
            var replayTypes = await db.GameReplayTypes.Where(r => r.UserId == userId).OrderBy(r => r.SortOrder).ToListAsync(ct);
            foreach (var rt in replayTypes)
                allRecords.Add(new FullExportModel { Type = "ReplayType", Name = rt.Name, Color = rt.Color, IsActive = rt.IsActive.ToString(), SortOrder = rt.SortOrder.ToString(), IsDefault = rt.IsDefault.ToString(), StatusType = rt.ReplayType.ToString() });

            // ── Replays ──────────────────────────────────────────────────────────
            var replays = await db.GameReplays
                .Where(r => r.UserId == userId)
                .Include(r => r.Game)
                .Include(r => r.ReplayType)
                .OrderBy(r => r.Game.Name).ThenBy(r => r.CreatedAt)
                .ToListAsync(ct);
            foreach (var r in replays)
                allRecords.Add(new FullExportModel { Type = "Replay", Name = r.Game?.Name ?? "", Status = r.ReplayType?.Name ?? "", Started = r.Started ?? "", Finished = r.Finished ?? "", Grade = r.Grade?.ToString() ?? "", Comment = r.Notes ?? "" });

            // ── History ──────────────────────────────────────────────────────────
            var historyEntries = await db.GameHistoryEntries
                .Where(h => h.UserId == userId)
                .OrderBy(h => h.GameName).ThenBy(h => h.ChangedAt)
                .ToListAsync(ct);
            foreach (var h in historyEntries)
                allRecords.Add(new FullExportModel { Type = "History", Name = h.GameName, Status = h.ActionType, Started = h.ChangedAt.ToString("O"), HistoryField = h.Field, HistoryOldValue = h.OldValue ?? "", HistoryNewValue = h.NewValue ?? "" });

            // ── Write CSV ────────────────────────────────────────────────────────
            var date = now.ToString("yyyyMMdd");
            var prefix = string.IsNullOrWhiteSpace(schedule.FileNamePrefix) ? "" : $"{schedule.FileNamePrefix.Trim()}-";
            var suffix = string.IsNullOrWhiteSpace(schedule.FileNameSuffix) ? "" : $"-{schedule.FileNameSuffix.Trim()}";
            var fileName = $"{prefix}{date}-{schedule.BackupType}{suffix}.csv";
            var filePath = Path.Combine(dest, fileName);

            using (var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var writer = new StreamWriter(fileStream, Encoding.UTF8))
            using (var csv = new CsvWriter(writer, new CsvConfiguration(CultureInfo.InvariantCulture)))
            {
                await csv.WriteRecordsAsync(allRecords, ct);
            }

            _logger.LogInformation("Backup written to {FilePath} ({Count} records)", filePath, allRecords.Count);

            // ── Retention: delete oldest files ───────────────────────────────────
            if (schedule.RetentionCount > 0)
            {
                var existing = Directory.GetFiles(dest, "*.csv")
                    .OrderByDescending(f => f)
                    .Skip(schedule.RetentionCount)
                    .ToList();
                foreach (var old in existing)
                {
                    try { File.Delete(old); }
                    catch (Exception ex) { _logger.LogWarning(ex, "Could not delete old backup file {File}", old); }
                }
            }

            schedule.LastRunStatus = "success";
            schedule.LastRunMessage = $"Wrote {allRecords.Count} records to {fileName}";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Backup failed for user {UserId}", schedule.UserId);
            schedule.LastRunStatus = "failed";
            schedule.LastRunMessage = ex.Message;
        }
        finally
        {
            await db.SaveChangesAsync(ct);
        }
    }
}
