namespace PaymentGateway.Core.Enums;

/// <summary>
/// Payment status enumeration
/// </summary>
public enum PaymentStatus
{
    INIT,
    NEW,
    AUTHORIZED,
    CONFIRMED,
    CAPTURED,
    CANCELLED,
    REJECTED,
    EXPIRED,
    PROCESSING,
    COMPLETED,
    FAILED,
    REFUNDED,
    PARTIALLY_REFUNDED
}