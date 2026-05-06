using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using GamesDatabase.Api.Data;
using GamesDatabase.Api.Models;
using GamesDatabase.Api.Services;

namespace GamesDatabase.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class BackupScheduleController : BaseApiController
{
    private readonly GamesDbContext _context;
    private readonly ILogger<BackupScheduleController> _logger;
    private readonly BackupScheduleService _backupService;

    public BackupScheduleController(
        GamesDbContext context,
        ILogger<BackupScheduleController> logger,
        BackupScheduleService backupService)
    {
        _context = context;
        _logger = logger;
        _backupService = backupService;
    }

    // ── DTO ──────────────────────────────────────────────────────────────────────

    public record BackupScheduleDto(
        bool IsEnabled,
        int BackupHour,
        int BackupMinute,
        string BackupType,
        string DestinationPath,
        int RetentionCount,
        DateTime? LastRunAt,
        string LastRunStatus,
        string? LastRunMessage
    );

    public record UpdateBackupScheduleRequest(
        bool IsEnabled,
        int BackupHour,
        int BackupMinute,
        string BackupType,
        string DestinationPath,
        int RetentionCount
    );

    // ── GET current config ────────────────────────────────────────────────────

    [HttpGet]
    public async Task<ActionResult<BackupScheduleDto>> GetSchedule()
    {
        RequireAdmin();
        var userId = GetCurrentUserIdOrDefault(1);
        var schedule = await _context.BackupSchedules.FirstOrDefaultAsync(s => s.UserId == userId);
        if (schedule == null)
            return Ok(new BackupScheduleDto(false, 3, 0, "full", "/backups", 7, null, "never", null));

        return Ok(ToDto(schedule));
    }

    // ── PUT (create or replace) ───────────────────────────────────────────────

    [HttpPut]
    public async Task<ActionResult<BackupScheduleDto>> UpdateSchedule([FromBody] UpdateBackupScheduleRequest req)
    {
        RequireAdmin();

        if (req.BackupHour < 0 || req.BackupHour > 23)
            return BadRequest(new { message = "BackupHour must be 0–23" });
        if (req.BackupMinute < 0 || req.BackupMinute > 59)
            return BadRequest(new { message = "BackupMinute must be 0–59" });
        if (req.BackupType is not "full" and not "partial")
            return BadRequest(new { message = "BackupType must be 'full' or 'partial'" });
        if (string.IsNullOrWhiteSpace(req.DestinationPath))
            return BadRequest(new { message = "DestinationPath is required" });
        if (req.RetentionCount < 0)
            return BadRequest(new { message = "RetentionCount must be >= 0" });

        var userId = GetCurrentUserIdOrDefault(1);
        var schedule = await _context.BackupSchedules.FirstOrDefaultAsync(s => s.UserId == userId);

        if (schedule == null)
        {
            schedule = new BackupSchedule { UserId = userId };
            _context.BackupSchedules.Add(schedule);
        }

        schedule.IsEnabled = req.IsEnabled;
        schedule.BackupHour = req.BackupHour;
        schedule.BackupMinute = req.BackupMinute;
        schedule.BackupType = req.BackupType;
        schedule.DestinationPath = req.DestinationPath;
        schedule.RetentionCount = req.RetentionCount;

        await _context.SaveChangesAsync();
        _logger.LogInformation("Backup schedule updated for user {UserId}: enabled={Enabled} at {H:D2}:{M:D2} UTC",
            userId, schedule.IsEnabled, schedule.BackupHour, schedule.BackupMinute);

        return Ok(ToDto(schedule));
    }

    // ── POST run-now (manual trigger) ─────────────────────────────────────────

    [HttpPost("run-now")]
    public async Task<IActionResult> RunNow()
    {
        RequireAdmin();

        var userId = GetCurrentUserIdOrDefault(1);
        var schedule = await _context.BackupSchedules.FirstOrDefaultAsync(s => s.UserId == userId);

        if (schedule == null)
        {
            // Create a default one-shot schedule (not persisted as enabled)
            schedule = new BackupSchedule
            {
                UserId = userId,
                IsEnabled = false,
                BackupHour = 3,
                BackupMinute = 0,
                BackupType = "full",
                DestinationPath = "/backups",
                RetentionCount = 7
            };
            _context.BackupSchedules.Add(schedule);
            await _context.SaveChangesAsync();
        }

        // Trigger backup immediately in background
        _ = Task.Run(async () =>
        {
            try
            {
                // We call the internal method via the service; because it uses its own scope,
                // we must trigger a new scope here.
                using var scope = HttpContext.RequestServices.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<GamesDbContext>();

                // Re-load schedule in new scope
                var freshSchedule = await db.BackupSchedules.FindAsync(schedule.Id);
                if (freshSchedule == null) return;

                // Use reflection-free invocation: call the private RunBackupAsync via an IServiceProvider
                // Instead, we just inline the logic by firing the background service's internal pathway.
                // The cleanest approach: make RunBackupAsync internal and call it, or duplicate here.
                // We'll use the dedicated service method exposed as public.
                await _backupService.RunBackupNowAsync(freshSchedule, db, CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in background run-now for user {UserId}", userId);
            }
        });

        return Accepted(new { message = "Backup started in background" });
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static BackupScheduleDto ToDto(BackupSchedule s) => new(
        s.IsEnabled, s.BackupHour, s.BackupMinute, s.BackupType,
        s.DestinationPath, s.RetentionCount, s.LastRunAt, s.LastRunStatus, s.LastRunMessage);

    private void RequireAdmin()
    {
        var role = User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value;
        if (role != "Admin")
            throw new UnauthorizedAccessException("Admin role required");
    }
}
