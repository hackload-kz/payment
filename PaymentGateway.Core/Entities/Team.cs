using PaymentGateway.Core.Interfaces;
using PaymentGateway.Core.Enums;
using System.ComponentModel.DataAnnotations;

namespace PaymentGateway.Core.Entities;

public class Team : BaseEntity, IAuditableEntity
{
    [Required]
    [StringLength(100)]
    public string TeamSlug { get; set; } = string.Empty;
    
    [Required]
    [StringLength(200)]
    public string TeamName { get; set; } = string.Empty;
    
    // Alias for compatibility with controllers
    public string Name => TeamName;
    
    [Required]
    [StringLength(128)]
    public string Password { get; set; } = string.Empty;
    
    public bool IsActive { get; set; } = true;
    
    // Authentication and security settings
    [StringLength(100)]
    public string? SecretKey { get; set; }
    
    public DateTime? LastPasswordChangeAt { get; set; }
    public int FailedAuthenticationAttempts { get; set; } = 0;
    public DateTime? LockedUntil { get; set; }
    public DateTime? LastSuccessfulAuthenticationAt { get; set; }
    
    [StringLength(45)]
    public string? LastAuthenticationIpAddress { get; set; }
    
    // Team configuration
    [EmailAddress]
    [StringLength(254)]
    public string? ContactEmail { get; set; }
    
    [StringLength(20)]
    public string? ContactPhone { get; set; }
    
    [StringLength(500)]
    public string? Description { get; set; }
    
    // Payment processing settings
    [Url]
    [StringLength(2048)]
    public string? NotificationUrl { get; set; }
    
    [Url]
    [StringLength(2048)]
    public string? SuccessUrl { get; set; }
    
    [Url]
    [StringLength(2048)]
    public string? FailUrl { get; set; }
    
    [Url]
    [StringLength(2048)]
    public string? CancelUrl { get; set; }
    
    // Payment limits and restrictions
    [Range(0, double.MaxValue)]
    public decimal? MinPaymentAmount { get; set; }
    
    [Range(0, double.MaxValue)]
    public decimal? MaxPaymentAmount { get; set; }
    
    [Range(0, double.MaxValue)]
    public decimal? DailyPaymentLimit { get; set; }
    
    [Range(0, double.MaxValue)]
    public decimal? MonthlyPaymentLimit { get; set; }
    
    [Range(0, int.MaxValue)]
    public int? DailyTransactionLimit { get; set; } = 1000;
    
    // Supported currencies and payment methods
    public List<string> SupportedCurrencies { get; set; } = new() { "RUB" };
    public List<PaymentMethod> SupportedPaymentMethods { get; set; } = new() { PaymentMethod.Card };
    
    // Processing permissions
    public bool CanProcessRefunds { get; set; } = true;
    
    // Business information
    [StringLength(500)]
    public string? LegalName { get; set; }
    
    [StringLength(50)]
    public string? TaxId { get; set; }
    
    [StringLength(1000)]
    public string? Address { get; set; }
    
    [StringLength(100)]
    public string? Country { get; set; }
    
    [StringLength(100)]
    public string? TimeZone { get; set; } = "UTC";
    
    // Fee and pricing configuration
    public decimal ProcessingFeePercentage { get; set; } = 0;
    public decimal FixedProcessingFee { get; set; } = 0;
    
    [StringLength(3)]
    public string FeeCurrency { get; set; } = "RUB";
    
    // Settlement configuration
    public int SettlementDelayDays { get; set; } = 1;
    
    [StringLength(100)]
    public string? SettlementAccountNumber { get; set; }
    
    [StringLength(100)]
    public string? SettlementBankCode { get; set; }
    
    // Risk and fraud settings
    public bool EnableFraudDetection { get; set; } = true;
    public int MaxFraudScore { get; set; } = 75;
    public bool RequireManualReviewForHighRisk { get; set; } = true;
    
    // Feature flags for team-specific functionality
    public bool EnableRefunds { get; set; } = true;
    public bool EnablePartialRefunds { get; set; } = false;
    public bool EnableReversals { get; set; } = true;
    public bool Enable3DSecure { get; set; } = true;
    public bool EnableTokenization { get; set; } = true;
    public bool EnableRecurringPayments { get; set; } = false;
    
    // API and webhook settings
    [StringLength(100)]
    public string? ApiVersion { get; set; } = "v1";
    
    public bool EnableWebhooks { get; set; } = true;
    public int WebhookRetryAttempts { get; set; } = 3;
    public int WebhookTimeoutSeconds { get; set; } = 30;
    
    [StringLength(500)]
    public string? WebhookSecret { get; set; }
    
    // Metadata and custom fields
    public Dictionary<string, string> Metadata { get; set; } = new();
    
    // Navigation properties
    public virtual ICollection<Payment> Payments { get; set; } = new List<Payment>();
    public virtual ICollection<Customer> Customers { get; set; } = new List<Customer>();
    public virtual ICollection<PaymentMethodInfo> PaymentMethods { get; set; } = new List<PaymentMethodInfo>();
    
    // Business information metadata
    public Dictionary<string, string> BusinessInfo { get; set; } = new();
    
    // Domain validation methods
    public bool IsLocked()
    {
        return LockedUntil.HasValue && DateTime.UtcNow < LockedUntil.Value;
    }
    
    public bool RequiresPasswordChange()
    {
        if (!LastPasswordChangeAt.HasValue) return true;
        return DateTime.UtcNow - LastPasswordChangeAt.Value > TimeSpan.FromDays(90);
    }
    
    public bool CanProcessPayment(decimal amount, string currency)
    {
        if (!IsActive || IsLocked()) return false;
        if (!SupportedCurrencies.Contains(currency)) return false;
        if (MinPaymentAmount.HasValue && amount < MinPaymentAmount.Value) return false;
        if (MaxPaymentAmount.HasValue && amount > MaxPaymentAmount.Value) return false;
        return true;
    }
    
    public bool HasReachedDailyLimit(decimal currentDailyAmount)
    {
        return DailyPaymentLimit.HasValue && currentDailyAmount >= DailyPaymentLimit.Value;
    }
    
    public bool HasReachedMonthlyLimit(decimal currentMonthlyAmount)
    {
        return MonthlyPaymentLimit.HasValue && currentMonthlyAmount >= MonthlyPaymentLimit.Value;
    }
    
    public bool SupportsPaymentMethod(PaymentMethod paymentMethod)
    {
        return SupportedPaymentMethods.Contains(paymentMethod);
    }
    
    public decimal CalculateProcessingFee(decimal amount)
    {
        var percentageFee = amount * (ProcessingFeePercentage / 100);
        return percentageFee + FixedProcessingFee;
    }
    
    public DateTime GetNextSettlementDate(DateTime transactionDate)
    {
        return transactionDate.AddDays(SettlementDelayDays);
    }
    
    public void IncrementFailedAuthenticationAttempts()
    {
        FailedAuthenticationAttempts++;
        
        // Lock after 5 failed attempts for 30 minutes
        if (FailedAuthenticationAttempts >= 5)
        {
            LockedUntil = DateTime.UtcNow.AddMinutes(30);
        }
    }
    
    public void ResetFailedAuthenticationAttempts()
    {
        FailedAuthenticationAttempts = 0;
        LockedUntil = null;
        LastSuccessfulAuthenticationAt = DateTime.UtcNow;
    }
    
    // Domain validation rules
    public (bool IsValid, List<string> Errors) ValidateForCreation()
    {
        var errors = new List<string>();
        
        if (string.IsNullOrWhiteSpace(TeamName))
            errors.Add("Team name is required");
            
        if (TeamName.Length > 200)
            errors.Add("Team name cannot exceed 200 characters");
            
        if (string.IsNullOrWhiteSpace(TeamSlug))
            errors.Add("Team slug is required");
            
        if (string.IsNullOrWhiteSpace(Password))
            errors.Add("Password is required");
            
        if (DailyPaymentLimit.HasValue && DailyPaymentLimit <= 0)
            errors.Add("Daily payment limit must be greater than zero");
            
        if (DailyPaymentLimit.HasValue && DailyPaymentLimit > 10000000) // 10M daily limit
            errors.Add("Daily payment limit exceeds maximum allowed");
            
        if (MonthlyPaymentLimit.HasValue && DailyPaymentLimit.HasValue && 
            MonthlyPaymentLimit <= DailyPaymentLimit)
            errors.Add("Monthly payment limit must be greater than daily limit");
            
        if (SupportedCurrencies.Count == 0)
            errors.Add("At least one supported currency is required");
            
        return (errors.Count == 0, errors);
    }
    
    public (bool IsValid, List<string> Errors) ValidateForPayment(decimal amount, string currency)
    {
        var errors = new List<string>();
        
        if (!IsActive)
            errors.Add("Team is not active");
            
        if (LockedUntil.HasValue && DateTime.UtcNow < LockedUntil.Value)
            errors.Add("Team is temporarily locked due to authentication failures");
            
        if (!SupportedCurrencies.Contains(currency))
            errors.Add($"Currency {currency} is not supported by this team");
            
        if (MaxPaymentAmount.HasValue && amount > MaxPaymentAmount.Value)
            errors.Add("Transaction amount exceeds maximum payment amount");
            
        if (MinPaymentAmount.HasValue && amount < MinPaymentAmount.Value)
            errors.Add("Transaction amount is below minimum payment amount");
            
        // Note: Daily/monthly limits would be checked by a service with current usage data
        
        return (errors.Count == 0, errors);
    }
    
    public (bool IsValid, List<string> Errors) ValidateApiKeyRotation()
    {
        var errors = new List<string>();
        
        if (LastPasswordChangeAt.HasValue && 
            DateTime.UtcNow - LastPasswordChangeAt.Value < TimeSpan.FromDays(1))
            errors.Add("Password can only be changed once per day");
            
        return (errors.Count == 0, errors);
    }
}