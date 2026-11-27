using System.Net;
using System.Net.Sockets;
using GamesDatabase.Api.Configuration;

namespace GamesDatabase.Api.Common;

/// <summary>Fail-closed egress policy, checked again against the actual socket address.</summary>
public sealed class ExportMediaDestinationPolicy(ExportMediaOptions options)
{
    public bool IsAllowedUri(Uri uri)
    {
        if (!uri.IsAbsoluteUri || uri.Scheme is not ("http" or "https")
            || !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Fragment)
            || uri.OriginalString.Length > 8192) return false;
        return !IPAddress.TryParse(uri.IdnHost.Trim('[', ']'), out var address) || IsAllowedAddress(uri, address);
    }

    public bool IsAllowedAddress(Uri uri, IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        return IsPublic(address) || IsPrivateNetwork(address) && IsConfiguredPrivateResource(uri);
    }

    private bool IsConfiguredPrivateResource(Uri uri)
    {
        // Avoid encoded separators/double decoding weakening the configured path scope.
        var path = Uri.UnescapeDataString(uri.AbsolutePath);
        if (path.Contains('\\') || path.Contains('%') || path.Split('/').Any(part => part is "." or "..")) return false;
        return (options.AllowedPrivateBaseUrls ?? []).Any(value =>
        {
            if (!Uri.TryCreate(value, UriKind.Absolute, out var allowed) || allowed.Scheme is not ("http" or "https")
                || !string.IsNullOrEmpty(allowed.UserInfo) || !string.IsNullOrEmpty(allowed.Query) || !string.IsNullOrEmpty(allowed.Fragment)) return false;
            var prefix = Uri.UnescapeDataString(allowed.AbsolutePath).TrimEnd('/') + "/";
            // A whole-origin exception would permit /admin and other internal services.
            if (prefix == "/" || prefix.Contains('\\') || prefix.Contains('%') || prefix.Split('/').Any(part => part is "." or "..")) return false;
            return uri.Scheme.Equals(allowed.Scheme, StringComparison.OrdinalIgnoreCase)
                && uri.IdnHost.Equals(allowed.IdnHost, StringComparison.OrdinalIgnoreCase) && uri.Port == allowed.Port
                && path.StartsWith(prefix, StringComparison.Ordinal);
        });
    }

    private static bool IsPrivateNetwork(IPAddress address)
    {
        var bytes = address.GetAddressBytes();
        return address.AddressFamily == AddressFamily.InterNetwork
            ? bytes[0] == 10 || bytes[0] == 172 && bytes[1] is >= 16 and <= 31 || bytes[0] == 192 && bytes[1] == 168
            : address.AddressFamily == AddressFamily.InterNetworkV6 && (bytes[0] & 0xfe) == 0xfc;
    }

    private static bool IsPublic(IPAddress address)
    {
        var bytes = address.GetAddressBytes();
        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            return !IsPrivateNetwork(address) && bytes[0] is not (0 or 127) && bytes[0] < 224
                && !(bytes[0] == 100 && bytes[1] is >= 64 and <= 127)
                && !(bytes[0] == 169 && bytes[1] == 254)
                && !(bytes[0] == 192 && bytes[1] == 0 && bytes[2] is 0 or 2)
                && !(bytes[0] == 192 && bytes[1] == 88 && bytes[2] == 99)
                && !(bytes[0] == 198 && bytes[1] is 18 or 19)
                && !(bytes[0] == 198 && bytes[1] == 51 && bytes[2] == 100)
                && !(bytes[0] == 203 && bytes[1] == 0 && bytes[2] == 113);
        }
        // Only ordinary global-unicast IPv6. Exclude transition/documentation and
        // special-purpose ranges which can conceal embedded/local IPv4 targets.
        return address.AddressFamily == AddressFamily.InterNetworkV6 && address.ScopeId == 0
            && (bytes[0] & 0xe0) == 0x20
            && !(bytes[0] == 0x20 && bytes[1] == 0x02)
            && !(bytes[0] == 0x20 && bytes[1] == 0x01 && bytes[2] <= 0x01)
            && !(bytes[0] == 0x20 && bytes[1] == 0x01 && bytes[2] == 0x0d && bytes[3] == 0xb8);
    }
}
