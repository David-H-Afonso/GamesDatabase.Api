using GamesDatabase.Api.Application.Interfaces;

namespace GamesDatabase.Api.Application.Services;

public sealed class MetadataAutofillSignal : IDisposable
{
    private readonly SemaphoreSlim ready = new(0, 1);
    public void Wake() { try { ready.Release(); } catch (SemaphoreFullException) { } }
    public Task<bool> WaitAsync(CancellationToken token) => ready.WaitAsync(TimeSpan.FromSeconds(15), token);
    public void Dispose() => ready.Dispose();
}

/// <summary>Resumes only persisted user-requested work; never discovers/schedules library work.</summary>
public sealed class MetadataAutofillWorker(IServiceScopeFactory scopes, MetadataAutofillSignal signal, ILogger<MetadataAutofillWorker> logger) : BackgroundService
{
    private readonly string workerId = Guid.NewGuid().ToString("N");
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var service = scope.ServiceProvider.GetRequiredService<IMetadataAutofillJobService>();
                if (await service.ProcessNextAsync(workerId, stoppingToken)) await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
                else await signal.WaitAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception error)
            {
                logger.LogWarning("Metadata worker paused after {FailureType}; only durable requested work will resume", error.GetType().Name);
                await signal.WaitAsync(stoppingToken);
            }
        }
    }
}
