using Payment.Gateway.Models;

namespace Payment.Gateway.Services;

public interface IPaymentStateMachine
{
    bool CanTransition(PaymentStatus from, PaymentStatus to);
    Task<bool> TransitionAsync(string paymentId, PaymentStatus newStatus, string? errorCode = null, string? message = null);
    PaymentStatus[] GetValidNextStates(PaymentStatus currentStatus);
}