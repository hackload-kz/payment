using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PaymentGateway.Core.Configuration;
using System.Collections.Concurrent;
using System.Text.Json;

namespace PaymentGateway.Core.Services;

public interface IConfigurationAuditService
{
    Task LogConfigurationChangeAsync(ConfigurationChangeAuditEntry auditEntry);
    Task<List<ConfigurationChangeAuditEntry>> GetConfigurationChangesAsync(TimeSpan period);
    Task<List<ConfigurationChangeAuditEntry>> GetConfigurationChangesForSectionAsync(string sectionName, TimeSpan period);
    Task<ConfigurationAuditSummary> GetConfigurationAuditSummaryAsync(TimeSpan period);
    Task<bool> HasCriticalChangesAsync(TimeSpan period);
}

public record ConfigurationChangeAuditEntry(
    string ChangeId,
    string SectionName,
    string PropertyPath,
    object? OldValue,
    object? NewValue,
    DateTime ChangedAt,
    string? ChangedBy,
    string? ChangeReason,
    ConfigurationChangeSeverity Severity,
    Dictionary<string, string> Metadata);

public record ConfigurationAuditSummary(
    TimeSpan Period,
    int TotalChanges,
    Dictionary<string, int> ChangesBySection,
    Dictionary<ConfigurationChangeSeverity, int> ChangesBySeverity,
    List<string> MostChangedSections,
    DateTime LastChange,
    bool HasCriticalChanges);

public enum ConfigurationChangeSeverity
{
    Info = 1,
    Low = 2,
    Medium = 3,
    High = 4,
    Critical = 5
}

public class ConfigurationAuditService : BackgroundService, IConfigurationAuditService
{
    private readonly ILogger<ConfigurationAuditService> _logger;
    private readonly IConfigurationHotReloadService _hotReloadService;
    private readonly ISecurityAuditService _securityAuditService;
    private readonly IOptionsMonitor<FeatureFlagsOptions> _featureFlagsOptions;

    // Thread-safe collections for audit data
    private readonly ConcurrentQueue<ConfigurationChangeAuditEntry> _auditEntries;
    private readonly ConcurrentDictionary<string, DateTime> _lastChangeBySection;

    // Configuration change patterns that indicate security concerns
    private readonly HashSet<string> _securitySensitiveSettings = new()
    {
        "ConnectionStrings",
        "Security",
        "Authentication",
        "Https",
        "RateLimit",
        "Encryption",
        "Certificates",
        "Secrets"
    };

    public ConfigurationAuditService(
        ILogger<ConfigurationAuditService> logger,
        IConfigurationHotReloadService hotReloadService,
        ISecurityAuditService securityAuditService,
        IOptionsMonitor<FeatureFlagsOptions> featureFlagsOptions)
    {
        _logger = logger;
        _hotReloadService = hotReloadService;
        _securityAuditService = securityAuditService;
        _featureFlagsOptions = featureFlagsOptions;
        _auditEntries = new ConcurrentQueue<ConfigurationChangeAuditEntry>();
        _lastChangeBySection = new ConcurrentDictionary<string, DateTime>();

        // Subscribe to configuration changes
        _hotReloadService.ConfigurationChanged += OnConfigurationChanged;
    }

    public async Task LogConfigurationChangeAsync(ConfigurationChangeAuditEntry auditEntry)
    {
        ArgumentNullException.ThrowIfNull(auditEntry);

        try
        {
            // Add to audit trail
            _auditEntries.Enqueue(auditEntry);

            // Update last change tracking
            _lastChangeBySection[auditEntry.SectionName] = auditEntry.ChangedAt;

            // Log structured audit entry
            using var scope = _logger.BeginScope(new Dictionary<string, object>
            {
                ["ChangeId"] = auditEntry.ChangeId,
                ["SectionName"] = auditEntry.SectionName,
                ["PropertyPath"] = auditEntry.PropertyPath,
                ["Severity"] = auditEntry.Severity.ToString(),
                ["ChangedBy"] = auditEntry.ChangedBy ?? "System",
                ["ChangedAt"] = auditEntry.ChangedAt
            });

            var logLevel = auditEntry.Severity switch
            {
                ConfigurationChangeSeverity.Critical => LogLevel.Critical,
                ConfigurationChangeSeverity.High => LogLevel.Error,
                ConfigurationChangeSeverity.Medium => LogLevel.Warning,
                ConfigurationChangeSeverity.Low => LogLevel.Information,
                ConfigurationChangeSeverity.Info => LogLevel.Debug,
                _ => LogLevel.Information
            };

            _logger.Log(logLevel, "Configuration change: {SectionName}.{PropertyPath} changed from {OldValue} to {NewValue} (Reason: {ChangeReason})",
                auditEntry.SectionName, auditEntry.PropertyPath, 
                MaskSensitiveValue(auditEntry.SectionName, auditEntry.OldValue),
                MaskSensitiveValue(auditEntry.SectionName, auditEntry.NewValue),
                auditEntry.ChangeReason ?? "Unknown");

            // Log security events for sensitive changes
            if (IsSecuritySensitiveChange(auditEntry))
            {
                await LogSecurityAuditEventAsync(auditEntry);
            }

            // Clean up old entries (keep last 10000)
            while (_auditEntries.Count > 10000)
            {
                _auditEntries.TryDequeue(out _);
            }

            _logger.LogDebug("Configuration change audit entry logged: {ChangeId}", auditEntry.ChangeId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error logging configuration change audit entry: {ChangeId}", auditEntry.ChangeId);
        }

        await Task.CompletedTask;
    }

    public async Task<List<ConfigurationChangeAuditEntry>> GetConfigurationChangesAsync(TimeSpan period)
    {
        var cutoff = DateTime.UtcNow - period;
        return await Task.FromResult(_auditEntries
            .Where(entry => entry.ChangedAt > cutoff)
            .OrderByDescending(entry => entry.ChangedAt)
            .ToList());
    }

    public async Task<List<ConfigurationChangeAuditEntry>> GetConfigurationChangesForSectionAsync(string sectionName, TimeSpan period)
    {
        ArgumentException.ThrowIfNullOrEmpty(sectionName);

        var cutoff = DateTime.UtcNow - period;
        return await Task.FromResult(_auditEntries
            .Where(entry => entry.ChangedAt > cutoff && 
                           entry.SectionName.Equals(sectionName, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(entry => entry.ChangedAt)
            .ToList());
    }

    public async Task<ConfigurationAuditSummary> GetConfigurationAuditSummaryAsync(TimeSpan period)
    {
        var cutoff = DateTime.UtcNow - period;
        var periodEntries = _auditEntries
            .Where(entry => entry.ChangedAt > cutoff)
            .ToList();

        var totalChanges = periodEntries.Count;

        var changesBySection = periodEntries
            .GroupBy(entry => entry.SectionName)
            .ToDictionary(g => g.Key, g => g.Count());

        var changesBySeverity = periodEntries
            .GroupBy(entry => entry.Severity)
            .ToDictionary(g => g.Key, g => g.Count());

        var mostChangedSections = changesBySection
            .OrderByDescending(kvp => kvp.Value)
            .Take(5)
            .Select(kvp => $"{kvp.Key} ({kvp.Value} changes)")
            .ToList();

        var lastChange = periodEntries.Any() 
            ? periodEntries.Max(entry => entry.ChangedAt)
            : DateTime.MinValue;

        var hasCriticalChanges = periodEntries.Any(entry => 
            entry.Severity >= ConfigurationChangeSeverity.High);

        return await Task.FromResult(new ConfigurationAuditSummary(
            period,
            totalChanges,
            changesBySection,
            changesBySeverity,
            mostChangedSections,
            lastChange,
            hasCriticalChanges));
    }

    public async Task<bool> HasCriticalChangesAsync(TimeSpan period)
    {
        var cutoff = DateTime.UtcNow - period;
        return await Task.FromResult(_auditEntries
            .Any(entry => entry.ChangedAt > cutoff && 
                         entry.Severity >= ConfigurationChangeSeverity.Critical));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // Perform periodic analysis and cleanup
                await PerformPeriodicAnalysisAsync();
                await CleanupOldEntriesAsync();

                await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in configuration audit background service");
                await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
            }
        }
    }

    private async void OnConfigurationChanged(object? sender, ConfigurationChangedEventArgs e)
    {
        try
        {
            foreach (var change in e.Changes)
            {
                var severity = DetermineChangeSeverity(change);
                var auditEntry = new ConfigurationChangeAuditEntry(
                    Guid.NewGuid().ToString(),
                    change.SectionName,
                    change.PropertyPath,
                    change.OldValue,
                    change.NewValue,
                    change.ChangedAt,
                    "HotReload",
                    "Configuration hot reload",
                    severity,
                    new Dictionary<string, string>
                    {
                        ["ChangeType"] = change.ChangeType,
                        ["Source"] = "HotReload"
                    });

                await LogConfigurationChangeAsync(auditEntry);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling configuration change event");
        }
    }

    private ConfigurationChangeSeverity DetermineChangeSeverity(ConfigurationChange change)
    {
        // Security-sensitive changes are high severity
        if (IsSecuritySensitiveSectionName(change.SectionName))
        {
            return ConfigurationChangeSeverity.High;
        }

        // Database connection changes are critical
        if (change.SectionName.Contains("ConnectionStrings", StringComparison.OrdinalIgnoreCase))
        {
            return ConfigurationChangeSeverity.Critical;
        }

        // Feature flag changes are medium severity
        if (change.SectionName.Contains("FeatureFlags", StringComparison.OrdinalIgnoreCase))
        {
            return ConfigurationChangeSeverity.Medium;
        }

        // Logging and metrics changes are low severity
        if (change.SectionName.Contains("Logging", StringComparison.OrdinalIgnoreCase) ||
            change.SectionName.Contains("Metrics", StringComparison.OrdinalIgnoreCase))
        {
            return ConfigurationChangeSeverity.Low;
        }

        return ConfigurationChangeSeverity.Info;
    }

    private bool IsSecuritySensitiveChange(ConfigurationChangeAuditEntry auditEntry)
    {
        return IsSecuritySensitiveSectionName(auditEntry.SectionName) ||
               auditEntry.Severity >= ConfigurationChangeSeverity.High;
    }

    private bool IsSecuritySensitiveSectionName(string sectionName)
    {
        return _securitySensitiveSettings.Any(setting => 
            sectionName.Contains(setting, StringComparison.OrdinalIgnoreCase));
    }

    private async Task LogSecurityAuditEventAsync(ConfigurationChangeAuditEntry auditEntry)
    {
        try
        {
            var securityEvent = new SecurityAuditEvent(
                Guid.NewGuid().ToString(),
                SecurityEventType.ConfigurationChange,
                auditEntry.Severity switch
                {
                    ConfigurationChangeSeverity.Critical => SecurityEventSeverity.Critical,
                    ConfigurationChangeSeverity.High => SecurityEventSeverity.High,
                    ConfigurationChangeSeverity.Medium => SecurityEventSeverity.Medium,
                    _ => SecurityEventSeverity.Low
                },
                auditEntry.ChangedAt,
                null, // UserId
                null, // TeamSlug
                null, // IpAddress
                null, // UserAgent
                $"Configuration change: {auditEntry.SectionName}.{auditEntry.PropertyPath}",
                new Dictionary<string, string>
                {
                    ["SectionName"] = auditEntry.SectionName,
                    ["PropertyPath"] = auditEntry.PropertyPath,
                    ["ChangeReason"] = auditEntry.ChangeReason ?? "Unknown",
                    ["ChangedBy"] = auditEntry.ChangedBy ?? "System",
                    ["ChangeId"] = auditEntry.ChangeId
                },
                auditEntry.ChangeId,
                true,
                null);

            await _securityAuditService.LogSecurityEventAsync(securityEvent);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error logging security audit event for configuration change: {ChangeId}", 
                auditEntry.ChangeId);
        }
    }

    private object? MaskSensitiveValue(string sectionName, object? value)
    {
        if (value == null)
            return null;

        if (IsSecuritySensitiveSectionName(sectionName))
        {
            return "***MASKED***";
        }

        var stringValue = value.ToString();
        if (string.IsNullOrEmpty(stringValue))
            return value;

        // Mask potential passwords, keys, tokens
        var lowerValue = stringValue.ToLowerInvariant();
        if (lowerValue.Contains("password") || lowerValue.Contains("key") || lowerValue.Contains("token") ||
            lowerValue.Contains("secret") || lowerValue.Contains("connectionstring"))
        {
            return "***MASKED***";
        }

        return value;
    }

    private async Task PerformPeriodicAnalysisAsync()
    {
        try
        {
            var summary = await GetConfigurationAuditSummaryAsync(TimeSpan.FromHours(24));

            if (summary.HasCriticalChanges)
            {
                _logger.LogWarning("Critical configuration changes detected in the last 24 hours: {TotalChanges} total changes",
                    summary.TotalChanges);
            }

            if (summary.TotalChanges > 50) // Threshold for unusual activity
            {
                _logger.LogWarning("High configuration change activity detected: {TotalChanges} changes in 24 hours",
                    summary.TotalChanges);
            }

            _logger.LogDebug("Configuration audit summary: {TotalChanges} changes, Last change: {LastChange}",
                summary.TotalChanges, summary.LastChange);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error performing periodic configuration audit analysis");
        }
    }

    private async Task CleanupOldEntriesAsync()
    {
        try
        {
            var cutoff = DateTime.UtcNow - TimeSpan.FromDays(90); // Keep 90 days of audit data
            var removedCount = 0;

            var allEntries = _auditEntries.ToArray();
            var entriesToKeep = allEntries.Where(entry => entry.ChangedAt > cutoff).ToList();

            // Clear and re-add (not the most efficient, but simple for now)
            while (_auditEntries.TryDequeue(out _))
            {
                removedCount++;
            }

            foreach (var entry in entriesToKeep)
            {
                _auditEntries.Enqueue(entry);
            }

            var actuallyRemoved = removedCount - entriesToKeep.Count;
            if (actuallyRemoved > 0)
            {
                _logger.LogInformation("Cleaned up {RemovedCount} old configuration audit entries", actuallyRemoved);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error cleaning up old configuration audit entries");
        }

        await Task.CompletedTask;
    }

    public override void Dispose()
    {
        if (_hotReloadService != null)
        {
            _hotReloadService.ConfigurationChanged -= OnConfigurationChanged;
        }
        base.Dispose();
    }
}

// Extension methods for easier service registration
public static class ConfigurationAuditServiceExtensions
{
    public static IServiceCollection AddConfigurationAudit(
        this IServiceCollection services)
    {
        services.AddSingleton<IConfigurationAuditService, ConfigurationAuditService>();
        services.AddHostedService<ConfigurationAuditService>(provider =>
            (ConfigurationAuditService)provider.GetRequiredService<IConfigurationAuditService>());
        
        return services;
    }

    public static async Task<IServiceProvider> LogStartupConfigurationAsync(
        this IServiceProvider serviceProvider,
        string startupReason = "Application startup")
    {
        var auditService = serviceProvider.GetService<IConfigurationAuditService>();
        if (auditService != null)
        {
            var startupEntry = new ConfigurationChangeAuditEntry(
                Guid.NewGuid().ToString(),
                "Application",
                "Startup",
                null,
                "Started",
                DateTime.UtcNow,
                "System",
                startupReason,
                ConfigurationChangeSeverity.Info,
                new Dictionary<string, string>
                {
                    ["Event"] = "ApplicationStartup",
                    ["Environment"] = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Unknown"
                });

            await auditService.LogConfigurationChangeAsync(startupEntry);
        }

        return serviceProvider;
    }
}