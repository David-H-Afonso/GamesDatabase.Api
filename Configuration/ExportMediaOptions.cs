namespace GamesDatabase.Api.Configuration;

public sealed class ExportMediaOptions
{
    public const string SectionName = "ExportMedia";

    // Administrator-controlled, exact scheme/host/port/path prefixes. Only RFC1918
    // and IPv6 ULA addresses may use this exception; never loopback or link-local.
    public List<string> AllowedPrivateBaseUrls { get; set; } = [];
}
