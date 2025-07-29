using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PaymentGateway.Core.Configuration;
using System.Collections.Concurrent;
using System.Text.Json;

namespace PaymentGateway.Core.Services;

public interface IConfigurationHotReloadService
{
    Task<bool> ReloadConfigurationAsync();
    Task<ConfigurationReloadResult> ReloadConfigurationSectionAsync<T>(string sectionName) where T : class;
    Task<List<ConfigurationChange>> GetRecentChangesAsync(TimeSpan period);
    Task<bool> IsHotReloadEnabledAsync();
    
    event EventHandler<ConfigurationChangedEventArgs>? ConfigurationChanged;
}

public record ConfigurationReloadResult(
    bool IsSuccessful,
    List<ConfigurationChange> Changes,
    DateTime ReloadedAt,
    string? ErrorMessage);

public record ConfigurationChange(
    string SectionName,
    string PropertyPath,
    object? OldValue,
    object? NewValue,
    DateTime ChangedAt,
    string ChangeType); // "Added", "Modified", "Removed"

public class ConfigurationChangedEventArgs : EventArgs
{
    public List<ConfigurationChange> Changes { get; }
    public DateTime ChangedAt { get; }

    public ConfigurationChangedEventArgs(List<ConfigurationChange> changes, DateTime changedAt)
    {
        Changes = changes;
        ChangedAt = changedAt;
    }
}

public class ConfigurationHotReloadService : BackgroundService, IConfigurationHotReloadService
{
    private readonly ILogger<ConfigurationHotReloadService> _logger;
    private readonly IConfiguration _configuration;
    private readonly IServiceProvider _serviceProvider;
    private readonly IConfigurationValidationService _validationService;
    private readonly FeatureFlagsOptions _featureFlags;

    // Thread-safe collections for tracking configuration state
    private readonly ConcurrentDictionary<string, object> _lastKnownValues;
    private readonly ConcurrentQueue<ConfigurationChange> _recentChanges;
    private readonly Timer _reloadTimer;

    public event EventHandler<ConfigurationChangedEventArgs>? ConfigurationChanged;

    public ConfigurationHotReloadService(
        ILogger<ConfigurationHotReloadService> logger,
        IConfiguration configuration,
        IServiceProvider serviceProvider,
        IConfigurationValidationService validationService,
        IOptions<FeatureFlagsOptions> featureFlags)
    {
        _logger = logger;
        _configuration = configuration;
        _serviceProvider = serviceProvider;
        _validationService = validationService;
        _featureFlags = featureFlags.Value;
        _lastKnownValues = new ConcurrentDictionary<string, object>();
        _recentChanges = new ConcurrentQueue<ConfigurationChange>();

        // Set up periodic reload check
        _reloadTimer = new Timer(async _ => await CheckForConfigurationChangesAsync(),
            null, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));

        // Initialize baseline configuration
        _ = Task.Run(InitializeBaselineAsync);
    }

    public async Task<bool> ReloadConfigurationAsync()
    {
        if (!_featureFlags.EnableConfigurationHotReload)
        {
            _logger.LogWarning("Configuration hot reload is disabled via feature flag");
            return false;
        }

        try
        {
            _logger.LogInformation("Starting configuration hot reload");

            var changes = new List<ConfigurationChange>();

            // Reload each configuration section
            changes.AddRange(await ReloadSectionAsync<DatabaseOptions>(DatabaseOptions.SectionName));
            changes.AddRange(await ReloadSectionAsync<SecurityOptions>(SecurityOptions.SectionName));
            changes.AddRange(await ReloadSectionAsync<LoggingOptions>(LoggingOptions.SectionName));
            changes.AddRange(await ReloadSectionAsync<MetricsOptions>(MetricsOptions.SectionName));
            changes.AddRange(await ReloadSectionAsync<FeatureFlagsOptions>(FeatureFlagsOptions.SectionName));
            changes.AddRange(await ReloadSectionAsync<HealthCheckOptions>(HealthCheckOptions.SectionName));

            if (changes.Any())
            {
                // Validate configuration after reload
                var validationResult = await _validationService.ValidateConfigurationAsync();
                if (!validationResult.IsValid)
                {
                    var criticalErrors = validationResult.Issues.Where(i => 
                        i.Severity >= ConfigurationIssueSeverity.Error).ToList();
                    
                    if (criticalErrors.Any())
                    {
                        _logger.LogError("Configuration reload validation failed with {ErrorCount} errors",
                            criticalErrors.Count);
                        return false;
                    }
                }

                // Record changes
                foreach (var change in changes)
                {
                    _recentChanges.Enqueue(change);
                }

                // Clean up old changes (keep last 100)
                while (_recentChanges.Count > 100)
                {
                    _recentChanges.TryDequeue(out _);
                }

                // Notify listeners
                ConfigurationChanged?.Invoke(this, new ConfigurationChangedEventArgs(changes, DateTime.UtcNow));

                _logger.LogInformation("Configuration hot reload completed successfully with {ChangeCount} changes",
                    changes.Count);
            }
            else
            {
                _logger.LogDebug("Configuration hot reload completed with no changes detected");
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during configuration hot reload");
            return false;
        }
    }

    public async Task<ConfigurationReloadResult> ReloadConfigurationSectionAsync<T>(string sectionName) where T : class
    {
        if (!_featureFlags.EnableConfigurationHotReload)
        {
            return new ConfigurationReloadResult(false, new List<ConfigurationChange>(),
                DateTime.UtcNow, "Configuration hot reload is disabled");
        }

        try
        {
            var changes = await ReloadSectionAsync<T>(sectionName);
            
            if (changes.Any())
            {
                ConfigurationChanged?.Invoke(this, new ConfigurationChangedEventArgs(changes, DateTime.UtcNow));
            }

            return new ConfigurationReloadResult(true, changes, DateTime.UtcNow, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reloading configuration section {SectionName}", sectionName);
            return new ConfigurationReloadResult(false, new List<ConfigurationChange>(),
                DateTime.UtcNow, ex.Message);
        }
    }

    public async Task<List<ConfigurationChange>> GetRecentChangesAsync(TimeSpan period)
    {
        var cutoff = DateTime.UtcNow - period;
        return await Task.FromResult(_recentChanges
            .Where(c => c.ChangedAt > cutoff)
            .OrderByDescending(c => c.ChangedAt)
            .ToList());
    }

    public async Task<bool> IsHotReloadEnabledAsync()
    {
        return await Task.FromResult(_featureFlags.EnableConfigurationHotReload);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (_featureFlags.EnableConfigurationHotReload)
                {
                    await CheckForConfigurationChangesAsync();
                }

                await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in configuration hot reload background service");
                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
            }
        }
    }

    private async Task InitializeBaselineAsync()
    {
        try
        {
            await CaptureCurrentConfigurationState<DatabaseOptions>(DatabaseOptions.SectionName);
            await CaptureCurrentConfigurationState<SecurityOptions>(SecurityOptions.SectionName);
            await CaptureCurrentConfigurationState<LoggingOptions>(LoggingOptions.SectionName);
            await CaptureCurrentConfigurationState<MetricsOptions>(MetricsOptions.SectionName);
            await CaptureCurrentConfigurationState<FeatureFlagsOptions>(FeatureFlagsOptions.SectionName);
            await CaptureCurrentConfigurationState<HealthCheckOptions>(HealthCheckOptions.SectionName);

            _logger.LogInformation("Configuration baseline initialized for hot reload monitoring");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error initializing configuration baseline");
        }
    }

    private async Task CheckForConfigurationChangesAsync()
    {
        if (!_featureFlags.EnableConfigurationHotReload)
            return;

        try
        {
            var hasChanges = false;

            // Check each section for changes
            hasChanges |= await HasSectionChangedAsync<DatabaseOptions>(DatabaseOptions.SectionName);
            hasChanges |= await HasSectionChangedAsync<SecurityOptions>(SecurityOptions.SectionName);
            hasChanges |= await HasSectionChangedAsync<LoggingOptions>(LoggingOptions.SectionName);
            hasChanges |= await HasSectionChangedAsync<MetricsOptions>(MetricsOptions.SectionName);
            hasChanges |= await HasSectionChangedAsync<FeatureFlagsOptions>(FeatureFlagsOptions.SectionName);
            hasChanges |= await HasSectionChangedAsync<HealthCheckOptions>(HealthCheckOptions.SectionName);

            if (hasChanges)
            {
                _logger.LogInformation("Configuration changes detected, triggering hot reload");
                await ReloadConfigurationAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking for configuration changes");
        }
    }

    private async Task<List<ConfigurationChange>> ReloadSectionAsync<T>(string sectionName) where T : class
    {
        var changes = new List<ConfigurationChange>();

        try
        {
            // Get current configuration
            var currentConfig = _configuration.GetSection(sectionName).Get<T>();
            if (currentConfig == null)
                return changes;

            // Get previous configuration
            var previousConfigKey = $"{sectionName}:current";
            if (_lastKnownValues.TryGetValue(previousConfigKey, out var previousConfigObj) &&
                previousConfigObj is T previousConfig)
            {
                // Compare configurations
                changes.AddRange(CompareConfigurations(sectionName, previousConfig, currentConfig));
            }

            // Update stored configuration
            _lastKnownValues[previousConfigKey] = currentConfig;

            // Refresh the options in DI container if possible
            await RefreshOptionsInContainer<T>();

            _logger.LogDebug("Reloaded configuration section {SectionName} with {ChangeCount} changes",
                sectionName, changes.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reloading configuration section {SectionName}", sectionName);
        }

        return changes;
    }

    private async Task<bool> HasSectionChangedAsync<T>(string sectionName) where T : class
    {
        try
        {
            var currentConfig = _configuration.GetSection(sectionName).Get<T>();
            if (currentConfig == null)
                return false;

            var previousConfigKey = $"{sectionName}:current";
            if (_lastKnownValues.TryGetValue(previousConfigKey, out var previousConfigObj) &&
                previousConfigObj is T previousConfig)
            {
                var currentJson = JsonSerializer.Serialize(currentConfig);
                var previousJson = JsonSerializer.Serialize(previousConfig);
                return !string.Equals(currentJson, previousJson, StringComparison.Ordinal);
            }

            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking if section {SectionName} has changed", sectionName);
            return false;
        }
    }

    private async Task CaptureCurrentConfigurationState<T>(string sectionName) where T : class
    {
        try
        {
            var currentConfig = _configuration.GetSection(sectionName).Get<T>();
            if (currentConfig != null)
            {
                _lastKnownValues[$"{sectionName}:current"] = currentConfig;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error capturing configuration state for section {SectionName}", sectionName);
        }

        await Task.CompletedTask;
    }

    private List<ConfigurationChange> CompareConfigurations<T>(string sectionName, T oldConfig, T newConfig) where T : class
    {
        var changes = new List<ConfigurationChange>();

        try
        {
            var oldJson = JsonSerializer.Serialize(oldConfig, new JsonSerializerOptions { WriteIndented = true });
            var newJson = JsonSerializer.Serialize(newConfig, new JsonSerializerOptions { WriteIndented = true });

            if (!string.Equals(oldJson, newJson, StringComparison.Ordinal))
            {
                // For simplicity, we'll treat the entire section as changed
                // In a more sophisticated implementation, you could compare individual properties
                changes.Add(new ConfigurationChange(
                    sectionName,
                    "Section",
                    oldConfig,
                    newConfig,
                    DateTime.UtcNow,
                    "Modified"));
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error comparing configurations for section {SectionName}", sectionName);
        }

        return changes;
    }

    private async Task RefreshOptionsInContainer<T>() where T : class
    {
        try
        {
            // This is a simplified approach. In a full implementation,
            // you might need to refresh specific IOptionsMonitor instances
            var optionsMonitor = _serviceProvider.GetService<IOptionsMonitor<T>>();
            if (optionsMonitor != null)
            {
                // The IOptionsMonitor should automatically pick up changes
                // from IConfiguration due to the change token mechanism
                _logger.LogDebug("Options monitor for {OptionsType} should refresh automatically", typeof(T).Name);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error refreshing options for type {OptionsType}", typeof(T).Name);
        }

        await Task.CompletedTask;
    }

    public override void Dispose()
    {
        _reloadTimer?.Dispose();
        base.Dispose();
    }
}

// Extension methods for easier service registration
public static class ConfigurationHotReloadServiceExtensions
{
    public static IServiceCollection AddConfigurationHotReload(
        this IServiceCollection services)
    {
        services.AddSingleton<IConfigurationHotReloadService, ConfigurationHotReloadService>();
        services.AddHostedService<ConfigurationHotReloadService>(provider =>
            (ConfigurationHotReloadService)provider.GetRequiredService<IConfigurationHotReloadService>());
        
        return services;
    }

    public static IServiceCollection ConfigureOptionsWithHotReload<T>(
        this IServiceCollection services,
        IConfiguration configuration,
        string sectionName) where T : class
    {
        // Configure options with change token support for hot reload
        services.Configure<T>(configuration.GetSection(sectionName));
        
        // Add options validation
        services.AddOptions<T>()
            .Bind(configuration.GetSection(sectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        return services;
    }
}

// Configuration change audit service
public interface IConfigurationChangeAuditService
{
    Task LogConfigurationChangeAsync(ConfigurationChange change);
    Task<List<ConfigurationChange>> GetConfigurationHistoryAsync(TimeSpan period);
    Task<List<ConfigurationChange>> GetConfigurationHistoryForSectionAsync(string sectionName, TimeSpan period);
}

public class ConfigurationChangeAuditService : IConfigurationChangeAuditService
{
    private readonly ILogger<ConfigurationChangeAuditService> _logger;
    private readonly ConcurrentQueue<ConfigurationChange> _auditTrail;

    public ConfigurationChangeAuditService(ILogger<ConfigurationChangeAuditService> logger)
    {
        _logger = logger;
        _auditTrail = new ConcurrentQueue<ConfigurationChange>();
    }

    public async Task LogConfigurationChangeAsync(ConfigurationChange change)
    {
        _auditTrail.Enqueue(change);
        
        // Keep only last 1000 changes
        while (_auditTrail.Count > 1000)
        {
            _auditTrail.TryDequeue(out _);
        }

        _logger.LogInformation("Configuration change recorded: {SectionName}.{PropertyPath} {ChangeType} at {ChangedAt}",
            change.SectionName, change.PropertyPath, change.ChangeType, change.ChangedAt);

        await Task.CompletedTask;
    }

    public async Task<List<ConfigurationChange>> GetConfigurationHistoryAsync(TimeSpan period)
    {
        var cutoff = DateTime.UtcNow - period;
        return await Task.FromResult(_auditTrail
            .Where(c => c.ChangedAt > cutoff)
            .OrderByDescending(c => c.ChangedAt)
            .ToList());
    }

    public async Task<List<ConfigurationChange>> GetConfigurationHistoryForSectionAsync(string sectionName, TimeSpan period)
    {
        var cutoff = DateTime.UtcNow - period;
        return await Task.FromResult(_auditTrail
            .Where(c => c.ChangedAt > cutoff && c.SectionName == sectionName)
            .OrderByDescending(c => c.ChangedAt)
            .ToList());
    }
}