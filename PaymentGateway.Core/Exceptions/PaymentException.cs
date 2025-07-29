using PaymentGateway.Core.Enums;

namespace PaymentGateway.Core.Exceptions;

/// <summary>
/// Exception thrown during payment processing operations
/// </summary>
public class PaymentException : Exception
{
    public PaymentException(string message) : base(message)
    {
        ErrorCode = PaymentErrorCode.InternalRequestProcessingError;
    }

    public PaymentException(string message, Exception innerException) : base(message, innerException)
    {
        ErrorCode = PaymentErrorCode.InternalRequestProcessingError;
    }

    public PaymentException(PaymentErrorCode errorCode, string message) : base(message)
    {
        ErrorCode = errorCode;
    }

    public PaymentException(PaymentErrorCode errorCode, string message, Exception innerException) 
        : base(message, innerException)
    {
        ErrorCode = errorCode;
    }

    /// <summary>
    /// The specific payment error code associated with this exception
    /// </summary>
    public PaymentErrorCode ErrorCode { get; }

    public string? PaymentId { get; set; }
    public string? OrderId { get; set; }
    public string? MerchantId { get; set; }
}