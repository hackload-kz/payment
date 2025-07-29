using Microsoft.Extensions.Logging;
using PaymentGateway.Core.Entities;
using PaymentGateway.Core.Enums;
using PaymentGateway.Core.Interfaces;
using PaymentGateway.Core.Repositories;
using System.Collections.Concurrent;

namespace PaymentGateway.Core.Services;

public interface IPaymentStateMachine
{
    Task<PaymentStateTransitionResult> TransitionAsync(Payment payment, PaymentStatus targetStatus, string? userId = null, Dictionary<string, object>? context = null, CancellationToken cancellationToken = default);
    Task<bool> CanTransitionAsync(Payment payment, PaymentStatus targetStatus, CancellationToken cancellationToken = default);
    Task<IEnumerable<PaymentStatus>> GetValidTransitionsAsync(Payment payment, CancellationToken cancellationToken = default);
    Task<PaymentStateTransitionResult> RollbackAsync(Payment payment, string transitionId, string? userId = null, CancellationToken cancellationToken = default);
    Task<IEnumerable<PaymentStateTransition>> GetTransitionHistoryAsync(Guid paymentId, CancellationToken cancellationToken = default);
}

public class PaymentStateTransitionResult
{
    public bool IsSuccess { get; set; }
    public PaymentStatus FromStatus { get; set; }
    public PaymentStatus ToStatus { get; set; }
    public string TransitionId { get; set; } = string.Empty;
    public DateTime TransitionedAt { get; set; }
    public string? UserId { get; set; }
    public List<string> Errors { get; set; } = new();
    public Dictionary<string, object> Context { get; set; } = new();
    public PaymentStateTransition? Transition { get; set; }
}

public class PaymentStateTransition : BaseEntity
{
    public Guid PaymentId { get; set; }
    public PaymentStatus FromStatus { get; set; }
    public PaymentStatus ToStatus { get; set; }
    public string TransitionId { get; set; } = string.Empty;
    public DateTime TransitionedAt { get; set; }
    public string? UserId { get; set; }
    public string? Reason { get; set; }
    public Dictionary<string, object> Context { get; set; } = new();
    public bool IsRollback { get; set; } = false;
    public string? RollbackFromTransitionId { get; set; }
    public virtual Payment Payment { get; set; } = null!;
}

public class PaymentStateMachine : IPaymentStateMachine
{
    private readonly ILogger<PaymentStateMachine> _logger;
    private readonly IPaymentStateTransitionRepository _transitionRepository;
    private readonly IDistributedLockService _lockService;
    private readonly IPaymentStateMachineMetrics _metrics;
    
    // State transition matrix defining valid transitions
    private static readonly Dictionary<PaymentStatus, HashSet<PaymentStatus>> ValidTransitions = new()
    {
        [PaymentStatus.INIT] = new HashSet<PaymentStatus> { PaymentStatus.NEW, PaymentStatus.EXPIRED },
        [PaymentStatus.NEW] = new HashSet<PaymentStatus> 
        { 
            PaymentStatus.FORM_SHOWED, PaymentStatus.AUTHORIZING, PaymentStatus.CANCELLED, PaymentStatus.EXPIRED 
        },
        [PaymentStatus.FORM_SHOWED] = new HashSet<PaymentStatus> 
        { 
            PaymentStatus.AUTHORIZING, PaymentStatus.CANCELLED, PaymentStatus.EXPIRED 
        },
        [PaymentStatus.ONECHOOSEVISION] = new HashSet<PaymentStatus> 
        { 
            PaymentStatus.FINISHAUTHORIZE, PaymentStatus.AUTH_FAIL, PaymentStatus.CANCELLED 
        },
        [PaymentStatus.FINISHAUTHORIZE] = new HashSet<PaymentStatus> 
        { 
            PaymentStatus.AUTHORIZING, PaymentStatus.AUTH_FAIL, PaymentStatus.CANCELLED 
        },
        [PaymentStatus.AUTHORIZING] = new HashSet<PaymentStatus> 
        { 
            PaymentStatus.AUTHORIZED, PaymentStatus.AUTH_FAIL, PaymentStatus.CANCELLED, PaymentStatus.EXPIRED 
        },
        [PaymentStatus.AUTHORIZED] = new HashSet<PaymentStatus> 
        { 
            PaymentStatus.CONFIRMING, PaymentStatus.REVERSING, PaymentStatus.CANCELLED, PaymentStatus.EXPIRED 
        },
        [PaymentStatus.AUTH_FAIL] = new HashSet<PaymentStatus> 
        { 
            PaymentStatus.AUTHORIZING, PaymentStatus.REJECTED, PaymentStatus.CANCELLED 
        },
        [PaymentStatus.CONFIRM] = new HashSet<PaymentStatus> 
        { 
            PaymentStatus.CONFIRMING, PaymentStatus.CANCELLED 
        },
        [PaymentStatus.CONFIRMING] = new HashSet<PaymentStatus> 
        { 
            PaymentStatus.CONFIRMED, PaymentStatus.AUTH_FAIL, PaymentStatus.CANCELLED 
        },
        [PaymentStatus.CONFIRMED] = new HashSet<PaymentStatus> 
        { 
            PaymentStatus.REFUNDING, PaymentStatus.PARTIAL_REFUNDED 
        },
        [PaymentStatus.CANCEL] = new HashSet<PaymentStatus> 
        { 
            PaymentStatus.CANCELLING 
        },
        [PaymentStatus.CANCELLING] = new HashSet<PaymentStatus> 
        { 
            PaymentStatus.CANCELLED, PaymentStatus.REVERSING 
        },
        [PaymentStatus.REVERSING] = new HashSet<PaymentStatus> 
        { 
            PaymentStatus.REVERSED, PaymentStatus.CANCELLED 
        },
        [PaymentStatus.REFUNDING] = new HashSet<PaymentStatus> 
        { 
            PaymentStatus.REFUNDED, PaymentStatus.PARTIAL_REFUNDED, PaymentStatus.CONFIRMED 
        },
        [PaymentStatus.PARTIAL_REFUNDED] = new HashSet<PaymentStatus> 
        { 
            PaymentStatus.REFUNDING, PaymentStatus.REFUNDED 
        },
        // Final states - no transitions allowed
        [PaymentStatus.CANCELLED] = new HashSet<PaymentStatus>(),
        [PaymentStatus.REVERSED] = new HashSet<PaymentStatus>(),
        [PaymentStatus.REFUNDED] = new HashSet<PaymentStatus>(),
        [PaymentStatus.REJECTED] = new HashSet<PaymentStatus>(),
        [PaymentStatus.DEADLINE_EXPIRED] = new HashSet<PaymentStatus>(),
        [PaymentStatus.EXPIRED] = new HashSet<PaymentStatus>()
    };

    // Critical state transitions that require special handling
    private static readonly HashSet<PaymentStatus> CriticalStates = new()
    {
        PaymentStatus.AUTHORIZING,
        PaymentStatus.CONFIRMING,
        PaymentStatus.REFUNDING,
        PaymentStatus.REVERSING,
        PaymentStatus.CANCELLING
    };

    // Business rule transitions that may have additional validation
    private static readonly Dictionary<PaymentStatus, Func<Payment, (bool IsValid, List<string> Errors)>> BusinessRuleValidators = new()
    {
        [PaymentStatus.AUTHORIZING] = (payment) => payment.ValidateForAuthorization(),
        [PaymentStatus.CONFIRMING] = (payment) => payment.ValidateForConfirmation(),
        [PaymentStatus.REFUNDING] = (payment) => payment.ValidateForRefund(0), // Amount validation done separately
        [PaymentStatus.EXPIRED] = (payment) => 
        {
            var errors = new List<string>();
            if (!payment.HasExpired())
                errors.Add("Payment has not expired yet");
            return (errors.Count == 0, errors);
        }
    };

    public PaymentStateMachine(
        ILogger<PaymentStateMachine> logger,
        IPaymentStateTransitionRepository transitionRepository,
        IDistributedLockService lockService,
        IPaymentStateMachineMetrics metrics)
    {
        _logger = logger;
        _transitionRepository = transitionRepository;
        _lockService = lockService;
        _metrics = metrics;
    }

    public async Task<PaymentStateTransitionResult> TransitionAsync(
        Payment payment, 
        PaymentStatus targetStatus, 
        string? userId = null, 
        Dictionary<string, object>? context = null, 
        CancellationToken cancellationToken = default)
    {
        var transitionId = Guid.NewGuid().ToString();
        var lockKey = $"payment_state_transition_{payment.Id}";
        var lockTimeout = TimeSpan.FromSeconds(30);

        try
        {
            // Acquire distributed lock for concurrent state change protection
            using var lockHandle = await _lockService.AcquireLockAsync(lockKey, lockTimeout, cancellationToken);
            
            if (lockHandle == null)
            {
                _logger.LogWarning("Failed to acquire lock for payment {PaymentId} state transition", payment.Id);
                return new PaymentStateTransitionResult
                {
                    IsSuccess = false,
                    FromStatus = payment.Status,
                    ToStatus = targetStatus,
                    TransitionId = transitionId,
                    Errors = new List<string> { "Unable to acquire lock for state transition. Another transition may be in progress." }
                };
            }

            var result = await ExecuteTransitionAsync(payment, targetStatus, transitionId, userId, context, cancellationToken);
            
            // Record metrics
            await _metrics.RecordTransitionAsync(payment.Status, targetStatus, result.IsSuccess, cancellationToken);
            
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during payment {PaymentId} state transition from {FromStatus} to {ToStatus}", 
                payment.Id, payment.Status, targetStatus);
                
            await _metrics.RecordTransitionErrorAsync(payment.Status, targetStatus, ex.GetType().Name, cancellationToken);
            
            return new PaymentStateTransitionResult
            {
                IsSuccess = false,
                FromStatus = payment.Status,
                ToStatus = targetStatus,
                TransitionId = transitionId,
                Errors = new List<string> { "An unexpected error occurred during state transition" }
            };
        }
    }

    private async Task<PaymentStateTransitionResult> ExecuteTransitionAsync(
        Payment payment,
        PaymentStatus targetStatus,
        string transitionId,
        string? userId,
        Dictionary<string, object>? context,
        CancellationToken cancellationToken)
    {
        var fromStatus = payment.Status;
        var now = DateTime.UtcNow;
        
        var result = new PaymentStateTransitionResult
        {
            FromStatus = fromStatus,
            ToStatus = targetStatus,
            TransitionId = transitionId,
            TransitionedAt = now,
            UserId = userId,
            Context = context ?? new Dictionary<string, object>()
        };

        // Validate transition is allowed
        if (!IsValidTransition(fromStatus, targetStatus))
        {
            result.Errors.Add($"Invalid transition from {fromStatus} to {targetStatus}");
            _logger.LogWarning("Invalid state transition attempted for payment {PaymentId}: {FromStatus} -> {ToStatus}", 
                payment.Id, fromStatus, targetStatus);
            return result;
        }

        // Execute business rule validation
        var businessRuleResult = await ValidateBusinessRulesAsync(payment, targetStatus, cancellationToken);
        if (!businessRuleResult.IsValid)
        {
            result.Errors.AddRange(businessRuleResult.Errors);
            _logger.LogWarning("Business rule validation failed for payment {PaymentId} transition {FromStatus} -> {ToStatus}: {Errors}", 
                payment.Id, fromStatus, targetStatus, string.Join(", ", businessRuleResult.Errors));
            return result;
        }

        // Create transition record
        var transition = new PaymentStateTransition
        {
            PaymentId = payment.Id,
            FromStatus = fromStatus,
            ToStatus = targetStatus,
            TransitionId = transitionId,
            TransitionedAt = now,
            UserId = userId,
            Context = context ?? new Dictionary<string, object>(),
            Reason = context?.ContainsKey("reason") == true ? context["reason"]?.ToString() : null
        };

        try
        {
            // Update payment status and timestamps
            payment.Status = targetStatus;
            UpdatePaymentTimestamps(payment, targetStatus, now);
            
            // Save transition to audit trail
            await _transitionRepository.AddAsync(transition, cancellationToken);
            
            result.IsSuccess = true;
            result.Transition = transition;
            
            _logger.LogInformation("Payment {PaymentId} successfully transitioned from {FromStatus} to {ToStatus} by user {UserId}", 
                payment.Id, fromStatus, targetStatus, userId ?? "system");
                
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save state transition for payment {PaymentId}", payment.Id);
            result.Errors.Add("Failed to save state transition");
            return result;
        }
    }

    public async Task<bool> CanTransitionAsync(Payment payment, PaymentStatus targetStatus, CancellationToken cancellationToken = default)
    {
        if (!IsValidTransition(payment.Status, targetStatus))
            return false;

        var businessRuleResult = await ValidateBusinessRulesAsync(payment, targetStatus, cancellationToken);
        return businessRuleResult.IsValid;
    }

    public async Task<IEnumerable<PaymentStatus>> GetValidTransitionsAsync(Payment payment, CancellationToken cancellationToken = default)
    {
        if (!ValidTransitions.TryGetValue(payment.Status, out var validTransitions))
            return Enumerable.Empty<PaymentStatus>();

        var result = new List<PaymentStatus>();
        
        foreach (var targetStatus in validTransitions)
        {
            if (await CanTransitionAsync(payment, targetStatus, cancellationToken))
            {
                result.Add(targetStatus);
            }
        }

        return result;
    }

    public async Task<PaymentStateTransitionResult> RollbackAsync(
        Payment payment, 
        string transitionId, 
        string? userId = null, 
        CancellationToken cancellationToken = default)
    {
        try
        {
            var originalTransition = await _transitionRepository.GetByTransitionIdAsync(transitionId, cancellationToken);
            if (originalTransition == null)
            {
                return new PaymentStateTransitionResult
                {
                    IsSuccess = false,
                    Errors = new List<string> { $"Transition with ID {transitionId} not found" }
                };
            }

            if (originalTransition.PaymentId != payment.Id)
            {
                return new PaymentStateTransitionResult
                {
                    IsSuccess = false,
                    Errors = new List<string> { "Transition does not belong to this payment" }
                };
            }

            // Check if rollback is allowed for the current state
            if (!CanRollback(payment.Status, originalTransition.FromStatus))
            {
                return new PaymentStateTransitionResult
                {
                    IsSuccess = false,
                    Errors = new List<string> { $"Cannot rollback from {payment.Status} to {originalTransition.FromStatus}" }
                };
            }

            var rollbackTransitionId = Guid.NewGuid().ToString();
            var context = new Dictionary<string, object>
            {
                ["rollback"] = true,
                ["original_transition_id"] = transitionId,
                ["reason"] = "State rollback"
            };

            var result = await TransitionAsync(payment, originalTransition.FromStatus, userId, context, cancellationToken);
            
            if (result.IsSuccess && result.Transition != null)
            {
                result.Transition.IsRollback = true;
                result.Transition.RollbackFromTransitionId = transitionId;
                // The transition is already tracked by EF, changes will be saved when SaveChanges is called
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during rollback for payment {PaymentId}, transition {TransitionId}", 
                payment.Id, transitionId);
            return new PaymentStateTransitionResult
            {
                IsSuccess = false,
                Errors = new List<string> { "An error occurred during rollback" }
            };
        }
    }

    public async Task<IEnumerable<PaymentStateTransition>> GetTransitionHistoryAsync(Guid paymentId, CancellationToken cancellationToken = default)
    {
        return await _transitionRepository.GetByPaymentIdAsync(paymentId, cancellationToken);
    }

    private static bool IsValidTransition(PaymentStatus fromStatus, PaymentStatus toStatus)
    {
        return ValidTransitions.TryGetValue(fromStatus, out var validTransitions) && 
               validTransitions.Contains(toStatus);
    }

    private static async Task<(bool IsValid, List<string> Errors)> ValidateBusinessRulesAsync(
        Payment payment, 
        PaymentStatus targetStatus, 
        CancellationToken cancellationToken)
    {
        if (BusinessRuleValidators.TryGetValue(targetStatus, out var validator))
        {
            return validator(payment);
        }

        return (true, new List<string>());
    }

    private static bool CanRollback(PaymentStatus currentStatus, PaymentStatus targetStatus)
    {
        // Only allow rollback from non-final states
        var finalStates = new HashSet<PaymentStatus>
        {
            PaymentStatus.CONFIRMED,
            PaymentStatus.CANCELLED,
            PaymentStatus.REVERSED,
            PaymentStatus.REFUNDED,
            PaymentStatus.REJECTED,
            PaymentStatus.EXPIRED,
            PaymentStatus.DEADLINE_EXPIRED
        };

        return !finalStates.Contains(currentStatus) && IsValidTransition(currentStatus, targetStatus);
    }

    private static void UpdatePaymentTimestamps(Payment payment, PaymentStatus status, DateTime timestamp)
    {
        switch (status)
        {
            case PaymentStatus.NEW:
                payment.InitializedAt = timestamp;
                break;
            case PaymentStatus.FORM_SHOWED:
                payment.FormShowedAt = timestamp;
                break;
            case PaymentStatus.AUTHORIZING:
                payment.AuthorizingStartedAt = timestamp;
                break;
            case PaymentStatus.AUTHORIZED:
                payment.AuthorizedAt = timestamp;
                break;
            case PaymentStatus.CONFIRMING:
                payment.ConfirmingStartedAt = timestamp;
                break;
            case PaymentStatus.CONFIRMED:
                payment.ConfirmedAt = timestamp;
                break;
            case PaymentStatus.CANCELLING:
                payment.CancellingStartedAt = timestamp;
                break;
            case PaymentStatus.CANCELLED:
                payment.CancelledAt = timestamp;
                break;
            case PaymentStatus.REVERSING:
                payment.ReversingStartedAt = timestamp;
                break;
            case PaymentStatus.REVERSED:
                payment.ReversedAt = timestamp;
                break;
            case PaymentStatus.REFUNDING:
                payment.RefundingStartedAt = timestamp;
                break;
            case PaymentStatus.REFUNDED:
            case PaymentStatus.PARTIAL_REFUNDED:
                payment.RefundedAt = timestamp;
                break;
            case PaymentStatus.REJECTED:
                payment.RejectedAt = timestamp;
                break;
            case PaymentStatus.EXPIRED:
            case PaymentStatus.DEADLINE_EXPIRED:
                payment.ExpiredAt = timestamp;
                break;
        }
    }
}