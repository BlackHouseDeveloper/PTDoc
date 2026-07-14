using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PTDoc.Application.Integrations;

namespace PTDoc.Infrastructure.Integrations;

/// <summary>
/// Drains provider work from the transactional integration outbox. Each pass uses
/// a fresh scope so EF contexts and provider tokens cannot leak across clinics.
/// </summary>
public sealed class IntegrationOutboxHostedService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IntegrationWorkerOptions _options;
    private readonly ILogger<IntegrationOutboxHostedService> _logger;

    public IntegrationOutboxHostedService(
        IServiceScopeFactory scopeFactory,
        IOptions<IntegrationWorkerOptions> options,
        ILogger<IntegrationOutboxHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("Integration outbox worker is disabled.");
            return;
        }

        using var timer = new PeriodicTimer(NormalizeInterval(_options.PollInterval));
        var recurringDueAt = DateTime.MinValue;
        do
        {
            try
            {
                await using var scope = _scopeFactory.CreateAsyncScope();
                var processor = scope.ServiceProvider.GetRequiredService<IIntegrationJobProcessor>();
                if (DateTime.UtcNow >= recurringDueAt)
                {
                    await processor.EnqueueRecurringWorkAsync(stoppingToken);
                    recurringDueAt = DateTime.UtcNow.AddMinutes(1);
                }
                await processor.ProcessAvailableAsync(Math.Clamp(_options.BatchSize, 1, 50), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Integration outbox worker pass failed.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private static TimeSpan NormalizeInterval(TimeSpan value) =>
        value < TimeSpan.FromSeconds(1) ? TimeSpan.FromSeconds(1) : value;
}
