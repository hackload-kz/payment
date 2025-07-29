using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;
using System.Text.Json;

namespace PaymentGateway.Core.Services;

public interface ISecurityAuditService
{
    Task LogSecurityEventAsync(SecurityAuditEvent auditEvent, CancellationToken cancellationToken = default);
    Task<List<SecurityAuditEvent>> GetSecurityEventsAsync(SecurityEventFilter filter, CancellationToken cancellationToken = default);
    Task<SecurityEventSummary> GetSecuritySummaryAsync(TimeSpan period, CancellationToken cancellationToken = default);
    Task<List<SecurityAlert>> GetActiveSecurityAlertsAsync(CancellationToken cancellationToken = default);
    Task AlertOnSuspiciousActivityAsync(string identifier, SecurityEventType eventType, int eventCount, TimeSpan period);
}

public record SecurityAuditEvent(
    string EventId,
    SecurityEventType EventType,
    SecurityEventSeverity Severity,
    DateTime Timestamp,
    string? UserId,
    string? TeamSlug,
    string? IpAddress,
    string? UserAgent,
    string EventDescription,
    Dictionary<string, string> EventData,
    string? CorrelationId,
    bool IsSuccessful,
    string? ErrorMessage);

public record SecurityEventFilter(
    DateTime? StartDate,
    DateTime? EndDate,
    SecurityEventType? EventType,
    SecurityEventSeverity? MinSeverity,
    string? TeamSlug,
    string? IpAddress,
    bool? IsSuccessful,
    int? MaxResults);

public record SecurityEventSummary(
    TimeSpan Period,
    int TotalEvents,
    Dictionary<SecurityEventType, int> EventsByType,
    Dictionary<SecurityEventSeverity, int> EventsBySeverity,
    Dictionary<string, int> EventsByTeam,
    Dictionary<string, int> EventsByIp,
    List<SecurityTrend> Trends);

public record SecurityTrend(
    SecurityEventType EventType,
    int CurrentCount,
    int PreviousCount,
    double ChangePercentage,
    string TrendDirection); // "increasing", "decreasing", "stable"

public record SecurityAlert(
    string AlertId,
    SecurityAlertType AlertType,
    SecurityEventSeverity Severity,
    DateTime CreatedAt,
    string Title,
    string Description,
    Dictionary<string, string> AlertData,
    bool IsActive,
    DateTime? ResolvedAt);

public enum SecurityEventType
{
    Authentication,
    AuthenticationFailure,
    AuthenticationSuccess,
    TokenGeneration,
    TokenValidation,
    TokenValidationFailure,
    PasswordChange,
    PasswordReset,
    AccountLockout,
    AccountUnlock,
    RateLimitExceeded,
    HttpsViolation,
    SuspiciousActivity,
    DataAccess,
    DataModification,
    PaymentInitiated,
    PaymentCompleted,
    PaymentFailed,
    SystemError,
    ConfigurationChange,
    SecurityPolicyViolation
}

public enum SecurityEventSeverity
{
    Low = 1,
    Medium = 2,
    High = 3,
    Critical = 4
}

public enum SecurityAlertType
{
    MultipleFailedAuthentications,
    SuspiciousIpActivity,
    RateLimitViolations,
    UnusualPaymentPatterns,
    SystemSecurityBreach,
    DataAccessAnomaly,
    ConfigurationAnomaly
}

public class SecurityAuditOptions
{
    public bool EnableAuditLogging { get; set; } = true;
    public bool EnableRealTimeAlerting { get; set; } = true;
    public TimeSpan AuditRetentionPeriod { get; set; } = TimeSpan.FromDays(365);
    public Dictionary<SecurityEventType, int> AlertThresholds { get; set; } = new()
    {
        { SecurityEventType.AuthenticationFailure, 5 },
        { SecurityEventType.RateLimitExceeded, 10 },
        { SecurityEventType.HttpsViolation, 3 },
        { SecurityEventType.SuspiciousActivity, 1 }
    };
    public TimeSpan AlertTimeWindow { get; set; } = TimeSpan.FromMinutes(15);
    public bool EnableTrendAnalysis { get; set; } = true;
    public List<string> SensitiveDataKeys { get; set; } = new()
    {
        "password", "token", "cardnumber", "cvv", "pin", "secret"
    };
}

public class SecurityAuditService : ISecurityAuditService
{
    private readonly ILogger<SecurityAuditService> _logger;
    private readonly SecurityAuditOptions _options;
    
    // In-memory storage (in production, use database with proper indexing)
    private readonly ConcurrentDictionary<string, SecurityAuditEvent> _auditEvents;
    private readonly ConcurrentDictionary<string, SecurityAlert> _activeAlerts;
    private readonly ConcurrentDictionary<string, List<DateTime>> _eventCounters;

    public SecurityAuditService(
        ILogger<SecurityAuditService> logger,
        IOptions<SecurityAuditOptions> options)
    {
        _logger = logger;
        _options = options.Value;
        _auditEvents = new ConcurrentDictionary<string, SecurityAuditEvent>();
        _activeAlerts = new ConcurrentDictionary<string, SecurityAlert>();
        _eventCounters = new ConcurrentDictionary<string, List<DateTime>>();

        // Start background cleanup task
        _ = Task.Run(CleanupOldEventsAsync);
    }

    public async Task LogSecurityEventAsync(SecurityAuditEvent auditEvent, CancellationToken cancellationToken = default)
    {
        if (!_options.EnableAuditLogging)
            return;

        ArgumentNullException.ThrowIfNull(auditEvent);

        try
        {
            // Sanitize sensitive data
            var sanitizedEvent = SanitizeSecurityEvent(auditEvent);

            // Store the event
            _auditEvents.TryAdd(sanitizedEvent.EventId, sanitizedEvent);

            // Log to structured logger
            LogStructuredSecurityEvent(sanitizedEvent);

            // Update event counters for alerting
            UpdateEventCounters(sanitizedEvent);

            // Check for alerting conditions
            if (_options.EnableRealTimeAlerting)
            {
                await CheckForSecurityAlertsAsync(sanitizedEvent);
            }

            _logger.LogDebug("Security event logged: {EventType} for {TeamSlug} (Event ID: {EventId})",
                sanitizedEvent.EventType, sanitizedEvent.TeamSlug ?? "unknown", sanitizedEvent.EventId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to log security event: {EventType}", auditEvent.EventType);
        }

        await Task.CompletedTask;
    }

    public async Task<List<SecurityAuditEvent>> GetSecurityEventsAsync(SecurityEventFilter filter, CancellationToken cancellationToken = default)
    {
        var events = _auditEvents.Values.AsEnumerable();

        // Apply filters
        if (filter.StartDate.HasValue)
            events = events.Where(e => e.Timestamp >= filter.StartDate.Value);

        if (filter.EndDate.HasValue)
            events = events.Where(e => e.Timestamp <= filter.EndDate.Value);

        if (filter.EventType.HasValue)
            events = events.Where(e => e.EventType == filter.EventType.Value);

        if (filter.MinSeverity.HasValue)
            events = events.Where(e => e.Severity >= filter.MinSeverity.Value);

        if (!string.IsNullOrEmpty(filter.TeamSlug))
            events = events.Where(e => e.TeamSlug == filter.TeamSlug);

        if (!string.IsNullOrEmpty(filter.IpAddress))
            events = events.Where(e => e.IpAddress == filter.IpAddress);

        if (filter.IsSuccessful.HasValue)
            events = events.Where(e => e.IsSuccessful == filter.IsSuccessful.Value);

        // Order by timestamp (most recent first)
        events = events.OrderByDescending(e => e.Timestamp);

        // Apply max results limit
        if (filter.MaxResults.HasValue)
            events = events.Take(filter.MaxResults.Value);

        return await Task.FromResult(events.ToList());
    }

    public async Task<SecurityEventSummary> GetSecuritySummaryAsync(TimeSpan period, CancellationToken cancellationToken = default)
    {
        var endTime = DateTime.UtcNow;
        var startTime = endTime - period;

        var periodEvents = _auditEvents.Values
            .Where(e => e.Timestamp >= startTime && e.Timestamp <= endTime)
            .ToList();

        var totalEvents = periodEvents.Count;

        var eventsByType = periodEvents
            .GroupBy(e => e.EventType)
            .ToDictionary(g => g.Key, g => g.Count());

        var eventsBySeverity = periodEvents
            .GroupBy(e => e.Severity)
            .ToDictionary(g => g.Key, g => g.Count());

        var eventsByTeam = periodEvents
            .Where(e => !string.IsNullOrEmpty(e.TeamSlug))
            .GroupBy(e => e.TeamSlug!)
            .ToDictionary(g => g.Key, g => g.Count());

        var eventsByIp = periodEvents
            .Where(e => !string.IsNullOrEmpty(e.IpAddress))
            .GroupBy(e => e.IpAddress!)
            .ToDictionary(g => g.Key, g => g.Count());

        var trends = _options.EnableTrendAnalysis 
            ? await CalculateSecurityTrendsAsync(period)
            : new List<SecurityTrend>();

        return new SecurityEventSummary(
            period,
            totalEvents,
            eventsByType,
            eventsBySeverity,
            eventsByTeam,
            eventsByIp,
            trends);
    }

    public async Task<List<SecurityAlert>> GetActiveSecurityAlertsAsync(CancellationToken cancellationToken = default)
    {
        var activeAlerts = _activeAlerts.Values
            .Where(a => a.IsActive)
            .OrderByDescending(a => a.CreatedAt)
            .ToList();

        return await Task.FromResult(activeAlerts);
    }

    public async Task AlertOnSuspiciousActivityAsync(string identifier, SecurityEventType eventType, int eventCount, TimeSpan period)
    {
        var alertId = $"{eventType}_{identifier}_{DateTime.UtcNow:yyyyMMddHHmmss}";
        
        var severity = DetermineAlertSeverity(eventType, eventCount);
        var alert = new SecurityAlert(
            alertId,
            MapToSecurityAlertType(eventType),
            severity,
            DateTime.UtcNow,
            $"Suspicious {eventType} Activity Detected",
            $"Detected {eventCount} {eventType} events for {identifier} within {period}",
            new Dictionary<string, string>
            {
                { "Identifier", identifier },
                { "EventType", eventType.ToString() },
                { "EventCount", eventCount.ToString() },
                { "Period", period.ToString() }
            },
            true,
            null);

        _activeAlerts.TryAdd(alertId, alert);

        _logger.LogWarning("Security alert created: {AlertType} - {Description} (Alert ID: {AlertId})",
            alert.AlertType, alert.Description, alertId);

        await Task.CompletedTask;
    }

    private SecurityAuditEvent SanitizeSecurityEvent(SecurityAuditEvent auditEvent)
    {
        var sanitizedEventData = new Dictionary<string, string>();

        foreach (var kvp in auditEvent.EventData)
        {
            var key = kvp.Key.ToLowerInvariant();
            var isSensitive = _options.SensitiveDataKeys.Any(sensitiveKey => 
                key.Contains(sensitiveKey, StringComparison.OrdinalIgnoreCase));

            sanitizedEventData[kvp.Key] = isSensitive ? "***REDACTED***" : kvp.Value;
        }

        return auditEvent with { EventData = sanitizedEventData };
    }

    private void LogStructuredSecurityEvent(SecurityAuditEvent auditEvent)
    {
        using var scope = _logger.BeginScope(new Dictionary<string, object>
        {
            ["EventId"] = auditEvent.EventId,
            ["EventType"] = auditEvent.EventType.ToString(),
            ["Severity"] = auditEvent.Severity.ToString(),
            ["TeamSlug"] = auditEvent.TeamSlug ?? "unknown",
            ["IpAddress"] = auditEvent.IpAddress ?? "unknown",
            ["CorrelationId"] = auditEvent.CorrelationId ?? "unknown",
            ["IsSuccessful"] = auditEvent.IsSuccessful
        });

        var logLevel = auditEvent.Severity switch
        {
            SecurityEventSeverity.Critical => LogLevel.Critical,
            SecurityEventSeverity.High => LogLevel.Error,
            SecurityEventSeverity.Medium => LogLevel.Warning,
            SecurityEventSeverity.Low => LogLevel.Information,
            _ => LogLevel.Information
        };

        _logger.Log(logLevel, "Security Event: {EventDescription}", auditEvent.EventDescription);
    }

    private void UpdateEventCounters(SecurityAuditEvent auditEvent)
    {
        var key = $"{auditEvent.EventType}_{auditEvent.TeamSlug ?? "unknown"}";
        var now = DateTime.UtcNow;

        _eventCounters.AddOrUpdate(key,
            new List<DateTime> { now },
            (_, existing) =>
            {
                existing.Add(now);
                // Keep only events within the alert time window
                var cutoff = now - _options.AlertTimeWindow;
                return existing.Where(timestamp => timestamp > cutoff).ToList();
            });
    }

    private async Task CheckForSecurityAlertsAsync(SecurityAuditEvent auditEvent)
    {
        if (!_options.AlertThresholds.TryGetValue(auditEvent.EventType, out var threshold))
            return;

        var key = $"{auditEvent.EventType}_{auditEvent.TeamSlug ?? "unknown"}";
        if (!_eventCounters.TryGetValue(key, out var eventTimes))
            return;

        if (eventTimes.Count >= threshold)
        {
            await AlertOnSuspiciousActivityAsync(
                auditEvent.TeamSlug ?? auditEvent.IpAddress ?? "unknown",
                auditEvent.EventType,
                eventTimes.Count,
                _options.AlertTimeWindow);
        }
    }

    private async Task<List<SecurityTrend>> CalculateSecurityTrendsAsync(TimeSpan period)
    {
        var trends = new List<SecurityTrend>();
        var endTime = DateTime.UtcNow;
        var currentPeriodStart = endTime - period;
        var previousPeriodStart = currentPeriodStart - period;

        var currentPeriodEvents = _auditEvents.Values
            .Where(e => e.Timestamp >= currentPeriodStart && e.Timestamp <= endTime)
            .ToList();

        var previousPeriodEvents = _auditEvents.Values
            .Where(e => e.Timestamp >= previousPeriodStart && e.Timestamp < currentPeriodStart)
            .ToList();

        var eventTypes = currentPeriodEvents.Select(e => e.EventType).Distinct();

        foreach (var eventType in eventTypes)
        {
            var currentCount = currentPeriodEvents.Count(e => e.EventType == eventType);
            var previousCount = previousPeriodEvents.Count(e => e.EventType == eventType);

            var changePercentage = previousCount > 0 
                ? ((double)(currentCount - previousCount) / previousCount) * 100
                : currentCount > 0 ? 100.0 : 0.0;

            var trendDirection = changePercentage switch
            {
                > 10 => "increasing",
                < -10 => "decreasing",
                _ => "stable"
            };

            trends.Add(new SecurityTrend(
                eventType,
                currentCount,
                previousCount,
                Math.Round(changePercentage, 2),
                trendDirection));
        }

        return await Task.FromResult(trends.OrderByDescending(t => Math.Abs(t.ChangePercentage)).ToList());
    }

    private SecurityEventSeverity DetermineAlertSeverity(SecurityEventType eventType, int eventCount)
    {
        return eventType switch
        {
            SecurityEventType.SystemError or SecurityEventType.SecurityPolicyViolation => SecurityEventSeverity.Critical,
            SecurityEventType.AuthenticationFailure when eventCount > 10 => SecurityEventSeverity.High,
            SecurityEventType.RateLimitExceeded when eventCount > 20 => SecurityEventSeverity.High,
            SecurityEventType.HttpsViolation => SecurityEventSeverity.Medium,
            SecurityEventType.SuspiciousActivity => SecurityEventSeverity.High,
            _ => SecurityEventSeverity.Medium
        };
    }

    private SecurityAlertType MapToSecurityAlertType(SecurityEventType eventType)
    {
        return eventType switch
        {
            SecurityEventType.AuthenticationFailure => SecurityAlertType.MultipleFailedAuthentications,
            SecurityEventType.RateLimitExceeded => SecurityAlertType.RateLimitViolations,
            SecurityEventType.HttpsViolation => SecurityAlertType.SuspiciousIpActivity,
            SecurityEventType.SuspiciousActivity => SecurityAlertType.SuspiciousIpActivity,
            SecurityEventType.DataAccess => SecurityAlertType.DataAccessAnomaly,
            SecurityEventType.ConfigurationChange => SecurityAlertType.ConfigurationAnomaly,
            _ => SecurityAlertType.SystemSecurityBreach
        };
    }

    private async Task CleanupOldEventsAsync()
    {
        while (true)
        {
            try
            {
                var cutoff = DateTime.UtcNow - _options.AuditRetentionPeriod;
                var expiredEventIds = _auditEvents
                    .Where(kvp => kvp.Value.Timestamp < cutoff)
                    .Select(kvp => kvp.Key)
                    .ToList();

                foreach (var eventId in expiredEventIds)
                {
                    _auditEvents.TryRemove(eventId, out _);
                }

                if (expiredEventIds.Count > 0)
                {
                    _logger.LogInformation("Cleaned up {Count} expired security audit events", expiredEventIds.Count);
                }

                await Task.Delay(TimeSpan.FromHours(1)); // Run cleanup every hour
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during security audit cleanup");
                await Task.Delay(TimeSpan.FromMinutes(15)); // Shorter delay on error
            }
        }
    }
}

// Extension methods for easier security audit logging
public static class SecurityAuditServiceExtensions
{
    public static async Task LogAuthenticationAttemptAsync(
        this ISecurityAuditService auditService,
        string teamSlug,
        string? ipAddress,
        bool isSuccessful,
        string? correlationId = null,
        string? errorMessage = null)
    {
        var eventType = isSuccessful ? SecurityEventType.AuthenticationSuccess : SecurityEventType.AuthenticationFailure;
        var severity = isSuccessful ? SecurityEventSeverity.Low : SecurityEventSeverity.Medium;

        var auditEvent = new SecurityAuditEvent(
            Guid.NewGuid().ToString(),
            eventType,
            severity,
            DateTime.UtcNow,
            null,
            teamSlug,
            ipAddress,
            null,
            $"Authentication attempt for team {teamSlug}: {(isSuccessful ? "Success" : "Failure")}",
            new Dictionary<string, string>
            {
                { "TeamSlug", teamSlug },
                { "Result", isSuccessful ? "Success" : "Failure" }
            },
            correlationId,
            isSuccessful,
            errorMessage);

        await auditService.LogSecurityEventAsync(auditEvent);
    }

    public static async Task LogRateLimitViolationAsync(
        this ISecurityAuditService auditService,
        string? ipAddress,
        string path,
        string? teamSlug = null,
        string? correlationId = null)
    {
        var auditEvent = new SecurityAuditEvent(
            Guid.NewGuid().ToString(),
            SecurityEventType.RateLimitExceeded,
            SecurityEventSeverity.Medium,
            DateTime.UtcNow,
            null,
            teamSlug,
            ipAddress,
            null,
            $"Rate limit exceeded for path {path}",
            new Dictionary<string, string>
            {
                { "Path", path },
                { "ViolationType", "RateLimit" }
            },
            correlationId,
            false,
            "Rate limit threshold exceeded");

        await auditService.LogSecurityEventAsync(auditEvent);
    }

    public static async Task LogHttpsViolationAsync(
        this ISecurityAuditService auditService,
        string? ipAddress,
        string path,
        string? userAgent = null,
        string? correlationId = null)
    {
        var auditEvent = new SecurityAuditEvent(
            Guid.NewGuid().ToString(),
            SecurityEventType.HttpsViolation,
            SecurityEventSeverity.Medium,
            DateTime.UtcNow,
            null,
            null,
            ipAddress,
            userAgent,
            $"HTTP request attempted on HTTPS-only endpoint: {path}",
            new Dictionary<string, string>
            {
                { "Path", path },
                { "Protocol", "HTTP" },
                { "ExpectedProtocol", "HTTPS" }
            },
            correlationId,
            false,
            "HTTPS required but HTTP used");

        await auditService.LogSecurityEventAsync(auditEvent);
    }
}