// SPDX-License-Identifier: MIT
// Copyright (c) 2025 HackLoad Payment Gateway

using PaymentGateway.Core.Entities;
using PaymentGateway.Core.Interfaces;
using PaymentGateway.Core.Repositories;
using PaymentGateway.Core.Enums;
using PaymentGateway.Core.DTOs.PaymentCheck;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Caching.Memory;
using System.Collections.Concurrent;
using Prometheus;
using System.Text.Json;

namespace PaymentGateway.Core.Services;

/// <summary>
/// Payment status checking service for order status queries and monitoring
/// </summary>
public interface IPaymentStatusCheckService
{
    Task<StatusCheckResult> CheckOrderStatusAsync(string orderId, Guid teamId, CancellationToken cancellationToken = default);
    Task<StatusCheckResult> CheckPaymentStatusAsync(Guid paymentId, CancellationToken cancellationToken = default);
    Task<StatusCheckResult> CheckPaymentByPaymentIdAsync(string paymentId, CancellationToken cancellationToken = default);
    Task<IEnumerable<PaymentStatusHistory>> GetPaymentStatusHistoryAsync(Guid paymentId, CancellationToken cancellationToken = default);
    Task<StatusCheckStatistics> GetStatusCheckStatisticsAsync(Guid? teamId = null, TimeSpan? period = null, CancellationToken cancellationToken = default);
    Task<IEnumerable<Payment>> GetActivePaymentsAsync(Guid teamId, int limit = 100, CancellationToken cancellationToken = default);
    Task<bool> IsPaymentStatusFinalAsync(PaymentStatus status);
    Task<PaymentStatusSummary> GetPaymentStatusSummaryAsync(Guid teamId, DateTime? fromDate = null, DateTime? toDate = null, CancellationToken cancellationToken = default);
    
    // Methods needed by PaymentCheckController
    Task<PaymentCheckResponseDto> CheckPaymentByIdAsync(string paymentId, PaymentCheckRequestDto request, CancellationToken cancellationToken = default);
    Task<PaymentCheckResponseDto> CheckPaymentsByOrderIdAsync(string orderId, PaymentCheckRequestDto request, CancellationToken cancellationToken = default);
}

public class StatusCheckResult
{
    public string OrderId { get; set; }
    public Guid TeamId { get; set; }
    public string TeamSlug { get; set; }
    public bool Success { get; set; }
    public string ErrorCode { get; set; } = "0";
    public string Message { get; set; }
    public string Details { get; set; }
    public List<PaymentStatusInfo> Payments { get; set; } = new();
    public DateTime CheckedAt { get; set; } = DateTime.UtcNow;
    public TimeSpan ProcessingDuration { get; set; }
    public Dictionary<string, object> Metadata { get; set; } = new();
}

public class PaymentStatusInfo
{
    public long PaymentId { get; set; }
    public string PaymentIdString { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; }
    public PaymentStatus Status { get; set; }
    public string StatusDescription { get; set; }
    public DateTime CreatedDate { get; set; }
    public DateTime? CompletedDate { get; set; }
    public DateTime? AuthorizedAt { get; set; }
    public DateTime? ConfirmedAt { get; set; }
    public string ErrorCode { get; set; }
    public string ErrorMessage { get; set; }
    public Dictionary<string, object> PaymentMetadata { get; set; } = new();
}

public class PaymentStatusHistory : BaseEntity
{
    public long PaymentId { get; set; }
    public PaymentStatus PreviousStatus { get; set; }
    public PaymentStatus NewStatus { get; set; }
    public string ChangeReason { get; set; }
    public string TriggeredBy { get; set; }
    public DateTime StatusChangedAt { get; set; }
    public TimeSpan TransitionDuration { get; set; }
    public Dictionary<string, object> ChangeMetadata { get; set; } = new();
    
    // Navigation
    public Payment Payment { get; set; }
}

public class StatusCheckStatistics
{
    public TimeSpan Period { get; set; }
    public int TotalStatusChecks { get; set; }
    public int SuccessfulChecks { get; set; }
    public int FailedChecks { get; set; }
    public double SuccessRate { get; set; }
    public TimeSpan AverageCheckTime { get; set; }
    public Dictionary<PaymentStatus, int> ChecksByStatus { get; set; } = new();
    public Dictionary<string, int> ChecksByTeam { get; set; } = new();
    public Dictionary<string, int> ErrorsByCode { get; set; } = new();
    public int CacheHits { get; set; }
    public int CacheMisses { get; set; }
    public double CacheHitRate { get; set; }
}

public class PaymentStatusSummary
{
    public Guid TeamId { get; set; }
    public DateTime FromDate { get; set; }
    public DateTime ToDate { get; set; }
    public int TotalPayments { get; set; }
    public Dictionary<PaymentStatus, PaymentStatusCount> StatusCounts { get; set; } = new();
    public decimal TotalAmount { get; set; }
    public decimal CompletedAmount { get; set; }
    public double CompletionRate { get; set; }
    public TimeSpan AverageProcessingTime { get; set; }
}

public class PaymentStatusCount
{
    public int Count { get; set; }
    public decimal TotalAmount { get; set; }
    public double Percentage { get; set; }
    public TimeSpan AverageAge { get; set; }
}

public class PaymentStatusCheckService : IPaymentStatusCheckService
{
    private readonly IPaymentRepository _paymentRepository;
    private readonly ITeamRepository _teamRepository;
    private readonly IMemoryCache _cache;
    private readonly ILogger<PaymentStatusCheckService> _logger;
    
    // Status change notification system
    private readonly ConcurrentDictionary<long, List<Func<PaymentStatusInfo, Task>>> _statusChangeSubscribers = new();
    
    // Rate limiting for status checks
    private readonly ConcurrentDictionary<string, DateTime> _lastCheckTimes = new();
    private readonly ConcurrentDictionary<string, int> _checkCounts = new();
    
    // Metrics
    private static readonly Counter StatusCheckOperations = Metrics
        .CreateCounter("payment_status_check_operations_total", "Total payment status check operations", new[] { "team_id", "result", "type" });
    
    private static readonly Histogram StatusCheckDuration = Metrics
        .CreateHistogram("payment_status_check_duration_seconds", "Payment status check operation duration");
    
    private static readonly Counter StatusCheckCacheHits = Metrics
        .CreateCounter("payment_status_check_cache_hits_total", "Total status check cache hits", new[] { "cache_type" });
    
    private static readonly Gauge ActivePaymentsByStatus = Metrics
        .CreateGauge("active_payments_by_status_total", "Total active payments by status", new[] { "team_id", "status" });

    // Status descriptions for user-friendly display
    private static readonly Dictionary<PaymentStatus, string> StatusDescriptions = new()
    {
        [PaymentStatus.INIT] = "Payment initialized",
        [PaymentStatus.NEW] = "New payment created",
        [PaymentStatus.PROCESSING] = "Payment being processed",
        [PaymentStatus.AUTHORIZED] = "Payment authorized, awaiting confirmation",
        [PaymentStatus.CONFIRMED] = "Payment successfully completed",
        [PaymentStatus.CANCELLED] = "Payment cancelled",
        [PaymentStatus.REFUNDED] = "Payment refunded",
        [PaymentStatus.EXPIRED] = "Payment expired"
    };

    // Final statuses that cannot change
    private static readonly HashSet<PaymentStatus> FinalStatuses = new()
    {
        PaymentStatus.CONFIRMED,
        PaymentStatus.CANCELLED,
        PaymentStatus.REFUNDED,
        PaymentStatus.EXPIRED
    };

    public PaymentStatusCheckService(
        IPaymentRepository paymentRepository,
        ITeamRepository teamRepository,
        IMemoryCache cache,
        ILogger<PaymentStatusCheckService> logger)
    {
        _paymentRepository = paymentRepository;
        _teamRepository = teamRepository;
        _cache = cache;
        _logger = logger;
    }

    public async Task<StatusCheckResult> CheckOrderStatusAsync(string orderId, Guid teamId, CancellationToken cancellationToken = default)
    {
        using var activity = StatusCheckDuration.NewTimer();
        var startTime = DateTime.UtcNow;
        
        try
        {
            // Rate limiting check
            var rateLimitKey = $"team:{teamId}:order:{orderId}";
            if (!CheckRateLimit(rateLimitKey))
            {
                StatusCheckOperations.WithLabels(teamId.ToString(), "rate_limited", "order").Inc();
                return new StatusCheckResult
                {
                    OrderId = orderId,
                    TeamId = teamId,
                    Success = false,
                    ErrorCode = "1029",
                    Message = "Rate limit exceeded for status checks",
                    ProcessingDuration = DateTime.UtcNow - startTime
                };
            }

            // Check cache first
            var cacheKey = $"status_check:order:{teamId}:{orderId}";
            if (_cache.TryGetValue(cacheKey, out StatusCheckResult cachedResult))
            {
                StatusCheckCacheHits.WithLabels("order").Inc();
                StatusCheckOperations.WithLabels(teamId.ToString(), "success", "order_cached").Inc();
                _logger.LogDebug("Status check cache hit for OrderId: {OrderId}, TeamId: {TeamId}", orderId, teamId);
                return cachedResult;
            }

            // Get team information
            var team = await _teamRepository.GetByIdAsync(teamId, cancellationToken);
            if (team == null)
            {
                StatusCheckOperations.WithLabels(teamId.ToString(), "failed", "team_not_found").Inc();
                return new StatusCheckResult
                {
                    OrderId = orderId,
                    TeamId = teamId,
                    Success = false,
                    ErrorCode = "1003",
                    Message = "Team not found",
                    ProcessingDuration = DateTime.UtcNow - startTime
                };
            }

            // Get all payments for this order and team
            var payments = await _paymentRepository.GetByTeamIdAsync(teamId, cancellationToken);
            var orderPayments = payments.Where(p => p.OrderId == orderId).OrderBy(p => p.CreatedAt).ToList();

            var result = new StatusCheckResult
            {
                OrderId = orderId,
                TeamId = teamId,
                TeamSlug = team.TeamSlug,
                Success = true,
                ErrorCode = "0",
                ProcessingDuration = DateTime.UtcNow - startTime
            };

            if (!orderPayments.Any())
            {
                result.Success = false;
                result.ErrorCode = "1004";
                result.Message = "Order not found";
                result.Details = $"No payments found for OrderId '{orderId}' in team '{teamId}'";
                
                StatusCheckOperations.WithLabels(teamId.ToString(), "failed", "order_not_found").Inc();
            }
            else
            {
                // Convert payments to status info
                result.Payments = orderPayments.Select(MapPaymentToStatusInfo).ToList();
                
                // Add metadata
                result.Metadata["total_payments"] = orderPayments.Count;
                result.Metadata["latest_status"] = orderPayments.Last().Status.ToString();
                result.Metadata["total_amount"] = orderPayments.Sum(p => p.Amount);
                
                StatusCheckOperations.WithLabels(teamId.ToString(), "success", "order").Inc();
                _logger.LogInformation("Order status retrieved: OrderId: {OrderId}, TeamId: {TeamId}, PaymentCount: {Count}", 
                    orderId, teamId, orderPayments.Count);
            }

            // Cache the result
            _cache.Set(cacheKey, result, TimeSpan.FromMinutes(2));
            
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to check order status: OrderId: {OrderId}, TeamId: {TeamId}", orderId, teamId);
            StatusCheckOperations.WithLabels(teamId.ToString(), "error", "order").Inc();
            
            return new StatusCheckResult
            {
                OrderId = orderId,
                TeamId = teamId,
                Success = false,
                ErrorCode = "1001",
                Message = "Internal service error",
                Details = "Failed to retrieve order status",
                ProcessingDuration = DateTime.UtcNow - startTime
            };
        }
    }

    public async Task<StatusCheckResult> CheckPaymentStatusAsync(Guid paymentId, CancellationToken cancellationToken = default)
    {
        using var activity = StatusCheckDuration.NewTimer();
        var startTime = DateTime.UtcNow;
        
        try
        {
            // Check cache first
            var cacheKey = $"status_check:payment:{paymentId}";
            if (_cache.TryGetValue(cacheKey, out StatusCheckResult cachedResult))
            {
                StatusCheckCacheHits.WithLabels("payment").Inc();
                return cachedResult;
            }

            var payment = await _paymentRepository.GetByIdAsync(new Guid(paymentId.ToString()), cancellationToken);
            if (payment == null)
            {
                StatusCheckOperations.WithLabels("unknown", "failed", "payment_not_found").Inc();
                return new StatusCheckResult
                {
                    Success = false,
                    ErrorCode = "1004",
                    Message = "Payment not found",
                    ProcessingDuration = DateTime.UtcNow - startTime
                };
            }

            var result = new StatusCheckResult
            {
                OrderId = payment.OrderId,
                TeamId = payment.TeamId,
                TeamSlug = payment.TeamSlug,
                Success = true,
                ErrorCode = "0",
                Payments = new List<PaymentStatusInfo> { MapPaymentToStatusInfo(payment) },
                ProcessingDuration = DateTime.UtcNow - startTime
            };

            // Cache the result
            _cache.Set(cacheKey, result, TimeSpan.FromMinutes(1));
            
            StatusCheckOperations.WithLabels(payment.TeamId.ToString(), "success", "payment").Inc();
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to check payment status: PaymentId: {PaymentId}", paymentId);
            StatusCheckOperations.WithLabels("unknown", "error", "payment").Inc();
            
            return new StatusCheckResult
            {
                Success = false,
                ErrorCode = "1001",
                Message = "Internal service error",
                ProcessingDuration = DateTime.UtcNow - startTime
            };
        }
    }

    public async Task<StatusCheckResult> CheckPaymentByPaymentIdAsync(string paymentId, CancellationToken cancellationToken = default)
    {
        try
        {
            var payment = await _paymentRepository.GetByPaymentIdAsync(paymentId, cancellationToken);
            if (payment == null)
            {
                return new StatusCheckResult
                {
                    Success = false,
                    ErrorCode = "1004",
                    Message = "Payment not found"
                };
            }

            // Fixed: Now using Guid directly instead of GetHashCode conversion
            return await CheckPaymentStatusAsync(payment.Id, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to check payment by PaymentId: {PaymentId}", paymentId);
            return new StatusCheckResult
            {
                Success = false,
                ErrorCode = "1001",
                Message = "Internal service error"
            };
        }
    }

    public async Task<IEnumerable<PaymentStatusHistory>> GetPaymentStatusHistoryAsync(Guid paymentId, CancellationToken cancellationToken = default)
    {
        try
        {
            // This would typically query a status history table
            // For now, return empty collection as this requires additional repository setup
            return new List<PaymentStatusHistory>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get payment status history: PaymentId: {PaymentId}", paymentId);
            return new List<PaymentStatusHistory>();
        }
    }

    public async Task<StatusCheckStatistics> GetStatusCheckStatisticsAsync(Guid? teamId = null, TimeSpan? period = null, CancellationToken cancellationToken = default)
    {
        try
        {
            period ??= TimeSpan.FromDays(7);
            
            // This would typically query status check logs or metrics
            // For now, return simulated statistics
            var stats = new StatusCheckStatistics
            {
                Period = period.Value,
                TotalStatusChecks = 5000,
                SuccessfulChecks = 4850,
                FailedChecks = 150,
                SuccessRate = 0.97,
                AverageCheckTime = TimeSpan.FromMilliseconds(250),
                ChecksByStatus = new Dictionary<PaymentStatus, int>
                {
                    [PaymentStatus.NEW] = 500,
                    [PaymentStatus.PROCESSING] = 300,
                    [PaymentStatus.AUTHORIZED] = 800,
                    [PaymentStatus.CONFIRMED] = 3200,
                    [PaymentStatus.CANCELLED] = 150,
                    [PaymentStatus.REFUNDED] = 50
                },
                CacheHits = 3500,
                CacheMisses = 1500,
                CacheHitRate = 0.70
            };

            return stats;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get status check statistics");
            return new StatusCheckStatistics();
        }
    }

    public async Task<IEnumerable<Payment>> GetActivePaymentsAsync(Guid teamId, int limit = 100, CancellationToken cancellationToken = default)
    {
        try
        {
            var allPayments = await _paymentRepository.GetByTeamIdAsync(teamId, cancellationToken);
            var activePayments = allPayments
                .Where(p => !FinalStatuses.Contains(p.Status))
                .OrderByDescending(p => p.CreatedAt)
                .Take(limit)
                .ToList();

            // Update metrics
            var statusGroups = activePayments.GroupBy(p => p.Status);
            foreach (var group in statusGroups)
            {
                ActivePaymentsByStatus.WithLabels(teamId.ToString(), group.Key.ToString()).Set(group.Count());
            }

            return activePayments;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get active payments for team: {TeamId}", teamId);
            return new List<Payment>();
        }
    }

    public async Task<bool> IsPaymentStatusFinalAsync(PaymentStatus status)
    {
        await Task.CompletedTask; // For async consistency
        return FinalStatuses.Contains(status);
    }

    public async Task<PaymentStatusSummary> GetPaymentStatusSummaryAsync(Guid teamId, DateTime? fromDate = null, DateTime? toDate = null, CancellationToken cancellationToken = default)
    {
        try
        {
            fromDate ??= DateTime.UtcNow.AddDays(-30);
            toDate ??= DateTime.UtcNow;

            var payments = await _paymentRepository.GetByTeamIdAsync(teamId, cancellationToken);
            var filteredPayments = payments
                .Where(p => p.CreatedAt >= fromDate && p.CreatedAt <= toDate)
                .ToList();

            var summary = new PaymentStatusSummary
            {
                TeamId = teamId,
                FromDate = fromDate.Value,
                ToDate = toDate.Value,
                TotalPayments = filteredPayments.Count,
                TotalAmount = filteredPayments.Sum(p => p.Amount)
            };

            var completedPayments = filteredPayments.Where(p => p.Status == PaymentStatus.CONFIRMED).ToList();
            summary.CompletedAmount = completedPayments.Sum(p => p.Amount);
            summary.CompletionRate = filteredPayments.Count > 0 ? (double)completedPayments.Count / filteredPayments.Count : 0;

            if (completedPayments.Count > 0)
            {
                var processingTimes = completedPayments
                    .Where(p => p.ConfirmedAt.HasValue)
                    .Select(p => p.ConfirmedAt!.Value - p.CreatedAt);
                
                if (processingTimes.Any())
                {
                    summary.AverageProcessingTime = TimeSpan.FromMilliseconds(processingTimes.Average(t => t.TotalMilliseconds));
                }
            }

            // Group by status
            var statusGroups = filteredPayments.GroupBy(p => p.Status);
            foreach (var group in statusGroups)
            {
                var count = group.Count();
                var amount = group.Sum(p => p.Amount);
                var avgAge = TimeSpan.FromMilliseconds(group.Average(p => (DateTime.UtcNow - p.CreatedAt).TotalMilliseconds));
                
                summary.StatusCounts[group.Key] = new PaymentStatusCount
                {
                    Count = count,
                    TotalAmount = amount,
                    Percentage = (double)count / filteredPayments.Count * 100,
                    AverageAge = avgAge
                };
            }

            return summary;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get payment status summary for team: {TeamId}", teamId);
            return new PaymentStatusSummary { TeamId = teamId };
        }
    }

    private PaymentStatusInfo MapPaymentToStatusInfo(Payment payment)
    {
        return new PaymentStatusInfo
        {
            PaymentId = payment.Id.GetHashCode(),
            PaymentIdString = payment.PaymentId,
            Amount = payment.Amount,
            Currency = payment.Currency ?? "RUB",
            Status = payment.Status,
            StatusDescription = StatusDescriptions.GetValueOrDefault(payment.Status, payment.Status.ToString()),
            CreatedDate = payment.CreatedAt,
            CompletedDate = payment.ConfirmedAt ?? payment.CancelledAt,
            AuthorizedAt = payment.AuthorizedAt,
            ConfirmedAt = payment.ConfirmedAt,
            ErrorCode = payment.ErrorCode,
            ErrorMessage = payment.ErrorMessage,
            PaymentMetadata = new Dictionary<string, object>
            {
                ["order_id"] = payment.OrderId,
                ["team_slug"] = payment.TeamSlug,
                ["customer_email"] = payment.CustomerEmail ?? "",
                ["description"] = payment.Description ?? "",
                ["is_final"] = FinalStatuses.Contains(payment.Status)
            }
        };
    }

    private bool CheckRateLimit(string key)
    {
        var now = DateTime.UtcNow;
        var timeWindow = TimeSpan.FromMinutes(1);
        var maxRequests = 100; // per minute per key

        // Clean old entries
        var expiredKeys = _lastCheckTimes
            .Where(kvp => now - kvp.Value > timeWindow)
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var expiredKey in expiredKeys)
        {
            _lastCheckTimes.TryRemove(expiredKey, out _);
            _checkCounts.TryRemove(expiredKey, out _);
        }

        // Check current rate
        if (_lastCheckTimes.TryGetValue(key, out var lastCheck))
        {
            if (now - lastCheck < timeWindow)
            {
                var currentCount = _checkCounts.GetOrAdd(key, 0);
                if (currentCount >= maxRequests)
                {
                    return false; // Rate limit exceeded
                }
                _checkCounts.TryUpdate(key, currentCount + 1, currentCount);
            }
            else
            {
                // Reset counter for new time window
                _checkCounts[key] = 1;
                _lastCheckTimes[key] = now;
            }
        }
        else
        {
            // First request for this key
            _lastCheckTimes[key] = now;
            _checkCounts[key] = 1;
        }

        return true;
    }

    // Methods needed by PaymentCheckController
    public async Task<PaymentCheckResponseDto> CheckPaymentByIdAsync(string paymentId, PaymentCheckRequestDto request, CancellationToken cancellationToken = default)
    {
        try
        {
            // Convert PaymentId string to find the payment
            var payment = await _paymentRepository.GetByPaymentIdAsync(paymentId, cancellationToken);
            
            if (payment == null)
            {
                return new PaymentCheckResponseDto
                {
                    Success = false,
                    ErrorCode = "1404",
                    Message = "Payment not found",
                    Payments = new List<PaymentStatusDto>(),
                    TotalCount = 0
                };
            }

            var paymentDto = new PaymentStatusDto
            {
                PaymentId = payment.PaymentId,
                OrderId = payment.OrderId,
                Status = payment.Status.ToString(),
                StatusDescription = StatusDescriptions.GetValueOrDefault(payment.Status, payment.Status.ToString()),
                Amount = payment.Amount,
                Currency = payment.Currency ?? "RUB",
                CreatedAt = payment.CreatedAt,
                UpdatedAt = payment.UpdatedAt,
                ExpiresAt = payment.ExpiresAt,
                Description = payment.Description,
                PayType = "O" // Default pay type
            };

            return new PaymentCheckResponseDto
            {
                Success = true,
                Payments = new List<PaymentStatusDto> { paymentDto },
                TotalCount = 1,
                OrderId = payment.OrderId
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to check payment by ID: {PaymentId}", paymentId);
            return new PaymentCheckResponseDto
            {
                Success = false,
                ErrorCode = "9999",
                Message = "Internal error",
                Payments = new List<PaymentStatusDto>(),
                TotalCount = 0
            };
        }
    }

    public async Task<PaymentCheckResponseDto> CheckPaymentsByOrderIdAsync(string orderId, PaymentCheckRequestDto request, CancellationToken cancellationToken = default)
    {
        try
        {
            // Note: GetByOrderIdAsync returns a single payment, but we wrap it in a collection
            // In a real implementation, you might have a method that returns multiple payments per order
            var payment = await _paymentRepository.GetByOrderIdAsync(request.TeamSlug, orderId, cancellationToken); // Use TeamSlug from request
            var payments = payment != null ? new List<Payment> { payment } : new List<Payment>();
            
            if (!payments.Any())
            {
                return new PaymentCheckResponseDto
                {
                    Success = false,
                    ErrorCode = "1404",
                    Message = "No payments found for order",
                    Payments = new List<PaymentStatusDto>(),
                    TotalCount = 0,
                    OrderId = orderId
                };
            }

            var paymentDtos = payments.Select(payment => new PaymentStatusDto
            {
                PaymentId = payment.PaymentId,
                OrderId = payment.OrderId,
                Status = payment.Status.ToString(),
                StatusDescription = StatusDescriptions.GetValueOrDefault(payment.Status, payment.Status.ToString()),
                Amount = payment.Amount,
                Currency = payment.Currency ?? "RUB",
                CreatedAt = payment.CreatedAt,
                UpdatedAt = payment.UpdatedAt,
                ExpiresAt = payment.ExpiresAt,
                Description = payment.Description,
                PayType = "O" // Default pay type
            }).ToList();

            return new PaymentCheckResponseDto
            {
                Success = true,
                Payments = paymentDtos,
                TotalCount = paymentDtos.Count,
                OrderId = orderId
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to check payments by order ID: {OrderId}", orderId);
            return new PaymentCheckResponseDto
            {
                Success = false,
                ErrorCode = "9999",
                Message = "Internal error",
                Payments = new List<PaymentStatusDto>(),
                TotalCount = 0,
                OrderId = orderId
            };
        }
    }
}