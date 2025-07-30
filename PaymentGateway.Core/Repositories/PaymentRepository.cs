using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using PaymentGateway.Core.Data;
using PaymentGateway.Core.Entities;
using PaymentGateway.Core.Enums;
using PaymentGateway.Core.Interfaces;
using System.Linq.Expressions;

namespace PaymentGateway.Core.Repositories;

public interface IPaymentRepository : IRepository<Payment>
{
    Task<Payment?> GetByPaymentIdAsync(string paymentId, CancellationToken cancellationToken = default);
    Task<Payment?> GetByOrderIdAsync(string orderId, Guid teamId, CancellationToken cancellationToken = default);
    Task<IEnumerable<Payment>> GetByTeamIdAsync(Guid teamId, CancellationToken cancellationToken = default);
    Task<IEnumerable<Payment>> GetByCustomerIdAsync(Guid? customerId, CancellationToken cancellationToken = default);
    Task<IEnumerable<Payment>> GetByStatusAsync(PaymentStatus status, CancellationToken cancellationToken = default);
    Task<IEnumerable<Payment>> GetPaymentsByStatusAsync(PaymentStatus status, CancellationToken cancellationToken = default);
    Task<IEnumerable<Payment>> GetPaymentsRequiringProcessingAsync(CancellationToken cancellationToken = default);
    Task<IEnumerable<Payment>> GetExpiredPaymentsAsync(CancellationToken cancellationToken = default);
    Task<IEnumerable<Payment>> GetExpiredPaymentsAsync(DateTime cutoffTime, CancellationToken cancellationToken = default);
    Task<IEnumerable<Payment>> GetActivePaymentsByTeamAsync(Guid teamId, CancellationToken cancellationToken = default);
    Task<decimal> GetTotalAmountByTeamAndDateRangeAsync(Guid teamId, DateTime startDate, DateTime endDate, CancellationToken cancellationToken = default);
    Task<(decimal TotalAmount, int Count)> GetPaymentStatsByTeamAsync(Guid teamId, DateTime? startDate = null, DateTime? endDate = null, CancellationToken cancellationToken = default);
    Task<IEnumerable<Payment>> GetRecentPaymentsByTeamAsync(Guid teamId, int count = 10, CancellationToken cancellationToken = default);
    Task<bool> IsOrderIdUniqueForTeamAsync(Guid teamId, string orderId, Guid? excludePaymentId = null, CancellationToken cancellationToken = default);
    
    // Add missing methods used by services
    Task<Payment> CreateAsync(Payment payment, CancellationToken cancellationToken = default);
    Task<Payment> UpdateAsync(Payment payment, CancellationToken cancellationToken = default);
    Task<int> GetPaymentCountByStatusAsync(PaymentStatus status, CancellationToken cancellationToken = default);
    
    // Methods using teamSlug for validation services
    Task<Payment?> GetByOrderIdAsync(string teamSlug, string orderId, CancellationToken cancellationToken = default);
    Task<int> GetActivePaymentCountAsync(CancellationToken cancellationToken = default);
    Task<int> GetActivePaymentCountAsync(Guid teamId, CancellationToken cancellationToken = default);
    Task<decimal> GetTodayPaymentsTotalAsync(Guid teamId, CancellationToken cancellationToken = default);
    Task<int> GetTodayTransactionCountAsync(Guid teamId, CancellationToken cancellationToken = default);
    Task<int> GetProcessingPaymentCountAsync(Guid teamId, CancellationToken cancellationToken = default);
}

public class PaymentRepository : Repository<Payment>, IPaymentRepository
{
    public PaymentRepository(
        PaymentGatewayDbContext context, 
        ILogger<PaymentRepository> logger,
        IMemoryCache cache) 
        : base(context, logger, cache)
    {
    }

    public async Task<Payment?> GetByPaymentIdAsync(string paymentId, CancellationToken cancellationToken = default)
    {
        try
        {
            return await _dbSet
                .Include(p => p.Team)
                .Include(p => p.Customer)
                .Include(p => p.Transactions)
                .FirstOrDefaultAsync(p => p.PaymentId == paymentId, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting payment by PaymentId {PaymentId}", paymentId);
            throw;
        }
    }

    public async Task<Payment?> GetByOrderIdAsync(string orderId, Guid teamId, CancellationToken cancellationToken = default)
    {
        try
        {
            return await _dbSet
                .Include(p => p.Team)
                .Include(p => p.Customer)
                .FirstOrDefaultAsync(p => p.TeamId == teamId && p.OrderId == orderId, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting payment by TeamId {TeamId} and OrderId {OrderId}", teamId, orderId);
            throw;
        }
    }

    public async Task<IEnumerable<Payment>> GetByTeamIdAsync(Guid teamId, CancellationToken cancellationToken = default)
    {
        try
        {
            return await _dbSet
                .Where(p => p.TeamId == teamId)
                .OrderByDescending(p => p.CreatedAt)
                .ToListAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting payments by TeamId {TeamId}", teamId);
            throw;
        }
    }

    public async Task<IEnumerable<Payment>> GetByCustomerIdAsync(Guid? customerId, CancellationToken cancellationToken = default)
    {
        try
        {
            return await _dbSet
                .Where(p => p.CustomerId == customerId)
                .OrderByDescending(p => p.CreatedAt)
                .ToListAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting payments by CustomerId {CustomerId}", customerId);
            throw;
        }
    }

    public async Task<IEnumerable<Payment>> GetByStatusAsync(PaymentStatus status, CancellationToken cancellationToken = default)
    {
        try
        {
            return await _dbSet
                .Where(p => p.Status == status)
                .OrderByDescending(p => p.CreatedAt)
                .ToListAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting payments by Status {Status}", status);
            throw;
        }
    }

    public async Task<IEnumerable<Payment>> GetPaymentsByStatusAsync(PaymentStatus status, CancellationToken cancellationToken = default)
    {
        // Alias for GetByStatusAsync to maintain compatibility
        return await GetByStatusAsync(status, cancellationToken);
    }

    public async Task<IEnumerable<Payment>> GetPaymentsRequiringProcessingAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var processingStatuses = new[]
            {
                PaymentStatus.NEW,
                PaymentStatus.FORM_SHOWED,
                PaymentStatus.AUTHORIZING,
                PaymentStatus.CONFIRMING
            };

            return await _dbSet
                .Where(p => processingStatuses.Contains(p.Status))
                .Where(p => !p.ExpiresAt.HasValue || p.ExpiresAt > DateTime.UtcNow)
                .OrderBy(p => p.CreatedAt)
                .ToListAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting payments requiring processing");
            throw;
        }
    }

    public async Task<IEnumerable<Payment>> GetExpiredPaymentsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var now = DateTime.UtcNow;
            var expirableStatuses = new[]
            {
                PaymentStatus.INIT,
                PaymentStatus.NEW,
                PaymentStatus.FORM_SHOWED,
                PaymentStatus.AUTHORIZING,
                PaymentStatus.AUTHORIZED
            };

            return await _dbSet
                .Where(p => expirableStatuses.Contains(p.Status))
                .Where(p => p.ExpiresAt.HasValue && p.ExpiresAt <= now)
                .ToListAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting expired payments");
            throw;
        }
    }

    public async Task<decimal> GetTotalAmountByTeamAndDateRangeAsync(Guid teamId, DateTime startDate, DateTime endDate, CancellationToken cancellationToken = default)
    {
        try
        {
            var successfulStatuses = new[]
            {
                PaymentStatus.CONFIRMED,
                PaymentStatus.REFUNDED,
                PaymentStatus.PARTIALLY_REFUNDED
            };

            return await _dbSet
                .Where(p => p.TeamId == teamId)
                .Where(p => successfulStatuses.Contains(p.Status))
                .Where(p => p.CreatedAt >= startDate && p.CreatedAt <= endDate)
                .SumAsync(p => p.Amount, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting total amount for TeamId {TeamId} between {StartDate} and {EndDate}", 
                teamId, startDate, endDate);
            throw;
        }
    }

    public async Task<(decimal TotalAmount, int Count)> GetPaymentStatsByTeamAsync(Guid teamId, DateTime? startDate = null, DateTime? endDate = null, CancellationToken cancellationToken = default)
    {
        try
        {
            var query = _dbSet.Where(p => p.TeamId == teamId);

            if (startDate.HasValue)
                query = query.Where(p => p.CreatedAt >= startDate.Value);

            if (endDate.HasValue)
                query = query.Where(p => p.CreatedAt <= endDate.Value);

            var successfulStatuses = new[]
            {
                PaymentStatus.CONFIRMED,
                PaymentStatus.REFUNDED,
                PaymentStatus.PARTIALLY_REFUNDED
            };

            var stats = await query
                .Where(p => successfulStatuses.Contains(p.Status))
                .GroupBy(p => 1)
                .Select(g => new
                {
                    TotalAmount = g.Sum(p => p.Amount),
                    Count = g.Count()
                })
                .FirstOrDefaultAsync(cancellationToken);

            return (stats?.TotalAmount ?? 0, stats?.Count ?? 0);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting payment stats for TeamId {TeamId}", teamId);
            throw;
        }
    }

    public async Task<IEnumerable<Payment>> GetRecentPaymentsByTeamAsync(Guid teamId, int count = 10, CancellationToken cancellationToken = default)
    {
        try
        {
            return await _dbSet
                .Where(p => p.TeamId == teamId)
                .OrderByDescending(p => p.CreatedAt)
                .Take(count)
                .Include(p => p.Customer)
                .ToListAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting recent payments for TeamId {TeamId}", teamId);
            throw;
        }
    }

    public async Task<bool> IsOrderIdUniqueForTeamAsync(Guid teamId, string orderId, Guid? excludePaymentId = null, CancellationToken cancellationToken = default)
    {
        try
        {
            var query = _dbSet.Where(p => p.TeamId == teamId && p.OrderId == orderId);

            if (excludePaymentId.HasValue)
                query = query.Where(p => p.Id != excludePaymentId.Value);

            return !await query.AnyAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking OrderId uniqueness for TeamId {TeamId} and OrderId {OrderId}", teamId, orderId);
            throw;
        }
    }

    public async Task<IEnumerable<Payment>> GetExpiredPaymentsAsync(DateTime cutoffTime, CancellationToken cancellationToken = default)
    {
        try
        {
            return await _dbSet
                .Where(p => p.ExpiresAt.HasValue && p.ExpiresAt.Value <= cutoffTime)
                .Where(p => p.Status == PaymentStatus.NEW || p.Status == PaymentStatus.FORM_SHOWED)
                .ToListAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting expired payments before {CutoffTime}", cutoffTime);
            throw;
        }
    }

    public async Task<IEnumerable<Payment>> GetActivePaymentsByTeamAsync(Guid teamId, CancellationToken cancellationToken = default)
    {
        try
        {
            var activeStatuses = new[]
            {
                PaymentStatus.NEW,
                PaymentStatus.FORM_SHOWED,
                PaymentStatus.AUTHORIZED,
                PaymentStatus.PROCESSING
            };
            
            return await _dbSet
                .Where(p => p.TeamId == teamId && activeStatuses.Contains(p.Status))
                .OrderByDescending(p => p.CreatedAt)
                .ToListAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting active payments for TeamId {TeamId}", teamId);
            throw;
        }
    }

    public async Task<Payment> CreateAsync(Payment payment, CancellationToken cancellationToken = default)
    {
        try
        {
            payment.MarkAsCreated();
            await AddAsync(payment, cancellationToken);
            await _context.SaveChangesAsync(cancellationToken);
            return payment;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating payment with PaymentId {PaymentId}", payment.PaymentId);
            throw;
        }
    }

    public async Task<Payment> UpdateAsync(Payment payment, CancellationToken cancellationToken = default)
    {
        try
        {
            payment.MarkAsUpdated();
            Update(payment);
            await _context.SaveChangesAsync(cancellationToken);
            return payment;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating payment with PaymentId {PaymentId}", payment.PaymentId);
            throw;
        }
    }

    public async Task<int> GetPaymentCountByStatusAsync(PaymentStatus status, CancellationToken cancellationToken = default)
    {
        try
        {
            return await _dbSet
                .Where(p => p.Status == status && !p.IsDeleted)
                .CountAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting payment count for status {Status}", status);
            throw;
        }
    }

    // Override to include related entities by default
    public override async Task<Payment?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        try
        {
            return await _dbSet
                .Include(p => p.Team)
                .Include(p => p.Customer)
                .Include(p => p.Transactions)
                .FirstOrDefaultAsync(p => p.Id == id && !p.IsDeleted, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting payment by ID {Id}", id);
            throw;
        }
    }

    public async Task<Payment?> GetByOrderIdAsync(string teamSlug, string orderId, CancellationToken cancellationToken = default)
    {
        try
        {
            return await _dbSet
                .Include(p => p.Team)
                .Include(p => p.Customer)
                .FirstOrDefaultAsync(p => p.Team.TeamSlug == teamSlug && p.OrderId == orderId, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting payment by TeamSlug {TeamSlug} and OrderId {OrderId}", teamSlug, orderId);
            throw;
        }
    }

    public async Task<int> GetActivePaymentCountAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var activeStatuses = new[]
            {
                PaymentStatus.NEW,
                PaymentStatus.FORM_SHOWED,
                PaymentStatus.AUTHORIZED,
                PaymentStatus.PROCESSING
            };
            
            return await _dbSet
                .Where(p => activeStatuses.Contains(p.Status) && !p.IsDeleted)
                .CountAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting active payment count");
            throw;
        }
    }
    
    public async Task<int> GetActivePaymentCountAsync(Guid teamId, CancellationToken cancellationToken = default)
    {
        try
        {
            var activeStatuses = new[]
            {
                PaymentStatus.NEW,
                PaymentStatus.FORM_SHOWED,
                PaymentStatus.AUTHORIZED,
                PaymentStatus.PROCESSING
            };
            
            return await _dbSet
                .Where(p => p.TeamId == teamId && activeStatuses.Contains(p.Status) && !p.IsDeleted)
                .CountAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting active payment count for team {TeamId}", teamId);
            throw;
        }
    }

    public async Task<decimal> GetTodayPaymentsTotalAsync(Guid teamId, CancellationToken cancellationToken = default)
    {
        try
        {
            var today = DateTime.UtcNow.Date;
            var tomorrow = today.AddDays(1);

            var successfulStatuses = new[]
            {
                PaymentStatus.CONFIRMED,
                PaymentStatus.REFUNDED,
                PaymentStatus.PARTIALLY_REFUNDED
            };

            return await _dbSet
                .Where(p => p.TeamId == teamId)
                .Where(p => successfulStatuses.Contains(p.Status))
                .Where(p => p.CreatedAt >= today && p.CreatedAt < tomorrow)
                .SumAsync(p => p.Amount, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting today's payments total for TeamId {TeamId}", teamId);
            throw;
        }
    }

    public async Task<int> GetTodayTransactionCountAsync(Guid teamId, CancellationToken cancellationToken = default)
    {
        try
        {
            var today = DateTime.UtcNow.Date;
            var tomorrow = today.AddDays(1);

            return await _dbSet
                .Where(p => p.TeamId == teamId)
                .Where(p => p.CreatedAt >= today && p.CreatedAt < tomorrow)
                .CountAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting today's transaction count for TeamId {TeamId}", teamId);
            throw;
        }
    }

    public async Task<int> GetProcessingPaymentCountAsync(Guid teamId, CancellationToken cancellationToken = default)
    {
        try
        {
            var processingStatuses = new[]
            {
                PaymentStatus.PROCESSING,
                PaymentStatus.AUTHORIZING,
                PaymentStatus.CONFIRMING
            };

            return await _dbSet
                .Where(p => p.TeamId == teamId)
                .Where(p => processingStatuses.Contains(p.Status) && !p.IsDeleted)
                .CountAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting processing payment count for TeamId {TeamId}", teamId);
            throw;
        }
    }
}