namespace GamesDatabase.Api.Configuration;

public class SteamSettings
{
    public const string SectionName = "SteamSettings";

    public string ApiKey { get; set; } = string.Empty;
    public string ApiBaseUrl { get; set; } = "https://api.steampowered.com";
    public string StoreApiBaseUrl { get; set; } = "https://store.steampowered.com";
    public string CallbackBaseUrl { get; set; } = "http://localhost:8080";
    public string FrontendBaseUrl { get; set; } = "http://localhost:8088";
    public int AppCacheTtlDays { get; set; } = 7;
}
