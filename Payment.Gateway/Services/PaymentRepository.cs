using Microsoft.EntityFrameworkCore;
using Payment.Gateway.Infrastructure;
using Payment.Gateway.Models;

namespace Payment.Gateway.Services;

public class PaymentRepository : IPaymentRepository
{
    private readonly PaymentDbContext _context;

    public PaymentRepository(PaymentDbContext context)
    {
        _context = context;
    }

    public async Task<PaymentEntity?> GetByIdAsync(string paymentId)
    {
        return await _context.Payments.FirstOrDefaultAsync(p => p.PaymentId == paymentId);
    }

    public async Task<PaymentEntity[]> GetByOrderIdAsync(string orderId, string terminalKey)
    {
        return await _context.Payments
            .Where(p => p.OrderId == orderId && p.TerminalKey == terminalKey)
            .OrderBy(p => p.CreatedDate)
            .ToArrayAsync();
    }

    public async Task<PaymentEntity> CreateAsync(PaymentEntity payment)
    {
        _context.Payments.Add(payment);
        await _context.SaveChangesAsync();
        return payment;
    }

    public async Task<PaymentEntity> UpdateAsync(PaymentEntity payment)
    {
        payment.UpdatedDate = DateTime.UtcNow;
        _context.Payments.Update(payment);
        await _context.SaveChangesAsync();
        return payment;
    }

    public async Task<bool> DeleteAsync(string paymentId)
    {
        var payment = await GetByIdAsync(paymentId);
        if (payment == null) return false;

        _context.Payments.Remove(payment);
        await _context.SaveChangesAsync();
        return true;
    }

    public async Task AddStatusHistoryAsync(string paymentId, PaymentStatus status, string? errorCode = null, string? message = null)
    {
        var history = new PaymentStatusHistory
        {
            PaymentId = paymentId,
            Status = status,
            ErrorCode = errorCode,
            Message = message,
            Timestamp = DateTime.UtcNow
        };

        _context.PaymentStatusHistory.Add(history);
        await _context.SaveChangesAsync();
    }
}