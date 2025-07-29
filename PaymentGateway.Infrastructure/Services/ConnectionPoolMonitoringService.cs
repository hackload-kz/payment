using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using System.Data;

namespace PaymentGateway.Infrastructure.Services;

public interface IConnectionPoolMonitoringService
{
    Task<ConnectionPoolStats> GetConnectionPoolStatsAsync();
    Task<bool> IsConnectionPoolHealthyAsync();
}

public record ConnectionPoolStats(
    int TotalConnections,
    int AvailableConnections,
    int BusyConnections,
    int MinPoolSize,
    int MaxPoolSize,
    TimeSpan AverageConnectionTime,
    int ConnectionsCreated,
    int ConnectionsDestroyed,
    bool IsHealthy);

public class ConnectionPoolOptions
{
    public TimeSpan MonitoringInterval { get; set; } = TimeSpan.FromMinutes(1);
    public int HealthThresholdPercentage { get; set; } = 80; // Alert when pool usage exceeds this percentage
    public bool EnableDetailedLogging { get; set; } = false;
}

public class ConnectionPoolMonitoringService : IConnectionPoolMonitoringService
{
    private readonly string _connectionString;
    private readonly ILogger<ConnectionPoolMonitoringService> _logger;
    private readonly ConnectionPoolOptions _options;

    public ConnectionPoolMonitoringService(
        IConfiguration configuration,
        ILogger<ConnectionPoolMonitoringService> logger,
        IOptions<ConnectionPoolOptions> options)
    {
        _connectionString = configuration.GetConnectionString("DefaultConnection") ??
            throw new InvalidOperationException("Database connection string not found.");
        _logger = logger;
        _options = options.Value;
    }

    public async Task<ConnectionPoolStats> GetConnectionPoolStatsAsync()
    {
        try
        {
            var builder = new NpgsqlConnectionStringBuilder(_connectionString);
            var totalConnections = 0;
            var busyConnections = 0;
            var availableConnections = 0;
            var connectionsCreated = 0;
            var connectionsDestroyed = 0;
            var averageConnectionTime = TimeSpan.Zero;

            using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync();

            // Query PostgreSQL statistics
            var query = @"
                SELECT 
                    COUNT(*) as total_connections,
                    COUNT(CASE WHEN state = 'active' THEN 1 END) as busy_connections,
                    COUNT(CASE WHEN state = 'idle' THEN 1 END) as available_connections
                FROM pg_stat_activity 
                WHERE datname = current_database()";

            using var command = new NpgsqlCommand(query, connection);
            using var reader = await command.ExecuteReaderAsync();

            if (await reader.ReadAsync())
            {
                totalConnections = reader.GetInt32("total_connections");
                busyConnections = reader.GetInt32("busy_connections");
                availableConnections = reader.GetInt32("available_connections");
            }

            var isHealthy = totalConnections < (builder.MaxPoolSize * _options.HealthThresholdPercentage / 100);

            var stats = new ConnectionPoolStats(
                totalConnections,
                availableConnections,
                busyConnections,
                builder.MinPoolSize,
                builder.MaxPoolSize,
                averageConnectionTime,
                connectionsCreated,
                connectionsDestroyed,
                isHealthy);

            if (_options.EnableDetailedLogging)
            {
                _logger.LogDebug("Connection Pool Stats: Total={Total}, Available={Available}, Busy={Busy}, MaxPool={MaxPool}",
                    totalConnections, availableConnections, busyConnections, builder.MaxPoolSize);
            }

            return stats;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get connection pool statistics");
            return new ConnectionPoolStats(0, 0, 0, 0, 0, TimeSpan.Zero, 0, 0, false);
        }
    }

    public async Task<bool> IsConnectionPoolHealthyAsync()
    {
        try
        {
            var stats = await GetConnectionPoolStatsAsync();
            return stats.IsHealthy;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to check connection pool health");
            return false;
        }
    }
}

public class ConnectionPoolMonitoringBackgroundService : BackgroundService
{
    private readonly IConnectionPoolMonitoringService _connectionPoolService;
    private readonly ILogger<ConnectionPoolMonitoringBackgroundService> _logger;
    private readonly ConnectionPoolOptions _options;

    public ConnectionPoolMonitoringBackgroundService(
        IConnectionPoolMonitoringService connectionPoolService,
        ILogger<ConnectionPoolMonitoringBackgroundService> logger,
        IOptions<ConnectionPoolOptions> options)
    {
        _connectionPoolService = connectionPoolService;
        _logger = logger;
        _options = options.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Connection pool monitoring service started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var stats = await _connectionPoolService.GetConnectionPoolStatsAsync();

                if (!stats.IsHealthy)
                {
                    var usagePercentage = (double)stats.TotalConnections / stats.MaxPoolSize * 100;
                    _logger.LogWarning("Connection pool usage is high: {Usage:F1}% ({Total}/{MaxPool})",
                        usagePercentage, stats.TotalConnections, stats.MaxPoolSize);
                }

                if (stats.TotalConnections == stats.MaxPoolSize)
                {
                    _logger.LogError("Connection pool exhausted! All {MaxPool} connections are in use",
                        stats.MaxPoolSize);
                }

                if (_options.EnableDetailedLogging || !stats.IsHealthy)
                {
                    _logger.LogInformation("Connection Pool Status: Total={Total}, Available={Available}, Busy={Busy}, Health={IsHealthy}",
                        stats.TotalConnections, stats.AvailableConnections, stats.BusyConnections, stats.IsHealthy);
                }

                await Task.Delay(_options.MonitoringInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in connection pool monitoring service");
                await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
            }
        }

        _logger.LogInformation("Connection pool monitoring service stopped");
    }
}