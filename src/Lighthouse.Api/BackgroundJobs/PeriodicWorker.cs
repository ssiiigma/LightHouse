namespace Lighthouse.Api.BackgroundJobs;

/// <summary>Основа фонових завдань, що виконуються періодично; помилка одного запуску не зупиняє процес.</summary>
public abstract class PeriodicWorker(IServiceScopeFactory scopes, ILogger logger) : BackgroundService
{
    protected abstract string Name { get; }
    protected abstract TimeSpan Period { get; }
    protected virtual TimeSpan InitialDelay => TimeSpan.FromMinutes(1);

    protected abstract Task RunOnceAsync(IServiceProvider services, CancellationToken ct);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(InitialDelay, stoppingToken);
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using var scope = scopes.CreateScope();
                    await RunOnceAsync(scope.ServiceProvider, stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogError(ex, "{Worker} run failed.", Name);
                }

                await Task.Delay(Period, stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
            // зупинка застосунку
        }
    }
}