// SPDX-License-Identifier: MIT
// Copyright (c) 2025 HackLoad Payment Gateway

using Microsoft.Extensions.Logging;
using PaymentGateway.Core.Repositories;
using System.Diagnostics;

namespace PaymentGateway.Core.Services;

/// <summary>
/// Database Performance Monitoring Service for tracking query performance and database metrics
/// </summary>
public interface IDatabasePerformanceMonitoringService
{
    Task<Dictionary<string, object>> GetPerformanceMetricsAsync(CancellationToken cancellationToken = default);
    Task<Dictionary<string, object>> GetQueryPerformanceStatisticsAsync(CancellationToken cancellationToken = default);
    Task LogSlowQueryAsync(string queryName, TimeSpan duration, Dictionary<string, object>? parameters = null);
    Task<T> MonitorQueryAsync<T>(string queryName, Func<Task<T>> queryFunc, CancellationToken cancellationToken = default);
}

public class DatabasePerformanceMonitoringService : IDatabasePerformanceMonitoringService
{
    private readonly ILogger<DatabasePerformanceMonitoringService> _logger;
    private readonly IPaymentRepository _paymentRepository;
    
    // Performance thresholds
    private readonly Dictionary<string, TimeSpan> _slowQueryThresholds = new()
    {
        ["GetRecentPayments"] = TimeSpan.FromSeconds(2),
        ["GetPaymentsByTeam"] = TimeSpan.FromSeconds(1),
        ["GetActivePaymentCount"] = TimeSpan.FromMilliseconds(500),
        ["Default"] = TimeSpan.FromSeconds(1)
    };

    // Metrics tracking
    private static readonly System.Diagnostics.Metrics.Meter _meter = new("PaymentGateway.DatabasePerformance");
    private static readonly System.Diagnostics.Metrics.Counter<long> _queryCounter = 
        _meter.CreateCounter<long>("database_queries_total");
    private static readonly System.Diagnostics.Metrics.Histogram<double> _queryDuration = 
        _meter.CreateHistogram<double>("database_query_duration_seconds");
    private static readonly System.Diagnostics.Metrics.Counter<long> _slowQueryCounter = 
        _meter.CreateCounter<long>("database_slow_queries_total");

    public DatabasePerformanceMonitoringService(
        ILogger<DatabasePerformanceMonitoringService> logger,
        IPaymentRepository paymentRepository)
    {
        _logger = logger;
        _paymentRepository = paymentRepository;
    }

    public async Task<Dictionary<string, object>> GetPerformanceMetricsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Getting database performance metrics");
            
            var stopwatch = Stopwatch.StartNew();
            var metrics = await _paymentRepository.GetDatabasePerformanceMetricsAsync(cancellationToken);
            stopwatch.Stop();
            
            // Add monitoring metadata
            metrics["MetricsCollectionDuration"] = stopwatch.ElapsedMilliseconds;
            metrics["CollectedAt"] = DateTime.UtcNow;
            
            _queryDuration.Record(stopwatch.Elapsed.TotalSeconds,
                new KeyValuePair<string, object?>("query_type", "performance_metrics"));
            
            return metrics;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting database performance metrics");
            throw;
        }
    }

    public async Task<Dictionary<string, object>> GetQueryPerformanceStatisticsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var statistics = new Dictionary<string, object>
            {
                ["SlowQueryThresholds"] = _slowQueryThresholds,
                ["MonitoringEnabled"] = true,
                ["LastUpdated"] = DateTime.UtcNow
            };

            // Add database-level performance metrics
            var dbMetrics = await GetPerformanceMetricsAsync(cancellationToken);
            statistics["DatabaseMetrics"] = dbMetrics;

            return statistics;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting query performance statistics");
            throw;
        }
    }

    public async Task LogSlowQueryAsync(string queryName, TimeSpan duration, Dictionary<string, object>? parameters = null)
    {
        try
        {
            var threshold = _slowQueryThresholds.GetValueOrDefault(queryName, _slowQueryThresholds["Default"]);
            
            if (duration > threshold)
            {
                _logger.LogWarning("Slow query detected: {QueryName} took {Duration}ms (threshold: {Threshold}ms). Parameters: {@Parameters}",
                    queryName, duration.TotalMilliseconds, threshold.TotalMilliseconds, parameters);
                
                _slowQueryCounter.Add(1, 
                    new KeyValuePair<string, object?>("query_name", queryName),
                    new KeyValuePair<string, object?>("threshold_exceeded", "true"));
            }
            
            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error logging slow query for {QueryName}", queryName);
        }
    }

    public async Task<T> MonitorQueryAsync<T>(string queryName, Func<Task<T>> queryFunc, CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        
        try
        {
            _logger.LogDebug("Executing monitored query: {QueryName}", queryName);
            
            var result = await queryFunc();
            stopwatch.Stop();
            
            // Record metrics
            _queryCounter.Add(1, 
                new KeyValuePair<string, object?>("query_name", queryName),
                new KeyValuePair<string, object?>("result", "success"));
            
            _queryDuration.Record(stopwatch.Elapsed.TotalSeconds,
                new KeyValuePair<string, object?>("query_name", queryName));
            
            // Check for slow queries
            await LogSlowQueryAsync(queryName, stopwatch.Elapsed);
            
            _logger.LogDebug("Query {QueryName} completed in {Duration}ms", queryName, stopwatch.ElapsedMilliseconds);
            
            return result;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            
            _logger.LogError(ex, "Query {QueryName} failed after {Duration}ms", queryName, stopwatch.ElapsedMilliseconds);
            
            _queryCounter.Add(1, 
                new KeyValuePair<string, object?>("query_name", queryName),
                new KeyValuePair<string, object?>("result", "error"));
            
            _queryDuration.Record(stopwatch.Elapsed.TotalSeconds,
                new KeyValuePair<string, object?>("query_name", queryName),
                new KeyValuePair<string, object?>("error", "true"));
            
            throw;
        }
    }
}