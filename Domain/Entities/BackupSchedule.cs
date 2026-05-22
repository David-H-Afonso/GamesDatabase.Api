namespace GamesDatabase.Api.Domain.Entities;

/// <summary>
/// Configuration for scheduled automatic database backups.
/// One row per user; stored in the database so it can be edited at runtime.
/// </summary>
public class BackupSchedule
{
    public int Id { get; set; }

    /// <summary>Owner user. For single-user setups this will always be user 1 (admin).</summary>
    public int UserId { get; set; }

    public bool IsEnabled { get; set; } = false;

    /// <summary>Hour of the day (0–23) in UTC at which the backup should run.</summary>
    public int BackupHour { get; set; } = 3;

    /// <summary>Minute of the hour (0–59) at which the backup should run.</summary>
    public int BackupMinute { get; set; } = 0;

    /// <summary>"full" or "partial". Full = export everything; Partial = only games modified since last export.</summary>
    public string BackupType { get; set; } = "full";

    /// <summary>Absolute path on the host where backup files will be written.</summary>
    public string DestinationPath { get; set; } = "/backups";

    /// <summary>Keep the last N backup files. Older ones are deleted automatically (0 = keep all).</summary>
    public int RetentionCount { get; set; } = 7;

    /// <summary>Optional filename prefix (e.g. "1-admin").</summary>
    public string FileNamePrefix { get; set; } = "";

    /// <summary>Optional filename suffix appended before the extension.</summary>
    public string FileNameSuffix { get; set; } = "";

    // ── Last run info ──────────────────────────────────────────────────────────
    public DateTime? LastRunAt { get; set; }
    /// <summary>"never" | "running" | "success" | "failed"</summary>
    public string LastRunStatus { get; set; } = "never";
    public string? LastRunMessage { get; set; }

    public virtual User User { get; set; } = null!;
}
