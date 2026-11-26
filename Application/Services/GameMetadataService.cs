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
public sealed class GameMetadataService(GamesDbContext context, IGameService games, IPublicGameMetadataProvider sources, IDataProtectionProvider protection, TimeProvider clock) : IGameMetadataService
{
    private readonly IDataProtector protector = protection.CreateProtector("GamesDatabase.Metadata.v1");
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly HashSet<string> Allowed = ["name", "released", "developer", "critic", "logo", "hero", "cover"];
    private sealed class Plan
    {
        public int UserId { get; set; }
        public int GameId { get; set; }
        public DateTime UpdatedAt { get; set; }
        public DateTimeOffset ExpiresAt { get; set; }
        public bool Overwrite { get; set; }
        public List<GameMetadataFieldDto> Fields { get; set; } = [];
    }
    private static object? Current(Game game, string field) => field switch
    {
        "name" => game.Name, "released" => game.Released, "developer" => game.Developer, "critic" => game.Critic,
        "logo" => game.Logo, "hero" => game.Hero, "cover" => game.Cover, _ => null
    };
    private static bool Empty(object? value) => value is null || value is string text && string.IsNullOrWhiteSpace(text);
    private static bool SameName(string left, string right) => NameSearchMatching.Matches(left, new() { IncludeTerms = [right], MatchType = "exact" });
    private static bool ValidValue(string field, JsonElement value)
    {
        if (field == "critic") return value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var score) && score is >= 0 and <= 100;
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
        if (request.SteamAppId is <= 0 || request.WikidataId is { } id && !System.Text.RegularExpressions.Regex.IsMatch(id, "^Q[1-9][0-9]{0,14}$")) return CatalogServiceResult<GameMetadataPreviewDto>.BadRequest("Invalid metadata identity.");
        var game = await context.Games.AsNoTracking().SingleOrDefaultAsync(game => game.Id == gameId && game.UserId == userId, cancellationToken);
        if (game is null) return CatalogServiceResult<GameMetadataPreviewDto>.NotFoundResult("Game unavailable.");
        if (game.SteamAppId.HasValue && request.SteamAppId.HasValue && game.SteamAppId != request.SteamAppId) return CatalogServiceResult<GameMetadataPreviewDto>.BadRequest("Change the linked App ID explicitly before using another Steam identity.");
        var result = new GameMetadataPreviewDto { Overwrite = request.Overwrite, Notices = ["durationUnavailable"] };
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
        if (request.WikidataId is not null || steam is null || new[] { "name", "released", "developer" }.Any(field => !steam.Fields.ContainsKey(field)))
        {
            try
            {
                var wiki = await sources.WikidataAsync(game.Name, appId, token);
                result.Matches.AddRange(wiki.Select(item => item.Identity));
                var chosen = request.WikidataId is not null ? wiki.SingleOrDefault(item => item.Identity.WikidataId == request.WikidataId)
                    : wiki.Count == 1 && (appId.HasValue || SameName(game.Name, wiki[0].Identity.Name)) ? wiki[0] : null;
                if (chosen is not null) facts.Add(chosen);
            }
            catch (Exception error) when (error is HttpRequestException or JsonException or OperationCanceledException && !cancellationToken.IsCancellationRequested) { result.Notices.Add("wikidataUnavailable"); }
        }
        cancellationToken.ThrowIfCancellationRequested();
        var fields = new Dictionary<string, GameMetadataFieldDto>();
        foreach (var source in facts)
        {
            if (!result.Matches.Any(match => match.Url == source.Identity.Url)) result.Matches.Add(source.Identity);
            foreach (var (field, value) in source.Fields)
            {
                if (!Allowed.Contains(field) || fields.ContainsKey(field) || !request.Overwrite && !Empty(Current(game, field))) continue;
                var candidate = JsonSerializer.SerializeToElement(value, Json);
                if (!ValidValue(field, candidate)) continue;
                if (JsonSerializer.SerializeToElement(Current(game, field), Json).GetRawText() == candidate.GetRawText()) continue;
                fields[field] = new() { Field = field, Value = candidate, Current = JsonSerializer.SerializeToElement(Current(game, field), Json), Source = source.Identity.Source, Url = source.Identity.Url };
            }
        }
        result.Fields = fields.Values.ToList();
        result.Matches = result.Matches.DistinctBy(match => match.Url).Take(12).ToList();
        if (result.Fields.Count == 0) result.Notices.Add(result.Matches.Count > 0 && facts.Count == 0 ? "chooseIdentity" : "noChanges");
        else result.Token = protector.Protect(JsonSerializer.Serialize(new Plan { UserId = userId, GameId = gameId, UpdatedAt = game.UpdatedAt, ExpiresAt = clock.GetUtcNow().AddMinutes(10), Overwrite = request.Overwrite, Fields = result.Fields }, Json));
        return CatalogServiceResult<GameMetadataPreviewDto>.Ok(result);
    }

    public async Task<CatalogServiceResult<GameMetadataApplyDto>> ApplyAsync(int gameId, int userId, GameMetadataApplyRequest request, CancellationToken cancellationToken = default)
    {
        if (request.Token is null || request.Token.Length is < 1 or > 65536 || request.Fields is null || request.Fields.Count is < 1 or > 7 || request.Fields.Distinct().Count() != request.Fields.Count || request.Fields.Any(field => !Allowed.Contains(field))) return CatalogServiceResult<GameMetadataApplyDto>.BadRequest("Invalid metadata selection.");
        Plan? plan;
        try { plan = JsonSerializer.Deserialize<Plan>(protector.Unprotect(request.Token), Json); }
        catch (Exception error) when (error is CryptographicException or JsonException or FormatException) { return CatalogServiceResult<GameMetadataApplyDto>.BadRequest("Metadata review is invalid or expired."); }
        if (plan is null || plan.GameId != gameId || plan.ExpiresAt <= clock.GetUtcNow()) return CatalogServiceResult<GameMetadataApplyDto>.BadRequest("Metadata review is invalid or expired.");
        if (plan.UserId != userId) return new() { StatusCode = 403, Error = "Metadata review belongs to another account." };
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var game = await context.Games.SingleOrDefaultAsync(game => game.Id == gameId && game.UserId == userId, cancellationToken);
        if (game is null) return CatalogServiceResult<GameMetadataApplyDto>.NotFoundResult("Game unavailable.");
        if (game.UpdatedAt != plan.UpdatedAt) return CatalogServiceResult<GameMetadataApplyDto>.ConflictResult("Game changed. Look up and review the latest values again.");
        var patch = new Dictionary<string, object>();
        foreach (var field in request.Fields)
        {
            var value = plan.Fields.SingleOrDefault(candidate => candidate.Field == field);
            if (value is null || !ValidValue(field, value.Value) || !plan.Overwrite && !Empty(Current(game, field))) return CatalogServiceResult<GameMetadataApplyDto>.ConflictResult("Metadata selection changed. Review again.");
            patch[field] = value.Value.ValueKind == JsonValueKind.Number ? value.Value.GetInt32() : value.Value.GetString()!;
            if (field == "critic") patch["criticProvider"] = "Metacritic";
        }
        cancellationToken.ThrowIfCancellationRequested();
        var saved = await games.UpdateGameAsync(gameId, JsonSerializer.SerializeToElement(patch, Json), userId);
        if (!saved.Success) return CatalogServiceResult<GameMetadataApplyDto>.BadRequest(saved.Error ?? "Metadata could not be saved.");
        await transaction.CommitAsync(cancellationToken);
        var updated = await games.GetGameByIdAsync(gameId, userId);
        return updated is null ? CatalogServiceResult<GameMetadataApplyDto>.NotFoundResult("Game unavailable.") : CatalogServiceResult<GameMetadataApplyDto>.Ok(new() { Game = updated, AppliedFields = request.Fields });
    }
}
