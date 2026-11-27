using GamesDatabase.Api.Application.Interfaces;
using System.Collections.Concurrent;
using System.Text;

namespace GamesDatabase.Api.Application.Services.Steam;

public class SteamAuthService : ISteamAuthService
{
    // ── Nonces (OpenID state tokens) ──────────────────────────────────────────
    private static readonly ConcurrentDictionary<Guid, (SteamAuthNonce State, DateTime Expiry)>
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

    public Guid StoreNonce(int? userId, string mode, string frontendBaseUrl, string callbackUrl)
    {
        CleanExpiredNonces();
        var nonce = Guid.NewGuid();
        var sep = callbackUrl.Contains('?') ? '&' : '?';
        var returnTo = $"{callbackUrl}{sep}nonce={nonce}";
        _nonces[nonce] = (new(userId, mode, frontendBaseUrl, returnTo), DateTime.UtcNow.AddMinutes(15));
        return nonce;
    }

    public SteamAuthNonce? ConsumeNonce(Guid nonce)
    {
        if (_nonces.TryRemove(nonce, out var entry))
        {
            if (entry.Expiry > DateTime.UtcNow)
                return entry.State;
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

    public string BuildLoginUrl(Guid nonce)
    {
        if (!_nonces.TryGetValue(nonce, out var entry) || entry.Expiry <= DateTime.UtcNow)
            throw new InvalidOperationException("Steam login state is unavailable.");
        var returnTo = entry.State.ReturnTo;
        var realm = ExtractRealm(returnTo);

        var sb = new StringBuilder(SteamOpenIdEndpoint);
        sb.Append("?openid.ns=").Append(Uri.EscapeDataString("http://specs.openid.net/auth/2.0"));
        sb.Append("&openid.mode=checkid_setup");
        sb.Append("&openid.return_to=").Append(Uri.EscapeDataString(returnTo));
        sb.Append("&openid.realm=").Append(Uri.EscapeDataString(realm));
        sb.Append("&openid.identity=").Append(Uri.EscapeDataString("http://specs.openid.net/auth/2.0/identifier_select"));
        sb.Append("&openid.claimed_id=").Append(Uri.EscapeDataString("http://specs.openid.net/auth/2.0/identifier_select"));

        return sb.ToString();
    }

    public async Task<string?> ValidateCallbackAsync(IQueryCollection queryParams, string expectedReturnTo, CancellationToken cancellationToken = default)
    {
        // The provider validates its signature, not our intended destination. Bind
        // that signed destination to the exact URL saved before leaving this API.
        var claimedId = SingleValue(queryParams, "openid.claimed_id");
        const string prefix = "https://steamcommunity.com/openid/id/";
        if (SingleValue(queryParams, "openid.ns") != "http://specs.openid.net/auth/2.0"
            || SingleValue(queryParams, "openid.mode") != "id_res"
            || SingleValue(queryParams, "openid.op_endpoint") != SteamOpenIdEndpoint
            || SingleValue(queryParams, "openid.return_to") != expectedReturnTo
            || claimedId is null || !claimedId.StartsWith(prefix, StringComparison.Ordinal)
            || claimedId.Length != prefix.Length + 17 || !claimedId[prefix.Length..].All(char.IsAsciiDigit)
            || SingleValue(queryParams, "openid.identity") != claimedId
            || SingleValue(queryParams, "openid.response_nonce") is null
            || SingleValue(queryParams, "openid.assoc_handle") is null
            || SingleValue(queryParams, "openid.sig") is null)
            return null;

        var signed = SingleValue(queryParams, "openid.signed")?.Split(',').ToHashSet(StringComparer.Ordinal);
        if (signed is null || new[] { "op_endpoint", "claimed_id", "identity", "return_to", "response_nonce", "assoc_handle" }.Any(field => !signed.Contains(field)))
            return null;

        var postParams = new Dictionary<string, string>();
        foreach (var key in queryParams.Keys)
        {
            if (!key.StartsWith("openid.", StringComparison.Ordinal)) continue;
            var value = SingleValue(queryParams, key);
            if (value is null) return null;
            postParams[key] = value;
        }

        postParams["openid.mode"] = "check_authentication";

        using var content = new FormUrlEncodedContent(postParams);
        try
        {
            using var response = await _httpClient.PostAsync(SteamOpenIdEndpoint, content, cancellationToken);
            if (!response.IsSuccessStatusCode) return null;
            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!body.Split('\n').Any(line => line.TrimEnd('\r') == "is_valid:true"))
            {
                _logger.LogWarning("Steam OpenID validation returned invalid.");
                return null;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating Steam OpenID callback");
            return null;
        }

        return claimedId[prefix.Length..];
    }

    // ── Private ───────────────────────────────────────────────────────────────

    private static string? SingleValue(IQueryCollection query, string key) =>
        query.TryGetValue(key, out var values) && values.Count == 1 && !string.IsNullOrWhiteSpace(values[0]) ? values[0] : null;

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
