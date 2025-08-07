using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using PaymentGateway.Core.Data;
using PaymentGateway.Core.Entities;
using PaymentGateway.Core.Enums;
using PaymentGateway.Core.Interfaces;
using System.Linq.Expressions;

namespace PaymentGateway.Core.Repositories;

public interface ITeamRepository : IRepository<Team>
{
    Task<Team?> GetByTeamSlugAsync(string teamSlug, CancellationToken cancellationToken = default);
    Task<Team?> GetBySecretKeyAsync(string secretKey, CancellationToken cancellationToken = default);
    Task<IEnumerable<Team>> GetActiveTeamsAsync(CancellationToken cancellationToken = default);
    Task<IEnumerable<Team>> GetTeamsRequiringPasswordChangeAsync(CancellationToken cancellationToken = default);
    Task<IEnumerable<Team>> GetLockedTeamsAsync(CancellationToken cancellationToken = default);
    Task<bool> IsTeamSlugUniqueAsync(string teamSlug, Guid? excludeTeamId = null, CancellationToken cancellationToken = default);
    Task<(decimal TotalAmount, int Count)> GetPaymentStatsByTeamAsync(Guid teamId, DateTime? startDate = null, DateTime? endDate = null, CancellationToken cancellationToken = default);
    Task<decimal> GetCurrentDailyAmountAsync(Guid teamId, CancellationToken cancellationToken = default);
    Task<decimal> GetCurrentMonthlyAmountAsync(Guid teamId, CancellationToken cancellationToken = default);
    Task<IEnumerable<Team>> GetTeamsWithHighFailedAuthAttemptsAsync(int threshold = 3, CancellationToken cancellationToken = default);
    Task<IEnumerable<Team>> GetTeamsForRiskReviewAsync(CancellationToken cancellationToken = default);
    Task<Team> UpdateAsync(Team team, CancellationToken cancellationToken = default);
}

public class TeamRepository : Repository<Team>, ITeamRepository
{
    public TeamRepository(
        PaymentGatewayDbContext context, 
        ILogger<TeamRepository> logger,
        IMemoryCache cache) 
        : base(context, logger, cache)
    {
    }

    public async Task<Team?> GetByTeamSlugAsync(string teamSlug, CancellationToken cancellationToken = default)
    {
        try
        {
            return await _dbSet
                .FirstOrDefaultAsync(t => t.TeamSlug == teamSlug, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting team by TeamSlug {TeamSlug}", teamSlug);
            throw;
        }
    }

    public async Task<Team?> GetBySecretKeyAsync(string secretKey, CancellationToken cancellationToken = default)
    {
        try
        {
            return await _dbSet
                .FirstOrDefaultAsync(t => t.SecretKey == secretKey, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting team by SecretKey");
            throw;
        }
    }

    public async Task<IEnumerable<Team>> GetActiveTeamsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await _dbSet
                .Where(t => t.IsActive)
                .Where(t => !t.LockedUntil.HasValue || t.LockedUntil <= DateTime.UtcNow)
                .OrderBy(t => t.TeamName)
                .ToListAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting active teams");
            throw;
        }
    }

    public async Task<IEnumerable<Team>> GetTeamsRequiringPasswordChangeAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var cutoffDate = DateTime.UtcNow.AddDays(-90);
            return await _dbSet
                .Where(t => t.IsActive)
                .Where(t => !t.LastPasswordChangeAt.HasValue || t.LastPasswordChangeAt <= cutoffDate)
                .OrderBy(t => t.LastPasswordChangeAt ?? DateTime.MinValue)
                .ToListAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting teams requiring password change");
            throw;
        }
    }

    public async Task<IEnumerable<Team>> GetLockedTeamsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var now = DateTime.UtcNow;
            return await _dbSet
                .Where(t => t.LockedUntil.HasValue && t.LockedUntil > now)
                .OrderByDescending(t => t.LockedUntil)
                .ToListAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting locked teams");
            throw;
        }
    }

    public async Task<bool> IsTeamSlugUniqueAsync(string teamSlug, Guid? excludeTeamId = null, CancellationToken cancellationToken = default)
    {
        try
        {
            var query = _dbSet.Where(t => t.TeamSlug == teamSlug);

            if (excludeTeamId.HasValue)
                query = query.Where(t => t.Id != excludeTeamId.Value);

            return !await query.AnyAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking TeamSlug uniqueness for {TeamSlug}", teamSlug);
            throw;
        }
    }

    public async Task<(decimal TotalAmount, int Count)> GetPaymentStatsByTeamAsync(Guid teamId, DateTime? startDate = null, DateTime? endDate = null, CancellationToken cancellationToken = default)
    {
        try
        {
            var query = _context.Set<Payment>().Where(p => p.TeamId == teamId);

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
            _logger.LogError(ex, "Error getting payment stats for TeamId {TeamId}", teamId);
            throw;
        }
    }

    public async Task<decimal> GetCurrentDailyAmountAsync(Guid teamId, CancellationToken cancellationToken = default)
    {
        try
        {
            var today = DateTime.UtcNow.Date;
            var tomorrow = today.AddDays(1);

            var successfulStatuses = new[]
            {
                PaymentStatus.CONFIRMED,
                PaymentStatus.REFUNDED,
                PaymentStatus.PARTIAL_REFUNDED
            };

            return await _context.Set<Payment>()
                .Where(p => p.TeamId == teamId)
                .Where(p => successfulStatuses.Contains(p.Status))
                .Where(p => p.CreatedAt >= today && p.CreatedAt < tomorrow)
                .SumAsync(p => p.Amount, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting current daily amount for TeamId {TeamId}", teamId);
            throw;
        }
    }

    public async Task<decimal> GetCurrentMonthlyAmountAsync(Guid teamId, CancellationToken cancellationToken = default)
    {
        try
        {
            var firstDayOfMonth = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1);
            var firstDayOfNextMonth = firstDayOfMonth.AddMonths(1);

            var successfulStatuses = new[]
            {
                PaymentStatus.CONFIRMED,
                PaymentStatus.REFUNDED,
                PaymentStatus.PARTIAL_REFUNDED
            };

            return await _context.Set<Payment>()
                .Where(p => p.TeamId == teamId)
                .Where(p => successfulStatuses.Contains(p.Status))
                .Where(p => p.CreatedAt >= firstDayOfMonth && p.CreatedAt < firstDayOfNextMonth)
                .SumAsync(p => p.Amount, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting current monthly amount for TeamId {TeamId}", teamId);
            throw;
        }
    }

    public async Task<IEnumerable<Team>> GetTeamsWithHighFailedAuthAttemptsAsync(int threshold = 3, CancellationToken cancellationToken = default)
    {
        try
        {
            return await _dbSet
                .Where(t => t.FailedAuthenticationAttempts >= threshold)
                .OrderByDescending(t => t.FailedAuthenticationAttempts)
                .ThenByDescending(t => t.LastSuccessfulAuthenticationAt)
                .ToListAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting teams with high failed auth attempts (threshold: {Threshold})", threshold);
            throw;
        }
    }

    public async Task<IEnumerable<Team>> GetTeamsForRiskReviewAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await _dbSet
                .Where(t => t.IsActive)
                .Where(t => t.RequireManualReviewForHighRisk)
                .Where(t => t.EnableFraudDetection)
                .OrderBy(t => t.TeamName)
                .ToListAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting teams for risk review");
            throw;
        }
    }

    // Override to include related entities by default
    public override async Task<Team?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        try
        {
            return await _dbSet
                .Include(t => t.Payments)
                .Include(t => t.Customers)
                .Include(t => t.PaymentMethods)
                .FirstOrDefaultAsync(t => t.Id == id && !t.IsDeleted, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting team by ID {Id}", id);
            throw;
        }
    }

    public async Task<Team> UpdateAsync(Team team, CancellationToken cancellationToken = default)
    {
        try
        {
            team.MarkAsUpdated();
            Update(team);
            await _context.SaveChangesAsync(cancellationToken);
            return team;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating team with TeamSlug {TeamSlug}", team.TeamSlug);
            throw;
        }
    }
}