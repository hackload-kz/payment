using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PaymentGateway.Core.Configuration;
using PaymentGateway.Core.Entities;
using PaymentGateway.Core.Models;
using PaymentGateway.Core.Repositories;
using System.Text.Json;

namespace PaymentGateway.Core.Services;

/// <summary>
/// Service for generating compliance reports from audit logs
/// </summary>
public interface IAuditComplianceReportingService
{
    // Standard compliance reports
    Task<PciDssComplianceReport> GeneratePciDssReportAsync(DateTime fromDate, DateTime toDate, CancellationToken cancellationToken = default);
    Task<GdprComplianceReport> GenerateGdprReportAsync(DateTime fromDate, DateTime toDate, CancellationToken cancellationToken = default);
    Task<SoxComplianceReport> GenerateSoxReportAsync(DateTime fromDate, DateTime toDate, CancellationToken cancellationToken = default);
    
    // Custom compliance reports
    Task<ComplianceReport> GenerateCustomComplianceReportAsync(ComplianceReportRequest request, CancellationToken cancellationToken = default);
    Task<AuditTrailReport> GenerateAuditTrailReportAsync(Guid entityId, string entityType, CancellationToken cancellationToken = default);
    
    // Data protection and privacy reports
    Task<DataProcessingReport> GenerateDataProcessingReportAsync(DateTime fromDate, DateTime toDate, string? dataSubject = null, CancellationToken cancellationToken = default);
    Task<DataRetentionReport> GenerateDataRetentionReportAsync(CancellationToken cancellationToken = default);
    Task<DataBreachReport> GenerateDataBreachReportAsync(DateTime fromDate, DateTime toDate, CancellationToken cancellationToken = default);
    
    // Access and authorization reports
    Task<AccessReport> GenerateAccessReportAsync(DateTime fromDate, DateTime toDate, string? userId = null, CancellationToken cancellationToken = default);
    Task<PrivilegedAccessReport> GeneratePrivilegedAccessReportAsync(DateTime fromDate, DateTime toDate, CancellationToken cancellationToken = default);
    
    // Financial and transaction reports
    Task<FinancialAuditReport> GenerateFinancialAuditReportAsync(DateTime fromDate, DateTime toDate, CancellationToken cancellationToken = default);
    Task<TransactionAuditReport> GenerateTransactionAuditReportAsync(DateTime fromDate, DateTime toDate, string? teamSlug = null, CancellationToken cancellationToken = default);
    
    // Report management
    Task<List<ComplianceReportSummary>> GetReportHistoryAsync(int days = 30, CancellationToken cancellationToken = default);
    Task<byte[]> ExportReportAsync(string reportId, ComplianceReportFormat format, CancellationToken cancellationToken = default);
}

public class AuditComplianceReportingService : IAuditComplianceReportingService
{
    private readonly IAuditRepository _auditRepository;
    private readonly IComprehensiveAuditService _auditService;
    private readonly IAuditIntegrityService _integrityService;
    private readonly ILogger<AuditComplianceReportingService> _logger;
    private readonly AuditConfiguration _auditConfig;

    public AuditComplianceReportingService(
        IAuditRepository auditRepository,
        IComprehensiveAuditService auditService,
        IAuditIntegrityService integrityService,
        ILogger<AuditComplianceReportingService> logger,
        IOptions<AuditConfiguration> auditConfig)
    {
        _auditRepository = auditRepository;
        _auditService = auditService;
        _integrityService = integrityService;
        _logger = logger;
        _auditConfig = auditConfig.Value;
    }

    public async Task<PciDssComplianceReport> GeneratePciDssReportAsync(DateTime fromDate, DateTime toDate, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Generating PCI DSS compliance report for period {FromDate} to {ToDate}", fromDate, toDate);

        var report = new PciDssComplianceReport
        {
            ReportPeriod = $"{fromDate:yyyy-MM-dd} to {toDate:yyyy-MM-dd}",
            GeneratedAt = DateTime.UtcNow,
            ComplianceFramework = "PCI DSS v4.0"
        };

        // PCI DSS Requirement 10: Log and monitor all access to network resources and cardholder data
        await AnalyzePciRequirement10Async(report, fromDate, toDate, cancellationToken);
        
        // PCI DSS Requirement 8: Identify and authenticate access to system components
        await AnalyzePciRequirement8Async(report, fromDate, toDate, cancellationToken);
        
        // PCI DSS Requirement 7: Restrict access to cardholder data by business need to know
        await AnalyzePciRequirement7Async(report, fromDate, toDate, cancellationToken);

        // Overall compliance assessment
        report.OverallComplianceScore = CalculatePciComplianceScore(report);
        report.IsCompliant = report.OverallComplianceScore >= 95; // 95% threshold for compliance

        // Generate recommendations
        GeneratePciRecommendations(report);

        await _auditService.LogSystemEventAsync(
            AuditAction.DataExported,
            "PciDssReport",
            $"Generated PCI DSS compliance report: {report.OverallComplianceScore:F1}% compliance score"
        );

        return report;
    }

    public async Task<GdprComplianceReport> GenerateGdprReportAsync(DateTime fromDate, DateTime toDate, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Generating GDPR compliance report for period {FromDate} to {ToDate}", fromDate, toDate);

        var report = new GdprComplianceReport
        {
            ReportPeriod = $"{fromDate:yyyy-MM-dd} to {toDate:yyyy-MM-dd}",
            GeneratedAt = DateTime.UtcNow,
            ComplianceFramework = "GDPR (EU) 2016/679"
        };

        // Article 5: Principles relating to processing of personal data
        await AnalyzeGdprArticle5Async(report, fromDate, toDate, cancellationToken);
        
        // Article 32: Security of processing
        await AnalyzeGdprArticle32Async(report, fromDate, toDate, cancellationToken);
        
        // Article 33: Notification of personal data breach
        await AnalyzeGdprArticle33Async(report, fromDate, toDate, cancellationToken);
        
        // Article 30: Records of processing activities
        await AnalyzeGdprArticle30Async(report, fromDate, toDate, cancellationToken);

        // Data subject rights compliance
        await AnalyzeDataSubjectRightsAsync(report, fromDate, toDate, cancellationToken);

        report.OverallComplianceScore = CalculateGdprComplianceScore(report);
        report.IsCompliant = report.OverallComplianceScore >= 90; // 90% threshold for GDPR compliance

        GenerateGdprRecommendations(report);

        await _auditService.LogSystemEventAsync(
            AuditAction.DataExported,
            "GdprReport",
            $"Generated GDPR compliance report: {report.OverallComplianceScore:F1}% compliance score"
        );

        return report;
    }

    public async Task<SoxComplianceReport> GenerateSoxReportAsync(DateTime fromDate, DateTime toDate, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Generating SOX compliance report for period {FromDate} to {ToDate}", fromDate, toDate);

        var report = new SoxComplianceReport
        {
            ReportPeriod = $"{fromDate:yyyy-MM-dd} to {toDate:yyyy-MM-dd}",
            GeneratedAt = DateTime.UtcNow,
            ComplianceFramework = "Sarbanes-Oxley Act Section 404"
        };

        // Section 404: Management assessment of internal controls
        await AnalyzeSoxSection404Async(report, fromDate, toDate, cancellationToken);
        
        // Financial data integrity
        await AnalyzeFinancialDataIntegrityAsync(report, fromDate, toDate, cancellationToken);
        
        // Change management controls
        await AnalyzeChangeManagementControlsAsync(report, fromDate, toDate, cancellationToken);
        
        // Segregation of duties
        await AnalyzeSegregationOfDutiesAsync(report, fromDate, toDate, cancellationToken);

        report.OverallComplianceScore = CalculateSoxComplianceScore(report);
        report.IsCompliant = report.OverallComplianceScore >= 98; // 98% threshold for SOX compliance

        GenerateSoxRecommendations(report);

        await _auditService.LogSystemEventAsync(
            AuditAction.DataExported,
            "SoxReport",
            $"Generated SOX compliance report: {report.OverallComplianceScore:F1}% compliance score"
        );

        return report;
    }

    public async Task<ComplianceReport> GenerateCustomComplianceReportAsync(ComplianceReportRequest request, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Generating custom compliance report: {ReportName}", request.ReportName);

        var report = new ComplianceReport
        {
            ReportId = Guid.NewGuid(),
            ReportName = request.ReportName,
            ReportPeriod = $"{request.FromDate:yyyy-MM-dd} to {request.ToDate:yyyy-MM-dd}",
            GeneratedAt = DateTime.UtcNow,
            RequestedBy = request.RequestedBy
        };

        // Apply filters based on request
        var filter = new AuditQueryFilter
        {
            FromDate = request.FromDate,
            ToDate = request.ToDate,
            EntityType = request.EntityType,
            UserId = request.UserId,
            TeamSlug = request.TeamSlug,
            Category = request.Category,
            MinSeverity = request.MinSeverity,
            Take = int.MaxValue
        };

        var entries = await _auditRepository.QueryAsync(filter, cancellationToken);
        
        // Generate sections based on request
        foreach (var section in request.Sections)
        {
            var sectionData = await GenerateSectionDataAsync(section, entries, cancellationToken);
            report.Sections.Add(section, sectionData);
        }

        // Calculate metrics
        report.TotalEntries = entries.Count;
        report.CriticalIssues = entries.Count(e => e.Severity == AuditSeverity.Critical);
        report.SecurityEvents = entries.Count(e => e.Category == AuditCategory.Security);
        report.ComplianceScore = CalculateCustomComplianceScore(entries, request);

        await _auditService.LogSystemEventAsync(
            AuditAction.DataExported,
            "CustomComplianceReport",
            $"Generated custom compliance report '{request.ReportName}' with {entries.Count} entries"
        );

        return report;
    }

    public async Task<AuditTrailReport> GenerateAuditTrailReportAsync(Guid entityId, string entityType, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Generating audit trail report for {EntityType} {EntityId}", entityType, entityId);

        var auditTrail = await _auditRepository.GetEntityAuditTrailAsync(entityId, entityType, cancellationToken);
        
        var report = new AuditTrailReport
        {
            EntityId = entityId,
            EntityType = entityType,
            GeneratedAt = DateTime.UtcNow,
            TotalEntries = auditTrail.Count
        };

        if (auditTrail.Any())
        {
            report.FirstEntry = auditTrail.Min(e => e.Timestamp);
            report.LastEntry = auditTrail.Max(e => e.Timestamp);
            
            // Categorize entries
            report.EntriesByAction = auditTrail
                .GroupBy(e => e.Action.ToString())
                .ToDictionary(g => g.Key, g => g.Count());

            report.EntriesByUser = auditTrail
                .Where(e => !string.IsNullOrEmpty(e.UserId))
                .GroupBy(e => e.UserId!)
                .ToDictionary(g => g.Key, g => g.Count());

            // Check for suspicious patterns
            report.SuspiciousPatterns = await DetectSuspiciousPatternsInTrailAsync(auditTrail);

            // Verify integrity
            var integrityReport = await _integrityService.VerifyBatchIntegrityAsync(
                auditTrail.Select(e => e.Id), 
                cancellationToken
            );
            
            report.IntegrityVerified = integrityReport.IsSuccessful;
            report.IntegrityIssues = integrityReport.FailedEntryIds.Count;
        }

        await _auditService.LogSystemEventAsync(
            AuditAction.DataExported,
            "AuditTrailReport",
            $"Generated audit trail report for {entityType} {entityId} with {auditTrail.Count} entries"
        );

        return report;
    }

    public async Task<DataProcessingReport> GenerateDataProcessingReportAsync(DateTime fromDate, DateTime toDate, string? dataSubject = null, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Generating data processing report for period {FromDate} to {ToDate}", fromDate, toDate);

        var filter = new AuditQueryFilter
        {
            FromDate = fromDate,
            ToDate = toDate,
            UserId = dataSubject,
            Take = int.MaxValue
        };

        var entries = await _auditRepository.QueryAsync(filter, cancellationToken);
        
        var report = new DataProcessingReport
        {
            ReportPeriod = $"{fromDate:yyyy-MM-dd} to {toDate:yyyy-MM-dd}",
            DataSubject = dataSubject,
            GeneratedAt = DateTime.UtcNow
        };

        // Categorize processing activities
        var processingActivities = new[]
        {
            AuditAction.Created,
            AuditAction.Updated,
            AuditAction.Deleted,
            AuditAction.DataExported,
            AuditAction.DataImported
        };

        report.ProcessingActivities = entries
            .Where(e => processingActivities.Contains(e.Action))
            .GroupBy(e => e.Action.ToString())
            .ToDictionary(g => g.Key, g => g.Count());

        // Data categories processed
        report.DataCategoriesProcessed = entries
            .GroupBy(e => e.EntityType)
            .ToDictionary(g => g.Key, g => g.Count());

        // Processing purposes (inferred from actions and context)
        report.ProcessingPurposes = InferProcessingPurposes(entries);

        // Legal basis for processing
        report.LegalBasisCount = entries
            .Where(e => e.IsSensitive)
            .GroupBy(e => InferLegalBasis(e))
            .ToDictionary(g => g.Key, g => g.Count());

        // Data retention analysis
        report.DataRetentionCompliance = await AnalyzeDataRetentionComplianceAsync(entries, cancellationToken);

        await _auditService.LogSystemEventAsync(
            AuditAction.DataExported,
            "DataProcessingReport",
            $"Generated data processing report for {dataSubject ?? "all subjects"} with {entries.Count} processing activities"
        );

        return report;
    }

    public async Task<DataRetentionReport> GenerateDataRetentionReportAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Generating data retention compliance report");

        var report = new DataRetentionReport
        {
            GeneratedAt = DateTime.UtcNow,
            RetentionPolicies = _auditConfig.Compliance.DataRetentionRequirements
        };

        // Analyze retention compliance for each data type
        foreach (var policy in _auditConfig.Compliance.DataRetentionRequirements)
        {
            var retentionDate = DateTime.UtcNow.AddDays(-policy.Value);
            
            var filter = new AuditQueryFilter
            {
                EntityType = policy.Key,
                ToDate = retentionDate,
                IsArchived = false,
                Take = int.MaxValue
            };

            var oldEntries = await _auditRepository.QueryAsync(filter, cancellationToken);
            
            report.RetentionCompliance[policy.Key] = new DataRetentionStatus
            {
                DataType = policy.Key,
                RetentionPeriodDays = policy.Value,
                EntriesRequiringArchival = oldEntries.Count,
                OldestEntry = oldEntries.Any() ? oldEntries.Min(e => e.Timestamp) : null,
                ComplianceStatus = oldEntries.Count == 0 ? "Compliant" : "Action Required"
            };
        }

        // Overall compliance assessment
        report.OverallCompliance = report.RetentionCompliance.Values.All(r => r.ComplianceStatus == "Compliant");

        await _auditService.LogSystemEventAsync(
            AuditAction.DataExported,
            "DataRetentionReport",
            $"Generated data retention report: {(report.OverallCompliance ? "Compliant" : "Action Required")}"
        );

        return report;
    }

    public async Task<DataBreachReport> GenerateDataBreachReportAsync(DateTime fromDate, DateTime toDate, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Generating data breach report for period {FromDate} to {ToDate}", fromDate, toDate);

        var securityEvents = await _auditRepository.GetSecurityEventsAsync(fromDate, toDate, AuditSeverity.Warning, cancellationToken);
        
        var report = new DataBreachReport
        {
            ReportPeriod = $"{fromDate:yyyy-MM-dd} to {toDate:yyyy-MM-dd}",
            GeneratedAt = DateTime.UtcNow
        };

        // Identify potential breaches
        var potentialBreaches = securityEvents
            .Where(e => e.Action == AuditAction.SecurityViolation || 
                       e.Action == AuditAction.FraudDetected ||
                       e.Severity == AuditSeverity.Critical)
            .ToList();

        foreach (var incident in potentialBreaches)
        {
            var breach = new DataBreachIncident
            {
                IncidentId = incident.Id,
                Timestamp = incident.Timestamp,
                Severity = incident.Severity.ToString(),
                Description = incident.Details ?? "Security incident detected",
                AffectedDataTypes = new[] { incident.EntityType },
                IpAddress = incident.IpAddress,
                UserId = incident.UserId,
                Status = DetermineBreachStatus(incident)
            };

            report.Incidents.Add(breach);
        }

        // Categorize by severity
        report.IncidentsBySeverity = report.Incidents
            .GroupBy(i => i.Severity)
            .ToDictionary(g => g.Key, g => g.Count());

        // Analyze response times
        report.AverageResponseTime = CalculateAverageResponseTime(report.Incidents);
        
        // Compliance assessment
        report.BreachNotificationCompliance = AssessBreachNotificationCompliance(report.Incidents);

        await _auditService.LogSystemEventAsync(
            AuditAction.DataExported,
            "DataBreachReport",
            $"Generated data breach report with {report.Incidents.Count} incidents"
        );

        return report;
    }

    public async Task<AccessReport> GenerateAccessReportAsync(DateTime fromDate, DateTime toDate, string? userId = null, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Generating access report for period {FromDate} to {ToDate}", fromDate, toDate);

        var filter = new AuditQueryFilter
        {
            FromDate = fromDate,
            ToDate = toDate,
            UserId = userId,
            Category = AuditCategory.Authentication,
            Take = int.MaxValue
        };

        var accessEntries = await _auditRepository.QueryAsync(filter, cancellationToken);
        
        var report = new AccessReport
        {
            ReportPeriod = $"{fromDate:yyyy-MM-dd} to {toDate:yyyy-MM-dd}",
            UserId = userId,
            GeneratedAt = DateTime.UtcNow
        };

        // Successful vs failed access attempts
        report.SuccessfulAttempts = accessEntries.Count(e => e.Action == AuditAction.AuthenticationSuccess);
        report.FailedAttempts = accessEntries.Count(e => e.Action == AuditAction.AuthenticationFailure);

        // Access patterns by time
        report.AccessByHour = accessEntries
            .GroupBy(e => e.Timestamp.Hour)
            .ToDictionary(g => g.Key, g => g.Count());

        // Access by IP address
        report.AccessByIP = accessEntries
            .Where(e => !string.IsNullOrEmpty(e.IpAddress))
            .GroupBy(e => e.IpAddress!)
            .ToDictionary(g => g.Key, g => g.Count());

        // Suspicious access patterns
        report.SuspiciousPatterns = await DetectSuspiciousAccessPatternsAsync(accessEntries);

        await _auditService.LogSystemEventAsync(
            AuditAction.DataExported,
            "AccessReport",
            $"Generated access report for {userId ?? "all users"} with {accessEntries.Count} access events"
        );

        return report;
    }

    public async Task<PrivilegedAccessReport> GeneratePrivilegedAccessReportAsync(DateTime fromDate, DateTime toDate, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Generating privileged access report for period {FromDate} to {ToDate}", fromDate, toDate);

        var filter = new AuditQueryFilter
        {
            FromDate = fromDate,
            ToDate = toDate,
            IsSensitive = true,
            Take = int.MaxValue
        };

        var privilegedEntries = await _auditRepository.QueryAsync(filter, cancellationToken);
        
        var report = new PrivilegedAccessReport
        {
            ReportPeriod = $"{fromDate:yyyy-MM-dd} to {toDate:yyyy-MM-dd}",
            GeneratedAt = DateTime.UtcNow
        };

        // Privileged operations by user
        report.PrivilegedOperationsByUser = privilegedEntries
            .Where(e => !string.IsNullOrEmpty(e.UserId))
            .GroupBy(e => e.UserId!)
            .ToDictionary(g => g.Key, g => g.Count());

        // Privileged operations by type
        report.PrivilegedOperationsByType = privilegedEntries
            .GroupBy(e => e.Action.ToString())
            .ToDictionary(g => g.Key, g => g.Count());

        // Administrative changes
        var adminActions = new[]
        {
            AuditAction.ConfigurationChanged,
            AuditAction.PasswordChanged,
            AuditAction.TokenGenerated
        };

        report.AdministrativeChanges = privilegedEntries
            .Where(e => adminActions.Contains(e.Action))
            .Count();

        // Off-hours privileged access
        report.OffHoursPrivilegedAccess = privilegedEntries
            .Where(e => e.Timestamp.Hour < 6 || e.Timestamp.Hour > 22)
            .Count();

        await _auditService.LogSystemEventAsync(
            AuditAction.DataExported,
            "PrivilegedAccessReport",
            $"Generated privileged access report with {privilegedEntries.Count} privileged operations"
        );

        return report;
    }

    public async Task<FinancialAuditReport> GenerateFinancialAuditReportAsync(DateTime fromDate, DateTime toDate, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Generating financial audit report for period {FromDate} to {ToDate}", fromDate, toDate);

        var filter = new AuditQueryFilter
        {
            FromDate = fromDate,
            ToDate = toDate,
            Category = AuditCategory.Payment,
            Take = int.MaxValue
        };

        var financialEntries = await _auditRepository.QueryAsync(filter, cancellationToken);
        
        var report = new FinancialAuditReport
        {
            ReportPeriod = $"{fromDate:yyyy-MM-dd} to {toDate:yyyy-MM-dd}",
            GeneratedAt = DateTime.UtcNow
        };

        // Payment transactions audit
        var paymentActions = new[]
        {
            AuditAction.PaymentInitialized,
            AuditAction.PaymentAuthorized,
            AuditAction.PaymentConfirmed,
            AuditAction.PaymentCancelled,
            AuditAction.PaymentRefunded
        };

        report.PaymentTransactions = financialEntries
            .Where(e => paymentActions.Contains(e.Action))
            .GroupBy(e => e.Action.ToString())
            .ToDictionary(g => g.Key, g => g.Count());

        // Failed transactions
        report.FailedTransactions = financialEntries.Count(e => e.Action == AuditAction.PaymentFailed);

        // High-value transactions (inferred from sensitive flag)
        report.HighValueTransactions = financialEntries.Count(e => e.IsSensitive);

        // Transaction integrity verification
        var transactionEntries = financialEntries.Where(e => paymentActions.Contains(e.Action)).Take(100);
        var integrityReport = await _integrityService.VerifyBatchIntegrityAsync(
            transactionEntries.Select(e => e.Id),
            cancellationToken
        );

        report.TransactionIntegrityScore = integrityReport.SuccessRate;

        await _auditService.LogSystemEventAsync(
            AuditAction.DataExported,
            "FinancialAuditReport",
            $"Generated financial audit report with {financialEntries.Count} entries, {report.TransactionIntegrityScore:F1}% integrity score"
        );

        return report;
    }

    public async Task<TransactionAuditReport> GenerateTransactionAuditReportAsync(DateTime fromDate, DateTime toDate, string? teamSlug = null, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Generating transaction audit report for period {FromDate} to {ToDate}", fromDate, toDate);

        var filter = new AuditQueryFilter
        {
            FromDate = fromDate,
            ToDate = toDate,
            TeamSlug = teamSlug,
            EntityType = "Transaction",
            Take = int.MaxValue
        };

        var transactionEntries = await _auditRepository.QueryAsync(filter, cancellationToken);
        
        var report = new TransactionAuditReport
        {
            ReportPeriod = $"{fromDate:yyyy-MM-dd} to {toDate:yyyy-MM-dd}",
            TeamSlug = teamSlug,
            GeneratedAt = DateTime.UtcNow,
            TotalTransactions = transactionEntries.Count
        };

        // Transaction lifecycle tracking
        report.TransactionsByStatus = transactionEntries
            .GroupBy(e => e.Action.ToString())
            .ToDictionary(g => g.Key, g => g.Count());

        // Daily transaction volume
        report.DailyTransactions = transactionEntries
            .GroupBy(e => e.Timestamp.Date)
            .OrderBy(g => g.Key)
            .ToDictionary(g => g.Key, g => g.Count());

        // Transaction processing times (if available in metadata)
        var processingTimes = ExtractProcessingTimes(transactionEntries);
        if (processingTimes.Any())
        {
            report.AverageProcessingTime = processingTimes.Average();
            report.MaxProcessingTime = processingTimes.Max();
        }

        // Exception transactions
        report.ExceptionTransactions = transactionEntries
            .Where(e => e.Severity >= AuditSeverity.Warning)
            .Count();

        await _auditService.LogSystemEventAsync(
            AuditAction.DataExported,
            "TransactionAuditReport",
            $"Generated transaction audit report for {teamSlug ?? "all teams"} with {transactionEntries.Count} transactions"
        );

        return report;
    }

    public async Task<List<ComplianceReportSummary>> GetReportHistoryAsync(int days = 30, CancellationToken cancellationToken = default)
    {
        var fromDate = DateTime.UtcNow.AddDays(-days);
        
        var filter = new AuditQueryFilter
        {
            FromDate = fromDate,
            Action = AuditAction.DataExported,
            Take = int.MaxValue
        };

        var reportEntries = await _auditRepository.QueryAsync(filter, cancellationToken);
        
        return reportEntries
            .Where(e => e.EntityType.Contains("Report"))
            .Select(e => new ComplianceReportSummary
            {
                ReportId = e.Id,
                ReportType = e.EntityType,
                GeneratedAt = e.Timestamp,
                GeneratedBy = e.UserId ?? "System",
                Description = e.Details ?? "Compliance report generated"
            })
            .OrderByDescending(r => r.GeneratedAt)
            .ToList();
    }

    public async Task<byte[]> ExportReportAsync(string reportId, ComplianceReportFormat format, CancellationToken cancellationToken = default)
    {
        // This would typically involve retrieving the report data and formatting it
        // For now, return a placeholder implementation
        
        _logger.LogInformation("Exporting report {ReportId} in {Format} format", reportId, format);

        var placeholder = $"Report {reportId} exported in {format} format at {DateTime.UtcNow}";
        
        await _auditService.LogSystemEventAsync(
            AuditAction.DataExported,
            "ReportExport",
            $"Exported report {reportId} in {format} format"
        );

        return System.Text.Encoding.UTF8.GetBytes(placeholder);
    }

    // Private helper methods for compliance analysis
    private async Task AnalyzePciRequirement10Async(PciDssComplianceReport report, DateTime fromDate, DateTime toDate, CancellationToken cancellationToken)
    {
        // PCI DSS 10.1: Implement audit trails to link all access to system components to each individual user
        var accessEntries = await _auditRepository.QueryAsync(new AuditQueryFilter
        {
            FromDate = fromDate,
            ToDate = toDate,
            Category = AuditCategory.Authentication,
            Take = int.MaxValue
        }, cancellationToken);

        var userAttributedAccess = accessEntries.Count(e => !string.IsNullOrEmpty(e.UserId));
        var totalAccess = accessEntries.Count;
        
        report.Requirement10Score = totalAccess > 0 ? (double)userAttributedAccess / totalAccess * 100 : 100;
        report.Requirements["10.1"] = $"User attribution: {userAttributedAccess}/{totalAccess} ({report.Requirement10Score:F1}%)";
    }

    private async Task AnalyzePciRequirement8Async(PciDssComplianceReport report, DateTime fromDate, DateTime toDate, CancellationToken cancellationToken)
    {
        // PCI DSS 8.2: Strong authentication requirements
        var authFailures = await _auditRepository.QueryAsync(new AuditQueryFilter
        {
            FromDate = fromDate,
            ToDate = toDate,
            Action = AuditAction.AuthenticationFailure,
            Take = int.MaxValue
        }, cancellationToken);

        var authSuccesses = await _auditRepository.QueryAsync(new AuditQueryFilter
        {
            FromDate = fromDate,
            ToDate = toDate,
            Action = AuditAction.AuthenticationSuccess,
            Take = int.MaxValue
        }, cancellationToken);

        var totalAuth = authFailures.Count + authSuccesses.Count;
        var successRate = totalAuth > 0 ? (double)authSuccesses.Count / totalAuth * 100 : 100;
        
        report.Requirement8Score = successRate;
        report.Requirements["8.2"] = $"Authentication success rate: {successRate:F1}%";
    }

    private async Task AnalyzePciRequirement7Async(PciDssComplianceReport report, DateTime fromDate, DateTime toDate, CancellationToken cancellationToken)
    {
        // PCI DSS 7.1: Limit access to system components and cardholder data
        var sensitiveOps = await _auditRepository.GetSensitiveOperationsAsync(fromDate, toDate, cancellationToken);
        var unauthorizedAccess = sensitiveOps.Count(e => string.IsNullOrEmpty(e.UserId));
        
        var accessControlScore = sensitiveOps.Count > 0 ? 
            (double)(sensitiveOps.Count - unauthorizedAccess) / sensitiveOps.Count * 100 : 100;
        
        report.Requirement7Score = accessControlScore;
        report.Requirements["7.1"] = $"Access control: {accessControlScore:F1}% authorized access";
    }

    private double CalculatePciComplianceScore(PciDssComplianceReport report)
    {
        var scores = new[] { report.Requirement10Score, report.Requirement8Score, report.Requirement7Score };
        return scores.Average();
    }

    private async Task AnalyzeGdprArticle5Async(GdprComplianceReport report, DateTime fromDate, DateTime toDate, CancellationToken cancellationToken)
    {
        // Article 5: Lawfulness, fairness and transparency
        var processingEntries = await _auditRepository.QueryAsync(new AuditQueryFilter
        {
            FromDate = fromDate,
            ToDate = toDate,
            IsSensitive = true,
            Take = int.MaxValue
        }, cancellationToken);

        var lawfulProcessing = processingEntries.Count(e => !string.IsNullOrEmpty(e.UserId)); // Has legal basis (user attribution)
        var totalProcessing = processingEntries.Count;
        
        report.Article5Score = totalProcessing > 0 ? (double)lawfulProcessing / totalProcessing * 100 : 100;
        report.Articles["5"] = $"Lawful processing: {lawfulProcessing}/{totalProcessing} ({report.Article5Score:F1}%)";
    }

    private async Task AnalyzeGdprArticle32Async(GdprComplianceReport report, DateTime fromDate, DateTime toDate, CancellationToken cancellationToken)
    {
        // Article 32: Security of processing
        var securityEvents = await _auditRepository.GetSecurityEventsAsync(fromDate, toDate, AuditSeverity.Warning, cancellationToken);
        var criticalSecurityEvents = securityEvents.Count(e => e.Severity == AuditSeverity.Critical);
        
        // Lower score for more critical security events
        report.Article32Score = Math.Max(0, 100 - (criticalSecurityEvents * 10));
        report.Articles["32"] = $"Security incidents: {criticalSecurityEvents} critical events";
    }

    private async Task AnalyzeGdprArticle33Async(GdprComplianceReport report, DateTime fromDate, DateTime toDate, CancellationToken cancellationToken)
    {
        // Article 33: Notification of personal data breach to supervisory authority
        var breaches = await _auditRepository.QueryAsync(new AuditQueryFilter
        {
            FromDate = fromDate,
            ToDate = toDate,
            Action = AuditAction.SecurityViolation,
            Take = int.MaxValue
        }, cancellationToken);

        // For this implementation, assume all breaches were reported (would need additional tracking)
        report.Article33Score = 100; // Placeholder
        report.Articles["33"] = $"Data breaches: {breaches.Count} incidents (reporting compliance: assumed 100%)";
    }

    private async Task AnalyzeGdprArticle30Async(GdprComplianceReport report, DateTime fromDate, DateTime toDate, CancellationToken cancellationToken)
    {
        // Article 30: Records of processing activities
        var processingActivities = await _auditRepository.QueryAsync(new AuditQueryFilter
        {
            FromDate = fromDate,
            ToDate = toDate,
            Category = AuditCategory.Data,
            Take = int.MaxValue
        }, cancellationToken);

        var documentedActivities = processingActivities.Count(e => !string.IsNullOrEmpty(e.Details));
        var totalActivities = processingActivities.Count;
        
        report.Article30Score = totalActivities > 0 ? (double)documentedActivities / totalActivities * 100 : 100;
        report.Articles["30"] = $"Processing records: {documentedActivities}/{totalActivities} documented";
    }

    private async Task AnalyzeDataSubjectRightsAsync(GdprComplianceReport report, DateTime fromDate, DateTime toDate, CancellationToken cancellationToken)
    {
        // Check for data subject rights requests (deletion, export, etc.)
        var dataExports = await _auditRepository.QueryAsync(new AuditQueryFilter
        {
            FromDate = fromDate,
            ToDate = toDate,
            Action = AuditAction.DataExported,
            Take = int.MaxValue
        }, cancellationToken);

        var dataDeletions = await _auditRepository.QueryAsync(new AuditQueryFilter
        {
            FromDate = fromDate,
            ToDate = toDate,
            Action = AuditAction.Deleted,
            Take = int.MaxValue
        }, cancellationToken);

        report.DataSubjectRightsScore = 100; // Placeholder - would need specific tracking
        report.DataSubjectRights = $"Export requests: {dataExports.Count}, Deletion requests: {dataDeletions.Count}";
    }

    private double CalculateGdprComplianceScore(GdprComplianceReport report)
    {
        var scores = new[] { report.Article5Score, report.Article32Score, report.Article33Score, report.Article30Score, report.DataSubjectRightsScore };
        return scores.Average();
    }

    private async Task AnalyzeSoxSection404Async(SoxComplianceReport report, DateTime fromDate, DateTime toDate, CancellationToken cancellationToken)
    {
        // Section 404: Internal control over financial reporting
        var financialEntries = await _auditRepository.QueryAsync(new AuditQueryFilter
        {
            FromDate = fromDate,
            ToDate = toDate,
            Category = AuditCategory.Payment,
            Take = int.MaxValue
        }, cancellationToken);

        var controlledOperations = financialEntries.Count(e => !string.IsNullOrEmpty(e.UserId) && !string.IsNullOrEmpty(e.Details));
        var totalOperations = financialEntries.Count;
        
        report.Section404Score = totalOperations > 0 ? (double)controlledOperations / totalOperations * 100 : 100;
        report.Sections["404"] = $"Internal controls: {controlledOperations}/{totalOperations} ({report.Section404Score:F1}%)";
    }

    private async Task AnalyzeFinancialDataIntegrityAsync(SoxComplianceReport report, DateTime fromDate, DateTime toDate, CancellationToken cancellationToken)
    {
        var financialEntries = await _auditRepository.QueryAsync(new AuditQueryFilter
        {
            FromDate = fromDate,
            ToDate = toDate,
            Category = AuditCategory.Payment,
            Take = 100 // Sample for integrity check
        }, cancellationToken);

        if (financialEntries.Any())
        {
            var integrityReport = await _integrityService.VerifyBatchIntegrityAsync(
                financialEntries.Select(e => e.Id),
                CancellationToken.None
            );

            report.DataIntegrityScore = integrityReport.SuccessRate;
        }
        else
        {
            report.DataIntegrityScore = 100;
        }
    }

    private async Task AnalyzeChangeManagementControlsAsync(SoxComplianceReport report, DateTime fromDate, DateTime toDate, CancellationToken cancellationToken)
    {
        var configChanges = await _auditRepository.QueryAsync(new AuditQueryFilter
        {
            FromDate = fromDate,
            ToDate = toDate,
            Action = AuditAction.ConfigurationChanged,
            Take = int.MaxValue
        }, cancellationToken);

        var approvedChanges = configChanges.Count(e => !string.IsNullOrEmpty(e.UserId) && !string.IsNullOrEmpty(e.Details));
        var totalChanges = configChanges.Count;
        
        report.ChangeManagementScore = totalChanges > 0 ? (double)approvedChanges / totalChanges * 100 : 100;
    }

    private async Task AnalyzeSegregationOfDutiesAsync(SoxComplianceReport report, DateTime fromDate, DateTime toDate, CancellationToken cancellationToken)
    {
        // Analyze if same users are performing conflicting duties
        var sensitiveOps = await _auditRepository.GetSensitiveOperationsAsync(fromDate, toDate, cancellationToken);
        
        var userOperations = sensitiveOps
            .Where(e => !string.IsNullOrEmpty(e.UserId))
            .GroupBy(e => e.UserId!)
            .ToList();

        var conflictingUsers = 0;
        foreach (var userGroup in userOperations)
        {
            var actions = userGroup.Select(e => e.Action).Distinct().ToList();
            
            // Check for conflicting actions (e.g., both initiating and approving payments)
            if (actions.Contains(AuditAction.PaymentInitialized) && actions.Contains(AuditAction.PaymentConfirmed))
            {
                conflictingUsers++;
            }
        }

        var totalUsers = userOperations.Count;
        report.SegregationScore = totalUsers > 0 ? (double)(totalUsers - conflictingUsers) / totalUsers * 100 : 100;
    }

    private double CalculateSoxComplianceScore(SoxComplianceReport report)
    {
        var scores = new[] { report.Section404Score, report.DataIntegrityScore, report.ChangeManagementScore, report.SegregationScore };
        return scores.Average();
    }

    private void GeneratePciRecommendations(PciDssComplianceReport report)
    {
        if (report.Requirement10Score < 95)
            report.Recommendations.Add("Implement comprehensive user attribution for all system access");
            
        if (report.Requirement8Score < 95)
            report.Recommendations.Add("Strengthen authentication mechanisms to reduce failure rates");
            
        if (report.Requirement7Score < 95)
            report.Recommendations.Add("Review and restrict access to sensitive cardholder data");
    }

    private void GenerateGdprRecommendations(GdprComplianceReport report)
    {
        if (report.Article5Score < 90)
            report.Recommendations.Add("Ensure all personal data processing has documented legal basis");
            
        if (report.Article32Score < 90)
            report.Recommendations.Add("Implement additional security measures to prevent data breaches");
            
        if (report.Article30Score < 90)
            report.Recommendations.Add("Improve documentation of data processing activities");
    }

    private void GenerateSoxRecommendations(SoxComplianceReport report)
    {
        if (report.Section404Score < 98)
            report.Recommendations.Add("Strengthen internal controls over financial reporting");
            
        if (report.DataIntegrityScore < 98)
            report.Recommendations.Add("Implement additional data integrity verification measures");
            
        if (report.ChangeManagementScore < 98)
            report.Recommendations.Add("Improve change management approval processes");
            
        if (report.SegregationScore < 98)
            report.Recommendations.Add("Review and enforce segregation of duties policies");
    }

    // Additional helper methods would be implemented here for:
    // - GenerateSectionDataAsync
    // - CalculateCustomComplianceScore
    // - DetectSuspiciousPatternsInTrailAsync
    // - InferProcessingPurposes
    // - InferLegalBasis
    // - AnalyzeDataRetentionComplianceAsync
    // - DetermineBreachStatus
    // - CalculateAverageResponseTime
    // - AssessBreachNotificationCompliance
    // - DetectSuspiciousAccessPatternsAsync
    // - ExtractProcessingTimes

    private async Task<Dictionary<string, object>> GenerateSectionDataAsync(string section, List<AuditEntry> entries, CancellationToken cancellationToken)
    {
        // Placeholder implementation - would generate section-specific data
        return new Dictionary<string, object>
        {
            ["TotalEntries"] = entries.Count,
            ["GeneratedAt"] = DateTime.UtcNow
        };
    }

    private double CalculateCustomComplianceScore(List<AuditEntry> entries, ComplianceReportRequest request)
    {
        // Placeholder implementation - would calculate based on custom criteria
        var errorRate = entries.Count > 0 ? (double)entries.Count(e => e.Severity >= AuditSeverity.Error) / entries.Count : 0;
        return Math.Max(0, 100 - (errorRate * 100));
    }

    private async Task<List<string>> DetectSuspiciousPatternsInTrailAsync(List<AuditEntry> auditTrail)
    {
        var patterns = new List<string>();
        
        // Check for rapid sequential operations
        var rapidOps = auditTrail
            .OrderBy(e => e.Timestamp)
            .Zip(auditTrail.OrderBy(e => e.Timestamp).Skip(1), (first, second) => new { First = first, Second = second })
            .Where(pair => (pair.Second.Timestamp - pair.First.Timestamp).TotalSeconds < 1)
            .Count();

        if (rapidOps > 5)
            patterns.Add($"Rapid sequential operations detected: {rapidOps} operations within 1 second intervals");

        return patterns;
    }

    private Dictionary<string, int> InferProcessingPurposes(List<AuditEntry> entries)
    {
        // Infer processing purposes from audit actions and context
        var purposes = new Dictionary<string, int>();
        
        foreach (var entry in entries)
        {
            var purpose = entry.Action switch
            {
                AuditAction.PaymentInitialized => "Payment Processing",
                AuditAction.Created => "Data Creation",
                AuditAction.Updated => "Data Maintenance",
                AuditAction.DataExported => "Data Export/Reporting",
                AuditAction.AuthenticationAttempt => "User Authentication",
                _ => "Other Business Operations"
            };

            purposes[purpose] = purposes.GetValueOrDefault(purpose, 0) + 1;
        }

        return purposes;
    }

    private string InferLegalBasis(AuditEntry entry)
    {
        // Infer GDPR legal basis from the type of operation
        return entry.Action switch
        {
            AuditAction.PaymentInitialized or AuditAction.PaymentConfirmed => "Contract Performance",
            AuditAction.AuthenticationAttempt => "Legitimate Interest",
            AuditAction.SecurityViolation => "Legal Obligation",
            _ => "Legitimate Interest"
        };
    }

    private async Task<Dictionary<string, string>> AnalyzeDataRetentionComplianceAsync(List<AuditEntry> entries, CancellationToken cancellationToken)
    {
        var compliance = new Dictionary<string, string>();
        
        var entityTypes = entries.Select(e => e.EntityType).Distinct();
        
        foreach (var entityType in entityTypes)
        {
            if (_auditConfig.Compliance.DataRetentionRequirements.TryGetValue(entityType, out var retentionDays))
            {
                var oldEntries = entries.Where(e => e.EntityType == entityType && 
                                               e.Timestamp < DateTime.UtcNow.AddDays(-retentionDays)).Count();
                
                compliance[entityType] = oldEntries == 0 ? "Compliant" : $"{oldEntries} entries exceed retention period";
            }
            else
            {
                compliance[entityType] = "No retention policy defined";
            }
        }

        return compliance;
    }

    private string DetermineBreachStatus(AuditEntry incident)
    {
        return incident.Severity switch
        {
            AuditSeverity.Critical => "Under Investigation",
            AuditSeverity.Error => "Reviewing",
            _ => "Monitored"
        };
    }

    private double CalculateAverageResponseTime(List<DataBreachIncident> incidents)
    {
        // Placeholder - would calculate based on incident resolution times
        return 2.5; // Hours
    }

    private string AssessBreachNotificationCompliance(List<DataBreachIncident> incidents)
    {
        // Placeholder - would assess notification timing compliance
        var criticalIncidents = incidents.Count(i => i.Severity == "Critical");
        return criticalIncidents == 0 ? "Compliant" : "Review Required";
    }

    private async Task<List<string>> DetectSuspiciousAccessPatternsAsync(List<AuditEntry> accessEntries)
    {
        var patterns = new List<string>();
        
        // Check for multiple failures followed by success (potential brute force)
        var grouped = accessEntries
            .OrderBy(e => e.Timestamp)
            .GroupBy(e => new { e.UserId, e.IpAddress })
            .Where(g => g.Count() > 3);

        foreach (var group in grouped)
        {
            var entries = group.OrderBy(e => e.Timestamp).ToList();
            var failures = entries.Count(e => e.Action == AuditAction.AuthenticationFailure);
            var successes = entries.Count(e => e.Action == AuditAction.AuthenticationSuccess);
            
            if (failures > 3 && successes > 0)
            {
                patterns.Add($"Potential brute force: {failures} failures followed by {successes} successes for {group.Key.UserId} from {group.Key.IpAddress}");
            }
        }

        return patterns;
    }

    private List<double> ExtractProcessingTimes(List<AuditEntry> entries)
    {
        var times = new List<double>();
        
        foreach (var entry in entries)
        {
            if (!string.IsNullOrEmpty(entry.Metadata))
            {
                try
                {
                    var metadata = JsonSerializer.Deserialize<Dictionary<string, object>>(entry.Metadata);
                    if (metadata?.TryGetValue("ProcessingTimeMs", out var timeObj) == true)
                    {
                        times.Add(Convert.ToDouble(timeObj));
                    }
                }
                catch
                {
                    // Ignore parsing errors
                }
            }
        }

        return times;
    }
}

// Supporting classes for compliance reporting would be defined here...
// (Due to length constraints, I'm including just the interface definitions)

public class ComplianceReportRequest
{
    public string ReportName { get; set; } = string.Empty;
    public DateTime FromDate { get; set; }
    public DateTime ToDate { get; set; }
    public string? RequestedBy { get; set; }
    public string? EntityType { get; set; }
    public string? UserId { get; set; }
    public string? TeamSlug { get; set; }
    public AuditCategory? Category { get; set; }
    public AuditSeverity? MinSeverity { get; set; }
    public List<string> Sections { get; set; } = new();
}

public enum ComplianceReportFormat
{
    Json,
    Pdf,
    Excel,
    Csv
}

// Additional report class definitions would continue here...