using System.Collections.Concurrent;
using System.Threading.Channels;
using GamesDatabase.Api.Application.Interfaces;
using GamesDatabase.Api.Contracts.Steam;
using GamesDatabase.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GamesDatabase.Api.Application.Services.Steam;

/// <summary>Processes only explicitly requested jobs. No schedule, startup sync or automatic retry.</summary>
public sealed class SteamSyncJobService(IServiceScopeFactory scopeFactory, ILogger<SteamSyncJobService> logger)
    : BackgroundService, ISteamSyncJobService
{
    private readonly ConcurrentDictionary<int, Job> jobs = new();
    private readonly object enqueueLock = new();
    private bool stopping;
    private readonly Channel<Job> queue = Channel.CreateBounded<Job>(new BoundedChannelOptions(16)
    {
        SingleReader = true, FullMode = BoundedChannelFullMode.Wait
    });

    public async Task<SteamSyncJobStartResult> StartAsync(int userId, CancellationToken cancellationToken = default)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<GamesDbContext>();
        if (!await context.Users.AnyAsync(user => user.Id == userId && user.SteamId != null && user.SteamId != "", cancellationToken))
            return new(null, "steamNotLinked");
        lock (enqueueLock)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (stopping) return new(null, "serviceStopping");
            if (jobs.TryGetValue(userId, out var existing) && Active(existing.Snapshot().State))
                return new(existing.Snapshot());
            var job = new Job(userId);
            lock (job.Gate)
            {
                if (!queue.Writer.TryWrite(job)) return new(null, "queueFull");
                jobs[userId] = job; // Retain only the latest status per user.
                return new(job.Snapshot());
            }
        }
    }

    public SteamSyncJobDto? GetCurrent(int userId) => jobs.TryGetValue(userId, out var job) ? job.Snapshot() : null;
    public SteamSyncJobDto? GetById(int userId, Guid jobId) => jobs.TryGetValue(userId, out var job) && job.Id == jobId ? job.Snapshot() : null;
    public bool IsActive(int userId) => jobs.TryGetValue(userId, out var job) && job.IsActive();
    private static bool Active(string state) => state is "queued" or "running";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var job in queue.Reader.ReadAllAsync(stoppingToken))
            {
                lock (job.Gate) { job.State = "running"; job.StartedAt = DateTime.UtcNow; }
                try
                {
                    await using var scope = scopeFactory.CreateAsyncScope();
                    var service = scope.ServiceProvider.GetRequiredService<ISteamSyncService>();
                    var result = await service.SyncAllUserGamesAsync(job.UserId, progress => job.Update(progress), stoppingToken);
                    lock (job.Gate)
                    {
                        job.Result = result;
                        job.Progress = new SteamSyncProgress
                        {
                            TotalGames = result.TotalGames, ProcessedGames = result.ProcessedGames,
                            GamesUpdated = result.GamesUpdated, AchievementsUpdated = result.AchievementsUpdated,
                            FailedGames = result.FailedGames, WarningCount = result.WarningCount, InfoCount = result.InfoCount,
                            ReasonCounts = new Dictionary<string, int>(result.ReasonCounts)
                        };
                        job.Issues.Clear();
                        job.Issues.AddRange(result.Issues);
                        job.FinishedAt = DateTime.UtcNow;
                        job.State = result.Success ? "completed" : "failed";
                        job.Error = result.Success ? null : result.Error switch
                        {
                            "steamAccountChanged" => "steamAccountChanged",
                            "User has no Steam account linked" => "steamNotLinked",
                            _ => "syncFailed"
                        };
                    }
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    lock (job.Gate) { job.State = "interrupted"; job.Error = "syncInterrupted"; job.FinishedAt = DateTime.UtcNow; }
                }
                catch (Exception ex)
                {
                    logger.LogWarning("Steam sync job {JobId} failed ({FailureType}); committed progress is retained", job.Id, ex.GetType().Name);
                    lock (job.Gate) { job.State = "failed"; job.Error = "syncFailed"; job.FinishedAt = DateTime.UtcNow; }
                }
                finally
                {
                    lock (job.Gate) { job.Progress = job.Progress with { CurrentGameName = null }; }
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        lock (enqueueLock) { stopping = true; queue.Writer.TryComplete(); }
        await base.StopAsync(cancellationToken);
        foreach (var job in jobs.Values)
        {
            lock (job.Gate)
            {
                if (!Active(job.State)) continue;
                job.State = "interrupted";
                job.Error = "syncInterrupted";
                job.FinishedAt = DateTime.UtcNow;
                job.Progress = job.Progress with { CurrentGameName = null };
            }
        }
    }

    private sealed class Job(int userId)
    {
        public object Gate { get; } = new();
        public int UserId { get; } = userId;
        public Guid Id { get; } = Guid.NewGuid();
        public DateTime CreatedAt { get; } = DateTime.UtcNow;
        public DateTime? StartedAt;
        public DateTime? FinishedAt;
        public string State = "queued";
        public string? Error;
        public SteamSyncResult? Result;
        public SteamSyncProgress Progress = new();
        public List<SteamSyncIssueDto> Issues { get; } = [];
        public bool IsActive() { lock (Gate) return Active(State); }
        public void Update(SteamSyncProgress progress)
        {
            lock (Gate)
            {
                Progress = progress;
                if (progress.Issue != null) SteamSyncClassification.Retain(Issues, progress.Issue);
            }
        }
        public SteamSyncJobDto Snapshot()
        {
            lock (Gate) return new()
            {
                Id = Id, State = State, CreatedAt = CreatedAt, StartedAt = StartedAt, FinishedAt = FinishedAt,
                TotalGames = Progress.TotalGames, ProcessedGames = Progress.ProcessedGames,
                GamesUpdated = Progress.GamesUpdated, AchievementsUpdated = Progress.AchievementsUpdated,
                FailedGames = Progress.FailedGames, WarningCount = Progress.WarningCount,
                InfoCount = Progress.InfoCount, ReasonCounts = new Dictionary<string, int>(Progress.ReasonCounts),
                Issues = Active(State) ? Issues.AsEnumerable().Reverse().OrderBy(issue => issue.Severity == "error" ? 0 : issue.Severity == "warning" ? 1 : 2).Take(5).ToList() : [.. Issues],
                IssuesTruncated = (Active(State) ? Math.Min(Issues.Count, 5) : Issues.Count) < Progress.InfoCount + Progress.WarningCount + Progress.FailedGames,
                CurrentGameName = Progress.CurrentGameName, Error = Error,
                Result = Result == null ? null : new SteamSyncResult
                {
                    Success = Result.Success, Error = Result.Error, TotalGames = Result.TotalGames,
                    ProcessedGames = Result.ProcessedGames, GamesUpdated = Result.GamesUpdated,
                    AchievementsUpdated = Result.AchievementsUpdated, FailedGames = Result.FailedGames,
                    WarningCount = Result.WarningCount, InfoCount = Result.InfoCount,
                    ReasonCounts = new Dictionary<string, int>(Result.ReasonCounts), AchievementsSyncSucceeded = Result.AchievementsSyncSucceeded,
                    Warnings = [.. Result.Warnings]
                }
            };
        }
    }
}
