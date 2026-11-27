using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using GamesDatabase.Api.Application.Interfaces;
using GamesDatabase.Api.Common;
using GamesDatabase.Api.Contracts;
using HtmlAgilityPack;
using Microsoft.Extensions.Caching.Memory;

namespace GamesDatabase.Api.Application.Services;

/// <summary>On-demand public HTML only. No HLTB requests, credentials, redirects or challenge bypass.</summary>
public sealed partial class DekuDealsDurationProvider(IHttpClientFactory clients, IMemoryCache cache, TimeProvider clock) : IGameDurationProvider
{
    private readonly HttpClient client = clients.CreateClient("game-durations");
    private readonly SemaphoreSlim lookupGate = new(1, 1);
    private const string Origin = "https://www.dekudeals.com";
    // Anonymous Deku search otherwise defaults to Switch/Switch 2. Use its
    // public platform preference parameter, not cookies or a guessed item slug.
    private const string Platforms = "switch_2,switch,ps5,ps4,xbox_series,xbox_one,steam";
    private const int MaxBytes = 1_000_000;
    private const string Cooldown = "deku-duration-cooldown";

    [GeneratedRegex("^[a-z0-9]+(?:-[a-z0-9]+)*$")]
    private static partial Regex SlugPattern();
    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
    [GeneratedRegex(@"^https://(?:www\.)?howlongtobeat\.com/(?:game\.php\?id=|game/)[1-9][0-9]*$")]
    private static partial Regex HltbLink();
    [GeneratedRegex(@"^(?<number>[0-9]+(?:\.[0-9]+)?)(?<fraction>[¼½¾])?\s*(?<unit>hours?|hrs?|h|minutes?|mins?|m)$", RegexOptions.IgnoreCase)]
    private static partial Regex DurationPattern();
    [GeneratedRegex(@"^https://www\.metacritic\.com/game/(?:[a-z0-9]+(?:-[a-z0-9]+)*/){1,2}$")]
    private static partial Regex MetacriticLink();
    [GeneratedRegex(@"^https://opencritic\.com/game/[1-9][0-9]*/[a-z0-9]+(?:-[a-z0-9]+)*$")]
    private static partial Regex OpenCriticLink();

    public static bool ValidSlug(string? slug) => slug is { Length: > 0 and <= 160 } && SlugPattern().IsMatch(slug);
    public static bool ValidCriticUrl(string? provider, string? url) => url is { Length: > 0 and <= 600 } && (provider == "Metacritic" ? MetacriticLink().IsMatch(url) : provider == "OpenCritic" && OpenCriticLink().IsMatch(url));
    private static string Text(HtmlNode node) => Whitespace().Replace(HtmlEntity.DeEntitize(node.InnerText), " ").Trim();
    private static bool SameName(string left, string right) => NameSearchMatching.Matches(left, new() { IncludeTerms = [right], MatchType = "exact" });
    private static HtmlDocument Document(string html) { var document = new HtmlDocument(); document.LoadHtml(html); return document; }

    private async Task<(string? Html, bool Failed)> ReadAsync(string path, CancellationToken token)
    {
        if (cache.TryGetValue<DateTimeOffset>(Cooldown, out var until) && until > clock.GetUtcNow()) return (null, true);
        using var response = await client.GetAsync(Origin + path, HttpCompletionOption.ResponseHeadersRead, token);
        if (!response.IsSuccessStatusCode)
        {
            if ((int)response.StatusCode is 403 or 429 or >= 500)
            {
                var delay = response.Headers.RetryAfter?.Delta ?? (response.Headers.RetryAfter?.Date is { } date ? date - clock.GetUtcNow() : TimeSpan.FromMinutes(1));
                delay = TimeSpan.FromSeconds(Math.Clamp(delay.TotalSeconds, 1, 3600));
                cache.Set(Cooldown, clock.GetUtcNow() + delay, delay);
            }
            return (null, response.StatusCode != System.Net.HttpStatusCode.NotFound);
        }
        if (response.RequestMessage?.RequestUri is { } actual && (actual.Scheme != "https" || actual.Host != "www.dekudeals.com" || actual.PathAndQuery != path)) return (null, true);
        if (response.Content.Headers.ContentType?.MediaType is not ("text/html" or "application/xhtml+xml") || response.Content.Headers.ContentLength > MaxBytes) return (null, true);
        await using var stream = await response.Content.ReadAsStreamAsync(token);
        using var buffer = new MemoryStream(); var block = new byte[8192]; int count;
        while ((count = await stream.ReadAsync(block, token)) > 0)
        {
            if (buffer.Length + count > MaxBytes) return (null, true);
            buffer.Write(block, 0, count);
        }
        return (Encoding.UTF8.GetString(buffer.ToArray()), false);
    }

    private static List<GameMetadataIdentityDto> SearchResults(string html)
    {
        var anchors = Document(html).DocumentNode.SelectNodes("//main//a[contains(concat(' ', normalize-space(@class), ' '), ' main-link ')]");
        if (anchors is null) return [];
        var matches = new List<GameMetadataIdentityDto>();
        foreach (var anchor in anchors)
        {
            var href = anchor.GetAttributeValue("href", "");
            if (!href.StartsWith("/items/", StringComparison.Ordinal)) continue;
            var slug = href[7..]; var name = Text(anchor);
            if (!ValidSlug(slug) || name.Length is < 1 or > 250 || matches.Any(match => match.DekuDealsSlug == slug)) continue;
            matches.Add(new() { Source = "dekudeals", DekuDealsSlug = slug, Name = name, Url = Origin + href });
            if (matches.Count == 8) break;
        }
        return matches;
    }

    private static decimal? Hours(string raw)
    {
        var match = DurationPattern().Match(raw);
        if (!match.Success || !decimal.TryParse(match.Groups["number"].Value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var number)) return null;
        if (match.Groups["fraction"].Success)
        {
            if (raw.Contains('.')) return null;
            number += match.Groups["fraction"].Value switch { "¼" => 0.25m, "½" => 0.5m, "¾" => 0.75m, _ => 0m };
        }
        if (match.Groups["unit"].Value.StartsWith("m", StringComparison.OrdinalIgnoreCase)) number /= 60;
        return number is > 0 and <= 100_000 ? number : null;
    }

    private static PublicGameMetadata? ParseGame(string html, GameMetadataIdentityDto identity)
    {
        var document = Document(html);
        var title = document.DocumentNode.SelectSingleNode("//meta[@property='og:title' or @name='og:title']")?.GetAttributeValue("content", "");
        const string suffix = " | Deku Deals";
        if (title is null || !title.EndsWith(suffix, StringComparison.Ordinal) || !SameName(HtmlEntity.DeEntitize(title[..^suffix.Length]), identity.Name)) return null;
        var canonical = document.DocumentNode.SelectSingleNode("//link[@rel='canonical']")?.GetAttributeValue("href", "");
        if (canonical != identity.Url) return null;
        var link = document.DocumentNode.SelectNodes("//main//a[@href]")?.FirstOrDefault(anchor => HltbLink().IsMatch(anchor.GetAttributeValue("href", "")) && Text(anchor) == "How Long To Beat");
        var rows = link?.SelectSingleNode("ancestor::li[1]")?.SelectNodes("./ul/li");
        var result = new PublicGameMetadata { Identity = identity };
        foreach (var (label, field) in new[] { ("Main Story:", "story"), ("Completionist:", "completion") })
        {
            var matching = rows?.Where(row => Text(row.SelectSingleNode("./strong") ?? row) == label).ToList();
            if (matching?.Count != 1) continue;
            var text = Text(matching[0]);
            if (!text.StartsWith(label, StringComparison.Ordinal) || Hours(text[label.Length..].Trim()) is not { } hours) continue;
            // Existing fields are integer hours. Preserve exact public hours in the
            // signed review, and visibly disclose rounding before any explicit save.
            result.Fields[field] = Math.Max(1, (int)decimal.Round(hours, 0, MidpointRounding.AwayFromZero));
            result.SourceHours[field] = hours;
        }
        foreach (var (provider, css) in new[] { ("Metacritic", "metacritic"), ("OpenCritic", "opencritic") })
        {
            var ratingRows = document.DocumentNode.SelectNodes("//main//li[strong]")?.Where(row => Text(row.SelectSingleNode("./strong")!) == provider + ":").ToList();
            if (ratingRows?.Count != 1) continue;
            var anchors = ratingRows[0].SelectNodes($"./a[contains(concat(' ', normalize-space(@class), ' '), ' {css} ')]");
            if (anchors?.Count != 1) continue;
            var anchor = anchors[0]; var url = anchor.GetAttributeValue("href", "");
            if (!ValidCriticUrl(provider, url)) continue;
            // Metacritic's adjacent second badge is a /10 USER score. Never use
            // it, page stars, recommendation percentages or unrelated numbers.
            var scoreNode = provider == "Metacritic" ? anchor.SelectSingleNode("./span[contains(concat(' ', normalize-space(@class), ' '), ' text-white ')]") : anchor;
            if (scoreNode is null || !int.TryParse(Text(scoreNode), NumberStyles.None, CultureInfo.InvariantCulture, out var score) || score is < 0 or > 100) continue;
            result.Fields["critic"] = score; result.CriticProvider = provider; result.CriticUrl = url; result.CriticScope = "listed-platform";
            break;
        }
        return result.Fields.Count == 0 ? null : result;
    }

    public async Task<GameDurationLookup> LookupAsync(string name, string? selectedSlug, CancellationToken cancellationToken, bool forceRefresh = false)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 250 || selectedSlug is not null && !ValidSlug(selectedSlug)) return new();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(10)); var token = deadline.Token;
        await lookupGate.WaitAsync(token);
        try
        {
            // Separate this corrected catalog scope from older Nintendo-only
            // results (including cached misses). The public review stays v2.
            var searchKey = "deku-metadata-v3-all-platforms-search:" + name;
            List<GameMetadataIdentityDto>? matches = null;
            if (forceRefresh || !cache.TryGetValue(searchKey, out matches) || matches is null)
            {
                var read = await ReadAsync("/search?q=" + Uri.EscapeDataString(name) + "&platforms=" + Uri.EscapeDataString(Platforms), token);
                if (read.Html is null) return new() { Unavailable = read.Failed, RetryAfterSeconds = CooldownSeconds() };
                matches = SearchResults(read.Html);
                cache.Set(searchKey, matches, TimeSpan.FromHours(matches.Count == 0 ? 1 : 24));
            }
            var result = new GameDurationLookup { Matches = matches };
            var selected = selectedSlug is null ? MetadataNameMatching.Best(matches, name, match => match.Name) : matches.SingleOrDefault(match => match.DekuDealsSlug == selectedSlug);
            if (selected is null) return result;
            result.IdentityConfirmed = true;
            var gameKey = "deku-metadata-v3-all-platforms-game:" + selected.DekuDealsSlug;
            if (!forceRefresh && cache.TryGetValue<PublicGameMetadata>(gameKey, out var cached) && cached is not null) { result.Metadata = cached; return result; }
            var game = await ReadAsync("/items/" + selected.DekuDealsSlug, token);
            result.Unavailable = game.Failed;
            result.RetryAfterSeconds = CooldownSeconds();
            if (game.Html is null) return result;
            result.Metadata = ParseGame(game.Html, selected);
            if (result.Metadata is not null) cache.Set(gameKey, result.Metadata, TimeSpan.FromHours(24));
            return result;
        }
        finally { lookupGate.Release(); }
    }

    private int? CooldownSeconds() => cache.TryGetValue<DateTimeOffset>(Cooldown, out var until) && until > clock.GetUtcNow()
        ? Math.Clamp((int)Math.Ceiling((until - clock.GetUtcNow()).TotalSeconds), 1, 3600) : null;
}
