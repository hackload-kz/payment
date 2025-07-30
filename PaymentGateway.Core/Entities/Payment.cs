using PaymentGateway.Core.Interfaces;
using PaymentGateway.Core.Enums;
using System.ComponentModel.DataAnnotations;

namespace PaymentGateway.Core.Entities;

public class Payment : BaseEntity, IAuditableEntity
{
    [Required]
    [StringLength(50)]
    public string PaymentId { get; set; } = string.Empty;
    
    [Required]
    [StringLength(50)]
    public string OrderId { get; set; } = string.Empty;
    
    [Required]
    [StringLength(100)]
    public string TeamSlug { get; set; } = string.Empty;
    
    [Range(0.01, double.MaxValue, ErrorMessage = "Amount must be greater than 0")]
    public decimal Amount { get; set; }
    
    [Required]
    [StringLength(3)]
    public string Currency { get; set; } = "RUB";
    
    public PaymentStatus Status { get; set; } = PaymentStatus.INIT;
    
    [StringLength(500)]
    public string? Description { get; set; }
    
    [EmailAddress]
    [StringLength(254)]
    public string? CustomerEmail { get; set; }
    
    [StringLength(2048)]
    public string? PaymentURL { get; set; }
    
    [StringLength(2048)]
    public string? SuccessUrl { get; set; }
    
    [StringLength(2048)]
    public string? FailUrl { get; set; }
    
    // Payment lifecycle timestamps
    public DateTime? InitializedAt { get; set; }
    public DateTime? FormShowedAt { get; set; }
    public DateTime? OneChooseVisionAt { get; set; }
    public DateTime? FinishAuthorizeAt { get; set; }
    public DateTime? AuthorizingStartedAt { get; set; }
    public DateTime? AuthorizedAt { get; set; }
    public DateTime? ConfirmingStartedAt { get; set; }
    public DateTime? ConfirmedAt { get; set; }
    public DateTime? CancellingStartedAt { get; set; }
    public DateTime? CancelledAt { get; set; }
    public DateTime? ReversingStartedAt { get; set; }
    public DateTime? ReversedAt { get; set; }
    public DateTime? RefundingStartedAt { get; set; }
    public DateTime? RefundedAt { get; set; }
    public DateTime? RejectedAt { get; set; }
    public DateTime? ExpiredAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    
    // Payment processing details
    [StringLength(1000)]
    public string? FailureReason { get; set; }
    
    [StringLength(50)]
    public string? ErrorCode { get; set; }
    
    [StringLength(1000)]
    public string? ErrorMessage { get; set; }
    
    [StringLength(2000)]
    public string? Receipt { get; set; }
    
    [StringLength(100)]
    public string? BankOrderId { get; set; }
    
    [StringLength(50)]
    public string? CardMask { get; set; }
    
    [StringLength(100)]
    public string? CardType { get; set; }
    
    [StringLength(100)]
    public string? BankName { get; set; }
    
    public PaymentMethod PaymentMethod { get; set; } = PaymentMethod.Card;
    
    // Retry and attempt tracking
    public int AuthorizationAttempts { get; set; } = 0;
    public int MaxAllowedAttempts { get; set; } = 3;
    
    // Security and tracking
    [StringLength(45)]
    public string? CustomerIpAddress { get; set; }
    
    [StringLength(500)]
    public string? UserAgent { get; set; }
    
    [StringLength(100)]
    public string? SessionId { get; set; }
    
    // Payment expiration
    public DateTime? ExpiresAt { get; set; }
    
    // Refund details
    public decimal RefundedAmount { get; set; } = 0;
    public int RefundCount { get; set; } = 0;
    
    // Additional metadata
    public Dictionary<string, string> Metadata { get; set; } = new();
    
    // Navigation properties
    public Guid TeamId { get; set; }
    public virtual Team Team { get; set; } = null!;
    
    public Guid? CustomerId { get; set; }
    public virtual Customer? Customer { get; set; }
    
    public virtual ICollection<Transaction> Transactions { get; set; } = new List<Transaction>();
    public virtual ICollection<PaymentMethodInfo> PaymentMethods { get; set; } = new List<PaymentMethodInfo>();
    
    // Domain validation methods
    public bool CanBeAuthorized()
    {
        return Status == PaymentStatus.NEW || Status == PaymentStatus.FORM_SHOWED;
    }
    
    public bool CanBeConfirmed()
    {
        return Status == PaymentStatus.AUTHORIZED;
    }
    
    public bool CanBeCancelled()
    {
        return Status is PaymentStatus.NEW or PaymentStatus.FORM_SHOWED or PaymentStatus.AUTHORIZED;
    }
    
    public bool CanBeReversed()
    {
        return Status == PaymentStatus.AUTHORIZED;
    }
    
    public bool CanBeRefunded()
    {
        return Status == PaymentStatus.CONFIRMED && RefundedAmount < Amount;
    }
    
    public bool HasExpired()
    {
        return ExpiresAt.HasValue && DateTime.UtcNow > ExpiresAt.Value;
    }
    
    public bool HasRemainingAttempts()
    {
        return AuthorizationAttempts < MaxAllowedAttempts;
    }
    
    public decimal GetRemainingAmount()
    {
        return Amount - RefundedAmount;
    }
    
    public TimeSpan? GetTimeUntilExpiry()
    {
        if (!ExpiresAt.HasValue) return null;
        var remaining = ExpiresAt.Value - DateTime.UtcNow;
        return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
    }
    
    // Domain validation rules
    public (bool IsValid, List<string> Errors) ValidateForCreation()
    {
        var errors = new List<string>();
        
        if (Amount <= 0)
            errors.Add("Payment amount must be greater than zero");
            
        if (Amount > 1000000) // 1M limit
            errors.Add("Payment amount exceeds maximum allowed limit");
            
        if (string.IsNullOrWhiteSpace(Currency) || Currency.Length != 3)
            errors.Add("Currency must be a valid 3-character ISO code");
            
        if (string.IsNullOrWhiteSpace(Description))
            errors.Add("Payment description is required");
            
        if (ExpiresAt.HasValue && ExpiresAt.Value <= DateTime.UtcNow)
            errors.Add("Payment expiration date must be in the future");
            
        if (ExpiresAt.HasValue && ExpiresAt.Value > DateTime.UtcNow.AddDays(30))
            errors.Add("Payment expiration date cannot exceed 30 days from now");
            
        return (errors.Count == 0, errors);
    }
    
    public (bool IsValid, List<string> Errors) ValidateForAuthorization()
    {
        var errors = new List<string>();
        
        if (Status != PaymentStatus.NEW && Status != PaymentStatus.FORM_SHOWED)
            errors.Add($"Payment cannot be authorized from status {Status}");
            
        if (ExpiresAt.HasValue && DateTime.UtcNow > ExpiresAt.Value)
            errors.Add("Payment has expired and cannot be authorized");
            
        if (AuthorizationAttempts >= MaxAllowedAttempts)
            errors.Add("Maximum authorization attempts exceeded");
            
        return (errors.Count == 0, errors);
    }
    
    public (bool IsValid, List<string> Errors) ValidateForConfirmation()
    {
        var errors = new List<string>();
        
        if (Status != PaymentStatus.AUTHORIZED)
            errors.Add($"Payment cannot be confirmed from status {Status}");
            
        if (ExpiresAt.HasValue && DateTime.UtcNow > ExpiresAt.Value)
            errors.Add("Payment has expired and cannot be confirmed");
            
        return (errors.Count == 0, errors);
    }
    
    public (bool IsValid, List<string> Errors) ValidateForRefund(decimal refundAmount)
    {
        var errors = new List<string>();
        
        if (Status != PaymentStatus.CONFIRMED)
            errors.Add($"Payment cannot be refunded from status {Status}");
            
        if (refundAmount <= 0)
            errors.Add("Refund amount must be greater than zero");
            
        if (RefundedAmount + refundAmount > Amount)
            errors.Add("Total refund amount cannot exceed payment amount");
            
        if (RefundCount >= 10) // Max 10 refunds per payment
            errors.Add("Maximum number of refunds exceeded");
            
        return (errors.Count == 0, errors);
    }
}

