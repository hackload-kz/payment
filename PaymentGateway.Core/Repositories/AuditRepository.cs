using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PaymentGateway.Core.Configuration;
using PaymentGateway.Core.Data;
using PaymentGateway.Core.Entities;

namespace PaymentGateway.Core.Repositories;

/// <summary>
/// Repository interface for audit operations
/// </summary>
public interface IAuditRepository
{
    // Basic querying
    Task<AuditEntry?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<List<AuditEntry>> GetEntityAuditTrailAsync(Guid entityId, string entityType, CancellationToken cancellationToken = default);
    Task<List<AuditEntry>> GetUserAuditTrailAsync(string userId, int limit = 100, CancellationToken cancellationToken = default);
    Task<List<AuditEntry>> GetTeamAuditTrailAsync(string teamSlug, DateTime? fromDate = null, DateTime? toDate = null, CancellationToken cancellationToken = default);
    
    // Advanced querying
    Task<List<AuditEntry>> QueryAsync(AuditQueryFilter filter, CancellationToken cancellationToken = default);
    Task<int> CountAsync(AuditQueryFilter filter, CancellationToken cancellationToken = default);
    Task<List<AuditEntry>> SearchAsync(string searchTerm, AuditCategory? category = null, int limit = 100, CancellationToken cancellationToken = default);
    
    // Statistics and analytics
    Task<AuditStatistics> GetStatisticsAsync(DateTime? fromDate = null, DateTime? toDate = null, CancellationToken cancellationToken = default);
    Task<Dictionary<string, int>> GetActionCountsAsync(DateTime fromDate, DateTime toDate, CancellationToken cancellationToken = default);
    Task<Dictionary<string, int>> GetEntityTypeCountsAsync(DateTime fromDate, DateTime toDate, CancellationToken cancellationToken = default);
    Task<Dictionary<string, int>> GetUserActivityCountsAsync(DateTime fromDate, DateTime toDate, int topUsers = 10, CancellationToken cancellationToken = default);
    
    // Security and compliance
    Task<List<AuditEntry>> GetSecurityEventsAsync(DateTime fromDate, DateTime toDate, AuditSeverity minSeverity = AuditSeverity.Warning, CancellationToken cancellationToken = default);
    Task<List<AuditEntry>> GetSensitiveOperationsAsync(DateTime fromDate, DateTime toDate, CancellationToken cancellationToken = default);
    Task<List<AuditEntry>> GetFailedOperationsAsync(DateTime fromDate, DateTime toDate, CancellationToken cancellationToken = default);
    Task<List<AuditEntry>> GetComplianceReportDataAsync(DateTime fromDate, DateTime toDate, string? entityType = null, CancellationToken cancellationToken = default);
    
    // Data management
    Task<int> ArchiveOldEntriesAsync(DateTime olderThan, int batchSize = 1000, CancellationToken cancellationToken = default);
    Task<int> DeleteArchivedEntriesAsync(DateTime olderThan, int batchSize = 1000, CancellationToken cancellationToken = default);
    Task<bool> VerifyIntegrityAsync(Guid auditEntryId, CancellationToken cancellationToken = default);
    
    // Performance and monitoring
    Task<List<AuditEntry>> GetSlowOperationsAsync(DateTime fromDate, DateTime toDate, CancellationToken cancellationToken = default);
    Task<Dictionary<DateTime, int>> GetHourlyActivityAsync(DateTime date, CancellationToken cancellationToken = default);
    Task<List<AuditEntry>> GetCorrelatedEventsAsync(string correlationId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Repository implementation for audit operations
/// </summary>
public class AuditRepository : IAuditRepository
{
    private readonly PaymentGatewayDbContext _context;
    private readonly IMemoryCache _cache;
    private readonly ILogger<AuditRepository> _logger;
    private readonly AuditConfiguration _auditConfig;

    public AuditRepository(
        PaymentGatewayDbContext context,
        IMemoryCache cache,
        ILogger<AuditRepository> logger,
        IOptions<AuditConfiguration> auditConfig)
    {
        _context = context;
        _cache = cache;
        _logger = logger;
        _auditConfig = auditConfig.Value;
    }

    public async Task<AuditEntry?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _context.AuditLog
            .FirstOrDefaultAsync(ae => ae.Id == id, cancellationToken);
    }

    public async Task<List<AuditEntry>> GetEntityAuditTrailAsync(Guid entityId, string entityType, CancellationToken cancellationToken = default)
    {
        var cacheKey = $"audit_trail_{entityType}_{entityId}";
        
        if (_auditConfig.Performance.EnableQueryCaching && _cache.TryGetValue(cacheKey, out List<AuditEntry>? cachedEntries))
        {
            return cachedEntries!;
        }

        var entries = await _context.AuditLog
            .Where(ae => ae.EntityId == entityId && ae.EntityType == entityType)
            .OrderByDescending(ae => ae.Timestamp)
            .Take(_auditConfig.MaxHistoryRecords)
            .ToListAsync(cancellationToken);

        if (_auditConfig.Performance.EnableQueryCaching)
        {
            _cache.Set(cacheKey, entries, TimeSpan.FromMinutes(_auditConfig.Performance.QueryCacheDurationMinutes));
        }

        return entries;
    }

    public async Task<List<AuditEntry>> GetUserAuditTrailAsync(string userId, int limit = 100, CancellationToken cancellationToken = default)
    {
        var actualLimit = Math.Min(limit, _auditConfig.MaxHistoryRecords);
        
        return await _context.AuditLog
            .Where(ae => ae.UserId == userId)
            .OrderByDescending(ae => ae.Timestamp)
            .Take(actualLimit)
            .ToListAsync(cancellationToken);
    }

    public async Task<List<AuditEntry>> GetTeamAuditTrailAsync(string teamSlug, DateTime? fromDate = null, DateTime? toDate = null, CancellationToken cancellationToken = default)
    {
        var query = _context.AuditLog.Where(ae => ae.TeamSlug == teamSlug);
        
        if (fromDate.HasValue)
            query = query.Where(ae => ae.Timestamp >= fromDate.Value);
            
        if (toDate.HasValue)
            query = query.Where(ae => ae.Timestamp <= toDate.Value);

        return await query
            .OrderByDescending(ae => ae.Timestamp)
            .Take(_auditConfig.MaxQueryResults)
            .ToListAsync(cancellationToken);
    }

    public async Task<List<AuditEntry>> QueryAsync(AuditQueryFilter filter, CancellationToken cancellationToken = default)
    {
        var query = _context.AuditLog.AsQueryable();

        // Apply filters
        query = ApplyFilters(query, filter);

        // Apply ordering and pagination
        return await query
            .OrderByDescending(ae => ae.Timestamp)
            .Skip(filter.Skip)
            .Take(Math.Min(filter.Take, _auditConfig.MaxQueryResults))
            .ToListAsync(cancellationToken);
    }

    public async Task<int> CountAsync(AuditQueryFilter filter, CancellationToken cancellationToken = default)
    {
        var query = _context.AuditLog.AsQueryable();
        query = ApplyFilters(query, filter);
        
        return await query.CountAsync(cancellationToken);
    }

    public async Task<List<AuditEntry>> SearchAsync(string searchTerm, AuditCategory? category = null, int limit = 100, CancellationToken cancellationToken = default)
    {
        var query = _context.AuditLog.AsQueryable();
        
        if (category.HasValue)
            query = query.Where(ae => ae.Category == category.Value);

        // Search in multiple fields
        query = query.Where(ae => 
            ae.Details!.Contains(searchTerm) ||
            ae.EntityType.Contains(searchTerm) ||
            ae.UserId!.Contains(searchTerm) ||
            ae.TeamSlug!.Contains(searchTerm) ||
            ae.EntitySnapshotAfter.Contains(searchTerm));

        return await query
            .OrderByDescending(ae => ae.Timestamp)
            .Take(Math.Min(limit, _auditConfig.MaxQueryResults))
            .ToListAsync(cancellationToken);
    }

    public async Task<AuditStatistics> GetStatisticsAsync(DateTime? fromDate = null, DateTime? toDate = null, CancellationToken cancellationToken = default)
    {
        var cacheKey = $"audit_stats_{fromDate?.ToString("yyyyMMdd")}_{toDate?.ToString("yyyyMMdd")}";
        
        if (_auditConfig.Performance.EnableQueryCaching && _cache.TryGetValue(cacheKey, out AuditStatistics? cachedStats))
        {
            return cachedStats!;
        }

        var query = _context.AuditLog.AsQueryable();
        
        if (fromDate.HasValue)
            query = query.Where(ae => ae.Timestamp >= fromDate.Value);
            
        if (toDate.HasValue)
            query = query.Where(ae => ae.Timestamp <= toDate.Value);

        var entries = await query.ToListAsync(cancellationToken);

        var statistics = new AuditStatistics
        {
            TotalEntries = entries.Count,
            ActionCounts = entries.GroupBy(ae => ae.Action.ToString()).ToDictionary(g => g.Key, g => g.Count()),
            EntityTypeCounts = entries.GroupBy(ae => ae.EntityType).ToDictionary(g => g.Key, g => g.Count()),
            SeverityCounts = entries.GroupBy(ae => ae.Severity.ToString()).ToDictionary(g => g.Key, g => g.Count()),
            CategoryCounts = entries.GroupBy(ae => ae.Category.ToString()).ToDictionary(g => g.Key, g => g.Count()),
            UserCounts = entries.Where(ae => !string.IsNullOrEmpty(ae.UserId))
                               .GroupBy(ae => ae.UserId!)
                               .ToDictionary(g => g.Key, g => g.Count()),
            EarliestEntry = entries.Count > 0 ? entries.Min(ae => ae.Timestamp) : null,
            LatestEntry = entries.Count > 0 ? entries.Max(ae => ae.Timestamp) : null,
            SensitiveEntries = entries.Count(ae => ae.IsSensitive),
            ArchivedEntries = entries.Count(ae => ae.IsArchived)
        };

        if (statistics.EarliestEntry.HasValue && statistics.LatestEntry.HasValue)
        {
            var daysDiff = (statistics.LatestEntry.Value - statistics.EarliestEntry.Value).TotalDays;
            statistics.AverageEntriesPerDay = daysDiff > 0 ? entries.Count / daysDiff : entries.Count;
        }

        if (_auditConfig.Performance.EnableQueryCaching)
        {
            _cache.Set(cacheKey, statistics, TimeSpan.FromMinutes(_auditConfig.Performance.QueryCacheDurationMinutes));
        }

        return statistics;
    }

    public async Task<Dictionary<string, int>> GetActionCountsAsync(DateTime fromDate, DateTime toDate, CancellationToken cancellationToken = default)
    {
        return await _context.AuditLog
            .Where(ae => ae.Timestamp >= fromDate && ae.Timestamp <= toDate)
            .GroupBy(ae => ae.Action)
            .Select(g => new { Action = g.Key.ToString(), Count = g.Count() })
            .ToDictionaryAsync(x => x.Action, x => x.Count, cancellationToken);
    }

    public async Task<Dictionary<string, int>> GetEntityTypeCountsAsync(DateTime fromDate, DateTime toDate, CancellationToken cancellationToken = default)
    {
        return await _context.AuditLog
            .Where(ae => ae.Timestamp >= fromDate && ae.Timestamp <= toDate)
            .GroupBy(ae => ae.EntityType)
            .Select(g => new { EntityType = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.EntityType, x => x.Count, cancellationToken);
    }

    public async Task<Dictionary<string, int>> GetUserActivityCountsAsync(DateTime fromDate, DateTime toDate, int topUsers = 10, CancellationToken cancellationToken = default)
    {
        return await _context.AuditLog
            .Where(ae => ae.Timestamp >= fromDate && ae.Timestamp <= toDate && ae.UserId != null)
            .GroupBy(ae => ae.UserId!)
            .Select(g => new { UserId = g.Key, Count = g.Count() })
            .OrderByDescending(x => x.Count)
            .Take(topUsers)
            .ToDictionaryAsync(x => x.UserId, x => x.Count, cancellationToken);
    }

    public async Task<List<AuditEntry>> GetSecurityEventsAsync(DateTime fromDate, DateTime toDate, AuditSeverity minSeverity = AuditSeverity.Warning, CancellationToken cancellationToken = default)
    {
        return await _context.AuditLog
            .Where(ae => ae.Category == AuditCategory.Security && 
                        ae.Timestamp >= fromDate && 
                        ae.Timestamp <= toDate && 
                        ae.Severity >= minSeverity)
            .OrderByDescending(ae => ae.Timestamp)
            .Take(_auditConfig.MaxQueryResults)
            .ToListAsync(cancellationToken);
    }

    public async Task<List<AuditEntry>> GetSensitiveOperationsAsync(DateTime fromDate, DateTime toDate, CancellationToken cancellationToken = default)
    {
        return await _context.AuditLog
            .Where(ae => ae.IsSensitive && ae.Timestamp >= fromDate && ae.Timestamp <= toDate)
            .OrderByDescending(ae => ae.Timestamp)
            .Take(_auditConfig.MaxQueryResults)
            .ToListAsync(cancellationToken);
    }

    public async Task<List<AuditEntry>> GetFailedOperationsAsync(DateTime fromDate, DateTime toDate, CancellationToken cancellationToken = default)
    {
        var failureActions = new[]
        {
            AuditAction.PaymentFailed,
            AuditAction.AuthenticationFailure,
            AuditAction.ApiCallFailed,
            AuditAction.SecurityViolation
        };

        return await _context.AuditLog
            .Where(ae => failureActions.Contains(ae.Action) && 
                        ae.Timestamp >= fromDate && 
                        ae.Timestamp <= toDate)
            .OrderByDescending(ae => ae.Timestamp)
            .Take(_auditConfig.MaxQueryResults)
            .ToListAsync(cancellationToken);
    }

    public async Task<List<AuditEntry>> GetComplianceReportDataAsync(DateTime fromDate, DateTime toDate, string? entityType = null, CancellationToken cancellationToken = default)
    {
        var query = _context.AuditLog
            .Where(ae => ae.Timestamp >= fromDate && ae.Timestamp <= toDate);

        if (!string.IsNullOrEmpty(entityType))
        {
            query = query.Where(ae => ae.EntityType == entityType);
        }

        // Focus on compliance-relevant actions
        var complianceActions = new[]
        {
            AuditAction.PaymentInitialized,
            AuditAction.PaymentAuthorized,
            AuditAction.PaymentConfirmed,
            AuditAction.PaymentCancelled,
            AuditAction.PaymentRefunded,
            AuditAction.AuthenticationAttempt,
            AuditAction.AuthenticationFailure,
            AuditAction.SecurityViolation,
            AuditAction.FraudDetected,
            AuditAction.ConfigurationChanged,
            AuditAction.DataExported
        };

        return await query
            .Where(ae => complianceActions.Contains(ae.Action) || ae.IsSensitive)
            .OrderBy(ae => ae.Timestamp)
            .ToListAsync(cancellationToken);
    }

    public async Task<int> ArchiveOldEntriesAsync(DateTime olderThan, int batchSize = 1000, CancellationToken cancellationToken = default)
    {
        var actualBatchSize = Math.Min(batchSize, _auditConfig.ArchivingBatchSize);
        var totalArchived = 0;

        while (true)
        {
            var entriesToArchive = await _context.AuditLog
                .Where(ae => ae.Timestamp < olderThan && !ae.IsArchived)
                .Take(actualBatchSize)
                .ToListAsync(cancellationToken);

            if (!entriesToArchive.Any())
                break;

            foreach (var entry in entriesToArchive)
            {
                entry.IsArchived = true;
                entry.ArchivedAt = DateTime.UtcNow;
            }

            await _context.SaveChangesAsync(cancellationToken);
            totalArchived += entriesToArchive.Count;

            _logger.LogDebug("Archived batch of {Count} audit entries", entriesToArchive.Count);

            if (entriesToArchive.Count < actualBatchSize)
                break;
        }

        _logger.LogInformation("Archived {Total} audit entries older than {Date}", totalArchived, olderThan);
        return totalArchived;
    }

    public async Task<int> DeleteArchivedEntriesAsync(DateTime olderThan, int batchSize = 1000, CancellationToken cancellationToken = default)
    {
        var actualBatchSize = Math.Min(batchSize, _auditConfig.ArchivingBatchSize);
        var totalDeleted = 0;

        while (true)
        {
            var entriesToDelete = await _context.AuditLog
                .Where(ae => ae.IsArchived && ae.ArchivedAt < olderThan)
                .Take(actualBatchSize)
                .ToListAsync(cancellationToken);

            if (!entriesToDelete.Any())
                break;

            _context.AuditLog.RemoveRange(entriesToDelete);
            await _context.SaveChangesAsync(cancellationToken);
            totalDeleted += entriesToDelete.Count;

            _logger.LogDebug("Deleted batch of {Count} archived audit entries", entriesToDelete.Count);

            if (entriesToDelete.Count < actualBatchSize)
                break;
        }

        _logger.LogInformation("Deleted {Total} archived audit entries older than {Date}", totalDeleted, olderThan);
        return totalDeleted;
    }

    public async Task<bool> VerifyIntegrityAsync(Guid auditEntryId, CancellationToken cancellationToken = default)
    {
        var auditEntry = await _context.AuditLog.FindAsync(new object[] { auditEntryId }, cancellationToken);
        if (auditEntry == null)
            return false;

        if (string.IsNullOrEmpty(auditEntry.IntegrityHash))
            return false;

        // Recalculate hash and compare
        var calculatedHash = CalculateIntegrityHash(auditEntry);
        return calculatedHash == auditEntry.IntegrityHash;
    }

    public async Task<List<AuditEntry>> GetSlowOperationsAsync(DateTime fromDate, DateTime toDate, CancellationToken cancellationToken = default)
    {
        // This would typically require additional timing information stored in metadata
        return await _context.AuditLog
            .Where(ae => ae.Timestamp >= fromDate && 
                        ae.Timestamp <= toDate && 
                        ae.Metadata != null && 
                        ae.Metadata.Contains("duration"))
            .OrderByDescending(ae => ae.Timestamp)
            .Take(100)
            .ToListAsync(cancellationToken);
    }

    public async Task<Dictionary<DateTime, int>> GetHourlyActivityAsync(DateTime date, CancellationToken cancellationToken = default)
    {
        var startDate = date.Date;
        var endDate = startDate.AddDays(1);

        var entries = await _context.AuditLog
            .Where(ae => ae.Timestamp >= startDate && ae.Timestamp < endDate)
            .ToListAsync(cancellationToken);

        return entries
            .GroupBy(ae => new DateTime(ae.Timestamp.Year, ae.Timestamp.Month, ae.Timestamp.Day, ae.Timestamp.Hour, 0, 0))
            .ToDictionary(g => g.Key, g => g.Count());
    }

    public async Task<List<AuditEntry>> GetCorrelatedEventsAsync(string correlationId, CancellationToken cancellationToken = default)
    {
        return await _context.AuditLog
            .Where(ae => ae.CorrelationId == correlationId)
            .OrderBy(ae => ae.Timestamp)
            .ToListAsync(cancellationToken);
    }

    // Private helper methods
    private IQueryable<AuditEntry> ApplyFilters(IQueryable<AuditEntry> query, AuditQueryFilter filter)
    {
        if (filter.EntityId.HasValue)
            query = query.Where(ae => ae.EntityId == filter.EntityId.Value);
            
        if (!string.IsNullOrEmpty(filter.EntityType))
            query = query.Where(ae => ae.EntityType == filter.EntityType);
            
        if (filter.Action.HasValue)
            query = query.Where(ae => ae.Action == filter.Action.Value);
            
        if (!string.IsNullOrEmpty(filter.UserId))
            query = query.Where(ae => ae.UserId == filter.UserId);
            
        if (!string.IsNullOrEmpty(filter.TeamSlug))
            query = query.Where(ae => ae.TeamSlug == filter.TeamSlug);
            
        if (filter.FromDate.HasValue)
            query = query.Where(ae => ae.Timestamp >= filter.FromDate.Value);
            
        if (filter.ToDate.HasValue)
            query = query.Where(ae => ae.Timestamp <= filter.ToDate.Value);
            
        if (filter.MinSeverity.HasValue)
            query = query.Where(ae => ae.Severity >= filter.MinSeverity.Value);
            
        if (filter.Category.HasValue)
            query = query.Where(ae => ae.Category == filter.Category.Value);
            
        if (!string.IsNullOrEmpty(filter.CorrelationId))
            query = query.Where(ae => ae.CorrelationId == filter.CorrelationId);
            
        if (!string.IsNullOrEmpty(filter.RequestId))
            query = query.Where(ae => ae.RequestId == filter.RequestId);
            
        if (filter.IsSensitive.HasValue)
            query = query.Where(ae => ae.IsSensitive == filter.IsSensitive.Value);
            
        if (filter.IsArchived.HasValue)
            query = query.Where(ae => ae.IsArchived == filter.IsArchived.Value);

        return query;
    }

    private string CalculateIntegrityHash(AuditEntry entry)
    {
        var hashInput = $"{entry.EntityId}|{entry.EntityType}|{entry.Action}|{entry.UserId}|{entry.Timestamp:O}|{entry.Details}|{entry.EntitySnapshotAfter}";
        
        using var sha256 = System.Security.Cryptography.SHA256.Create();
        var hashBytes = sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(hashInput));
        return Convert.ToHexString(hashBytes);
    }
}