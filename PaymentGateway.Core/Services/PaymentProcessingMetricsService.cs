// SPDX-License-Identifier: MIT
// Copyright (c) 2025 HackLoad Payment Gateway

using PaymentGateway.Core.Entities;
using PaymentGateway.Core.Interfaces;
using PaymentGateway.Core.Repositories;
using PaymentStatus = PaymentGateway.Core.Entities.PaymentStatus;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.Concurrent;
using System.Diagnostics;
using Prometheus;

namespace PaymentGateway.Core.Services;

/// <summary>
/// Comprehensive payment processing metrics and monitoring service
/// </summary>
public interface IPaymentProcessingMetricsService
{
    void RecordPaymentProcessingStarted(long paymentId, PaymentStatus fromStatus);
    void RecordPaymentProcessingCompleted(long paymentId, PaymentStatus toStatus, TimeSpan duration, bool isSuccess);
    void RecordPaymentProcessingFailed(long paymentId, PaymentStatus status, string errorCode, TimeSpan duration);
    void RecordConcurrentProcessingMetrics(int activeCount, int queueLength);
    void RecordPerformanceMetrics(string operation, TimeSpan duration, bool isSuccess);
    void RecordBusinessMetrics(int teamId, decimal amount, string currency, PaymentStatus status);
    Task<ProcessingMetricsSnapshot> GetCurrentMetricsSnapshotAsync(CancellationToken cancellationToken = default);
    Task<ProcessingAnalytics> GetProcessingAnalyticsAsync(int? teamId = null, TimeSpan? period = null, CancellationToken cancellationToken = default);
    Task<IEnumerable<MetricsAlert>> CheckMetricsAlertsAsync(CancellationToken cancellationToken = default);
    void RecordSystemResourceMetrics();
    void RecordDatabaseMetrics(string operation, TimeSpan duration, bool isSuccess, int recordsAffected = 0);
}

public class ProcessingMetricsSnapshot
{
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public int ActiveProcessingCount { get; set; }
    public int QueueLength { get; set; }
    public double ProcessingRate { get; set; } // per second
    public double SuccessRate { get; set; }
    public TimeSpan AverageProcessingTime { get; set; }
    public Dictionary<PaymentStatus, int> PaymentsByStatus { get; set; } = new();
    public Dictionary<string, double> SystemResourceUsage { get; set; } = new();
    public Dictionary<string, int> ErrorCodeFrequency { get; set; } = new();
}

public class ProcessingAnalytics
{
    public TimeSpan Period { get; set; }
    public int TotalPaymentsProcessed { get; set; }
    public int SuccessfulPayments { get; set; }
    public int FailedPayments { get; set; }
    public double OverallSuccessRate { get; set; }
    public TimeSpan AverageProcessingTime { get; set; }
    public TimeSpan MedianProcessingTime { get; set; }
    public TimeSpan P95ProcessingTime { get; set; }
    public double PeakProcessingRate { get; set; }
    public Dictionary<PaymentStatus, ProcessingStatusAnalytics> AnalyticsByStatus { get; set; } = new();
    public Dictionary<int, TeamProcessingAnalytics> AnalyticsByTeam { get; set; } = new();
    public Dictionary<string, ErrorAnalytics> ErrorAnalytics { get; set; } = new();
    public List<PerformanceInsight> PerformanceInsights { get; set; } = new();
}

public class ProcessingStatusAnalytics
{
    public int Count { get; set; }
    public TimeSpan AverageTime { get; set; }
    public double SuccessRate { get; set; }
    public Dictionary<PaymentStatus, int> TransitionsTo { get; set; } = new();
}

public class TeamProcessingAnalytics
{
    public int TeamId { get; set; }
    public string TeamSlug { get; set; }
    public int TotalPayments { get; set; }
    public decimal TotalAmount { get; set; }
    public double SuccessRate { get; set; }
    public TimeSpan AverageProcessingTime { get; set; }
    public Dictionary<string, int> CurrencyBreakdown { get; set; } = new();
}

public class ErrorAnalytics
{
    public string ErrorCode { get; set; }
    public int Frequency { get; set; }
    public double ErrorRate { get; set; }
    public TimeSpan AverageResolutionTime { get; set; }
    public Dictionary<PaymentStatus, int> ErrorsByStatus { get; set; } = new();
}

public class PerformanceInsight
{
    public string Category { get; set; }
    public string Insight { get; set; }
    public string Recommendation { get; set; }
    public double Severity { get; set; } // 0-1
    public Dictionary<string, object> SupportingData { get; set; } = new();
}

public class MetricsAlert
{
    public string AlertId { get; set; }
    public string AlertType { get; set; }
    public string Title { get; set; }
    public string Description { get; set; }
    public AlertSeverity Severity { get; set; }
    public DateTime TriggeredAt { get; set; }
    public Dictionary<string, object> AlertData { get; set; } = new();
}

public enum AlertSeverity
{
    Info,
    Warning,
    Critical
}

public class PaymentProcessingMetricsService : IPaymentProcessingMetricsService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<PaymentProcessingMetricsService> _logger;
    
    // Processing tracking
    private readonly ConcurrentDictionary<long, DateTime> _processingStartTimes = new();
    private readonly ConcurrentDictionary<long, PaymentStatus> _processingStartStatuses = new();
    
    // Performance data collection
    private readonly ConcurrentQueue<ProcessingRecord> _processingRecords = new();
    private readonly ConcurrentDictionary<string, List<double>> _performanceHistory = new();
    
    // System resource tracking - commented out to avoid additional dependencies
    // private readonly PerformanceCounter? _cpuCounter;
    // private readonly PerformanceCounter? _memoryCounter;
    
    // Prometheus metrics
    private static readonly Counter PaymentProcessingTotal = Metrics
        .CreateCounter("payment_processing_total", "Total payment processing operations", new[] { "team_id", "from_status", "to_status", "result" });
    
    private static readonly Histogram PaymentProcessingDuration = Metrics
        .CreateHistogram("payment_processing_duration_seconds", "Payment processing duration", new[] { "team_id", "status", "result" });
    
    private static readonly Gauge ActivePaymentProcessing = Metrics
        .CreateGauge("active_payment_processing", "Currently active payment processing operations");
    
    private static readonly Gauge PaymentProcessingQueueLength = Metrics
        .CreateGauge("payment_processing_queue_length", "Payment processing queue length");
    
    private static readonly Counter PaymentProcessingErrors = Metrics
        .CreateCounter("payment_processing_errors_total", "Total payment processing errors", new[] { "team_id", "error_code", "status" });
    
    private static readonly Gauge PaymentProcessingSuccessRate = Metrics
        .CreateGauge("payment_processing_success_rate", "Payment processing success rate", new[] { "team_id" });
    
    private static readonly Counter BusinessTransactionAmount = Metrics
        .CreateCounter("business_transaction_amount_total", "Total transaction amount", new[] { "team_id", "currency", "status" });
    
    private static readonly Gauge SystemCpuUtilization = Metrics
        .CreateGauge("system_cpu_utilization_percent", "System CPU utilization percentage");
    
    private static readonly Gauge SystemMemoryUtilization = Metrics
        .CreateGauge("system_memory_utilization_percent", "System memory utilization percentage");
    
    private static readonly Histogram DatabaseOperationDuration = Metrics
        .CreateHistogram("database_operation_duration_seconds", "Database operation duration", new[] { "operation", "result" });
    
    private static readonly Counter DatabaseOperationsTotal = Metrics
        .CreateCounter("database_operations_total", "Total database operations", new[] { "operation", "result" });

    private class ProcessingRecord
    {
        public long PaymentId { get; set; }
        public int TeamId { get; set; }
        public PaymentStatus FromStatus { get; set; }
        public PaymentStatus ToStatus { get; set; }
        public TimeSpan Duration { get; set; }
        public bool IsSuccess { get; set; }
        public string ErrorCode { get; set; }
        public DateTime Timestamp { get; set; }
        public decimal Amount { get; set; }
        public string Currency { get; set; }
    }

    public PaymentProcessingMetricsService(
        IServiceProvider serviceProvider,
        ILogger<PaymentProcessingMetricsService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        
        // Performance counters initialization commented out to avoid additional dependencies
        /*
        try
        {
            // Initialize performance counters (Windows-specific)
            if (Environment.OSVersion.Platform == PlatformID.Win32NT)
            {
                _cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
                _memoryCounter = new PerformanceCounter("Memory", "Available MBytes");
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to initialize performance counters");
        }
        */
    }

    public void RecordPaymentProcessingStarted(long paymentId, PaymentStatus fromStatus)
    {
        try
        {
            var startTime = DateTime.UtcNow;
            _processingStartTimes.TryAdd(paymentId, startTime);
            _processingStartStatuses.TryAdd(paymentId, fromStatus);
            
            ActivePaymentProcessing.Inc();
            
            _logger.LogDebug("Payment processing started: {PaymentId}, Status: {FromStatus}", paymentId, fromStatus);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to record payment processing start: {PaymentId}", paymentId);
        }
    }

    public void RecordPaymentProcessingCompleted(long paymentId, PaymentStatus toStatus, TimeSpan duration, bool isSuccess)
    {
        try
        {
            _processingStartTimes.TryRemove(paymentId, out var startTime);
            _processingStartStatuses.TryRemove(paymentId, out var fromStatus);
            
            ActivePaymentProcessing.Dec();
            
            // Get payment details for team information
            Task.Run(async () =>
            {
                try
                {
                    using var scope = _serviceProvider.CreateScope();
                    var paymentRepository = scope.ServiceProvider.GetRequiredService<IPaymentRepository>();
                    var payment = await paymentRepository.GetByIdAsync(paymentId);
                    
                    if (payment != null)
                    {
                        var teamId = payment.TeamId.ToString();
                        
                        // Record Prometheus metrics
                        PaymentProcessingTotal.WithLabels(teamId, fromStatus.ToString(), toStatus.ToString(), isSuccess ? "success" : "failure").Inc();
                        PaymentProcessingDuration.WithLabels(teamId, toStatus.ToString(), isSuccess ? "success" : "failure").Observe(duration.TotalSeconds);
                        
                        // Record processing record for analytics
                        var record = new ProcessingRecord
                        {
                            PaymentId = paymentId,
                            TeamId = payment.TeamId,
                            FromStatus = fromStatus,
                            ToStatus = toStatus,
                            Duration = duration,
                            IsSuccess = isSuccess,
                            Timestamp = DateTime.UtcNow,
                            Amount = payment.Amount,
                            Currency = payment.Currency ?? "RUB"
                        };
                        
                        _processingRecords.Enqueue(record);
                        
                        // Update success rate
                        UpdateSuccessRateMetrics(payment.TeamId);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to complete payment processing metrics: {PaymentId}", paymentId);
                }
            });
            
            _logger.LogDebug("Payment processing completed: {PaymentId}, Status: {ToStatus}, Duration: {Duration}ms, Success: {IsSuccess}", 
                paymentId, toStatus, duration.TotalMilliseconds, isSuccess);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to record payment processing completion: {PaymentId}", paymentId);
        }
    }

    public void RecordPaymentProcessingFailed(long paymentId, PaymentStatus status, string errorCode, TimeSpan duration)
    {
        try
        {
            _processingStartTimes.TryRemove(paymentId, out _);
            _processingStartStatuses.TryRemove(paymentId, out _);
            
            ActivePaymentProcessing.Dec();
            
            Task.Run(async () =>
            {
                try
                {
                    using var scope = _serviceProvider.CreateScope();
                    var paymentRepository = scope.ServiceProvider.GetRequiredService<IPaymentRepository>();
                    var payment = await paymentRepository.GetByIdAsync(paymentId);
                    
                    if (payment != null)
                    {
                        var teamId = payment.TeamId.ToString();
                        
                        PaymentProcessingErrors.WithLabels(teamId, errorCode ?? "UNKNOWN", status.ToString()).Inc();
                        
                        var record = new ProcessingRecord
                        {
                            PaymentId = paymentId,
                            TeamId = payment.TeamId,
                            ToStatus = status,
                            Duration = duration,
                            IsSuccess = false,
                            ErrorCode = errorCode,
                            Timestamp = DateTime.UtcNow,
                            Amount = payment.Amount,
                            Currency = payment.Currency ?? "RUB"
                        };
                        
                        _processingRecords.Enqueue(record);
                        UpdateSuccessRateMetrics(payment.TeamId);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to complete payment processing failure metrics: {PaymentId}", paymentId);
                }
            });
            
            _logger.LogWarning("Payment processing failed: {PaymentId}, Status: {Status}, Error: {ErrorCode}, Duration: {Duration}ms", 
                paymentId, status, errorCode, duration.TotalMilliseconds);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to record payment processing failure: {PaymentId}", paymentId);
        }
    }

    public void RecordConcurrentProcessingMetrics(int activeCount, int queueLength)
    {
        try
        {
            ActivePaymentProcessing.Set(activeCount);
            PaymentProcessingQueueLength.Set(queueLength);
            
            _logger.LogDebug("Concurrent processing metrics: Active: {ActiveCount}, Queue: {QueueLength}", activeCount, queueLength);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to record concurrent processing metrics");
        }
    }

    public void RecordPerformanceMetrics(string operation, TimeSpan duration, bool isSuccess)
    {
        try
        {
            var key = $"{operation}_{(isSuccess ? "success" : "failure")}";
            var performanceList = _performanceHistory.GetOrAdd(key, _ => new List<double>());
            
            lock (performanceList)
            {
                performanceList.Add(duration.TotalMilliseconds);
                
                // Keep only recent data (last 1000 entries)
                if (performanceList.Count > 1000)
                {
                    performanceList.RemoveAt(0);
                }
            }
            
            _logger.LogDebug("Performance metrics recorded: {Operation}, Duration: {Duration}ms, Success: {IsSuccess}", 
                operation, duration.TotalMilliseconds, isSuccess);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to record performance metrics for operation: {Operation}", operation);
        }
    }

    public void RecordBusinessMetrics(int teamId, decimal amount, string currency, PaymentStatus status)
    {
        try
        {
            BusinessTransactionAmount.WithLabels(teamId.ToString(), currency ?? "RUB", status.ToString()).Inc((double)amount);
            
            _logger.LogDebug("Business metrics recorded: Team: {TeamId}, Amount: {Amount} {Currency}, Status: {Status}", 
                teamId, amount, currency, status);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to record business metrics");
        }
    }

    public async Task<ProcessingMetricsSnapshot> GetCurrentMetricsSnapshotAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var snapshot = new ProcessingMetricsSnapshot
            {
                ActiveProcessingCount = _processingStartTimes.Count,
                QueueLength = 0 // This would come from queue service
            };

            // Calculate success rate from recent records
            var recentRecords = GetRecentProcessingRecords(TimeSpan.FromMinutes(5));
            if (recentRecords.Any())
            {
                snapshot.SuccessRate = (double)recentRecords.Count(r => r.IsSuccess) / recentRecords.Count();
                snapshot.AverageProcessingTime = TimeSpan.FromMilliseconds(recentRecords.Average(r => r.Duration.TotalMilliseconds));
                snapshot.ProcessingRate = recentRecords.Count() / 300.0; // per second over 5 minutes
            }

            // Get payment status distribution
            using var scope = _serviceProvider.CreateScope();
            var paymentRepository = scope.ServiceProvider.GetRequiredService<IPaymentRepository>();
            
            var statusCounts = new Dictionary<PaymentStatus, int>();
            foreach (PaymentStatus status in Enum.GetValues<PaymentStatus>())
            {
                var count = await paymentRepository.GetPaymentCountByStatusAsync(status, cancellationToken);
                statusCounts[status] = count;
            }
            snapshot.PaymentsByStatus = statusCounts;

            // System resource usage
            RecordSystemResourceMetrics();
            snapshot.SystemResourceUsage["cpu"] = SystemCpuUtilization.Value;
            snapshot.SystemResourceUsage["memory"] = SystemMemoryUtilization.Value;

            // Error code frequency
            var errorCodes = recentRecords
                .Where(r => !string.IsNullOrEmpty(r.ErrorCode))
                .GroupBy(r => r.ErrorCode)
                .ToDictionary(g => g.Key, g => g.Count());
            snapshot.ErrorCodeFrequency = errorCodes;

            return snapshot;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get current metrics snapshot");
            return new ProcessingMetricsSnapshot();
        }
    }

    public async Task<ProcessingAnalytics> GetProcessingAnalyticsAsync(int? teamId = null, TimeSpan? period = null, CancellationToken cancellationToken = default)
    {
        try
        {
            period ??= TimeSpan.FromHours(24);
            var recentRecords = GetRecentProcessingRecords(period.Value);
            
            if (teamId.HasValue)
            {
                recentRecords = recentRecords.Where(r => r.TeamId == teamId.Value);
            }

            var analytics = new ProcessingAnalytics
            {
                Period = period.Value,
                TotalPaymentsProcessed = recentRecords.Count(),
                SuccessfulPayments = recentRecords.Count(r => r.IsSuccess),
                FailedPayments = recentRecords.Count(r => !r.IsSuccess)
            };

            if (analytics.TotalPaymentsProcessed > 0)
            {
                analytics.OverallSuccessRate = (double)analytics.SuccessfulPayments / analytics.TotalPaymentsProcessed;
                
                var durations = recentRecords.Select(r => r.Duration.TotalMilliseconds).OrderBy(d => d).ToList();
                analytics.AverageProcessingTime = TimeSpan.FromMilliseconds(durations.Average());
                analytics.MedianProcessingTime = TimeSpan.FromMilliseconds(durations[durations.Count / 2]);
                analytics.P95ProcessingTime = TimeSpan.FromMilliseconds(durations[(int)(durations.Count * 0.95)]);
                
                analytics.PeakProcessingRate = CalculatePeakProcessingRate(recentRecords, period.Value);
            }

            // Analytics by status
            var statusGroups = recentRecords.GroupBy(r => r.ToStatus);
            foreach (var group in statusGroups)
            {
                analytics.AnalyticsByStatus[group.Key] = new ProcessingStatusAnalytics
                {
                    Count = group.Count(),
                    AverageTime = TimeSpan.FromMilliseconds(group.Average(r => r.Duration.TotalMilliseconds)),
                    SuccessRate = (double)group.Count(r => r.IsSuccess) / group.Count()
                };
            }

            // Analytics by team
            var teamGroups = recentRecords.GroupBy(r => r.TeamId);
            foreach (var group in teamGroups)
            {
                analytics.AnalyticsByTeam[group.Key] = new TeamProcessingAnalytics
                {
                    TeamId = group.Key,
                    TotalPayments = group.Count(),
                    TotalAmount = group.Sum(r => r.Amount),
                    SuccessRate = (double)group.Count(r => r.IsSuccess) / group.Count(),
                    AverageProcessingTime = TimeSpan.FromMilliseconds(group.Average(r => r.Duration.TotalMilliseconds)),
                    CurrencyBreakdown = group.GroupBy(r => r.Currency).ToDictionary(g => g.Key, g => g.Count())
                };
            }

            // Error analytics
            var errorGroups = recentRecords
                .Where(r => !string.IsNullOrEmpty(r.ErrorCode))
                .GroupBy(r => r.ErrorCode);
            
            foreach (var group in errorGroups)
            {
                analytics.ErrorAnalytics[group.Key] = new ErrorAnalytics
                {
                    ErrorCode = group.Key,
                    Frequency = group.Count(),
                    ErrorRate = (double)group.Count() / analytics.TotalPaymentsProcessed,
                    AverageResolutionTime = TimeSpan.FromMilliseconds(group.Average(r => r.Duration.TotalMilliseconds))
                };
            }

            // Generate performance insights
            analytics.PerformanceInsights = GeneratePerformanceInsights(analytics);

            return analytics;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get processing analytics");
            return new ProcessingAnalytics();
        }
    }

    public async Task<IEnumerable<MetricsAlert>> CheckMetricsAlertsAsync(CancellationToken cancellationToken = default)
    {
        var alerts = new List<MetricsAlert>();
        
        try
        {
            var snapshot = await GetCurrentMetricsSnapshotAsync(cancellationToken);
            
            // High error rate alert
            if (snapshot.SuccessRate < 0.95)
            {
                alerts.Add(new MetricsAlert
                {
                    AlertId = "high_error_rate",
                    AlertType = "ProcessingQuality",
                    Title = "High Payment Processing Error Rate",
                    Description = $"Current success rate is {snapshot.SuccessRate:P2}, below threshold of 95%",
                    Severity = snapshot.SuccessRate < 0.90 ? AlertSeverity.Critical : AlertSeverity.Warning,
                    TriggeredAt = DateTime.UtcNow,
                    AlertData = new Dictionary<string, object> { ["success_rate"] = snapshot.SuccessRate }
                });
            }
            
            // High processing time alert
            if (snapshot.AverageProcessingTime > TimeSpan.FromSeconds(10))
            {
                alerts.Add(new MetricsAlert
                {
                    AlertId = "high_processing_time",
                    AlertType = "Performance",
                    Title = "High Average Processing Time",
                    Description = $"Average processing time is {snapshot.AverageProcessingTime.TotalSeconds:F1}s, above threshold",
                    Severity = snapshot.AverageProcessingTime > TimeSpan.FromSeconds(20) ? AlertSeverity.Critical : AlertSeverity.Warning,
                    TriggeredAt = DateTime.UtcNow,
                    AlertData = new Dictionary<string, object> { ["avg_processing_time"] = snapshot.AverageProcessingTime.TotalSeconds }
                });
            }
            
            // High system resource usage alerts
            if (snapshot.SystemResourceUsage.TryGetValue("cpu", out var cpuUsage) && cpuUsage > 80)
            {
                alerts.Add(new MetricsAlert
                {
                    AlertId = "high_cpu_usage",
                    AlertType = "System",
                    Title = "High CPU Usage",
                    Description = $"CPU usage is {cpuUsage:F1}%, above threshold of 80%",
                    Severity = cpuUsage > 90 ? AlertSeverity.Critical : AlertSeverity.Warning,
                    TriggeredAt = DateTime.UtcNow,
                    AlertData = new Dictionary<string, object> { ["cpu_usage"] = cpuUsage }
                });
            }
            
            return alerts;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to check metrics alerts");
            return alerts;
        }
    }

    public void RecordSystemResourceMetrics()
    {
        try
        {
            // Performance counters commented out to avoid additional dependencies
            // Placeholder implementation - in real scenarios would use appropriate monitoring
            SystemCpuUtilization.Set(Random.Shared.NextDouble() * 50); // Simulated value
            SystemMemoryUtilization.Set(Random.Shared.NextDouble() * 60); // Simulated value
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to record system resource metrics");
        }
    }

    public void RecordDatabaseMetrics(string operation, TimeSpan duration, bool isSuccess, int recordsAffected = 0)
    {
        try
        {
            var result = isSuccess ? "success" : "failure";
            DatabaseOperationDuration.WithLabels(operation, result).Observe(duration.TotalSeconds);
            DatabaseOperationsTotal.WithLabels(operation, result).Inc();
            
            _logger.LogDebug("Database metrics recorded: {Operation}, Duration: {Duration}ms, Success: {IsSuccess}, Records: {RecordsAffected}", 
                operation, duration.TotalMilliseconds, isSuccess, recordsAffected);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to record database metrics for operation: {Operation}", operation);
        }
    }

    private void UpdateSuccessRateMetrics(int teamId)
    {
        try
        {
            var recentRecords = GetRecentProcessingRecords(TimeSpan.FromMinutes(5))
                .Where(r => r.TeamId == teamId);
            
            if (recentRecords.Any())
            {
                var successRate = (double)recentRecords.Count(r => r.IsSuccess) / recentRecords.Count();
                PaymentProcessingSuccessRate.WithLabels(teamId.ToString()).Set(successRate);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update success rate metrics for team: {TeamId}", teamId);
        }
    }

    private IEnumerable<ProcessingRecord> GetRecentProcessingRecords(TimeSpan period)
    {
        var cutoff = DateTime.UtcNow - period;
        var records = new List<ProcessingRecord>();
        
        // Drain queue and collect recent records
        while (_processingRecords.TryDequeue(out var record))
        {
            if (record.Timestamp >= cutoff)
            {
                records.Add(record);
            }
        }
        
        // Re-enqueue recent records
        foreach (var record in records)
        {
            _processingRecords.Enqueue(record);
        }
        
        return records;
    }

    private double CalculatePeakProcessingRate(IEnumerable<ProcessingRecord> records, TimeSpan period)
    {
        // Group by minute and find peak
        var minuteGroups = records
            .GroupBy(r => new DateTime(r.Timestamp.Year, r.Timestamp.Month, r.Timestamp.Day, r.Timestamp.Hour, r.Timestamp.Minute, 0))
            .Select(g => g.Count() / 60.0); // per second
        
        return minuteGroups.DefaultIfEmpty(0).Max();
    }

    private List<PerformanceInsight> GeneratePerformanceInsights(ProcessingAnalytics analytics)
    {
        var insights = new List<PerformanceInsight>();
        
        // Success rate insight
        if (analytics.OverallSuccessRate < 0.95)
        {
            insights.Add(new PerformanceInsight
            {
                Category = "Quality",
                Insight = $"Success rate is {analytics.OverallSuccessRate:P2}, below recommended 95%",
                Recommendation = "Review error patterns and improve error handling",
                Severity = analytics.OverallSuccessRate < 0.9 ? 0.8 : 0.5
            });
        }
        
        // Processing time insight
        if (analytics.AverageProcessingTime > TimeSpan.FromSeconds(5))
        {
            insights.Add(new PerformanceInsight
            {
                Category = "Performance",
                Insight = $"Average processing time is {analytics.AverageProcessingTime.TotalSeconds:F1}s, above optimal range",
                Recommendation = "Consider optimizing database queries and adding caching",
                Severity = analytics.AverageProcessingTime > TimeSpan.FromSeconds(10) ? 0.7 : 0.4
            });
        }
        
        return insights;
    }

    public void Dispose()
    {
        // Performance counters disposal commented out
        // _cpuCounter?.Dispose();
        // _memoryCounter?.Dispose();
    }
}