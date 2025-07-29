using PaymentGateway.Core.Interfaces;

namespace PaymentGateway.Core.Entities;

public class Payment : BaseEntity
{
    public string PaymentId { get; set; } = string.Empty;
    public string OrderId { get; set; } = string.Empty;
    public string TeamSlug { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "RUB";
    public PaymentStatus Status { get; set; } = PaymentStatus.INIT;
    public string? Description { get; set; }
    public string? CustomerEmail { get; set; }
    public string? PaymentURL { get; set; }
    public DateTime? AuthorizedAt { get; set; }
    public DateTime? ConfirmedAt { get; set; }
    public DateTime? CancelledAt { get; set; }
    public string? FailureReason { get; set; }
    public string? BankOrderId { get; set; }
    public string? CardMask { get; set; }
    public PaymentMethod PaymentMethod { get; set; } = PaymentMethod.Card;
}

public enum PaymentStatus
{
    INIT = 0,
    NEW = 1,
    FORM_SHOWED = 2,
    AUTHORIZED = 3,
    CONFIRMED = 4,
    CANCELLED = 5,
    REJECTED = 6,
    REFUNDED = 7,
    PARTIAL_REFUNDED = 8,
    EXPIRED = 9
}

public enum PaymentMethod
{
    Card = 0,
    SBP = 1,
    Wallet = 2
}