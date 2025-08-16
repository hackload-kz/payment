using PaymentGateway.Core.Enums;

namespace PaymentGateway.Core.DTOs.TeamRegistration;

/// <summary>
/// Comprehensive team information response DTO for admin endpoints
/// Contains all team details including sensitive information for administrative purposes
/// </summary>
public class TeamInfoDto
{
    // Basic team information
    public Guid Id { get; set; }
    public string TeamSlug { get; set; } = string.Empty;
    public string TeamName { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    
    // Contact information
    public string? ContactEmail { get; set; }
    public string? ContactPhone { get; set; }
    public string? Description { get; set; }
    
    // Authentication and security settings
    public string? SecretKey { get; set; }
    public DateTime? LastPasswordChangeAt { get; set; }
    public int FailedAuthenticationAttempts { get; set; }
    public DateTime? LockedUntil { get; set; }
    public DateTime? LastSuccessfulAuthenticationAt { get; set; }
    public string? LastAuthenticationIpAddress { get; set; }
    
    // Payment processing URLs
    public string? NotificationUrl { get; set; }
    public string? SuccessUrl { get; set; }
    public string? FailUrl { get; set; }
    public string? CancelUrl { get; set; }
    
    // Payment limits and restrictions
    public decimal? MinPaymentAmount { get; set; }
    public decimal? MaxPaymentAmount { get; set; }
    public decimal? DailyPaymentLimit { get; set; }
    public decimal? MonthlyPaymentLimit { get; set; }
    public int? DailyTransactionLimit { get; set; }
    
    // Supported currencies and payment methods
    public List<string> SupportedCurrencies { get; set; } = new();
    public List<PaymentMethod> SupportedPaymentMethods { get; set; } = new();
    
    // Processing permissions
    public bool CanProcessRefunds { get; set; }
    
    // Business information
    public string? LegalName { get; set; }
    public string? TaxId { get; set; }
    public string? Address { get; set; }
    public string? Country { get; set; }
    public string? TimeZone { get; set; }
    
    // Fee and pricing configuration
    public decimal ProcessingFeePercentage { get; set; }
    public decimal FixedProcessingFee { get; set; }
    public string FeeCurrency { get; set; } = "RUB";
    
    // Settlement configuration
    public int SettlementDelayDays { get; set; }
    public string? SettlementAccountNumber { get; set; }
    public string? SettlementBankCode { get; set; }
    
    // Risk and fraud settings
    public bool EnableFraudDetection { get; set; }
    public int MaxFraudScore { get; set; }
    public bool RequireManualReviewForHighRisk { get; set; }
    
    // Feature flags for team-specific functionality
    public bool EnableRefunds { get; set; }
    public bool EnablePartialRefunds { get; set; }
    public bool EnableReversals { get; set; }
    public bool Enable3DSecure { get; set; }
    public bool EnableTokenization { get; set; }
    public bool EnableRecurringPayments { get; set; }
    
    // API and webhook settings
    public string? ApiVersion { get; set; }
    public bool EnableWebhooks { get; set; }
    public int WebhookRetryAttempts { get; set; }
    public int WebhookTimeoutSeconds { get; set; }
    public string? WebhookSecret { get; set; }
    
    // Metadata and custom fields
    public Dictionary<string, string> Metadata { get; set; } = new();
    public Dictionary<string, string> BusinessInfo { get; set; } = new();
    
    // Usage statistics (calculated fields)
    public TeamUsageStatsDto? UsageStats { get; set; }
    
    // Status information
    public TeamStatusDto Status { get; set; } = new();
}

/// <summary>
/// Team usage statistics for administrative monitoring
/// </summary>
public class TeamUsageStatsDto
{
    public int TotalPayments { get; set; }
    public decimal TotalPaymentAmount { get; set; }
    public int PaymentsToday { get; set; }
    public decimal PaymentAmountToday { get; set; }
    public int PaymentsThisMonth { get; set; }
    public decimal PaymentAmountThisMonth { get; set; }
    public int TotalCustomers { get; set; }
    public int ActivePaymentMethods { get; set; }
    public DateTime? LastPaymentAt { get; set; }
    public DateTime? LastWebhookAt { get; set; }
    public int FailedWebhooksLast24Hours { get; set; }
}

/// <summary>
/// Team status information for administrative monitoring
/// </summary>
public class TeamStatusDto
{
    public bool IsLocked { get; set; }
    public bool RequiresPasswordChange { get; set; }
    public bool HasReachedDailyLimit { get; set; }
    public bool HasReachedMonthlyLimit { get; set; }
    public bool IsHealthy { get; set; }
    public List<string> HealthIssues { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
}