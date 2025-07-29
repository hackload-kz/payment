using PaymentGateway.Core.Enums;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;

namespace PaymentGateway.Core.Services;

public interface IErrorRetryService
{
    Task<T> ExecuteWithRetryAsync<T>(Func<Task<T>> operation, RetryPolicy policy, CancellationToken cancellationToken = default);
    Task ExecuteWithRetryAsync(Func<Task> operation, RetryPolicy policy, CancellationToken cancellationToken = default);
    bool ShouldRetry(PaymentErrorCode errorCode, int attemptNumber);
    TimeSpan GetRetryDelay(PaymentErrorCode errorCode, int attemptNumber);
    RetryPolicy GetRetryPolicyForError(PaymentErrorCode errorCode);
    Task<RetryAttemptResult> RecordRetryAttemptAsync(string operationId, PaymentErrorCode errorCode, int attemptNumber, bool wasSuccessful);
}

public record RetryPolicy(
    int MaxAttempts,
    TimeSpan BaseDelay,
    TimeSpan MaxDelay,
    double BackoffMultiplier,
    bool EnableJitter,
    List<PaymentErrorCode> RetryableErrors);

public record RetryAttemptResult(
    string OperationId,
    int AttemptNumber,
    PaymentErrorCode ErrorCode,
    DateTime AttemptTime,
    TimeSpan RetryDelay,
    bool WasSuccessful,
    string? ErrorMessage);

public class ErrorRetryOptions
{
    public RetryPolicy DefaultPolicy { get; set; } = new(
        MaxAttempts: 3,
        BaseDelay: TimeSpan.FromSeconds(1),
        MaxDelay: TimeSpan.FromMinutes(5),
        BackoffMultiplier: 2.0,
        EnableJitter: true,
        RetryableErrors: new List<PaymentErrorCode>());

    public Dictionary<string, RetryPolicy> PolicyByCategory { get; set; } = new();
    public bool EnableRetryTracking { get; set; } = true;
    public TimeSpan RetryTrackingExpiry { get; set; } = TimeSpan.FromHours(24);
}

public class ErrorRetryService : IErrorRetryService
{
    private readonly ILogger<ErrorRetryService> _logger;
    private readonly IErrorCategorizationService _categorizationService;
    private readonly ErrorRetryOptions _options;
    private readonly ConcurrentDictionary<string, List<RetryAttemptResult>> _retryHistory;

    private static readonly RetryPolicy TemporaryIssuesPolicy = new(
        MaxAttempts: 5,
        BaseDelay: TimeSpan.FromSeconds(30),
        MaxDelay: TimeSpan.FromMinutes(5),
        BackoffMultiplier: 1.5,
        EnableJitter: true,
        RetryableErrors: new List<PaymentErrorCode>
        {
            PaymentErrorCode.ServiceTemporarilyUnavailable,
            PaymentErrorCode.ServiceTemporarilyUnavailable2,
            PaymentErrorCode.TemporaryProcessingIssue,
            PaymentErrorCode.TemporaryProcessingIssue2,
            PaymentErrorCode.TemporaryProcessingIssue3,
            PaymentErrorCode.TemporarySystemIssue,
            PaymentErrorCode.RepeatOperationLater
        });

    private static readonly RetryPolicy ExternalServicePolicy = new(
        MaxAttempts: 3,
        BaseDelay: TimeSpan.FromMinutes(1),
        MaxDelay: TimeSpan.FromMinutes(10),
        BackoffMultiplier: 2.0,
        EnableJitter: true,
        RetryableErrors: new List<PaymentErrorCode>
        {
            PaymentErrorCode.ExternalServiceUnavailable,
            PaymentErrorCode.AcqApiServiceError,
            PaymentErrorCode.TimeoutWaitingForCreditSystemResponse
        });

    private static readonly RetryPolicy SystemErrorPolicy = new(
        MaxAttempts: 2,
        BaseDelay: TimeSpan.FromMinutes(5),
        MaxDelay: TimeSpan.FromMinutes(15),
        BackoffMultiplier: 3.0,
        EnableJitter: false,
        RetryableErrors: new List<PaymentErrorCode>
        {
            PaymentErrorCode.InternalRequestProcessingError
        });

    public ErrorRetryService(
        ILogger<ErrorRetryService> logger,
        IErrorCategorizationService categorizationService,
        IOptions<ErrorRetryOptions> options)
    {
        _logger = logger;
        _categorizationService = categorizationService;
        _options = options.Value;
        _retryHistory = new ConcurrentDictionary<string, List<RetryAttemptResult>>();

        InitializeRetryPolicies();
    }

    public async Task<T> ExecuteWithRetryAsync<T>(Func<Task<T>> operation, RetryPolicy policy, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(policy);

        var operationId = Guid.NewGuid().ToString();
        Exception? lastException = null;

        for (int attempt = 1; attempt <= policy.MaxAttempts; attempt++)
        {
            try
            {
                _logger.LogDebug("Executing operation {OperationId}, attempt {Attempt}/{MaxAttempts}", 
                    operationId, attempt, policy.MaxAttempts);

                var result = await operation();
                
                if (_options.EnableRetryTracking)
                {
                    await RecordRetryAttemptAsync(operationId, PaymentErrorCode.Success, attempt, true);
                }

                return result;
            }
            catch (Exception ex) when (ShouldRetryException(ex, attempt, policy))
            {
                lastException = ex;
                var errorCode = ExtractErrorCodeFromException(ex);
                var retryDelay = CalculateRetryDelay(policy, attempt);

                _logger.LogWarning("Operation {OperationId} failed on attempt {Attempt}, retrying in {RetryDelay}ms. Error: {Error}",
                    operationId, attempt, retryDelay.TotalMilliseconds, ex.Message);

                if (_options.EnableRetryTracking)
                {
                    await RecordRetryAttemptAsync(operationId, errorCode, attempt, false);
                }

                if (attempt < policy.MaxAttempts)
                {
                    await Task.Delay(retryDelay, cancellationToken);
                }
            }
        }

        _logger.LogError("Operation {OperationId} failed after {MaxAttempts} attempts", operationId, policy.MaxAttempts);
        throw lastException ?? new InvalidOperationException("Operation failed after all retry attempts");
    }

    public async Task ExecuteWithRetryAsync(Func<Task> operation, RetryPolicy policy, CancellationToken cancellationToken = default)
    {
        await ExecuteWithRetryAsync(async () =>
        {
            await operation();
            return true;
        }, policy, cancellationToken);
    }

    public bool ShouldRetry(PaymentErrorCode errorCode, int attemptNumber)
    {
        var policy = GetRetryPolicyForError(errorCode);
        return attemptNumber < policy.MaxAttempts && policy.RetryableErrors.Contains(errorCode);
    }

    public TimeSpan GetRetryDelay(PaymentErrorCode errorCode, int attemptNumber)
    {
        var policy = GetRetryPolicyForError(errorCode);
        return CalculateRetryDelay(policy, attemptNumber);
    }

    public RetryPolicy GetRetryPolicyForError(PaymentErrorCode errorCode)
    {
        var categoryInfo = _categorizationService.GetErrorCategoryInfo(errorCode);
        
        return categoryInfo.Category switch
        {
            ErrorCategory.TemporaryIssues => TemporaryIssuesPolicy,
            ErrorCategory.System when errorCode == PaymentErrorCode.ExternalServiceUnavailable => ExternalServicePolicy,
            ErrorCategory.System => SystemErrorPolicy,
            _ => _options.DefaultPolicy
        };
    }

    public async Task<RetryAttemptResult> RecordRetryAttemptAsync(string operationId, PaymentErrorCode errorCode, int attemptNumber, bool wasSuccessful)
    {
        var attemptResult = new RetryAttemptResult(
            operationId,
            attemptNumber,
            errorCode,
            DateTime.UtcNow,
            GetRetryDelay(errorCode, attemptNumber),
            wasSuccessful,
            wasSuccessful ? null : errorCode.ToString());

        if (_options.EnableRetryTracking)
        {
            _retryHistory.AddOrUpdate(operationId,
                new List<RetryAttemptResult> { attemptResult },
                (_, existing) =>
                {
                    existing.Add(attemptResult);
                    return existing;
                });

            // Clean up old entries
            await CleanupOldRetryHistoryAsync();
        }

        return attemptResult;
    }

    private void InitializeRetryPolicies()
    {
        if (!_options.PolicyByCategory.ContainsKey("TemporaryIssues"))
        {
            _options.PolicyByCategory["TemporaryIssues"] = TemporaryIssuesPolicy;
        }

        if (!_options.PolicyByCategory.ContainsKey("ExternalService"))
        {
            _options.PolicyByCategory["ExternalService"] = ExternalServicePolicy;
        }

        if (!_options.PolicyByCategory.ContainsKey("SystemError"))
        {
            _options.PolicyByCategory["SystemError"] = SystemErrorPolicy;
        }
    }

    private bool ShouldRetryException(Exception exception, int attemptNumber, RetryPolicy policy)
    {
        if (attemptNumber >= policy.MaxAttempts)
            return false;

        var errorCode = ExtractErrorCodeFromException(exception);
        return policy.RetryableErrors.Contains(errorCode) || 
               (errorCode == PaymentErrorCode.InternalRequestProcessingError && 
                IsTransientException(exception));
    }

    private PaymentErrorCode ExtractErrorCodeFromException(Exception exception)
    {
        return exception switch
        {
            PaymentException paymentEx => paymentEx.ErrorCode,
            TimeoutException => PaymentErrorCode.ServiceTemporarilyUnavailable,
            HttpRequestException => PaymentErrorCode.ExternalServiceUnavailable,
            InvalidOperationException => PaymentErrorCode.InternalRequestProcessingError,
            _ => PaymentErrorCode.InternalRequestProcessingError
        };
    }

    private bool IsTransientException(Exception exception)
    {
        var transientExceptionTypes = new[]
        {
            typeof(TimeoutException),
            typeof(HttpRequestException),
            typeof(TaskCanceledException),
            typeof(OperationCanceledException)
        };

        return transientExceptionTypes.Contains(exception.GetType()) ||
               (exception.InnerException != null && IsTransientException(exception.InnerException)) ||
               exception.Message.Contains("timeout", StringComparison.OrdinalIgnoreCase) ||
               exception.Message.Contains("connection", StringComparison.OrdinalIgnoreCase) ||
               exception.Message.Contains("network", StringComparison.OrdinalIgnoreCase);
    }

    private TimeSpan CalculateRetryDelay(RetryPolicy policy, int attemptNumber)
    {
        var baseDelayMs = policy.BaseDelay.TotalMilliseconds;
        var exponentialDelay = baseDelayMs * Math.Pow(policy.BackoffMultiplier, attemptNumber - 1);
        
        // Cap at max delay
        exponentialDelay = Math.Min(exponentialDelay, policy.MaxDelay.TotalMilliseconds);

        if (policy.EnableJitter)
        {
            // Add ±25% jitter
            var jitterRange = exponentialDelay * 0.25;
            var jitter = Random.Shared.NextDouble() * jitterRange * 2 - jitterRange;
            exponentialDelay += jitter;
        }

        return TimeSpan.FromMilliseconds(Math.Max(exponentialDelay, 0));
    }

    private async Task CleanupOldRetryHistoryAsync()
    {
        try
        {
            var cutoffTime = DateTime.UtcNow - _options.RetryTrackingExpiry;
            var keysToRemove = new List<string>();

            foreach (var kvp in _retryHistory)
            {
                var attempts = kvp.Value;
                var hasRecentAttempts = attempts.Any(a => a.AttemptTime > cutoffTime);
                
                if (!hasRecentAttempts)
                {
                    keysToRemove.Add(kvp.Key);
                }
            }

            foreach (var key in keysToRemove)
            {
                _retryHistory.TryRemove(key, out _);
            }

            if (keysToRemove.Count > 0)
            {
                _logger.LogDebug("Cleaned up {Count} old retry history entries", keysToRemove.Count);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during retry history cleanup");
        }

        await Task.CompletedTask;
    }

    public Dictionary<string, List<RetryAttemptResult>> GetRetryHistory()
    {
        return _retryHistory.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.ToList());
    }

    public async Task<RetryStatistics> GetRetryStatisticsAsync(TimeSpan? period = null)
    {
        var actualPeriod = period ?? TimeSpan.FromHours(1);
        var cutoffTime = DateTime.UtcNow - actualPeriod;

        var recentAttempts = _retryHistory.Values
            .SelectMany(attempts => attempts)
            .Where(attempt => attempt.AttemptTime > cutoffTime)
            .ToList();

        var totalAttempts = recentAttempts.Count;
        var successfulOperations = recentAttempts.Count(a => a.WasSuccessful);
        var failedOperations = recentAttempts.Count(a => !a.WasSuccessful);
        var uniqueOperations = recentAttempts.Select(a => a.OperationId).Distinct().Count();

        var errorBreakdown = recentAttempts
            .Where(a => !a.WasSuccessful)
            .GroupBy(a => a.ErrorCode)
            .ToDictionary(g => g.Key, g => g.Count());

        var averageRetryDelay = recentAttempts
            .Where(a => !a.WasSuccessful)
            .Select(a => a.RetryDelay.TotalMilliseconds)
            .DefaultIfEmpty(0)
            .Average();

        return await Task.FromResult(new RetryStatistics(
            totalAttempts,
            successfulOperations,
            failedOperations,
            uniqueOperations,
            errorBreakdown,
            TimeSpan.FromMilliseconds(averageRetryDelay),
            actualPeriod));
    }
}

public record RetryStatistics(
    int TotalAttempts,
    int SuccessfulAttempts,
    int FailedAttempts,
    int UniqueOperations,
    Dictionary<PaymentErrorCode, int> ErrorBreakdown,
    TimeSpan AverageRetryDelay,
    TimeSpan Period);

// Extension methods for easier retry usage
public static class RetryExtensions
{
    public static async Task<T> WithRetryAsync<T>(this Task<T> task, IErrorRetryService retryService, PaymentErrorCode errorCode)
    {
        var policy = retryService.GetRetryPolicyForError(errorCode);
        return await retryService.ExecuteWithRetryAsync(() => task, policy);
    }

    public static async Task WithRetryAsync(this Task task, IErrorRetryService retryService, PaymentErrorCode errorCode)
    {
        var policy = retryService.GetRetryPolicyForError(errorCode);
        await retryService.ExecuteWithRetryAsync(() => task, policy);
    }
}