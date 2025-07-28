using Microsoft.Extensions.Logging;
using Payment.Gateway.Models;

namespace Payment.Gateway.Services;

public class PaymentStateMachine : IPaymentStateMachine
{
    private readonly IPaymentRepository _paymentRepository;
    private readonly ILogger<PaymentStateMachine> _logger;

    // Define valid state transitions based on payment lifecycle flowchart
    private static readonly Dictionary<PaymentStatus, PaymentStatus[]> _validTransitions = new()
    {
        // Payment Initialization Phase
        [PaymentStatus.INIT] = new[] { PaymentStatus.NEW },
        [PaymentStatus.NEW] = new[] { PaymentStatus.CANCELLED, PaymentStatus.DEADLINE_EXPIRED, PaymentStatus.FORM_SHOWED },
        [PaymentStatus.FORM_SHOWED] = new[] { PaymentStatus.ONECHOOSEVISION, PaymentStatus.DEADLINE_EXPIRED, PaymentStatus.CANCELLED },

        // Pre-Authorization Phase
        [PaymentStatus.ONECHOOSEVISION] = new[] { PaymentStatus.FINISHAUTHORIZE, PaymentStatus.DEADLINE_EXPIRED },
        [PaymentStatus.FINISHAUTHORIZE] = new[] { PaymentStatus.AUTHORIZING, PaymentStatus.DEADLINE_EXPIRED },
        [PaymentStatus.AUTHORIZING] = new[] { PaymentStatus.THREE_DS_CHECKING, PaymentStatus.AUTHORIZED, PaymentStatus.AUTH_FAIL, PaymentStatus.REJECTED },

        // 3DS Processing Phase
        [PaymentStatus.THREE_DS_CHECKING] = new[] { PaymentStatus.SUBMITPASSIVIZATION, PaymentStatus.SUBMITPASSIVIZATION2, PaymentStatus.THREE_DS_CHECKED, PaymentStatus.DEADLINE_EXPIRED },
        [PaymentStatus.SUBMITPASSIVIZATION] = new[] { PaymentStatus.THREE_DS_CHECKED, PaymentStatus.DEADLINE_EXPIRED },
        [PaymentStatus.SUBMITPASSIVIZATION2] = new[] { PaymentStatus.THREE_DS_CHECKED, PaymentStatus.DEADLINE_EXPIRED },
        [PaymentStatus.THREE_DS_CHECKED] = new[] { PaymentStatus.AUTHORIZED, PaymentStatus.AUTH_FAIL, PaymentStatus.AUTHORIZING },

        // Final Authorization States
        [PaymentStatus.AUTHORIZED] = new[] { PaymentStatus.CONFIRMING, PaymentStatus.REVERSING },
        [PaymentStatus.AUTH_FAIL] = new[] { PaymentStatus.AUTHORIZING, PaymentStatus.REJECTED },

        // Confirmation Phase
        [PaymentStatus.CONFIRMING] = new[] { PaymentStatus.CONFIRMED, PaymentStatus.AUTH_FAIL },
        [PaymentStatus.CONFIRMED] = new[] { PaymentStatus.REFUNDING },

        // Reversal Operations
        [PaymentStatus.REVERSING] = new[] { PaymentStatus.REVERSED, PaymentStatus.PARTIAL_REVERSED },

        // Refund Operations
        [PaymentStatus.REFUNDING] = new[] { PaymentStatus.REFUNDED, PaymentStatus.PARTIAL_REFUNDED },

        // Terminal states (no further transitions)
        [PaymentStatus.CANCELLED] = Array.Empty<PaymentStatus>(),
        [PaymentStatus.DEADLINE_EXPIRED] = Array.Empty<PaymentStatus>(),
        [PaymentStatus.REJECTED] = Array.Empty<PaymentStatus>(),
        [PaymentStatus.REVERSED] = Array.Empty<PaymentStatus>(),
        [PaymentStatus.PARTIAL_REVERSED] = Array.Empty<PaymentStatus>(),
        [PaymentStatus.REFUNDED] = Array.Empty<PaymentStatus>(),
        [PaymentStatus.PARTIAL_REFUNDED] = Array.Empty<PaymentStatus>()
    };

    public PaymentStateMachine(IPaymentRepository paymentRepository, ILogger<PaymentStateMachine> logger)
    {
        _paymentRepository = paymentRepository;
        _logger = logger;
    }

    public bool CanTransition(PaymentStatus from, PaymentStatus to)
    {
        if (!_validTransitions.TryGetValue(from, out var validStates))
        {
            _logger.LogWarning("No valid transitions defined for status {FromStatus}", from);
            return false;
        }

        var canTransition = validStates.Contains(to);
        
        if (!canTransition)
        {
            _logger.LogWarning("Invalid state transition from {FromStatus} to {ToStatus}", from, to);
        }

        return canTransition;
    }

    public async Task<bool> TransitionAsync(string paymentId, PaymentStatus newStatus, string? errorCode = null, string? message = null)
    {
        try
        {
            // Get current payment state
            var payment = await _paymentRepository.GetByIdAsync(paymentId);
            if (payment == null)
            {
                _logger.LogError("Payment {PaymentId} not found", paymentId);
                return false;
            }

            var currentStatus = payment.CurrentStatus;

            // Validate transition
            if (!CanTransition(currentStatus, newStatus))
            {
                _logger.LogError("Invalid state transition for payment {PaymentId} from {CurrentStatus} to {NewStatus}", 
                    paymentId, currentStatus, newStatus);
                return false;
            }

            // Update payment status
            payment.CurrentStatus = newStatus;
            payment.UpdatedDate = DateTime.UtcNow;
            payment.ErrorCode = errorCode;
            payment.Message = message;

            // Handle attempt counting for authorization states
            if (newStatus == PaymentStatus.AUTHORIZING)
            {
                payment.AttemptCount++;
            }

            // Set expiration date for deadline-sensitive states
            if (newStatus == PaymentStatus.NEW || newStatus == PaymentStatus.FORM_SHOWED)
            {
                payment.ExpirationDate = DateTime.UtcNow.AddMinutes(30); // 30 minutes default
            }

            // Update payment in repository
            await _paymentRepository.UpdateAsync(payment);

            // Add status history entry
            await _paymentRepository.AddStatusHistoryAsync(paymentId, newStatus, errorCode, message);

            _logger.LogInformation("Payment {PaymentId} transitioned from {FromStatus} to {ToStatus}", 
                paymentId, currentStatus, newStatus);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error transitioning payment {PaymentId} to status {NewStatus}", paymentId, newStatus);
            return false;
        }
    }

    public PaymentStatus[] GetValidNextStates(PaymentStatus currentStatus)
    {
        if (_validTransitions.TryGetValue(currentStatus, out var validStates))
        {
            return validStates;
        }

        _logger.LogWarning("No valid transitions defined for status {CurrentStatus}", currentStatus);
        return Array.Empty<PaymentStatus>();
    }
}