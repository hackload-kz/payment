using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using PaymentGateway.Core.Data;
using PaymentGateway.Core.Entities;
using PaymentGateway.Core.Interfaces;
using PaymentGateway.Core.Services;

namespace PaymentGateway.Core.Repositories;

public interface IPaymentStateTransitionRepository : IRepository<PaymentStateTransition>
{
    Task<PaymentStateTransition?> GetByTransitionIdAsync(string transitionId, CancellationToken cancellationToken = default);
    Task<IEnumerable<PaymentStateTransition>> GetByPaymentIdAsync(Guid paymentId, CancellationToken cancellationToken = default);
    Task<IEnumerable<PaymentStateTransition>> GetByUserIdAsync(string userId, CancellationToken cancellationToken = default);
    Task<IEnumerable<PaymentStateTransition>> GetByDateRangeAsync(DateTime startDate, DateTime endDate, CancellationToken cancellationToken = default);
    Task<IEnumerable<PaymentStateTransition>> GetFailedTransitionsAsync(CancellationToken cancellationToken = default);
    Task<IEnumerable<PaymentStateTransition>> GetRollbackTransitionsAsync(CancellationToken cancellationToken = default);
    Task<Dictionary<PaymentStatus, int>> GetTransitionStatisticsAsync(DateTime? startDate = null, DateTime? endDate = null, CancellationToken cancellationToken = default);
    Task<IEnumerable<PaymentStateTransition>> GetCriticalTransitionsAsync(CancellationToken cancellationToken = default);
}

public class PaymentStateTransitionRepository : Repository<PaymentStateTransition>, IPaymentStateTransitionRepository
{
    public PaymentStateTransitionRepository(
        PaymentGatewayDbContext context,
        ILogger<PaymentStateTransitionRepository> logger,
        IMemoryCache cache)
        : base(context, logger, cache)
    {
    }

    public async Task<PaymentStateTransition?> GetByTransitionIdAsync(string transitionId, CancellationToken cancellationToken = default)
    {
        try
        {
            return await _dbSet
                .Include(t => t.Payment)
                .FirstOrDefaultAsync(t => t.TransitionId == transitionId, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting state transition by TransitionId {TransitionId}", transitionId);
            throw;
        }
    }

    public async Task<IEnumerable<PaymentStateTransition>> GetByPaymentIdAsync(Guid paymentId, CancellationToken cancellationToken = default)
    {
        try
        {
            return await _dbSet
                .Where(t => t.PaymentId == paymentId)
                .OrderByDescending(t => t.TransitionedAt)
                .ToListAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting state transitions by PaymentId {PaymentId}", paymentId);
            throw;
        }
    }

    public async Task<IEnumerable<PaymentStateTransition>> GetByUserIdAsync(string userId, CancellationToken cancellationToken = default)
    {
        try
        {
            return await _dbSet
                .Where(t => t.UserId == userId)
                .Include(t => t.Payment)
                .OrderByDescending(t => t.TransitionedAt)
                .ToListAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting state transitions by UserId {UserId}", userId);
            throw;
        }
    }

    public async Task<IEnumerable<PaymentStateTransition>> GetByDateRangeAsync(DateTime startDate, DateTime endDate, CancellationToken cancellationToken = default)
    {
        try
        {
            return await _dbSet
                .Where(t => t.TransitionedAt >= startDate && t.TransitionedAt <= endDate)
                .Include(t => t.Payment)
                .OrderByDescending(t => t.TransitionedAt)
                .ToListAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting state transitions by date range {StartDate} to {EndDate}", startDate, endDate);
            throw;
        }
    }

    public async Task<IEnumerable<PaymentStateTransition>> GetFailedTransitionsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var failedToStates = new[]
            {
                PaymentStatus.AUTH_FAIL,
                PaymentStatus.REJECTED,
                PaymentStatus.EXPIRED,
                PaymentStatus.DEADLINE_EXPIRED
            };

            return await _dbSet
                .Where(t => failedToStates.Contains(t.ToStatus))
                .Include(t => t.Payment)
                .OrderByDescending(t => t.TransitionedAt)
                .ToListAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting failed state transitions");
            throw;
        }
    }

    public async Task<IEnumerable<PaymentStateTransition>> GetRollbackTransitionsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await _dbSet
                .Where(t => t.IsRollback)
                .Include(t => t.Payment)
                .OrderByDescending(t => t.TransitionedAt)
                .ToListAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting rollback state transitions");
            throw;
        }
    }

    public async Task<Dictionary<PaymentStatus, int>> GetTransitionStatisticsAsync(DateTime? startDate = null, DateTime? endDate = null, CancellationToken cancellationToken = default)
    {
        try
        {
            var query = _dbSet.AsQueryable();

            if (startDate.HasValue)
                query = query.Where(t => t.TransitionedAt >= startDate.Value);

            if (endDate.HasValue)
                query = query.Where(t => t.TransitionedAt <= endDate.Value);

            var statistics = await query
                .GroupBy(t => t.ToStatus)
                .Select(g => new { Status = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.Status, x => x.Count, cancellationToken);

            return statistics;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting state transition statistics");
            throw;
        }
    }

    public async Task<IEnumerable<PaymentStateTransition>> GetCriticalTransitionsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var criticalStates = new[]
            {
                PaymentStatus.AUTHORIZING,
                PaymentStatus.CONFIRMING,
                PaymentStatus.REFUNDING,
                PaymentStatus.REVERSING,
                PaymentStatus.CANCELLING
            };

            return await _dbSet
                .Where(t => criticalStates.Contains(t.ToStatus) || criticalStates.Contains(t.FromStatus))
                .Include(t => t.Payment)
                .OrderByDescending(t => t.TransitionedAt)
                .ToListAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting critical state transitions");
            throw;
        }
    }

    // Override to include related entities by default
    public override async Task<PaymentStateTransition?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        try
        {
            return await _dbSet
                .Include(t => t.Payment)
                .FirstOrDefaultAsync(t => t.Id == id && !t.IsDeleted, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting state transition by ID {Id}", id);
            throw;
        }
    }
}