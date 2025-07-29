using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace PaymentGateway.Infrastructure.Data.Migrations;

public class MigrationMonitoringService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<MigrationMonitoringService> _logger;
    private readonly TimeSpan _checkInterval = TimeSpan.FromMinutes(30);

    public MigrationMonitoringService(IServiceProvider serviceProvider, ILogger<MigrationMonitoringService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CheckMigrationStatusAsync();
                await Task.Delay(_checkInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                // Expected when cancellation is requested
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in migration monitoring service");
                await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken); // Wait before retrying
            }
        }
    }

    private async Task CheckMigrationStatusAsync()
    {
        using var scope = _serviceProvider.CreateScope();
        var migrationRunner = scope.ServiceProvider.GetRequiredService<MigrationRunner>();

        try
        {
            var migrationInfo = await migrationRunner.GetMigrationInfoAsync();

            if (!migrationInfo.CanConnect)
            {
                _logger.LogWarning("Database connection check failed during migration monitoring");
                return;
            }

            if (migrationInfo.TotalPendingCount > 0)
            {
                _logger.LogWarning("Found {Count} pending migrations: {Migrations}", 
                    migrationInfo.TotalPendingCount, 
                    string.Join(", ", migrationInfo.PendingMigrations));
            }
            else
            {
                _logger.LogDebug("Migration status check: All migrations are up to date");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking migration status");
        }
    }
}