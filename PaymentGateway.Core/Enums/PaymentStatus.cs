namespace PaymentGateway.Core.Enums;

/// <summary>
/// Payment status enumeration
/// </summary>
public enum PaymentStatus
{
    // Initialization Phase
    INIT = 0,
    NEW = 1,
    FORM_SHOWED = 2,
    
    // Authorization Phase
    AUTHORIZING = 12,
    AUTHORIZED = 13,
    AUTH_FAIL = 14,
    
    // Processing Phase
    PROCESSING = 15,
    
    // Confirmation Phase
    CONFIRMING = 21,
    CONFIRMED = 22,
    CAPTURED = 23,
    
    // Cancellation and Reversal
    CANCELLING = 31,
    CANCELLED = 32,
    REVERSING = 33,
    REVERSED = 34,
    
    // Refund Operations
    REFUNDING = 40,
    REFUNDED = 41,
    PARTIALLY_REFUNDED = 42,
    PARTIAL_REFUNDED = 42, // Alias for consistency
    
    // Final States
    REJECTED = 50,
    DEADLINE_EXPIRED = 51,
    EXPIRED = 52,
    COMPLETED = 60,
    FAILED = 70
}

/// <summary>
/// Payment method enumeration
/// </summary>
public enum PaymentMethod
{
    Card = 0,
    SBP = 1,        // Faster Payment System (Russia)
    Wallet = 2,
    ApplePay = 3,
    GooglePay = 4,
    SamsungPay = 5,
    BankTransfer = 6,
    Cryptocurrency = 7,
    Cash = 8,
    Other = 99
}

/// <summary>
/// Transaction type enumeration
/// </summary>
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
    CHARGEBACK_REVERSAL = 10, // Chargeback reversal
    INQUIRY = 11           // Inquiry transaction
}

/// <summary>
/// Transaction status enumeration
/// </summary>
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
    DISPUTE_RESOLVED = 15, // Dispute resolved
    SUCCESS = 2            // Alias for COMPLETED
}