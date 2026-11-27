using System.Security.Cryptography;
using System.Text.Json;
using GamesDatabase.Api.Application.Interfaces;
using GamesDatabase.Api.Contracts;
using GamesDatabase.Api.Domain.Entities;
using GamesDatabase.Api.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace GamesDatabase.Api.Application.Services;

/// <summary>Owned durable work; each metadata save and its receipt share one SQLite commit.</summary>
public sealed class MetadataAutofillJobService(GamesDbContext db, IGameMetadataService metadata, TimeProvider clock,
    MetadataAutofillSignal signal, ILogger<MetadataAutofillJobService> logger) : IMetadataAutofillJobService
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly HashSet<string> SourceFailures = ["steamUnavailable", "wikidataUnavailable", "durationSourceUnavailable", "criticSourceUnavailable"];
    private static readonly HashSet<string> AllowedFields = ["name", "released", "developer", "critic", "logo", "hero", "cover", "story", "completion"];
    private DateTime Now => clock.GetUtcNow().UtcDateTime;
    private sealed class Entry : MetadataAutofillItemDto
    {
        public DateTime ExpectedUpdatedAt { get; set; }
        public int Attempts { get; set; }
        public List<string> CompletedFields { get; set; } = [];
    }
    private static List<Entry> Entries(MetadataAutofillJob job) => JsonSerializer.Deserialize<List<Entry>>(job.ItemsJson, Json) ?? [];
    private static bool Pending(Entry item) => item.Status is "pending" or "looking" or "waiting";
    private static DateTime Utc(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc);
    private static MetadataAutofillJobDto Snapshot(MetadataAutofillJob job, bool includeItems = true)
    {
        var items = Entries(job);
        return new()
        {
            Id = job.Id, ClientRequestId = job.ClientRequestId, State = job.State, Overwrite = job.Overwrite,
            AcceptListedPlatformCritic = job.AcceptListedPlatformCritic, Active = job.ActiveUserId.HasValue,
            Dismissed = job.Dismissed, Revision = job.Revision, CreatedAt = Utc(job.CreatedAt), UpdatedAt = Utc(job.UpdatedAt),
            FinishedAt = job.FinishedAt.HasValue ? Utc(job.FinishedAt.Value) : null, NextAttemptAt = job.NextAttemptAt.HasValue ? Utc(job.NextAttemptAt.Value) : null, TotalGames = items.Count,
            ProcessedGames = items.Count(item => !Pending(item) && item.Status != "cancelled"),
            UpdatedGames = items.Count(item => item.Status == "updated"), UnchangedGames = items.Count(item => item.Status == "unchanged"),
            ReviewGames = items.Count(item => item.Status == "review"), FailedGames = items.Count(item => item.Status == "failed"),
            PartialGames = items.Count(item => item.Status == "partial"), CancelledGames = items.Count(item => item.Status == "cancelled"),
            CurrentGameId = job.CurrentGameId, CurrentGameName = items.FirstOrDefault(item => item.GameId == job.CurrentGameId)?.Name,
            Phase = job.CurrentPhase,
            // Project explicitly: persisted revisions/attempts are not a public mutation contract.
            Items = includeItems ? items.Select(item => new MetadataAutofillItemDto
            {
                GameId = item.GameId, Name = item.Name, Status = item.Status, AppliedFields = item.AppliedFields,
                UnchangedFields = item.UnchangedFields, Notices = item.Notices, Matches = item.Matches
            }).ToList() : []
        };
    }
    private void Store(MetadataAutofillJob job, List<Entry> items, bool releaseLease = true)
    {
        if (job.CancelRequested)
        {
            foreach (var item in items.Where(Pending)) item.Status = "cancelled";
            job.State = "cancelled";
        }
        else if (!items.Any(Pending)) job.State = "completed";
        else if (job.NextAttemptAt > Now) job.State = "waiting";
        else job.State = "queued";
        if (job.State is "cancelled" or "completed")
        {
            job.ActiveUserId = null; job.FinishedAt = Now; job.NextAttemptAt = null;
            releaseLease = true;
        }
        else if (!releaseLease && job.LeaseOwner is not null && job.LeaseUntil > Now) job.State = job.CancelRequested ? "cancelling" : "running";
        job.ItemsJson = JsonSerializer.Serialize(items, Json);
        job.UpdatedAt = Now; job.Revision++;
        if (releaseLease) { job.LeaseOwner = null; job.LeaseUntil = null; job.CurrentPhase = null; job.CurrentGameId = null; }
    }
    private Task<MetadataAutofillJob?> Owned(int userId, Guid id, CancellationToken token) => db.MetadataAutofillJobs.SingleOrDefaultAsync(job => job.Id == id && job.UserId == userId, token);
    private static CatalogServiceResult<T> Missing<T>() => CatalogServiceResult<T>.NotFoundResult("metadataJobUnavailable");
    private static CatalogServiceResult<T> Conflict<T>(string code) => CatalogServiceResult<T>.ConflictResult(code);

    public async Task<CatalogServiceResult<MetadataAutofillJobDto>> StartAsync(int userId, MetadataAutofillStartRequest request, CancellationToken token = default)
    {
        if (request.ClientRequestId == Guid.Empty || request.GameIds is not { Count: > 0 and <= 1000 } || request.GameIds.Any(id => id <= 0) || request.GameIds.Distinct().Count() != request.GameIds.Count)
            return CatalogServiceResult<MetadataAutofillJobDto>.BadRequest("invalidMetadataJobScope");
        var hash = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new { request.GameIds, request.Overwrite, request.AcceptListedPlatformCritic }, Json)));
        await using var transaction = await db.Database.BeginTransactionAsync(token);
        var replay = await db.MetadataAutofillJobs.SingleOrDefaultAsync(job => job.UserId == userId && job.ClientRequestId == request.ClientRequestId, token);
        if (replay is not null) return replay.RequestHash == hash ? CatalogServiceResult<MetadataAutofillJobDto>.Ok(Snapshot(replay)) : Conflict<MetadataAutofillJobDto>("metadataJobRequestChanged");
        if (await db.MetadataAutofillJobs.AnyAsync(job => job.ActiveUserId == userId, token)) return Conflict<MetadataAutofillJobDto>("metadataJobAlreadyActive");
        if (await db.MetadataAutofillJobs.CountAsync(job => job.ActiveUserId != null, token) >= 16) return new() { StatusCode = 429, Error = "metadataJobQueueFull" };
        var games = await db.Games.AsNoTracking().Where(game => game.UserId == userId && request.GameIds.Contains(game.Id)).Select(game => new { game.Id, game.Name, game.UpdatedAt }).ToListAsync(token);
        if (games.Count != request.GameIds.Count) return CatalogServiceResult<MetadataAutofillJobDto>.BadRequest("invalidMetadataJobScope");
        var owned = games.ToDictionary(game => game.Id);
        var items = request.GameIds.Select(id => new Entry { GameId = id, Name = owned[id].Name.Length <= 500 ? owned[id].Name : owned[id].Name[..499] + "…", ExpectedUpdatedAt = owned[id].UpdatedAt }).ToList();
        var job = new MetadataAutofillJob
        {
            UserId = userId, ClientRequestId = request.ClientRequestId, RequestHash = hash, ActiveUserId = userId,
            Overwrite = request.Overwrite, AcceptListedPlatformCritic = request.AcceptListedPlatformCritic,
            ItemsJson = JsonSerializer.Serialize(items, Json), CreatedAt = Now, UpdatedAt = Now
        };
        db.MetadataAutofillJobs.Add(job);
        await db.SaveChangesAsync(token);
        await transaction.CommitAsync(token);
        signal.Wake();
        logger.LogInformation("Metadata job {JobId} explicitly queued for {Count} frozen games (replace={Replace})", job.Id, items.Count, job.Overwrite);
        return CatalogServiceResult<MetadataAutofillJobDto>.Ok(Snapshot(job));
    }

    public async Task<MetadataAutofillJobDto?> CurrentAsync(int userId, bool includeItems = true, CancellationToken token = default)
    {
        var job = await db.MetadataAutofillJobs.AsNoTracking().Where(job => job.UserId == userId && (!job.Dismissed || job.ActiveUserId != null)).OrderByDescending(job => job.ActiveUserId != null).ThenByDescending(job => job.CreatedAt).FirstOrDefaultAsync(token);
        return job is null ? null : Snapshot(job, includeItems);
    }
    public async Task<MetadataAutofillJobDto?> GetAsync(int userId, Guid jobId, bool includeItems = true, CancellationToken token = default)
    {
        var job = await db.MetadataAutofillJobs.AsNoTracking().SingleOrDefaultAsync(job => job.Id == jobId && job.UserId == userId, token);
        return job is null ? null : Snapshot(job, includeItems);
    }
    public async Task<List<MetadataAutofillJobDto>> HistoryAsync(int userId, int skip = 0, int take = 10, CancellationToken token = default)
    {
        var jobs = await db.MetadataAutofillJobs.AsNoTracking().Where(job => job.UserId == userId).OrderByDescending(job => job.CreatedAt).ThenByDescending(job => job.Id).Skip(Math.Clamp(skip, 0, 100000)).Take(Math.Clamp(take, 1, 20)).ToListAsync(token);
        return jobs.Select(job => Snapshot(job, false)).ToList();
    }
    public async Task<CatalogServiceResult<MetadataAutofillJobDto>> CancelAsync(int userId, Guid jobId, CancellationToken token = default)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(token);
        var job = await Owned(userId, jobId, token);
        if (job is null) return Missing<MetadataAutofillJobDto>();
        if (job.ActiveUserId.HasValue)
        {
            job.CancelRequested = true;
            if (job.LeaseOwner is null || job.LeaseUntil <= Now) Store(job, Entries(job));
            else { job.State = "cancelling"; job.Revision++; job.UpdatedAt = Now; }
            await db.SaveChangesAsync(token);
        }
        await transaction.CommitAsync(token);
        signal.Wake();
        return CatalogServiceResult<MetadataAutofillJobDto>.Ok(Snapshot(job));
    }
    public async Task<CatalogServiceResult<MetadataAutofillJobDto>> RetryAsync(int userId, Guid jobId, CancellationToken token = default)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(token);
        var job = await Owned(userId, jobId, token);
        if (job is null) return Missing<MetadataAutofillJobDto>();
        if (job.ActiveUserId.HasValue || await db.MetadataAutofillJobs.AnyAsync(other => other.ActiveUserId == userId, token)) return Conflict<MetadataAutofillJobDto>("metadataJobAlreadyActive");
        var items = Entries(job);
        var retry = items.Where(item => item.Status is "failed" or "partial" or "cancelled").ToList();
        if (retry.Count == 0) return CatalogServiceResult<MetadataAutofillJobDto>.Ok(Snapshot(job));
        foreach (var item in retry) { item.Status = "pending"; item.Attempts = 0; item.Notices = []; }
        job.ActiveUserId = userId; job.CancelRequested = false; job.Dismissed = false; job.FinishedAt = null; job.NextAttemptAt = null;
        Store(job, items);
        await db.SaveChangesAsync(token); await transaction.CommitAsync(token); signal.Wake();
        return CatalogServiceResult<MetadataAutofillJobDto>.Ok(Snapshot(job));
    }
    public async Task<CatalogServiceResult<MetadataAutofillJobDto>> DismissAsync(int userId, Guid jobId, CancellationToken token = default)
    {
        var job = await Owned(userId, jobId, token);
        if (job is null) return Missing<MetadataAutofillJobDto>();
        if (job.ActiveUserId.HasValue) return Conflict<MetadataAutofillJobDto>("metadataJobStillActive");
        job.Dismissed = true; job.Revision++; job.UpdatedAt = Now;
        await db.SaveChangesAsync(token);
        return CatalogServiceResult<MetadataAutofillJobDto>.Ok(Snapshot(job));
    }

    public async Task<CatalogServiceResult<MetadataAutofillJobDto>> AcceptListedCriticsAsync(int userId, Guid jobId, CancellationToken token = default)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(token);
        var job = await Owned(userId, jobId, token);
        if (job is null) return Missing<MetadataAutofillJobDto>();
        if (job.State == "cancelling" || await db.MetadataAutofillJobs.AnyAsync(other => other.Id != jobId && other.ActiveUserId == userId, token)) return Conflict<MetadataAutofillJobDto>("metadataJobAlreadyActive");
        var items = Entries(job);
        var pending = items.Where(item => item.Status == "review" && item.Notices.Contains("criticPlatformScope") && !item.Notices.Any(notice => notice is "chooseIdentity" or "chooseDurationIdentity" or "gameChanged")).ToList();
        if (pending.Count == 0) return CatalogServiceResult<MetadataAutofillJobDto>.Ok(Snapshot(job));
        foreach (var item in pending) { item.Status = "pending"; item.Attempts = 0; item.Notices = []; }
        job.AcceptListedPlatformCritic = true; job.ActiveUserId = userId; job.Dismissed = false; job.CancelRequested = false; job.FinishedAt = null;
        Store(job, items, releaseLease: job.LeaseOwner is null || job.LeaseUntil <= Now);
        await db.SaveChangesAsync(token); await transaction.CommitAsync(token); signal.Wake();
        return CatalogServiceResult<MetadataAutofillJobDto>.Ok(Snapshot(job));
    }

    public async Task<CatalogServiceResult<GameMetadataApplyDto>> ApplyReviewAsync(int userId, Guid jobId, int gameId, GameMetadataApplyRequest request, CancellationToken token = default)
    {
        // A manual review is an explicit new signed choice, not a replay of a stale job plan.
        await using var transaction = await db.Database.BeginTransactionAsync(token);
        var job = await Owned(userId, jobId, token);
        if (job is null) return Missing<GameMetadataApplyDto>();
        if (job.CurrentGameId == gameId && job.LeaseOwner is not null && job.LeaseUntil > Now) return Conflict<GameMetadataApplyDto>("metadataJobBusy");
        var items = Entries(job); var item = items.SingleOrDefault(item => item.GameId == gameId);
        if (item is null || item.Status is not ("review" or "failed" or "partial")) return Conflict<GameMetadataApplyDto>("metadataJobReviewChanged");
        if (request.Fields is null || request.Fields.Any(field => !AllowedFields.Contains(field))) return CatalogServiceResult<GameMetadataApplyDto>.BadRequest("invalidMetadataSelection");
        var saved = await metadata.ApplyAsync(gameId, userId, request, token);
        if (!saved.Success || saved.Data is null) return saved; // Disposal rolls back BOTH components on any failure.
        item.AppliedFields = item.AppliedFields.Concat(saved.Data.AppliedFields.Except(saved.Data.UnchangedFields)).Distinct().ToList();
        item.UnchangedFields = item.UnchangedFields.Concat(saved.Data.UnchangedFields).Distinct().ToList();
        item.CompletedFields = item.CompletedFields.Concat(saved.Data.AppliedFields).Distinct().ToList();
        item.ExpectedUpdatedAt = saved.Data.Game.UpdatedAt;
        item.Status = item.AppliedFields.Count > 0 ? "updated" : "unchanged"; item.Notices = []; item.Matches = [];
        // Other pending games may still be looking up sources. Keep that lease
        // and merge the reviewed item; closing/reviewing one never loses siblings.
        Store(job, items, releaseLease: job.LeaseOwner is null || job.LeaseUntil <= Now);
        await db.SaveChangesAsync(token); await transaction.CommitAsync(token);
        return saved;
    }

    public async Task<bool> ProcessNextAsync(string workerId, CancellationToken token = default)
    {
        var now = Now;
        var candidate = await db.MetadataAutofillJobs.AsNoTracking()
            .Where(job => job.ActiveUserId != null && (job.NextAttemptAt == null || job.NextAttemptAt <= now || job.CancelRequested) && (job.LeaseOwner == null || job.LeaseUntil <= now))
            .OrderBy(job => job.UpdatedAt).ThenBy(job => job.CreatedAt).FirstOrDefaultAsync(token);
        if (candidate is null) return false;
        var claimed = await db.MetadataAutofillJobs.Where(job => job.Id == candidate.Id && job.Revision == candidate.Revision && (job.LeaseOwner == null || job.LeaseUntil <= now))
            .ExecuteUpdateAsync(setters => setters.SetProperty(job => job.LeaseOwner, workerId).SetProperty(job => job.LeaseUntil, now.AddMinutes(2)).SetProperty(job => job.Revision, job => job.Revision + 1), token);
        if (claimed == 0) return true;
        db.ChangeTracker.Clear();
        var job = (await db.MetadataAutofillJobs.SingleAsync(job => job.Id == candidate.Id, token));
        var items = Entries(job);
        var item = items.FirstOrDefault(Pending);
        if (job.CancelRequested || item is null)
        {
            Store(job, items); await db.SaveChangesAsync(token); return true;
        }
        item.Status = "looking"; item.Attempts++;
        job.State = "running"; job.CurrentPhase = "looking"; job.CurrentGameId = item.GameId; job.NextAttemptAt = null;
        job.ItemsJson = JsonSerializer.Serialize(items, Json); job.Revision++; job.UpdatedAt = Now;
        await db.SaveChangesAsync(token);
        try
        {
            var current = await db.Games.AsNoTracking().Where(game => game.Id == item.GameId && game.UserId == job.UserId).Select(game => new { game.UpdatedAt }).SingleOrDefaultAsync(token);
            if (current is null) { await RecordOutcome(job.Id, item.GameId, workerId, "failed", ["gameUnavailable"], token); return true; }
            if (current.UpdatedAt != item.ExpectedUpdatedAt) { await RecordOutcome(job.Id, item.GameId, workerId, "review", ["gameChanged"], token); return true; }
            var response = await metadata.PreviewAsync(item.GameId, job.UserId, new() { ReviewVersion = 2, Overwrite = job.Overwrite, BestMatch = true }, token);
            if (!response.Success || response.Data is null) { await RecordOutcome(job.Id, item.GameId, workerId, "failed", ["lookupFailed"], token); return true; }
            var preview = response.Data;
            var sourceFailed = preview.Notices.Any(SourceFailures.Contains);
            if (sourceFailed && item.Attempts < 3)
            {
                db.ChangeTracker.Clear(); job = await db.MetadataAutofillJobs.SingleAsync(value => value.Id == job.Id, token);
                if (job.LeaseOwner != workerId) return true;
                items = Entries(job); item = items.Single(value => value.GameId == item.GameId);
                item.Status = "waiting"; item.Notices = preview.Notices;
                job.NextAttemptAt = Now.AddSeconds(Math.Clamp(preview.RetryAfterSeconds ?? 15, 1, 3600));
                Store(job, items); await db.SaveChangesAsync(token); return true;
            }
            await CheckpointPreview(job.Id, item.GameId, workerId, preview, sourceFailed, token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception error) when (error is HttpRequestException or OperationCanceledException or JsonException)
        {
            await RecordOutcome(job.Id, item.GameId, workerId, "failed", ["lookupFailed"], token);
        }
        catch (Exception error) when (error is DbUpdateException or SqliteException)
        {
            // Commit may have succeeded. Do NOT repeat a save here: the next lease
            // reads the atomic ledger, so a committed receipt is never re-applied.
            db.ChangeTracker.Clear();
            logger.LogWarning("Metadata job {JobId} database checkpoint interrupted ({FailureType}); durable receipt will be reconciled", job.Id, error.GetType().Name);
        }
        catch (Exception error)
        {
            // A malformed/unexpected provider result must not trap the entire
            // queue forever. Atomic terminal receipts no longer own this lease,
            // so RecordOutcome cannot turn a committed success into a retry.
            logger.LogWarning("Metadata job {JobId} lookup failed ({FailureType}); other frozen games remain available", job.Id, error.GetType().Name);
            await RecordOutcome(job.Id, item.GameId, workerId, "failed", ["lookupFailed"], token);
        }
        return true;
    }

    private async Task RecordOutcome(Guid id, int gameId, string worker, string state, List<string> notices, CancellationToken token)
    {
        db.ChangeTracker.Clear();
        var job = await db.MetadataAutofillJobs.SingleOrDefaultAsync(job => job.Id == id && job.LeaseOwner == worker, token);
        if (job is null) return;
        var items = Entries(job); var item = items.Single(value => value.GameId == gameId);
        if (!Pending(item)) return;
        item.Status = state; item.Notices = notices;
        Store(job, items); await db.SaveChangesAsync(token);
    }

    private async Task CheckpointPreview(Guid id, int gameId, string worker, GameMetadataPreviewDto preview, bool sourceFailed, CancellationToken token)
    {
        db.ChangeTracker.Clear();
        string? failure = null;
        {
            await using var transaction = await db.Database.BeginTransactionAsync(token);
            var job = await db.MetadataAutofillJobs.SingleOrDefaultAsync(job => job.Id == id && job.LeaseOwner == worker, token);
            if (job is null) return;
            var items = Entries(job); var item = items.Single(value => value.GameId == gameId);
            if (!Pending(item)) return;
            if (job.CancelRequested) { Store(job, items); await db.SaveChangesAsync(token); await transaction.CommitAsync(token); return; }
            // Re-read the batch choice inside the transaction: a user can accept
            // all listed-platform notes while a different item is looking up.
            var blocked = preview.Fields.Where(field => !field.Unchanged && field.Field == "critic" && field.CriticProvider == "Metacritic" && field.CriticScope == "listed-platform" && !job.AcceptListedPlatformCritic).ToList();
            var selected = preview.Fields.Where(field => !field.Unchanged && !item.CompletedFields.Contains(field.Field) && !blocked.Contains(field)).ToList();
            var review = blocked.Count > 0 || preview.Notices.Any(notice => notice is "chooseIdentity" or "chooseDurationIdentity");
            var game = await db.Games.AsNoTracking().SingleOrDefaultAsync(game => game.Id == gameId && game.UserId == job.UserId, token);
            if (game is null || game.UpdatedAt != item.ExpectedUpdatedAt) failure = game is null ? "gameUnavailable" : "gameChanged";
            else
            {
                if (selected.Count > 0)
                {
                    job.CurrentPhase = "saving";
                    var saved = await metadata.ApplyAsync(gameId, job.UserId, new() { Token = preview.Token ?? "", Fields = selected.Select(field => field.Field).ToList() }, token);
                    if (!saved.Success || saved.Data is null) failure = saved.Conflict ? "gameChanged" : "saveFailed";
                    else
                    {
                        item.AppliedFields = item.AppliedFields.Concat(saved.Data.AppliedFields.Except(saved.Data.UnchangedFields)).Distinct().ToList();
                        item.CompletedFields = item.CompletedFields.Concat(saved.Data.AppliedFields).Distinct().ToList();
                        item.ExpectedUpdatedAt = saved.Data.Game.UpdatedAt;
                    }
                }
                if (failure is null)
                {
                    item.UnchangedFields = item.UnchangedFields.Concat(preview.Fields.Where(field => field.Unchanged).Select(field => field.Field)).Distinct().ToList();
                    item.CompletedFields = item.CompletedFields.Concat(preview.Fields.Where(field => field.Unchanged).Select(field => field.Field)).Distinct().ToList();
                    item.Notices = preview.Notices.Distinct().Take(16).ToList();
                    if (review && !item.Notices.Any(notice => notice is "chooseIdentity" or "chooseDurationIdentity")) item.Notices.Add("criticPlatformScope");
                    if (job.AcceptListedPlatformCritic && selected.Any(field => field.CriticScope == "listed-platform")) item.Notices.Add("criticListedPlatformAccepted");
                    item.Matches = review ? preview.Matches.Take(12).ToList() : [];
                    item.Status = review ? "review" : sourceFailed ? item.AppliedFields.Count > 0 ? "partial" : "failed" : item.AppliedFields.Count > 0 ? "updated" : "unchanged";
                    Store(job, items);
                    await db.SaveChangesAsync(token);
                    await transaction.CommitAsync(token);
                    logger.LogInformation("Metadata job {JobId} checkpoint {Status}: {AppliedCount} fields changed; notices={NoticeCodes}", id, item.Status, item.AppliedFields.Count, string.Join(',', item.Notices));
                    return;
                }
            }
            // An Apply failure cannot accidentally commit any tracked game changes.
            await transaction.RollbackAsync(token);
        }
        await RecordOutcome(id, gameId, worker, failure == "gameChanged" ? "review" : "failed", [failure ?? "saveFailed"], token);
    }
}
