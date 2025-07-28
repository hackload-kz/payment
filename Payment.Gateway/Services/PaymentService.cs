using Payment.Gateway.Models;

namespace Payment.Gateway.Services;

public class PaymentService : IPaymentService
{
    public Task<PaymentEntity> InitializePaymentAsync(object request)
    {
        // Implementation will be added in Task 4
        throw new NotImplementedException("Will be implemented in Task 4");
    }

    public Task<PaymentEntity> ConfirmPaymentAsync(string paymentId, object request)
    {
        // Implementation will be added in Task 5
        throw new NotImplementedException("Will be implemented in Task 5");
    }

    public Task<PaymentEntity> CancelPaymentAsync(string paymentId, object request)
    {
        // Implementation will be added in Task 7
        throw new NotImplementedException("Will be implemented in Task 7");
    }

    public Task<PaymentEntity[]> CheckOrderAsync(string orderId, string terminalKey)
    {
        // Implementation will be added in Task 6
        throw new NotImplementedException("Will be implemented in Task 6");
    }
}