using System.Text;
using System.Xml;

namespace GamesDatabase.Api.Common;

/// <summary>Shared by CRUD, CSV import and serving, including values saved by older clients.</summary>
public static class PlatformLogoContent
{
    public const int MaximumLength = 500_000;
    private const int MaximumDecodedBytes = 375_000;
    private const string SvgNamespace = "http://www.w3.org/2000/svg";
    public const string ContentSecurityPolicy = "sandbox; default-src 'none'; script-src 'none'; style-src 'unsafe-inline'; img-src data:; object-src 'none'; base-uri 'none'; form-action 'none'; frame-ancestors 'none'";

    public static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    public static string? Validate(string? value)
    {
        if (value is null) return null;
        if (value.Length > MaximumLength) return "El logo es demasiado grande";
        var logo = Normalize(value);
        if (logo is null || IsSafeReference(logo) || TryReadDataUrl(logo, out _)) return null;
        return "El logo debe ser una URL http(s), un icono de plataforma o una imagen válida PNG, JPG, WebP, AVIF o SVG sin contenido activo";
    }

    public static bool IsSafeReference(string logo)
    {
        if (logo.Length > MaximumLength || logo.Any(char.IsControl)) return false;
        if (Uri.TryCreate(logo, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
            && string.IsNullOrEmpty(uri.UserInfo)) return true;
        // These are bundled raster presets, not an arbitrary redirect target.
        return logo.StartsWith("/platform-icons/", StringComparison.OrdinalIgnoreCase)
            && logo.Length > "/platform-icons/".Length
            && !logo.Contains('\\') && !logo.Contains('%') && !logo.Contains('?') && !logo.Contains('#')
            && logo["/platform-icons/".Length..].All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.')
            && !logo.Contains("..", StringComparison.Ordinal);
    }

    public static bool TryReadDataUrl(string logo, out VerifiedImageContent? image)
    {
        image = null;
        if (logo.Length > MaximumLength || !logo.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase)) return false;
        var comma = logo.IndexOf(',');
        if (comma < 0 || comma > 64) return false;
        var declaration = logo[5..comma].Split(';');
        if (declaration.Length > 2 || declaration.Length == 2 && !declaration[1].Equals("base64", StringComparison.OrdinalIgnoreCase)) return false;
        var mediaType = declaration[0].ToLowerInvariant();
        var isSvg = mediaType == "image/svg+xml";
        if (!isSvg && mediaType is not ("image/png" or "image/jpeg" or "image/jpg" or "image/webp" or "image/avif" or "image/gif" or "image/bmp" or "image/x-icon")) return false;
        try
        {
            byte[] bytes;
            if (declaration.Length == 2)
                bytes = Convert.FromBase64String(logo[(comma + 1)..]);
            else if (isSvg)
            {
                var payload = Uri.UnescapeDataString(logo[(comma + 1)..]);
                // Older API versions served base64 SVGs without a ;base64 marker.
                // Preserve those legitimate records as well as standard URL-encoded SVG.
                bytes = payload.TrimStart().StartsWith('<') ? Encoding.UTF8.GetBytes(payload) : Convert.FromBase64String(payload);
            }
            else return false;
            if (bytes.Length is 0 or > MaximumDecodedBytes) return false;

            if (isSvg)
            {
                if (!IsPassiveSvg(bytes)) return false;
                image = new VerifiedImageContent(bytes, "image/svg+xml", ".svg");
                return true;
            }
            if (!VerifiedImageContent.TryReadRaster(bytes, out var raster, 16_000_000)
                || raster!.MediaType != (mediaType == "image/jpg" ? "image/jpeg" : mediaType)) return false;
            image = raster;
            return true;
        }
        catch (Exception exception) when (exception is FormatException or ArgumentException or XmlException)
        {
            return false;
        }
    }

    private static bool IsPassiveSvg(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes, writable: false);
        using var reader = XmlReader.Create(stream, new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = MaximumDecodedBytes,
            MaxCharactersFromEntities = 0
        });
        var rootSeen = false;
        var elements = 0;
        while (reader.Read())
        {
            if (reader.Depth > 64 || reader.NodeType == XmlNodeType.ProcessingInstruction) return false;
            if (reader.NodeType != XmlNodeType.Element) continue;
            if (++elements > 10_000) return false;
            if (!rootSeen)
            {
                if (reader.LocalName != "svg" || reader.NamespaceURI != SvgNamespace) return false;
                rootSeen = true;
            }
            var name = reader.LocalName.ToLowerInvariant();
            if (name is "script" or "foreignobject" or "iframe" or "object" or "embed" or "audio" or "video" or "set" or "discard"
                || name.StartsWith("animate", StringComparison.Ordinal)) return false;
            while (reader.MoveToNextAttribute())
            {
                if (reader.LocalName.StartsWith("on", StringComparison.OrdinalIgnoreCase)) return false;
                if (reader.LocalName.Equals("href", StringComparison.OrdinalIgnoreCase) && !reader.Value.TrimStart().StartsWith('#')) return false;
                if (reader.LocalName.Equals("base", StringComparison.OrdinalIgnoreCase) && reader.NamespaceURI == "http://www.w3.org/XML/1998/namespace") return false;
            }
            reader.MoveToElement();
        }
        return rootSeen;
    }
}
