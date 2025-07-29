// SPDX-License-Identifier: MIT
// Copyright (c) 2025 HackLoad Payment Gateway

using PaymentGateway.Core.Entities;
using PaymentGateway.Core.Interfaces;
using PaymentGateway.Core.Repositories;
using PaymentGateway.Core.Enums;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.Concurrent;
using Prometheus;
using System.Text.Json;

namespace PaymentGateway.Core.Services;

/// <summary>
/// Advanced payment retry service with intelligent retry policies and failure analysis
/// </summary>
public interface IPaymentRetryService
{
    Task<RetryResult> RetryPaymentAsync(long paymentId, PaymentRetryPolicy policy = null, CancellationToken cancellationToken = default);
    Task<RetryResult> RetryPaymentWithCustomPolicyAsync(long paymentId, Func<int, TimeSpan> retryDelayCalculator, int maxRetries, CancellationToken cancellationToken = default);
    Task ScheduleRetryAsync(long paymentId, DateTime retryAt, PaymentRetryPolicy policy = null, CancellationToken cancellationToken = default);
    Task<IEnumerable<PaymentRetryAttempt>> GetRetryHistoryAsync(long paymentId, CancellationToken cancellationToken = default);
    Task<RetryAnalytics> GetRetryAnalyticsAsync(int? teamId = null, TimeSpan? period = null, CancellationToken cancellationToken = default);
    Task ProcessScheduledRetriesAsync(CancellationToken cancellationToken = default);
    Task<bool> ShouldRetryPaymentAsync(long paymentId, string errorCode, CancellationToken cancellationToken = default);
    Task<PaymentRetryPolicy> GetOptimalRetryPolicyAsync(long paymentId, CancellationToken cancellationToken = default);
}

public class PaymentRetryPolicy
{
    public int MaxRetries { get; set; } = 3;
    public TimeSpan InitialDelay { get; set; } = TimeSpan.FromSeconds(1);
    public double BackoffMultiplier { get; set; } = 2.0;
    public TimeSpan MaxDelay { get; set; } = TimeSpan.FromMinutes(30);
    public bool UseJitter { get; set; } = true;
    public HashSet<string> RetryableErrorCodes { get; set; } = new()
    {
        "NETWORK_ERROR",
        "TIMEOUT_ERROR", 
        "TEMPORARY_UNAVAILABLE",
        "RATE_LIMITED",
        "PROCESSING_ERROR"
    };
    public HashSet<string> NonRetryableErrorCodes { get; set; } = new()
    {
        "INVALID_CARD",
        "INSUFFICIENT_FUNDS",
        "CARD_EXPIRED",
        "AUTHENTICATION_FAILED",
        "FRAUD_DETECTED"
    };
    public Func<int, TimeSpan> CustomDelayCalculator { get; set; }
}

public class RetryResult
{
    public long PaymentId { get; set; }
    public bool IsSuccess { get; set; }
    public bool ShouldRetry { get; set; }
    public int AttemptsUsed { get; set; }
    public TimeSpan TotalRetryDuration { get; set; }
    public PaymentStatus? FinalStatus { get; set; }
    public List<string> Errors { get; set; } = new();
    public List<RetryAttemptResult> AttemptResults { get; set; } = new();
    public DateTime? NextRetryAt { get; set; }

    public static RetryResult Success(long paymentId, int attempts, TimeSpan duration, PaymentStatus status) =>
        new() { PaymentId = paymentId, IsSuccess = true, AttemptsUsed = attempts, TotalRetryDuration = duration, FinalStatus = status };

    public static RetryResult Failure(long paymentId, int attempts, TimeSpan duration, params string[] errors) =>
        new() { PaymentId = paymentId, IsSuccess = false, AttemptsUsed = attempts, TotalRetryDuration = duration, Errors = errors.ToList() };
}

public class PaymentRetryAttemptResult
{
    public int AttemptNumber { get; set; }
    public DateTime AttemptedAt { get; set; }
    public bool IsSuccess { get; set; }
    public string ErrorCode { get; set; }
    public string ErrorMessage { get; set; }
    public TimeSpan Duration { get; set; }
    public PaymentStatus? StatusBefore { get; set; }
    public PaymentStatus? StatusAfter { get; set; }
}

public class PaymentRetryAttempt : BaseEntity
{
    public long PaymentId { get; set; }
    public int AttemptNumber { get; set; }
    public DateTime AttemptedAt { get; set; }
    public bool IsSuccess { get; set; }
    public string ErrorCode { get; set; }
    public string ErrorMessage { get; set; }
    public TimeSpan Duration { get; set; }
    public PaymentStatus StatusBefore { get; set; }
    public PaymentStatus StatusAfter { get; set; }
    public string RetryPolicyUsed { get; set; }
    public Dictionary<string, object> Metadata { get; set; } = new();
    
    // Navigation
    public Payment Payment { get; set; }
}

public class RetryAnalytics
{
    public int TotalRetryAttempts { get; set; }
    public int SuccessfulRetries { get; set; }
    public int FailedRetries { get; set; }
    public double SuccessRate { get; set; }
    public TimeSpan AverageRetryTime { get; set; }
    public Dictionary<string, int> ErrorCodeFrequency { get; set; } = new();
    public Dictionary<int, int> RetriesByAttemptNumber { get; set; } = new();
    public Dictionary<string, double> RetrySuccessRateByErrorCode { get; set; } = new();
}

public class PaymentRetryService : IPaymentRetryService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IPaymentRepository _paymentRepository;
    private readonly IDistributedLockService _distributedLockService;
    private readonly ILogger<PaymentRetryService> _logger;
    
    // Retry state tracking
    private readonly ConcurrentDictionary<long, DateTime> _scheduledRetries = new();
    private readonly ConcurrentDictionary<long, int> _retryCounters = new();
    
    // Metrics
    private static readonly Counter RetryOperations = Metrics
        .CreateCounter("payment_retry_operations_total", "Total payment retry operations", new[] { "result", "attempt_number" });
    
    private static readonly Histogram RetryDuration = Metrics
        .CreateHistogram("payment_retry_duration_seconds", "Payment retry operation duration");
    
    private static readonly Gauge ScheduledRetriesGauge = Metrics
        .CreateGauge("scheduled_payment_retries_total", "Total scheduled payment retries");

    // Default retry policies
    private static readonly RetryPolicy DefaultPolicy = new();
    private static readonly RetryPolicy AggressivePolicy = new()
    {
        MaxRetries = 5,
        InitialDelay = TimeSpan.FromMilliseconds(500),
        BackoffMultiplier = 1.5,
        MaxDelay = TimeSpan.FromMinutes(10)
    };
    private static readonly RetryPolicy ConservativePolicy = new()
    {
        MaxRetries = 2,
        InitialDelay = TimeSpan.FromSeconds(5),
        BackoffMultiplier = 3.0,
        MaxDelay = TimeSpan.FromHours(1)
    };

    public PaymentRetryService(
        IServiceProvider serviceProvider,
        IPaymentRepository paymentRepository,
        IDistributedLockService distributedLockService,
        ILogger<PaymentRetryService> logger)
    {
        _serviceProvider = serviceProvider;
        _paymentRepository = paymentRepository;
        _distributedLockService = distributedLockService;
        _logger = logger;
    }

    public async Task<RetryResult> RetryPaymentAsync(long paymentId, PaymentRetryPolicy policy = null, CancellationToken cancellationToken = default)
    {
        using var activity = RetryDuration.NewTimer();
        var startTime = DateTime.UtcNow;
        var lockKey = $"payment:retry:{paymentId}";
        
        try
        {
            policy ??= await GetOptimalRetryPolicyAsync(paymentId, cancellationToken);
            
            await using var lockHandle = await _distributedLockService.AcquireLockAsync(lockKey, TimeSpan.FromMinutes(10), cancellationToken);
            if (lockHandle == null)
            {
                _logger.LogWarning("Failed to acquire retry lock for payment: {PaymentId}", paymentId);
                return RetryResult.Failure(paymentId, 0, TimeSpan.Zero, "Failed to acquire retry lock");
            }

            // Get payment and validate retry eligibility
            var payment = await _paymentRepository.GetByIdAsync(paymentId, cancellationToken);
            if (payment == null)
            {
                return RetryResult.Failure(paymentId, 0, TimeSpan.Zero, "Payment not found");
            }

            var shouldRetry = await ShouldRetryPaymentAsync(paymentId, payment.ErrorCode, cancellationToken);
            if (!shouldRetry)
            {
                return RetryResult.Failure(paymentId, 0, TimeSpan.Zero, "Payment is not eligible for retry");
            }

            var currentRetryCount = _retryCounters.GetOrAdd(paymentId, 0);
            var result = new RetryResult
            {
                PaymentId = paymentId,
                AttemptsUsed = 0,
                AttemptResults = new List<RetryAttemptResult>()
            };

            // Perform retry attempts
            for (int attempt = 1; attempt <= policy.MaxRetries; attempt++)
            {
                if (currentRetryCount + attempt > policy.MaxRetries)
                {
                    _logger.LogWarning("Max retry limit exceeded for payment: {PaymentId}", paymentId);
                    break;
                }

                var attemptStartTime = DateTime.UtcNow;
                var attemptResult = new RetryAttemptResult
                {
                    AttemptNumber = currentRetryCount + attempt,
                    AttemptedAt = attemptStartTime,
                    StatusBefore = payment.Status
                };

                try
                {
                    // Calculate delay for this attempt
                    if (attempt > 1)
                    {
                        var delay = CalculateRetryDelay(attempt - 1, policy);
                        await Task.Delay(delay, cancellationToken);
                    }

                    // Attempt to process payment
                    using var scope = _serviceProvider.CreateScope();
                    var processingEngine = scope.ServiceProvider.GetRequiredService<IConcurrentPaymentProcessingEngineService>();
                    
                    var processingResult = await processingEngine.ProcessPaymentAsync(paymentId, new ProcessingOptions
                    {
                        MaxRetries = 1, // Don't double-retry
                        Timeout = TimeSpan.FromMinutes(5),
                        Priority = ProcessingPriority.High
                    }, cancellationToken);

                    attemptResult.IsSuccess = processingResult.IsSuccess;
                    attemptResult.StatusAfter = processingResult.ResultStatus;
                    attemptResult.Duration = DateTime.UtcNow - attemptStartTime;

                    if (processingResult.IsSuccess)
                    {
                        attemptResult.ErrorCode = null;
                        attemptResult.ErrorMessage = null;
                        
                        result.IsSuccess = true;
                        result.FinalStatus = processingResult.ResultStatus;
                        result.AttemptsUsed = attempt;
                        
                        // Record successful retry
                        await RecordRetryAttemptAsync(paymentId, attemptResult, policy, cancellationToken);
                        
                        RetryOperations.WithLabels("success", attempt.ToString()).Inc();
                        _logger.LogInformation("Payment retry successful: {PaymentId}, Attempt: {Attempt}", paymentId, attempt);
                        
                        // Update retry counter
                        _retryCounters.AddOrUpdate(paymentId, currentRetryCount + attempt, (k, v) => currentRetryCount + attempt);
                        
                        result.TotalRetryDuration = DateTime.UtcNow - startTime;
                        return result;
                    }
                    else
                    {
                        attemptResult.ErrorCode = processingResult.Errors.FirstOrDefault() ?? "UNKNOWN_ERROR";
                        attemptResult.ErrorMessage = string.Join(", ", processingResult.Errors);
                        
                        // Check if error is retryable
                        if (!IsRetryableError(attemptResult.ErrorCode, policy))
                        {
                            _logger.LogWarning("Non-retryable error encountered: {PaymentId}, Error: {ErrorCode}", 
                                paymentId, attemptResult.ErrorCode);
                            break;
                        }
                    }
                }
                catch (Exception ex)
                {
                    attemptResult.IsSuccess = false;
                    attemptResult.ErrorCode = "RETRY_EXCEPTION";
                    attemptResult.ErrorMessage = ex.Message;
                    attemptResult.Duration = DateTime.UtcNow - attemptStartTime;
                    
                    _logger.LogError(ex, "Retry attempt failed with exception: {PaymentId}, Attempt: {Attempt}", paymentId, attempt);
                }

                result.AttemptResults.Add(attemptResult);
                await RecordRetryAttemptAsync(paymentId, attemptResult, policy, cancellationToken);
                
                RetryOperations.WithLabels("failed", attempt.ToString()).Inc();
            }

            // All retry attempts failed
            result.IsSuccess = false;
            result.AttemptsUsed = result.AttemptResults.Count;
            result.Errors = result.AttemptResults.Select(r => r.ErrorMessage).Where(e => !string.IsNullOrEmpty(e)).ToList();
            result.TotalRetryDuration = DateTime.UtcNow - startTime;
            
            // Update retry counter
            _retryCounters.AddOrUpdate(paymentId, currentRetryCount + result.AttemptsUsed, (k, v) => currentRetryCount + result.AttemptsUsed);
            
            _logger.LogWarning("All retry attempts failed for payment: {PaymentId}, Attempts: {AttemptsUsed}", 
                paymentId, result.AttemptsUsed);
            
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Payment retry service failed: {PaymentId}", paymentId);
            return RetryResult.Failure(paymentId, 0, DateTime.UtcNow - startTime, "Retry service error");
        }
    }

    public async Task<RetryResult> RetryPaymentWithCustomPolicyAsync(long paymentId, Func<int, TimeSpan> retryDelayCalculator, int maxRetries, CancellationToken cancellationToken = default)
    {
        var customPolicy = new PaymentRetryPolicy
        {
            MaxRetries = maxRetries,
            CustomDelayCalculator = retryDelayCalculator
        };

        return await RetryPaymentAsync(paymentId, customPolicy, cancellationToken);
    }

    public async Task ScheduleRetryAsync(long paymentId, DateTime retryAt, PaymentRetryPolicy policy = null, CancellationToken cancellationToken = default)
    {
        try
        {
            _scheduledRetries.AddOrUpdate(paymentId, retryAt, (k, v) => retryAt);
            ScheduledRetriesGauge.Inc();
            
            _logger.LogInformation("Retry scheduled for payment: {PaymentId}, RetryAt: {RetryAt}", paymentId, retryAt);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to schedule retry for payment: {PaymentId}", paymentId);
            throw;
        }
    }

    public async Task<IEnumerable<PaymentRetryAttempt>> GetRetryHistoryAsync(long paymentId, CancellationToken cancellationToken = default)
    {
        try
        {
            // This would typically query a retry attempts repository
            // For now, return empty collection as this requires additional repository setup
            return new List<PaymentRetryAttempt>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get retry history for payment: {PaymentId}", paymentId);
            return new List<PaymentRetryAttempt>();
        }
    }

    public async Task<RetryAnalytics> GetRetryAnalyticsAsync(int? teamId = null, TimeSpan? period = null, CancellationToken cancellationToken = default)
    {
        try
        {
            // This would typically aggregate data from retry attempts repository
            // For now, return basic analytics
            return new RetryAnalytics
            {
                TotalRetryAttempts = _retryCounters.Count,
                SuccessfulRetries = 0,
                FailedRetries = 0,
                SuccessRate = 0.0,
                AverageRetryTime = TimeSpan.Zero
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get retry analytics");
            return new RetryAnalytics();
        }
    }

    public async Task ProcessScheduledRetriesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var now = DateTime.UtcNow;
            var readyForRetry = _scheduledRetries
                .Where(kvp => kvp.Value <= now)
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var paymentId in readyForRetry)
            {
                try
                {
                    if (_scheduledRetries.TryRemove(paymentId, out _))
                    {
                        ScheduledRetriesGauge.Dec();
                        await RetryPaymentAsync(paymentId, null, cancellationToken);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to process scheduled retry for payment: {PaymentId}", paymentId);
                    // Continue processing other retries
                }
            }

            if (readyForRetry.Count > 0)
            {
                _logger.LogInformation("Processed {Count} scheduled retries", readyForRetry.Count);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process scheduled retries");
            throw;
        }
    }

    public async Task<bool> ShouldRetryPaymentAsync(long paymentId, string errorCode, CancellationToken cancellationToken = default)
    {
        try
        {
            var payment = await _paymentRepository.GetByIdAsync(paymentId, cancellationToken);
            if (payment == null) return false;

            // Don't retry payments in final states
            if (payment.Status == PaymentStatus.CONFIRMED ||
                payment.Status == PaymentStatus.REFUNDED) return false;

            // Don't retry if too much time has passed
            if (DateTime.UtcNow - payment.CreatedAt > TimeSpan.FromHours(24)) return false;

            // Check retry count limits
            var currentRetryCount = _retryCounters.GetOrAdd(paymentId, 0);
            if (currentRetryCount >= DefaultPolicy.MaxRetries) return false;

            // Check if error code is retryable
            if (!string.IsNullOrEmpty(errorCode))
            {
                var policy = await GetOptimalRetryPolicyAsync(paymentId, cancellationToken);
                return IsRetryableError(errorCode, policy);
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to determine if payment should retry: {PaymentId}", paymentId);
            return false;
        }
    }

    public async Task<PaymentRetryPolicy> GetOptimalRetryPolicyAsync(long paymentId, CancellationToken cancellationToken = default)
    {
        try
        {
            var payment = await _paymentRepository.GetByIdAsync(paymentId, cancellationToken);
            if (payment == null) return DefaultPolicy;

            // Select policy based on payment characteristics
            return payment.Amount switch
            {
                > 100000 => ConservativePolicy, // High-value payments
                < 1000 => AggressivePolicy,     // Low-value payments
                _ => DefaultPolicy              // Normal payments
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get optimal retry policy for payment: {PaymentId}", paymentId);
            return DefaultPolicy;
        }
    }

    private TimeSpan CalculateRetryDelay(int attemptNumber, PaymentRetryPolicy policy)
    {
        if (policy.CustomDelayCalculator != null)
        {
            return policy.CustomDelayCalculator(attemptNumber);
        }

        var delay = TimeSpan.FromMilliseconds(
            policy.InitialDelay.TotalMilliseconds * Math.Pow(policy.BackoffMultiplier, attemptNumber));

        if (delay > policy.MaxDelay)
        {
            delay = policy.MaxDelay;
        }

        if (policy.UseJitter)
        {
            var jitter = Random.Shared.NextDouble() * 0.1; // 10% jitter
            delay = TimeSpan.FromMilliseconds(delay.TotalMilliseconds * (1 + jitter));
        }

        return delay;
    }

    private bool IsRetryableError(string errorCode, PaymentRetryPolicy policy)
    {
        if (string.IsNullOrEmpty(errorCode)) return true;

        if (policy.NonRetryableErrorCodes.Contains(errorCode)) return false;
        if (policy.RetryableErrorCodes.Contains(errorCode)) return true;

        // Default to non-retryable for unknown error codes
        return false;
    }

    private async Task RecordRetryAttemptAsync(long paymentId, PaymentRetryAttemptResult attemptResult, PaymentRetryPolicy policy, CancellationToken cancellationToken)
    {
        try
        {
            // This would typically save to a retry attempts repository
            _logger.LogDebug("Retry attempt recorded: {PaymentId}, Attempt: {AttemptNumber}, Success: {IsSuccess}", 
                paymentId, attemptResult.AttemptNumber, attemptResult.IsSuccess);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to record retry attempt: {PaymentId}", paymentId);
            // Don't throw - this is just logging
        }
    }
}