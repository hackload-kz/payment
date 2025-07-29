using PaymentGateway.Core.Interfaces;
using System.ComponentModel.DataAnnotations;

namespace PaymentGateway.Core.Entities;

public class Transaction : BaseEntity, IAuditableEntity
{
    [Required]
    [StringLength(50)]
    public string TransactionId { get; set; } = string.Empty;
    
    [Required]
    public int PaymentId { get; set; }
    
    [Required]
    [StringLength(50)]
    public string PaymentExternalId { get; set; } = string.Empty;
    
    public TransactionType Type { get; set; }
    
    public TransactionStatus Status { get; set; } = TransactionStatus.PENDING;
    
    [Range(0, double.MaxValue)]
    public decimal Amount { get; set; }
    
    [Required]
    [StringLength(3)]
    public string Currency { get; set; } = "RUB";
    
    // Bank and processing details
    [StringLength(100)]
    public string? BankTransactionId { get; set; }
    
    [StringLength(100)]
    public string? BankOrderId { get; set; }
    
    [StringLength(100)]
    public string? AuthorizationCode { get; set; }
    
    [StringLength(100)]
    public string? ProcessorTransactionId { get; set; }
    
    [StringLength(100)]
    public string? AcquirerTransactionId { get; set; }
    
    [StringLength(100)]
    public string? ExternalTransactionId { get; set; }
    
    // Card details (masked)
    [StringLength(50)]
    public string? CardMask { get; set; }
    
    [StringLength(100)]
    public string? CardType { get; set; }
    
    [StringLength(100)]
    public string? CardBrand { get; set; }
    
    [StringLength(100)]
    public string? CardBin { get; set; }
    
    [StringLength(100)]
    public string? IssuingBank { get; set; }
    
    [StringLength(100)]
    public string? CardCountry { get; set; }
    
    // Processing timestamps
    public DateTime? ProcessingStartedAt { get; set; }
    public DateTime? ProcessingCompletedAt { get; set; }
    public DateTime? AuthorizedAt { get; set; }
    public DateTime? CapturedAt { get; set; }
    public DateTime? RefundedAt { get; set; }
    public DateTime? ReversedAt { get; set; }
    public DateTime? FailedAt { get; set; }
    
    // Response details
    [StringLength(10)]
    public string? ResponseCode { get; set; }
    
    [StringLength(1000)]
    public string? ResponseMessage { get; set; }
    
    [StringLength(1000)]
    public string? FailureReason { get; set; }
    
    // Security and fraud prevention
    [StringLength(45)]
    public string? CustomerIpAddress { get; set; }
    
    [StringLength(500)]
    public string? UserAgent { get; set; }
    
    public bool Is3DSecureUsed { get; set; } = false;
    
    [StringLength(100)]
    public string? ThreeDSecureVersion { get; set; }
    
    [StringLength(100)]
    public string? ThreeDSecureStatus { get; set; }
    
    public int FraudScore { get; set; } = 0;
    
    [StringLength(100)]
    public string? RiskCategory { get; set; }
    
    // Retry and attempt tracking
    public int AttemptNumber { get; set; } = 1;
    public int MaxRetryAttempts { get; set; } = 3;
    public DateTime? NextRetryAt { get; set; }
    public DateTime? ExpiresAt { get; set; }
    
    // Fee and commission details
    public decimal ProcessingFee { get; set; } = 0;
    public decimal AcquirerFee { get; set; } = 0;
    public decimal NetworkFee { get; set; } = 0;
    public decimal TotalFees { get; set; } = 0;
    
    [StringLength(3)]
    public string? FeeCurrency { get; set; }
    
    // Settlement information
    public DateTime? SettlementDate { get; set; }
    public decimal SettlementAmount { get; set; } = 0;
    
    [StringLength(3)]
    public string? SettlementCurrency { get; set; }
    
    public decimal ExchangeRate { get; set; } = 1;
    
    // Additional processing metadata
    public Dictionary<string, string> ProcessingMetadata { get; set; } = new();
    public Dictionary<string, string> BankResponseData { get; set; } = new();
    public Dictionary<string, string> AcquirerResponseData { get; set; } = new();
    
    // Parent/Child transaction relationships
    public int? ParentTransactionId { get; set; }
    public virtual Transaction? ParentTransaction { get; set; }
    public virtual ICollection<Transaction> ChildTransactions { get; set; } = new List<Transaction>();
    
    // Navigation properties
    public virtual Payment Payment { get; set; } = null!;
    
    // Additional metadata
    public Dictionary<string, string> AdditionalData { get; set; } = new();
    
    // Domain methods
    public bool IsSuccessful()
    {
        return Status == TransactionStatus.COMPLETED;
    }
    
    public bool IsFailed()
    {
        return Status is TransactionStatus.FAILED or TransactionStatus.DECLINED or TransactionStatus.EXPIRED;
    }
    
    public bool IsPending()
    {
        return Status is TransactionStatus.PENDING or TransactionStatus.PROCESSING;
    }
    
    public bool CanBeRetried()
    {
        return IsFailed() && AttemptNumber < MaxRetryAttempts;
    }
    
    public bool RequiresManualReview()
    {
        return FraudScore > 75 || RiskCategory == "HIGH";
    }
    
    public TimeSpan? GetProcessingDuration()
    {
        if (ProcessingStartedAt.HasValue && ProcessingCompletedAt.HasValue)
        {
            return ProcessingCompletedAt.Value - ProcessingStartedAt.Value;
        }
        return null;
    }
    
    public decimal GetNetAmount()
    {
        return Amount - TotalFees;
    }
    
    public bool IsRefundTransaction()
    {
        return Type == TransactionType.REFUND;
    }
    
    public bool IsAuthorizationTransaction()
    {
        return Type is TransactionType.AUTHORIZATION or TransactionType.PREAUTH;
    }
    
    public bool IsCaptureTransaction()
    {
        return Type is TransactionType.CAPTURE or TransactionType.SALE;
    }
    
    // Domain validation rules
    public (bool IsValid, List<string> Errors) ValidateForCreation()
    {
        var errors = new List<string>();
        
        if (Amount <= 0)
            errors.Add("Transaction amount must be greater than zero");
            
        if (Amount > 5000000) // 5M transaction limit
            errors.Add("Transaction amount exceeds maximum allowed limit");
            
        if (string.IsNullOrWhiteSpace(Currency) || Currency.Length != 3)
            errors.Add("Currency must be a valid 3-character ISO code");
            
        if (string.IsNullOrWhiteSpace(ExternalTransactionId))
            errors.Add("External transaction ID is required");
            
        if (TotalFees < 0)
            errors.Add("Transaction fees cannot be negative");
            
        if (TotalFees > Amount)
            errors.Add("Transaction fees cannot exceed transaction amount");
            
        return (errors.Count == 0, errors);
    }
    
    public (bool IsValid, List<string> Errors) ValidateForProcessing()
    {
        var errors = new List<string>();
        
        if (Status != TransactionStatus.PENDING)
            errors.Add($"Transaction cannot be processed from status {Status}");
            
        if (FraudScore > 90) // Block high fraud score transactions
            errors.Add("Transaction blocked due to high fraud score");
            
        if (RequiresManualReview() && Status != TransactionStatus.REQUIRES_REVIEW)
            errors.Add("Transaction requires manual review before processing");
            
        return (errors.Count == 0, errors);
    }
    
    public (bool IsValid, List<string> Errors) ValidateForCapture(decimal captureAmount)
    {
        var errors = new List<string>();
        
        if (!IsAuthorizationTransaction())
            errors.Add("Only authorization transactions can be captured");
            
        if (Status != TransactionStatus.COMPLETED)
            errors.Add($"Transaction cannot be captured from status {Status}");
            
        if (captureAmount <= 0)
            errors.Add("Capture amount must be greater than zero");
            
        if (captureAmount > Amount)
            errors.Add("Capture amount cannot exceed authorized amount");
            
        return (errors.Count == 0, errors);
    }
}

public enum TransactionType
{
    AUTHORIZATION = 1,      // Authorization only
    CAPTURE = 2,           // Capture of previously authorized amount
    SALE = 3,              // Authorization + Capture in one step
    PREAUTH = 4,           // Pre-authorization
    REFUND = 5,            // Full or partial refund
    REVERSAL = 6,          // Reversal of authorization
    VOID = 7,              // Void transaction
    SETTLEMENT = 8,        // Settlement transaction
    CHARGEBACK = 9,        // Chargeback transaction
    CHARGEBACK_REVERSAL = 10 // Chargeback reversal
}

public enum TransactionStatus
{
    PENDING = 1,           // Transaction initiated but not processed
    PROCESSING = 2,        // Currently being processed
    COMPLETED = 3,         // Successfully completed
    FAILED = 4,            // Failed due to processing error
    DECLINED = 5,          // Declined by bank or card issuer
    CANCELLED = 6,         // Cancelled by merchant or system
    EXPIRED = 7,           // Expired due to timeout
    PARTIALLY_COMPLETED = 8, // Partially completed (for partial refunds)
    REQUIRES_REVIEW = 9,   // Requires manual review
    ON_HOLD = 10,          // On hold for compliance/fraud review
    REVERSED = 11,         // Transaction reversed
    VOIDED = 12,           // Transaction voided
    SETTLED = 13,          // Transaction settled
    DISPUTE = 14,          // Transaction disputed/chargeback
    DISPUTE_RESOLVED = 15  // Dispute resolved
}