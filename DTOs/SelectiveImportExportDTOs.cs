namespace GamesDatabase.Api.DTOs;

// ─── Export DTOs ──────────────────────────────────────────────────────────────

public class SelectiveExportRequest
{
    /// <summary>IDs of the games to export.</summary>
    public List<int> GameIds { get; set; } = new();

    /// <summary>Global export configuration applied to all games unless overridden.</summary>
    public GameExportConfig GlobalConfig { get; set; } = new();

    /// <summary>Per-game export overrides keyed by game ID. Overrides GlobalConfig for that game.</summary>
    public Dictionary<int, GameExportConfig>? PerGameConfig { get; set; }
}

public class GameExportConfig
{
    /// <summary>"simple" = all AsStored. "custom" = use Properties.</summary>
    public string Mode { get; set; } = "simple";

    public Dictionary<string, ExportPropertyOverride>? Properties { get; set; }
}

public class ExportPropertyOverride
{
    /// <summary>"asStored" or "clean"</summary>
    public string Mode { get; set; } = "asStored";
}

// ─── Import DTOs ──────────────────────────────────────────────────────────────

public class SelectiveImportConfig
{
    /// <summary>Global import configuration applied to all games unless overridden.</summary>
    public GameImportConfig GlobalConfig { get; set; } = new();

    /// <summary>Per-game import overrides keyed by game name (from CSV). Overrides GlobalConfig for that game.</summary>
    public Dictionary<string, GameImportConfig>? PerGameConfig { get; set; }
}

public class GameImportConfig
{
    /// <summary>"simple" = all AsImported. "custom" = use Properties.</summary>
    public string Mode { get; set; } = "simple";

    public Dictionary<string, ImportPropertyOverride>? Properties { get; set; }
}

public class ImportPropertyOverride
{
    /// <summary>"asImported", "clean", or "custom"</summary>
    public string Mode { get; set; } = "asImported";

    /// <summary>Used when Mode == "custom". String representation of the value (name for entities, ISO for dates, etc.)</summary>
    public string? CustomValue { get; set; }
}

// ─── Result ───────────────────────────────────────────────────────────────────

public class SelectiveImportResult
{
    public string Message { get; set; } = string.Empty;
    public int Imported { get; set; }
    public int Updated { get; set; }
    public List<string> Errors { get; set; } = new();
}
