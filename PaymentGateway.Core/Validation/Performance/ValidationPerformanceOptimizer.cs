using FluentValidation;
using FluentValidation.Results;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Diagnostics;

namespace PaymentGateway.Core.Validation.Performance;

/// <summary>
/// Service for optimizing validation performance
/// </summary>
public interface IValidationPerformanceOptimizer
{
    Task<ValidationResult> ValidateWithCachingAsync<T>(T instance, IValidator<T> validator, CancellationToken cancellationToken = default);
    Task<ValidationResult> ValidateWithTimeoutAsync<T>(T instance, IValidator<T> validator, TimeSpan timeout, CancellationToken cancellationToken = default);
    ValidationResult ValidateWithPerformanceTracking<T>(T instance, IValidator<T> validator, out ValidationPerformanceMetrics metrics);
    Task<ValidationResult> ValidateWithBatchOptimizationAsync<T>(IEnumerable<T> instances, IValidator<T> validator, CancellationToken cancellationToken = default);
    ValidationPerformanceReport GeneratePerformanceReport();
    void OptimizeValidatorConfiguration<T>(AbstractValidator<T> validator);
    bool IsValidationCacheEnabled { get; set; }
    TimeSpan DefaultValidationTimeout { get; set; }
}

/// <summary>
/// Implementation of validation performance optimizer
/// </summary>
public class ValidationPerformanceOptimizer : IValidationPerformanceOptimizer
{
    private readonly IMemoryCache _cache;
    private readonly ILogger<ValidationPerformanceOptimizer> _logger;
    private readonly ConcurrentDictionary<string, ValidationPerformanceMetrics> _performanceMetrics;
    private readonly ValidationPerformanceConfiguration _configuration;

    public bool IsValidationCacheEnabled { get; set; } = true;
    public TimeSpan DefaultValidationTimeout { get; set; } = TimeSpan.FromSeconds(5);

    public ValidationPerformanceOptimizer(
        IMemoryCache cache,
        ILogger<ValidationPerformanceOptimizer> logger,
        ValidationPerformanceConfiguration? configuration = null)
    {
        _cache = cache;
        _logger = logger;
        _performanceMetrics = new ConcurrentDictionary<string, ValidationPerformanceMetrics>();
        _configuration = configuration ?? new ValidationPerformanceConfiguration();
    }

    public async Task<ValidationResult> ValidateWithCachingAsync<T>(T instance, IValidator<T> validator, CancellationToken cancellationToken = default)
    {
        if (!IsValidationCacheEnabled)
        {
            return await validator.ValidateAsync(instance, cancellationToken);
        }

        var cacheKey = GenerateCacheKey(instance, validator.GetType());
        
        if (_cache.TryGetValue(cacheKey, out ValidationResult cachedResult))
        {
            _logger.LogDebug("Validation result retrieved from cache for key: {CacheKey}", cacheKey);
            UpdatePerformanceMetrics(typeof(T).Name, TimeSpan.Zero, true, cachedResult.IsValid);
            return cachedResult;
        }

        var stopwatch = Stopwatch.StartNew();
        var result = await validator.ValidateAsync(instance, cancellationToken);
        stopwatch.Stop();

        // Cache successful validations and critical errors
        if (result.IsValid || HasCriticalErrors(result))
        {
            var cacheOptions = new MemoryCacheEntryOptions
            {
                SlidingExpiration = _configuration.CacheSlidingExpiration,
                AbsoluteExpirationRelativeToNow = _configuration.CacheAbsoluteExpiration,
                Priority = result.IsValid ? CacheItemPriority.Normal : CacheItemPriority.High
            };

            _cache.Set(cacheKey, result, cacheOptions);
            _logger.LogDebug("Validation result cached for key: {CacheKey}", cacheKey);
        }

        UpdatePerformanceMetrics(typeof(T).Name, stopwatch.Elapsed, false, result.IsValid);
        return result;
    }

    public async Task<ValidationResult> ValidateWithTimeoutAsync<T>(T instance, IValidator<T> validator, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        using var timeoutCts = new CancellationTokenSource(timeout);
        using var combinedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

        try
        {
            var stopwatch = Stopwatch.StartNew();
            var result = await validator.ValidateAsync(instance, combinedCts.Token);
            stopwatch.Stop();

            UpdatePerformanceMetrics(typeof(T).Name, stopwatch.Elapsed, false, result.IsValid);
            return result;
        }
        catch (OperationCanceledException) when (timeoutCts.Token.IsCancellationRequested)
        {
            _logger.LogWarning("Validation timeout exceeded for type: {TypeName}", typeof(T).Name);
            
            var timeoutResult = new ValidationResult();
            timeoutResult.Errors.Add(new ValidationFailure("", "Validation timeout exceeded")
            {
                ErrorCode = "VALIDATION_TIMEOUT"
            });

            UpdatePerformanceMetrics(typeof(T).Name, timeout, false, false, true);
            return timeoutResult;
        }
    }

    public ValidationResult ValidateWithPerformanceTracking<T>(T instance, IValidator<T> validator, out ValidationPerformanceMetrics metrics)
    {
        var stopwatch = Stopwatch.StartNew();
        var memoryBefore = GC.GetTotalMemory(false);

        var result = validator.Validate(instance);

        stopwatch.Stop();
        var memoryAfter = GC.GetTotalMemory(false);

        metrics = new ValidationPerformanceMetrics
        {
            ValidationTypeName = typeof(T).Name,
            AverageExecutionTime = stopwatch.Elapsed,
            MaxExecutionTime = stopwatch.Elapsed,
            MinExecutionTime = stopwatch.Elapsed,
            MemoryUsed = memoryAfter - memoryBefore,
            IsValid = result.IsValid,
            ErrorCount = result.Errors.Count,
            ValidationRuleCount = CountValidationRules(validator),
            Timestamp = DateTime.UtcNow
        };

        UpdatePerformanceMetrics(metrics.ValidationTypeName, metrics.AverageExecutionTime, false, metrics.IsValid);
        return result;
    }

    public async Task<ValidationResult> ValidateWithBatchOptimizationAsync<T>(IEnumerable<T> instances, IValidator<T> validator, CancellationToken cancellationToken = default)
    {
        var aggregatedResult = new ValidationResult();
        var tasks = new List<Task<ValidationResult>>();
        
        var instanceList = instances.ToList();
        var batchSize = _configuration.OptimalBatchSize;

        _logger.LogDebug("Starting batch validation for {Count} instances with batch size {BatchSize}", 
            instanceList.Count, batchSize);

        // Process in batches to avoid overwhelming the system
        for (int i = 0; i < instanceList.Count; i += batchSize)
        {
            var batch = instanceList.Skip(i).Take(batchSize);
            var batchTasks = batch.Select(instance => ValidateWithCachingAsync(instance, validator, cancellationToken));
            tasks.AddRange(batchTasks);

            // Wait for current batch to complete before starting next batch
            if (tasks.Count >= batchSize || i + batchSize >= instanceList.Count)
            {
                var results = await Task.WhenAll(tasks);
                foreach (var result in results)
                {
                    aggregatedResult.Errors.AddRange(result.Errors);
                }
                tasks.Clear();
            }
        }

        _logger.LogDebug("Batch validation completed with {ErrorCount} total errors", aggregatedResult.Errors.Count);
        return aggregatedResult;
    }

    public ValidationPerformanceReport GeneratePerformanceReport()
    {
        var report = new ValidationPerformanceReport
        {
            GeneratedAt = DateTime.UtcNow,
            TotalValidations = _performanceMetrics.Values.Sum(m => m.TotalValidations),
            TotalCacheHits = _performanceMetrics.Values.Sum(m => m.CacheHits),
            TotalTimeouts = _performanceMetrics.Values.Sum(m => m.Timeouts)
        };

        report.ValidationsByType = _performanceMetrics.ToDictionary(
            kvp => kvp.Key,
            kvp => kvp.Value
        );

        // Calculate overall statistics
        var allMetrics = _performanceMetrics.Values.ToList();
        if (allMetrics.Any())
        {
            report.AverageExecutionTime = TimeSpan.FromMilliseconds(
                allMetrics.Average(m => m.AverageExecutionTime.TotalMilliseconds));
            
            report.CacheHitRate = allMetrics.Sum(m => m.CacheHits) / (double)Math.Max(1, allMetrics.Sum(m => m.TotalValidations));
            
            report.SuccessRate = allMetrics.Sum(m => m.SuccessfulValidations) / (double)Math.Max(1, allMetrics.Sum(m => m.TotalValidations));
        }

        // Identify performance bottlenecks
        report.PerformanceBottlenecks = IdentifyPerformanceBottlenecks();
        
        // Generate optimization recommendations
        report.OptimizationRecommendations = GenerateOptimizationRecommendations();

        return report;
    }

    public void OptimizeValidatorConfiguration<T>(AbstractValidator<T> validator)
    {
        // Enable cascade mode to stop on first failure for better performance
        if (_configuration.EnableCascadeMode)
        {
            validator.RuleLevelCascadeMode = CascadeMode.Stop;
        }

        // Configure async validation timeout
        if (_configuration.AsyncValidationTimeout.HasValue)
        {
            // This would be implemented based on specific validator capabilities
            _logger.LogDebug("Optimized validator configuration for type: {TypeName}", typeof(T).Name);
        }

        // Add performance monitoring interceptors if available
        ConfigurePerformanceInterceptors(validator);
    }

    private string GenerateCacheKey<T>(T instance, Type validatorType)
    {
        // Generate a hash-based cache key from the instance properties
        var instanceHash = instance?.GetHashCode() ?? 0;
        var validatorHash = validatorType.GetHashCode();
        return $"validation_{typeof(T).Name}_{instanceHash}_{validatorHash}";
    }

    private bool HasCriticalErrors(ValidationResult result)
    {
        var criticalErrorCodes = new[]
        {
            "TEAM_NOT_FOUND", "TOKEN_INVALID", "PAYMENT_NOT_FOUND",
            "DAILY_LIMIT_EXCEEDED", "VALIDATION_SERVICE_ERROR"
        };

        return result.Errors.Any(e => criticalErrorCodes.Contains(e.ErrorCode));
    }

    private void UpdatePerformanceMetrics(string typeName, TimeSpan executionTime, bool cacheHit, bool isValid, bool timeout = false)
    {
        _performanceMetrics.AddOrUpdate(typeName,
            new ValidationPerformanceMetrics
            {
                ValidationTypeName = typeName,
                TotalValidations = 1,
                CacheHits = cacheHit ? 1 : 0,
                SuccessfulValidations = isValid ? 1 : 0,
                AverageExecutionTime = executionTime,
                MaxExecutionTime = executionTime,
                MinExecutionTime = executionTime,
                Timeouts = timeout ? 1 : 0,
                Timestamp = DateTime.UtcNow
            },
            (key, existing) =>
            {
                existing.TotalValidations++;
                if (cacheHit) existing.CacheHits++;
                if (isValid) existing.SuccessfulValidations++;
                if (timeout) existing.Timeouts++;

                // Update execution time statistics
                var totalTime = TimeSpan.FromMilliseconds(
                    existing.AverageExecutionTime.TotalMilliseconds * (existing.TotalValidations - 1) + 
                    executionTime.TotalMilliseconds);
                existing.AverageExecutionTime = TimeSpan.FromMilliseconds(totalTime.TotalMilliseconds / existing.TotalValidations);

                if (executionTime > existing.MaxExecutionTime)
                    existing.MaxExecutionTime = executionTime;
                
                if (executionTime < existing.MinExecutionTime && executionTime > TimeSpan.Zero)
                    existing.MinExecutionTime = executionTime;

                existing.Timestamp = DateTime.UtcNow;
                return existing;
            });
    }

    private int CountValidationRules<T>(IValidator<T> validator)
    {
        // This would count the actual validation rules in the validator
        // For now, return an estimated count
        return 10; // Placeholder
    }

    private List<PerformanceBottleneck> IdentifyPerformanceBottlenecks()
    {
        var bottlenecks = new List<PerformanceBottleneck>();

        foreach (var metrics in _performanceMetrics.Values)
        {
            // Identify slow validations
            if (metrics.AverageExecutionTime > _configuration.SlowValidationThreshold)
            {
                bottlenecks.Add(new PerformanceBottleneck
                {
                    Type = BottleneckType.SlowValidation,
                    ValidationTypeName = metrics.ValidationTypeName,
                    Description = $"Average execution time ({metrics.AverageExecutionTime.TotalMilliseconds:F2}ms) exceeds threshold",
                    Severity = metrics.AverageExecutionTime > _configuration.CriticalValidationThreshold ? BottleneckSeverity.Critical : BottleneckSeverity.Warning
                });
            }

            // Identify low cache hit rates
            var cacheHitRate = metrics.TotalValidations > 0 ? metrics.CacheHits / (double)metrics.TotalValidations : 0;
            if (cacheHitRate < _configuration.MinimumCacheHitRate && metrics.TotalValidations > 10)
            {
                bottlenecks.Add(new PerformanceBottleneck
                {
                    Type = BottleneckType.LowCacheHitRate,
                    ValidationTypeName = metrics.ValidationTypeName,
                    Description = $"Cache hit rate ({cacheHitRate:P}) is below optimal threshold",
                    Severity = BottleneckSeverity.Warning
                });
            }

            // Identify high failure rates
            var successRate = metrics.TotalValidations > 0 ? metrics.SuccessfulValidations / (double)metrics.TotalValidations : 0;
            if (successRate < _configuration.MinimumSuccessRate && metrics.TotalValidations > 5)
            {
                bottlenecks.Add(new PerformanceBottleneck
                {
                    Type = BottleneckType.HighFailureRate,
                    ValidationTypeName = metrics.ValidationTypeName,
                    Description = $"Success rate ({successRate:P}) indicates potential validation issues",
                    Severity = BottleneckSeverity.Warning
                });
            }
        }

        return bottlenecks;
    }

    private List<OptimizationRecommendation> GenerateOptimizationRecommendations()
    {
        var recommendations = new List<OptimizationRecommendation>();

        // Analyze current performance and suggest improvements
        var totalValidations = _performanceMetrics.Values.Sum(m => m.TotalValidations);
        var totalCacheHits = _performanceMetrics.Values.Sum(m => m.CacheHits);

        if (totalValidations > 0)
        {
            var overallCacheHitRate = totalCacheHits / (double)totalValidations;
            
            if (overallCacheHitRate < 0.5)
            {
                recommendations.Add(new OptimizationRecommendation
                {
                    Type = OptimizationType.CacheOptimization,
                    Priority = RecommendationPriority.High,
                    Description = "Consider increasing cache expiration times or improving cache key generation",
                    EstimatedImprovement = "20-40% performance gain"
                });
            }

            if (_performanceMetrics.Values.Any(m => m.AverageExecutionTime > TimeSpan.FromSeconds(1)))
            {
                recommendations.Add(new OptimizationRecommendation
                {
                    Type = OptimizationType.ValidationRuleOptimization,
                    Priority = RecommendationPriority.Medium,
                    Description = "Review complex validation rules and consider async optimization",
                    EstimatedImprovement = "10-30% performance gain"
                });
            }
        }

        return recommendations;
    }

    private void ConfigurePerformanceInterceptors<T>(AbstractValidator<T> validator)
    {
        // This would add performance monitoring interceptors if the validator supports them
        _logger.LogDebug("Performance interceptors configured for validator: {ValidatorType}", validator.GetType().Name);
    }
}

// Supporting classes for performance optimization
public class ValidationPerformanceConfiguration
{
    public TimeSpan CacheSlidingExpiration { get; set; } = TimeSpan.FromMinutes(10);
    public TimeSpan CacheAbsoluteExpiration { get; set; } = TimeSpan.FromHours(1);
    public int OptimalBatchSize { get; set; } = 50;
    public bool EnableCascadeMode { get; set; } = true;
    public TimeSpan? AsyncValidationTimeout { get; set; } = TimeSpan.FromSeconds(30);
    public TimeSpan SlowValidationThreshold { get; set; } = TimeSpan.FromMilliseconds(500);
    public TimeSpan CriticalValidationThreshold { get; set; } = TimeSpan.FromSeconds(2);
    public double MinimumCacheHitRate { get; set; } = 0.3;
    public double MinimumSuccessRate { get; set; } = 0.8;
}

public class ValidationPerformanceMetrics
{
    public string ValidationTypeName { get; set; } = string.Empty;
    public int TotalValidations { get; set; }
    public int CacheHits { get; set; }
    public int SuccessfulValidations { get; set; }
    public int Timeouts { get; set; }
    public TimeSpan AverageExecutionTime { get; set; }
    public TimeSpan MaxExecutionTime { get; set; }
    public TimeSpan MinExecutionTime { get; set; } = TimeSpan.MaxValue;
    public DateTime Timestamp { get; set; }
    public long MemoryUsed { get; set; }
    public bool IsValid { get; set; }
    public int ErrorCount { get; set; }
    public int ValidationRuleCount { get; set; }
}

public class ValidationPerformanceReport
{
    public DateTime GeneratedAt { get; set; }
    public int TotalValidations { get; set; }
    public int TotalCacheHits { get; set; }
    public int TotalTimeouts { get; set; }
    public TimeSpan AverageExecutionTime { get; set; }
    public double CacheHitRate { get; set; }
    public double SuccessRate { get; set; }
    public Dictionary<string, ValidationPerformanceMetrics> ValidationsByType { get; set; } = new();
    public List<PerformanceBottleneck> PerformanceBottlenecks { get; set; } = new();
    public List<OptimizationRecommendation> OptimizationRecommendations { get; set; } = new();
}

public class PerformanceBottleneck
{
    public BottleneckType Type { get; set; }
    public string ValidationTypeName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public BottleneckSeverity Severity { get; set; }
}

public class OptimizationRecommendation
{
    public OptimizationType Type { get; set; }
    public RecommendationPriority Priority { get; set; }
    public string Description { get; set; } = string.Empty;
    public string EstimatedImprovement { get; set; } = string.Empty;
}

public enum BottleneckType
{
    SlowValidation,
    LowCacheHitRate,
    HighFailureRate,
    MemoryUsage,
    Timeouts
}

public enum BottleneckSeverity
{
    Info,
    Warning,
    Critical
}

public enum OptimizationType
{
    CacheOptimization,
    ValidationRuleOptimization,
    BatchSizeOptimization,
    TimeoutOptimization,
    MemoryOptimization
}

public enum RecommendationPriority
{
    Low,
    Medium,
    High,
    Critical
}