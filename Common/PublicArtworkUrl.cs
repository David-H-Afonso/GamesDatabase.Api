using System.Text.RegularExpressions;

namespace GamesDatabase.Api.Common;

public static partial class PublicArtworkUrl
{
    [GeneratedRegex(@"^/(?:thumb/)?[a-f0-9]/[a-f0-9]{2}/[^?#]+\.(?:png|jpe?g|webp)$", RegexOptions.IgnoreCase)]
    private static partial Regex WikimediaPath();

    public static bool Valid(string? value)
    {
        if (value is not { Length: > 0 and <= 2000 } || !Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || uri.Scheme != "https" || !uri.IsDefaultPort || uri.UserInfo.Length != 0 || uri.Fragment.Length != 0) return false;
        if (uri.Query.Length > 0) return Application.Services.PublicGameMetadataProvider.SteamImage(value) && Regex.IsMatch(uri.Query, @"^\?t=[0-9]{1,16}$");
        return uri.Host switch
        {
            "cdn2.steamgriddb.com" or "cdn.steamgriddb.com" => Regex.IsMatch(uri.AbsolutePath, @"^/(?:grid|hero|logo)/[a-f0-9]+\.(?:png|jpe?g|webp)$", RegexOptions.IgnoreCase),
            "assets.nintendo.com" => uri.AbsolutePath.StartsWith("/image/upload/", StringComparison.Ordinal) && !uri.AbsolutePath.Contains("..", StringComparison.Ordinal) && !uri.AbsolutePath.Contains("fetch", StringComparison.OrdinalIgnoreCase),
            "cdn.dekudeals.com" => Regex.IsMatch(uri.AbsolutePath, @"^/images/[a-f0-9]{40}/w[0-9]{2,4}\.(?:jpg|webp)$"),
            "image.api.playstation.com" => Regex.IsMatch(uri.AbsolutePath, @"^/vulcan/ap/rnd/[0-9]{6}/[0-9]{4}/[A-Za-z0-9_-]+\.(?:png|jpe?g|webp)$", RegexOptions.IgnoreCase),
            "images.launchbox-app.com" or "gamesdb-images.launchbox.gg" => Regex.IsMatch(uri.AbsolutePath, @"^/{1,2}(?:r2_)?[a-f0-9]{8}(?:-[a-f0-9]{4}){3}-[a-f0-9]{12}\.(?:png|jpe?g|webp)$", RegexOptions.IgnoreCase),
            "upload.wikimedia.org" => uri.AbsolutePath.StartsWith("/wikipedia/commons/", StringComparison.Ordinal) && WikimediaPath().IsMatch(uri.AbsolutePath[18..]),
            _ => Application.Services.PublicGameMetadataProvider.SteamImage(value)
        };
    }
}
