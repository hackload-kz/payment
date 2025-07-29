using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PaymentGateway.Core.Configuration;
using PaymentGateway.Infrastructure.Data;

namespace PaymentGateway.Infrastructure.Services;

public interface ILogRetentionService
{
    Task CleanupOldLogsAsync(CancellationToken cancellationToken = default);
    Task CleanupOldFileLogsAsync(CancellationToken cancellationToken = default);
    Task CleanupOldDatabaseLogsAsync(CancellationToken cancellationToken = default);
    Task CleanupOldAuditLogsAsync(CancellationToken cancellationToken = default);
}

public class LogRetentionService : ILogRetentionService
{
    private readonly ILogger<LogRetentionService> _logger;
    private readonly RetentionConfiguration _retentionConfig;
    private readonly IServiceScopeFactory _serviceScopeFactory;

    public LogRetentionService(
        ILogger<LogRetentionService> logger,
        RetentionConfiguration retentionConfig,
        IServiceScopeFactory serviceScopeFactory)
    {
        _logger = logger;
        _retentionConfig = retentionConfig;
        _serviceScopeFactory = serviceScopeFactory;
    }

    public async Task CleanupOldLogsAsync(CancellationToken cancellationToken = default)
    {
        if (!_retentionConfig.EnableAutoCleanup)
        {
            _logger.LogDebug("Log cleanup is disabled in configuration");
            return;
        }

        _logger.LogInformation("Starting log cleanup process");

        try
        {
            await CleanupOldFileLogsAsync(cancellationToken);
            await CleanupOldDatabaseLogsAsync(cancellationToken);
            await CleanupOldAuditLogsAsync(cancellationToken);

            _logger.LogInformation("Log cleanup process completed successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error occurred during log cleanup process");
            throw;
        }
    }

    public async Task CleanupOldFileLogsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var logDirectory = Path.Combine(AppContext.BaseDirectory, "logs");
            if (!Directory.Exists(logDirectory))
            {
                _logger.LogDebug("Log directory does not exist: {LogDirectory}", logDirectory);
                return;
            }

            var cutoffDate = DateTime.UtcNow.AddDays(-_retentionConfig.FileRetentionDays);
            var logFiles = Directory.GetFiles(logDirectory, "*.log")
                .Concat(Directory.GetFiles(logDirectory, "*.json"))
                .Where(file => File.GetCreationTimeUtc(file) < cutoffDate)
                .ToList();

            var deletedCount = 0;
            foreach (var file in logFiles)
            {
                try
                {
                    File.Delete(file);
                    deletedCount++;
                    _logger.LogDebug("Deleted old log file: {FileName}", Path.GetFileName(file));
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to delete log file: {FileName}", Path.GetFileName(file));
                }
            }

            if (deletedCount > 0)
            {
                _logger.LogInformation("Deleted {DeletedCount} old log files older than {RetentionDays} days",
                    deletedCount, _retentionConfig.FileRetentionDays);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during file log cleanup");
            throw;
        }

        await Task.CompletedTask;
    }

    public async Task CleanupOldDatabaseLogsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var scope = _serviceScopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<PaymentGatewayDbContext>();

            var cutoffDate = DateTime.UtcNow.AddDays(-_retentionConfig.DatabaseRetentionDays);

            // Delete old log entries from the logs table
            var deletedCount = await dbContext.Database.ExecuteSqlRawAsync(
                @"DELETE FROM logs WHERE timestamp < {0}",
                cutoffDate,
                cancellationToken);

            if (deletedCount > 0)
            {
                _logger.LogInformation("Deleted {DeletedCount} old database log entries older than {RetentionDays} days",
                    deletedCount, _retentionConfig.DatabaseRetentionDays);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during database log cleanup");
            throw;
        }
    }

    public async Task CleanupOldAuditLogsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var scope = _serviceScopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<PaymentGatewayDbContext>();

            var cutoffDate = DateTime.UtcNow.AddDays(-_retentionConfig.AuditRetentionDays);

            // Delete old audit log entries
            var deletedCount = await dbContext.AuditLogs
                .Where(al => al.CreatedAt < cutoffDate)
                .ExecuteDeleteAsync(cancellationToken);

            if (deletedCount > 0)
            {
                _logger.LogInformation("Deleted {DeletedCount} old audit log entries older than {RetentionDays} days",
                    deletedCount, _retentionConfig.AuditRetentionDays);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during audit log cleanup");
            throw;
        }
    }
}

public class LogRetentionBackgroundService : BackgroundService
{
    private readonly ILogger<LogRetentionBackgroundService> _logger;
    private readonly ILogRetentionService _logRetentionService;
    private readonly RetentionConfiguration _retentionConfig;
    private readonly TimeSpan _cleanupInterval;

    public LogRetentionBackgroundService(
        ILogger<LogRetentionBackgroundService> logger,
        ILogRetentionService logRetentionService,
        RetentionConfiguration retentionConfig)
    {
        _logger = logger;
        _logRetentionService = logRetentionService;
        _retentionConfig = retentionConfig;
        
        // Parse cron expression to determine interval (defaulting to daily)
        _cleanupInterval = TimeSpan.FromHours(24);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_retentionConfig.EnableAutoCleanup)
        {
            _logger.LogInformation("Log retention background service is disabled");
            return;
        }

        _logger.LogInformation("Log retention background service started. Cleanup interval: {Interval}",
            _cleanupInterval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await _logRetentionService.CleanupOldLogsAsync(stoppingToken);
                
                await Task.Delay(_cleanupInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("Log retention background service is stopping");
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in log retention background service");
                
                // Wait a shorter interval before retrying after an error
                await Task.Delay(TimeSpan.FromMinutes(30), stoppingToken);
            }
        }
    }
}