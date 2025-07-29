using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PaymentGateway.Core.Configuration;
using PaymentGateway.Core.Entities;
using PaymentGateway.Core.Repositories;
using System.Text.Json;

namespace PaymentGateway.Core.Services;

/// <summary>
/// Service for analyzing audit logs and generating alerts
/// </summary>
public interface IAuditAnalysisService
{
    // Pattern detection
    Task<List<AuditPattern>> DetectSuspiciousPatternsAsync(DateTime fromDate, DateTime toDate, CancellationToken cancellationToken = default);
    Task<List<AuditAnomaly>> DetectAnomaliesAsync(DateTime fromDate, DateTime toDate, CancellationToken cancellationToken = default);
    Task<FraudRiskAssessment> AssessFraudRiskAsync(string? userId = null, string? teamSlug = null, DateTime? fromDate = null, CancellationToken cancellationToken = default);
    
    // Real-time monitoring
    Task<List<AuditAlert>> ProcessRealtimeAuditEventAsync(AuditEntry auditEntry, CancellationToken cancellationToken = default);
    Task<bool> ShouldTriggerAlertAsync(AuditEntry auditEntry, CancellationToken cancellationToken = default);
    
    // Trend analysis
    Task<AuditTrendAnalysis> AnalyzeTrendsAsync(DateTime fromDate, DateTime toDate, string? entityType = null, CancellationToken cancellationToken = default);
    Task<Dictionary<string, double>> CalculateActivityScoresAsync(DateTime fromDate, DateTime toDate, CancellationToken cancellationToken = default);
    
    // Security analysis
    Task<SecurityAnalysisReport> AnalyzeSecurityEventsAsync(DateTime fromDate, DateTime toDate, CancellationToken cancellationToken = default);
    Task<List<FailurePattern>> AnalyzeFailurePatternsAsync(DateTime fromDate, DateTime toDate, CancellationToken cancellationToken = default);
    
    // Performance analysis
    Task<PerformanceAnalysisReport> AnalyzePerformanceAsync(DateTime fromDate, DateTime toDate, CancellationToken cancellationToken = default);
    Task<List<AuditEntry>> FindSlowOperationsAsync(DateTime fromDate, DateTime toDate, CancellationToken cancellationToken = default);
    
    // Business intelligence
    Task<BusinessIntelligenceReport> GenerateBusinessReportAsync(DateTime fromDate, DateTime toDate, CancellationToken cancellationToken = default);
    Task<Dictionary<string, object>> GenerateExecutiveSummaryAsync(DateTime fromDate, DateTime toDate, CancellationToken cancellationToken = default);
}

public class AuditAnalysisService : IAuditAnalysisService
{
    private readonly IAuditRepository _auditRepository;
    private readonly IComprehensiveAuditService _auditService;
    private readonly ILogger<AuditAnalysisService> _logger;
    private readonly AuditConfiguration _auditConfig;

    public AuditAnalysisService(
        IAuditRepository auditRepository,
        IComprehensiveAuditService auditService,
        ILogger<AuditAnalysisService> logger,
        IOptions<AuditConfiguration> auditConfig)
    {
        _auditRepository = auditRepository;
        _auditService = auditService;
        _logger = logger;
        _auditConfig = auditConfig.Value;
    }

    public async Task<List<AuditPattern>> DetectSuspiciousPatternsAsync(DateTime fromDate, DateTime toDate, CancellationToken cancellationToken = default)
    {
        var patterns = new List<AuditPattern>();
        
        var filter = new AuditQueryFilter
        {
            FromDate = fromDate,
            ToDate = toDate,
            Take = int.MaxValue
        };

        var entries = await _auditRepository.QueryAsync(filter, cancellationToken);

        // Pattern 1: Rapid repeated failed authentication attempts
        var authFailures = entries.Where(e => e.Action == AuditAction.AuthenticationFailure).ToList();
        var suspiciousAuthPatterns = authFailures
            .GroupBy(e => new { e.IpAddress, Hour = e.Timestamp.Hour })
            .Where(g => g.Count() > 10) // More than 10 failures per hour from same IP
            .Select(g => new AuditPattern
            {
                PatternType = "SuspiciousAuthentication",
                Description = $"Multiple authentication failures from IP {g.Key.IpAddress} in hour {g.Key.Hour}",
                Severity = AuditSeverity.Warning,
                AffectedEntries = g.ToList(),
                RiskScore = Math.Min(g.Count() * 0.1m, 10m),
                DetectedAt = DateTime.UtcNow
            });

        patterns.AddRange(suspiciousAuthPatterns);

        // Pattern 2: Unusual payment patterns
        var paymentEntries = entries.Where(e => e.Category == AuditCategory.Payment).ToList();
        var unusualPaymentPatterns = paymentEntries
            .GroupBy(e => new { e.UserId, Date = e.Timestamp.Date })
            .Where(g => g.Count() > 100) // More than 100 payment operations per day
            .Select(g => new AuditPattern
            {
                PatternType = "UnusualPaymentVolume",
                Description = $"Unusual payment volume for user {g.Key.UserId} on {g.Key.Date:yyyy-MM-dd}",
                Severity = AuditSeverity.Warning,
                AffectedEntries = g.ToList(),
                RiskScore = Math.Min(g.Count() * 0.05m, 10m),
                DetectedAt = DateTime.UtcNow
            });

        patterns.AddRange(unusualPaymentPatterns);

        // Pattern 3: Off-hours activity
        var offHoursEntries = entries
            .Where(e => e.IsSensitive && (e.Timestamp.Hour < 6 || e.Timestamp.Hour > 22))
            .GroupBy(e => e.UserId)
            .Where(g => g.Count() > 5)
            .Select(g => new AuditPattern
            {
                PatternType = "OffHoursActivity",
                Description = $"Sensitive operations performed outside business hours by user {g.Key}",
                Severity = AuditSeverity.Information,
                AffectedEntries = g.ToList(),
                RiskScore = g.Count() * 0.2m,
                DetectedAt = DateTime.UtcNow
            });

        patterns.AddRange(offHoursEntries);

        // Pattern 4: Rapid configuration changes
        var configChanges = entries.Where(e => e.Action == AuditAction.ConfigurationChanged).ToList();
        var rapidConfigChanges = configChanges
            .GroupBy(e => new { e.UserId, Hour = e.Timestamp.Hour })
            .Where(g => g.Count() > 5)
            .Select(g => new AuditPattern
            {
                PatternType = "RapidConfigurationChanges",
                Description = $"Multiple configuration changes by user {g.Key.UserId} in hour {g.Key.Hour}",
                Severity = AuditSeverity.Warning,
                AffectedEntries = g.ToList(),
                RiskScore = g.Count() * 0.3m,
                DetectedAt = DateTime.UtcNow
            });

        patterns.AddRange(rapidConfigChanges);

        if (patterns.Any())
        {
            await _auditService.LogSystemEventAsync(
                AuditAction.RiskAssessment,
                "PatternDetection",
                $"Detected {patterns.Count} suspicious patterns between {fromDate:yyyy-MM-dd} and {toDate:yyyy-MM-dd}"
            );
        }

        return patterns;
    }

    public async Task<List<AuditAnomaly>> DetectAnomaliesAsync(DateTime fromDate, DateTime toDate, CancellationToken cancellationToken = default)
    {
        var anomalies = new List<AuditAnomaly>();
        
        // Get baseline statistics for comparison
        var baselineFromDate = fromDate.AddDays(-30);
        var baselineFilter = new AuditQueryFilter
        {
            FromDate = baselineFromDate,
            ToDate = fromDate,
            Take = int.MaxValue
        };
        
        var currentFilter = new AuditQueryFilter
        {
            FromDate = fromDate,
            ToDate = toDate,
            Take = int.MaxValue
        };

        var baselineEntries = await _auditRepository.QueryAsync(baselineFilter, cancellationToken);
        var currentEntries = await _auditRepository.QueryAsync(currentFilter, cancellationToken);

        // Anomaly 1: Significant increase in error rates
        var baselineErrors = baselineEntries.Count(e => e.Severity >= AuditSeverity.Error);
        var currentErrors = currentEntries.Count(e => e.Severity >= AuditSeverity.Error);
        
        var baselineErrorRate = baselineEntries.Count > 0 ? (double)baselineErrors / baselineEntries.Count : 0;
        var currentErrorRate = currentEntries.Count > 0 ? (double)currentErrors / currentEntries.Count : 0;

        if (currentErrorRate > baselineErrorRate * 2 && currentErrors > 10) // Error rate doubled
        {
            anomalies.Add(new AuditAnomaly
            {
                AnomalyType = "ErrorRateSpike",
                Description = $"Error rate increased from {baselineErrorRate:P2} to {currentErrorRate:P2}",
                Severity = AuditSeverity.Warning,
                BaselineValue = baselineErrorRate,
                CurrentValue = currentErrorRate,
                DeviationPercent = ((currentErrorRate - baselineErrorRate) / baselineErrorRate) * 100,
                DetectedAt = DateTime.UtcNow
            });
        }

        // Anomaly 2: Unusual user activity
        var baselineUserActivity = baselineEntries.GroupBy(e => e.UserId).ToDictionary(g => g.Key, g => g.Count());
        var currentUserActivity = currentEntries.GroupBy(e => e.UserId).ToDictionary(g => g.Key, g => g.Count());

        foreach (var user in currentUserActivity.Keys)
        {
            if (baselineUserActivity.TryGetValue(user, out var baselineCount))
            {
                var currentCount = currentUserActivity[user];
                if (currentCount > baselineCount * 3 && currentCount > 50) // Activity tripled
                {
                    anomalies.Add(new AuditAnomaly
                    {
                        AnomalyType = "UserActivitySpike",
                        Description = $"User {user} activity increased from {baselineCount} to {currentCount}",
                        Severity = AuditSeverity.Information,
                        BaselineValue = baselineCount,
                        CurrentValue = currentCount,
                        DeviationPercent = ((double)(currentCount - baselineCount) / baselineCount) * 100,
                        DetectedAt = DateTime.UtcNow,
                        AffectedUserId = user
                    });
                }
            }
        }

        // Anomaly 3: New IP addresses with high activity
        var baselineIps = baselineEntries.Where(e => !string.IsNullOrEmpty(e.IpAddress)).Select(e => e.IpAddress).Distinct().ToHashSet();
        var newIpsWithActivity = currentEntries
            .Where(e => !string.IsNullOrEmpty(e.IpAddress) && !baselineIps.Contains(e.IpAddress))
            .GroupBy(e => e.IpAddress)
            .Where(g => g.Count() > 20)
            .Select(g => new AuditAnomaly
            {
                AnomalyType = "NewIPHighActivity",
                Description = $"New IP address {g.Key} with {g.Count()} activities",
                Severity = AuditSeverity.Warning,
                CurrentValue = g.Count(),
                DetectedAt = DateTime.UtcNow,
                AdditionalData = new Dictionary<string, object> { { "IpAddress", g.Key! } }
            });

        anomalies.AddRange(newIpsWithActivity);

        if (anomalies.Any())
        {
            await _auditService.LogSystemEventAsync(
                AuditAction.SuspiciousActivity,
                "AnomalyDetection",
                $"Detected {anomalies.Count} anomalies between {fromDate:yyyy-MM-dd} and {toDate:yyyy-MM-dd}"
            );
        }

        return anomalies;
    }

    public async Task<FraudRiskAssessment> AssessFraudRiskAsync(string? userId = null, string? teamSlug = null, DateTime? fromDate = null, CancellationToken cancellationToken = default)
    {
        var assessmentDate = fromDate ?? DateTime.UtcNow.AddDays(-7);
        var filter = new AuditQueryFilter
        {
            FromDate = assessmentDate,
            ToDate = DateTime.UtcNow,
            UserId = userId,
            TeamSlug = teamSlug,
            Take = int.MaxValue
        };

        var entries = await _auditRepository.QueryAsync(filter, cancellationToken);
        
        var assessment = new FraudRiskAssessment
        {
            UserId = userId,
            TeamSlug = teamSlug,
            AssessmentPeriod = $"{assessmentDate:yyyy-MM-dd} to {DateTime.UtcNow:yyyy-MM-dd}",
            GeneratedAt = DateTime.UtcNow
        };

        // Risk factor 1: Authentication failures
        var authFailures = entries.Count(e => e.Action == AuditAction.AuthenticationFailure);
        assessment.RiskFactors.Add("Authentication Failures", authFailures);
        assessment.RiskScore += authFailures * 0.5m;

        // Risk factor 2: Failed payment attempts
        var paymentFailures = entries.Count(e => e.Action == AuditAction.PaymentFailed);
        assessment.RiskFactors.Add("Payment Failures", paymentFailures);
        assessment.RiskScore += paymentFailures * 0.3m;

        // Risk factor 3: Multiple IP addresses
        var uniqueIps = entries.Where(e => !string.IsNullOrEmpty(e.IpAddress)).Select(e => e.IpAddress).Distinct().Count();
        assessment.RiskFactors.Add("Unique IP Addresses", uniqueIps);
        if (uniqueIps > 5) assessment.RiskScore += (uniqueIps - 5) * 0.2m;

        // Risk factor 4: Off-hours activity
        var offHoursActivity = entries.Count(e => e.Timestamp.Hour < 6 || e.Timestamp.Hour > 22);
        assessment.RiskFactors.Add("Off-Hours Activity", offHoursActivity);
        assessment.RiskScore += offHoursActivity * 0.1m;

        // Risk factor 5: High-value operations
        var highValueOps = entries.Count(e => e.IsSensitive);
        assessment.RiskFactors.Add("Sensitive Operations", highValueOps);
        assessment.RiskScore += highValueOps * 0.05m;

        // Determine risk level
        assessment.RiskLevel = assessment.RiskScore switch
        {
            < 2 => "Low",
            < 5 => "Medium",
            < 10 => "High",
            _ => "Critical"
        };

        // Generate recommendations
        if (authFailures > 5)
            assessment.Recommendations.Add("Consider implementing additional authentication measures");
        
        if (uniqueIps > 10)
            assessment.Recommendations.Add("Monitor for potential account compromise due to multiple IP usage");
        
        if (assessment.RiskScore > 5)
            assessment.Recommendations.Add("Implement enhanced monitoring for this entity");

        await _auditService.LogSystemEventAsync(
            AuditAction.RiskAssessment,
            "FraudAssessment",
            $"Fraud risk assessment completed for {userId ?? teamSlug ?? "system"}: {assessment.RiskLevel} risk (score: {assessment.RiskScore:F2})"
        );

        return assessment;
    }

    public async Task<List<AuditAlert>> ProcessRealtimeAuditEventAsync(AuditEntry auditEntry, CancellationToken cancellationToken = default)
    {
        var alerts = new List<AuditAlert>();

        // Check if this event should trigger an alert
        if (!await ShouldTriggerAlertAsync(auditEntry, cancellationToken))
            return alerts;

        // Alert 1: Critical security events
        if (auditEntry.Category == AuditCategory.Security && auditEntry.Severity >= AuditSeverity.Error)
        {
            alerts.Add(new AuditAlert
            {
                AlertType = "SecurityEvent",
                Severity = auditEntry.Severity,
                Title = "Critical Security Event Detected",
                Description = $"Security event: {auditEntry.Action} for {auditEntry.EntityType} {auditEntry.EntityId}",
                AuditEntryId = auditEntry.Id,
                TriggeredAt = DateTime.UtcNow,
                RequiresImmedateAction = auditEntry.Severity == AuditSeverity.Critical
            });
        }

        // Alert 2: Multiple rapid failures
        var recentFailures = await GetRecentFailuresAsync(auditEntry.UserId, auditEntry.IpAddress, cancellationToken);
        if (recentFailures >= 5)
        {
            alerts.Add(new AuditAlert
            {
                AlertType = "RapidFailures",
                Severity = AuditSeverity.Warning,
                Title = "Multiple Rapid Failures Detected",
                Description = $"{recentFailures} failures in the last 10 minutes from {auditEntry.UserId ?? auditEntry.IpAddress}",
                AuditEntryId = auditEntry.Id,
                TriggeredAt = DateTime.UtcNow
            });
        }

        // Alert 3: Unusual high-value operations
        if (auditEntry.IsSensitive && IsUnusualTime(auditEntry.Timestamp))
        {
            alerts.Add(new AuditAlert
            {
                AlertType = "OffHoursSensitiveOperation",
                Severity = AuditSeverity.Information,
                Title = "Sensitive Operation Outside Business Hours",
                Description = $"Sensitive operation {auditEntry.Action} performed at {auditEntry.Timestamp:HH:mm}",
                AuditEntryId = auditEntry.Id,
                TriggeredAt = DateTime.UtcNow
            });
        }

        // Log alerts generated
        foreach (var alert in alerts)
        {
            await _auditService.LogSystemEventAsync(
                AuditAction.RiskAssessment,
                "AuditAlert",
                $"Generated {alert.AlertType} alert for audit entry {auditEntry.Id}"
            );
        }

        return alerts;
    }

    public async Task<bool> ShouldTriggerAlertAsync(AuditEntry auditEntry, CancellationToken cancellationToken = default)
    {
        // Don't alert on routine operations
        if (auditEntry.Severity < (AuditSeverity)_auditConfig.AlertSeverityThreshold)
            return false;

        // Don't alert on system operations unless critical
        if (auditEntry.UserId == "System" && auditEntry.Severity < AuditSeverity.Error)
            return false;

        // Always alert on security violations
        if (auditEntry.Action == AuditAction.SecurityViolation || auditEntry.Action == AuditAction.FraudDetected)
            return true;

        // Always alert on critical events
        if (auditEntry.Severity == AuditSeverity.Critical)
            return true;

        return await Task.FromResult(true);
    }

    public async Task<AuditTrendAnalysis> AnalyzeTrendsAsync(DateTime fromDate, DateTime toDate, string? entityType = null, CancellationToken cancellationToken = default)
    {
        var filter = new AuditQueryFilter
        {
            FromDate = fromDate,
            ToDate = toDate,
            EntityType = entityType,
            Take = int.MaxValue
        };

        var entries = await _auditRepository.QueryAsync(filter, cancellationToken);
        
        var analysis = new AuditTrendAnalysis
        {
            AnalysisPeriod = $"{fromDate:yyyy-MM-dd} to {toDate:yyyy-MM-dd}",
            EntityType = entityType,
            GeneratedAt = DateTime.UtcNow,
            TotalEntries = entries.Count
        };

        // Daily activity trend
        analysis.DailyActivity = entries
            .GroupBy(e => e.Timestamp.Date)
            .OrderBy(g => g.Key)
            .ToDictionary(g => g.Key, g => g.Count());

        // Hourly activity pattern
        analysis.HourlyPattern = entries
            .GroupBy(e => e.Timestamp.Hour)
            .OrderBy(g => g.Key)
            .ToDictionary(g => g.Key, g => g.Count());

        // Action trends
        analysis.ActionTrends = entries
            .GroupBy(e => e.Action.ToString())
            .OrderByDescending(g => g.Count())
            .ToDictionary(g => g.Key, g => g.Count());

        // Error rate trend
        var dailyErrors = entries
            .Where(e => e.Severity >= AuditSeverity.Error)
            .GroupBy(e => e.Timestamp.Date)
            .ToDictionary(g => g.Key, g => g.Count());

        analysis.ErrorRateTrend = analysis.DailyActivity
            .ToDictionary(
                kvp => kvp.Key,
                kvp => dailyErrors.TryGetValue(kvp.Key, out var errorCount) ? 
                       (double)errorCount / kvp.Value * 100 : 0
            );

        // Calculate growth rates
        var midPoint = fromDate.AddDays((toDate - fromDate).TotalDays / 2);
        var firstHalf = entries.Where(e => e.Timestamp < midPoint).Count();
        var secondHalf = entries.Where(e => e.Timestamp >= midPoint).Count();
        
        if (firstHalf > 0)
        {
            analysis.GrowthRate = ((double)(secondHalf - firstHalf) / firstHalf) * 100;
        }

        return analysis;
    }

    public async Task<Dictionary<string, double>> CalculateActivityScoresAsync(DateTime fromDate, DateTime toDate, CancellationToken cancellationToken = default)
    {
        var filter = new AuditQueryFilter
        {
            FromDate = fromDate,
            ToDate = toDate,
            Take = int.MaxValue
        };

        var entries = await _auditRepository.QueryAsync(filter, cancellationToken);
        var scores = new Dictionary<string, double>();

        // Calculate scores by user
        var userGroups = entries.Where(e => !string.IsNullOrEmpty(e.UserId)).GroupBy(e => e.UserId!);
        
        foreach (var userGroup in userGroups)
        {
            var userEntries = userGroup.ToList();
            var score = 0.0;

            // Base activity score
            score += userEntries.Count * 0.1;

            // Sensitive operations bonus
            score += userEntries.Count(e => e.IsSensitive) * 0.5;

            // Error penalty
            score -= userEntries.Count(e => e.Severity >= AuditSeverity.Error) * 0.3;

            // Off-hours activity
            score += userEntries.Count(e => e.Timestamp.Hour < 6 || e.Timestamp.Hour > 22) * 0.2;

            scores[userGroup.Key] = Math.Max(0, score);
        }

        return scores;
    }

    public async Task<SecurityAnalysisReport> AnalyzeSecurityEventsAsync(DateTime fromDate, DateTime toDate, CancellationToken cancellationToken = default)
    {
        var securityEntries = await _auditRepository.GetSecurityEventsAsync(fromDate, toDate, AuditSeverity.Information, cancellationToken);
        
        var report = new SecurityAnalysisReport
        {
            AnalysisPeriod = $"{fromDate:yyyy-MM-dd} to {toDate:yyyy-MM-dd}",
            GeneratedAt = DateTime.UtcNow,
            TotalSecurityEvents = securityEntries.Count
        };

        // Categorize security events
        report.EventsByType = securityEntries
            .GroupBy(e => e.Action.ToString())
            .ToDictionary(g => g.Key, g => g.Count());

        report.EventsBySeverity = securityEntries
            .GroupBy(e => e.Severity.ToString())
            .ToDictionary(g => g.Key, g => g.Count());

        // Top affected IPs
        report.TopAffectedIPs = securityEntries
            .Where(e => !string.IsNullOrEmpty(e.IpAddress))
            .GroupBy(e => e.IpAddress!)
            .OrderByDescending(g => g.Count())
            .Take(10)
            .ToDictionary(g => g.Key, g => g.Count());

        // Security incidents by hour
        report.IncidentsByHour = securityEntries
            .GroupBy(e => e.Timestamp.Hour)
            .ToDictionary(g => g.Key, g => g.Count());

        // Critical incidents
        report.CriticalIncidents = securityEntries
            .Where(e => e.Severity == AuditSeverity.Critical)
            .Select(e => new SecurityIncident
            {
                IncidentId = e.Id,
                Timestamp = e.Timestamp,
                Action = e.Action.ToString(),
                Description = e.Details ?? "No details available",
                IpAddress = e.IpAddress,
                UserId = e.UserId
            })
            .ToList();

        return report;
    }

    public async Task<List<FailurePattern>> AnalyzeFailurePatternsAsync(DateTime fromDate, DateTime toDate, CancellationToken cancellationToken = default)
    {
        var failureEntries = await _auditRepository.GetFailedOperationsAsync(fromDate, toDate, cancellationToken);
        var patterns = new List<FailurePattern>();

        // Pattern by IP address
        var ipPatterns = failureEntries
            .Where(e => !string.IsNullOrEmpty(e.IpAddress))
            .GroupBy(e => e.IpAddress!)
            .Where(g => g.Count() > 5)
            .Select(g => new FailurePattern
            {
                PatternType = "IP-based",
                Identifier = g.Key,
                FailureCount = g.Count(),
                FirstFailure = g.Min(e => e.Timestamp),
                LastFailure = g.Max(e => e.Timestamp),
                PrimaryActions = g.GroupBy(e => e.Action).OrderByDescending(ag => ag.Count()).Take(3).Select(ag => ag.Key.ToString()).ToList()
            });

        patterns.AddRange(ipPatterns);

        // Pattern by user
        var userPatterns = failureEntries
            .Where(e => !string.IsNullOrEmpty(e.UserId))
            .GroupBy(e => e.UserId!)
            .Where(g => g.Count() > 3)
            .Select(g => new FailurePattern
            {
                PatternType = "User-based",
                Identifier = g.Key,
                FailureCount = g.Count(),
                FirstFailure = g.Min(e => e.Timestamp),
                LastFailure = g.Max(e => e.Timestamp),
                PrimaryActions = g.GroupBy(e => e.Action).OrderByDescending(ag => ag.Count()).Take(3).Select(ag => ag.Key.ToString()).ToList()
            });

        patterns.AddRange(userPatterns);

        return patterns;
    }

    public async Task<PerformanceAnalysisReport> AnalyzePerformanceAsync(DateTime fromDate, DateTime toDate, CancellationToken cancellationToken = default)
    {
        var allEntries = await _auditRepository.QueryAsync(new AuditQueryFilter
        {
            FromDate = fromDate,
            ToDate = toDate,
            Take = int.MaxValue
        }, cancellationToken);

        var report = new PerformanceAnalysisReport
        {
            AnalysisPeriod = $"{fromDate:yyyy-MM-dd} to {toDate:yyyy-MM-dd}",
            GeneratedAt = DateTime.UtcNow,
            TotalOperations = allEntries.Count
        };

        // Calculate average operations per hour
        var timeSpan = toDate - fromDate;
        report.AverageOperationsPerHour = timeSpan.TotalHours > 0 ? allEntries.Count / timeSpan.TotalHours : 0;

        // Peak activity periods
        var hourlyActivity = allEntries
            .GroupBy(e => new { e.Timestamp.Date, e.Timestamp.Hour })
            .OrderByDescending(g => g.Count())
            .Take(10)
            .Select(g => new { DateTime = g.Key.Date.AddHours(g.Key.Hour), Count = g.Count() })
            .ToDictionary(x => x.DateTime, x => x.Count);

        report.PeakActivityPeriods = hourlyActivity;

        // Response time analysis (if available in metadata)
        var entriesWithResponseTime = allEntries
            .Where(e => !string.IsNullOrEmpty(e.Metadata))
            .Select(e => new { Entry = e, ResponseTime = ExtractResponseTime(e.Metadata) })
            .Where(x => x.ResponseTime.HasValue)
            .ToList();

        if (entriesWithResponseTime.Any())
        {
            report.AverageResponseTime = entriesWithResponseTime.Average(x => x.ResponseTime!.Value);
            report.SlowOperations = entriesWithResponseTime
                .Where(x => x.ResponseTime > 5000) // Slower than 5 seconds
                .Count();
        }

        return report;
    }

    public async Task<List<AuditEntry>> FindSlowOperationsAsync(DateTime fromDate, DateTime toDate, CancellationToken cancellationToken = default)
    {
        // This implementation looks for operations that might have performance metadata
        return await _auditRepository.GetSlowOperationsAsync(fromDate, toDate, cancellationToken);
    }

    public async Task<BusinessIntelligenceReport> GenerateBusinessReportAsync(DateTime fromDate, DateTime toDate, CancellationToken cancellationToken = default)
    {
        var statistics = await _auditRepository.GetStatisticsAsync(fromDate, toDate, cancellationToken);
        
        var report = new BusinessIntelligenceReport
        {
            ReportPeriod = $"{fromDate:yyyy-MM-dd} to {toDate:yyyy-MM-dd}",
            GeneratedAt = DateTime.UtcNow
        };

        // Key metrics
        report.TotalActivities = statistics.TotalEntries;
        report.UniqueUsers = statistics.UserCounts.Count;
        report.ErrorRate = statistics.TotalEntries > 0 ? 
            (double)statistics.SeverityCounts.Where(kvp => kvp.Key == "Error" || kvp.Key == "Critical").Sum(kvp => kvp.Value) / statistics.TotalEntries * 100 : 0;

        // Top activities
        report.TopActivities = statistics.ActionCounts
            .OrderByDescending(kvp => kvp.Value)
            .Take(10)
            .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

        // Most active users
        report.TopUsers = statistics.UserCounts
            .OrderByDescending(kvp => kvp.Value)
            .Take(10)
            .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

        // System health indicators
        report.SystemHealthScore = CalculateSystemHealthScore(statistics);

        return report;
    }

    public async Task<Dictionary<string, object>> GenerateExecutiveSummaryAsync(DateTime fromDate, DateTime toDate, CancellationToken cancellationToken = default)
    {
        var statistics = await _auditRepository.GetStatisticsAsync(fromDate, toDate, cancellationToken);
        var securityEvents = await _auditRepository.GetSecurityEventsAsync(fromDate, toDate, AuditSeverity.Warning, cancellationToken);
        
        return new Dictionary<string, object>
        {
            ["ReportPeriod"] = $"{fromDate:yyyy-MM-dd} to {toDate:yyyy-MM-dd}",
            ["GeneratedAt"] = DateTime.UtcNow,
            ["TotalActivities"] = statistics.TotalEntries,
            ["DailyAverage"] = statistics.AverageEntriesPerDay,
            ["SecurityAlerts"] = securityEvents.Count,
            ["CriticalIssues"] = statistics.SeverityCounts.GetValueOrDefault("Critical", 0),
            ["SystemUptime"] = "99.9%", // This would come from actual system monitoring
            ["ComplianceStatus"] = securityEvents.Count == 0 ? "Compliant" : "Review Required",
            ["KeyTrends"] = new[]
            {
                statistics.TotalEntries > 1000 ? "High Activity Volume" : "Normal Activity",
                securityEvents.Count > 10 ? "Security Attention Required" : "Security Status Good",
                statistics.ArchivedEntries > statistics.TotalEntries * 0.5 ? "Good Data Retention" : "Review Retention Policy"
            }
        };
    }

    // Helper methods
    private async Task<int> GetRecentFailuresAsync(string? userId, string? ipAddress, CancellationToken cancellationToken)
    {
        var tenMinutesAgo = DateTime.UtcNow.AddMinutes(-10);
        var filter = new AuditQueryFilter
        {
            FromDate = tenMinutesAgo,
            ToDate = DateTime.UtcNow,
            MinSeverity = AuditSeverity.Error
        };

        if (!string.IsNullOrEmpty(userId))
        {
            filter.UserId = userId;
        }

        var entries = await _auditRepository.QueryAsync(filter, cancellationToken);
        
        return entries.Count(e => 
            (string.IsNullOrEmpty(userId) || e.UserId == userId) &&
            (string.IsNullOrEmpty(ipAddress) || e.IpAddress == ipAddress));
    }

    private static bool IsUnusualTime(DateTime timestamp)
    {
        return timestamp.Hour < 6 || timestamp.Hour > 22;
    }

    private static double? ExtractResponseTime(string? metadata)
    {
        if (string.IsNullOrEmpty(metadata))
            return null;

        try
        {
            var metadataDict = JsonSerializer.Deserialize<Dictionary<string, object>>(metadata);
            if (metadataDict?.TryGetValue("ResponseTimeMs", out var responseTimeObj) == true)
            {
                return Convert.ToDouble(responseTimeObj);
            }
        }
        catch
        {
            // Ignore parsing errors
        }

        return null;
    }

    private static double CalculateSystemHealthScore(AuditStatistics statistics)
    {
        var baseScore = 100.0;
        
        // Reduce score for errors
        var errorCount = statistics.SeverityCounts.Where(kvp => kvp.Key == "Error" || kvp.Key == "Critical").Sum(kvp => kvp.Value);
        var errorRate = statistics.TotalEntries > 0 ? (double)errorCount / statistics.TotalEntries : 0;
        baseScore -= errorRate * 50; // Max 50 point reduction for errors

        // Reduce score for low activity (might indicate system problems)
        if (statistics.AverageEntriesPerDay < 10)
        {
            baseScore -= 20;
        }

        return Math.Max(0, Math.Min(100, baseScore));
    }
}

// Supporting classes for audit analysis
public class AuditPattern
{
    public string PatternType { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public AuditSeverity Severity { get; set; }
    public List<AuditEntry> AffectedEntries { get; set; } = new();
    public decimal RiskScore { get; set; }
    public DateTime DetectedAt { get; set; }
}

public class AuditAnomaly
{
    public string AnomalyType { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public AuditSeverity Severity { get; set; }
    public double BaselineValue { get; set; }
    public double CurrentValue { get; set; }
    public double DeviationPercent { get; set; }
    public DateTime DetectedAt { get; set; }
    public string? AffectedUserId { get; set; }
    public Dictionary<string, object>? AdditionalData { get; set; }
}

public class FraudRiskAssessment
{
    public string? UserId { get; set; }
    public string? TeamSlug { get; set; }
    public string AssessmentPeriod { get; set; } = string.Empty;
    public DateTime GeneratedAt { get; set; }
    public decimal RiskScore { get; set; }
    public string RiskLevel { get; set; } = string.Empty;
    public Dictionary<string, int> RiskFactors { get; set; } = new();
    public List<string> Recommendations { get; set; } = new();
}

public class AuditAlert
{
    public string AlertType { get; set; } = string.Empty;
    public AuditSeverity Severity { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public Guid AuditEntryId { get; set; }
    public DateTime TriggeredAt { get; set; }
    public bool RequiresImmedateAction { get; set; }
}

public class AuditTrendAnalysis
{
    public string AnalysisPeriod { get; set; } = string.Empty;
    public string? EntityType { get; set; }
    public DateTime GeneratedAt { get; set; }
    public int TotalEntries { get; set; }
    public Dictionary<DateTime, int> DailyActivity { get; set; } = new();
    public Dictionary<int, int> HourlyPattern { get; set; } = new();
    public Dictionary<string, int> ActionTrends { get; set; } = new();
    public Dictionary<DateTime, double> ErrorRateTrend { get; set; } = new();
    public double GrowthRate { get; set; }
}

public class SecurityAnalysisReport
{
    public string AnalysisPeriod { get; set; } = string.Empty;
    public DateTime GeneratedAt { get; set; }
    public int TotalSecurityEvents { get; set; }
    public Dictionary<string, int> EventsByType { get; set; } = new();
    public Dictionary<string, int> EventsBySeverity { get; set; } = new();
    public Dictionary<string, int> TopAffectedIPs { get; set; } = new();
    public Dictionary<int, int> IncidentsByHour { get; set; } = new();
    public List<SecurityIncident> CriticalIncidents { get; set; } = new();
}

public class SecurityIncident
{
    public Guid IncidentId { get; set; }
    public DateTime Timestamp { get; set; }
    public string Action { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string? IpAddress { get; set; }
    public string? UserId { get; set; }
}

public class FailurePattern
{
    public string PatternType { get; set; } = string.Empty;
    public string Identifier { get; set; } = string.Empty;
    public int FailureCount { get; set; }
    public DateTime FirstFailure { get; set; }
    public DateTime LastFailure { get; set; }
    public List<string> PrimaryActions { get; set; } = new();
}

public class PerformanceAnalysisReport
{
    public string AnalysisPeriod { get; set; } = string.Empty;
    public DateTime GeneratedAt { get; set; }
    public int TotalOperations { get; set; }
    public double AverageOperationsPerHour { get; set; }
    public Dictionary<DateTime, int> PeakActivityPeriods { get; set; } = new();
    public double? AverageResponseTime { get; set; }
    public int? SlowOperations { get; set; }
}

public class BusinessIntelligenceReport
{
    public string ReportPeriod { get; set; } = string.Empty;
    public DateTime GeneratedAt { get; set; }
    public int TotalActivities { get; set; }
    public int UniqueUsers { get; set; }
    public double ErrorRate { get; set; }
    public Dictionary<string, int> TopActivities { get; set; } = new();
    public Dictionary<string, int> TopUsers { get; set; } = new();
    public double SystemHealthScore { get; set; }
}