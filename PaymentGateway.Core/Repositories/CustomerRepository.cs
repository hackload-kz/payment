using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using PaymentGateway.Core.Data;
using PaymentGateway.Core.Entities;
using PaymentGateway.Core.Interfaces;
using System.Linq.Expressions;

namespace PaymentGateway.Core.Repositories;

public interface ICustomerRepository : IRepository<Customer>
{
    Task<Customer?> GetByCustomerIdAsync(string customerId, CancellationToken cancellationToken = default);
    Task<Customer?> GetByEmailAsync(string email, CancellationToken cancellationToken = default);
    Task<Customer?> GetByPhoneAsync(string phone, CancellationToken cancellationToken = default);
    Task<IEnumerable<Customer>> GetByTeamIdAsync(int teamId, CancellationToken cancellationToken = default);
    Task<IEnumerable<Customer>> GetByRiskScoreRangeAsync(int minScore, int maxScore, CancellationToken cancellationToken = default);
    Task<IEnumerable<Customer>> GetHighRiskCustomersAsync(int riskThreshold = 75, CancellationToken cancellationToken = default);
    Task<IEnumerable<Customer>> GetCustomersRequiringKycAsync(CancellationToken cancellationToken = default);
    Task<IEnumerable<Customer>> GetCustomersWithMultiplePaymentMethodsAsync(CancellationToken cancellationToken = default);
    Task<(decimal TotalAmount, int Count)> GetCustomerPaymentStatsAsync(Guid customerId, DateTime? startDate = null, DateTime? endDate = null, CancellationToken cancellationToken = default);
    Task<IEnumerable<Customer>> GetRecentCustomersAsync(int count = 50, CancellationToken cancellationToken = default);
    Task<bool> IsCustomerIdUniqueAsync(string customerId, Guid? excludeCustomerId = null, CancellationToken cancellationToken = default);
    Task<bool> IsEmailUniqueForTeamAsync(int teamId, string email, Guid? excludeCustomerId = null, CancellationToken cancellationToken = default);
}

public class CustomerRepository : Repository<Customer>, ICustomerRepository
{
    public CustomerRepository(
        PaymentGatewayDbContext context, 
        ILogger<CustomerRepository> logger,
        IMemoryCache cache) 
        : base(context, logger, cache)
    {
    }

    public async Task<Customer?> GetByCustomerIdAsync(string customerId, CancellationToken cancellationToken = default)
    {
        try
        {
            return await _dbSet
                .Include(c => c.Team)
                .Include(c => c.Payments)
                .Include(c => c.PaymentMethods)
                .FirstOrDefaultAsync(c => c.CustomerId == customerId, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting customer by CustomerId {CustomerId}", customerId);
            throw;
        }
    }

    public async Task<Customer?> GetByEmailAsync(string email, CancellationToken cancellationToken = default)
    {
        try
        {
            return await _dbSet
                .Include(c => c.Team)
                .Include(c => c.Payments)
                .FirstOrDefaultAsync(c => c.Email == email, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting customer by Email {Email}", email);
            throw;
        }
    }

    public async Task<Customer?> GetByPhoneAsync(string phone, CancellationToken cancellationToken = default)
    {
        try
        {
            return await _dbSet
                .Include(c => c.Team)
                .Include(c => c.Payments)
                .FirstOrDefaultAsync(c => c.Phone == phone, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting customer by Phone {Phone}", phone);
            throw;
        }
    }

    public async Task<IEnumerable<Customer>> GetByTeamIdAsync(int teamId, CancellationToken cancellationToken = default)
    {
        try
        {
            return await _dbSet
                .Where(c => c.TeamId == teamId)
                .Include(c => c.Payments)
                .OrderByDescending(c => c.CreatedAt)
                .ToListAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting customers by TeamId {TeamId}", teamId);
            throw;
        }
    }

    public async Task<IEnumerable<Customer>> GetByRiskScoreRangeAsync(int minScore, int maxScore, CancellationToken cancellationToken = default)
    {
        try
        {
            return await _dbSet
                .Where(c => c.RiskScore >= minScore && c.RiskScore <= maxScore)
                .OrderByDescending(c => c.RiskScore)
                .ThenByDescending(c => c.UpdatedAt)
                .ToListAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting customers by risk score range {MinScore}-{MaxScore}", minScore, maxScore);
            throw;
        }
    }

    public async Task<IEnumerable<Customer>> GetHighRiskCustomersAsync(int riskThreshold = 75, CancellationToken cancellationToken = default)
    {
        try
        {
            return await _dbSet
                .Where(c => c.RiskScore >= riskThreshold)
                .Include(c => c.Team)
                .Include(c => c.Payments)
                .OrderByDescending(c => c.RiskScore)
                .ThenByDescending(c => c.UpdatedAt)
                .ToListAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting high risk customers (threshold: {RiskThreshold})", riskThreshold);
            throw;
        }
    }

    public async Task<IEnumerable<Customer>> GetCustomersRequiringKycAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await _dbSet
                .Where(c => !c.IsKycVerified)
                .Where(c => !c.IsDeleted)
                .OrderBy(c => c.CreatedAt)
                .ToListAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting customers requiring KYC");
            throw;
        }
    }

    public async Task<IEnumerable<Customer>> GetCustomersWithMultiplePaymentMethodsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await _dbSet
                .Include(c => c.PaymentMethods)
                .Where(c => c.PaymentMethods.Count > 1)
                .OrderByDescending(c => c.PaymentMethods.Count)
                .ToListAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting customers with multiple payment methods");
            throw;
        }
    }

    public async Task<(decimal TotalAmount, int Count)> GetCustomerPaymentStatsAsync(Guid customerId, DateTime? startDate = null, DateTime? endDate = null, CancellationToken cancellationToken = default)
    {
        try
        {
            // First find the Customer's TeamId since Payment.CustomerId is int?, not Guid
            var customer = await _dbSet.FirstOrDefaultAsync(c => c.Id == customerId, cancellationToken);
            if (customer == null)
                return (0, 0);
            
            var query = _context.Set<Payment>().Where(p => p.CustomerId == customer.TeamId);

            if (startDate.HasValue)
                query = query.Where(p => p.CreatedAt >= startDate.Value);

            if (endDate.HasValue)
                query = query.Where(p => p.CreatedAt <= endDate.Value);

            var successfulStatuses = new[]
            {
                PaymentStatus.CONFIRMED,
                PaymentStatus.REFUNDED,
                PaymentStatus.PARTIAL_REFUNDED
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
            _logger.LogError(ex, "Error getting payment stats for CustomerId {CustomerId}", customerId);
            throw;
        }
    }

    public async Task<IEnumerable<Customer>> GetRecentCustomersAsync(int count = 50, CancellationToken cancellationToken = default)
    {
        try
        {
            return await _dbSet
                .Include(c => c.Team)
                .OrderByDescending(c => c.CreatedAt)
                .Take(count)
                .ToListAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting recent customers (count: {Count})", count);
            throw;
        }
    }

    public async Task<bool> IsCustomerIdUniqueAsync(string customerId, Guid? excludeCustomerId = null, CancellationToken cancellationToken = default)
    {
        try
        {
            var query = _dbSet.Where(c => c.CustomerId == customerId);

            if (excludeCustomerId.HasValue)
                query = query.Where(c => c.Id != excludeCustomerId.Value);

            return !await query.AnyAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking CustomerId uniqueness for {CustomerId}", customerId);
            throw;
        }
    }

    public async Task<bool> IsEmailUniqueForTeamAsync(int teamId, string email, Guid? excludeCustomerId = null, CancellationToken cancellationToken = default)
    {
        try
        {
            var query = _dbSet.Where(c => c.TeamId == teamId && c.Email == email);

            if (excludeCustomerId.HasValue)
                query = query.Where(c => c.Id != excludeCustomerId.Value);

            return !await query.AnyAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking email uniqueness for TeamId {TeamId} and Email {Email}", teamId, email);
            throw;
        }
    }

    // Override to include related entities by default
    public override async Task<Customer?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        try
        {
            return await _dbSet
                .Include(c => c.Team)
                .Include(c => c.Payments)
                .Include(c => c.PaymentMethods)
                .FirstOrDefaultAsync(c => c.Id == id && !c.IsDeleted, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting customer by ID {Id}", id);
            throw;
        }
    }
}