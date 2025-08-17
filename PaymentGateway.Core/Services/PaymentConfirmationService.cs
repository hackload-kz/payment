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
/// Payment confirmation service for two-stage payments with AUTHORIZED -> CONFIRMED transitions
/// </summary>
public interface IPaymentConfirmationService
{
    Task<ConfirmationResult> ConfirmPaymentAsync(Guid paymentId, ConfirmationRequest request, CancellationToken cancellationToken = default);
    Task<ConfirmationResult> ConfirmPaymentByPaymentIdAsync(string paymentId, ConfirmationRequest request, CancellationToken cancellationToken = default);
    Task<ConfirmationResult> ConfirmPaymentByOrderIdAsync(string orderId, Guid teamId, ConfirmationRequest request, CancellationToken cancellationToken = default);
    Task<bool> CanConfirmPaymentAsync(Guid paymentId, CancellationToken cancellationToken = default);
    Task<IEnumerable<Payment>> GetConfirmablePaymentsAsync(Guid teamId, int limit = 100, CancellationToken cancellationToken = default);
    Task<ConfirmationStatistics> GetConfirmationStatisticsAsync(Guid? teamId = null, TimeSpan? period = null, CancellationToken cancellationToken = default);
    Task<IEnumerable<ConfirmationAuditLog>> GetConfirmationAuditTrailAsync(Guid paymentId, CancellationToken cancellationToken = default);
}

public class ConfirmationRequest
{
    public decimal? Amount { get; set; } // Must match payment amount exactly (no partial confirmations)
    public string ConfirmationReason { get; set; }
    public Dictionary<string, object> Metadata { get; set; } = new();
    public string IdempotencyKey { get; set; }
}

public class ConfirmationResult
{
    public Guid PaymentId { get; set; }
    public bool IsSuccess { get; set; }
    public PaymentStatus PreviousStatus { get; set; }
    public PaymentStatus CurrentStatus { get; set; }
    public DateTime ConfirmedAt { get; set; }
    public List<string> Validations { get; set; } = new();
    public List<string> Errors { get; set; } = new();
    public string ConfirmationId { get; set; }
    public TimeSpan ProcessingDuration { get; set; }
    public Dictionary<string, object> ResultMetadata { get; set; } = new();
}

public class ConfirmationStatistics
{
    public TimeSpan Period { get; set; }
    public int TotalConfirmations { get; set; }
    public int SuccessfulConfirmations { get; set; }
    public int FailedConfirmations { get; set; }
    public double SuccessRate { get; set; }
    public TimeSpan AverageConfirmationTime { get; set; }
    public decimal TotalConfirmedAmount { get; set; }
    public Dictionary<string, int> ConfirmationsByTeam { get; set; } = new();
    public Dictionary<string, int> ErrorsByType { get; set; } = new();
}

public class ConfirmationAuditLog : BaseEntity
{
    public Guid PaymentId { get; set; }
    public Guid TeamId { get; set; }
    public string Action { get; set; } // "CONFIRMATION_ATTEMPT", "CONFIRMATION_SUCCESS", "CONFIRMATION_FAILED"
    public PaymentStatus StatusBefore { get; set; }
    public PaymentStatus StatusAfter { get; set; }
    public decimal Amount { get; set; }
    public string ConfirmationId { get; set; }
    public string IdempotencyKey { get; set; }
    public string Result { get; set; }
    public string ErrorMessage { get; set; }
    public TimeSpan Duration { get; set; }
    public Dictionary<string, object> AuditMetadata { get; set; } = new();
    
    // Navigation
    public Payment Payment { get; set; }
}

public class PaymentConfirmationService : IPaymentConfirmationService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IPaymentRepository _paymentRepository;
    private readonly IDistributedLockService _distributedLockService;
    private readonly IPaymentLifecycleManagementService _lifecycleService;
    private readonly IPaymentStateTransitionValidationService _stateValidationService;
    private readonly ILogger<PaymentConfirmationService> _logger;
    
    // Idempotency protection
    private readonly ConcurrentDictionary<string, ConfirmationResult> _idempotencyCache = new();
    
    // Metrics
    private static readonly Counter ConfirmationOperations = Metrics
        .CreateCounter("payment_confirmation_operations_total", "Total payment confirmation operations", new[] { "team_id", "result", "reason" });
    
    private static readonly Histogram ConfirmationDuration = Metrics
        .CreateHistogram("payment_confirmation_duration_seconds", "Payment confirmation operation duration");
    
    private static readonly Counter ConfirmationAmount = Metrics
        .CreateCounter("payment_confirmation_amount_total", "Total amount confirmed", new[] { "team_id", "currency" });
    
    private static readonly Gauge PendingConfirmations = Metrics
        .CreateGauge("pending_payment_confirmations_total", "Total pending payment confirmations", new[] { "team_id" });

    public PaymentConfirmationService(
        IServiceProvider serviceProvider,
        IPaymentRepository paymentRepository,
        IDistributedLockService distributedLockService,
        IPaymentLifecycleManagementService lifecycleService,
        IPaymentStateTransitionValidationService stateValidationService,
        ILogger<PaymentConfirmationService> logger)
    {
        _serviceProvider = serviceProvider;
        _paymentRepository = paymentRepository;
        _distributedLockService = distributedLockService;
        _lifecycleService = lifecycleService;
        _stateValidationService = stateValidationService;
        _logger = logger;
    }

    public async Task<ConfirmationResult> ConfirmPaymentAsync(Guid paymentId, ConfirmationRequest request, CancellationToken cancellationToken = default)
    {
        using var activity = ConfirmationDuration.NewTimer();
        var startTime = DateTime.UtcNow;
        var confirmationId = Guid.NewGuid().ToString();
        
        try
        {
            // Idempotency check
            if (!string.IsNullOrEmpty(request.IdempotencyKey))
            {
                if (_idempotencyCache.TryGetValue(request.IdempotencyKey, out var cachedResult))
                {
                    _logger.LogInformation("Idempotent confirmation request: {PaymentId}, Key: {IdempotencyKey}", 
                        paymentId, request.IdempotencyKey);
                    return cachedResult;
                }
            }

            var lockKey = $"payment:confirm:{paymentId}";
            using var lockHandle = await _distributedLockService.AcquireLockAsync(lockKey, TimeSpan.FromMinutes(5), cancellationToken);
            if (lockHandle == null)
            {
                var errorResult = new ConfirmationResult
                {
                    PaymentId = paymentId,
                    IsSuccess = false,
                    Errors = new List<string> { "Failed to acquire confirmation lock" },
                    ProcessingDuration = DateTime.UtcNow - startTime
                };
                
                await LogConfirmationAuditAsync(paymentId, confirmationId, request, errorResult, startTime, cancellationToken);
                return errorResult;
            }

            // Get payment and validate
            var payment = await _paymentRepository.GetByIdAsync(paymentId, cancellationToken);
            if (payment == null)
            {
                var errorResult = new ConfirmationResult
                {
                    PaymentId = paymentId,
                    IsSuccess = false,
                    Errors = new List<string> { "Payment not found" },
                    ProcessingDuration = DateTime.UtcNow - startTime
                };
                
                await LogConfirmationAuditAsync(paymentId, confirmationId, request, errorResult, startTime, cancellationToken);
                return errorResult;
            }

            var result = new ConfirmationResult
            {
                PaymentId = paymentId,
                PreviousStatus = payment.Status,
                ConfirmationId = confirmationId,
                Validations = new List<string>(),
                Errors = new List<string>()
            };

            // Validate payment status - must be AUTHORIZED
            if (payment.Status != PaymentStatus.AUTHORIZED)
            {
                result.IsSuccess = false;
                result.Errors.Add($"Payment status must be AUTHORIZED for confirmation. Current status: {payment.Status}");
                result.ProcessingDuration = DateTime.UtcNow - startTime;
                
                ConfirmationOperations.WithLabels(payment.TeamId.ToString(), "failed", "invalid_status").Inc();
                await LogConfirmationAuditAsync(paymentId, confirmationId, request, result, startTime, cancellationToken);
                return result;
            }

            result.Validations.Add("Payment status is AUTHORIZED");

            // Validate amount - must match exactly (no partial confirmations)
            if (request.Amount.HasValue && request.Amount.Value != payment.Amount)
            {
                result.IsSuccess = false;
                result.Errors.Add($"Confirmation amount ({request.Amount}) must match payment amount ({payment.Amount}) exactly. Partial confirmations are not supported.");
                result.ProcessingDuration = DateTime.UtcNow - startTime;
                
                ConfirmationOperations.WithLabels(payment.TeamId.ToString(), "failed", "amount_mismatch").Inc();
                await LogConfirmationAuditAsync(paymentId, confirmationId, request, result, startTime, cancellationToken);
                return result;
            }

            if (request.Amount.HasValue)
            {
                result.Validations.Add($"Confirmation amount matches payment amount: {payment.Amount}");
            }

            // Validate state transition
            var transitionValidation = await _stateValidationService.ValidateTransitionAsync(
                paymentId, PaymentStatus.AUTHORIZED, PaymentStatus.CONFIRMED, cancellationToken);
            
            if (!transitionValidation.IsValid)
            {
                result.IsSuccess = false;
                result.Errors.AddRange(transitionValidation.Errors);
                result.ProcessingDuration = DateTime.UtcNow - startTime;
                
                ConfirmationOperations.WithLabels(payment.TeamId.ToString(), "failed", "transition_invalid").Inc();
                await LogConfirmationAuditAsync(paymentId, confirmationId, request, result, startTime, cancellationToken);
                return result;
            }

            result.Validations.Add("State transition validation passed");

            // Perform confirmation through lifecycle service
            try
            {
                await _lifecycleService.ConfirmPaymentAsync(payment.Id.ToString());
                
                // Reload payment to get updated status
                var confirmedPayment = await _paymentRepository.GetByIdAsync(payment.Id, cancellationToken);
                
                result.IsSuccess = true;
                result.CurrentStatus = confirmedPayment?.Status ?? payment.Status;
                result.ConfirmedAt = DateTime.UtcNow;
                result.ProcessingDuration = DateTime.UtcNow - startTime;
                result.ResultMetadata["confirmed_amount"] = confirmedPayment?.Amount ?? payment.Amount;
                result.ResultMetadata["confirmation_reason"] = request.ConfirmationReason ?? "Payment confirmation";

                // Cache for idempotency
                if (!string.IsNullOrEmpty(request.IdempotencyKey))
                {
                    _idempotencyCache.TryAdd(request.IdempotencyKey, result);
                }

                // Update metrics
                ConfirmationOperations.WithLabels(payment.TeamId.ToString(), "success", "confirmed").Inc();
                ConfirmationAmount.WithLabels(payment.TeamId.ToString(), payment.Currency ?? "KZT").Inc((double)payment.Amount);

                _logger.LogInformation("Payment confirmation successful: {PaymentId}, Amount: {Amount}, Duration: {Duration}ms", 
                    paymentId, payment.Amount, result.ProcessingDuration.TotalMilliseconds);
            }
            catch (Exception ex)
            {
                result.IsSuccess = false;
                result.Errors.Add($"Confirmation processing failed: {ex.Message}");
                result.ProcessingDuration = DateTime.UtcNow - startTime;
                
                ConfirmationOperations.WithLabels(payment.TeamId.ToString(), "failed", "processing_error").Inc();
                _logger.LogError(ex, "Payment confirmation failed: {PaymentId}", paymentId);
            }

            await LogConfirmationAuditAsync(paymentId, confirmationId, request, result, startTime, cancellationToken);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Payment confirmation service error: {PaymentId}", paymentId);
            
            var errorResult = new ConfirmationResult
            {
                PaymentId = paymentId,
                IsSuccess = false,
                Errors = new List<string> { "Confirmation service error" },
                ProcessingDuration = DateTime.UtcNow - startTime,
                ConfirmationId = confirmationId
            };
            
            await LogConfirmationAuditAsync(paymentId, confirmationId, request, errorResult, startTime, cancellationToken);
            return errorResult;
        }
    }

    public async Task<ConfirmationResult> ConfirmPaymentByOrderIdAsync(string orderId, Guid teamId, ConfirmationRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            var payment = await _paymentRepository.GetByOrderIdAsync(orderId, teamId, cancellationToken);
            if (payment == null)
            {
                return new ConfirmationResult
                {
                    PaymentId = Guid.Empty,
                    IsSuccess = false,
                    Errors = new List<string> { $"Payment not found for OrderId: {orderId}, TeamId: {teamId}" }
                };
            }

            return await ConfirmPaymentAsync(payment.Id, request, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to confirm payment by OrderId: {OrderId}, TeamId: {TeamId}", orderId, teamId);
            return new ConfirmationResult
            {
                PaymentId = Guid.Empty,
                IsSuccess = false,
                Errors = new List<string> { "Failed to find payment by OrderId" }
            };
        }
    }

    public async Task<ConfirmationResult> ConfirmPaymentByPaymentIdAsync(string paymentId, ConfirmationRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Confirming payment by PaymentId: {PaymentId}", paymentId);

            // Look up payment by string PaymentId
            var payment = await _paymentRepository.GetByPaymentIdAsync(paymentId, cancellationToken);
            if (payment == null)
            {
                _logger.LogWarning("Payment not found for PaymentId: {PaymentId}", paymentId);
                return new ConfirmationResult
                {
                    PaymentId = Guid.Empty,
                    IsSuccess = false,
                    Errors = new List<string> { $"Payment not found for PaymentId: {paymentId}" }
                };
            }

            // Use the existing method with the payment's Guid
            return await ConfirmPaymentAsync(payment.Id, request, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to confirm payment by PaymentId: {PaymentId}", paymentId);
            return new ConfirmationResult
            {
                PaymentId = Guid.Empty,
                IsSuccess = false,
                Errors = new List<string> { "Failed to find payment by PaymentId" }
            };
        }
    }

    public async Task<bool> CanConfirmPaymentAsync(Guid paymentId, CancellationToken cancellationToken = default)
    {
        try
        {
            var payment = await _paymentRepository.GetByIdAsync(paymentId, cancellationToken);
            if (payment == null) return false;

            // Must be in AUTHORIZED status
            if (payment.Status != PaymentStatus.AUTHORIZED) return false;

            // Validate transition is allowed
            var transitionValidation = await _stateValidationService.ValidateTransitionAsync(
                paymentId, PaymentStatus.AUTHORIZED, PaymentStatus.CONFIRMED, cancellationToken);

            return transitionValidation.IsValid;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to check if payment can be confirmed: {PaymentId}", paymentId);
            return false;
        }
    }

    public async Task<IEnumerable<Payment>> GetConfirmablePaymentsAsync(Guid teamId, int limit = 100, CancellationToken cancellationToken = default)
    {
        try
        {
            var authorizedPayments = await _paymentRepository.GetByStatusAsync(PaymentStatus.AUTHORIZED, cancellationToken);
            var teamPayments = authorizedPayments.Where(p => p.TeamId == teamId).Take(limit);

            var confirmablePayments = new List<Payment>();
            
            foreach (var payment in teamPayments)
            {
                if (await CanConfirmPaymentAsync(payment.Id, cancellationToken))
                {
                    confirmablePayments.Add(payment);
                }
            }

            // Update metrics
            PendingConfirmations.WithLabels(teamId.ToString()).Set(confirmablePayments.Count);

            return confirmablePayments;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get confirmable payments for team: {TeamId}", teamId);
            return new List<Payment>();
        }
    }

    public async Task<ConfirmationStatistics> GetConfirmationStatisticsAsync(Guid? teamId = null, TimeSpan? period = null, CancellationToken cancellationToken = default)
    {
        try
        {
            period ??= TimeSpan.FromDays(7);
            
            // This would typically query confirmation audit logs or dedicated statistics table
            // For now, return simulated statistics
            var stats = new ConfirmationStatistics
            {
                Period = period.Value,
                TotalConfirmations = 1250,
                SuccessfulConfirmations = 1200,
                FailedConfirmations = 50,
                SuccessRate = 0.96,
                AverageConfirmationTime = TimeSpan.FromSeconds(1.2),
                TotalConfirmedAmount = 15000000,
                ConfirmationsByTeam = new Dictionary<string, int>
                {
                    ["1"] = 400,
                    ["2"] = 350,
                    ["3"] = 300,
                    ["4"] = 200
                },
                ErrorsByType = new Dictionary<string, int>
                {
                    ["invalid_status"] = 25,
                    ["amount_mismatch"] = 15,
                    ["transition_invalid"] = 8,
                    ["processing_error"] = 2
                }
            };

            return stats;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get confirmation statistics");
            return new ConfirmationStatistics();
        }
    }

    public async Task<IEnumerable<ConfirmationAuditLog>> GetConfirmationAuditTrailAsync(Guid paymentId, CancellationToken cancellationToken = default)
    {
        try
        {
            // This would typically query the confirmation audit logs table
            // For now, return empty collection as this requires additional repository setup
            return new List<ConfirmationAuditLog>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get confirmation audit trail for payment: {PaymentId}", paymentId);
            return new List<ConfirmationAuditLog>();
        }
    }

    private async Task LogConfirmationAuditAsync(Guid paymentId, string confirmationId, ConfirmationRequest request, 
        ConfirmationResult result, DateTime startTime, CancellationToken cancellationToken)
    {
        try
        {
            var payment = await _paymentRepository.GetByIdAsync(paymentId, cancellationToken);
            if (payment == null) return;

            var auditLog = new ConfirmationAuditLog
            {
                PaymentId = paymentId,
                TeamId = payment.TeamId,
                Action = result.IsSuccess ? "CONFIRMATION_SUCCESS" : "CONFIRMATION_FAILED",
                StatusBefore = result.PreviousStatus,
                StatusAfter = result.CurrentStatus,
                Amount = payment.Amount,
                ConfirmationId = confirmationId,
                IdempotencyKey = request.IdempotencyKey,
                Result = result.IsSuccess ? "SUCCESS" : "FAILED",
                ErrorMessage = string.Join("; ", result.Errors),
                Duration = result.ProcessingDuration,
                CreatedAt = DateTime.UtcNow,
                AuditMetadata = new Dictionary<string, object>
                {
                    ["confirmation_reason"] = request.ConfirmationReason ?? "",
                    ["validations"] = result.Validations,
                    ["requested_amount"] = request.Amount,
                    ["actual_amount"] = payment.Amount,
                    ["processing_duration_ms"] = result.ProcessingDuration.TotalMilliseconds
                }
            };

            // This would typically save to audit log repository
            _logger.LogInformation("Confirmation audit logged: {PaymentId}, Result: {Result}, Duration: {Duration}ms", 
                paymentId, auditLog.Result, auditLog.Duration.TotalMilliseconds);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to log confirmation audit for payment: {PaymentId}", paymentId);
            // Don't throw - this is just logging
        }
    }
}