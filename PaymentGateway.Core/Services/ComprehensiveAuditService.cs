using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PaymentGateway.Core.Data;
using PaymentGateway.Core.Entities;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PaymentGateway.Core.Configuration;

namespace PaymentGateway.Core.Services;

/// <summary>
/// Comprehensive audit service interface for payment operations
/// </summary>
public interface IComprehensiveAuditService
{
    // Basic audit operations
    Task<AuditEntry> LogAsync<T>(T entity, AuditAction action, AuditContext? context = null, string? details = null) where T : BaseEntity;
    Task<AuditEntry> LogAsync<T>(T entityBefore, T entityAfter, AuditAction action, AuditContext? context = null, string? details = null) where T : BaseEntity;
    Task<AuditEntry> LogSystemEventAsync(AuditAction action, string entityType, string details, AuditContext? context = null);
    
    // Query operations
    Task<List<AuditEntry>> GetAuditHistoryAsync(Guid entityId, CancellationToken cancellationToken = default);
    Task<List<AuditEntry>> GetUserAuditHistoryAsync(string userId, DateTime? fromDate = null, int maxRecords = 1000, CancellationToken cancellationToken = default);
    Task<List<AuditEntry>> QueryAuditLogsAsync(AuditQueryFilter filter, CancellationToken cancellationToken = default);
    
    // Statistics and reporting
    Task<AuditStatistics> GetAuditStatisticsAsync(DateTime? fromDate = null, DateTime? toDate = null, CancellationToken cancellationToken = default);
    Task<List<AuditEntry>> GetSensitiveOperationsAsync(DateTime fromDate, DateTime toDate, CancellationToken cancellationToken = default);
    Task<List<AuditEntry>> GetSecurityEventsAsync(DateTime fromDate, DateTime toDate, CancellationToken cancellationToken = default);
    
    // Integrity and compliance
    Task<bool> VerifyIntegrityAsync(Guid auditEntryId, CancellationToken cancellationToken = default);
    Task<int> ArchiveOldEntriesAsync(DateTime olderThan, CancellationToken cancellationToken = default);
    Task<List<AuditEntry>> GenerateComplianceReportAsync(DateTime fromDate, DateTime toDate, string? entityType = null, CancellationToken cancellationToken = default);
    
    // Context management
    void SetAuditContext(AuditContext context);
    AuditContext? GetCurrentAuditContext();
    void ClearAuditContext();
}

/// <summary>
/// Comprehensive audit service implementation
/// </summary>
public class ComprehensiveAuditService : IComprehensiveAuditService
{
    private readonly PaymentGatewayDbContext _context;
    private readonly ILogger<ComprehensiveAuditService> _logger;
    private readonly AuditConfiguration _auditConfig;
    private readonly ThreadLocal<AuditContext?> _currentContext = new();

    public ComprehensiveAuditService(
        PaymentGatewayDbContext context,
        ILogger<ComprehensiveAuditService> logger,
        IOptions<AuditConfiguration> auditConfig)
    {
        _context = context;
        _logger = logger;
        _auditConfig = auditConfig.Value;
    }

    public async Task<AuditEntry> LogAsync<T>(T entity, AuditAction action, AuditContext? context = null, string? details = null) where T : BaseEntity
    {
        return await LogAsync(null, entity, action, context, details);
    }

    public async Task<AuditEntry> LogAsync<T>(T? entityBefore, T entityAfter, AuditAction action, AuditContext? context = null, string? details = null) where T : BaseEntity
    {
        try
        {
            var auditContext = context ?? GetCurrentAuditContext();
            
            var auditEntry = new AuditEntry
            {
                EntityId = entityAfter.Id,
                EntityType = typeof(T).Name,
                Action = action,
                UserId = auditContext?.UserId,
                TeamSlug = auditContext?.TeamSlug,
                Timestamp = DateTime.UtcNow,
                Details = details,
                CorrelationId = auditContext?.CorrelationId,
                RequestId = auditContext?.RequestId,
                IpAddress = auditContext?.IpAddress,
                UserAgent = auditContext?.UserAgent,
                SessionId = auditContext?.SessionId,
                RiskScore = auditContext?.RiskScore,
                Severity = DetermineAuditSeverity(action),
                Category = DetermineAuditCategory(action),
                IsSensitive = IsSensitiveOperation(action, typeof(T).Name)
            };

            // Set entity snapshots
            if (entityBefore != null)
            {
                auditEntry.EntitySnapshotBefore = SerializeEntity(entityBefore);
            }
            auditEntry.EntitySnapshotAfter = SerializeEntity(entityAfter);

            // Set metadata from additional context data
            if (auditContext?.AdditionalData != null)
            {
                auditEntry.SetMetadata(auditContext.AdditionalData);
            }

            // Calculate integrity hash
            auditEntry.IntegrityHash = CalculateIntegrityHash(auditEntry);

            // Add to database
            _context.AuditLog.Add(auditEntry);
            await _context.SaveChangesAsync();

            _logger.LogInformation("Audit entry created: {Action} on {EntityType} {EntityId} by user {UserId}", 
                action, typeof(T).Name, entityAfter.Id, auditContext?.UserId ?? "System");

            return auditEntry;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create audit entry for {Action} on {EntityType} {EntityId}", 
                action, typeof(T).Name, entityAfter.Id);
            throw;
        }
    }

    public async Task<AuditEntry> LogSystemEventAsync(AuditAction action, string entityType, string details, AuditContext? context = null)
    {
        try
        {
            var auditContext = context ?? GetCurrentAuditContext();
            
            var auditEntry = new AuditEntry
            {
                EntityId = Guid.NewGuid(), // Generate unique ID for system events
                EntityType = entityType,
                Action = action,
                UserId = auditContext?.UserId ?? "System",
                TeamSlug = auditContext?.TeamSlug,
                Timestamp = DateTime.UtcNow,
                Details = details,
                CorrelationId = auditContext?.CorrelationId,
                RequestId = auditContext?.RequestId,
                IpAddress = auditContext?.IpAddress,
                UserAgent = auditContext?.UserAgent,
                SessionId = auditContext?.SessionId,
                Severity = DetermineAuditSeverity(action),
                Category = DetermineAuditCategory(action),
                IsSensitive = IsSensitiveOperation(action, entityType)
            };

            // Set system event snapshot
            auditEntry.EntitySnapshotAfter = JsonSerializer.Serialize(new { Details = details, Timestamp = auditEntry.Timestamp });

            // Set metadata from additional context data
            if (auditContext?.AdditionalData != null)
            {
                auditEntry.SetMetadata(auditContext.AdditionalData);
            }

            // Calculate integrity hash
            auditEntry.IntegrityHash = CalculateIntegrityHash(auditEntry);

            // Add to database
            _context.AuditLog.Add(auditEntry);
            await _context.SaveChangesAsync();

            _logger.LogInformation("System audit entry created: {Action} for {EntityType} by {UserId}", 
                action, entityType, auditContext?.UserId ?? "System");

            return auditEntry;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create system audit entry for {Action} on {EntityType}", action, entityType);
            throw;
        }
    }

    public async Task<List<AuditEntry>> GetAuditHistoryAsync(Guid entityId, CancellationToken cancellationToken = default)
    {
        return await _context.AuditLog
            .Where(ae => ae.EntityId == entityId)
            .OrderByDescending(ae => ae.Timestamp)
            .Take(_auditConfig.MaxHistoryRecords)
            .ToListAsync(cancellationToken);
    }

    public async Task<List<AuditEntry>> GetUserAuditHistoryAsync(string userId, DateTime? fromDate = null, int maxRecords = 1000, CancellationToken cancellationToken = default)
    {
        var query = _context.AuditLog.Where(ae => ae.UserId == userId);
        
        if (fromDate.HasValue)
        {
            query = query.Where(ae => ae.Timestamp >= fromDate.Value);
        }

        return await query
            .OrderByDescending(ae => ae.Timestamp)
            .Take(Math.Min(maxRecords, _auditConfig.MaxHistoryRecords))
            .ToListAsync(cancellationToken);
    }

    public async Task<List<AuditEntry>> QueryAuditLogsAsync(AuditQueryFilter filter, CancellationToken cancellationToken = default)
    {
        var query = _context.AuditLog.AsQueryable();

        // Apply filters
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

        return await query
            .OrderByDescending(ae => ae.Timestamp)
            .Skip(filter.Skip)
            .Take(Math.Min(filter.Take, _auditConfig.MaxQueryResults))
            .ToListAsync(cancellationToken);
    }

    public async Task<AuditStatistics> GetAuditStatisticsAsync(DateTime? fromDate = null, DateTime? toDate = null, CancellationToken cancellationToken = default)
    {
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

        return statistics;
    }

    public async Task<List<AuditEntry>> GetSensitiveOperationsAsync(DateTime fromDate, DateTime toDate, CancellationToken cancellationToken = default)
    {
        return await _context.AuditLog
            .Where(ae => ae.IsSensitive && ae.Timestamp >= fromDate && ae.Timestamp <= toDate)
            .OrderByDescending(ae => ae.Timestamp)
            .Take(_auditConfig.MaxQueryResults)
            .ToListAsync(cancellationToken);
    }

    public async Task<List<AuditEntry>> GetSecurityEventsAsync(DateTime fromDate, DateTime toDate, CancellationToken cancellationToken = default)
    {
        return await _context.AuditLog
            .Where(ae => ae.Category == AuditCategory.Security && ae.Timestamp >= fromDate && ae.Timestamp <= toDate)
            .OrderByDescending(ae => ae.Timestamp)
            .Take(_auditConfig.MaxQueryResults)
            .ToListAsync(cancellationToken);
    }

    public async Task<bool> VerifyIntegrityAsync(Guid auditEntryId, CancellationToken cancellationToken = default)
    {
        var auditEntry = await _context.AuditLog.FindAsync(new object[] { auditEntryId }, cancellationToken);
        if (auditEntry == null)
            return false;

        var calculatedHash = CalculateIntegrityHash(auditEntry);
        return calculatedHash == auditEntry.IntegrityHash;
    }

    public async Task<int> ArchiveOldEntriesAsync(DateTime olderThan, CancellationToken cancellationToken = default)
    {
        var entriesToArchive = await _context.AuditLog
            .Where(ae => ae.Timestamp < olderThan && !ae.IsArchived)
            .ToListAsync(cancellationToken);

        foreach (var entry in entriesToArchive)
        {
            entry.IsArchived = true;
            entry.ArchivedAt = DateTime.UtcNow;
        }

        await _context.SaveChangesAsync(cancellationToken);
        
        _logger.LogInformation("Archived {Count} audit entries older than {Date}", entriesToArchive.Count, olderThan);
        
        return entriesToArchive.Count;
    }

    public async Task<List<AuditEntry>> GenerateComplianceReportAsync(DateTime fromDate, DateTime toDate, string? entityType = null, CancellationToken cancellationToken = default)
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
            .Where(ae => complianceActions.Contains(ae.Action))
            .OrderBy(ae => ae.Timestamp)
            .ToListAsync(cancellationToken);
    }

    public void SetAuditContext(AuditContext context)
    {
        _currentContext.Value = context;
    }

    public AuditContext? GetCurrentAuditContext()
    {
        return _currentContext.Value;
    }

    public void ClearAuditContext()
    {
        _currentContext.Value = null;
    }

    // Private helper methods
    private string SerializeEntity<T>(T entity) where T : class
    {
        return JsonSerializer.Serialize(entity, new JsonSerializerOptions
        {
            WriteIndented = false,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
            ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles
        });
    }

    private AuditSeverity DetermineAuditSeverity(AuditAction action)
    {
        return action switch
        {
            AuditAction.SecurityViolation or AuditAction.FraudDetected => AuditSeverity.Critical,
            AuditAction.AuthenticationFailure or AuditAction.PaymentFailed => AuditSeverity.Error,
            AuditAction.RiskAssessment or AuditAction.SuspiciousActivity => AuditSeverity.Warning,
            _ => AuditSeverity.Information
        };
    }

    private AuditCategory DetermineAuditCategory(AuditAction action)
    {
        return action switch
        {
            AuditAction.PaymentInitialized or AuditAction.PaymentAuthorized or AuditAction.PaymentConfirmed 
                or AuditAction.PaymentCancelled or AuditAction.PaymentRefunded or AuditAction.PaymentFailed 
                or AuditAction.PaymentExpired or AuditAction.PaymentRetried => AuditCategory.Payment,
            
            AuditAction.AuthenticationAttempt or AuditAction.AuthenticationSuccess or AuditAction.AuthenticationFailure 
                or AuditAction.TokenGenerated or AuditAction.TokenExpired or AuditAction.TokenRevoked 
                or AuditAction.PasswordChanged => AuditCategory.Authentication,
            
            AuditAction.SecurityViolation or AuditAction.FraudDetected or AuditAction.SuspiciousActivity => AuditCategory.Security,
            
            AuditAction.ConfigurationChanged or AuditAction.FeatureFlagChanged or AuditAction.SettingsUpdated => AuditCategory.Configuration,
            
            AuditAction.SystemStart or AuditAction.SystemStop or AuditAction.DatabaseMigration 
                or AuditAction.BackupCreated => AuditCategory.System,
            
            AuditAction.ApiCallMade or AuditAction.ApiCallFailed or AuditAction.RateLimitExceeded => AuditCategory.API,
            
            AuditAction.DataExported or AuditAction.DataImported or AuditAction.DataArchived 
                or AuditAction.DataPurged => AuditCategory.Data,
            
            AuditAction.RiskAssessment => AuditCategory.Fraud,
            
            _ => AuditCategory.General
        };
    }

    private bool IsSensitiveOperation(AuditAction action, string entityType)
    {
        var sensitiveActions = new[]
        {
            AuditAction.PaymentInitialized,
            AuditAction.PaymentAuthorized,
            AuditAction.PaymentConfirmed,
            AuditAction.PaymentRefunded,
            AuditAction.AuthenticationAttempt,
            AuditAction.PasswordChanged,
            AuditAction.SecurityViolation,
            AuditAction.FraudDetected,
            AuditAction.DataExported,
            AuditAction.ConfigurationChanged
        };

        var sensitiveEntityTypes = new[] { "Payment", "Transaction", "Customer", "Team" };

        return sensitiveActions.Contains(action) || sensitiveEntityTypes.Contains(entityType);
    }

    private string CalculateIntegrityHash(AuditEntry entry)
    {
        var hashInput = $"{entry.EntityId}|{entry.EntityType}|{entry.Action}|{entry.UserId}|{entry.Timestamp:O}|{entry.Details}|{entry.EntitySnapshotAfter}";
        
        using var sha256 = SHA256.Create();
        var hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(hashInput));
        return Convert.ToHexString(hashBytes);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
        {
            _currentContext?.Dispose();
        }
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }
}