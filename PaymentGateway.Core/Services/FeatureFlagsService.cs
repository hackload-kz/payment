using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PaymentGateway.Core.Configuration;
using System.Collections.Concurrent;
using System.Text.Json;

namespace PaymentGateway.Core.Services;

public interface IFeatureFlagsService
{
    Task<bool> IsEnabledAsync(string featureName);
    Task<bool> IsEnabledForTeamAsync(string featureName, string teamSlug);
    Task<T> GetFeatureValueAsync<T>(string featureName, T defaultValue = default!);
    Task<T> GetFeatureValueForTeamAsync<T>(string featureName, string teamSlug, T defaultValue = default!);
    Task SetFeatureFlagAsync(string featureName, bool enabled, string? teamSlug = null);
    Task SetFeatureValueAsync<T>(string featureName, T value, string? teamSlug = null);
    Task<Dictionary<string, object>> GetAllFeatureFlagsAsync();
    Task<Dictionary<string, object>> GetTeamFeatureFlagsAsync(string teamSlug);
    Task<List<FeatureFlagAuditEntry>> GetFeatureFlagHistoryAsync(string featureName, TimeSpan period);
}

public record FeatureFlagAuditEntry(
    string FeatureName,
    string? TeamSlug,
    object? OldValue,
    object? NewValue,
    DateTime ChangedAt,
    string ChangedBy,
    string ChangeReason);

public class FeatureFlagsService : IFeatureFlagsService
{
    private readonly ILogger<FeatureFlagsService> _logger;
    private readonly IConfiguration _configuration;
    private readonly IOptionsMonitor<FeatureFlagsOptions> _featureFlagsOptions;
    
    // In-memory cache for feature flags
    private readonly ConcurrentDictionary<string, object> _globalFeatureFlags;
    private readonly ConcurrentDictionary<string, Dictionary<string, object>> _teamFeatureFlags;
    private readonly ConcurrentQueue<FeatureFlagAuditEntry> _auditTrail;

    public FeatureFlagsService(
        ILogger<FeatureFlagsService> logger,
        IConfiguration configuration,
        IOptionsMonitor<FeatureFlagsOptions> featureFlagsOptions)
    {
        _logger = logger;
        _configuration = configuration;
        _featureFlagsOptions = featureFlagsOptions;
        _globalFeatureFlags = new ConcurrentDictionary<string, object>();
        _teamFeatureFlags = new ConcurrentDictionary<string, Dictionary<string, object>>();
        _auditTrail = new ConcurrentQueue<FeatureFlagAuditEntry>();

        // Initialize feature flags from configuration
        InitializeFeatureFlags();

        // Monitor configuration changes
        _featureFlagsOptions.OnChange(OnFeatureFlagsOptionsChanged);
    }

    public async Task<bool> IsEnabledAsync(string featureName)
    {
        ArgumentException.ThrowIfNullOrEmpty(featureName);

        try
        {
            // Check runtime feature flags first
            if (_globalFeatureFlags.TryGetValue(featureName, out var runtimeValue))
            {
                if (runtimeValue is bool boolValue)
                {
                    _logger.LogDebug("Feature flag {FeatureName} resolved from runtime cache: {IsEnabled}",
                        featureName, boolValue);
                    return boolValue;
                }
            }

            // Check configuration
            var configValue = await GetConfigurationValueAsync<bool>(featureName);
            if (configValue.HasValue)
            {
                _logger.LogDebug("Feature flag {FeatureName} resolved from configuration: {IsEnabled}",
                    featureName, configValue.Value);
                return configValue.Value;
            }

            // Check built-in feature flags
            var builtInValue = GetBuiltInFeatureFlag(featureName);
            if (builtInValue.HasValue)
            {
                _logger.LogDebug("Feature flag {FeatureName} resolved from built-in defaults: {IsEnabled}",
                    featureName, builtInValue.Value);
                return builtInValue.Value;
            }

            // Default to false for unknown features
            _logger.LogWarning("Unknown feature flag {FeatureName}, defaulting to false", featureName);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error resolving feature flag {FeatureName}, defaulting to false", featureName);
            return false;
        }
    }

    public async Task<bool> IsEnabledForTeamAsync(string featureName, string teamSlug)
    {
        ArgumentException.ThrowIfNullOrEmpty(featureName);
        ArgumentException.ThrowIfNullOrEmpty(teamSlug);

        try
        {
            // Check team-specific feature flags first
            if (_teamFeatureFlags.TryGetValue(teamSlug, out var teamFlags) &&
                teamFlags.TryGetValue(featureName, out var teamValue))
            {
                if (teamValue is bool boolValue)
                {
                    _logger.LogDebug("Feature flag {FeatureName} for team {TeamSlug} resolved from team settings: {IsEnabled}",
                        featureName, teamSlug, boolValue);
                    return boolValue;
                }
            }

            // Fall back to global feature flag
            return await IsEnabledAsync(featureName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error resolving feature flag {FeatureName} for team {TeamSlug}, defaulting to false",
                featureName, teamSlug);
            return false;
        }
    }

    public async Task<T> GetFeatureValueAsync<T>(string featureName, T defaultValue = default!)
    {
        ArgumentException.ThrowIfNullOrEmpty(featureName);

        try
        {
            // Check runtime feature flags first
            if (_globalFeatureFlags.TryGetValue(featureName, out var runtimeValue))
            {
                if (runtimeValue is T typedValue)
                {
                    _logger.LogDebug("Feature value {FeatureName} resolved from runtime cache: {Value}",
                        featureName, typedValue);
                    return typedValue;
                }

                // Try to convert the value
                if (TryConvertValue<T>(runtimeValue, out var convertedValue))
                {
                    return convertedValue;
                }
            }

            // Check configuration
            var configValue = await GetConfigurationValueAsync<T>(featureName);
            if (configValue is not null)
            {
                _logger.LogDebug("Feature value {FeatureName} resolved from configuration: {Value}",
                    featureName, configValue);
                return configValue;
            }

            // Return default value
            _logger.LogDebug("Feature value {FeatureName} not found, using default: {DefaultValue}",
                featureName, defaultValue);
            return defaultValue;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting feature value {FeatureName}, using default: {DefaultValue}",
                featureName, defaultValue);
            return defaultValue;
        }
    }

    public async Task<T> GetFeatureValueForTeamAsync<T>(string featureName, string teamSlug, T defaultValue = default!)
    {
        ArgumentException.ThrowIfNullOrEmpty(featureName);
        ArgumentException.ThrowIfNullOrEmpty(teamSlug);

        try
        {
            // Check team-specific feature flags first
            if (_teamFeatureFlags.TryGetValue(teamSlug, out var teamFlags) &&
                teamFlags.TryGetValue(featureName, out var teamValue))
            {
                if (teamValue is T typedValue)
                {
                    _logger.LogDebug("Feature value {FeatureName} for team {TeamSlug} resolved from team settings: {Value}",
                        featureName, teamSlug, typedValue);
                    return typedValue;
                }

                // Try to convert the value
                if (TryConvertValue<T>(teamValue, out var convertedValue))
                {
                    return convertedValue;
                }
            }

            // Fall back to global feature value
            return await GetFeatureValueAsync(featureName, defaultValue);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting feature value {FeatureName} for team {TeamSlug}, using default: {DefaultValue}",
                featureName, teamSlug, defaultValue);
            return defaultValue;
        }
    }

    public async Task SetFeatureFlagAsync(string featureName, bool enabled, string? teamSlug = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(featureName);

        try
        {
            var oldValue = teamSlug != null
                ? await GetFeatureValueForTeamAsync<bool?>(featureName, teamSlug, null)
                : await GetFeatureValueAsync<bool?>(featureName, null);

            if (teamSlug != null)
            {
                // Set team-specific feature flag
                var teamFlags = _teamFeatureFlags.GetOrAdd(teamSlug, _ => new Dictionary<string, object>());
                lock (teamFlags)
                {
                    teamFlags[featureName] = enabled;
                }

                _logger.LogInformation("Feature flag {FeatureName} set to {Enabled} for team {TeamSlug}",
                    featureName, enabled, teamSlug);
            }
            else
            {
                // Set global feature flag
                _globalFeatureFlags[featureName] = enabled;

                _logger.LogInformation("Global feature flag {FeatureName} set to {Enabled}",
                    featureName, enabled);
            }

            // Record audit entry
            _auditTrail.Enqueue(new FeatureFlagAuditEntry(
                featureName,
                teamSlug,
                oldValue,
                enabled,
                DateTime.UtcNow,
                "System", // In a real implementation, you'd get the current user
                "Feature flag updated via API"));

            // Clean up audit trail (keep last 1000 entries)
            while (_auditTrail.Count > 1000)
            {
                _auditTrail.TryDequeue(out _);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error setting feature flag {FeatureName} to {Enabled} for team {TeamSlug}",
                featureName, enabled, teamSlug);
            throw new InvalidOperationException($"Failed to set feature flag {featureName}", ex);
        }

        await Task.CompletedTask;
    }

    public async Task SetFeatureValueAsync<T>(string featureName, T value, string? teamSlug = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(featureName);

        try
        {
            var oldValue = teamSlug != null
                ? await GetFeatureValueForTeamAsync<T>(featureName, teamSlug, default!)
                : await GetFeatureValueAsync<T>(featureName, default!);

            if (teamSlug != null)
            {
                // Set team-specific feature value
                var teamFlags = _teamFeatureFlags.GetOrAdd(teamSlug, _ => new Dictionary<string, object>());
                lock (teamFlags)
                {
                    teamFlags[featureName] = value!;
                }

                _logger.LogInformation("Feature value {FeatureName} set to {Value} for team {TeamSlug}",
                    featureName, value, teamSlug);
            }
            else
            {
                // Set global feature value
                _globalFeatureFlags[featureName] = value!;

                _logger.LogInformation("Global feature value {FeatureName} set to {Value}",
                    featureName, value);
            }

            // Record audit entry
            _auditTrail.Enqueue(new FeatureFlagAuditEntry(
                featureName,
                teamSlug,
                oldValue,
                value,
                DateTime.UtcNow,
                "System",
                "Feature value updated via API"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error setting feature value {FeatureName} to {Value} for team {TeamSlug}",
                featureName, value, teamSlug);
            throw new InvalidOperationException($"Failed to set feature value {featureName}", ex);
        }

        await Task.CompletedTask;
    }

    public async Task<Dictionary<string, object>> GetAllFeatureFlagsAsync()
    {
        var result = new Dictionary<string, object>();

        // Add built-in feature flags
        var options = _featureFlagsOptions.CurrentValue;
        result["EnableAdvancedMetrics"] = options.EnableAdvancedMetrics;
        result["EnableSecurityAudit"] = options.EnableSecurityAudit;
        result["EnableTokenExpiration"] = options.EnableTokenExpiration;
        result["EnableRateLimit"] = options.EnableRateLimit;
        result["EnableHttpsEnforcement"] = options.EnableHttpsEnforcement;
        result["EnableConfigurationValidation"] = options.EnableConfigurationValidation;
        result["EnableConfigurationHotReload"] = options.EnableConfigurationHotReload;
        result["EnablePaymentQueueing"] = options.EnablePaymentQueueing;
        result["EnableConcurrencyMetrics"] = options.EnableConcurrencyMetrics;
        result["EnableErrorAnalytics"] = options.EnableErrorAnalytics;

        // Add runtime feature flags
        foreach (var kvp in _globalFeatureFlags)
        {
            result[kvp.Key] = kvp.Value;
        }

        return await Task.FromResult(result);
    }

    public async Task<Dictionary<string, object>> GetTeamFeatureFlagsAsync(string teamSlug)
    {
        ArgumentException.ThrowIfNullOrEmpty(teamSlug);

        if (_teamFeatureFlags.TryGetValue(teamSlug, out var teamFlags))
        {
            lock (teamFlags)
            {
                return await Task.FromResult(new Dictionary<string, object>(teamFlags));
            }
        }

        return await Task.FromResult(new Dictionary<string, object>());
    }

    public async Task<List<FeatureFlagAuditEntry>> GetFeatureFlagHistoryAsync(string featureName, TimeSpan period)
    {
        ArgumentException.ThrowIfNullOrEmpty(featureName);

        var cutoff = DateTime.UtcNow - period;
        return await Task.FromResult(_auditTrail
            .Where(entry => entry.FeatureName == featureName && entry.ChangedAt > cutoff)
            .OrderByDescending(entry => entry.ChangedAt)
            .ToList());
    }

    private void InitializeFeatureFlags()
    {
        try
        {
            // Load any custom feature flags from configuration
            var customFlagsSection = _configuration.GetSection("CustomFeatureFlags");
            foreach (var child in customFlagsSection.GetChildren())
            {
                if (bool.TryParse(child.Value, out var boolValue))
                {
                    _globalFeatureFlags[child.Key] = boolValue;
                }
                else if (int.TryParse(child.Value, out var intValue))
                {
                    _globalFeatureFlags[child.Key] = intValue;
                }
                else
                {
                    _globalFeatureFlags[child.Key] = child.Value ?? string.Empty;
                }
            }

            _logger.LogInformation("Initialized {Count} custom feature flags from configuration",
                _globalFeatureFlags.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error initializing feature flags");
        }
    }

    private void OnFeatureFlagsOptionsChanged(FeatureFlagsOptions options)
    {
        _logger.LogInformation("Feature flags options changed, updating cache");
        // The options monitor will automatically provide updated values when accessed
    }

    private async Task<T?> GetConfigurationValueAsync<T>(string featureName)
    {
        try
        {
            var configPath = $"FeatureFlags:{featureName}";
            var configValue = _configuration[configPath];
            
            if (configValue != null)
            {
                if (typeof(T) == typeof(bool) && bool.TryParse(configValue, out var boolValue))
                {
                    return (T)(object)boolValue;
                }
                
                if (typeof(T) == typeof(int) && int.TryParse(configValue, out var intValue))
                {
                    return (T)(object)intValue;
                }
                
                if (typeof(T) == typeof(string))
                {
                    return (T)(object)configValue;
                }

                // Try JSON deserialization for complex types
                if (!typeof(T).IsPrimitive && typeof(T) != typeof(string))
                {
                    try
                    {
                        return JsonSerializer.Deserialize<T>(configValue);
                    }
                    catch (JsonException)
                    {
                        // Fall through to return default
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error getting configuration value for feature {FeatureName}", featureName);
        }

        return await Task.FromResult(default(T));
    }

    private bool? GetBuiltInFeatureFlag(string featureName)
    {
        var options = _featureFlagsOptions.CurrentValue;
        
        return featureName switch
        {
            "EnableAdvancedMetrics" => options.EnableAdvancedMetrics,
            "EnableSecurityAudit" => options.EnableSecurityAudit,
            "EnableTokenExpiration" => options.EnableTokenExpiration,
            "EnableRateLimit" => options.EnableRateLimit,
            "EnableHttpsEnforcement" => options.EnableHttpsEnforcement,
            "EnableConfigurationValidation" => options.EnableConfigurationValidation,
            "EnableConfigurationHotReload" => options.EnableConfigurationHotReload,
            "EnablePaymentQueueing" => options.EnablePaymentQueueing,
            "EnableConcurrencyMetrics" => options.EnableConcurrencyMetrics,
            "EnableErrorAnalytics" => options.EnableErrorAnalytics,
            _ => null
        };
    }

    private static bool TryConvertValue<T>(object value, out T convertedValue)
    {
        convertedValue = default!;

        try
        {
            if (value is T directValue)
            {
                convertedValue = directValue;
                return true;
            }

            if (typeof(T) == typeof(bool) && value is string stringValue)
            {
                if (bool.TryParse(stringValue, out var boolValue))
                {
                    convertedValue = (T)(object)boolValue;
                    return true;
                }
            }

            if (typeof(T) == typeof(int) && value is string intStringValue)
            {
                if (int.TryParse(intStringValue, out var intValue))
                {
                    convertedValue = (T)(object)intValue;
                    return true;
                }
            }

            // Try general conversion
            convertedValue = (T)Convert.ChangeType(value, typeof(T));
            return true;
        }
        catch
        {
            return false;
        }
    }
}

// Extension methods for easier feature flag usage
public static class FeatureFlagsServiceExtensions
{
    public static IServiceCollection AddFeatureFlags(
        this IServiceCollection services)
    {
        services.AddSingleton<IFeatureFlagsService, FeatureFlagsService>();
        return services;
    }

    // Convenience methods for common feature flags
    public static async Task<bool> IsAdvancedMetricsEnabledAsync(this IFeatureFlagsService featureFlags)
    {
        return await featureFlags.IsEnabledAsync("EnableAdvancedMetrics");
    }

    public static async Task<bool> IsSecurityAuditEnabledAsync(this IFeatureFlagsService featureFlags)
    {
        return await featureFlags.IsEnabledAsync("EnableSecurityAudit");
    }

    public static async Task<bool> IsTokenExpirationEnabledAsync(this IFeatureFlagsService featureFlags)
    {
        return await featureFlags.IsEnabledAsync("EnableTokenExpiration");
    }

    public static async Task<bool> IsRateLimitEnabledAsync(this IFeatureFlagsService featureFlags)
    {
        return await featureFlags.IsEnabledAsync("EnableRateLimit");
    }

    public static async Task<bool> IsHttpsEnforcementEnabledAsync(this IFeatureFlagsService featureFlags)
    {
        return await featureFlags.IsEnabledAsync("EnableHttpsEnforcement");
    }

    public static async Task<bool> IsConfigurationValidationEnabledAsync(this IFeatureFlagsService featureFlags)
    {
        return await featureFlags.IsEnabledAsync("EnableConfigurationValidation");
    }

    public static async Task<bool> IsConfigurationHotReloadEnabledAsync(this IFeatureFlagsService featureFlags)
    {
        return await featureFlags.IsEnabledAsync("EnableConfigurationHotReload");
    }
}