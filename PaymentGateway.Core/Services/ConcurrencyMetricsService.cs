using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Prometheus;
using System.Collections.Concurrent;

namespace PaymentGateway.Core.Services;

public interface IConcurrencyMetricsService
{
    void RecordPaymentProcessingStart(string paymentId, string operation);
    void RecordPaymentProcessingEnd(string paymentId, string operation, bool success, TimeSpan duration);
    void RecordLockAcquisition(string resource, TimeSpan waitTime, bool success);
    void RecordQueueOperation(string operation, int queueLength, int processingCount);
    void RecordRateLimitHit(string policy, string identifier);
    void RecordConnectionPoolUsage(int totalConnections, int maxConnections, bool isHealthy);
    Task<ConcurrencyReport> GenerateReportAsync();
}

public record ConcurrencyReport(
    int ActivePaymentOperations,
    int TotalProcessedPayments,
    double AverageProcessingTime,
    int ActiveLocks,
    double AverageLockWaitTime,
    int QueueLength,
    int ProcessingCount,
    int RateLimitHits,
    ConnectionPoolUsage ConnectionPool,
    Dictionary<string, OperationMetrics> OperationBreakdown);

public record ConnectionPoolUsage(int Active, int Max, double UsagePercentage, bool IsHealthy);

public record OperationMetrics(
    string Operation,
    int Count,
    double AverageTime,
    int Successes,
    int Failures,
    double SuccessRate);

public class ConcurrencyMetricsOptions
{
    public TimeSpan ReportingInterval { get; set; } = TimeSpan.FromMinutes(1);
    public bool EnableDetailedMetrics { get; set; } = true;
    public int MetricsRetentionHours { get; set; } = 24;
}

public class ConcurrencyMetricsService : IConcurrencyMetricsService
{
    private readonly ILogger<ConcurrencyMetricsService> _logger;
    private readonly ConcurrencyMetricsOptions _options;

    // Prometheus metrics
    private readonly Counter _paymentProcessingTotal;
    private readonly Histogram _paymentProcessingDuration;
    private readonly Gauge _activePaymentOperations;
    private readonly Histogram _lockWaitTime;
    private readonly Counter _lockAcquisitionTotal;
    private readonly Gauge _queueLength;
    private readonly Gauge _processingCount;
    private readonly Counter _rateLimitHits;
    private readonly Gauge _connectionPoolUsage;

    // Internal tracking
    private readonly ConcurrentDictionary<string, DateTime> _activeOperations;
    private readonly ConcurrentDictionary<string, List<double>> _operationTimes;
    private readonly ConcurrentDictionary<string, int> _operationCounts;
    private readonly ConcurrentDictionary<string, int> _operationSuccesses;
    private readonly ConcurrentDictionary<string, int> _operationFailures;

    public ConcurrencyMetricsService(
        ILogger<ConcurrencyMetricsService> logger,
        IOptions<ConcurrencyMetricsOptions> options)
    {
        _logger = logger;
        _options = options.Value;
        _activeOperations = new ConcurrentDictionary<string, DateTime>();
        _operationTimes = new ConcurrentDictionary<string, List<double>>();
        _operationCounts = new ConcurrentDictionary<string, int>();
        _operationSuccesses = new ConcurrentDictionary<string, int>();
        _operationFailures = new ConcurrentDictionary<string, int>();

        // Initialize Prometheus metrics
        _paymentProcessingTotal = Metrics
            .CreateCounter("payment_processing_total", "Total number of payment processing operations", new[] { "operation", "status" });

        _paymentProcessingDuration = Metrics
            .CreateHistogram("payment_processing_duration_seconds", "Payment processing duration in seconds", new[] { "operation" });

        _activePaymentOperations = Metrics
            .CreateGauge("active_payment_operations", "Number of currently active payment operations");

        _lockWaitTime = Metrics
            .CreateHistogram("lock_wait_time_seconds", "Time spent waiting for locks in seconds", new[] { "resource" });

        _lockAcquisitionTotal = Metrics
            .CreateCounter("lock_acquisition_total", "Total lock acquisition attempts", new[] { "resource", "status" });

        _queueLength = Metrics
            .CreateGauge("payment_queue_length", "Current length of payment processing queue");

        _processingCount = Metrics
            .CreateGauge("payment_processing_count", "Current number of payments being processed");

        _rateLimitHits = Metrics
            .CreateCounter("rate_limit_hits_total", "Total number of rate limit hits", new[] { "policy", "identifier_type" });

        _connectionPoolUsage = Metrics
            .CreateGauge("connection_pool_usage_ratio", "Database connection pool usage ratio");
    }

    public void RecordPaymentProcessingStart(string paymentId, string operation)
    {
        ArgumentNullException.ThrowIfNull(paymentId);
        ArgumentNullException.ThrowIfNull(operation);

        _activeOperations.TryAdd($"{paymentId}:{operation}", DateTime.UtcNow);
        _activePaymentOperations.Inc();

        _logger.LogDebug("Started tracking payment operation {Operation} for payment {PaymentId}", operation, paymentId);
    }

    public void RecordPaymentProcessingEnd(string paymentId, string operation, bool success, TimeSpan duration)
    {
        ArgumentNullException.ThrowIfNull(paymentId);
        ArgumentNullException.ThrowIfNull(operation);

        var key = $"{paymentId}:{operation}";
        _activeOperations.TryRemove(key, out _);
        _activePaymentOperations.Dec();

        // Update Prometheus metrics
        _paymentProcessingTotal.WithLabels(operation, success ? "success" : "failure").Inc();
        _paymentProcessingDuration.WithLabels(operation).Observe(duration.TotalSeconds);

        // Update internal tracking
        _operationTimes.AddOrUpdate(operation, 
            new List<double> { duration.TotalMilliseconds },
            (_, list) => 
            {
                lock (list)
                {
                    list.Add(duration.TotalMilliseconds);
                    // Keep only recent measurements
                    if (list.Count > 1000)
                    {
                        list.RemoveRange(0, list.Count - 900);
                    }
                }
                return list;
            });

        _operationCounts.AddOrUpdate(operation, 1, (_, count) => count + 1);

        if (success)
        {
            _operationSuccesses.AddOrUpdate(operation, 1, (_, count) => count + 1);
        }
        else
        {
            _operationFailures.AddOrUpdate(operation, 1, (_, count) => count + 1);
        }

        _logger.LogDebug("Completed payment operation {Operation} for payment {PaymentId} in {Duration}ms (Success: {Success})", 
            operation, paymentId, duration.TotalMilliseconds, success);
    }

    public void RecordLockAcquisition(string resource, TimeSpan waitTime, bool success)
    {
        ArgumentNullException.ThrowIfNull(resource);

        _lockWaitTime.WithLabels(resource).Observe(waitTime.TotalSeconds);
        _lockAcquisitionTotal.WithLabels(resource, success ? "success" : "failure").Inc();

        _logger.LogDebug("Lock acquisition for resource {Resource}: waited {WaitTime}ms, success: {Success}", 
            resource, waitTime.TotalMilliseconds, success);
    }

    public void RecordQueueOperation(string operation, int queueLength, int processingCount)
    {
        ArgumentNullException.ThrowIfNull(operation);

        _queueLength.Set(queueLength);
        _processingCount.Set(processingCount);

        _logger.LogDebug("Queue operation {Operation}: queue length {QueueLength}, processing count {ProcessingCount}", 
            operation, queueLength, processingCount);
    }

    public void RecordRateLimitHit(string policy, string identifier)
    {
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(identifier);

        var identifierType = identifier.StartsWith("team:") ? "team" : 
                            identifier.StartsWith("ip:") ? "ip" : "unknown";

        _rateLimitHits.WithLabels(policy, identifierType).Inc();

        _logger.LogDebug("Rate limit hit for policy {Policy}, identifier type {IdentifierType}", policy, identifierType);
    }

    public void RecordConnectionPoolUsage(int totalConnections, int maxConnections, bool isHealthy)
    {
        var usageRatio = maxConnections > 0 ? (double)totalConnections / maxConnections : 0;
        _connectionPoolUsage.Set(usageRatio);

        _logger.LogDebug("Connection pool usage: {TotalConnections}/{MaxConnections} ({UsageRatio:P1}), healthy: {IsHealthy}", 
            totalConnections, maxConnections, usageRatio, isHealthy);
    }

    public async Task<ConcurrencyReport> GenerateReportAsync()
    {
        var activeOperations = _activeOperations.Count;
        var totalProcessed = _operationCounts.Values.Sum();
        
        var allTimes = _operationTimes.Values.SelectMany(list => 
        {
            lock (list) { return list.ToList(); }
        }).ToList();
        
        var averageProcessingTime = allTimes.Any() ? allTimes.Average() : 0;

        var operationBreakdown = new Dictionary<string, OperationMetrics>();
        foreach (var operation in _operationCounts.Keys)
        {
            var count = _operationCounts.GetValueOrDefault(operation, 0);
            var successes = _operationSuccesses.GetValueOrDefault(operation, 0);
            var failures = _operationFailures.GetValueOrDefault(operation, 0);
            var successRate = count > 0 ? (double)successes / count : 0;

            var times = _operationTimes.GetValueOrDefault(operation, new List<double>());
            var avgTime = times.Any() ? 
                (double)(times.Count > 0 ? times.Sum() / times.Count : 0) : 0;

            operationBreakdown[operation] = new OperationMetrics(
                operation, count, avgTime, successes, failures, successRate);
        }

        var connectionPool = new ConnectionPoolUsage(0, 0, 0, true); // This would be populated from actual pool stats

        var report = new ConcurrencyReport(
            activeOperations,
            totalProcessed,
            averageProcessingTime,
            0, // Active locks would need to be tracked separately
            0, // Average lock wait time would need to be calculated
            0, // Queue length would come from queue service
            0, // Processing count would come from queue service
            0, // Rate limit hits would need to be aggregated
            connectionPool,
            operationBreakdown);

        _logger.LogInformation("Generated concurrency report: {ActiveOperations} active, {TotalProcessed} total processed, {AverageTime}ms avg time",
            activeOperations, totalProcessed, averageProcessingTime);

        return await Task.FromResult(report);
    }
}

public class ConcurrencyMonitoringBackgroundService : BackgroundService
{
    private readonly IConcurrencyMetricsService _metricsService;
    private readonly IConnectionPoolMonitoringService _connectionPoolService;
    private readonly ILogger<ConcurrencyMonitoringBackgroundService> _logger;
    private readonly ConcurrencyMetricsOptions _options;

    public ConcurrencyMonitoringBackgroundService(
        IConcurrencyMetricsService metricsService,
        IConnectionPoolMonitoringService connectionPoolService,
        ILogger<ConcurrencyMonitoringBackgroundService> logger,
        IOptions<ConcurrencyMetricsOptions> options)
    {
        _metricsService = metricsService;
        _connectionPoolService = connectionPoolService;
        _logger = logger;
        _options = options.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Concurrency monitoring service started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // Update connection pool metrics
                var poolStats = await _connectionPoolService.GetConnectionPoolStatsAsync();
                _metricsService.RecordConnectionPoolUsage(
                    poolStats.TotalConnections, 
                    poolStats.MaxPoolSize, 
                    poolStats.IsHealthy);

                // Generate periodic report if detailed metrics are enabled
                if (_options.EnableDetailedMetrics)
                {
                    var report = await _metricsService.GenerateReportAsync();
                    
                    if (report.ActivePaymentOperations > 0 || report.TotalProcessedPayments > 0)
                    {
                        _logger.LogInformation("Concurrency Status: Active Operations: {Active}, Total Processed: {Total}, Avg Time: {AvgTime:F1}ms, Pool Usage: {PoolUsage:P1}",
                            report.ActivePaymentOperations,
                            report.TotalProcessedPayments,
                            report.AverageProcessingTime,
                            report.ConnectionPool.UsagePercentage);
                    }
                }

                await Task.Delay(_options.ReportingInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in concurrency monitoring service");
                await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
            }
        }

        _logger.LogInformation("Concurrency monitoring service stopped");
    }
}