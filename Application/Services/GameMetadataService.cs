using System.Security.Cryptography;
using System.Text.Json;
using GamesDatabase.Api.Application.Interfaces;
using GamesDatabase.Api.Common;
using GamesDatabase.Api.Contracts;
using GamesDatabase.Api.Domain.Entities;
using GamesDatabase.Api.Infrastructure.Persistence;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace GamesDatabase.Api.Application.Services;

/// <summary>Explicit metadata preview/apply. Signed plans bind owner, game, fields and the reviewed timestamp.</summary>
public sealed class GameMetadataService(GamesDbContext context, IGameService games, IPublicGameMetadataProvider sources, IGameDurationProvider durations, IDataProtectionProvider protection, TimeProvider clock) : IGameMetadataService
{
    private readonly IDataProtector protector = protection.CreateProtector("GamesDatabase.Metadata.v1");
    private readonly IDataProtector criticProtector = protection.CreateProtector("GamesDatabase.Metadata.v2");
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly HashSet<string> Allowed = ["name", "released", "developer", "critic", "logo", "hero", "cover", "story", "completion"];
    private sealed class Plan
    {
        public int UserId { get; set; }
        public int GameId { get; set; }
        public DateTime UpdatedAt { get; set; }
        public DateTimeOffset ExpiresAt { get; set; }
        public bool Overwrite { get; set; }
        public int ReviewVersion { get; set; } = 1;
        public List<GameMetadataFieldDto> Fields { get; set; } = [];
    }
    private static object? Current(Game game, string field) => field switch
    {
        "name" => game.Name, "released" => game.Released, "developer" => game.Developer, "critic" => game.Critic,
        "logo" => game.Logo, "hero" => game.Hero, "cover" => game.Cover, "story" => game.Story, "completion" => game.Completion, _ => null
    };
    private static bool Empty(object? value) => value is null || value is string text && string.IsNullOrWhiteSpace(text);
    private static bool SameName(string left, string right) => NameSearchMatching.Matches(left, new() { IncludeTerms = [right], MatchType = "exact" });
    private static bool KnownCritic(string? provider) => provider is "Metacritic" or "OpenCritic" or "SteamDB";
    private static bool SameValue(object? current, JsonElement candidate) => JsonSerializer.SerializeToElement(current, Json).GetRawText() == candidate.GetRawText();
    private static bool HasCritic(PublicGameMetadata? metadata) => metadata is not null && metadata.Fields.TryGetValue("critic", out var value) && ValidValue("critic", JsonSerializer.SerializeToElement(value, Json));
    private static bool ValidValue(string field, JsonElement value)
    {
        if (field == "critic") return value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var score) && score is >= 0 and <= 100;
        if (field is "story" or "completion") return value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var hours) && hours is > 0 and <= 100_000;
        if (value.ValueKind != JsonValueKind.String || value.GetString() is not { Length: > 0 } text) return false;
        return field switch
        {
            "name" => text.Length <= 250 && !string.IsNullOrWhiteSpace(text),
            "developer" => text.Length <= 150 && !string.IsNullOrWhiteSpace(text),
            "released" => GameDateNormalizer.NormalizeSteamReleaseDate(text) == text,
            "logo" or "hero" or "cover" => text.Length <= 2000 && PublicGameMetadataProvider.SteamImage(text),
            _ => false
        };
    }

    public async Task<CatalogServiceResult<GameMetadataPreviewDto>> PreviewAsync(int gameId, int userId, GameMetadataLookupRequest request, CancellationToken cancellationToken = default)
    {
        if (request.ReviewVersion is not (1 or 2)) return CatalogServiceResult<GameMetadataPreviewDto>.BadRequest("Invalid metadata review version.");
        if (request.SteamAppId is <= 0 || request.WikidataId is { } id && !System.Text.RegularExpressions.Regex.IsMatch(id, "^Q[1-9][0-9]{0,14}$")) return CatalogServiceResult<GameMetadataPreviewDto>.BadRequest("Invalid metadata identity.");
        if (request.DekuDealsSlug is not null && !DekuDealsDurationProvider.ValidSlug(request.DekuDealsSlug)) return CatalogServiceResult<GameMetadataPreviewDto>.BadRequest("Invalid duration identity.");
        var game = await context.Games.AsNoTracking().SingleOrDefaultAsync(game => game.Id == gameId && game.UserId == userId, cancellationToken);
        if (game is null) return CatalogServiceResult<GameMetadataPreviewDto>.NotFoundResult("Game unavailable.");
        if (game.SteamAppId.HasValue && request.SteamAppId.HasValue && game.SteamAppId != request.SteamAppId) return CatalogServiceResult<GameMetadataPreviewDto>.BadRequest("Change the linked App ID explicitly before using another Steam identity.");
        var result = new GameMetadataPreviewDto { Overwrite = request.Overwrite, ReviewVersion = request.ReviewVersion };
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(25));
        var token = deadline.Token;
        var appId = game.SteamAppId ?? request.SteamAppId;
        PublicGameMetadata? steam = null;
        if (!appId.HasValue && request.WikidataId is null)
        {
            try
            {
                var matches = await sources.SearchSteamAsync(game.Name, token);
                result.Matches.AddRange(matches);
                var exact = matches.Where(match => SameName(game.Name, match.Name)).ToList();
                if (exact.Count == 1) appId = exact[0].SteamAppId;
            }
            catch (Exception error) when (error is HttpRequestException or JsonException or OperationCanceledException && !cancellationToken.IsCancellationRequested) { result.Notices.Add("steamUnavailable"); }
        }
        if (appId.HasValue)
        {
            try { steam = await sources.SteamAsync(appId.Value, request.IncludeArtwork, token); }
            catch (Exception error) when (error is HttpRequestException or JsonException or OperationCanceledException && !cancellationToken.IsCancellationRequested) { result.Notices.Add("steamUnavailable"); }
        }
        var facts = new List<PublicGameMetadata>();
        if (steam is not null) facts.Add(steam);
        var durationName = steam?.Identity.Name;
        if (request.WikidataId is not null || steam is null || new[] { "name", "released", "developer" }.Any(field => !steam.Fields.ContainsKey(field)))
        {
            try
            {
                var wiki = await sources.WikidataAsync(game.Name, appId, token);
                result.Matches.AddRange(wiki.Select(item => item.Identity));
                var chosen = request.WikidataId is not null ? wiki.SingleOrDefault(item => item.Identity.WikidataId == request.WikidataId)
                    : wiki.Count == 1 && (appId.HasValue || SameName(game.Name, wiki[0].Identity.Name)) ? wiki[0] : null;
                if (chosen is not null) { facts.Add(chosen); durationName ??= chosen.Identity.Name; }
            }
            catch (Exception error) when (error is HttpRequestException or JsonException or OperationCanceledException && !cancellationToken.IsCancellationRequested) { result.Notices.Add("wikidataUnavailable"); }
        }
        var needsDurations = request.IncludeDurations && (request.Overwrite || Empty(game.Story) || Empty(game.Completion));
        var needsCritic = request.IncludeCritic && request.ReviewVersion == 2 && (request.Overwrite || Empty(game.Critic)) && !HasCritic(steam);
        if (needsDurations || needsCritic)
        {
            // A reviewed edition must not silently revert to the custom library
            // title when that explicit identity cannot be resolved.
            if (durationName is null && (request.SteamAppId.HasValue || request.WikidataId is not null)) result.Notices.Add("chooseIdentity");
            else try
            {
                var duration = await durations.LookupAsync(durationName ?? game.Name, request.DekuDealsSlug, token, forceRefresh: request.ReviewVersion == 2 && request.Overwrite);
                result.Matches.AddRange(duration.Matches);
                if (duration.Metadata is not null) facts.Add(duration.Metadata);
                if (duration.Metadata is null) result.Notices.Add(duration.Unavailable ? needsDurations ? "durationSourceUnavailable" : "criticSourceUnavailable" : duration.Matches.Count > 0 ? "chooseDurationIdentity" : needsDurations ? "durationUnavailable" : "criticUnavailable");
                else
                {
                    if (needsDurations && !duration.Metadata.SourceHours.Keys.Any(field => field is "story" or "completion")) result.Notices.Add("durationUnavailable");
                    if (needsCritic && (!HasCritic(duration.Metadata) || duration.Metadata.CriticProvider == "OpenCritic")) result.Notices.Add("steamDbNotAutomated");
                    if (needsCritic && !HasCritic(duration.Metadata)) result.Notices.Add("criticUnavailable");
                }
            }
            catch (Exception error) when (error is HttpRequestException or OperationCanceledException && !cancellationToken.IsCancellationRequested) { result.Notices.Add(needsDurations ? "durationSourceUnavailable" : "criticSourceUnavailable"); }
        }
        cancellationToken.ThrowIfCancellationRequested();
        var fields = new Dictionary<string, GameMetadataFieldDto>();
        foreach (var source in facts)
        {
            if (!result.Matches.Any(match => match.Url == source.Identity.Url)) result.Matches.Add(source.Identity);
            foreach (var (field, value) in source.Fields)
            {
                if (!Allowed.Contains(field) || fields.ContainsKey(field) || !request.Overwrite && !Empty(Current(game, field))) continue;
                if (field == "critic" && !request.IncludeCritic || field is "logo" or "hero" or "cover" && !request.IncludeArtwork) continue;
                if (field is "story" or "completion" && (!request.IncludeDurations || source.Identity.Source != "dekudeals" || !source.SourceHours.ContainsKey(field))) continue;
                var criticProvider = field == "critic" ? source.Identity.Source == "steam" ? "Metacritic" : request.ReviewVersion == 2 && source.Identity.Source == "dekudeals" && source.CriticProvider is "Metacritic" or "OpenCritic" && DekuDealsDurationProvider.ValidCriticUrl(source.CriticProvider, source.CriticUrl) ? source.CriticProvider : null : null;
                if (field == "critic" && criticProvider is null) continue;
                var candidate = JsonSerializer.SerializeToElement(value, Json);
                if (!ValidValue(field, candidate)) continue;
                var unchanged = SameValue(Current(game, field), candidate) && (field != "critic" || game.CriticProvider == criticProvider);
                if (SameValue(Current(game, field), candidate) && !(request.ReviewVersion == 2 && request.Overwrite)) continue;
                fields[field] = new() { Field = field, Value = candidate, Current = JsonSerializer.SerializeToElement(Current(game, field), Json), Source = source.Identity.Source, Url = source.Identity.Url, SourceHours = source.SourceHours.TryGetValue(field, out var hours) ? hours : null,
                    CriticProvider = criticProvider, CurrentCriticProvider = field == "critic" && KnownCritic(game.CriticProvider) ? game.CriticProvider : null,
                    CriticUrl = field == "critic" ? source.CriticUrl : null, CriticScope = field == "critic" ? source.Identity.Source == "steam" ? "steam-edition" : "listed-platform" : null, Unchanged = unchanged };
            }
        }
        result.Fields = fields.Values.ToList();
        result.Matches = result.Matches.DistinctBy(match => match.Url).Take(12).ToList();
        if (result.Fields.Count == 0) result.Notices.Add(result.Matches.Count > 0 && facts.Count == 0 ? "chooseIdentity" : "noChanges");
        else result.Token = (request.ReviewVersion == 2 ? criticProtector : protector).Protect(JsonSerializer.Serialize(new Plan { UserId = userId, GameId = gameId, UpdatedAt = game.UpdatedAt, ExpiresAt = clock.GetUtcNow().AddMinutes(10), Overwrite = request.Overwrite, ReviewVersion = request.ReviewVersion, Fields = result.Fields }, Json));
        result.Notices = result.Notices.Distinct().ToList();
        return CatalogServiceResult<GameMetadataPreviewDto>.Ok(result);
    }

    public async Task<CatalogServiceResult<GameMetadataApplyDto>> ApplyAsync(int gameId, int userId, GameMetadataApplyRequest request, CancellationToken cancellationToken = default)
    {
        if (request.Token is null || request.Token.Length is < 1 or > 65536 || request.Fields is null || request.Fields.Count is < 1 or > 9 || request.Fields.Distinct().Count() != request.Fields.Count || request.Fields.Any(field => !Allowed.Contains(field))) return CatalogServiceResult<GameMetadataApplyDto>.BadRequest("Invalid metadata selection.");
        Plan? plan; var reviewVersion = 2;
        try
        {
            string payload;
            try { payload = criticProtector.Unprotect(request.Token); }
            catch (CryptographicException) { reviewVersion = 1; payload = protector.Unprotect(request.Token); }
            plan = JsonSerializer.Deserialize<Plan>(payload, Json);
        }
        catch (Exception error) when (error is CryptographicException or JsonException or FormatException) { return CatalogServiceResult<GameMetadataApplyDto>.BadRequest("Metadata review is invalid or expired."); }
        if (plan is null || plan.ReviewVersion != reviewVersion || plan.GameId != gameId || plan.ExpiresAt <= clock.GetUtcNow()) return CatalogServiceResult<GameMetadataApplyDto>.BadRequest("Metadata review is invalid or expired.");
        if (plan.UserId != userId) return new() { StatusCode = 403, Error = "Metadata review belongs to another account." };
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var game = await context.Games.SingleOrDefaultAsync(game => game.Id == gameId && game.UserId == userId, cancellationToken);
        if (game is null) return CatalogServiceResult<GameMetadataApplyDto>.NotFoundResult("Game unavailable.");
        if (game.UpdatedAt != plan.UpdatedAt) return CatalogServiceResult<GameMetadataApplyDto>.ConflictResult("Game changed. Look up and review the latest values again.");
        var patch = new Dictionary<string, object>();
        var unchangedFields = new List<string>();
        foreach (var field in request.Fields)
        {
            var value = plan.Fields.SingleOrDefault(candidate => candidate.Field == field);
            if (value is null || !ValidValue(field, value.Value) || !plan.Overwrite && !Empty(Current(game, field))) return CatalogServiceResult<GameMetadataApplyDto>.ConflictResult("Metadata selection changed. Review again.");
            var sameValue = SameValue(Current(game, field), value.Value);
            var criticProvider = field == "critic" ? reviewVersion == 1 ? "Metacritic" : value.CriticProvider : null;
            if (field == "critic" && (criticProvider is not ("Metacritic" or "OpenCritic") || value.Source == "dekudeals" && !DekuDealsDurationProvider.ValidCriticUrl(criticProvider, value.CriticUrl))) return CatalogServiceResult<GameMetadataApplyDto>.BadRequest("Invalid critic provenance.");
            if (!sameValue) patch[field] = value.Value.ValueKind == JsonValueKind.Number ? value.Value.GetInt32() : value.Value.GetString()!;
            if (field == "critic" && game.CriticProvider != criticProvider) patch["criticProvider"] = criticProvider!;
            else if (sameValue) unchangedFields.Add(field);
        }
        cancellationToken.ThrowIfCancellationRequested();
        if (patch.Count > 0)
        {
            var saved = await games.UpdateGameAsync(gameId, JsonSerializer.SerializeToElement(patch, Json), userId);
            if (!saved.Success) return CatalogServiceResult<GameMetadataApplyDto>.BadRequest(saved.Error ?? "Metadata could not be saved.");
        }
        await transaction.CommitAsync(cancellationToken);
        var updated = await games.GetGameByIdAsync(gameId, userId);
        return updated is null ? CatalogServiceResult<GameMetadataApplyDto>.NotFoundResult("Game unavailable.") : CatalogServiceResult<GameMetadataApplyDto>.Ok(new() { Game = updated, AppliedFields = request.Fields, UnchangedFields = unchangedFields });
    }
}
