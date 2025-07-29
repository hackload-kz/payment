using System.ComponentModel.DataAnnotations;

namespace PaymentGateway.Core.Configuration;

public class DatabaseOptions
{
    public const string SectionName = "Database";

    [Required]
    public string ConnectionString { get; set; } = string.Empty;
    
    public bool EnableRetryOnFailure { get; set; } = true;
    
    [Range(1, 10)]
    public int MaxRetryCount { get; set; } = 3;
    
    public TimeSpan MaxRetryDelay { get; set; } = TimeSpan.FromSeconds(30);
    
    [Range(10, 300)]
    public int CommandTimeout { get; set; } = 30;
    
    [Range(1, 1000)]
    public int PoolSize { get; set; } = 128;
    
    public bool EnableSensitiveDataLogging { get; set; } = false;
    public bool EnableDetailedErrors { get; set; } = false;
}

public class SecurityOptions
{
    public const string SectionName = "Security";

    public AuthenticationOptions Authentication { get; set; } = new();
    public HttpsOptions Https { get; set; } = new();
    public RateLimitOptions RateLimit { get; set; } = new();
}

public class AuthenticationOptions
{
    [Range(5, 480)]
    public int TokenExpirationMinutes { get; set; } = 60;
    
    [Range(1, 365)]
    public int RefreshTokenExpirationDays { get; set; } = 30;
    
    public bool EnableRefreshTokens { get; set; } = true;
    
    [Range(1, 20)]
    public int MaxFailedAttempts { get; set; } = 5;
    
    [Range(1, 1440)]
    public int LockoutDurationMinutes { get; set; } = 15;
    
    [Required]
    public string SecretKey { get; set; } = string.Empty;
}

public class HttpsOptions
{
    public bool RequireHttps { get; set; } = true;
    public bool EnableHsts { get; set; } = true;
    
    [Range(1, 365)]
    public int HstsMaxAgeDays { get; set; } = 365;
    
    public bool HstsIncludeSubdomains { get; set; } = true;
    public bool HstsPreload { get; set; } = false;
    
    public List<string> ExcludedPaths { get; set; } = new() { "/health", "/metrics" };
}

public class RateLimitOptions
{
    public RateLimitConfig IpRateLimit { get; set; } = new();
    public RateLimitConfig AuthenticationRateLimit { get; set; } = new();
}

public class RateLimitConfig
{
    [Range(1, 10000)]
    public int MaxRequests { get; set; } = 100;
    
    [Range(1, 60)]
    public int WindowMinutes { get; set; } = 1;
}

public class LoggingOptions
{
    public const string SectionName = "Logging";

    public SerilogOptions Serilog { get; set; } = new();
    public AuditOptions Audit { get; set; } = new();
    public RetentionOptions Retention { get; set; } = new();
}

public class SerilogOptions
{
    [Required]
    public string MinimumLevel { get; set; } = "Information";
    
    public bool EnableConsole { get; set; } = true;
    public bool EnableFile { get; set; } = true;
    public bool EnableDatabase { get; set; } = true;
    
    [Required]
    public string LogDirectory { get; set; } = "logs";
    
    public bool EnableCompactJsonFormatting { get; set; } = true;
    public bool EnableStructuredLogging { get; set; } = true;
    public bool EnableMetrics { get; set; } = true;
    
    public Dictionary<string, string> MinimumLevelOverrides { get; set; } = new();
}

public class AuditOptions
{
    public bool EnableAuditLogging { get; set; } = true;
    
    [Required]
    public string AuditTableName { get; set; } = "audit_logs";
    
    public bool LogPaymentOperations { get; set; } = true;
    public bool LogAuthenticationEvents { get; set; } = true;
    public bool LogDatabaseChanges { get; set; } = true;
    public bool EnableSensitiveDataMasking { get; set; } = true;
    
    public List<string> SensitiveFields { get; set; } = new()
    {
        "CardNumber", "CVV", "Password", "Token", "TerminalKey", "TeamSlug"
    };
}

public class RetentionOptions
{
    [Range(1, 365)]
    public int FileRetentionDays { get; set; } = 30;
    
    [Range(1, 3650)]
    public int DatabaseRetentionDays { get; set; } = 90;
    
    [Range(1, 3650)]
    public int AuditRetentionDays { get; set; } = 365;
    
    public bool EnableAutoCleanup { get; set; } = true;
    
    [Required]
    public string CleanupSchedule { get; set; } = "0 2 * * *";
}

public class MetricsOptions
{
    public const string SectionName = "Metrics";

    public PrometheusOptions Prometheus { get; set; } = new();
    public DashboardOptions Dashboard { get; set; } = new();
    public BusinessMetricsOptions Business { get; set; } = new();
}

public class PrometheusOptions
{
    public bool Enabled { get; set; } = true;
    
    [Required]
    public string MetricsPath { get; set; } = "/metrics";
    
    [Range(1, 65535)]
    public int Port { get; set; } = 8081;
    
    [Required]
    public string Host { get; set; } = "*";
    
    public bool EnableDebugMetrics { get; set; } = false;
    
    [Range(1, 300)]
    public int ScrapeIntervalSeconds { get; set; } = 15;
    
    public Dictionary<string, string> Labels { get; set; } = new();
}

public class DashboardOptions
{
    public bool Enabled { get; set; } = true;
    
    [Required]
    public string DashboardPath { get; set; } = "/metrics-dashboard";
    
    public bool ShowDetailedMetrics { get; set; } = true;
    public bool ShowBusinessMetrics { get; set; } = true;
    public bool ShowSystemMetrics { get; set; } = true;
    
    [Range(1, 300)]
    public int RefreshIntervalSeconds { get; set; } = 30;
}

public class BusinessMetricsOptions
{
    public bool EnableTransactionMetrics { get; set; } = true;
    public bool EnableRevenueMetrics { get; set; } = true;
    public bool EnableCustomerMetrics { get; set; } = true;
    public bool EnablePaymentMethodMetrics { get; set; } = true;
    public bool EnableTeamMetrics { get; set; } = true;
    
    public List<string> ExcludedTeams { get; set; } = new();
    public Dictionary<string, decimal> CurrencyConversionRates { get; set; } = new();
}

public class FeatureFlagsOptions
{
    public const string SectionName = "FeatureFlags";

    public bool EnableAdvancedMetrics { get; set; } = true;
    public bool EnableSecurityAudit { get; set; } = true;
    public bool EnableTokenExpiration { get; set; } = true;
    public bool EnableRateLimit { get; set; } = true;
    public bool EnableHttpsEnforcement { get; set; } = true;
    public bool EnableConfigurationValidation { get; set; } = true;
    public bool EnableConfigurationHotReload { get; set; } = true;
    public bool EnablePaymentQueueing { get; set; } = true;
    public bool EnableConcurrencyMetrics { get; set; } = true;
    public bool EnableErrorAnalytics { get; set; } = true;
}

public class HealthCheckOptions
{
    public const string SectionName = "HealthChecks";

    public DatabaseHealthCheckOptions Database { get; set; } = new();
}

public class DatabaseHealthCheckOptions
{
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(5);
    
    [Required]
    public string FailureStatus { get; set; } = "Degraded";
}