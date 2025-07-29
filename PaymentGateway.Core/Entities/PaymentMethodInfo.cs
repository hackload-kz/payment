using PaymentGateway.Core.Interfaces;
using System.ComponentModel.DataAnnotations;

namespace PaymentGateway.Core.Entities;

public class PaymentMethodInfo : BaseEntity, IAuditableEntity
{
    [Required]
    [StringLength(100)]
    public string PaymentMethodId { get; set; } = string.Empty;
    
    public PaymentMethod Type { get; set; }
    
    public PaymentMethodStatus Status { get; set; } = PaymentMethodStatus.ACTIVE;
    
    // Card-specific information (masked for security)
    [StringLength(50)]
    public string? CardMask { get; set; }
    
    [StringLength(100)]
    public string? CardType { get; set; } // VISA, MASTERCARD, MIR, etc.
    
    [StringLength(100)]
    public string? CardBrand { get; set; }
    
    [StringLength(10)]
    public string? CardBin { get; set; } // First 6 digits
    
    [StringLength(4)]
    public string? CardLast4 { get; set; } // Last 4 digits
    
    public int? CardExpiryMonth { get; set; }
    public int? CardExpiryYear { get; set; }
    
    [StringLength(100)]
    public string? CardHolderName { get; set; }
    
    [StringLength(100)]
    public string? IssuingBank { get; set; }
    
    [StringLength(100)]
    public string? CardCountry { get; set; }
    
    // Tokenization information
    [StringLength(200)]
    public string? Token { get; set; }
    
    public DateTime? TokenExpiresAt { get; set; }
    
    [StringLength(100)]
    public string? TokenProvider { get; set; } // Internal, ApplePay, GooglePay, etc.
    
    // Digital wallet information
    [StringLength(200)]
    public string? WalletId { get; set; }
    
    [StringLength(100)]
    public string? WalletProvider { get; set; }
    
    [StringLength(254)]
    public string? WalletEmail { get; set; }
    
    // Bank transfer information
    [StringLength(100)]
    public string? BankAccountNumber { get; set; }
    
    [StringLength(100)]
    public string? BankCode { get; set; }
    
    [StringLength(200)]
    public string? BankName { get; set; }
    
    // SBP (Faster Payment System) information
    [StringLength(20)]
    public string? SbpPhoneNumber { get; set; }
    
    [StringLength(100)]
    public string? SbpBankCode { get; set; }
    
    // Usage statistics
    public int UsageCount { get; set; } = 0;
    public DateTime? LastUsedAt { get; set; }
    public decimal TotalAmountProcessed { get; set; } = 0;
    public int SuccessfulTransactions { get; set; } = 0;
    public int FailedTransactions { get; set; } = 0;
    
    // Security and fraud prevention
    public bool IsFraudulent { get; set; } = false;
    public DateTime? FlaggedAt { get; set; }
    public string? FraudReason { get; set; }
    
    public bool RequiresVerification { get; set; } = false;
    public bool IsVerified { get; set; } = false;
    public DateTime? VerifiedAt { get; set; }
    
    // Customer and team associations
    public int? CustomerId { get; set; }
    public virtual Customer? Customer { get; set; }
    
    public int TeamId { get; set; }
    public virtual Team Team { get; set; } = null!;
    
    // Payment associations
    public virtual ICollection<Payment> Payments { get; set; } = new List<Payment>();
    
    // Additional metadata
    public Dictionary<string, string> Metadata { get; set; } = new();
    
    // Domain methods
    public bool IsExpired()
    {
        if (Type == PaymentMethod.Card && CardExpiryMonth.HasValue && CardExpiryYear.HasValue)
        {
            var expiryDate = new DateTime(CardExpiryYear.Value, CardExpiryMonth.Value, 1).AddMonths(1).AddDays(-1);
            return DateTime.UtcNow > expiryDate;
        }
        
        if (TokenExpiresAt.HasValue)
        {
            return DateTime.UtcNow > TokenExpiresAt.Value;
        }
        
        return false;
    }
    
    public bool IsActive()
    {
        return Status == PaymentMethodStatus.ACTIVE && !IsExpired() && !IsFraudulent;
    }
    
    public string GetDisplayName()
    {
        return Type switch
        {
            PaymentMethod.Card when !string.IsNullOrEmpty(CardMask) => $"{CardType} **** {CardLast4}",
            PaymentMethod.SBP when !string.IsNullOrEmpty(SbpPhoneNumber) => $"SBP {SbpPhoneNumber}",
            PaymentMethod.Wallet when !string.IsNullOrEmpty(WalletProvider) => $"{WalletProvider} Wallet",
            PaymentMethod.BankTransfer when !string.IsNullOrEmpty(BankName) => $"{BankName} Transfer",
            _ => Type.ToString()
        };
    }
    
    public double GetSuccessRate()
    {
        var totalTransactions = SuccessfulTransactions + FailedTransactions;
        if (totalTransactions == 0) return 100.0;
        
        return (double)SuccessfulTransactions / totalTransactions * 100.0;
    }
    
    public bool IsReliable()
    {
        if (UsageCount < 5) return true; // Not enough data
        return GetSuccessRate() >= 85.0;
    }
    
    public void RecordUsage(bool isSuccessful, decimal amount)
    {
        UsageCount++;
        LastUsedAt = DateTime.UtcNow;
        TotalAmountProcessed += amount;
        
        if (isSuccessful)
            SuccessfulTransactions++;
        else
            FailedTransactions++;
    }
    
    public void MarkAsFraudulent(string reason)
    {
        IsFraudulent = true;
        FlaggedAt = DateTime.UtcNow;
        FraudReason = reason;
        Status = PaymentMethodStatus.BLOCKED;
    }
    
    public void Verify()
    {
        IsVerified = true;
        VerifiedAt = DateTime.UtcNow;
        RequiresVerification = false;
    }
    
    public bool RequiresUpdate()
    {
        // Card expiring in next 30 days
        if (Type == PaymentMethod.Card && CardExpiryMonth.HasValue && CardExpiryYear.HasValue)
        {
            var expiryDate = new DateTime(CardExpiryYear.Value, CardExpiryMonth.Value, 1).AddMonths(1).AddDays(-1);
            return (expiryDate - DateTime.UtcNow).TotalDays <= 30;
        }
        
        return false;
    }
}

public enum PaymentMethodStatus
{
    ACTIVE = 1,
    INACTIVE = 2,
    EXPIRED = 3,
    BLOCKED = 4,
    PENDING_VERIFICATION = 5,
    VERIFICATION_FAILED = 6,
    SUSPENDED = 7
}