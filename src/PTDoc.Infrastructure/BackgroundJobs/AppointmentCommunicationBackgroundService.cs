using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PTDoc.Application.BackgroundJobs;
using PTDoc.Application.Settings;
using PTDoc.Infrastructure.Data;

namespace PTDoc.Infrastructure.BackgroundJobs;

public sealed class AppointmentCommunicationBackgroundService(
    IServiceScopeFactory scopeFactory,
    ILogger<AppointmentCommunicationBackgroundService> logger) : BackgroundService, IBackgroundJobService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(2);
    private bool schemaNotReadyLogged;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Appointment communication worker started.");
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await ExecuteJobAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception exception) { logger.LogError(exception, "Appointment communication cycle failed."); }

            try { await Task.Delay(Interval, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
        logger.LogInformation("Appointment communication worker stopped.");
    }

    public async Task ExecuteJobAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var schema = await BackgroundJobDatabaseGuard.GetSchemaStatusAsync(context, cancellationToken);
        if (!schema.IsReady)
        {
            if (!schemaNotReadyLogged)
            {
                logger.LogWarning(
                    "Appointment communication worker is waiting for {PendingCount} pending migration(s).",
                    schema.PendingMigrations.Count);
                schemaNotReadyLogged = true;
            }
            return;
        }

        if (schemaNotReadyLogged)
        {
            logger.LogInformation("Appointment communication schema is current; processing resumed.");
            schemaNotReadyLogged = false;
        }

        await scope.ServiceProvider.GetRequiredService<IAppointmentCommunicationProcessor>()
            .ProcessDueAsync(cancellationToken);
    }
}
