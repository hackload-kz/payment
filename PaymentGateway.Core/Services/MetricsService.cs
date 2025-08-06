using Microsoft.Extensions.Logging;
using PaymentGateway.Core.Interfaces;
using System.Collections.Concurrent;

namespace PaymentGateway.Core.Services;

public class MetricsService : IMetricsService
{
    private readonly ILogger<MetricsService> _logger;
    private readonly ConcurrentDictionary<string, long> _counters = new();
    private readonly ConcurrentDictionary<string, double> _gauges = new();
    private readonly ConcurrentDictionary<string, List<TimeSpan>> _timers = new();
    private readonly object _lock = new();

    public MetricsService(ILogger<MetricsService> logger)
    {
        _logger = logger;
    }

    public void RecordPaymentInitiated(string teamSlug)
    {
        var key = $"payments.initiated.{teamSlug}";
        IncrementCounter(key);
        IncrementCounter("payments.initiated.total");
        _logger.LogDebug("Recorded payment initiation for team {TeamSlug}", teamSlug);
    }

    public void RecordPaymentCompleted(string teamSlug, decimal amount, bool successful)
    {
        var status = successful ? "success" : "failed";
        var key = $"payments.completed.{teamSlug}.{status}";
        
        IncrementCounter(key);
        IncrementCounter($"payments.completed.total.{status}");
        
        if (successful)
        {
            var amountKey = $"payments.amount.{teamSlug}";
            SetGauge(amountKey, (double)amount);
        }
        
        _logger.LogDebug("Recorded payment completion for team {TeamSlug}: {Status}, Amount: {Amount}", 
            teamSlug, status, amount);
    }

    public void RecordAuthenticationAttempt(string teamSlug, bool successful)
    {
        var status = successful ? "success" : "failed";
        var key = $"auth.attempts.{teamSlug}.{status}";
        
        IncrementCounter(key);
        IncrementCounter($"auth.attempts.total.{status}");
        
        _logger.LogDebug("Recorded authentication attempt for team {TeamSlug}: {Status}", teamSlug, status);
    }

    public void RecordApiCall(string endpoint, TimeSpan duration, bool successful)
    {
        var status = successful ? "success" : "failed";
        var key = $"api.calls.{endpoint}.{status}";
        
        IncrementCounter(key);
        RecordTimer($"api.duration.{endpoint}", duration);
        
        _logger.LogDebug("Recorded API call to {Endpoint}: {Status}, Duration: {Duration}ms", 
            endpoint, status, duration.TotalMilliseconds);
    }

    public void RecordDatabaseOperation(string operation, TimeSpan duration)
    {
        IncrementCounter($"db.operations.{operation}");
        RecordTimer($"db.duration.{operation}", duration);
        
        _logger.LogDebug("Recorded database operation {Operation}: Duration: {Duration}ms", 
            operation, duration.TotalMilliseconds);
    }

    public void IncrementCounter(string name, Dictionary<string, string>? tags = null)
    {
        var key = BuildKey(name, tags);
        _counters.AddOrUpdate(key, 1, (_, current) => current + 1);
    }

    public void RecordTimer(string name, TimeSpan duration, Dictionary<string, string>? tags = null)
    {
        var key = BuildKey(name, tags);
        
        lock (_lock)
        {
            if (!_timers.TryGetValue(key, out var durations))
            {
                durations = new List<TimeSpan>();
                _timers[key] = durations;
            }
            
            durations.Add(duration);
            
            // Keep only last 1000 measurements to prevent memory leaks
            if (durations.Count > 1000)
            {
                durations.RemoveRange(0, durations.Count - 1000);
            }
        }
    }

    public void SetGauge(string name, double value, Dictionary<string, string>? tags = null)
    {
        var key = BuildKey(name, tags);
        _gauges.AddOrUpdate(key, value, (_, _) => value);
    }

    public Dictionary<string, object> GetMetrics()
    {
        var metrics = new Dictionary<string, object>();
        
        // Add counters
        foreach (var counter in _counters)
        {
            metrics[$"counter.{counter.Key}"] = counter.Value;
        }
        
        // Add gauges
        foreach (var gauge in _gauges)
        {
            metrics[$"gauge.{gauge.Key}"] = gauge.Value;
        }
        
        // Add timer statistics
        lock (_lock)
        {
            foreach (var timer in _timers)
            {
                if (timer.Value.Any())
                {
                    var durations = timer.Value.Select(t => t.TotalMilliseconds).ToArray();
                    metrics[$"timer.{timer.Key}.count"] = durations.Length;
                    metrics[$"timer.{timer.Key}.min"] = durations.Min();
                    metrics[$"timer.{timer.Key}.max"] = durations.Max();
                    metrics[$"timer.{timer.Key}.avg"] = durations.Average();
                    
                    if (durations.Length > 1)
                    {
                        Array.Sort(durations);
                        var p50Index = (int)(durations.Length * 0.5);
                        var p95Index = (int)(durations.Length * 0.95);
                        var p99Index = (int)(durations.Length * 0.99);
                        
                        metrics[$"timer.{timer.Key}.p50"] = durations[Math.Min(p50Index, durations.Length - 1)];
                        metrics[$"timer.{timer.Key}.p95"] = durations[Math.Min(p95Index, durations.Length - 1)];
                        metrics[$"timer.{timer.Key}.p99"] = durations[Math.Min(p99Index, durations.Length - 1)];
                    }
                }
            }
        }
        
        return metrics;
    }

    public void Reset()
    {
        _counters.Clear();
        _gauges.Clear();
        
        lock (_lock)
        {
            _timers.Clear();
        }
        
        _logger.LogInformation("Metrics have been reset");
    }

    public async Task RecordCounterAsync(string metricName, double value = 1, Dictionary<string, string>? labels = null)
    {
        IncrementCounter(metricName, labels);
        await Task.CompletedTask;
    }

    public async Task RecordGaugeAsync(string metricName, double value, Dictionary<string, string>? labels = null)
    {
        SetGauge(metricName, value, labels);
        await Task.CompletedTask;
    }

    public async Task RecordHistogramAsync(string metricName, double value, Dictionary<string, string>? labels = null)
    {
        SetGauge(metricName, value, labels);
        await Task.CompletedTask;
    }

    private static string BuildKey(string name, Dictionary<string, string>? tags)
    {
        if (tags == null || !tags.Any())
        {
            return name;
        }
        
        var tagString = string.Join(",", tags.Select(kvp => $"{kvp.Key}={kvp.Value}"));
        return $"{name}[{tagString}]";
    }
}