using PaymentGateway.Core.Enums;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;

namespace PaymentGateway.Core.Services;

public interface IErrorTrackingService
{
    Task TrackErrorAsync(ErrorTrackingInfo errorInfo, CancellationToken cancellationToken = default);
    Task<ErrorCorrelationReport> GetErrorCorrelationAsync(string correlationId, CancellationToken cancellationToken = default);
    Task<List<ErrorTrackingInfo>> GetRelatedErrorsAsync(string correlationId, TimeSpan? timeWindow = null);
    Task<ErrorTrendAnalysis> AnalyzeErrorTrendsAsync(TimeSpan period, CancellationToken cancellationToken = default);
    Task<Dictionary<string, int>> GetErrorFrequencyAsync(TimeSpan period);
    Task CleanupOldErrorsAsync(TimeSpan retentionPeriod);
}

public record ErrorTrackingInfo(
    string CorrelationId,
    PaymentErrorCode ErrorCode,
    string? PaymentId,
    string? OrderId,
    string? TeamSlug,
    string? UserId,
    string ErrorMessage,
    string? StackTrace,
    Dictionary<string, string> Context,
    DateTime Timestamp,
    string? RequestPath,
    string? UserAgent,
    string? IpAddress);

public record ErrorCorrelationReport(
    string CorrelationId,
    List<ErrorTrackingInfo> RelatedErrors,
    Dictionary<PaymentErrorCode, int> ErrorCodeFrequency,
    TimeSpan TotalDuration,
    string? Pattern,
    List<string> PossibleCauses);

public record ErrorTrendAnalysis(
    TimeSpan Period,
    int TotalErrors,
    Dictionary<PaymentErrorCode, int> ErrorBreakdown,
    Dictionary<string, int> TeamBreakdown,
    List<ErrorSpike> ErrorSpikes,
    double ErrorRate,
    List<ErrorPattern> DetectedPatterns);

public record ErrorSpike(
    DateTime StartTime,
    DateTime EndTime,
    PaymentErrorCode ErrorCode,
    int ErrorCount,
    double SpikeIntensity);

public record ErrorPattern(
    string PatternType,
    List<PaymentErrorCode> ErrorSequence,
    int Frequency,
    string Description);

public class ErrorTrackingOptions
{
    public TimeSpan DefaultRetentionPeriod { get; set; } = TimeSpan.FromDays(30);
    public TimeSpan CorrelationTimeWindow { get; set; } = TimeSpan.FromHours(1);
    public int MaxErrorsPerCorrelation { get; set; } = 1000;
    public bool EnablePatternDetection { get; set; } = true;
    public double SpikeDetectionThreshold { get; set; } = 2.0; // 2x normal rate
    public List<string> SensitiveContextKeys { get; set; } = new() { "cardNumber", "cvv", "password", "token" };
}

public class ErrorTrackingService : IErrorTrackingService
{
    private readonly ILogger<ErrorTrackingService> _logger;
    private readonly ErrorTrackingOptions _options;
    private readonly ConcurrentDictionary<string, List<ErrorTrackingInfo>> _errorsByCorrelation;
    private readonly ConcurrentDictionary<DateTime, List<ErrorTrackingInfo>> _errorsByTime;

    public ErrorTrackingService(
        ILogger<ErrorTrackingService> logger,
        IOptions<ErrorTrackingOptions> options)
    {
        _logger = logger;
        _options = options.Value;
        _errorsByCorrelation = new ConcurrentDictionary<string, List<ErrorTrackingInfo>>();
        _errorsByTime = new ConcurrentDictionary<DateTime, List<ErrorTrackingInfo>>();
    }

    public async Task TrackErrorAsync(ErrorTrackingInfo errorInfo, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(errorInfo);

        try
        {
            // Sanitize sensitive data
            var sanitizedErrorInfo = SanitizeErrorInfo(errorInfo);

            // Store by correlation ID
            _errorsByCorrelation.AddOrUpdate(
                sanitizedErrorInfo.CorrelationId,
                new List<ErrorTrackingInfo> { sanitizedErrorInfo },
                (_, existing) =>
                {
                    if (existing.Count >= _options.MaxErrorsPerCorrelation)
                    {
                        existing.RemoveAt(0); // Remove oldest
                    }
                    existing.Add(sanitizedErrorInfo);
                    return existing;
                });

            // Store by time bucket (hourly)
            var timeBucket = new DateTime(
                sanitizedErrorInfo.Timestamp.Year,
                sanitizedErrorInfo.Timestamp.Month,
                sanitizedErrorInfo.Timestamp.Day,
                sanitizedErrorInfo.Timestamp.Hour,
                0, 0, DateTimeKind.Utc);

            _errorsByTime.AddOrUpdate(
                timeBucket,
                new List<ErrorTrackingInfo> { sanitizedErrorInfo },
                (_, existing) =>
                {
                    existing.Add(sanitizedErrorInfo);
                    return existing;
                });

            _logger.LogDebug("Tracked error {ErrorCode} for correlation {CorrelationId}",
                sanitizedErrorInfo.ErrorCode, sanitizedErrorInfo.CorrelationId);

            // Perform pattern detection if enabled
            if (_options.EnablePatternDetection)
            {
                await DetectErrorPatternsAsync(sanitizedErrorInfo);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to track error for correlation {CorrelationId}", errorInfo.CorrelationId);
        }

        await Task.CompletedTask;
    }

    public async Task<ErrorCorrelationReport> GetErrorCorrelationAsync(string correlationId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(correlationId);

        if (!_errorsByCorrelation.TryGetValue(correlationId, out var errors))
        {
            return new ErrorCorrelationReport(
                correlationId,
                new List<ErrorTrackingInfo>(),
                new Dictionary<PaymentErrorCode, int>(),
                TimeSpan.Zero,
                null,
                new List<string>());
        }

        var sortedErrors = errors.OrderBy(e => e.Timestamp).ToList();
        var errorCodeFrequency = sortedErrors
            .GroupBy(e => e.ErrorCode)
            .ToDictionary(g => g.Key, g => g.Count());

        var totalDuration = sortedErrors.Count > 1
            ? sortedErrors.Last().Timestamp - sortedErrors.First().Timestamp
            : TimeSpan.Zero;

        var pattern = await AnalyzeErrorPatternAsync(sortedErrors);
        var possibleCauses = await IdentifyPossibleCausesAsync(sortedErrors);

        return await Task.FromResult(new ErrorCorrelationReport(
            correlationId,
            sortedErrors,
            errorCodeFrequency,
            totalDuration,
            pattern,
            possibleCauses));
    }

    public async Task<List<ErrorTrackingInfo>> GetRelatedErrorsAsync(string correlationId, TimeSpan? timeWindow = null)
    {
        ArgumentNullException.ThrowIfNull(correlationId);

        var actualTimeWindow = timeWindow ?? _options.CorrelationTimeWindow;

        if (!_errorsByCorrelation.TryGetValue(correlationId, out var directErrors))
        {
            return new List<ErrorTrackingInfo>();
        }

        var firstError = directErrors.OrderBy(e => e.Timestamp).FirstOrDefault();
        if (firstError == null)
        {
            return new List<ErrorTrackingInfo>();
        }

        var startTime = firstError.Timestamp - actualTimeWindow;
        var endTime = firstError.Timestamp + actualTimeWindow;

        // Find related errors from other correlations in the same time window
        var relatedErrors = new List<ErrorTrackingInfo>();

        foreach (var kvp in _errorsByCorrelation)
        {
            if (kvp.Key == correlationId) continue;

            var candidateErrors = kvp.Value
                .Where(e => e.Timestamp >= startTime && e.Timestamp <= endTime)
                .Where(e => IsRelatedError(firstError, e))
                .ToList();

            relatedErrors.AddRange(candidateErrors);
        }

        // Combine direct errors with related errors
        var allRelatedErrors = directErrors.Concat(relatedErrors)
            .OrderBy(e => e.Timestamp)
            .ToList();

        return await Task.FromResult(allRelatedErrors);
    }

    public async Task<ErrorTrendAnalysis> AnalyzeErrorTrendsAsync(TimeSpan period, CancellationToken cancellationToken = default)
    {
        var endTime = DateTime.UtcNow;
        var startTime = endTime - period;

        var periodErrors = GetErrorsInPeriod(startTime, endTime);
        var totalErrors = periodErrors.Count;

        var errorBreakdown = periodErrors
            .GroupBy(e => e.ErrorCode)
            .ToDictionary(g => g.Key, g => g.Count());

        var teamBreakdown = periodErrors
            .Where(e => !string.IsNullOrEmpty(e.TeamSlug))
            .GroupBy(e => e.TeamSlug!)
            .ToDictionary(g => g.Key, g => g.Count());

        var errorSpikes = await DetectErrorSpikesAsync(periodErrors, period);
        var detectedPatterns = await DetectErrorPatternsInPeriodAsync(periodErrors);

        // Calculate error rate (errors per hour)
        var errorRate = totalErrors / period.TotalHours;

        return new ErrorTrendAnalysis(
            period,
            totalErrors,
            errorBreakdown,
            teamBreakdown,
            errorSpikes,
            errorRate,
            detectedPatterns);
    }

    public async Task<Dictionary<string, int>> GetErrorFrequencyAsync(TimeSpan period)
    {
        var endTime = DateTime.UtcNow;
        var startTime = endTime - period;

        var periodErrors = GetErrorsInPeriod(startTime, endTime);
        
        var frequency = periodErrors
            .GroupBy(e => e.ErrorCode.ToString())
            .ToDictionary(g => g.Key, g => g.Count());

        return await Task.FromResult(frequency);
    }

    public async Task CleanupOldErrorsAsync(TimeSpan retentionPeriod)
    {
        var cutoffTime = DateTime.UtcNow - retentionPeriod;
        var removedCorrelations = 0;
        var removedTimeBuckets = 0;

        try
        {
            // Clean up correlation-based storage
            var correlationsToRemove = new List<string>();
            foreach (var kvp in _errorsByCorrelation)
            {
                var recentErrors = kvp.Value.Where(e => e.Timestamp > cutoffTime).ToList();
                if (recentErrors.Count == 0)
                {
                    correlationsToRemove.Add(kvp.Key);
                }
                else if (recentErrors.Count < kvp.Value.Count)
                {
                    _errorsByCorrelation[kvp.Key] = recentErrors;
                }
            }

            foreach (var correlationId in correlationsToRemove)
            {
                _errorsByCorrelation.TryRemove(correlationId, out _);
                removedCorrelations++;
            }

            // Clean up time-based storage
            var timeBucketsToRemove = _errorsByTime.Keys
                .Where(time => time < cutoffTime)
                .ToList();

            foreach (var timeBucket in timeBucketsToRemove)
            {
                _errorsByTime.TryRemove(timeBucket, out _);
                removedTimeBuckets++;
            }

            _logger.LogInformation("Cleaned up {RemovedCorrelations} correlations and {RemovedTimeBuckets} time buckets older than {RetentionPeriod}",
                removedCorrelations, removedTimeBuckets, retentionPeriod);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during error tracking cleanup");
        }

        await Task.CompletedTask;
    }

    private ErrorTrackingInfo SanitizeErrorInfo(ErrorTrackingInfo errorInfo)
    {
        var sanitizedContext = new Dictionary<string, string>();
        
        foreach (var kvp in errorInfo.Context)
        {
            if (_options.SensitiveContextKeys.Any(sensitive => 
                kvp.Key.Contains(sensitive, StringComparison.OrdinalIgnoreCase)))
            {
                sanitizedContext[kvp.Key] = "***REDACTED***";
            }
            else
            {
                sanitizedContext[kvp.Key] = kvp.Value;
            }
        }

        return errorInfo with { Context = sanitizedContext };
    }

    private List<ErrorTrackingInfo> GetErrorsInPeriod(DateTime startTime, DateTime endTime)
    {
        var periodErrors = new List<ErrorTrackingInfo>();

        foreach (var kvp in _errorsByTime)
        {
            if (kvp.Key >= startTime && kvp.Key <= endTime)
            {
                periodErrors.AddRange(kvp.Value.Where(e => e.Timestamp >= startTime && e.Timestamp <= endTime));
            }
        }

        return periodErrors;
    }

    private bool IsRelatedError(ErrorTrackingInfo baseError, ErrorTrackingInfo candidateError)
    {
        // Errors are related if they share common identifiers
        return (!string.IsNullOrEmpty(baseError.PaymentId) && baseError.PaymentId == candidateError.PaymentId) ||
               (!string.IsNullOrEmpty(baseError.OrderId) && baseError.OrderId == candidateError.OrderId) ||
               (!string.IsNullOrEmpty(baseError.TeamSlug) && baseError.TeamSlug == candidateError.TeamSlug) ||
               (!string.IsNullOrEmpty(baseError.UserId) && baseError.UserId == candidateError.UserId) ||
               (baseError.IpAddress == candidateError.IpAddress && !string.IsNullOrEmpty(baseError.IpAddress));
    }

    private async Task<string?> AnalyzeErrorPatternAsync(List<ErrorTrackingInfo> errors)
    {
        if (errors.Count < 2) return null;

        var errorCodes = errors.Select(e => e.ErrorCode).ToList();

        // Check for common patterns
        if (errorCodes.Contains(PaymentErrorCode.InvalidToken) && 
            errorCodes.Any(c => c == PaymentErrorCode.PaymentNotFound))
        {
            return "Authentication followed by authorization failure";
        }

        if (errorCodes.Count(c => c == PaymentErrorCode.ServiceTemporarilyUnavailable) >= 3)
        {
            return "Service availability issues";
        }

        if (errorCodes.Contains(PaymentErrorCode.InvalidStateTransition))
        {
            return "State machine violation";
        }

        return await Task.FromResult((string?)null);
    }

    private async Task<List<string>> IdentifyPossibleCausesAsync(List<ErrorTrackingInfo> errors)
    {
        var causes = new List<string>();

        var errorCodes = errors.Select(e => e.ErrorCode).ToHashSet();

        if (errorCodes.Contains(PaymentErrorCode.ServiceTemporarilyUnavailable))
        {
            causes.Add("External service outage or maintenance");
        }

        if (errorCodes.Contains(PaymentErrorCode.CriticalSystemError))
        {
            causes.Add("System infrastructure failure");
        }

        if (errorCodes.Contains(PaymentErrorCode.InvalidToken))
        {
            causes.Add("Authentication configuration issue");
        }

        if (errors.All(e => !string.IsNullOrEmpty(e.TeamSlug)) && 
            errors.Select(e => e.TeamSlug).Distinct().Count() == 1)
        {
            causes.Add("Merchant-specific configuration issue");
        }

        return await Task.FromResult(causes);
    }

    private async Task DetectErrorPatternsAsync(ErrorTrackingInfo errorInfo)
    {
        // This would implement real-time pattern detection
        // For now, we'll just log patterns we detect
        await Task.CompletedTask;
    }

    private async Task<List<ErrorSpike>> DetectErrorSpikesAsync(List<ErrorTrackingInfo> errors, TimeSpan period)
    {
        var spikes = new List<ErrorSpike>();
        var hourlyBuckets = new Dictionary<DateTime, Dictionary<PaymentErrorCode, int>>();

        // Group errors by hour and error code
        foreach (var error in errors)
        {
            var hourBucket = new DateTime(error.Timestamp.Year, error.Timestamp.Month, error.Timestamp.Day, error.Timestamp.Hour, 0, 0);
            
            if (!hourlyBuckets.ContainsKey(hourBucket))
            {
                hourlyBuckets[hourBucket] = new Dictionary<PaymentErrorCode, int>();
            }

            hourlyBuckets[hourBucket].TryGetValue(error.ErrorCode, out var count);
            hourlyBuckets[hourBucket][error.ErrorCode] = count + 1;
        }

        // Calculate average error rates and detect spikes
        var totalHours = period.TotalHours;
        foreach (var errorCode in errors.Select(e => e.ErrorCode).Distinct())
        {
            var totalErrorsForCode = errors.Count(e => e.ErrorCode == errorCode);
            var averagePerHour = totalErrorsForCode / totalHours;
            var spikeThreshold = averagePerHour * _options.SpikeDetectionThreshold;

            foreach (var bucket in hourlyBuckets)
            {
                if (bucket.Value.TryGetValue(errorCode, out var hourlyCount) && hourlyCount > spikeThreshold)
                {
                    spikes.Add(new ErrorSpike(
                        bucket.Key,
                        bucket.Key.AddHours(1),
                        errorCode,
                        hourlyCount,
                        hourlyCount / Math.Max(averagePerHour, 1)));
                }
            }
        }

        return await Task.FromResult(spikes);
    }

    private async Task<List<ErrorPattern>> DetectErrorPatternsInPeriodAsync(List<ErrorTrackingInfo> errors)
    {
        var patterns = new List<ErrorPattern>();

        // Group by correlation ID and look for patterns
        var errorsByCorrelation = errors.GroupBy(e => e.CorrelationId);

        foreach (var correlationGroup in errorsByCorrelation)
        {
            var sortedErrors = correlationGroup.OrderBy(e => e.Timestamp).ToList();
            if (sortedErrors.Count >= 2)
            {
                var sequence = sortedErrors.Select(e => e.ErrorCode).ToList();
                // This would implement more sophisticated pattern detection
            }
        }

        return await Task.FromResult(patterns);
    }
}