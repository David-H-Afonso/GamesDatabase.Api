using GamesDatabase.Api.Application.Interfaces;
using System.Collections.Concurrent;
using System.Text;
using System.Web;

namespace GamesDatabase.Api.Application.Services.Steam;

public class SteamAuthService : ISteamAuthService
{
    // ── Nonces (OpenID state tokens) ──────────────────────────────────────────
    private static readonly ConcurrentDictionary<Guid, (int? UserId, string Mode, DateTime Expiry)>
        _nonces = new();

    // ── One-time login codes (replace JWT-in-URL anti-pattern) ───────────────
    private static readonly ConcurrentDictionary<Guid, (SteamLoginResult Result, DateTime Expiry)>
        _loginCodes = new();

    private const string SteamOpenIdEndpoint = "https://steamcommunity.com/openid/login";
    private readonly HttpClient _httpClient;
    private readonly ILogger<SteamAuthService> _logger;

    public SteamAuthService(IHttpClientFactory httpClientFactory, ILogger<SteamAuthService> logger)
    {
        _httpClient = httpClientFactory.CreateClient();
        _logger = logger;
    }

    // ── Nonce helpers ─────────────────────────────────────────────────────────

    public Guid StoreNonce(int? userId, string mode)
    {
        CleanExpiredNonces();
        var nonce = Guid.NewGuid();
        _nonces[nonce] = (userId, mode, DateTime.UtcNow.AddMinutes(15));
        return nonce;
    }

    public (int? UserId, string Mode)? ConsumeNonce(Guid nonce)
    {
        if (_nonces.TryRemove(nonce, out var entry))
        {
            if (entry.Expiry > DateTime.UtcNow)
                return (entry.UserId, entry.Mode);
        }
        return null;
    }

    // ── One-time login code ───────────────────────────────────────────────────

    public Guid StoreLoginResult(SteamLoginResult result)
    {
        CleanExpiredLoginCodes();
        var code = Guid.NewGuid();
        _loginCodes[code] = (result, DateTime.UtcNow.AddMinutes(5));
        return code;
    }

    public SteamLoginResult? ConsumeLoginResult(Guid code)
    {
        if (_loginCodes.TryRemove(code, out var entry))
        {
            if (entry.Expiry > DateTime.UtcNow)
                return entry.Result;
        }
        return null;
    }

    // ── OpenID helpers ────────────────────────────────────────────────────────

    public string BuildLoginUrl(Guid nonce, string callbackUrl)
    {
        var sep = callbackUrl.Contains('?') ? '&' : '?';
        var returnTo = $"{callbackUrl}{sep}nonce={nonce}";
        var realm = ExtractRealm(callbackUrl);

        var sb = new StringBuilder(SteamOpenIdEndpoint);
        sb.Append("?openid.ns=").Append(Uri.EscapeDataString("http://specs.openid.net/auth/2.0"));
        sb.Append("&openid.mode=checkid_setup");
        sb.Append("&openid.return_to=").Append(Uri.EscapeDataString(returnTo));
        sb.Append("&openid.realm=").Append(Uri.EscapeDataString(realm));
        sb.Append("&openid.identity=").Append(Uri.EscapeDataString("http://specs.openid.net/auth/2.0/identifier_select"));
        sb.Append("&openid.claimed_id=").Append(Uri.EscapeDataString("http://specs.openid.net/auth/2.0/identifier_select"));

        return sb.ToString();
    }

    public async Task<string?> ValidateCallbackAsync(IQueryCollection queryParams)
    {
        var postParams = new Dictionary<string, string>();
        foreach (var key in queryParams.Keys)
        {
            if (key.StartsWith("openid."))
                postParams[key] = queryParams[key].ToString();
        }

        if (!postParams.ContainsKey("openid.mode"))
            return null;

        postParams["openid.mode"] = "check_authentication";

        var content = new FormUrlEncodedContent(postParams);
        try
        {
            var response = await _httpClient.PostAsync(SteamOpenIdEndpoint, content);
            var body = await response.Content.ReadAsStringAsync();

            if (!body.Contains("is_valid:true"))
            {
                _logger.LogWarning("Steam OpenID validation returned invalid. Body: {Body}", body);
                return null;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating Steam OpenID callback");
            return null;
        }

        if (queryParams.TryGetValue("openid.claimed_id", out var claimedId))
        {
            var claimedIdStr = claimedId.ToString();
            const string prefix = "https://steamcommunity.com/openid/id/";
            if (claimedIdStr.StartsWith(prefix))
                return claimedIdStr[prefix.Length..];
        }

        return null;
    }

    // ── Private ───────────────────────────────────────────────────────────────

    private static string ExtractRealm(string callbackUrl)
    {
        var uri = new Uri(callbackUrl);
        return $"{uri.Scheme}://{uri.Authority}";
    }

    private static void CleanExpiredNonces()
    {
        var now = DateTime.UtcNow;
        foreach (var key in _nonces.Where(kv => kv.Value.Expiry <= now).Select(kv => kv.Key).ToList())
            _nonces.TryRemove(key, out _);
    }

    private static void CleanExpiredLoginCodes()
    {
        var now = DateTime.UtcNow;
        foreach (var key in _loginCodes.Where(kv => kv.Value.Expiry <= now).Select(kv => kv.Key).ToList())
            _loginCodes.TryRemove(key, out _);
    }
}
