using Payment.Gateway.Models;

namespace Payment.Gateway.Services;

public interface IPaymentRepository
{
    Task<PaymentEntity?> GetByIdAsync(string paymentId);
    Task<PaymentEntity[]> GetByOrderIdAsync(string orderId, string terminalKey);
    Task<PaymentEntity> CreateAsync(PaymentEntity payment);
    Task<PaymentEntity> UpdateAsync(PaymentEntity payment);
    Task<bool> DeleteAsync(string paymentId);
    Task AddStatusHistoryAsync(string paymentId, PaymentStatus status, string? errorCode = null, string? message = null);
}