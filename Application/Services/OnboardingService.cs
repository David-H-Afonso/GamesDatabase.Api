using GamesDatabase.Api.Application.Interfaces;
using GamesDatabase.Api.Application.Mapping;
using GamesDatabase.Api.Common;
using GamesDatabase.Api.Contracts;
using GamesDatabase.Api.Domain.Entities;
using GamesDatabase.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GamesDatabase.Api.Application.Services;

/// <summary>Explicit guide progress and a small, provenance-scoped sample batch. Reads never seed data.</summary>
public sealed class OnboardingService(GamesDbContext context, TimeProvider clock) : IOnboardingService
{
    public const string CurrentVersion = "3.0.0";
    public const string TutorialViewCreator = "Games Database tutorial";
    public static readonly string[] Steps = ["welcome", "library", "views", "organize", "progress", "home", "discover", "finish"];

    private IQueryable<Game> SampleGames(int userId, string? batch) => context.Games.AsNoTracking().AsSplitQuery()
        .Where(game => batch != null && game.UserId == userId && game.SampleBatchId == batch)
        .Include(game => game.Status).Include(game => game.Platform).Include(game => game.PlayedStatus)
        .Include(game => game.GamePlayWiths).ThenInclude(item => item.PlayWith);

    public async Task<OnboardingStateDto> GetAsync(int userId, CancellationToken cancellationToken = default)
    {
        var user = await context.Users.Where(user => user.Id == userId).Select(user => new { user.TutorialEnabled, RequiresPassword = user.IsDefault && user.PasswordHash == null }).FirstOrDefaultAsync(cancellationToken);
        var stored = await context.OnboardingStates.AsNoTracking().SingleOrDefaultAsync(state => state.UserId == userId, cancellationToken);
        var games = await SampleGames(userId, stored?.SampleBatchId).OrderBy(game => game.Id).ToListAsync(cancellationToken);
        var sampleLists = stored?.SampleBatchId is { } batchId ? await context.Playlists.AsNoTracking().Where(list => list.UserId == userId && list.SampleBatchId == batchId).Select(list => new TutorialSamplePlaylistDto { Id = list.Id, Name = list.Name, CreatedAt = list.CreatedAt, UpdatedAt = list.UpdatedAt }).ToListAsync(cancellationToken) : [];
        var exampleViews = await context.GameViews.AsNoTracking().Where(view => view.UserId == userId && view.CreatedBy == TutorialViewCreator).OrderBy(view => view.SortOrder).ThenBy(view => view.Id).Take(64).ToListAsync(cancellationToken);
        return new()
        {
            Version = CurrentVersion, Revision = stored?.Revision ?? 0, Enabled = user?.TutorialEnabled ?? false, RequiresPassword = user?.RequiresPassword ?? false,
            Step = stored is not null && Steps.Contains(stored.Step) ? stored.Step : "welcome", CompletedVersion = stored?.CompletedVersion,
            HasSamples = stored?.SampleBatchId != null,
            SampleGames = games.Select(game => game.ToDto()).ToList(),
            SamplePlaylists = sampleLists.Count, SampleLists = sampleLists, ExampleViews = exampleViews.Select(view => view.ToSummaryDto()).ToList()
        };
    }

    private OnboardingState AddState(int userId)
    {
        var state = new OnboardingState { UserId = userId, UpdatedAt = clock.GetUtcNow().UtcDateTime };
        context.OnboardingStates.Add(state);
        return state;
    }

    private void Advance(OnboardingState state) { state.Revision++; state.UpdatedAt = clock.GetUtcNow().UtcDateTime; }

    public async Task<CatalogServiceResult<OnboardingStateDto>> StartAsync(int userId, OnboardingStartRequest request, CancellationToken cancellationToken = default)
    {
        if (request.Revision < 0) return CatalogServiceResult<OnboardingStateDto>.BadRequest("Invalid guide revision.");
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var user = await context.Users.SingleOrDefaultAsync(user => user.Id == userId, cancellationToken);
        if (user is null) return CatalogServiceResult<OnboardingStateDto>.NotFoundResult("User unavailable.");
        var state = await context.OnboardingStates.SingleOrDefaultAsync(state => state.UserId == userId, cancellationToken);
        if ((state?.Revision ?? 0) != request.Revision) return CatalogServiceResult<OnboardingStateDto>.ConflictResult("Guide changed. Reload before continuing.");
        state ??= AddState(userId);
        if (request.Restart || state.CompletedVersion != null) { state.Step = "welcome"; state.CompletedVersion = null; }
        user.TutorialEnabled = true;
        Advance(state);
        try { await context.SaveChangesAsync(cancellationToken); await transaction.CommitAsync(cancellationToken); }
        catch (DbUpdateException) { return CatalogServiceResult<OnboardingStateDto>.ConflictResult("Guide changed. Reload before continuing."); }
        return CatalogServiceResult<OnboardingStateDto>.Ok(await GetAsync(userId, cancellationToken));
    }

    public async Task<CatalogServiceResult<OnboardingStateDto>> SaveAsync(int userId, OnboardingProgressRequest request, CancellationToken cancellationToken = default)
    {
        if (request.Revision < 0 || !Steps.Contains(request.Step) || request.Complete && request.Step != "finish") return CatalogServiceResult<OnboardingStateDto>.BadRequest("Invalid guide step.");
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var user = await context.Users.SingleOrDefaultAsync(user => user.Id == userId, cancellationToken);
        if (user is null) return CatalogServiceResult<OnboardingStateDto>.NotFoundResult("User unavailable.");
        var state = await context.OnboardingStates.SingleOrDefaultAsync(state => state.UserId == userId, cancellationToken);
        if ((state?.Revision ?? 0) != request.Revision) return CatalogServiceResult<OnboardingStateDto>.ConflictResult("Guide changed. Reload before continuing.");
        state ??= AddState(userId);
        state.Step = request.Step;
        if (request.Complete) state.CompletedVersion = CurrentVersion;
        user.TutorialEnabled = request.Enabled && !request.Complete;
        Advance(state);
        try { await context.SaveChangesAsync(cancellationToken); await transaction.CommitAsync(cancellationToken); }
        catch (DbUpdateException) { return CatalogServiceResult<OnboardingStateDto>.ConflictResult("Guide changed. Reload before continuing."); }
        return CatalogServiceResult<OnboardingStateDto>.Ok(await GetAsync(userId, cancellationToken));
    }

    public async Task<CatalogServiceResult<OnboardingStateDto>> CreateSamplesAsync(int userId, CreateTutorialSamplesRequest request, CancellationToken cancellationToken = default)
    {
        if (!request.Confirm || request.Revision < 0 || request.Locale is not ("en" or "es")) return CatalogServiceResult<OnboardingStateDto>.BadRequest("Confirm sample creation explicitly.");
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        if (!await context.Users.AnyAsync(user => user.Id == userId, cancellationToken)) return CatalogServiceResult<OnboardingStateDto>.NotFoundResult("User unavailable.");
        var state = await context.OnboardingStates.SingleOrDefaultAsync(state => state.UserId == userId, cancellationToken);
        if ((state?.Revision ?? 0) != request.Revision) return CatalogServiceResult<OnboardingStateDto>.ConflictResult("Guide changed. Reload before creating examples.");
        if (state?.SampleBatchId != null) return CatalogServiceResult<OnboardingStateDto>.Ok(await GetAsync(userId, cancellationToken));
        state ??= AddState(userId);
        var statuses = await context.GameStatuses.Where(status => status.UserId == userId && status.IsActive).OrderBy(status => status.SortOrder).ThenBy(status => status.Id).ToListAsync(cancellationToken);
        var regular = statuses.FirstOrDefault(status => status.StatusType == SpecialStatusType.None);
        if (regular is null)
        {
            var baseName = "Sample backlog";
            var taken = await context.GameStatuses.AnyAsync(status => status.UserId == userId && status.Name == baseName, cancellationToken);
            regular = new GameStatus { UserId = userId, Name = taken ? $"{baseName} {Guid.NewGuid():N}"[..30] : baseName, Color = "#647b91", SortOrder = statuses.Count + 1 };
            context.GameStatuses.Add(regular);
            await context.SaveChangesAsync(cancellationToken);
            state.SampleStatusId = regular.Id;
        }
        var playing = statuses.FirstOrDefault(status => status.StatusType == SpecialStatusType.Playing) ?? regular;
        var platform = await context.GamePlatforms.Where(platform => platform.UserId == userId && platform.IsActive).OrderBy(platform => platform.SortOrder).Select(platform => (int?)platform.Id).FirstOrDefaultAsync(cancellationToken);
        var batch = Guid.NewGuid().ToString();
        var names = new[] { "Amber Trail", "Orbit Shift", "Clockwork Harbor", "Quiet Grove", "Paper Horizon", "Signal Valley" };
        var games = names.Select((name, index) => new Game
        {
            UserId = userId, Name = name, StatusId = index == 1 ? playing.Id : regular.Id, PlatformId = platform,
            SampleBatchId = batch, SampleArtworkKey = index % 3 == 0 ? "amber" : index % 3 == 1 ? "orbit" : "harbor",
            Hero = $"/sample-artwork/{(index % 3 == 0 ? "amber" : index % 3 == 1 ? "orbit" : "harbor")}-hero.svg",
            Cover = $"/sample-artwork/{(index % 3 == 0 ? "amber" : index % 3 == 1 ? "orbit" : "harbor")}-cover.svg",
            Logo = $"/sample-artwork/{(index % 3 == 0 ? "amber" : index % 3 == 1 ? "orbit" : "harbor")}-logo.svg",
            Favorite = index == 2, ManualPlaytimeMinutes = index == 1 ? 0 : null,
            Story = new[] { 4, 12, 8, 2, 18, 6 }[index],
            Comment = "Tutorial example. This fictional game is safe to edit or remove."
        }).ToList();
        context.Games.AddRange(games);
        var listName = "Sample collection";
        if (await context.Playlists.AnyAsync(list => list.UserId == userId && list.Name == listName, cancellationToken)) listName += " " + batch[..8];
        var order = await context.Playlists.Where(list => list.UserId == userId).Select(list => (int?)list.SortOrder).MaxAsync(cancellationToken) ?? -1;
        var playlist = new Playlist { UserId = userId, Name = listName, SampleBatchId = batch, SortOrder = order + 1, Description = "Tutorial examples" };
        playlist.Items = games.Select((game, index) => new PlaylistItem { Game = game, Position = index }).ToList();
        context.Playlists.Add(playlist);
        await AddExampleViewsAsync(userId, request.Locale, cancellationToken);
        state.SampleBatchId = batch;
        state.Step = "library";
        Advance(state);
        try { await context.SaveChangesAsync(cancellationToken); await transaction.CommitAsync(cancellationToken); }
        catch (DbUpdateException) { return CatalogServiceResult<OnboardingStateDto>.ConflictResult("Sample creation could not be completed. Reload before retrying."); }
        return CatalogServiceResult<OnboardingStateDto>.Ok(await GetAsync(userId, cancellationToken));
    }

    // These are normal saved views, deliberately retained when temporary sample
    // games/playlists are removed. The creator marker is informational/idempotent,
    // never an authorization or deletion criterion.
    private async Task AddExampleViewsAsync(int userId, string locale, CancellationToken cancellationToken)
    {
        var existing = await context.GameViews.AsNoTracking().Where(view => view.UserId == userId).ToListAsync(cancellationToken);
        if (existing.Any(view => view.CreatedBy == TutorialViewCreator)) return;
        var names = existing.Select(view => view.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var spanish = locale == "es";
        var labels = spanish ? new[] { "Ejemplo · Favoritos", "Ejemplo · Sin valorar", "Ejemplo · Juegos cortos" } : new[] { "Example · Favorites", "Example · Not rated", "Example · Short games" };
        var descriptions = spanish ? new[] { "Reúne tus juegos favoritos sin moverlos de la biblioteca.", "Encuentra juegos sin nota personal para completarla cuando quieras.", "Muestra juegos con una estimación de historia de diez horas o menos." } : new[] { "Collects your favorite games without moving them out of the library.", "Finds games without a personal grade so you can rate them later.", "Shows games with an estimated main story of ten hours or less." };
        var filters = new[] { new ViewFilter { Field = FilterField.Favorite, Operator = FilterOperator.Equals, Value = true }, new ViewFilter { Field = FilterField.Grade, Operator = FilterOperator.IsNull }, new ViewFilter { Field = FilterField.Story, Operator = FilterOperator.LessThanOrEqual, Value = 10 } };
        var order = existing.Select(view => view.SortOrder).DefaultIfEmpty(0).Max();
        for (var index = 0; index < labels.Length; index++)
        {
            var name = labels[index];
            while (!names.Add(name)) name = labels[index] + " " + Guid.NewGuid().ToString("N")[..8];
            var view = new GameViewCreateDto { Name = name, Description = descriptions[index], CreatedBy = TutorialViewCreator, IsPublic = true, Configuration = new() { FilterGroups = [new() { Filters = [filters[index]] }], Sorting = [new() { Field = SortField.Name, Direction = SortDirection.Ascending, Order = 0 }] } }.ToEntity();
            view.UserId = userId; view.SortOrder = ++order;
            context.GameViews.Add(view);
        }
    }

    public async Task<CatalogServiceResult<OnboardingStateDto>> CreateViewsAsync(int userId, CreateTutorialSamplesRequest request, CancellationToken cancellationToken = default)
    {
        if (!request.Confirm || request.Revision < 0 || request.Locale is not ("en" or "es")) return CatalogServiceResult<OnboardingStateDto>.BadRequest("Confirm view creation explicitly.");
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        if (!await context.Users.AnyAsync(user => user.Id == userId, cancellationToken)) return CatalogServiceResult<OnboardingStateDto>.NotFoundResult("User unavailable.");
        var state = await context.OnboardingStates.SingleOrDefaultAsync(state => state.UserId == userId, cancellationToken);
        if ((state?.Revision ?? 0) != request.Revision) return CatalogServiceResult<OnboardingStateDto>.ConflictResult("Guide changed. Reload before creating views.");
        if (await context.GameViews.AnyAsync(view => view.UserId == userId && view.CreatedBy == TutorialViewCreator, cancellationToken)) return CatalogServiceResult<OnboardingStateDto>.Ok(await GetAsync(userId, cancellationToken));
        state ??= AddState(userId);
        await AddExampleViewsAsync(userId, request.Locale, cancellationToken);
        Advance(state);
        try { await context.SaveChangesAsync(cancellationToken); await transaction.CommitAsync(cancellationToken); }
        catch (DbUpdateException) { return CatalogServiceResult<OnboardingStateDto>.ConflictResult("View creation changed. Reload before retrying."); }
        return CatalogServiceResult<OnboardingStateDto>.Ok(await GetAsync(userId, cancellationToken));
    }

    public async Task<CatalogServiceResult<TutorialSampleChangeDto>> ChangeSamplesAsync(int userId, ChangeTutorialSamplesRequest request, bool remove, CancellationToken cancellationToken = default)
    {
        if (!request.Confirm || request.Revision < 0 || request.ExpectedGames is null || request.ExpectedGames.Count > 64 || request.ExpectedGames.Any(game => game is null || game.Id <= 0) || request.ExpectedGames.Select(game => game.Id).Distinct().Count() != request.ExpectedGames.Count
            || request.ExpectedPlaylists is null || request.ExpectedPlaylists.Count > 64 || request.ExpectedPlaylists.Any(list => list is null || list.Id <= 0) || request.ExpectedPlaylists.Select(list => list.Id).Distinct().Count() != request.ExpectedPlaylists.Count)
            return CatalogServiceResult<TutorialSampleChangeDto>.BadRequest("Review and confirm the exact sample batch.");
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var state = await context.OnboardingStates.SingleOrDefaultAsync(state => state.UserId == userId, cancellationToken);
        if ((state?.Revision ?? 0) != request.Revision) return CatalogServiceResult<TutorialSampleChangeDto>.ConflictResult("Guide changed. Reload the examples before continuing.");
        var batch = state?.SampleBatchId;
        var games = await context.Games.Where(game => batch != null && game.UserId == userId && game.SampleBatchId == batch).ToListAsync(cancellationToken);
        var expected = request.ExpectedGames.ToDictionary(game => game.Id, game => game.UpdatedAt);
        if (games.Count != expected.Count || games.Any(game => !expected.TryGetValue(game.Id, out var stamp) || game.UpdatedAt != stamp))
            return CatalogServiceResult<TutorialSampleChangeDto>.ConflictResult("An example changed. Reload and review it before continuing.");
        if (batch is null) return CatalogServiceResult<TutorialSampleChangeDto>.Ok(new() { State = await GetAsync(userId, cancellationToken) });

        var removeIds = games.Where(game => remove && (request.RemoveEditedSamples || game.UpdatedAt == game.CreatedAt)).Select(game => game.Id).ToHashSet();
        var kept = games.Where(game => !removeIds.Contains(game.Id)).ToList();
        var lists = await context.Playlists.Include(list => list.Items).Where(list => list.UserId == userId && list.SampleBatchId == batch).ToListAsync(cancellationToken);
        var expectedLists = request.ExpectedPlaylists.ToDictionary(list => list.Id, list => list.UpdatedAt);
        if (lists.Count != expectedLists.Count || lists.Any(list => !expectedLists.TryGetValue(list.Id, out var stamp) || list.UpdatedAt != stamp))
            return CatalogServiceResult<TutorialSampleChangeDto>.ConflictResult("An example playlist changed. Reload and review before continuing.");
        var removeLists = lists.Where(list => remove && list.Items.All(item => removeIds.Contains(item.GameId)) && (request.RemoveEditedSamples || list.CreatedAt == list.UpdatedAt)).ToList();
        foreach (var game in kept) game.SampleBatchId = null;
        foreach (var list in lists.Except(removeLists)) list.SampleBatchId = null;
        context.Playlists.RemoveRange(removeLists);
        // A real playlist containing an example is retained. Its remaining item order is normalized.
        var affectedIds = await context.PlaylistItems.Where(item => removeIds.Contains(item.GameId) && item.Playlist.UserId == userId).Select(item => item.PlaylistId).Distinct().ToListAsync(cancellationToken);
        await context.SteamAchievements.Where(item => item.UserId == userId && item.GameId.HasValue && removeIds.Contains(item.GameId.Value)).ExecuteDeleteAsync(cancellationToken);
        await context.SteamAchievementSets.Where(item => item.UserId == userId && item.GameId.HasValue && removeIds.Contains(item.GameId.Value)).ExecuteDeleteAsync(cancellationToken);
        context.Games.RemoveRange(games.Where(game => removeIds.Contains(game.Id)));
        state!.SampleBatchId = null;
        var sampleStatus = state.SampleStatusId;
        state.SampleStatusId = null;
        Advance(state);
        try
        {
            await context.SaveChangesAsync(cancellationToken);
            if (sampleStatus.HasValue && !await context.Games.AnyAsync(game => game.StatusId == sampleStatus && game.UserId == userId, cancellationToken))
                await context.GameStatuses.Where(status => status.Id == sampleStatus && status.UserId == userId).ExecuteDeleteAsync(cancellationToken);
            foreach (var id in affectedIds.Except(removeLists.Select(list => list.Id)))
            {
                var items = await context.PlaylistItems.Where(item => item.PlaylistId == id && item.Playlist.UserId == userId).OrderBy(item => item.Position).ThenBy(item => item.Id).ToListAsync(cancellationToken);
                for (var index = 0; index < items.Count; index++) items[index].Position = index;
            }
            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException) { return CatalogServiceResult<TutorialSampleChangeDto>.ConflictResult("Examples changed. Reload before retrying."); }
        return CatalogServiceResult<TutorialSampleChangeDto>.Ok(new()
        {
            RemovedGames = removeIds.Count, KeptGames = kept.Count, RemovedPlaylists = removeLists.Count, KeptPlaylists = lists.Count - removeLists.Count,
            State = await GetAsync(userId, cancellationToken)
        });
    }
}
