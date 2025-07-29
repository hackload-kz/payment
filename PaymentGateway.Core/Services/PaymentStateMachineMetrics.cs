using Microsoft.Extensions.Logging;
using PaymentGateway.Core.Entities;
using PaymentGateway.Core.Enums;
using System.Collections.Concurrent;
using System.Diagnostics.Metrics;

namespace PaymentGateway.Core.Services;

public interface IPaymentStateMachineMetrics
{
    Task RecordTransitionAsync(PaymentStatus fromStatus, PaymentStatus toStatus, bool isSuccess, CancellationToken cancellationToken = default);
    Task RecordTransitionErrorAsync(PaymentStatus fromStatus, PaymentStatus toStatus, string errorType, CancellationToken cancellationToken = default);
    Task RecordTransitionDurationAsync(PaymentStatus fromStatus, PaymentStatus toStatus, TimeSpan duration, CancellationToken cancellationToken = default);
    Task RecordRollbackAsync(PaymentStatus fromStatus, PaymentStatus toStatus, CancellationToken cancellationToken = default);
    Task<PaymentStateMachineMetricsSnapshot> GetMetricsSnapshotAsync(CancellationToken cancellationToken = default);
}

public class PaymentStateMachineMetricsSnapshot
{
    public DateTime SnapshotTime { get; set; } = DateTime.UtcNow;
    public long TotalTransitions { get; set; }
    public long SuccessfulTransitions { get; set; }
    public long FailedTransitions { get; set; }
    public long TotalRollbacks { get; set; }
    public double SuccessRate => TotalTransitions > 0 ? (double)SuccessfulTransitions / TotalTransitions : 0;
    public Dictionary<PaymentStatus, long> TransitionsToStatus { get; set; } = new();
    public Dictionary<PaymentStatus, long> TransitionsFromStatus { get; set; } = new();
    public Dictionary<string, long> ErrorsByType { get; set; } = new();
    public Dictionary<string, TimeSpan> AverageTransitionDurations { get; set; } = new();
}

public class PaymentStateMachineMetrics : IPaymentStateMachineMetrics
{
    private readonly ILogger<PaymentStateMachineMetrics> _logger;
    private readonly Meter _meter;
    
    // Counters
    private readonly Counter<long> _transitionCounter;
    private readonly Counter<long> _transitionErrorCounter;
    private readonly Counter<long> _rollbackCounter;
    
    // Histograms
    private readonly Histogram<double> _transitionDurationHistogram;
    
    // In-memory metrics for snapshot (in production, use proper metrics store)
    private readonly ConcurrentDictionary<string, long> _transitionCounts = new();
    private readonly ConcurrentDictionary<string, long> _errorCounts = new();
    private readonly ConcurrentDictionary<string, List<TimeSpan>> _transitionDurations = new();
    
    private long _totalTransitions = 0;
    private long _successfulTransitions = 0;
    private long _failedTransitions = 0;
    private long _totalRollbacks = 0;

    public PaymentStateMachineMetrics(ILogger<PaymentStateMachineMetrics> logger)
    {
        _logger = logger;
        _meter = new Meter("PaymentGateway.StateMachine");
        
        // Initialize counters
        _transitionCounter = _meter.CreateCounter<long>(
            "payment_state_transitions_total",
            description: "Total number of payment state transitions");
            
        _transitionErrorCounter = _meter.CreateCounter<long>(
            "payment_state_transition_errors_total",
            description: "Total number of payment state transition errors");
            
        _rollbackCounter = _meter.CreateCounter<long>(
            "payment_state_rollbacks_total",
            description: "Total number of payment state rollbacks");
            
        // Initialize histograms
        _transitionDurationHistogram = _meter.CreateHistogram<double>(
            "payment_state_transition_duration_seconds",
            unit: "s",
            description: "Duration of payment state transitions in seconds");
    }

    public async Task RecordTransitionAsync(PaymentStatus fromStatus, PaymentStatus toStatus, bool isSuccess, CancellationToken cancellationToken = default)
    {
        try
        {
            var transitionKey = $"{fromStatus}->{toStatus}";
            var tags = new[]
            {
                new KeyValuePair<string, object?>("from_status", fromStatus.ToString()),
                new KeyValuePair<string, object?>("to_status", toStatus.ToString()),
                new KeyValuePair<string, object?>("success", isSuccess.ToString())
            };

            // Record metrics
            _transitionCounter.Add(1, tags);
            
            // Update in-memory counters
            Interlocked.Increment(ref _totalTransitions);
            _transitionCounts.AddOrUpdate(transitionKey, 1, (key, value) => value + 1);
            _transitionCounts.AddOrUpdate($"to_{toStatus}", 1, (key, value) => value + 1);
            _transitionCounts.AddOrUpdate($"from_{fromStatus}", 1, (key, value) => value + 1);

            if (isSuccess)
            {
                Interlocked.Increment(ref _successfulTransitions);
            }
            else
            {
                Interlocked.Increment(ref _failedTransitions);
            }

            _logger.LogDebug("Recorded transition metric: {FromStatus} -> {ToStatus}, Success: {IsSuccess}",
                fromStatus, toStatus, isSuccess);

            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error recording transition metric for {FromStatus} -> {ToStatus}",
                fromStatus, toStatus);
        }
    }

    public async Task RecordTransitionErrorAsync(PaymentStatus fromStatus, PaymentStatus toStatus, string errorType, CancellationToken cancellationToken = default)
    {
        try
        {
            var tags = new[]
            {
                new KeyValuePair<string, object?>("from_status", fromStatus.ToString()),
                new KeyValuePair<string, object?>("to_status", toStatus.ToString()),
                new KeyValuePair<string, object?>("error_type", errorType)
            };

            _transitionErrorCounter.Add(1, tags);
            
            // Update in-memory error counts
            var errorKey = $"{fromStatus}->{toStatus}:{errorType}";
            _errorCounts.AddOrUpdate(errorKey, 1, (key, value) => value + 1);
            _errorCounts.AddOrUpdate(errorType, 1, (key, value) => value + 1);

            _logger.LogDebug("Recorded transition error metric: {FromStatus} -> {ToStatus}, Error: {ErrorType}",
                fromStatus, toStatus, errorType);

            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error recording transition error metric for {FromStatus} -> {ToStatus}",
                fromStatus, toStatus);
        }
    }

    public async Task RecordTransitionDurationAsync(PaymentStatus fromStatus, PaymentStatus toStatus, TimeSpan duration, CancellationToken cancellationToken = default)
    {
        try
        {
            var tags = new[]
            {
                new KeyValuePair<string, object?>("from_status", fromStatus.ToString()),
                new KeyValuePair<string, object?>("to_status", toStatus.ToString())
            };

            _transitionDurationHistogram.Record(duration.TotalSeconds, tags);
            
            // Update in-memory duration tracking
            var transitionKey = $"{fromStatus}->{toStatus}";
            _transitionDurations.AddOrUpdate(transitionKey, 
                new List<TimeSpan> { duration },
                (key, list) => 
                {
                    lock (list)
                    {
                        list.Add(duration);
                        // Keep only last 1000 entries to prevent memory growth
                        if (list.Count > 1000)
                        {
                            list.RemoveAt(0);
                        }
                        return list;
                    }
                });

            _logger.LogDebug("Recorded transition duration metric: {FromStatus} -> {ToStatus}, Duration: {Duration}ms",
                fromStatus, toStatus, duration.TotalMilliseconds);

            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error recording transition duration metric for {FromStatus} -> {ToStatus}",
                fromStatus, toStatus);
        }
    }

    public async Task RecordRollbackAsync(PaymentStatus fromStatus, PaymentStatus toStatus, CancellationToken cancellationToken = default)
    {
        try
        {
            var tags = new[]
            {
                new KeyValuePair<string, object?>("from_status", fromStatus.ToString()),
                new KeyValuePair<string, object?>("to_status", toStatus.ToString())
            };

            _rollbackCounter.Add(1, tags);
            Interlocked.Increment(ref _totalRollbacks);

            _logger.LogDebug("Recorded rollback metric: {FromStatus} -> {ToStatus}", fromStatus, toStatus);

            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error recording rollback metric for {FromStatus} -> {ToStatus}",
                fromStatus, toStatus);
        }
    }

    public async Task<PaymentStateMachineMetricsSnapshot> GetMetricsSnapshotAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var snapshot = new PaymentStateMachineMetricsSnapshot
            {
                TotalTransitions = _totalTransitions,
                SuccessfulTransitions = _successfulTransitions,
                FailedTransitions = _failedTransitions,
                TotalRollbacks = _totalRollbacks
            };

            // Build status-specific counts
            foreach (var kvp in _transitionCounts)
            {
                if (kvp.Key.StartsWith("to_"))
                {
                    var statusName = kvp.Key.Substring(3);
                    if (Enum.TryParse<PaymentStatus>(statusName, out var status))
                    {
                        snapshot.TransitionsToStatus[status] = kvp.Value;
                    }
                }
                else if (kvp.Key.StartsWith("from_"))
                {
                    var statusName = kvp.Key.Substring(5);
                    if (Enum.TryParse<PaymentStatus>(statusName, out var status))
                    {
                        snapshot.TransitionsFromStatus[status] = kvp.Value;
                    }
                }
            }

            // Build error counts
            foreach (var kvp in _errorCounts)
            {
                if (!kvp.Key.Contains("->")) // Only include error type totals, not specific transitions
                {
                    snapshot.ErrorsByType[kvp.Key] = kvp.Value;
                }
            }

            // Calculate average durations
            foreach (var kvp in _transitionDurations)
            {
                lock (kvp.Value)
                {
                    if (kvp.Value.Any())
                    {
                        var avgTicks = (long)kvp.Value.Average(ts => ts.Ticks);
                        snapshot.AverageTransitionDurations[kvp.Key] = new TimeSpan(avgTicks);
                    }
                }
            }

            _logger.LogDebug("Generated metrics snapshot with {TotalTransitions} total transitions",
                snapshot.TotalTransitions);

            return snapshot;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating metrics snapshot");
            return new PaymentStateMachineMetricsSnapshot();
        }
    }

    public void Dispose()
    {
        _meter?.Dispose();
    }
}