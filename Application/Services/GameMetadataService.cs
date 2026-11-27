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
public sealed class GameMetadataService(GamesDbContext context, IGameService games, IPublicGameMetadataProvider sources, IGameDurationProvider durations, IGameArtworkProvider artwork, IDataProtectionProvider protection, TimeProvider clock, ILogger<GameMetadataService> logger) : IGameMetadataService
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
    private static bool KnownCritic(string? provider) => provider is "Metacritic" or "OpenCritic" or "SteamDB";
    private static bool SameValue(object? current, JsonElement candidate) => JsonSerializer.SerializeToElement(current, Json).GetRawText() == candidate.GetRawText();
    private static bool HasCritic(PublicGameMetadata? metadata) => metadata is not null && metadata.Fields.TryGetValue("critic", out var value) && ValidValue("critic", JsonSerializer.SerializeToElement(value, Json));
    private static bool SameMetadataTitle(PublicGameMetadata left, PublicGameMetadata right) =>
        left.Aliases.Append(left.Identity.Name).Any(name => right.Aliases.Append(right.Identity.Name).Any(other => MetadataEditionMatching.SameTitle(name, other)));
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
            "logo" or "hero" or "cover" => PublicArtworkUrl.Valid(text),
            _ => false
        };
    }

    public async Task<CatalogServiceResult<GameMetadataPreviewDto>> PreviewAsync(int gameId, int userId, GameMetadataLookupRequest request, CancellationToken cancellationToken = default)
    {
        if (request.ReviewVersion is not (1 or 2)) return CatalogServiceResult<GameMetadataPreviewDto>.BadRequest("Invalid metadata review version.");
        if (request.SteamAppId is <= 0 || request.WikidataId is { } id && !System.Text.RegularExpressions.Regex.IsMatch(id, "^Q[1-9][0-9]{0,14}$")) return CatalogServiceResult<GameMetadataPreviewDto>.BadRequest("Invalid metadata identity.");
        if (request.DekuDealsSlug is not null && !DekuDealsDurationProvider.ValidSlug(request.DekuDealsSlug)) return CatalogServiceResult<GameMetadataPreviewDto>.BadRequest("Invalid duration identity.");
        if (request.LaunchBoxId is <= 0 or > 999_999_999) return CatalogServiceResult<GameMetadataPreviewDto>.BadRequest("Invalid artwork identity.");
        var game = await context.Games.AsNoTracking().Include(game => game.Platform).SingleOrDefaultAsync(game => game.Id == gameId && game.UserId == userId, cancellationToken);
        if (game is null) return CatalogServiceResult<GameMetadataPreviewDto>.NotFoundResult("Game unavailable.");
        if (game.SteamAppId.HasValue && request.SteamAppId.HasValue && game.SteamAppId != request.SteamAppId) return CatalogServiceResult<GameMetadataPreviewDto>.BadRequest("Change the linked App ID explicitly before using another Steam identity.");
        var result = new GameMetadataPreviewDto { Overwrite = request.Overwrite, ReviewVersion = request.ReviewVersion };
        var library = new GameMetadataIdentityDto { Name = game.Name, ReleaseYear = MetadataEditionMatching.Year(game.Released), Platforms = game.Platform?.UserId == userId ? [game.Platform.Name] : [] };
        var refresh = request.ReviewVersion == 2 && request.Overwrite;
        void Unavailable(string notice, Exception error)
        {
            result.Notices.Add(notice);
            if (error is MetadataSourceUnavailableException { RetryAfterSeconds: { } delay }) result.RetryAfterSeconds = Math.Max(result.RetryAfterSeconds ?? 0, delay);
            logger.LogWarning("Metadata lookup for game {GameId}: {Source} ({FailureType})", gameId, notice, error.GetType().Name);
        }
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(25));
        var token = deadline.Token;
        var appId = game.SteamAppId ?? request.SteamAppId;
        PublicGameMetadata? steam = null;
        if (!appId.HasValue && request.WikidataId is null && request.DekuDealsSlug is null)
        {
            try
            {
                var matches = await sources.SearchSteamAsync(game.Name, token);
                result.Matches.AddRange(matches);
                appId = MetadataEditionMatching.Best(matches, game.Name, match => match, library)?.SteamAppId;
            }
            catch (Exception error) when (error is HttpRequestException or JsonException or OperationCanceledException && !cancellationToken.IsCancellationRequested) { Unavailable("steamUnavailable", error); }
        }
        if (appId.HasValue)
        {
            try { steam = await sources.SteamAsync(appId.Value, request.IncludeArtwork, token); }
            catch (Exception error) when (error is HttpRequestException or JsonException or OperationCanceledException && !cancellationToken.IsCancellationRequested) { Unavailable("steamUnavailable", error); }
        }
        if (steam is not null && !game.SteamAppId.HasValue && !request.SteamAppId.HasValue && MetadataEditionMatching.Conflicts(steam.Identity, library)) { steam = null; appId = null; result.Notices.Add("editionMismatch"); }
        var wiki = new List<PublicGameMetadata>();
        PublicGameMetadata? chosenWiki = null;
        if (request.WikidataId is not null || steam is null || new[] { "name", "released", "developer" }.Any(field => !steam.Fields.ContainsKey(field)))
        {
            try
            {
                wiki = await sources.WikidataAsync(steam?.Identity.Name ?? game.Name, appId, token, request.WikidataId, refresh);
                result.Matches.AddRange(wiki.Select(item => item.Identity));
                chosenWiki = request.WikidataId is not null ? wiki.SingleOrDefault(item => item.Identity.WikidataId == request.WikidataId)
                    : appId.HasValue && wiki.Count == 1 && !MetadataEditionMatching.Conflicts(wiki[0].Identity, steam?.Identity ?? library) ? wiki[0]
                    : MetadataEditionMatching.Best(wiki, game.Name, item => item.Identity, steam?.Identity ?? library);
            }
            catch (Exception error) when (error is HttpRequestException or JsonException or OperationCanceledException && !cancellationToken.IsCancellationRequested) { Unavailable("wikidataUnavailable", error); }
        }
        var durationName = request.WikidataId is not null && chosenWiki is null ? null : steam?.Identity.Name ?? chosenWiki?.Identity.Name;
        var needsDurations = request.IncludeDurations && (request.Overwrite || Empty(game.Story) || Empty(game.Completion));
        var needsCritic = request.IncludeCritic && request.ReviewVersion == 2 && (request.Overwrite || Empty(game.Critic)) && !HasCritic(steam);
        var needsDate = (request.Overwrite || Empty(game.Released)) && steam?.Fields.ContainsKey("released") != true && chosenWiki?.Fields.ContainsKey("released") != true;
        var needsArtwork = request.IncludeArtwork && new[] { "logo", "hero", "cover" }.Any(field => (request.Overwrite || Empty(Current(game, field))) && steam?.Fields.ContainsKey(field) != true);
        GameDurationLookup? duration = null;
        if (needsDurations || needsCritic || needsDate || needsArtwork || request.DekuDealsSlug is not null)
        {
            // A reviewed edition must not silently revert to the custom library
            // title when that explicit identity cannot be resolved.
            if (durationName is null && (request.SteamAppId.HasValue || request.WikidataId is not null || request.BestMatch && game.SteamAppId.HasValue)) result.Notices.Add("chooseIdentity");
            else try
            {
                duration = await durations.LookupAsync(durationName ?? game.Name, request.DekuDealsSlug, token, forceRefresh: refresh);
                if (duration.Metadata is null && !duration.Unavailable && request.DekuDealsSlug is null && chosenWiki is not null)
                {
                    var aliasMatches = duration.Matches.Where(match => match.Source == "dekudeals" && DekuDealsDurationProvider.ValidSlug(match.DekuDealsSlug) && chosenWiki.Aliases.Any(alias => MetadataEditionMatching.SameTitle(alias, match.Name))).ToList();
                    if (aliasMatches.Count == 1) duration = await durations.LookupAsync(durationName ?? game.Name, aliasMatches[0].DekuDealsSlug, token);
                }
                result.Matches.AddRange(duration.Matches);
                if (duration.RetryAfterSeconds.HasValue) result.RetryAfterSeconds = Math.Max(result.RetryAfterSeconds ?? 0, duration.RetryAfterSeconds.Value);
                if (duration.Metadata is null && (needsDurations || needsCritic)) result.Notices.Add(duration.Unavailable ? needsDurations ? "durationSourceUnavailable" : "criticSourceUnavailable" : !duration.IdentityConfirmed && duration.Matches.Count > 0 ? "chooseDurationIdentity" : needsDurations ? "durationUnavailable" : "criticUnavailable");
                else if (duration.Unavailable) result.Notices.Add("catalogSourceUnavailable");
                if (duration.Metadata is not null)
                {
                    if (needsDurations && !duration.Metadata.SourceHours.Keys.Any(field => field is "story" or "completion")) result.Notices.Add("durationUnavailable");
                    if (needsCritic && (!HasCritic(duration.Metadata) || duration.Metadata.CriticProvider == "OpenCritic")) result.Notices.Add("steamDbNotAutomated");
                    if (needsCritic && !HasCritic(duration.Metadata)) result.Notices.Add("criticUnavailable");
                }
            }
            catch (Exception error) when (error is HttpRequestException or OperationCanceledException && !cancellationToken.IsCancellationRequested) { Unavailable(needsDurations ? "durationSourceUnavailable" : needsCritic ? "criticSourceUnavailable" : "catalogSourceUnavailable", error); }
        }
        var deku = duration?.Metadata;
        var explicitRoot = request.WikidataId is not null || request.SteamAppId.HasValue || game.SteamAppId.HasValue || request.DekuDealsSlug is not null;
        if (request.DekuDealsSlug is not null && deku is not null && request.WikidataId is null && steam is null)
            chosenWiki = MetadataEditionMatching.Best(wiki, deku.Identity.Name, item => item.Identity, deku.Identity);
        var ambiguousWiki = steam is null && request.WikidataId is null && request.DekuDealsSlug is null && chosenWiki is null
            && wiki.Count(item => MetadataEditionMatching.SameTitle(item.Identity.Name, game.Name) && !MetadataEditionMatching.Conflicts(item.Identity, library)) > 1;
        var disagreement = chosenWiki is not null && deku is not null && (!SameMetadataTitle(chosenWiki, deku) || MetadataEditionMatching.Conflicts(chosenWiki.Identity, deku.Identity));
        if (disagreement && !explicitRoot && library.ReleaseYear is null && library.Platforms.Count == 0) { ambiguousWiki = true; chosenWiki = null; }
        var unresolved = durationName is null && (request.WikidataId is not null || request.SteamAppId.HasValue || game.SteamAppId.HasValue);
        var facts = new List<PublicGameMetadata>();
        if (steam is not null) facts.Add(steam);
        if (chosenWiki is not null) facts.Add(chosenWiki);
        if (ambiguousWiki || unresolved) result.Notices.Add("chooseIdentity");
        else if (deku is not null)
        {
            var anchor = steam?.Identity ?? chosenWiki?.Identity ?? (request.DekuDealsSlug is not null ? deku.Identity : library);
            if (disagreement || !(chosenWiki is not null ? SameMetadataTitle(chosenWiki, deku) : MetadataEditionMatching.SameTitle(deku.Identity.Name, anchor.Name)) || MetadataEditionMatching.Conflicts(deku.Identity, anchor)) result.Notices.Add("editionMismatch");
            else facts.Add(deku);
        }
        foreach (var source in facts) result.Notices.AddRange(source.Notices);
        if (request.IncludeArtwork && !ambiguousWiki && !unresolved)
        {
            var missing = new[] { "logo", "hero", "cover" }.Where(field => (request.Overwrite || Empty(Current(game, field))) && !facts.Any(source => source.Fields.ContainsKey(field))).ToList();
            if (missing.Count > 0)
            {
                try
                {
                    var root = steam?.Identity ?? chosenWiki?.Identity ?? facts.FirstOrDefault()?.Identity ?? library;
                    var artworkName = chosenWiki is not null && deku is not null && facts.Contains(deku) && SameMetadataTitle(chosenWiki, deku) ? deku.Identity.Name : root.Name;
                    var artworkEdition = new GameMetadataIdentityDto { Name = artworkName, SteamAppId = root.SteamAppId ?? appId,
                        ReleaseYear = root.ReleaseYear ?? library.ReleaseYear,
                        Platforms = library.Platforms.Count > 0 && !MetadataEditionMatching.Conflicts(new() { Platforms = library.Platforms }, root) ? library.Platforms : root.Platforms };
                    var evidence = new List<PublicGameMetadata> { new() { Identity = artworkEdition } };
                    evidence.AddRange(facts);
                    var images = await artwork.LookupAsync(evidence, missing, token, refresh, request.LaunchBoxId);
                    facts.AddRange(images.Sources.Where(source => source.Identity.Source is "steam" or "wikidata" or "dekudeals" or "steamgriddb" or "launchbox" or "nintendo")
                        .Select(source => new PublicGameMetadata { Identity = source.Identity, Fields = source.Fields.Where(pair => pair.Key is "logo" or "hero" or "cover").ToDictionary() }));
                    result.Matches.AddRange(images.Matches);
                    if (images.RetryAfterSeconds.HasValue) result.RetryAfterSeconds = Math.Max(result.RetryAfterSeconds ?? 0, images.RetryAfterSeconds.Value);
                    if (images.Unavailable) result.Notices.Add("artworkSourceUnavailable");
                    if (missing.Any(field => !facts.Any(source => source.Fields.ContainsKey(field))))
                        result.Notices.Add(images.Matches.Count > 1 ? "chooseArtworkIdentity" : "artworkUnavailable");
                }
                catch (Exception error) when (error is HttpRequestException or JsonException or OperationCanceledException && !cancellationToken.IsCancellationRequested) { Unavailable("artworkSourceUnavailable", error); }
            }
        }
        cancellationToken.ThrowIfCancellationRequested();
        var fields = new Dictionary<string, GameMetadataFieldDto>();
        foreach (var source in facts)
        {
            if (source.Identity.Source is "steam" or "wikidata" or "dekudeals" or "launchbox" && !result.Matches.Any(match => match.Url == source.Identity.Url)) result.Matches.Add(source.Identity);
            foreach (var (field, value) in source.Fields)
            {
                if (!Allowed.Contains(field) || fields.ContainsKey(field) || !request.Overwrite && !Empty(Current(game, field))) continue;
                if (source.Identity.Source is "steamgriddb" or "launchbox" or "nintendo" && field is not ("logo" or "hero" or "cover")) continue;
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
        if (result.Fields.Count > 0) result.Token = (request.ReviewVersion == 2 ? criticProtector : protector).Protect(JsonSerializer.Serialize(new Plan { UserId = userId, GameId = gameId, UpdatedAt = game.UpdatedAt, ExpiresAt = clock.GetUtcNow().AddMinutes(10), Overwrite = request.Overwrite, ReviewVersion = request.ReviewVersion, Fields = result.Fields }, Json));
        else if (!result.Notices.Any(notice => notice.StartsWith("choose", StringComparison.Ordinal) || notice.EndsWith("SourceUnavailable", StringComparison.Ordinal) || notice is "steamUnavailable" or "wikidataUnavailable")) result.Notices.Add(result.Matches.Count > 0 && facts.Count == 0 ? "chooseIdentity" : "noChanges");
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
        // A persistent job owns the outer transaction so its exact receipt and
        // this mutation commit atomically. Direct API callers still own theirs.
        await using var transaction = context.Database.CurrentTransaction is null ? await context.Database.BeginTransactionAsync(cancellationToken) : null;
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
        if (transaction is not null) await transaction.CommitAsync(cancellationToken);
        var updated = await games.GetGameByIdAsync(gameId, userId);
        return updated is null ? CatalogServiceResult<GameMetadataApplyDto>.NotFoundResult("Game unavailable.") : CatalogServiceResult<GameMetadataApplyDto>.Ok(new() { Game = updated, AppliedFields = request.Fields, UnchangedFields = unchangedFields });
    }
}
