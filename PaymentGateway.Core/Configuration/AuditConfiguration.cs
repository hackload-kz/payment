using System.ComponentModel.DataAnnotations;

namespace PaymentGateway.Core.Configuration;

/// <summary>
/// Configuration settings for the audit system
/// </summary>
public class AuditConfiguration
{
    /// <summary>
    /// Maximum number of history records to return in a single query
    /// </summary>
    [Range(1, 10000)]
    public int MaxHistoryRecords { get; set; } = 1000;
    
    /// <summary>
    /// Maximum number of results to return in audit queries
    /// </summary>
    [Range(1, 10000)]
    public int MaxQueryResults { get; set; } = 1000;
    
    /// <summary>
    /// Number of days to retain audit logs before archiving
    /// </summary>
    [Range(1, 3650)] // 1 day to 10 years
    public int RetentionDays { get; set; } = 2555; // 7 years default for financial data
    
    /// <summary>
    /// Number of days to retain archived audit logs before deletion
    /// </summary>
    [Range(1, 3650)]
    public int ArchivedRetentionDays { get; set; } = 3650; // 10 years for archived data
    
    /// <summary>
    /// Enable integrity hash calculation for audit entries
    /// </summary>
    public bool EnableIntegrityHashing { get; set; } = true;
    
    /// <summary>
    /// Enable automatic archiving of old audit entries
    /// </summary>
    public bool EnableAutoArchiving { get; set; } = true;
    
    /// <summary>
    /// Hour of day to run automatic archiving (0-23)
    /// </summary>
    [Range(0, 23)]
    public int ArchivingHour { get; set; } = 2; // 2 AM
    
    /// <summary>
    /// Batch size for bulk archiving operations
    /// </summary>
    [Range(100, 10000)]
    public int ArchivingBatchSize { get; set; } = 1000;
    
    /// <summary>
    /// Enable real-time audit alerting for critical events
    /// </summary>
    public bool EnableRealTimeAlerting { get; set; } = true;
    
    /// <summary>
    /// Minimum severity level for real-time alerts
    /// </summary>
    public int AlertSeverityThreshold { get; set; } = 4; // Error level
    
    /// <summary>
    /// Maximum number of audit entries to process in a single batch
    /// </summary>
    [Range(10, 1000)]
    public int BatchProcessingSize { get; set; } = 100;
    
    /// <summary>
    /// Enable compression for archived audit entries
    /// </summary>
    public bool EnableArchiveCompression { get; set; } = true;
    
    /// <summary>
    /// Enable encryption for sensitive audit entries
    /// </summary>
    public bool EnableSensitiveDataEncryption { get; set; } = true;
    
    /// <summary>
    /// Connection string name for audit database (if separate from main database)
    /// </summary>
    public string? AuditConnectionStringName { get; set; }
    
    /// <summary>
    /// Table name for audit entries
    /// </summary>
    [Required]
    public string AuditTableName { get; set; } = "AuditLog";
    
    /// <summary>
    /// Schema name for audit table (PostgreSQL)
    /// </summary>
    public string AuditSchemaName { get; set; } = "audit";
    
    /// <summary>
    /// Enable partitioning for audit tables
    /// </summary>
    public bool EnableTablePartitioning { get; set; } = true;
    
    /// <summary>
    /// Partition interval for audit tables (Monthly, Weekly, Daily)
    /// </summary>
    public string PartitionInterval { get; set; } = "Monthly";
    
    /// <summary>
    /// Performance monitoring settings
    /// </summary>
    public AuditPerformanceSettings Performance { get; set; } = new();
    
    /// <summary>
    /// Compliance settings
    /// </summary>
    public AuditComplianceSettings Compliance { get; set; } = new();
}

/// <summary>
/// Performance settings for audit operations
/// </summary>
public class AuditPerformanceSettings
{
    /// <summary>
    /// Enable asynchronous audit logging
    /// </summary>
    public bool EnableAsyncLogging { get; set; } = true;
    
    /// <summary>
    /// Queue size for asynchronous audit operations
    /// </summary>
    [Range(100, 100000)]
    public int AsyncQueueSize { get; set; } = 10000;
    
    /// <summary>
    /// Enable audit entry buffering
    /// </summary>
    public bool EnableBuffering { get; set; } = true;
    
    /// <summary>
    /// Buffer flush interval in seconds
    /// </summary>
    [Range(1, 300)]
    public int BufferFlushIntervalSeconds { get; set; } = 30;
    
    /// <summary>
    /// Maximum buffer size before forced flush
    /// </summary>
    [Range(10, 10000)]
    public int MaxBufferSize { get; set; } = 1000;
    
    /// <summary>
    /// Enable audit metrics collection
    /// </summary>
    public bool EnableMetrics { get; set; } = true;
    
    /// <summary>
    /// Enable query result caching
    /// </summary>
    public bool EnableQueryCaching { get; set; } = true;
    
    /// <summary>
    /// Cache duration in minutes for audit queries
    /// </summary>
    [Range(1, 1440)]
    public int QueryCacheDurationMinutes { get; set; } = 15;
}

/// <summary>
/// Compliance settings for audit operations
/// </summary>
public class AuditComplianceSettings
{
    /// <summary>
    /// Enable PCI DSS compliance features
    /// </summary>
    public bool EnablePciDssCompliance { get; set; } = true;
    
    /// <summary>
    /// Enable GDPR compliance features
    /// </summary>
    public bool EnableGdprCompliance { get; set; } = true;
    
    /// <summary>
    /// Enable SOX compliance features
    /// </summary>
    public bool EnableSoxCompliance { get; set; } = true;
    
    /// <summary>
    /// Require digital signatures for critical audit entries
    /// </summary>
    public bool RequireDigitalSignatures { get; set; } = false;
    
    /// <summary>
    /// Enable audit trail tamper detection
    /// </summary>
    public bool EnableTamperDetection { get; set; } = true;
    
    /// <summary>
    /// Enable automatic compliance reporting
    /// </summary>
    public bool EnableAutoCompliance { get; set; } = true;
    
    /// <summary>
    /// Compliance report generation frequency (Daily, Weekly, Monthly)
    /// </summary>
    public string ComplianceReportFrequency { get; set; } = "Monthly";
    
    /// <summary>
    /// Email addresses for compliance notifications
    /// </summary>
    public List<string> ComplianceNotificationEmails { get; set; } = new();
    
    /// <summary>
    /// Data retention requirements by data type
    /// </summary>
    public Dictionary<string, int> DataRetentionRequirements { get; set; } = new()
    {
        { "Payment", 2555 }, // 7 years
        { "Transaction", 2555 }, // 7 years
        { "Authentication", 1095 }, // 3 years
        { "Configuration", 365 }, // 1 year
        { "Security", 2555 } // 7 years
    };
}