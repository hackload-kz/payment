using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PaymentGateway.Core.Configuration;
using System.ComponentModel.DataAnnotations;
using System.Reflection;

namespace PaymentGateway.Core.Services;

public interface IConfigurationValidationService
{
    Task<ConfigurationValidationResult> ValidateConfigurationAsync();
    Task<ConfigurationValidationResult> ValidateConfigurationSectionAsync<T>(T options) where T : class;
    Task<List<ConfigurationIssue>> GetConfigurationIssuesAsync();
    Task<bool> IsConfigurationValidAsync();
}

public record ConfigurationValidationResult(
    bool IsValid,
    List<ConfigurationIssue> Issues,
    DateTime ValidatedAt,
    string Environment);

public record ConfigurationIssue(
    ConfigurationIssueSeverity Severity,
    string Category,
    string Property,
    string Issue,
    string? RecommendedAction,
    string? CurrentValue);

public enum ConfigurationIssueSeverity
{
    Info = 1,
    Warning = 2,
    Error = 3,
    Critical = 4
}

public class ConfigurationValidationService : IConfigurationValidationService
{
    private readonly ILogger<ConfigurationValidationService> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly string _environment;

    public ConfigurationValidationService(
        ILogger<ConfigurationValidationService> logger,
        IServiceProvider serviceProvider)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
        _environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production";
    }

    public async Task<ConfigurationValidationResult> ValidateConfigurationAsync()
    {
        var issues = new List<ConfigurationIssue>();

        try
        {
            // Validate Database Configuration
            var databaseOptions = _serviceProvider.GetService<IOptions<DatabaseOptions>>();
            if (databaseOptions != null)
            {
                var dbValidation = await ValidateConfigurationSectionAsync(databaseOptions.Value);
                issues.AddRange(dbValidation.Issues);
            }

            // Validate Security Configuration
            var securityOptions = _serviceProvider.GetService<IOptions<SecurityOptions>>();
            if (securityOptions != null)
            {
                var securityValidation = await ValidateConfigurationSectionAsync(securityOptions.Value);
                issues.AddRange(securityValidation.Issues);
                issues.AddRange(await ValidateSecuritySpecificRulesAsync(securityOptions.Value));
            }

            // Validate Logging Configuration
            var loggingOptions = _serviceProvider.GetService<IOptions<LoggingOptions>>();
            if (loggingOptions != null)
            {
                var loggingValidation = await ValidateConfigurationSectionAsync(loggingOptions.Value);
                issues.AddRange(loggingValidation.Issues);
                issues.AddRange(await ValidateLoggingSpecificRulesAsync(loggingOptions.Value));
            }

            // Validate Metrics Configuration
            var metricsOptions = _serviceProvider.GetService<IOptions<MetricsOptions>>();
            if (metricsOptions != null)
            {
                var metricsValidation = await ValidateConfigurationSectionAsync(metricsOptions.Value);
                issues.AddRange(metricsValidation.Issues);
            }

            // Validate Feature Flags Configuration
            var featureFlagsOptions = _serviceProvider.GetService<IOptions<FeatureFlagsOptions>>();
            if (featureFlagsOptions != null)
            {
                var featureFlagsValidation = await ValidateConfigurationSectionAsync(featureFlagsOptions.Value);
                issues.AddRange(featureFlagsValidation.Issues);
                issues.AddRange(await ValidateFeatureFlagsSpecificRulesAsync(featureFlagsOptions.Value));
            }

            // Environment-specific validations
            issues.AddRange(await ValidateEnvironmentSpecificRulesAsync());

            var isValid = !issues.Any(i => i.Severity >= ConfigurationIssueSeverity.Error);

            _logger.LogInformation("Configuration validation completed. Valid: {IsValid}, Issues: {IssueCount}",
                isValid, issues.Count);

            return new ConfigurationValidationResult(isValid, issues, DateTime.UtcNow, _environment);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during configuration validation");
            issues.Add(new ConfigurationIssue(
                ConfigurationIssueSeverity.Critical,
                "Validation",
                "System",
                "Configuration validation failed with exception",
                "Check application logs for details",
                ex.Message));

            return new ConfigurationValidationResult(false, issues, DateTime.UtcNow, _environment);
        }
    }

    public async Task<ConfigurationValidationResult> ValidateConfigurationSectionAsync<T>(T options) where T : class
    {
        var issues = new List<ConfigurationIssue>();
        var context = new ValidationContext(options);
        var validationResults = new List<System.ComponentModel.DataAnnotations.ValidationResult>();

        // Data annotations validation
        if (!Validator.TryValidateObject(options, context, validationResults, true))
        {
            foreach (var validationResult in validationResults)
            {
                var memberName = validationResult.MemberNames.FirstOrDefault() ?? "Unknown";
                issues.Add(new ConfigurationIssue(
                    ConfigurationIssueSeverity.Error,
                    typeof(T).Name,
                    memberName,
                    validationResult.ErrorMessage ?? "Validation failed",
                    "Fix the configuration value according to the validation requirements",
                    GetPropertyValue(options, memberName)?.ToString()));
            }
        }

        var isValid = !issues.Any(i => i.Severity >= ConfigurationIssueSeverity.Error);
        return await Task.FromResult(new ConfigurationValidationResult(isValid, issues, DateTime.UtcNow, _environment));
    }

    public async Task<List<ConfigurationIssue>> GetConfigurationIssuesAsync()
    {
        var validationResult = await ValidateConfigurationAsync();
        return validationResult.Issues;
    }

    public async Task<bool> IsConfigurationValidAsync()
    {
        var validationResult = await ValidateConfigurationAsync();
        return validationResult.IsValid;
    }

    private async Task<List<ConfigurationIssue>> ValidateSecuritySpecificRulesAsync(SecurityOptions securityOptions)
    {
        var issues = new List<ConfigurationIssue>();

        // Production security checks
        if (_environment.Equals("Production", StringComparison.OrdinalIgnoreCase))
        {
            if (!securityOptions.Https.RequireHttps)
            {
                issues.Add(new ConfigurationIssue(
                    ConfigurationIssueSeverity.Critical,
                    "Security",
                    "Https.RequireHttps",
                    "HTTPS is not required in production environment",
                    "Enable HTTPS requirement for production",
                    securityOptions.Https.RequireHttps.ToString()));
            }

            if (!securityOptions.Https.EnableHsts)
            {
                issues.Add(new ConfigurationIssue(
                    ConfigurationIssueSeverity.Error,
                    "Security",
                    "Https.EnableHsts",
                    "HSTS is not enabled in production environment",
                    "Enable HSTS for production security",
                    securityOptions.Https.EnableHsts.ToString()));
            }

            if (securityOptions.Authentication.TokenExpirationMinutes > 60)
            {
                issues.Add(new ConfigurationIssue(
                    ConfigurationIssueSeverity.Warning,
                    "Security",
                    "Authentication.TokenExpirationMinutes",
                    "Token expiration time is longer than recommended for production",
                    "Consider reducing token expiration time to 30-60 minutes",
                    securityOptions.Authentication.TokenExpirationMinutes.ToString()));
            }
        }

        // Check for weak authentication settings
        if (securityOptions.Authentication.MaxFailedAttempts > 10)
        {
            issues.Add(new ConfigurationIssue(
                ConfigurationIssueSeverity.Warning,
                "Security",
                "Authentication.MaxFailedAttempts",
                "Max failed attempts is set too high",
                "Consider reducing to 3-5 attempts for better security",
                securityOptions.Authentication.MaxFailedAttempts.ToString()));
        }

        return await Task.FromResult(issues);
    }

    private async Task<List<ConfigurationIssue>> ValidateLoggingSpecificRulesAsync(LoggingOptions loggingOptions)
    {
        var issues = new List<ConfigurationIssue>();

        // Production logging checks
        if (_environment.Equals("Production", StringComparison.OrdinalIgnoreCase))
        {
            if (!loggingOptions.Audit.EnableAuditLogging)
            {
                issues.Add(new ConfigurationIssue(
                    ConfigurationIssueSeverity.Critical,
                    "Logging",
                    "Audit.EnableAuditLogging",
                    "Audit logging is disabled in production environment",
                    "Enable audit logging for compliance and security",
                    loggingOptions.Audit.EnableAuditLogging.ToString()));
            }

            if (!loggingOptions.Audit.EnableSensitiveDataMasking)
            {
                issues.Add(new ConfigurationIssue(
                    ConfigurationIssueSeverity.Error,
                    "Logging",
                    "Audit.EnableSensitiveDataMasking",
                    "Sensitive data masking is disabled in production",
                    "Enable sensitive data masking to protect customer data",
                    loggingOptions.Audit.EnableSensitiveDataMasking.ToString()));
            }

            if (loggingOptions.Serilog.EnableConsole)
            {
                issues.Add(new ConfigurationIssue(
                    ConfigurationIssueSeverity.Warning,
                    "Logging",
                    "Serilog.EnableConsole",
                    "Console logging is enabled in production",
                    "Consider disabling console logging in production for performance",
                    loggingOptions.Serilog.EnableConsole.ToString()));
            }
        }

        // Development logging checks
        if (_environment.Equals("Development", StringComparison.OrdinalIgnoreCase))
        {
            if (loggingOptions.Audit.EnableSensitiveDataMasking)
            {
                issues.Add(new ConfigurationIssue(
                    ConfigurationIssueSeverity.Info,
                    "Logging",
                    "Audit.EnableSensitiveDataMasking",
                    "Sensitive data masking is enabled in development",
                    "Consider disabling in development for easier debugging",
                    loggingOptions.Audit.EnableSensitiveDataMasking.ToString()));
            }
        }

        return await Task.FromResult(issues);
    }

    private async Task<List<ConfigurationIssue>> ValidateFeatureFlagsSpecificRulesAsync(FeatureFlagsOptions featureFlagsOptions)
    {
        var issues = new List<ConfigurationIssue>();

        // Production feature flag checks
        if (_environment.Equals("Production", StringComparison.OrdinalIgnoreCase))
        {
            if (featureFlagsOptions.EnableConfigurationHotReload)
            {
                issues.Add(new ConfigurationIssue(
                    ConfigurationIssueSeverity.Warning,
                    "FeatureFlags",
                    "EnableConfigurationHotReload",
                    "Configuration hot reload is enabled in production",
                    "Consider disabling hot reload in production for stability",
                    featureFlagsOptions.EnableConfigurationHotReload.ToString()));
            }

            if (!featureFlagsOptions.EnableSecurityAudit)
            {
                issues.Add(new ConfigurationIssue(
                    ConfigurationIssueSeverity.Error,
                    "FeatureFlags",
                    "EnableSecurityAudit",
                    "Security audit is disabled in production",
                    "Enable security audit for production compliance",
                    featureFlagsOptions.EnableSecurityAudit.ToString()));
            }
        }

        return await Task.FromResult(issues);
    }

    private async Task<List<ConfigurationIssue>> ValidateEnvironmentSpecificRulesAsync()
    {
        var issues = new List<ConfigurationIssue>();

        // Check environment variable consistency
        var configuredEnvironment = _environment;
        var envVariable = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");

        if (!string.Equals(configuredEnvironment, envVariable, StringComparison.OrdinalIgnoreCase))
        {
            issues.Add(new ConfigurationIssue(
                ConfigurationIssueSeverity.Warning,
                "Environment",
                "ASPNETCORE_ENVIRONMENT",
                "Environment variable doesn't match configuration",
                "Ensure environment consistency across configuration sources",
                $"Config: {configuredEnvironment}, EnvVar: {envVariable}"));
        }

        // Check for required environment variables
        var requiredEnvVars = new[] { "DB_HOST", "DB_NAME", "DB_USER", "DB_PASSWORD", "DB_PORT" };
        foreach (var envVar in requiredEnvVars)
        {
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(envVar)))
            {
                issues.Add(new ConfigurationIssue(
                    ConfigurationIssueSeverity.Error,
                    "Environment",
                    envVar,
                    $"Required environment variable {envVar} is not set",
                    $"Set the {envVar} environment variable",
                    "Not set"));
            }
        }

        return await Task.FromResult(issues);
    }

    private static object? GetPropertyValue(object obj, string propertyName)
    {
        try
        {
            var property = obj.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
            return property?.GetValue(obj);
        }
        catch
        {
            return null;
        }
    }
}

// Extension methods for easier service registration
public static class ConfigurationValidationServiceExtensions
{
    public static IServiceCollection AddConfigurationValidation(
        this IServiceCollection services)
    {
        services.AddSingleton<IConfigurationValidationService, ConfigurationValidationService>();
        return services;
    }

    public static async Task<IServiceProvider> ValidateConfigurationOnStartup(
        this IServiceProvider serviceProvider)
    {
        var validationService = serviceProvider.GetRequiredService<IConfigurationValidationService>();
        var logger = serviceProvider.GetRequiredService<ILogger<ConfigurationValidationService>>();

        var validationResult = await validationService.ValidateConfigurationAsync();

        if (!validationResult.IsValid)
        {
            var criticalIssues = validationResult.Issues.Where(i => i.Severity == ConfigurationIssueSeverity.Critical).ToList();
            var errorIssues = validationResult.Issues.Where(i => i.Severity == ConfigurationIssueSeverity.Error).ToList();

            logger.LogError("Configuration validation found {CriticalCount} critical and {ErrorCount} error issues",
                criticalIssues.Count, errorIssues.Count);

            foreach (var issue in criticalIssues.Concat(errorIssues))
            {
                logger.LogError("Configuration Issue [{Severity}] {Category}.{Property}: {Issue} | Current: {CurrentValue} | Action: {Action}",
                    issue.Severity, issue.Category, issue.Property, issue.Issue, issue.CurrentValue, issue.RecommendedAction);
            }

            throw new InvalidOperationException($"Configuration validation failed with {criticalIssues.Count} critical and {errorIssues.Count} error issues. Check application logs for details.");
        }

        var warningIssues = validationResult.Issues.Where(i => i.Severity == ConfigurationIssueSeverity.Warning).ToList();
        if (warningIssues.Any())
        {
            logger.LogWarning("Configuration validation found {WarningCount} warning issues", warningIssues.Count);
            foreach (var issue in warningIssues)
            {
                logger.LogWarning("Configuration Warning [{Severity}] {Category}.{Property}: {Issue} | Current: {CurrentValue} | Action: {Action}",
                    issue.Severity, issue.Category, issue.Property, issue.Issue, issue.CurrentValue, issue.RecommendedAction);
            }
        }

        logger.LogInformation("Configuration validation completed successfully for environment: {Environment}",
            validationResult.Environment);

        return serviceProvider;
    }
}