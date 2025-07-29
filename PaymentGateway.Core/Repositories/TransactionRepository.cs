using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using PaymentGateway.Core.Data;
using PaymentGateway.Core.Entities;
using PaymentGateway.Core.Interfaces;
using System.Linq.Expressions;

namespace PaymentGateway.Core.Repositories;

public interface ITransactionRepository : IRepository<Transaction>
{
    Task<Transaction?> GetByTransactionIdAsync(string transactionId, CancellationToken cancellationToken = default);
    Task<Transaction?> GetByExternalTransactionIdAsync(string externalTransactionId, CancellationToken cancellationToken = default);
    Task<IEnumerable<Transaction>> GetByPaymentIdAsync(Guid paymentId, CancellationToken cancellationToken = default);
    Task<IEnumerable<Transaction>> GetByTypeAsync(TransactionType type, CancellationToken cancellationToken = default);
    Task<IEnumerable<Transaction>> GetByStatusAsync(TransactionStatus status, CancellationToken cancellationToken = default);
    Task<IEnumerable<Transaction>> GetPendingTransactionsAsync(CancellationToken cancellationToken = default);
    Task<IEnumerable<Transaction>> GetFailedTransactionsAsync(CancellationToken cancellationToken = default);
    Task<IEnumerable<Transaction>> GetTransactionsRequiringRetryAsync(CancellationToken cancellationToken = default);
    Task<IEnumerable<Transaction>> GetExpiredTransactionsAsync(CancellationToken cancellationToken = default);
    Task<IEnumerable<Transaction>> GetTransactionsByDateRangeAsync(DateTime startDate, DateTime endDate, CancellationToken cancellationToken = default);
    Task<IEnumerable<Transaction>> GetHighValueTransactionsAsync(decimal threshold, CancellationToken cancellationToken = default);
    Task<IEnumerable<Transaction>> GetSuspiciousTransactionsAsync(CancellationToken cancellationToken = default);
    Task<(decimal TotalAmount, int Count)> GetTransactionStatsByTypeAsync(TransactionType type, DateTime? startDate = null, DateTime? endDate = null, CancellationToken cancellationToken = default);
    Task<decimal> GetTotalProcessingVolumeAsync(DateTime? startDate = null, DateTime? endDate = null, CancellationToken cancellationToken = default);
    Task<bool> IsTransactionIdUniqueAsync(string transactionId, Guid? excludeTransactionId = null, CancellationToken cancellationToken = default);
}

public class TransactionRepository : Repository<Transaction>, ITransactionRepository
{
    public TransactionRepository(
        PaymentGatewayDbContext context, 
        ILogger<TransactionRepository> logger,
        IMemoryCache cache) 
        : base(context, logger, cache)
    {
    }

    public async Task<Transaction?> GetByTransactionIdAsync(string transactionId, CancellationToken cancellationToken = default)
    {
        try
        {
            return await _dbSet
                .Include(t => t.Payment)
                    .ThenInclude(p => p!.Team)
                .Include(t => t.Payment)
                    .ThenInclude(p => p!.Customer)
                .FirstOrDefaultAsync(t => t.TransactionId == transactionId, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting transaction by TransactionId {TransactionId}", transactionId);
            throw;
        }
    }

    public async Task<Transaction?> GetByExternalTransactionIdAsync(string externalTransactionId, CancellationToken cancellationToken = default)
    {
        try
        {
            return await _dbSet
                .Include(t => t.Payment)
                .FirstOrDefaultAsync(t => t.ExternalTransactionId == externalTransactionId, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting transaction by ExternalTransactionId {ExternalTransactionId}", externalTransactionId);
            throw;
        }
    }

    public async Task<IEnumerable<Transaction>> GetByPaymentIdAsync(Guid paymentId, CancellationToken cancellationToken = default)
    {
        try
        {
            return await _dbSet
                .Where(t => t.Payment.Id == paymentId)
                .Include(t => t.Payment)
                .OrderByDescending(t => t.CreatedAt)
                .ToListAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting transactions by PaymentId {PaymentId}", paymentId);
            throw;
        }
    }

    public async Task<IEnumerable<Transaction>> GetByTypeAsync(TransactionType type, CancellationToken cancellationToken = default)
    {
        try
        {
            return await _dbSet
                .Where(t => t.Type == type)
                .Include(t => t.Payment)
                .OrderByDescending(t => t.CreatedAt)
                .ToListAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting transactions by Type {Type}", type);
            throw;
        }
    }

    public async Task<IEnumerable<Transaction>> GetByStatusAsync(TransactionStatus status, CancellationToken cancellationToken = default)
    {
        try
        {
            return await _dbSet
                .Where(t => t.Status == status)
                .Include(t => t.Payment)
                .OrderByDescending(t => t.CreatedAt)
                .ToListAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting transactions by Status {Status}", status);
            throw;
        }
    }

    public async Task<IEnumerable<Transaction>> GetPendingTransactionsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var pendingStatuses = new[]
            {
                TransactionStatus.PENDING,
                TransactionStatus.PROCESSING
            };

            return await _dbSet
                .Where(t => pendingStatuses.Contains(t.Status))
                .Include(t => t.Payment)
                .OrderBy(t => t.CreatedAt)
                .ToListAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting pending transactions");
            throw;
        }
    }

    public async Task<IEnumerable<Transaction>> GetFailedTransactionsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var failedStatuses = new[]
            {
                TransactionStatus.FAILED,
                TransactionStatus.DECLINED,
                TransactionStatus.EXPIRED
            };

            return await _dbSet
                .Where(t => failedStatuses.Contains(t.Status))
                .Include(t => t.Payment)
                .OrderByDescending(t => t.CreatedAt)
                .ToListAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting failed transactions");
            throw;
        }
    }

    public async Task<IEnumerable<Transaction>> GetTransactionsRequiringRetryAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await _dbSet
                .Where(t => t.Status == TransactionStatus.FAILED)
                .Where(t => t.AttemptNumber < t.MaxRetryAttempts)
                .Where(t => !t.NextRetryAt.HasValue || t.NextRetryAt <= DateTime.UtcNow)
                .Include(t => t.Payment)
                .OrderBy(t => t.NextRetryAt ?? t.CreatedAt)
                .ToListAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting transactions requiring retry");
            throw;
        }
    }

    public async Task<IEnumerable<Transaction>> GetExpiredTransactionsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var now = DateTime.UtcNow;
            var expirableStatuses = new[]
            {
                TransactionStatus.PENDING,
                TransactionStatus.PROCESSING
            };

            return await _dbSet
                .Where(t => expirableStatuses.Contains(t.Status))
                .Where(t => t.ExpiresAt.HasValue && t.ExpiresAt <= now)
                .Include(t => t.Payment)
                .ToListAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting expired transactions");
            throw;
        }
    }

    public async Task<IEnumerable<Transaction>> GetTransactionsByDateRangeAsync(DateTime startDate, DateTime endDate, CancellationToken cancellationToken = default)
    {
        try
        {
            return await _dbSet
                .Where(t => t.CreatedAt >= startDate && t.CreatedAt <= endDate)
                .Include(t => t.Payment)
                    .ThenInclude(p => p!.Team)
                .OrderByDescending(t => t.CreatedAt)
                .ToListAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting transactions by date range {StartDate} to {EndDate}", startDate, endDate);
            throw;
        }
    }

    public async Task<IEnumerable<Transaction>> GetHighValueTransactionsAsync(decimal threshold, CancellationToken cancellationToken = default)
    {
        try
        {
            return await _dbSet
                .Where(t => t.Amount >= threshold)
                .Include(t => t.Payment)
                    .ThenInclude(p => p!.Team)
                .Include(t => t.Payment)
                    .ThenInclude(p => p!.Customer)
                .OrderByDescending(t => t.Amount)
                .ThenByDescending(t => t.CreatedAt)
                .ToListAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting high value transactions (threshold: {Threshold})", threshold);
            throw;
        }
    }

    public async Task<IEnumerable<Transaction>> GetSuspiciousTransactionsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await _dbSet
                .Where(t => t.FraudScore >= 75 || t.RequiresManualReview())
                .Include(t => t.Payment)
                    .ThenInclude(p => p!.Team)
                .Include(t => t.Payment)
                    .ThenInclude(p => p!.Customer)
                .OrderByDescending(t => t.FraudScore)
                .ThenByDescending(t => t.CreatedAt)
                .ToListAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting suspicious transactions");
            throw;
        }
    }

    public async Task<(decimal TotalAmount, int Count)> GetTransactionStatsByTypeAsync(TransactionType type, DateTime? startDate = null, DateTime? endDate = null, CancellationToken cancellationToken = default)
    {
        try
        {
            var query = _dbSet.Where(t => t.Type == type);

            if (startDate.HasValue)
                query = query.Where(t => t.CreatedAt >= startDate.Value);

            if (endDate.HasValue)
                query = query.Where(t => t.CreatedAt <= endDate.Value);

            var successfulStatuses = new[]
            {
                TransactionStatus.COMPLETED,
                TransactionStatus.PARTIALLY_COMPLETED
            };

            var stats = await query
                .Where(t => successfulStatuses.Contains(t.Status))
                .GroupBy(t => 1)
                .Select(g => new
                {
                    TotalAmount = g.Sum(t => t.Amount),
                    Count = g.Count()
                })
                .FirstOrDefaultAsync(cancellationToken);

            return (stats?.TotalAmount ?? 0, stats?.Count ?? 0);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting transaction stats by Type {Type}", type);
            throw;
        }
    }

    public async Task<decimal> GetTotalProcessingVolumeAsync(DateTime? startDate = null, DateTime? endDate = null, CancellationToken cancellationToken = default)
    {
        try
        {
            var query = _dbSet.AsQueryable();

            if (startDate.HasValue)
                query = query.Where(t => t.CreatedAt >= startDate.Value);

            if (endDate.HasValue)
                query = query.Where(t => t.CreatedAt <= endDate.Value);

            var successfulStatuses = new[]
            {
                TransactionStatus.COMPLETED,
                TransactionStatus.PARTIALLY_COMPLETED
            };

            return await query
                .Where(t => successfulStatuses.Contains(t.Status))
                .SumAsync(t => t.Amount, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting total processing volume");
            throw;
        }
    }

    public async Task<bool> IsTransactionIdUniqueAsync(string transactionId, Guid? excludeTransactionId = null, CancellationToken cancellationToken = default)
    {
        try
        {
            var query = _dbSet.Where(t => t.TransactionId == transactionId);

            if (excludeTransactionId.HasValue)
                query = query.Where(t => t.Id != excludeTransactionId.Value);

            return !await query.AnyAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking TransactionId uniqueness for {TransactionId}", transactionId);
            throw;
        }
    }

    // Override to include related entities by default
    public override async Task<Transaction?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        try
        {
            return await _dbSet
                .Include(t => t.Payment)
                    .ThenInclude(p => p!.Team)
                .Include(t => t.Payment)
                    .ThenInclude(p => p!.Customer)
                .FirstOrDefaultAsync(t => t.Id == id && !t.IsDeleted, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting transaction by ID {Id}", id);
            throw;
        }
    }
}