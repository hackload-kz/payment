using Microsoft.Extensions.Configuration;

namespace PaymentGateway.Core.Configuration;

public class LoggingConfiguration
{
    public const string SectionName = "Logging";

    public SerilogConfiguration Serilog { get; set; } = new();
    public AuditConfiguration Audit { get; set; } = new();
    public RetentionConfiguration Retention { get; set; } = new();
}

public class SerilogConfiguration
{
    public string MinimumLevel { get; set; } = "Information";
    public bool EnableConsole { get; set; } = true;
    public bool EnableFile { get; set; } = true;
    public bool EnableDatabase { get; set; } = true;
    public string LogDirectory { get; set; } = "logs";
    public bool EnableCompactJsonFormatting { get; set; } = true;
    public bool EnableStructuredLogging { get; set; } = true;
    public bool EnableMetrics { get; set; } = true;
    public Dictionary<string, string> MinimumLevelOverrides { get; set; } = new();
}

public class AuditConfiguration
{
    public bool EnableAuditLogging { get; set; } = true;
    public string AuditTableName { get; set; } = "audit_logs";
    public bool LogPaymentOperations { get; set; } = true;
    public bool LogAuthenticationEvents { get; set; } = true;
    public bool LogDatabaseChanges { get; set; } = true;
    public bool EnableSensitiveDataMasking { get; set; } = true;
    public List<string> SensitiveFields { get; set; } = new()
    {
        "CardNumber", "CVV", "Password", "Token", "TerminalKey"
    };
}

public class RetentionConfiguration
{
    public int FileRetentionDays { get; set; } = 30;
    public int DatabaseRetentionDays { get; set; } = 90;
    public int AuditRetentionDays { get; set; } = 365;
    public bool EnableAutoCleanup { get; set; } = true;
    public string CleanupSchedule { get; set; } = "0 2 * * *"; // Daily at 2 AM
}