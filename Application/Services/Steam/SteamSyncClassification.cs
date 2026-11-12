using GamesDatabase.Api.Contracts.Steam;

namespace GamesDatabase.Api.Application.Services.Steam;

public static class SteamSyncClassification
{
    public const int DetailLimit = 1000;

    public static bool IsNotice(string? code) => code is "noPlayerData" or "unreleased";

    public static string Severity(string code) => code switch
    {
        "noPlayerData" or "unreleased" or "noAchievements" => "info",
        "profilePrivate" or "statsUnavailable" or "setsUnavailable" => "warning",
        _ => "error"
    };

    // Keep diagnostics bounded, prioritizing actionable errors over normal skips.
    public static void Retain(List<SteamSyncIssueDto> issues, SteamSyncIssueDto issue)
    {
        if (issues.Count < DetailLimit) { issues.Add(issue); return; }
        if (issue.Severity == "info") return;
        var replace = issues.FindIndex(previous => previous.Severity == "info");
        if (replace < 0 && issue.Severity == "error") replace = issues.FindIndex(previous => previous.Severity == "warning");
        if (replace >= 0) issues[replace] = issue;
    }
}
