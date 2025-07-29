using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PaymentGateway.Core.Configuration;
using PaymentGateway.Core.Repositories;
using PaymentGateway.Core.Services;

namespace PaymentGateway.Infrastructure.Services;

/// <summary>
/// Background service for audit log retention and archival
/// </summary>
public class AuditRetentionService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<AuditRetentionService> _logger;
    private readonly AuditConfiguration _auditConfig;
    private readonly TimeSpan _runInterval;

    public AuditRetentionService(
        IServiceProvider serviceProvider,
        ILogger<AuditRetentionService> logger,
        IOptions<AuditConfiguration> auditConfig)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _auditConfig = auditConfig.Value;
        _runInterval = TimeSpan.FromHours(24); // Run daily
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Audit retention service started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // Wait until the configured hour
                await WaitUntilScheduledTime(stoppingToken);

                if (stoppingToken.IsCancellationRequested)
                    break;

                await PerformRetentionTasksAsync(stoppingToken);

                // Wait for next scheduled run
                await Task.Delay(_runInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("Audit retention service is stopping");
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in audit retention service");
                
                // Wait before retrying
                await Task.Delay(TimeSpan.FromMinutes(30), stoppingToken);
            }
        }

        _logger.LogInformation("Audit retention service stopped");
    }

    private async Task WaitUntilScheduledTime(CancellationToken cancellationToken)
    {
        var now = DateTime.Now;
        var scheduledTime = now.Date.AddHours(_auditConfig.ArchivingHour);
        
        // If we've passed today's scheduled time, schedule for tomorrow
        if (now > scheduledTime)
        {
            scheduledTime = scheduledTime.AddDays(1);
        }

        var delay = scheduledTime - now;
        if (delay > TimeSpan.Zero)
        {
            _logger.LogInformation("Next audit retention run scheduled for {ScheduledTime}", scheduledTime);
            await Task.Delay(delay, cancellationToken);
        }
    }

    private async Task PerformRetentionTasksAsync(CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var auditRepository = scope.ServiceProvider.GetRequiredService<IAuditRepository>();
        var auditService = scope.ServiceProvider.GetRequiredService<IComprehensiveAuditService>();

        _logger.LogInformation("Starting audit retention tasks");

        try
        {
            // Task 1: Archive old entries
            if (_auditConfig.EnableAutoArchiving)
            {
                await ArchiveOldEntriesAsync(auditRepository, auditService, cancellationToken);
            }

            // Task 2: Delete old archived entries
            await DeleteOldArchivedEntriesAsync(auditRepository, auditService, cancellationToken);

            // Task 3: Verify integrity of critical entries
            await VerifyIntegrityAsync(auditRepository, auditService, cancellationToken);

            // Task 4: Generate retention statistics
            await GenerateRetentionStatisticsAsync(auditRepository, auditService, cancellationToken);

            _logger.LogInformation("Audit retention tasks completed successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during audit retention tasks");
            
            // Log the failure as an audit event
            await auditService.LogSystemEventAsync(
                PaymentGateway.Core.Entities.AuditAction.SystemStop,
                "AuditRetentionService",
                $"Retention tasks failed: {ex.Message}"
            );
            
            throw;
        }
    }

    private async Task ArchiveOldEntriesAsync(IAuditRepository auditRepository, IComprehensiveAuditService auditService, CancellationToken cancellationToken)
    {
        var archiveDate = DateTime.UtcNow.AddDays(-_auditConfig.RetentionDays);
        
        _logger.LogInformation("Archiving audit entries older than {ArchiveDate}", archiveDate);

        var archivedCount = await auditRepository.ArchiveOldEntriesAsync(
            archiveDate,
            _auditConfig.ArchivingBatchSize,
            cancellationToken
        );

        if (archivedCount > 0)
        {
            await auditService.LogSystemEventAsync(
                PaymentGateway.Core.Entities.AuditAction.DataArchived,
                "AuditEntry",
                $"Archived {archivedCount} audit entries older than {archiveDate:yyyy-MM-dd}"
            );

            _logger.LogInformation("Archived {Count} audit entries", archivedCount);
        }
        else
        {
            _logger.LogDebug("No audit entries to archive");
        }
    }

    private async Task DeleteOldArchivedEntriesAsync(IAuditRepository auditRepository, IComprehensiveAuditService auditService, CancellationToken cancellationToken)
    {
        var deleteDate = DateTime.UtcNow.AddDays(-_auditConfig.ArchivedRetentionDays);
        
        _logger.LogInformation("Deleting archived audit entries older than {DeleteDate}", deleteDate);

        var deletedCount = await auditRepository.DeleteArchivedEntriesAsync(
            deleteDate,
            _auditConfig.ArchivingBatchSize,
            cancellationToken
        );

        if (deletedCount > 0)
        {
            await auditService.LogSystemEventAsync(
                PaymentGateway.Core.Entities.AuditAction.DataPurged,
                "AuditEntry",
                $"Purged {deletedCount} archived audit entries older than {deleteDate:yyyy-MM-dd}"
            );

            _logger.LogInformation("Deleted {Count} archived audit entries", deletedCount);
        }
        else
        {
            _logger.LogDebug("No archived audit entries to delete");
        }
    }

    private async Task VerifyIntegrityAsync(IAuditRepository auditRepository, IComprehensiveAuditService auditService, CancellationToken cancellationToken)
    {
        if (!_auditConfig.EnableIntegrityHashing)
            return;

        _logger.LogDebug("Performing audit integrity verification");

        // Get a sample of recent sensitive entries for integrity verification
        var recentSensitiveEntries = await auditRepository.GetSensitiveOperationsAsync(
            DateTime.UtcNow.AddDays(-7),
            DateTime.UtcNow,
            cancellationToken
        );

        var integrityFailures = 0;
        var verifiedCount = 0;

        foreach (var entry in recentSensitiveEntries.Take(100)) // Verify up to 100 entries
        {
            var isValid = await auditRepository.VerifyIntegrityAsync(entry.Id, cancellationToken);
            if (!isValid)
            {
                integrityFailures++;
                
                await auditService.LogSystemEventAsync(
                    PaymentGateway.Core.Entities.AuditAction.SecurityViolation,
                    "AuditEntry",
                    $"Integrity verification failed for audit entry {entry.Id}"
                );

                _logger.LogWarning("Integrity verification failed for audit entry {AuditEntryId}", entry.Id);
            }
            
            verifiedCount++;
        }

        if (integrityFailures > 0)
        {
            _logger.LogError("Audit integrity verification found {Failures} failures out of {Total} entries", 
                integrityFailures, verifiedCount);
        }
        else
        {
            _logger.LogDebug("Audit integrity verification passed for {Count} entries", verifiedCount);
        }
    }

    private async Task GenerateRetentionStatisticsAsync(IAuditRepository auditRepository, IComprehensiveAuditService auditService, CancellationToken cancellationToken)
    {
        try
        {
            var statistics = await auditRepository.GetStatisticsAsync(
                DateTime.UtcNow.AddDays(-30),
                DateTime.UtcNow,
                cancellationToken
            );

            var retentionStats = new
            {
                TotalEntries = statistics.TotalEntries,
                ArchivedEntries = statistics.ArchivedEntries,
                SensitiveEntries = statistics.SensitiveEntries,
                ArchivePercentage = statistics.TotalEntries > 0 ? (double)statistics.ArchivedEntries / statistics.TotalEntries * 100 : 0,
                GeneratedAt = DateTime.UtcNow
            };

            await auditService.LogSystemEventAsync(
                PaymentGateway.Core.Entities.AuditAction.DataExported,
                "RetentionStatistics",
                $"Generated retention statistics: {retentionStats.TotalEntries} total, {retentionStats.ArchivedEntries} archived ({retentionStats.ArchivePercentage:F2}%)"
            );

            _logger.LogInformation("Generated audit retention statistics: {Stats}", retentionStats);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to generate retention statistics");
        }
    }
}

/// <summary>
/// Service for manual audit retention operations
/// </summary>
public interface IAuditRetentionManagementService
{
    Task<int> ArchiveEntriesAsync(DateTime olderThan, CancellationToken cancellationToken = default);
    Task<int> DeleteArchivedEntriesAsync(DateTime olderThan, CancellationToken cancellationToken = default);
    Task<AuditRetentionReport> GenerateRetentionReportAsync(CancellationToken cancellationToken = default);
    Task<bool> VerifyAllIntegrityAsync(int maxEntries = 1000, CancellationToken cancellationToken = default);
}

public class AuditRetentionManagementService : IAuditRetentionManagementService
{
    private readonly IAuditRepository _auditRepository;
    private readonly IComprehensiveAuditService _auditService;
    private readonly ILogger<AuditRetentionManagementService> _logger;
    private readonly AuditConfiguration _auditConfig;

    public AuditRetentionManagementService(
        IAuditRepository auditRepository,
        IComprehensiveAuditService auditService,
        ILogger<AuditRetentionManagementService> logger,
        IOptions<AuditConfiguration> auditConfig)
    {
        _auditRepository = auditRepository;
        _auditService = auditService;
        _logger = logger;
        _auditConfig = auditConfig.Value;
    }

    public async Task<int> ArchiveEntriesAsync(DateTime olderThan, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starting manual archive of entries older than {Date}", olderThan);

        var archivedCount = await _auditRepository.ArchiveOldEntriesAsync(
            olderThan,
            _auditConfig.ArchivingBatchSize,
            cancellationToken
        );

        await _auditService.LogSystemEventAsync(
            PaymentGateway.Core.Entities.AuditAction.DataArchived,
            "AuditEntry",
            $"Manually archived {archivedCount} audit entries older than {olderThan:yyyy-MM-dd}"
        );

        _logger.LogInformation("Manually archived {Count} audit entries", archivedCount);
        return archivedCount;
    }

    public async Task<int> DeleteArchivedEntriesAsync(DateTime olderThan, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starting manual deletion of archived entries older than {Date}", olderThan);

        var deletedCount = await _auditRepository.DeleteArchivedEntriesAsync(
            olderThan,
            _auditConfig.ArchivingBatchSize,
            cancellationToken
        );

        await _auditService.LogSystemEventAsync(
            PaymentGateway.Core.Entities.AuditAction.DataPurged,
            "AuditEntry",
            $"Manually purged {deletedCount} archived audit entries older than {olderThan:yyyy-MM-dd}"
        );

        _logger.LogInformation("Manually deleted {Count} archived audit entries", deletedCount);
        return deletedCount;
    }

    public async Task<AuditRetentionReport> GenerateRetentionReportAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Generating audit retention report");

        var now = DateTime.UtcNow;
        var thirtyDaysAgo = now.AddDays(-30);
        
        var statistics = await _auditRepository.GetStatisticsAsync(null, null, cancellationToken);
        var recentStatistics = await _auditRepository.GetStatisticsAsync(thirtyDaysAgo, now, cancellationToken);

        var archiveThreshold = now.AddDays(-_auditConfig.RetentionDays);
        var deleteThreshold = now.AddDays(-_auditConfig.ArchivedRetentionDays);

        var report = new AuditRetentionReport
        {
            GeneratedAt = now,
            TotalEntries = statistics.TotalEntries,
            ArchivedEntries = statistics.ArchivedEntries,
            SensitiveEntries = statistics.SensitiveEntries,
            RecentEntries = recentStatistics.TotalEntries,
            RetentionDays = _auditConfig.RetentionDays,
            ArchivedRetentionDays = _auditConfig.ArchivedRetentionDays,
            ArchiveThresholdDate = archiveThreshold,
            DeleteThresholdDate = deleteThreshold,
            OldestEntry = statistics.EarliestEntry,
            NewestEntry = statistics.LatestEntry,
            AverageEntriesPerDay = statistics.AverageEntriesPerDay,
            TopEntityTypes = statistics.EntityTypeCounts.OrderByDescending(kvp => kvp.Value).Take(10).ToDictionary(kvp => kvp.Key, kvp => kvp.Value),
            TopActions = statistics.ActionCounts.OrderByDescending(kvp => kvp.Value).Take(10).ToDictionary(kvp => kvp.Key, kvp => kvp.Value)
        };

        await _auditService.LogSystemEventAsync(
            PaymentGateway.Core.Entities.AuditAction.DataExported,
            "AuditRetentionReport",
            $"Generated retention report: {report.TotalEntries} total entries, {report.ArchivedEntries} archived"
        );

        return report;
    }

    public async Task<bool> VerifyAllIntegrityAsync(int maxEntries = 1000, CancellationToken cancellationToken = default)
    {
        if (!_auditConfig.EnableIntegrityHashing)
        {
            _logger.LogWarning("Integrity hashing is disabled, skipping verification");
            return true;
        }

        _logger.LogInformation("Starting comprehensive integrity verification of up to {MaxEntries} entries", maxEntries);

        var recentEntries = await _auditRepository.GetSensitiveOperationsAsync(
            DateTime.UtcNow.AddMonths(-1),
            DateTime.UtcNow,
            cancellationToken
        );

        var integrityFailures = 0;
        var verifiedCount = 0;
        var entriesToVerify = recentEntries.Take(maxEntries);

        foreach (var entry in entriesToVerify)
        {
            var isValid = await _auditRepository.VerifyIntegrityAsync(entry.Id, cancellationToken);
            if (!isValid)
            {
                integrityFailures++;
                
                await _auditService.LogSystemEventAsync(
                    PaymentGateway.Core.Entities.AuditAction.SecurityViolation,
                    "AuditEntry",
                    $"Integrity verification failed for audit entry {entry.Id}"
                );

                _logger.LogError("Integrity verification failed for audit entry {AuditEntryId}", entry.Id);
            }
            
            verifiedCount++;
        }

        var success = integrityFailures == 0;
        
        await _auditService.LogSystemEventAsync(
            success ? PaymentGateway.Core.Entities.AuditAction.DataExported : PaymentGateway.Core.Entities.AuditAction.SecurityViolation,
            "IntegrityVerification",
            $"Integrity verification completed: {verifiedCount} verified, {integrityFailures} failures"
        );

        if (success)
        {
            _logger.LogInformation("Integrity verification passed for all {Count} entries", verifiedCount);
        }
        else
        {
            _logger.LogError("Integrity verification found {Failures} failures out of {Total} entries", 
                integrityFailures, verifiedCount);
        }

        return success;
    }
}

/// <summary>
/// Report containing audit retention information
/// </summary>
public class AuditRetentionReport
{
    public DateTime GeneratedAt { get; set; }
    public int TotalEntries { get; set; }
    public int ArchivedEntries { get; set; }
    public int SensitiveEntries { get; set; }
    public int RecentEntries { get; set; }
    public int RetentionDays { get; set; }
    public int ArchivedRetentionDays { get; set; }
    public DateTime ArchiveThresholdDate { get; set; }
    public DateTime DeleteThresholdDate { get; set; }
    public DateTime? OldestEntry { get; set; }
    public DateTime? NewestEntry { get; set; }
    public double AverageEntriesPerDay { get; set; }
    public Dictionary<string, int> TopEntityTypes { get; set; } = new();
    public Dictionary<string, int> TopActions { get; set; } = new();
    
    public double ArchivePercentage => TotalEntries > 0 ? (double)ArchivedEntries / TotalEntries * 100 : 0;
    public TimeSpan DataSpan => NewestEntry.HasValue && OldestEntry.HasValue ? NewestEntry.Value - OldestEntry.Value : TimeSpan.Zero;
}