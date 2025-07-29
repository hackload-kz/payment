using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PaymentGateway.Core.Configuration;
using PaymentGateway.Core.Entities;
using PaymentGateway.Core.Repositories;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace PaymentGateway.Core.Services;

/// <summary>
/// Service for audit log integrity verification and tamper detection
/// </summary>
public interface IAuditIntegrityService
{
    // Integrity verification
    Task<bool> VerifyEntryIntegrityAsync(Guid auditEntryId, CancellationToken cancellationToken = default);
    Task<AuditIntegrityReport> VerifyBatchIntegrityAsync(IEnumerable<Guid> auditEntryIds, CancellationToken cancellationToken = default);
    Task<AuditIntegrityReport> VerifyTimeRangeIntegrityAsync(DateTime fromDate, DateTime toDate, CancellationToken cancellationToken = default);
    
    // Hash calculation and verification
    string CalculateIntegrityHash(AuditEntry entry);
    Task<bool> RecalculateAndVerifyHashAsync(Guid auditEntryId, CancellationToken cancellationToken = default);
    Task<int> RecalculateAllHashesAsync(int batchSize = 100, CancellationToken cancellationToken = default);
    
    // Tamper detection
    Task<List<AuditTamperAlert>> DetectTamperingAsync(DateTime fromDate, DateTime toDate, CancellationToken cancellationToken = default);
    Task<AuditChainVerificationResult> VerifyAuditChainAsync(Guid entityId, string entityType, CancellationToken cancellationToken = default);
    
    // Digital signatures (for high-security environments)
    Task<string> SignAuditEntryAsync(AuditEntry entry, CancellationToken cancellationToken = default);
    Task<bool> VerifyDigitalSignatureAsync(Guid auditEntryId, CancellationToken cancellationToken = default);
    
    // Compliance and reporting
    Task<AuditIntegrityComplianceReport> GenerateComplianceReportAsync(DateTime fromDate, DateTime toDate, CancellationToken cancellationToken = default);
}

public class AuditIntegrityService : IAuditIntegrityService
{
    private readonly IAuditRepository _auditRepository;
    private readonly IComprehensiveAuditService _auditService;
    private readonly ILogger<AuditIntegrityService> _logger;
    private readonly AuditConfiguration _auditConfig;
    private readonly string _signingKey;

    public AuditIntegrityService(
        IAuditRepository auditRepository,
        IComprehensiveAuditService auditService,
        ILogger<AuditIntegrityService> logger,
        IOptions<AuditConfiguration> auditConfig)
    {
        _auditRepository = auditRepository;
        _auditService = auditService;
        _logger = logger;
        _auditConfig = auditConfig.Value;
        _signingKey = Environment.GetEnvironmentVariable("AUDIT_SIGNING_KEY") ?? "default-key-for-development";
    }

    public async Task<bool> VerifyEntryIntegrityAsync(Guid auditEntryId, CancellationToken cancellationToken = default)
    {
        var entry = await _auditRepository.GetByIdAsync(auditEntryId, cancellationToken);
        if (entry == null)
        {
            _logger.LogWarning("Audit entry {AuditEntryId} not found for integrity verification", auditEntryId);
            return false;
        }

        if (string.IsNullOrEmpty(entry.IntegrityHash))
        {
            _logger.LogWarning("Audit entry {AuditEntryId} has no integrity hash", auditEntryId);
            return false;
        }

        var calculatedHash = CalculateIntegrityHash(entry);
        var isValid = calculatedHash == entry.IntegrityHash;

        if (!isValid)
        {
            await _auditService.LogSystemEventAsync(
                AuditAction.SecurityViolation,
                "AuditIntegrity",
                $"Integrity verification failed for audit entry {auditEntryId}. Expected: {entry.IntegrityHash}, Calculated: {calculatedHash}"
            );

            _logger.LogError("Integrity verification failed for audit entry {AuditEntryId}", auditEntryId);
        }

        return isValid;
    }

    public async Task<AuditIntegrityReport> VerifyBatchIntegrityAsync(IEnumerable<Guid> auditEntryIds, CancellationToken cancellationToken = default)
    {
        var report = new AuditIntegrityReport
        {
            StartTime = DateTime.UtcNow,
            TotalEntries = auditEntryIds.Count()
        };

        var verificationTasks = auditEntryIds.Select(async id =>
        {
            var isValid = await VerifyEntryIntegrityAsync(id, cancellationToken);
            return new { Id = id, IsValid = isValid };
        });

        var results = await Task.WhenAll(verificationTasks);

        report.ValidEntries = results.Count(r => r.IsValid);
        report.InvalidEntries = results.Count(r => !r.IsValid);
        report.FailedEntryIds = results.Where(r => !r.IsValid).Select(r => r.Id).ToList();
        report.EndTime = DateTime.UtcNow;
        report.IsSuccessful = report.InvalidEntries == 0;

        if (report.InvalidEntries > 0)
        {
            await _auditService.LogSystemEventAsync(
                AuditAction.SecurityViolation,
                "AuditIntegrity",
                $"Batch integrity verification found {report.InvalidEntries} invalid entries out of {report.TotalEntries}"
            );
        }

        _logger.LogInformation("Batch integrity verification completed: {Valid}/{Total} entries valid", 
            report.ValidEntries, report.TotalEntries);

        return report;
    }

    public async Task<AuditIntegrityReport> VerifyTimeRangeIntegrityAsync(DateTime fromDate, DateTime toDate, CancellationToken cancellationToken = default)
    {
        var filter = new AuditQueryFilter
        {
            FromDate = fromDate,
            ToDate = toDate,
            Take = int.MaxValue // Get all entries in range
        };

        var entries = await _auditRepository.QueryAsync(filter, cancellationToken);
        var entryIds = entries.Select(e => e.Id);

        var report = await VerifyBatchIntegrityAsync(entryIds, cancellationToken);
        report.DateRange = $"{fromDate:yyyy-MM-dd} to {toDate:yyyy-MM-dd}";

        return report;
    }

    public string CalculateIntegrityHash(AuditEntry entry)
    {
        var hashInput = $"{entry.EntityId}|{entry.EntityType}|{entry.Action}|{entry.UserId}|{entry.Timestamp:O}|{entry.Details}|{entry.EntitySnapshotAfter}";
        
        using var sha256 = SHA256.Create();
        var hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(hashInput));
        return Convert.ToHexString(hashBytes);
    }

    public async Task<bool> RecalculateAndVerifyHashAsync(Guid auditEntryId, CancellationToken cancellationToken = default)
    {
        var entry = await _auditRepository.GetByIdAsync(auditEntryId, cancellationToken);
        if (entry == null)
            return false;

        var originalHash = entry.IntegrityHash;
        var calculatedHash = CalculateIntegrityHash(entry);

        // If hashes don't match, there might be tampering
        if (originalHash != calculatedHash)
        {
            await _auditService.LogSystemEventAsync(
                AuditAction.SecurityViolation,
                "AuditIntegrity",
                $"Hash mismatch detected for audit entry {auditEntryId}. Original: {originalHash}, Calculated: {calculatedHash}"
            );

            return false;
        }

        return true;
    }

    public async Task<int> RecalculateAllHashesAsync(int batchSize = 100, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starting recalculation of all audit entry hashes");

        var filter = new AuditQueryFilter { Take = int.MaxValue };
        var allEntries = await _auditRepository.QueryAsync(filter, cancellationToken);
        
        var recalculatedCount = 0;
        var batches = allEntries.Chunk(batchSize);

        foreach (var batch in batches)
        {
            var tasks = batch.Select(async entry =>
            {
                var newHash = CalculateIntegrityHash(entry);
                if (entry.IntegrityHash != newHash)
                {
                    // This would require updating the entry, which would typically be done through a special maintenance API
                    _logger.LogWarning("Hash mismatch found for entry {EntryId}: stored={StoredHash}, calculated={CalculatedHash}", 
                        entry.Id, entry.IntegrityHash, newHash);
                    return false;
                }
                return true;
            });

            var results = await Task.WhenAll(tasks);
            recalculatedCount += results.Length;

            _logger.LogDebug("Recalculated hashes for batch of {BatchSize} entries", results.Length);
        }

        await _auditService.LogSystemEventAsync(
            AuditAction.DataExported,
            "AuditIntegrity",
            $"Recalculated integrity hashes for {recalculatedCount} audit entries"
        );

        _logger.LogInformation("Completed recalculation of {Count} audit entry hashes", recalculatedCount);
        return recalculatedCount;
    }

    public async Task<List<AuditTamperAlert>> DetectTamperingAsync(DateTime fromDate, DateTime toDate, CancellationToken cancellationToken = default)
    {
        var alerts = new List<AuditTamperAlert>();

        // Get all entries in the time range
        var filter = new AuditQueryFilter
        {
            FromDate = fromDate,
            ToDate = toDate,
            Take = int.MaxValue
        };

        var entries = await _auditRepository.QueryAsync(filter, cancellationToken);

        // Check for various tampering indicators
        foreach (var entry in entries)
        {
            // Check 1: Integrity hash mismatch
            if (!string.IsNullOrEmpty(entry.IntegrityHash))
            {
                var calculatedHash = CalculateIntegrityHash(entry);
                if (calculatedHash != entry.IntegrityHash)
                {
                    alerts.Add(new AuditTamperAlert
                    {
                        AuditEntryId = entry.Id,
                        AlertType = TamperAlertType.IntegrityHashMismatch,
                        Severity = AuditSeverity.Critical,
                        Description = $"Integrity hash mismatch detected",
                        DetectedAt = DateTime.UtcNow,
                        ExpectedValue = entry.IntegrityHash,
                        ActualValue = calculatedHash
                    });
                }
            }

            // Check 2: Suspicious timestamp patterns
            if (entry.Timestamp > DateTime.UtcNow.AddMinutes(5)) // Future timestamps
            {
                alerts.Add(new AuditTamperAlert
                {
                    AuditEntryId = entry.Id,
                    AlertType = TamperAlertType.SuspiciousTimestamp,
                    Severity = AuditSeverity.Warning,
                    Description = $"Future timestamp detected: {entry.Timestamp}",
                    DetectedAt = DateTime.UtcNow
                });
            }

            // Check 3: Missing required fields for sensitive operations
            if (entry.IsSensitive && string.IsNullOrEmpty(entry.UserId))
            {
                alerts.Add(new AuditTamperAlert
                {
                    AuditEntryId = entry.Id,
                    AlertType = TamperAlertType.MissingRequiredData,
                    Severity = AuditSeverity.Error,
                    Description = "Sensitive operation missing user identification",
                    DetectedAt = DateTime.UtcNow
                });
            }
        }

        // Check for sequence gaps (missing entries in chronological order)
        var chronologicalEntries = entries.OrderBy(e => e.Timestamp).ToList();
        for (int i = 1; i < chronologicalEntries.Count; i++)
        {
            var timeDiff = chronologicalEntries[i].Timestamp - chronologicalEntries[i - 1].Timestamp;
            if (timeDiff > TimeSpan.FromHours(24) && chronologicalEntries[i - 1].EntityId == chronologicalEntries[i].EntityId)
            {
                // Large time gap in same entity's audit trail might indicate missing entries
                alerts.Add(new AuditTamperAlert
                {
                    AuditEntryId = chronologicalEntries[i].Id,
                    AlertType = TamperAlertType.SuspiciousSequence,
                    Severity = AuditSeverity.Warning,
                    Description = $"Large time gap ({timeDiff.TotalHours:F1} hours) in audit sequence",
                    DetectedAt = DateTime.UtcNow
                });
            }
        }

        if (alerts.Any())
        {
            await _auditService.LogSystemEventAsync(
                AuditAction.SecurityViolation,
                "AuditTampering",
                $"Detected {alerts.Count} potential tampering indicators between {fromDate:yyyy-MM-dd} and {toDate:yyyy-MM-dd}"
            );
        }

        return alerts;
    }

    public async Task<AuditChainVerificationResult> VerifyAuditChainAsync(Guid entityId, string entityType, CancellationToken cancellationToken = default)
    {
        var auditTrail = await _auditRepository.GetEntityAuditTrailAsync(entityId, entityType, cancellationToken);
        
        var result = new AuditChainVerificationResult
        {
            EntityId = entityId,
            EntityType = entityType,
            TotalEntries = auditTrail.Count,
            VerificationTime = DateTime.UtcNow
        };

        // Verify each entry in the chain
        var invalidEntries = new List<Guid>();
        var previousEntry = (AuditEntry?)null;

        foreach (var entry in auditTrail.OrderBy(e => e.Timestamp))
        {
            // Verify integrity hash
            var calculatedHash = CalculateIntegrityHash(entry);
            if (calculatedHash != entry.IntegrityHash)
            {
                invalidEntries.Add(entry.Id);
                result.Issues.Add($"Integrity hash mismatch in entry {entry.Id}");
            }

            // Verify logical sequence
            if (previousEntry != null)
            {
                // Check for logical inconsistencies
                if (entry.Timestamp < previousEntry.Timestamp)
                {
                    result.Issues.Add($"Timestamp sequence violation: entry {entry.Id} is older than previous entry");
                }

                // Check for impossible state transitions
                if (IsImpossibleTransition(previousEntry, entry))
                {
                    result.Issues.Add($"Impossible state transition detected between entries {previousEntry.Id} and {entry.Id}");
                }
            }

            previousEntry = entry;
        }

        result.ValidEntries = result.TotalEntries - invalidEntries.Count;
        result.IsValid = invalidEntries.Count == 0 && result.Issues.Count == 0;
        result.InvalidEntryIds = invalidEntries;

        if (!result.IsValid)
        {
            await _auditService.LogSystemEventAsync(
                AuditAction.SecurityViolation,
                "AuditChain",
                $"Audit chain verification failed for {entityType} {entityId}: {result.Issues.Count} issues found"
            );
        }

        return result;
    }

    public async Task<string> SignAuditEntryAsync(AuditEntry entry, CancellationToken cancellationToken = default)
    {
        if (!_auditConfig.Compliance.RequireDigitalSignatures)
            return string.Empty;

        var dataToSign = $"{entry.Id}|{entry.IntegrityHash}|{entry.Timestamp:O}";
        
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(_signingKey));
        var signatureBytes = hmac.ComputeHash(Encoding.UTF8.GetBytes(dataToSign));
        var signature = Convert.ToBase64String(signatureBytes);

        await _auditService.LogSystemEventAsync(
            AuditAction.TokenGenerated,
            "AuditSignature",
            $"Digital signature generated for audit entry {entry.Id}"
        );

        return signature;
    }

    public async Task<bool> VerifyDigitalSignatureAsync(Guid auditEntryId, CancellationToken cancellationToken = default)
    {
        var entry = await _auditRepository.GetByIdAsync(auditEntryId, cancellationToken);
        if (entry == null)
            return false;

        // For this implementation, we'd need to store the signature in the metadata
        var signature = entry.GetMetadata<Dictionary<string, object>>()?.GetValueOrDefault("digital_signature")?.ToString();
        
        if (string.IsNullOrEmpty(signature))
            return !_auditConfig.Compliance.RequireDigitalSignatures; // If signatures not required, pass

        var expectedSignature = await SignAuditEntryAsync(entry, cancellationToken);
        return signature == expectedSignature;
    }

    public async Task<AuditIntegrityComplianceReport> GenerateComplianceReportAsync(DateTime fromDate, DateTime toDate, CancellationToken cancellationToken = default)
    {
        var report = new AuditIntegrityComplianceReport
        {
            ReportPeriod = $"{fromDate:yyyy-MM-dd} to {toDate:yyyy-MM-dd}",
            GeneratedAt = DateTime.UtcNow
        };

        // Overall integrity verification
        var integrityReport = await VerifyTimeRangeIntegrityAsync(fromDate, toDate, cancellationToken);
        report.TotalEntriesChecked = integrityReport.TotalEntries;
        report.IntegrityPassRate = integrityReport.TotalEntries > 0 ? 
            (double)integrityReport.ValidEntries / integrityReport.TotalEntries * 100 : 100;

        // Tamper detection
        var tamperAlerts = await DetectTamperingAsync(fromDate, toDate, cancellationToken);
        report.TamperAlertsCount = tamperAlerts.Count;
        report.CriticalTamperAlerts = tamperAlerts.Count(a => a.Severity == AuditSeverity.Critical);

        // Get sensitive operations
        var sensitiveOps = await _auditRepository.GetSensitiveOperationsAsync(fromDate, toDate, cancellationToken);
        report.SensitiveOperationsCount = sensitiveOps.Count;
        report.SensitiveOperationsWithoutUser = sensitiveOps.Count(op => string.IsNullOrEmpty(op.UserId));

        // Compliance status
        report.IsCompliant = report.IntegrityPassRate >= 99.9 && 
                           report.CriticalTamperAlerts == 0 && 
                           report.SensitiveOperationsWithoutUser == 0;

        // Recommendations
        if (report.IntegrityPassRate < 100)
        {
            report.Recommendations.Add("Investigate integrity failures and implement additional security measures");
        }

        if (report.TamperAlertsCount > 0)
        {
            report.Recommendations.Add("Review tamper alerts and strengthen audit trail protection");
        }

        if (report.SensitiveOperationsWithoutUser > 0)
        {
            report.Recommendations.Add("Ensure all sensitive operations are properly attributed to users");
        }

        await _auditService.LogSystemEventAsync(
            AuditAction.DataExported,
            "ComplianceReport",
            $"Generated integrity compliance report: {report.IntegrityPassRate:F2}% pass rate, {report.TamperAlertsCount} alerts"
        );

        return report;
    }

    private bool IsImpossibleTransition(AuditEntry previousEntry, AuditEntry currentEntry)
    {
        // Define impossible audit action transitions
        var impossibleTransitions = new Dictionary<AuditAction, AuditAction[]>
        {
            [AuditAction.Created] = new[] { AuditAction.Updated, AuditAction.Deleted }, // Can't update/delete before creation
            [AuditAction.Deleted] = new[] { AuditAction.Updated }, // Can't update after deletion
            [AuditAction.PaymentConfirmed] = new[] { AuditAction.PaymentInitialized }, // Can't initialize after confirmation
        };

        if (impossibleTransitions.TryGetValue(currentEntry.Action, out var invalidPreviousActions))
        {
            return invalidPreviousActions.Contains(previousEntry.Action);
        }

        return false;
    }
}

// Supporting classes for integrity verification
public class AuditIntegrityReport
{
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public string? DateRange { get; set; }
    public int TotalEntries { get; set; }
    public int ValidEntries { get; set; }
    public int InvalidEntries { get; set; }
    public List<Guid> FailedEntryIds { get; set; } = new();
    public bool IsSuccessful { get; set; }
    public TimeSpan Duration => EndTime - StartTime;
    public double SuccessRate => TotalEntries > 0 ? (double)ValidEntries / TotalEntries * 100 : 100;
}

public class AuditTamperAlert
{
    public Guid AuditEntryId { get; set; }
    public TamperAlertType AlertType { get; set; }
    public AuditSeverity Severity { get; set; }
    public string Description { get; set; } = string.Empty;
    public DateTime DetectedAt { get; set; }
    public string? ExpectedValue { get; set; }
    public string? ActualValue { get; set; }
}

public enum TamperAlertType
{
    IntegrityHashMismatch,
    SuspiciousTimestamp,
    MissingRequiredData,
    SuspiciousSequence,
    DigitalSignatureFailure
}

public class AuditChainVerificationResult
{
    public Guid EntityId { get; set; }
    public string EntityType { get; set; } = string.Empty;
    public int TotalEntries { get; set; }
    public int ValidEntries { get; set; }
    public List<Guid> InvalidEntryIds { get; set; } = new();
    public List<string> Issues { get; set; } = new();
    public bool IsValid { get; set; }
    public DateTime VerificationTime { get; set; }
}

public class AuditIntegrityComplianceReport
{
    public string ReportPeriod { get; set; } = string.Empty;
    public DateTime GeneratedAt { get; set; }
    public int TotalEntriesChecked { get; set; }
    public double IntegrityPassRate { get; set; }
    public int TamperAlertsCount { get; set; }
    public int CriticalTamperAlerts { get; set; }
    public int SensitiveOperationsCount { get; set; }
    public int SensitiveOperationsWithoutUser { get; set; }
    public bool IsCompliant { get; set; }
    public List<string> Recommendations { get; set; } = new();
}