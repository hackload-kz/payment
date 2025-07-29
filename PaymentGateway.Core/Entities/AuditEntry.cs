using System.ComponentModel.DataAnnotations;
using System.Text.Json;

namespace PaymentGateway.Core.Entities;

/// <summary>
/// Comprehensive audit entry for tracking all payment operations
/// </summary>
public class AuditEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    
    /// <summary>
    /// ID of the entity being audited
    /// </summary>
    public Guid EntityId { get; set; }
    
    /// <summary>
    /// Type of entity being audited (Payment, Transaction, Team, etc.)
    /// </summary>
    [Required]
    [MaxLength(100)]
    public string EntityType { get; set; } = string.Empty;
    
    /// <summary>
    /// Action performed on the entity
    /// </summary>
    public AuditAction Action { get; set; }
    
    /// <summary>
    /// User or system that performed the action
    /// </summary>
    [MaxLength(100)]
    public string? UserId { get; set; }
    
    /// <summary>
    /// Team slug if action is team-specific
    /// </summary>
    [MaxLength(50)]
    public string? TeamSlug { get; set; }
    
    /// <summary>
    /// Timestamp when the action occurred
    /// </summary>
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    
    /// <summary>
    /// Additional details about the action
    /// </summary>
    [MaxLength(1000)]
    public string? Details { get; set; }
    
    /// <summary>
    /// JSON snapshot of the entity before the change
    /// </summary>
    public string? EntitySnapshotBefore { get; set; }
    
    /// <summary>
    /// JSON snapshot of the entity after the change
    /// </summary>
    public string EntitySnapshotAfter { get; set; } = string.Empty;
    
    /// <summary>
    /// Correlation ID for tracking related operations
    /// </summary>
    public string? CorrelationId { get; set; }
    
    /// <summary>
    /// Request ID for HTTP requests
    /// </summary>
    public string? RequestId { get; set; }
    
    /// <summary>
    /// IP address of the requester
    /// </summary>
    [MaxLength(45)] // IPv6 max length
    public string? IpAddress { get; set; }
    
    /// <summary>
    /// User agent of the requester
    /// </summary>
    [MaxLength(500)]
    public string? UserAgent { get; set; }
    
    /// <summary>
    /// Session ID if available
    /// </summary>
    [MaxLength(100)]
    public string? SessionId { get; set; }
    
    /// <summary>
    /// Risk score if applicable (for fraud detection)
    /// </summary>
    public decimal? RiskScore { get; set; }
    
    /// <summary>
    /// Severity level of the audit event
    /// </summary>
    public AuditSeverity Severity { get; set; } = AuditSeverity.Information;
    
    /// <summary>
    /// Category of the audit event
    /// </summary>
    public AuditCategory Category { get; set; } = AuditCategory.General;
    
    /// <summary>
    /// Additional metadata as JSON
    /// </summary>
    public string? Metadata { get; set; }
    
    /// <summary>
    /// Hash of the audit entry for integrity verification
    /// </summary>
    [MaxLength(64)]
    public string? IntegrityHash { get; set; }
    
    /// <summary>
    /// Indicates if this is a sensitive operation that requires special handling
    /// </summary>
    public bool IsSensitive { get; set; }
    
    /// <summary>
    /// Indicates if this audit entry has been archived
    /// </summary>
    public bool IsArchived { get; set; } = false;
    
    /// <summary>
    /// When this audit entry was archived
    /// </summary>
    public DateTime? ArchivedAt { get; set; }
    
    /// <summary>
    /// Get typed metadata
    /// </summary>
    public T? GetMetadata<T>() where T : class
    {
        if (string.IsNullOrEmpty(Metadata))
            return null;
            
        try
        {
            return JsonSerializer.Deserialize<T>(Metadata);
        }
        catch
        {
            return null;
        }
    }
    
    /// <summary>
    /// Set typed metadata
    /// </summary>
    public void SetMetadata<T>(T metadata) where T : class
    {
        if (metadata == null)
        {
            Metadata = null;
            return;
        }
        
        Metadata = JsonSerializer.Serialize(metadata, new JsonSerializerOptions
        {
            WriteIndented = false,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        });
    }
}

/// <summary>
/// Types of audit actions
/// </summary>
public enum AuditAction
{
    // Entity CRUD operations
    Created = 1,
    Updated = 2,
    Deleted = 3,
    Restored = 4,
    
    // Payment-specific actions
    PaymentInitialized = 100,
    PaymentAuthorized = 101,
    PaymentConfirmed = 102,
    PaymentCancelled = 103,
    PaymentRefunded = 104,
    PaymentFailed = 105,
    PaymentExpired = 106,
    PaymentRetried = 107,
    
    // Status changes
    StatusChanged = 200,
    StateTransition = 201,
    
    // Financial operations
    AmountChanged = 300,
    CurrencyChanged = 301,
    FeeCalculated = 302,
    
    // Authentication and authorization
    AuthenticationAttempt = 400,
    AuthenticationSuccess = 401,
    AuthenticationFailure = 402,
    TokenGenerated = 403,
    TokenExpired = 404,
    TokenRevoked = 405,
    PasswordChanged = 406,
    
    // Configuration changes
    ConfigurationChanged = 500,
    FeatureFlagChanged = 501,
    SettingsUpdated = 502,
    
    // Security events
    SecurityViolation = 600,
    FraudDetected = 601,
    RiskAssessment = 602,
    SuspiciousActivity = 603,
    
    // System operations
    SystemStart = 700,
    SystemStop = 701,
    DatabaseMigration = 702,
    BackupCreated = 703,
    
    // API operations
    ApiCallMade = 800,
    ApiCallFailed = 801,
    RateLimitExceeded = 802,
    
    // Data operations
    DataExported = 900,
    DataImported = 901,
    DataArchived = 902,
    DataPurged = 903
}

/// <summary>
/// Severity levels for audit events
/// </summary>
public enum AuditSeverity
{
    Trace = 0,
    Debug = 1,
    Information = 2,
    Warning = 3,
    Error = 4,
    Critical = 5
}

/// <summary>
/// Categories for audit events
/// </summary>
public enum AuditCategory
{
    General = 0,
    Payment = 1,
    Authentication = 2,
    Authorization = 3,
    Configuration = 4,
    Security = 5,
    Performance = 6,
    System = 7,
    API = 8,
    Data = 9,
    Compliance = 10,
    Fraud = 11
}

/// <summary>
/// Audit context information for tracking request details
/// </summary>
public class AuditContext
{
    public string? UserId { get; set; }
    public string? TeamSlug { get; set; }
    public string? CorrelationId { get; set; }
    public string? RequestId { get; set; }
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
    public string? SessionId { get; set; }
    public decimal? RiskScore { get; set; }
    public Dictionary<string, object>? AdditionalData { get; set; }
}

/// <summary>
/// Audit query filters for searching audit logs
/// </summary>
public class AuditQueryFilter
{
    public Guid? EntityId { get; set; }
    public string? EntityType { get; set; }
    public AuditAction? Action { get; set; }
    public string? UserId { get; set; }
    public string? TeamSlug { get; set; }
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }
    public AuditSeverity? MinSeverity { get; set; }
    public AuditCategory? Category { get; set; }
    public string? CorrelationId { get; set; }
    public string? RequestId { get; set; }
    public bool? IsSensitive { get; set; }
    public bool? IsArchived { get; set; }
    public int Skip { get; set; } = 0;
    public int Take { get; set; } = 100;
}

/// <summary>
/// Audit statistics for reporting and analytics
/// </summary>
public class AuditStatistics
{
    public int TotalEntries { get; set; }
    public Dictionary<string, int> ActionCounts { get; set; } = new();
    public Dictionary<string, int> EntityTypeCounts { get; set; } = new();
    public Dictionary<string, int> SeverityCounts { get; set; } = new();
    public Dictionary<string, int> CategoryCounts { get; set; } = new();
    public Dictionary<string, int> UserCounts { get; set; } = new();
    public DateTime? EarliestEntry { get; set; }
    public DateTime? LatestEntry { get; set; }
    public double AverageEntriesPerDay { get; set; }
    public int SensitiveEntries { get; set; }
    public int ArchivedEntries { get; set; }
}