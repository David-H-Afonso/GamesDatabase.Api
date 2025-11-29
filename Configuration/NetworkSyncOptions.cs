namespace GamesDatabase.Api.Configuration;

public class NetworkSyncOptions
{
    public const string SectionName = "NetworkSync";

    public bool Enabled { get; set; } = false;
    public string NetworkPath { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}
