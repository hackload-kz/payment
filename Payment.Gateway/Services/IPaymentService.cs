using Payment.Gateway.DTOs;
using Payment.Gateway.Models;

namespace Payment.Gateway.Services;

public interface IPaymentService
{
    Task<InitPaymentResponse> InitializePaymentAsync(InitPaymentRequest request);
    Task<ConfirmPaymentResponse> ConfirmPaymentAsync(ConfirmPaymentRequest request);
    Task<PaymentEntity> CancelPaymentAsync(string paymentId, object request);
    Task<PaymentEntity[]> CheckOrderAsync(string orderId, string terminalKey);
}