using Payment.Gateway.Models;

namespace Payment.Gateway.Services;

public interface IPaymentService
{
    Task<PaymentEntity> InitializePaymentAsync(object request);
    Task<PaymentEntity> ConfirmPaymentAsync(string paymentId, object request);
    Task<PaymentEntity> CancelPaymentAsync(string paymentId, object request);
    Task<PaymentEntity[]> CheckOrderAsync(string orderId, string terminalKey);
}