using PaymentGateway.Core.Interfaces;
using System.ComponentModel.DataAnnotations;

namespace PaymentGateway.Core.Entities;

public class Customer : BaseEntity, IAuditableEntity
{
    [Required]
    [StringLength(100)]
    public string CustomerId { get; set; } = string.Empty;
    
    [EmailAddress]
    [StringLength(254)]
    public string? Email { get; set; }
    
    [StringLength(100)]
    public string? FirstName { get; set; }
    
    [StringLength(100)]
    public string? LastName { get; set; }
    
    [Phone]
    [StringLength(20)]
    public string? Phone { get; set; }
    
    [StringLength(500)]
    public string? Address { get; set; }
    
    [StringLength(100)]
    public string? City { get; set; }
    
    [StringLength(100)]
    public string? Country { get; set; }
    
    [StringLength(20)]
    public string? PostalCode { get; set; }
    
    public DateTime? DateOfBirth { get; set; }
    
    // Customer preferences
    [StringLength(10)]
    public string PreferredLanguage { get; set; } = "en";
    
    [StringLength(3)]
    public string PreferredCurrency { get; set; } = "RUB";
    
    // Risk and fraud scoring
    public int RiskScore { get; set; } = 0;
    
    [StringLength(20)]
    public string RiskLevel { get; set; } = "LOW";
    
    public bool IsBlacklisted { get; set; } = false;
    public DateTime? BlacklistedAt { get; set; }
    public string? BlacklistReason { get; set; }
    
    // Customer status
    public bool IsActive { get; set; } = true;
    
    // Customer activity tracking
    public DateTime? LastPaymentAt { get; set; }
    public int TotalPaymentCount { get; set; } = 0;
    public decimal TotalPaymentAmount { get; set; } = 0;
    public DateTime? LastLoginAt { get; set; }
    
    // KYC (Know Your Customer) information
    public bool IsKycVerified { get; set; } = false;
    public DateTime? KycVerifiedAt { get; set; }
    
    [StringLength(100)]
    public string? KycDocumentType { get; set; }
    
    [StringLength(100)]
    public string? KycDocumentNumber { get; set; }
    
    public DateTime? KycDocumentExpiryDate { get; set; }
    
    // Customer metadata
    public Dictionary<string, string> Metadata { get; set; } = new();
    
    // Navigation properties
    public int TeamId { get; set; }
    public virtual Team Team { get; set; } = null!;
    
    public virtual ICollection<Payment> Payments { get; set; } = new List<Payment>();
    public virtual ICollection<PaymentMethodInfo> PaymentMethods { get; set; } = new List<PaymentMethodInfo>();
    
    // Domain methods
    public string GetFullName()
    {
        if (string.IsNullOrEmpty(FirstName) && string.IsNullOrEmpty(LastName))
            return "Unknown Customer";
        
        return $"{FirstName} {LastName}".Trim();
    }
    
    public bool IsHighRisk()
    {
        return RiskScore > 75 || RiskLevel == "HIGH" || IsBlacklisted;
    }
    
    public bool RequiresKycVerification()
    {
        return !IsKycVerified && TotalPaymentAmount > 10000; // Require KYC for amounts > 10k
    }
    
    public bool CanMakePayment(decimal amount)
    {
        if (IsBlacklisted) return false;
        if (IsHighRisk() && amount > 50000) return false; // High risk customers limited to 50k
        return true;
    }
    
    public void UpdateRiskScore()
    {
        var baseScore = 0;
        
        // Age of account factor
        if (CreatedAt > DateTime.UtcNow.AddDays(-30))
            baseScore += 20; // New account
        
        // Payment history factor
        if (TotalPaymentCount == 0)
            baseScore += 30; // No payment history
        else if (TotalPaymentCount < 5)
            baseScore += 15; // Limited payment history
        
        // Amount factor
        if (TotalPaymentAmount > 100000)
            baseScore -= 10; // Large volume customer, lower risk
        
        // KYC factor
        if (!IsKycVerified)
            baseScore += 25;
        
        RiskScore = Math.Min(100, Math.Max(0, baseScore));
        
        RiskLevel = RiskScore switch
        {
            >= 75 => "HIGH",
            >= 50 => "MEDIUM",
            >= 25 => "LOW",
            _ => "VERY_LOW"
        };
    }
    
    public void RecordPayment(decimal amount)
    {
        TotalPaymentCount++;
        TotalPaymentAmount += amount;
        LastPaymentAt = DateTime.UtcNow;
        UpdateRiskScore();
    }
}