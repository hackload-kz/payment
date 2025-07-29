// SPDX-License-Identifier: MIT
// Copyright (c) 2025 HackLoad Payment Gateway

using PaymentGateway.Core.Entities;
using PaymentGateway.Core.Interfaces;
using PaymentGateway.Core.Repositories;
using PaymentGateway.Core.Enums;
using Microsoft.Extensions.Logging;
using Prometheus;

namespace PaymentGateway.Core.Services;

/// <summary>
/// Enhanced state transition validation service with comprehensive business rule validation
/// </summary>
public interface IPaymentStateTransitionValidationService
{
    Task<ValidationResult> ValidateTransitionAsync(Guid paymentId, PaymentStatus fromStatus, PaymentStatus toStatus, CancellationToken cancellationToken = default);
    Task<ValidationResult> ValidatePaymentBusinessRulesAsync(Payment payment, PaymentStatus targetStatus, CancellationToken cancellationToken = default);
    Task<ValidationResult> ValidateTeamLimitsAsync(int teamId, decimal amount, CancellationToken cancellationToken = default);
    Task<ValidationResult> ValidatePaymentExpirationAsync(Payment payment, CancellationToken cancellationToken = default);
    Task<ValidationResult> ValidateRefundRulesAsync(Payment payment, decimal refundAmount, CancellationToken cancellationToken = default);
    Task<ValidationResult> ValidateConcurrencyRulesAsync(Payment payment, PaymentStatus targetStatus, CancellationToken cancellationToken = default);
    ValidationResult ValidateStateTransitionMatrix(PaymentStatus fromStatus, PaymentStatus toStatus);
    Task<bool> CanProcessPaymentAsync(Guid paymentId, CancellationToken cancellationToken = default);
}

public class ValidationResult
{
    public bool IsValid { get; set; }
    public List<string> Errors { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
    public Dictionary<string, object> Metadata { get; set; } = new();

    public static ValidationResult Success() => new() { IsValid = true };
    public static ValidationResult Failure(params string[] errors) => new() { IsValid = false, Errors = errors.ToList() };
    public static ValidationResult WithWarnings(params string[] warnings) => new() { IsValid = true, Warnings = warnings.ToList() };
}

public class PaymentStateTransitionValidationService : IPaymentStateTransitionValidationService
{
    private readonly IPaymentRepository _paymentRepository;
    private readonly ITeamRepository _teamRepository;
    private readonly IPaymentStateMachine _paymentStateMachine;
    private readonly ILogger<PaymentStateTransitionValidationService> _logger;
    
    // Metrics
    private static readonly Counter ValidationOperations = Metrics
        .CreateCounter("payment_validation_operations_total", "Total payment validation operations", new[] { "type", "result" });
    
    private static readonly Histogram ValidationDuration = Metrics
        .CreateHistogram("payment_validation_duration_seconds", "Payment validation duration", new[] { "type" });

    // Business rule configurations
    private readonly TimeSpan _paymentExpirationTimeout = TimeSpan.FromMinutes(15);
    private readonly decimal _maxSinglePaymentAmount = 1000000; // 1M in minor units
    private readonly int _maxConcurrentPaymentsPerTeam = 100;

    // State transition matrix for validation
    private static readonly Dictionary<PaymentStatus, HashSet<PaymentStatus>> ValidTransitions = new()
    {
        [PaymentStatus.INIT] = new() { PaymentStatus.NEW },
        [PaymentStatus.NEW] = new() { PaymentStatus.PROCESSING, PaymentStatus.CANCELLED, PaymentStatus.EXPIRED },
        [PaymentStatus.PROCESSING] = new() { PaymentStatus.AUTHORIZED, PaymentStatus.CANCELLED, PaymentStatus.EXPIRED },
        [PaymentStatus.AUTHORIZED] = new() { PaymentStatus.CONFIRMED, PaymentStatus.CANCELLED, PaymentStatus.REFUNDED, PaymentStatus.EXPIRED },
        [PaymentStatus.CONFIRMED] = new() { PaymentStatus.REFUNDED },
        [PaymentStatus.CANCELLED] = new(), // Terminal state
        [PaymentStatus.REFUNDED] = new(), // Terminal state  
        [PaymentStatus.EXPIRED] = new() // Terminal state
    };

    public PaymentStateTransitionValidationService(
        IPaymentRepository paymentRepository,
        ITeamRepository teamRepository,
        IPaymentStateMachine paymentStateMachine,
        ILogger<PaymentStateTransitionValidationService> logger)
    {
        _paymentRepository = paymentRepository;
        _teamRepository = teamRepository;
        _paymentStateMachine = paymentStateMachine;
        _logger = logger;
    }

    public async Task<ValidationResult> ValidateTransitionAsync(Guid paymentId, PaymentStatus fromStatus, PaymentStatus toStatus, CancellationToken cancellationToken = default)
    {
        using var activity = ValidationDuration.WithLabels("transition").NewTimer();
        
        try
        {
            var result = new ValidationResult { IsValid = true };

            // 1. Validate state transition matrix
            var matrixValidation = ValidateStateTransitionMatrix(fromStatus, toStatus);
            if (!matrixValidation.IsValid)
            {
                ValidationOperations.WithLabels("transition", "invalid_matrix").Inc();
                return matrixValidation;
            }

            // 2. Get payment for business rule validation
            var payment = await _paymentRepository.GetByIdAsync(paymentId, cancellationToken);
            if (payment == null)
            {
                ValidationOperations.WithLabels("transition", "payment_not_found").Inc();
                return ValidationResult.Failure("Payment not found");
            }

            // 3. Validate business rules for target status
            var businessRuleValidation = await ValidatePaymentBusinessRulesAsync(payment, toStatus, cancellationToken);
            if (!businessRuleValidation.IsValid)
            {
                ValidationOperations.WithLabels("transition", "business_rules_failed").Inc();
                return businessRuleValidation;
            }

            // 4. Validate payment expiration
            var expirationValidation = await ValidatePaymentExpirationAsync(payment, cancellationToken);
            if (!expirationValidation.IsValid)
            {
                ValidationOperations.WithLabels("transition", "expired").Inc();
                return expirationValidation;
            }

            // 5. Validate concurrency rules
            var concurrencyValidation = await ValidateConcurrencyRulesAsync(payment, toStatus, cancellationToken);
            if (!concurrencyValidation.IsValid)
            {
                ValidationOperations.WithLabels("transition", "concurrency_failed").Inc();
                return concurrencyValidation;
            }

            // Combine warnings from all validations
            result.Warnings.AddRange(businessRuleValidation.Warnings);
            result.Warnings.AddRange(expirationValidation.Warnings);
            result.Warnings.AddRange(concurrencyValidation.Warnings);

            ValidationOperations.WithLabels("transition", "success").Inc();
            _logger.LogDebug("State transition validation successful: {PaymentId} {FromStatus} -> {ToStatus}", 
                paymentId, fromStatus, toStatus);
            
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "State transition validation failed: {PaymentId} {FromStatus} -> {ToStatus}", 
                paymentId, fromStatus, toStatus);
            ValidationOperations.WithLabels("transition", "error").Inc();
            return ValidationResult.Failure("Internal validation error");
        }
    }

    public async Task<ValidationResult> ValidatePaymentBusinessRulesAsync(Payment payment, PaymentStatus targetStatus, CancellationToken cancellationToken = default)
    {
        using var activity = ValidationDuration.WithLabels("business_rules").NewTimer();
        
        try
        {
            var result = new ValidationResult { IsValid = true };

            // 1. Validate payment amount limits
            if (payment.Amount <= 0)
            {
                result.Errors.Add("Payment amount must be greater than zero");
            }

            if (payment.Amount > _maxSinglePaymentAmount)
            {
                result.Errors.Add($"Payment amount exceeds maximum limit of {_maxSinglePaymentAmount}");
            }

            // 2. Validate team limits
            var teamLimitsValidation = await ValidateTeamLimitsAsync(payment.TeamId, payment.Amount, cancellationToken);
            if (!teamLimitsValidation.IsValid)
            {
                result.Errors.AddRange(teamLimitsValidation.Errors);
            }

            // 3. Validate payment metadata requirements
            if (targetStatus == PaymentStatus.PROCESSING && string.IsNullOrEmpty(payment.Description))
            {
                result.Warnings.Add("Payment description is recommended for processing");
            }

            // 4. Validate receipt requirements for confirmed payments
            if (targetStatus == PaymentStatus.CONFIRMED && string.IsNullOrEmpty(payment.Receipt))
            {
                result.Warnings.Add("Receipt data is recommended for confirmed payments");
            }

            // 5. Validate currency support
            var team = await _teamRepository.GetByIdAsync(payment.TeamId, cancellationToken);
            if (team?.SupportedCurrencies != null && !string.IsNullOrEmpty(payment.Currency))
            {
                if (!team.SupportedCurrencies.Contains(payment.Currency))
                {
                    result.Errors.Add($"Currency {payment.Currency} is not supported by team");
                }
            }

            result.IsValid = result.Errors.Count == 0;
            
            if (result.IsValid)
            {
                ValidationOperations.WithLabels("business_rules", "success").Inc();
            }
            else
            {
                ValidationOperations.WithLabels("business_rules", "failed").Inc();
                _logger.LogWarning("Business rule validation failed for payment {PaymentId}: {Errors}", 
                    payment.PaymentId, string.Join(", ", result.Errors));
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Business rule validation failed for payment: {PaymentId}", payment.PaymentId);
            ValidationOperations.WithLabels("business_rules", "error").Inc();
            return ValidationResult.Failure("Internal business rule validation error");
        }
    }

    public async Task<ValidationResult> ValidateTeamLimitsAsync(int teamId, decimal amount, CancellationToken cancellationToken = default)
    {
        using var activity = ValidationDuration.WithLabels("team_limits").NewTimer();
        
        try
        {
            var result = new ValidationResult { IsValid = true };

            var team = await _teamRepository.GetByIdAsync(teamId, cancellationToken);
            if (team == null)
            {
                ValidationOperations.WithLabels("team_limits", "team_not_found").Inc();
                return ValidationResult.Failure("Team not found");
            }

            // 1. Check if team is active
            if (!team.IsActive)
            {
                result.Errors.Add("Team is not active");
            }

            // 2. Check daily payment limits
            if (team.DailyPaymentLimit.HasValue)
            {
                var todayPayments = await _paymentRepository.GetTodayPaymentsTotalAsync(teamId, cancellationToken);
                if (todayPayments + amount > team.DailyPaymentLimit.Value)
                {
                    result.Errors.Add($"Daily payment limit exceeded. Limit: {team.DailyPaymentLimit}, Current: {todayPayments}, Requested: {amount}");
                }
            }

            // 3. Check transaction count limits  
            if (team.DailyTransactionLimit.HasValue)
            {
                var todayTransactionCount = await _paymentRepository.GetTodayTransactionCountAsync(teamId, cancellationToken);
                if (todayTransactionCount >= team.DailyTransactionLimit.Value)
                {
                    result.Errors.Add($"Daily transaction count limit exceeded. Limit: {team.DailyTransactionLimit}, Current: {todayTransactionCount}");
                }
            }

            // 4. Check concurrent payment limits
            var activePaymentCount = await _paymentRepository.GetActivePaymentCountAsync(teamId, cancellationToken);
            if (activePaymentCount >= _maxConcurrentPaymentsPerTeam)
            {
                result.Errors.Add($"Concurrent payment limit exceeded. Limit: {_maxConcurrentPaymentsPerTeam}, Current: {activePaymentCount}");
            }

            result.IsValid = result.Errors.Count == 0;
            
            if (result.IsValid)
            {
                ValidationOperations.WithLabels("team_limits", "success").Inc();
            }
            else
            {
                ValidationOperations.WithLabels("team_limits", "failed").Inc();
                _logger.LogWarning("Team limits validation failed for team {TeamId}: {Errors}", 
                    teamId, string.Join(", ", result.Errors));
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Team limits validation failed for team: {TeamId}", teamId);
            ValidationOperations.WithLabels("team_limits", "error").Inc();
            return ValidationResult.Failure("Internal team limits validation error");
        }
    }

    public async Task<ValidationResult> ValidatePaymentExpirationAsync(Payment payment, CancellationToken cancellationToken = default)
    {
        using var activity = ValidationDuration.WithLabels("expiration").NewTimer();
        
        try
        {
            var result = new ValidationResult { IsValid = true };

            // Check if payment has expired based on creation time
            var paymentAge = DateTime.UtcNow - payment.CreatedAt;
            if (paymentAge > _paymentExpirationTimeout)
            {
                // Only fail validation if payment is not in a final state
                if (payment.Status != PaymentStatus.CONFIRMED && 
                    payment.Status != PaymentStatus.CANCELLED && 
                    payment.Status != PaymentStatus.REFUNDED &&
                    payment.Status != PaymentStatus.EXPIRED)
                {
                    result.Errors.Add($"Payment has expired. Age: {paymentAge.TotalMinutes:F1} minutes, Limit: {_paymentExpirationTimeout.TotalMinutes} minutes");
                }
            }
            else if (paymentAge > TimeSpan.FromMinutes(_paymentExpirationTimeout.TotalMinutes * 0.8))
            {
                // Warning when payment is approaching expiration
                var remainingTime = _paymentExpirationTimeout - paymentAge;
                result.Warnings.Add($"Payment expires in {remainingTime.TotalMinutes:F1} minutes");
            }

            result.IsValid = result.Errors.Count == 0;
            
            if (result.IsValid)
            {
                ValidationOperations.WithLabels("expiration", "success").Inc();
            }
            else
            {
                ValidationOperations.WithLabels("expiration", "failed").Inc();
                _logger.LogWarning("Payment expiration validation failed for payment {PaymentId}: {Errors}", 
                    payment.PaymentId, string.Join(", ", result.Errors));
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Payment expiration validation failed for payment: {PaymentId}", payment.PaymentId);
            ValidationOperations.WithLabels("expiration", "error").Inc();
            return ValidationResult.Failure("Internal expiration validation error");
        }
    }

    public async Task<ValidationResult> ValidateRefundRulesAsync(Payment payment, decimal refundAmount, CancellationToken cancellationToken = default)
    {
        using var activity = ValidationDuration.WithLabels("refund_rules").NewTimer();
        
        try
        {
            var result = new ValidationResult { IsValid = true };

            // 1. Validate payment is in refundable state
            if (payment.Status != PaymentStatus.CONFIRMED)
            {
                result.Errors.Add($"Payment cannot be refunded in {payment.Status} state");
            }

            // 2. Validate refund amount
            if (refundAmount <= 0)
            {
                result.Errors.Add("Refund amount must be greater than zero");
            }

            if (refundAmount > payment.Amount)
            {
                result.Errors.Add($"Refund amount ({refundAmount}) cannot exceed payment amount ({payment.Amount})");
            }

            // 3. Check refund time limits (e.g., no refunds after 30 days)
            var paymentAge = DateTime.UtcNow - payment.CreatedAt;
            var refundTimeLimit = TimeSpan.FromDays(30);
            if (paymentAge > refundTimeLimit)
            {
                result.Errors.Add($"Refund time limit exceeded. Payment age: {paymentAge.TotalDays:F1} days, Limit: {refundTimeLimit.TotalDays} days");
            }

            // 4. Validate team refund permissions
            var team = await _teamRepository.GetByIdAsync(payment.TeamId, cancellationToken);
            if (team != null && !team.CanProcessRefunds)
            {
                result.Errors.Add("Team does not have refund processing permissions");
            }

            result.IsValid = result.Errors.Count == 0;
            
            if (result.IsValid)
            {
                ValidationOperations.WithLabels("refund_rules", "success").Inc();
            }
            else
            {
                ValidationOperations.WithLabels("refund_rules", "failed").Inc();
                _logger.LogWarning("Refund rules validation failed for payment {PaymentId}: {Errors}", 
                    payment.PaymentId, string.Join(", ", result.Errors));
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Refund rules validation failed for payment: {PaymentId}", payment.PaymentId);
            ValidationOperations.WithLabels("refund_rules", "error").Inc();
            return ValidationResult.Failure("Internal refund rules validation error");
        }
    }

    public async Task<ValidationResult> ValidateConcurrencyRulesAsync(Payment payment, PaymentStatus targetStatus, CancellationToken cancellationToken = default)
    {
        using var activity = ValidationDuration.WithLabels("concurrency_rules").NewTimer();
        
        try
        {
            var result = new ValidationResult { IsValid = true };

            // 1. Check for concurrent modifications by comparing UpdatedAt
            var currentPayment = await _paymentRepository.GetByIdAsync(payment.PaymentId, cancellationToken);
            if (currentPayment != null && currentPayment.UpdatedAt != payment.UpdatedAt)
            {
                result.Errors.Add("Payment has been modified by another process. Please reload and try again.");
            }

            // 2. Validate concurrent payment limits per team
            if (targetStatus == PaymentStatus.PROCESSING)
            {
                var processingCount = await _paymentRepository.GetProcessingPaymentCountAsync(payment.TeamId, cancellationToken);
                var maxConcurrentProcessing = 10; // Configurable limit
                
                if (processingCount >= maxConcurrentProcessing)
                {
                    result.Errors.Add($"Too many payments being processed concurrently. Limit: {maxConcurrentProcessing}, Current: {processingCount}");
                }
            }

            // 3. Check for duplicate OrderId in concurrent scenarios
            if (targetStatus == PaymentStatus.PROCESSING)
            {
                var duplicatePayment = await _paymentRepository.GetByOrderIdAsync(payment.OrderId, payment.TeamId, cancellationToken);
                if (duplicatePayment != null && duplicatePayment.PaymentId != payment.PaymentId)
                {
                    result.Errors.Add($"Duplicate OrderId detected: {payment.OrderId}");
                }
            }

            result.IsValid = result.Errors.Count == 0;
            
            if (result.IsValid)
            {
                ValidationOperations.WithLabels("concurrency_rules", "success").Inc();
            }
            else
            {
                ValidationOperations.WithLabels("concurrency_rules", "failed").Inc();
                _logger.LogWarning("Concurrency rules validation failed for payment {PaymentId}: {Errors}", 
                    payment.PaymentId, string.Join(", ", result.Errors));
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Concurrency rules validation failed for payment: {PaymentId}", payment.PaymentId);
            ValidationOperations.WithLabels("concurrency_rules", "error").Inc();
            return ValidationResult.Failure("Internal concurrency rules validation error");
        }
    }

    public ValidationResult ValidateStateTransitionMatrix(PaymentStatus fromStatus, PaymentStatus toStatus)
    {
        try
        {
            if (!ValidTransitions.ContainsKey(fromStatus))
            {
                ValidationOperations.WithLabels("matrix", "invalid_from_state").Inc();
                return ValidationResult.Failure($"Invalid source state: {fromStatus}");
            }

            if (!ValidTransitions[fromStatus].Contains(toStatus))
            {
                ValidationOperations.WithLabels("matrix", "invalid_transition").Inc();
                return ValidationResult.Failure($"Invalid state transition: {fromStatus} -> {toStatus}");
            }

            ValidationOperations.WithLabels("matrix", "success").Inc();
            return ValidationResult.Success();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "State transition matrix validation failed: {FromStatus} -> {ToStatus}", fromStatus, toStatus);
            ValidationOperations.WithLabels("matrix", "error").Inc();
            return ValidationResult.Failure("Internal matrix validation error");
        }
    }

    public async Task<bool> CanProcessPaymentAsync(Guid paymentId, CancellationToken cancellationToken = default)
    {
        try
        {
            var payment = await _paymentRepository.GetByIdAsync(paymentId, cancellationToken);
            if (payment == null) return false;

            // Quick checks for payment processability
            var validationResult = await ValidatePaymentExpirationAsync(payment, cancellationToken);
            if (!validationResult.IsValid) return false;

            var businessRulesResult = await ValidatePaymentBusinessRulesAsync(payment, PaymentStatus.PROCESSING, cancellationToken);
            if (!businessRulesResult.IsValid) return false;

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to check if payment can be processed: {PaymentId}", paymentId);
            return false;
        }
    }
}