using Payment.Gateway.Models;

namespace Payment.Gateway.Services;

public class PaymentStateMachine : IPaymentStateMachine
{
    public bool CanTransition(PaymentStatus from, PaymentStatus to)
    {
        // Implementation will be added in Task 3
        throw new NotImplementedException("Will be implemented in Task 3");
    }

    public Task<bool> TransitionAsync(string paymentId, PaymentStatus newStatus, string? errorCode = null, string? message = null)
    {
        // Implementation will be added in Task 3
        throw new NotImplementedException("Will be implemented in Task 3");
    }

    public PaymentStatus[] GetValidNextStates(PaymentStatus currentStatus)
    {
        // Implementation will be added in Task 3
        throw new NotImplementedException("Will be implemented in Task 3");
    }
}