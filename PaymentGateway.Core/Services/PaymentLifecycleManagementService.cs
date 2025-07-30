// SPDX-License-Identifier: MIT
// Copyright (c) 2025 HackLoad Payment Gateway

using PaymentGateway.Core.Entities;
using PaymentGateway.Core.Interfaces;
using PaymentGateway.Core.Repositories;
using PaymentGateway.Core.Enums;
using Microsoft.Extensions.Logging;
using Prometheus;
using System.Collections.Concurrent;

namespace PaymentGateway.Core.Services;

/// <summary>
/// Comprehensive payment lifecycle management service that orchestrates
/// the entire payment processing flow with state management and concurrency control
/// </summary>
/// <summary>
/// Result of payment lifecycle operation with audit trail information
/// </summary>
public class PaymentLifecycleResult
{
    public bool IsSuccess { get; set; }
    public Payment? Payment { get; set; }
    public string TransitionId { get; set; } = string.Empty;
    public DateTime? TransitionedAt { get; set; }
    public PaymentStatus FromStatus { get; set; }
    public PaymentStatus ToStatus { get; set; }
    public List<string> Errors { get; set; } = new();
    public Dictionary<string, object> Context { get; set; } = new();
}

public interface IPaymentLifecycleManagementService
{
    Task<Payment> InitializePaymentAsync(Payment payment, CancellationToken cancellationToken = default);
    Task<Payment> ProcessPaymentAsync(Guid paymentId, CancellationToken cancellationToken = default);
    Task<Payment> AuthorizePaymentAsync(Guid paymentId, CancellationToken cancellationToken = default);
    Task<PaymentLifecycleResult> AuthorizePaymentWithTransitionAsync(Guid paymentId, CancellationToken cancellationToken = default);
    Task<Payment> ConfirmPaymentAsync(Guid paymentId, CancellationToken cancellationToken = default);
    Task<Payment> CancelPaymentAsync(Guid paymentId, string reason, CancellationToken cancellationToken = default);
    Task<Payment> RefundPaymentAsync(Guid paymentId, decimal amount, string reason, CancellationToken cancellationToken = default);
    Task<Payment> ExpirePaymentAsync(Guid paymentId, CancellationToken cancellationToken = default);
    Task<Payment> FailPaymentAsync(Guid paymentId, string errorCode, string errorMessage, CancellationToken cancellationToken = default);
    Task<Payment> GetPaymentStatusAsync(Guid paymentId, CancellationToken cancellationToken = default);
    Task<IEnumerable<Payment>> GetActivePaymentsAsync(Guid teamId, CancellationToken cancellationToken = default);
    Task<bool> IsPaymentExpiredAsync(Guid paymentId, CancellationToken cancellationToken = default);
    Task ProcessExpiredPaymentsAsync(CancellationToken cancellationToken = default);
}

public class PaymentLifecycleManagementService : IPaymentLifecycleManagementService
{
    private readonly IPaymentRepository _paymentRepository;
    private readonly IPaymentStateMachine _paymentStateMachine;
    private readonly IDistributedLockService _distributedLockService;
    private readonly IPaymentStateTransitionEventService _eventService;
    private readonly ILogger<PaymentLifecycleManagementService> _logger;
    private readonly IUnitOfWork _unitOfWork;
    
    // Metrics
    private static readonly Counter PaymentLifecycleOperations = Metrics
        .CreateCounter("payment_lifecycle_operations_total", "Total payment lifecycle operations", new[] { "operation", "status" });
    
    private static readonly Histogram PaymentLifecycleOperationDuration = Metrics
        .CreateHistogram("payment_lifecycle_operation_duration_seconds", "Payment lifecycle operation duration", new[] { "operation" });
    
    private static readonly Gauge ActivePaymentsGauge = Metrics
        .CreateGauge("active_payments_total", "Total number of active payments");
    
    private static readonly Counter PaymentStateTransitions = Metrics
        .CreateCounter("payment_state_transitions_total", "Total payment state transitions", new[] { "from_state", "to_state", "result" });

    // In-memory cache for active payment sessions
    private readonly ConcurrentDictionary<Guid, DateTime> _activePaymentSessions = new();
    
    // Lock timeout configurations
    private readonly TimeSpan _lockTimeout = TimeSpan.FromSeconds(30);
    private readonly TimeSpan _paymentProcessingTimeout = TimeSpan.FromMinutes(15);

    public PaymentLifecycleManagementService(
        IPaymentRepository paymentRepository,
        IPaymentStateMachine paymentStateMachine,
        IDistributedLockService distributedLockService,
        IPaymentStateTransitionEventService eventService,
        ILogger<PaymentLifecycleManagementService> logger,
        IUnitOfWork unitOfWork)
    {
        _paymentRepository = paymentRepository;
        _paymentStateMachine = paymentStateMachine;
        _distributedLockService = distributedLockService;
        _eventService = eventService;
        _logger = logger;
        _unitOfWork = unitOfWork;
    }

    public async Task<Payment> InitializePaymentAsync(Payment payment, CancellationToken cancellationToken = default)
    {
        using var activity = PaymentLifecycleOperationDuration.WithLabels("initialize").NewTimer();
        var lockKey = $"payment:init:{payment.OrderId}:{payment.TeamId}";

        try
        {
            await using var lockHandle = await _distributedLockService.AcquireLockAsync(lockKey, _lockTimeout, cancellationToken);
            if (lockHandle == null)
            {
                _logger.LogWarning("Failed to acquire lock for payment initialization: {OrderId}", payment.OrderId);
                PaymentLifecycleOperations.WithLabels("initialize", "lock_failed").Inc();
                throw new InvalidOperationException("Payment initialization is already in progress");
            }

            // Validate initial state transition from INIT to NEW
            if (!await _paymentStateMachine.CanTransitionAsync(payment, PaymentStatus.NEW, cancellationToken))
            {
                _logger.LogError("Invalid initial state transition for payment: {OrderId}", payment.OrderId);
                PaymentLifecycleOperations.WithLabels("initialize", "invalid_state").Inc();
                throw new InvalidOperationException("Invalid payment state for initialization");
            }

            // Check for duplicate OrderId
            var existingPayment = await _paymentRepository.GetByOrderIdAsync(payment.OrderId, payment.TeamId, cancellationToken);
            if (existingPayment != null)
            {
                _logger.LogWarning("Duplicate payment detected for OrderId: {OrderId}", payment.OrderId);
                PaymentLifecycleOperations.WithLabels("initialize", "duplicate").Inc();
                throw new InvalidOperationException("Payment with this OrderId already exists");
            }

            // Initialize payment
            payment.Status = PaymentStatus.NEW;
            payment.CreatedAt = DateTime.UtcNow;
            payment.UpdatedAt = DateTime.UtcNow;
            
            // Generate PaymentURL
            payment.PaymentURL = GeneratePaymentUrl(payment);

            await _paymentRepository.AddAsync(payment, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            // Add to active sessions
            _activePaymentSessions.TryAdd(payment.Id, DateTime.UtcNow);
            ActivePaymentsGauge.Inc();

            // Publish state transition event
            await _eventService.PublishStateTransitionAsync(payment.Id, PaymentStatus.INIT, PaymentStatus.NEW, cancellationToken);

            _logger.LogInformation("Payment initialized successfully: {PaymentId}, OrderId: {OrderId}", 
                payment.PaymentId, payment.OrderId);
            
            PaymentLifecycleOperations.WithLabels("initialize", "success").Inc();
            PaymentStateTransitions.WithLabels("INIT", "NEW", "success").Inc();
            
            return payment;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize payment: {OrderId}", payment.OrderId);
            PaymentLifecycleOperations.WithLabels("initialize", "error").Inc();
            throw;
        }
    }

    public async Task<Payment> ProcessPaymentAsync(Guid paymentId, CancellationToken cancellationToken = default)
    {
        using var activity = PaymentLifecycleOperationDuration.WithLabels("process").NewTimer();
        var lockKey = $"payment:process:{paymentId}";

        try
        {
            await using var lockHandle = await _distributedLockService.AcquireLockAsync(lockKey, _lockTimeout, cancellationToken);
            if (lockHandle == null)
            {
                _logger.LogWarning("Failed to acquire lock for payment processing: {PaymentId}", paymentId);
                PaymentLifecycleOperations.WithLabels("process", "lock_failed").Inc();
                throw new InvalidOperationException("Payment processing is already in progress");
            }

            var payment = await _paymentRepository.GetByIdAsync(paymentId, cancellationToken);
            if (payment == null)
            {
                _logger.LogError("Payment not found: {PaymentId}", paymentId);
                PaymentLifecycleOperations.WithLabels("process", "not_found").Inc();
                throw new InvalidOperationException("Payment not found");
            }

            // Validate state transition
            if (!await _paymentStateMachine.CanTransitionAsync(payment, PaymentStatus.PROCESSING, cancellationToken))
            {
                _logger.LogError("Invalid state transition for payment processing: {PaymentId}, Current: {Status}", 
                    paymentId, payment.Status);
                PaymentLifecycleOperations.WithLabels("process", "invalid_state").Inc();
                throw new InvalidOperationException($"Cannot process payment in {payment.Status} state");
            }

            var previousStatus = payment.Status;
            payment.Status = PaymentStatus.PROCESSING;
            payment.UpdatedAt = DateTime.UtcNow;

            await _paymentRepository.UpdateAsync(payment, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            // Publish state transition event
            await _eventService.PublishStateTransitionAsync(paymentId, previousStatus, PaymentStatus.PROCESSING, cancellationToken);

            _logger.LogInformation("Payment processing started: {PaymentId}", paymentId);
            PaymentLifecycleOperations.WithLabels("process", "success").Inc();
            PaymentStateTransitions.WithLabels(previousStatus.ToString(), "PROCESSING", "success").Inc();
            
            return payment;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process payment: {PaymentId}", paymentId);
            PaymentLifecycleOperations.WithLabels("process", "error").Inc();
            throw;
        }
    }

    public async Task<Payment> AuthorizePaymentAsync(Guid paymentId, CancellationToken cancellationToken = default)
    {
        var result = await AuthorizePaymentWithTransitionAsync(paymentId, cancellationToken);
        if (!result.IsSuccess)
        {
            throw new InvalidOperationException($"Failed to authorize payment: {string.Join(", ", result.Errors)}");
        }
        return result.Payment!;
    }

    /// <summary>
    /// Authorize payment and return transition details for audit trail
    /// </summary>
    public async Task<PaymentLifecycleResult> AuthorizePaymentWithTransitionAsync(Guid paymentId, CancellationToken cancellationToken = default)
    {
        using var activity = PaymentLifecycleOperationDuration.WithLabels("authorize").NewTimer();
        var lockKey = $"payment:authorize:{paymentId}";

        try
        {
            await using var lockHandle = await _distributedLockService.AcquireLockAsync(lockKey, _lockTimeout, cancellationToken);
            if (lockHandle == null)
            {
                _logger.LogWarning("Failed to acquire lock for payment authorization: {PaymentId}", paymentId);
                PaymentLifecycleOperations.WithLabels("authorize", "lock_failed").Inc();
                return new PaymentLifecycleResult
                {
                    IsSuccess = false,
                    Errors = new List<string> { "Payment authorization is already in progress" }
                };
            }

            var payment = await _paymentRepository.GetByIdAsync(paymentId, cancellationToken);
            if (payment == null)
            {
                _logger.LogError("Payment not found: {PaymentId}", paymentId);
                PaymentLifecycleOperations.WithLabels("authorize", "not_found").Inc();
                return new PaymentLifecycleResult
                {
                    IsSuccess = false,
                    Errors = new List<string> { "Payment not found" }
                };
            }

            // Use proper state machine transition
            var transitionResult = await _paymentStateMachine.TransitionAsync(
                payment, 
                PaymentStatus.AUTHORIZED, 
                null, // userId - could be passed as parameter
                null, // context - could include additional audit information
                cancellationToken);

            if (!transitionResult.IsSuccess)
            {
                _logger.LogError("Invalid state transition for payment authorization: {PaymentId}, Current: {Status}, Errors: {Errors}", 
                    paymentId, payment.Status, string.Join(", ", transitionResult.Errors));
                PaymentLifecycleOperations.WithLabels("authorize", "invalid_state").Inc();
                return new PaymentLifecycleResult
                {
                    IsSuccess = false,
                    Errors = transitionResult.Errors,
                    TransitionId = transitionResult.TransitionId,
                    FromStatus = transitionResult.FromStatus,
                    ToStatus = transitionResult.ToStatus
                };
            }

            // Payment status is updated by the state machine, get the updated payment
            var updatedPayment = await _paymentRepository.GetByIdAsync(paymentId, cancellationToken);

            _logger.LogInformation("Payment authorized successfully: {PaymentId}, TransitionId: {TransitionId}", 
                paymentId, transitionResult.TransitionId);
            PaymentLifecycleOperations.WithLabels("authorize", "success").Inc();
            PaymentStateTransitions.WithLabels(transitionResult.FromStatus.ToString(), "AUTHORIZED", "success").Inc();

            return new PaymentLifecycleResult
            {
                IsSuccess = true,
                Payment = updatedPayment,
                TransitionId = transitionResult.TransitionId,
                TransitionedAt = transitionResult.TransitionedAt,
                FromStatus = transitionResult.FromStatus,
                ToStatus = transitionResult.ToStatus,
                Context = transitionResult.Context
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to authorize payment: {PaymentId}", paymentId);
            PaymentLifecycleOperations.WithLabels("authorize", "error").Inc();
            return new PaymentLifecycleResult
            {
                IsSuccess = false,
                Errors = new List<string> { ex.Message }
            };
        }
    }

    public async Task<Payment> ConfirmPaymentAsync(Guid paymentId, CancellationToken cancellationToken = default)
    {
        using var activity = PaymentLifecycleOperationDuration.WithLabels("confirm").NewTimer();
        var lockKey = $"payment:confirm:{paymentId}";

        try
        {
            await using var lockHandle = await _distributedLockService.AcquireLockAsync(lockKey, _lockTimeout, cancellationToken);
            if (lockHandle == null)
            {
                _logger.LogWarning("Failed to acquire lock for payment confirmation: {PaymentId}", paymentId);
                PaymentLifecycleOperations.WithLabels("confirm", "lock_failed").Inc();
                throw new InvalidOperationException("Payment confirmation is already in progress");
            }

            var payment = await _paymentRepository.GetByIdAsync(paymentId, cancellationToken);
            if (payment == null)
            {
                _logger.LogError("Payment not found: {PaymentId}", paymentId);
                PaymentLifecycleOperations.WithLabels("confirm", "not_found").Inc();
                throw new InvalidOperationException("Payment not found");
            }

            // Validate state transition
            if (!await _paymentStateMachine.CanTransitionAsync(payment, PaymentStatus.CONFIRMED, cancellationToken))
            {
                _logger.LogError("Invalid state transition for payment confirmation: {PaymentId}, Current: {Status}", 
                    paymentId, payment.Status);
                PaymentLifecycleOperations.WithLabels("confirm", "invalid_state").Inc();
                throw new InvalidOperationException($"Cannot confirm payment in {payment.Status} state");
            }

            var previousStatus = payment.Status;
            payment.Status = PaymentStatus.CONFIRMED;
            payment.UpdatedAt = DateTime.UtcNow;

            await _paymentRepository.UpdateAsync(payment, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            // Remove from active sessions
            _activePaymentSessions.TryRemove(paymentId, out _);
            ActivePaymentsGauge.Dec();

            // Publish state transition event
            await _eventService.PublishStateTransitionAsync(paymentId, previousStatus, PaymentStatus.CONFIRMED, cancellationToken);

            _logger.LogInformation("Payment confirmed successfully: {PaymentId}", paymentId);
            PaymentLifecycleOperations.WithLabels("confirm", "success").Inc();
            PaymentStateTransitions.WithLabels(previousStatus.ToString(), "CONFIRMED", "success").Inc();
            
            return payment;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to confirm payment: {PaymentId}", paymentId);
            PaymentLifecycleOperations.WithLabels("confirm", "error").Inc();
            throw;
        }
    }

    public async Task<Payment> CancelPaymentAsync(Guid paymentId, string reason, CancellationToken cancellationToken = default)
    {
        using var activity = PaymentLifecycleOperationDuration.WithLabels("cancel").NewTimer();
        var lockKey = $"payment:cancel:{paymentId}";

        try
        {
            await using var lockHandle = await _distributedLockService.AcquireLockAsync(lockKey, _lockTimeout, cancellationToken);
            if (lockHandle == null)
            {
                _logger.LogWarning("Failed to acquire lock for payment cancellation: {PaymentId}", paymentId);
                PaymentLifecycleOperations.WithLabels("cancel", "lock_failed").Inc();
                throw new InvalidOperationException("Payment cancellation is already in progress");
            }

            var payment = await _paymentRepository.GetByIdAsync(paymentId, cancellationToken);
            if (payment == null)
            {
                _logger.LogError("Payment not found: {PaymentId}", paymentId);
                PaymentLifecycleOperations.WithLabels("cancel", "not_found").Inc();
                throw new InvalidOperationException("Payment not found");
            }

            // Validate state transition
            if (!await _paymentStateMachine.CanTransitionAsync(payment, PaymentStatus.CANCELLED, cancellationToken))
            {
                _logger.LogError("Invalid state transition for payment cancellation: {PaymentId}, Current: {Status}", 
                    paymentId, payment.Status);
                PaymentLifecycleOperations.WithLabels("cancel", "invalid_state").Inc();
                throw new InvalidOperationException($"Cannot cancel payment in {payment.Status} state");
            }

            var previousStatus = payment.Status;
            payment.Status = PaymentStatus.CANCELLED;
            payment.ErrorMessage = reason;
            payment.UpdatedAt = DateTime.UtcNow;

            await _paymentRepository.UpdateAsync(payment, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            // Remove from active sessions
            _activePaymentSessions.TryRemove(paymentId, out _);
            ActivePaymentsGauge.Dec();

            // Publish state transition event
            await _eventService.PublishStateTransitionAsync(paymentId, previousStatus, PaymentStatus.CANCELLED, cancellationToken);

            _logger.LogInformation("Payment cancelled successfully: {PaymentId}, Reason: {Reason}", paymentId, reason);
            PaymentLifecycleOperations.WithLabels("cancel", "success").Inc();
            PaymentStateTransitions.WithLabels(previousStatus.ToString(), "CANCELLED", "success").Inc();
            
            return payment;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to cancel payment: {PaymentId}", paymentId);
            PaymentLifecycleOperations.WithLabels("cancel", "error").Inc();
            throw;
        }
    }

    public async Task<Payment> RefundPaymentAsync(Guid paymentId, decimal amount, string reason, CancellationToken cancellationToken = default)
    {
        using var activity = PaymentLifecycleOperationDuration.WithLabels("refund").NewTimer();
        var lockKey = $"payment:refund:{paymentId}";

        try
        {
            await using var lockHandle = await _distributedLockService.AcquireLockAsync(lockKey, _lockTimeout, cancellationToken);
            if (lockHandle == null)
            {
                _logger.LogWarning("Failed to acquire lock for payment refund: {PaymentId}", paymentId);
                PaymentLifecycleOperations.WithLabels("refund", "lock_failed").Inc();
                throw new InvalidOperationException("Payment refund is already in progress");
            }

            var payment = await _paymentRepository.GetByIdAsync(paymentId, cancellationToken);
            if (payment == null)
            {
                _logger.LogError("Payment not found: {PaymentId}", paymentId);
                PaymentLifecycleOperations.WithLabels("refund", "not_found").Inc();
                throw new InvalidOperationException("Payment not found");
            }

            // Validate state transition
            if (!await _paymentStateMachine.CanTransitionAsync(payment, PaymentStatus.REFUNDED, cancellationToken))
            {
                _logger.LogError("Invalid state transition for payment refund: {PaymentId}, Current: {Status}", 
                    paymentId, payment.Status);
                PaymentLifecycleOperations.WithLabels("refund", "invalid_state").Inc();
                throw new InvalidOperationException($"Cannot refund payment in {payment.Status} state");
            }

            // Validate refund amount
            if (amount <= 0 || amount > payment.Amount)
            {
                _logger.LogError("Invalid refund amount: {Amount} for payment: {PaymentId}", amount, paymentId);
                PaymentLifecycleOperations.WithLabels("refund", "invalid_amount").Inc();
                throw new InvalidOperationException("Invalid refund amount");
            }

            var previousStatus = payment.Status;
            payment.Status = PaymentStatus.REFUNDED;
            payment.ErrorMessage = reason;
            payment.UpdatedAt = DateTime.UtcNow;

            await _paymentRepository.UpdateAsync(payment, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            // Remove from active sessions
            _activePaymentSessions.TryRemove(paymentId, out _);
            ActivePaymentsGauge.Dec();

            // Publish state transition event
            await _eventService.PublishStateTransitionAsync(paymentId, previousStatus, PaymentStatus.REFUNDED, cancellationToken);

            _logger.LogInformation("Payment refunded successfully: {PaymentId}, Amount: {Amount}, Reason: {Reason}", 
                paymentId, amount, reason);
            PaymentLifecycleOperations.WithLabels("refund", "success").Inc();
            PaymentStateTransitions.WithLabels(previousStatus.ToString(), "REFUNDED", "success").Inc();
            
            return payment;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to refund payment: {PaymentId}", paymentId);
            PaymentLifecycleOperations.WithLabels("refund", "error").Inc();
            throw;
        }
    }

    public async Task<Payment> ExpirePaymentAsync(Guid paymentId, CancellationToken cancellationToken = default)
    {
        using var activity = PaymentLifecycleOperationDuration.WithLabels("expire").NewTimer();
        var lockKey = $"payment:expire:{paymentId}";

        try
        {
            await using var lockHandle = await _distributedLockService.AcquireLockAsync(lockKey, _lockTimeout, cancellationToken);
            if (lockHandle == null)
            {
                _logger.LogWarning("Failed to acquire lock for payment expiration: {PaymentId}", paymentId);
                PaymentLifecycleOperations.WithLabels("expire", "lock_failed").Inc();
                return null;
            }

            var payment = await _paymentRepository.GetByIdAsync(paymentId, cancellationToken);
            if (payment == null)
            {
                PaymentLifecycleOperations.WithLabels("expire", "not_found").Inc();
                return null;
            }

            // Check if payment is already in final state
            if (payment.Status == PaymentStatus.CONFIRMED || 
                payment.Status == PaymentStatus.CANCELLED || 
                payment.Status == PaymentStatus.REFUNDED ||
                payment.Status == PaymentStatus.EXPIRED)
            {
                return payment;
            }

            var previousStatus = payment.Status;
            payment.Status = PaymentStatus.EXPIRED;
            payment.ErrorMessage = "Payment expired due to timeout";
            payment.UpdatedAt = DateTime.UtcNow;

            await _paymentRepository.UpdateAsync(payment, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            // Remove from active sessions
            _activePaymentSessions.TryRemove(paymentId, out _);
            ActivePaymentsGauge.Dec();

            // Publish state transition event
            await _eventService.PublishStateTransitionAsync(paymentId, previousStatus, PaymentStatus.EXPIRED, cancellationToken);

            _logger.LogInformation("Payment expired: {PaymentId}", paymentId);
            PaymentLifecycleOperations.WithLabels("expire", "success").Inc();
            PaymentStateTransitions.WithLabels(previousStatus.ToString(), "EXPIRED", "success").Inc();
            
            return payment;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to expire payment: {PaymentId}", paymentId);
            PaymentLifecycleOperations.WithLabels("expire", "error").Inc();
            throw;
        }
    }

    public async Task<Payment> FailPaymentAsync(Guid paymentId, string errorCode, string errorMessage, CancellationToken cancellationToken = default)
    {
        using var activity = PaymentLifecycleOperationDuration.WithLabels("fail").NewTimer();
        var lockKey = $"payment:fail:{paymentId}";

        try
        {
            await using var lockHandle = await _distributedLockService.AcquireLockAsync(lockKey, _lockTimeout, cancellationToken);
            if (lockHandle == null)
            {
                _logger.LogWarning("Failed to acquire lock for payment failure: {PaymentId}", paymentId);
                PaymentLifecycleOperations.WithLabels("fail", "lock_failed").Inc();
                throw new InvalidOperationException("Payment failure processing is already in progress");
            }

            var payment = await _paymentRepository.GetByIdAsync(paymentId, cancellationToken);
            if (payment == null)
            {
                _logger.LogError("Payment not found: {PaymentId}", paymentId);
                PaymentLifecycleOperations.WithLabels("fail", "not_found").Inc();
                throw new InvalidOperationException("Payment not found");
            }

            var previousStatus = payment.Status;
            payment.Status = PaymentStatus.CANCELLED;
            payment.ErrorCode = errorCode;
            payment.ErrorMessage = errorMessage;
            payment.UpdatedAt = DateTime.UtcNow;

            await _paymentRepository.UpdateAsync(payment, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            // Remove from active sessions
            _activePaymentSessions.TryRemove(paymentId, out _);
            ActivePaymentsGauge.Dec();

            // Publish state transition event
            await _eventService.PublishStateTransitionAsync(paymentId, previousStatus, PaymentStatus.CANCELLED, cancellationToken);

            _logger.LogError("Payment failed: {PaymentId}, Error: {ErrorCode} - {ErrorMessage}", 
                paymentId, errorCode, errorMessage);
            PaymentLifecycleOperations.WithLabels("fail", "success").Inc();
            PaymentStateTransitions.WithLabels(previousStatus.ToString(), "CANCELLED", "success").Inc();
            
            return payment;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fail payment: {PaymentId}", paymentId);
            PaymentLifecycleOperations.WithLabels("fail", "error").Inc();
            throw;
        }
    }

    public async Task<Payment> GetPaymentStatusAsync(Guid paymentId, CancellationToken cancellationToken = default)
    {
        try
        {
            var payment = await _paymentRepository.GetByIdAsync(paymentId, cancellationToken);
            if (payment == null)
            {
                _logger.LogWarning("Payment not found: {PaymentId}", paymentId);
                throw new InvalidOperationException("Payment not found");
            }

            return payment;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get payment status: {PaymentId}", paymentId);
            throw;
        }
    }

    public async Task<IEnumerable<Payment>> GetActivePaymentsAsync(Guid teamId, CancellationToken cancellationToken = default)
    {
        try
        {
            var activeStatuses = new[]
            {
                PaymentStatus.NEW,
                PaymentStatus.PROCESSING,
                PaymentStatus.AUTHORIZED
            };

            var payments = await _paymentRepository.GetActivePaymentsByTeamAsync(teamId, cancellationToken);
            return payments;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get active payments for team: {TeamId}", teamId);
            throw;
        }
    }

    public async Task<bool> IsPaymentExpiredAsync(Guid paymentId, CancellationToken cancellationToken = default)
    {
        try
        {
            var payment = await _paymentRepository.GetByIdAsync(paymentId, cancellationToken);
            if (payment == null) return false;

            // Check if payment has exceeded timeout
            return DateTime.UtcNow - payment.CreatedAt > _paymentProcessingTimeout;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to check payment expiration: {PaymentId}", paymentId);
            return false;
        }
    }

    public async Task ProcessExpiredPaymentsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var expiredPayments = await _paymentRepository.GetExpiredPaymentsAsync(DateTime.UtcNow - _paymentProcessingTimeout, cancellationToken);
            
            foreach (var payment in expiredPayments)
            {
                try
                {
                    await ExpirePaymentAsync(payment.Id, cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to expire payment: {PaymentId}", payment.PaymentId);
                }
            }

            _logger.LogInformation("Processed {Count} expired payments", expiredPayments.Count());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process expired payments");
            throw;
        }
    }

    private string GeneratePaymentUrl(Payment payment)
    {
        // Generate a secure payment URL for hosted payment pages
        return $"/payment/{payment.PaymentId}/form";
    }
}