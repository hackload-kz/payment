using System.Text.Json.Serialization;
using PaymentGateway.Core.DTOs.Common;

namespace PaymentGateway.Core.DTOs.PaymentCancel;

/// <summary>
/// Response DTO for payment cancellation
/// </summary>
public class PaymentCancelResponseDto : BaseResponseDto
{
    /// <summary>
    /// Payment identifier
    /// </summary>
    [JsonPropertyName("paymentId")]
    public string? PaymentId { get; set; }

    /// <summary>
    /// Order identifier
    /// </summary>
    [JsonPropertyName("orderId")]
    public string? OrderId { get; set; }

    /// <summary>
    /// Current payment status after cancellation
    /// </summary>
    [JsonPropertyName("status")]
    public string? Status { get; set; }

    /// <summary>
    /// Type of cancellation performed
    /// </summary>
    [JsonPropertyName("cancellationType")]
    public string? CancellationType { get; set; }

    /// <summary>
    /// Original payment amount in kopecks
    /// </summary>
    [JsonPropertyName("originalAmount")]
    public decimal? OriginalAmount { get; set; }

    /// <summary>
    /// Cancelled/refunded amount in kopecks
    /// </summary>
    [JsonPropertyName("cancelledAmount")]
    public decimal? CancelledAmount { get; set; }

    /// <summary>
    /// Remaining amount (for partial cancellations)
    /// </summary>
    [JsonPropertyName("remainingAmount")]
    public decimal? RemainingAmount { get; set; }

    /// <summary>
    /// Currency code
    /// </summary>
    [JsonPropertyName("currency")]
    public string? Currency { get; set; }

    /// <summary>
    /// Cancellation timestamp
    /// </summary>
    [JsonPropertyName("cancelledAt")]
    public DateTime? CancelledAt { get; set; }

    /// <summary>
    /// Bank transaction details for refund
    /// </summary>
    [JsonPropertyName("bankDetails")]
    public CancelBankDetailsDto? BankDetails { get; set; }

    /// <summary>
    /// Refund information (for confirmed payment cancellations)
    /// </summary>
    [JsonPropertyName("refund")]
    public RefundDetailsDto? Refund { get; set; }

    /// <summary>
    /// Additional cancellation details
    /// </summary>
    [JsonPropertyName("details")]
    public new CancellationDetailsDto? Details { get; set; }
}

/// <summary>
/// Bank details for cancellation
/// </summary>
public class CancelBankDetailsDto
{
    /// <summary>
    /// Bank transaction identifier for cancellation
    /// </summary>
    [JsonPropertyName("bankTransactionId")]
    public string? BankTransactionId { get; set; }

    /// <summary>
    /// Original authorization code (for reversals)
    /// </summary>
    [JsonPropertyName("originalAuthorizationCode")]
    public string? OriginalAuthorizationCode { get; set; }

    /// <summary>
    /// Reversal/refund authorization code
    /// </summary>
    [JsonPropertyName("cancellationAuthorizationCode")]
    public string? CancellationAuthorizationCode { get; set; }

    /// <summary>
    /// RRN (Retrieval Reference Number)
    /// </summary>
    [JsonPropertyName("rrn")]
    public string? Rrn { get; set; }

    /// <summary>
    /// Response code from bank
    /// </summary>
    [JsonPropertyName("responseCode")]
    public string? ResponseCode { get; set; }

    /// <summary>
    /// Response message from bank
    /// </summary>
    [JsonPropertyName("responseMessage")]
    public string? ResponseMessage { get; set; }
}

/// <summary>
/// Refund details
/// </summary>
public class RefundDetailsDto
{
    /// <summary>
    /// Refund identifier
    /// </summary>
    [JsonPropertyName("refundId")]
    public string? RefundId { get; set; }

    /// <summary>
    /// Refund status
    /// </summary>
    [JsonPropertyName("refundStatus")]
    public string? RefundStatus { get; set; }

    /// <summary>
    /// Expected refund processing time
    /// </summary>
    [JsonPropertyName("expectedProcessingTime")]
    public string? ExpectedProcessingTime { get; set; }

    /// <summary>
    /// Refund method (same as original payment method)
    /// </summary>
    [JsonPropertyName("refundMethod")]
    public string? RefundMethod { get; set; }

    /// <summary>
    /// Card information for card refunds
    /// </summary>
    [JsonPropertyName("cardInfo")]
    public RefundCardInfoDto? CardInfo { get; set; }
}

/// <summary>
/// Card information for refund
/// </summary>
public class RefundCardInfoDto
{
    /// <summary>
    /// Masked card number
    /// </summary>
    [JsonPropertyName("cardMask")]
    public string? CardMask { get; set; }

    /// <summary>
    /// Card type
    /// </summary>
    [JsonPropertyName("cardType")]
    public string? CardType { get; set; }

    /// <summary>
    /// Issuing bank
    /// </summary>
    [JsonPropertyName("issuingBank")]
    public string? IssuingBank { get; set; }
}

/// <summary>
/// Cancellation details
/// </summary>
public class CancellationDetailsDto
{
    /// <summary>
    /// Cancellation reason
    /// </summary>
    [JsonPropertyName("reason")]
    public string? Reason { get; set; }

    /// <summary>
    /// Items cancelled
    /// </summary>
    [JsonPropertyName("cancelledItems")]
    public List<CancelledItemDto>? CancelledItems { get; set; }

    /// <summary>
    /// Whether cancellation was forced
    /// </summary>
    [JsonPropertyName("wasForced")]
    public bool WasForced { get; set; }

    /// <summary>
    /// Processing duration
    /// </summary>
    [JsonPropertyName("processingDuration")]
    public TimeSpan? ProcessingDuration { get; set; }

    /// <summary>
    /// Additional data
    /// </summary>
    [JsonPropertyName("data")]
    public Dictionary<string, string>? Data { get; set; }

    /// <summary>
    /// Warnings (if any)
    /// </summary>
    [JsonPropertyName("warnings")]
    public List<string>? Warnings { get; set; }
}

/// <summary>
/// Cancelled item details
/// </summary>
public class CancelledItemDto
{
    /// <summary>
    /// Item identifier
    /// </summary>
    [JsonPropertyName("itemId")]
    public string? ItemId { get; set; }

    /// <summary>
    /// Item name
    /// </summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>
    /// Cancelled quantity
    /// </summary>
    [JsonPropertyName("cancelledQuantity")]
    public decimal? CancelledQuantity { get; set; }

    /// <summary>
    /// Cancelled amount
    /// </summary>
    [JsonPropertyName("cancelledAmount")]
    public decimal? CancelledAmount { get; set; }

    /// <summary>
    /// Cancellation reason for this item
    /// </summary>
    [JsonPropertyName("reason")]
    public string? Reason { get; set; }

    /// <summary>
    /// Tax information
    /// </summary>
    [JsonPropertyName("tax")]
    public string? Tax { get; set; }
}