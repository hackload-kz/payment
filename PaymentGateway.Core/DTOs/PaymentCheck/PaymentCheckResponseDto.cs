using System.Text.Json.Serialization;
using PaymentGateway.Core.DTOs.Common;

namespace PaymentGateway.Core.DTOs.PaymentCheck;

/// <summary>
/// Response DTO for payment status check
/// </summary>
public class PaymentCheckResponseDto : BaseResponseDto
{
    /// <summary>
    /// List of payments found (can be multiple for same OrderId)
    /// </summary>
    [JsonPropertyName("payments")]
    public List<PaymentStatusDto> Payments { get; set; } = new();

    /// <summary>
    /// Total number of payments found
    /// </summary>
    [JsonPropertyName("totalCount")]
    public int TotalCount { get; set; }

    /// <summary>
    /// Order identifier if searched by OrderId
    /// </summary>
    [JsonPropertyName("orderId")]
    public string? OrderId { get; set; }
}

/// <summary>
/// Payment status information
/// </summary>
public class PaymentStatusDto
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
    /// Current payment status
    /// </summary>
    [JsonPropertyName("status")]
    public string? Status { get; set; }

    /// <summary>
    /// Human-readable status description
    /// </summary>
    [JsonPropertyName("statusDescription")]
    public string? StatusDescription { get; set; }

    /// <summary>
    /// Payment amount in kopecks
    /// </summary>
    [JsonPropertyName("amount")]
    public decimal? Amount { get; set; }

    /// <summary>
    /// Currency code
    /// </summary>
    [JsonPropertyName("currency")]
    public string? Currency { get; set; }

    /// <summary>
    /// Payment creation timestamp
    /// </summary>
    [JsonPropertyName("createdAt")]
    public DateTime? CreatedAt { get; set; }

    /// <summary>
    /// Last status update timestamp
    /// </summary>
    [JsonPropertyName("updatedAt")]
    public DateTime? UpdatedAt { get; set; }

    /// <summary>
    /// Payment expiration timestamp
    /// </summary>
    [JsonPropertyName("expiresAt")]
    public DateTime? ExpiresAt { get; set; }

    /// <summary>
    /// Payment description
    /// </summary>
    [JsonPropertyName("description")]
    public string? Description { get; set; }

    /// <summary>
    /// Payment type (O or T)
    /// </summary>
    [JsonPropertyName("payType")]
    public string? PayType { get; set; }

    /// <summary>
    /// Customer information (if requested)
    /// </summary>
    [JsonPropertyName("customer")]
    public CheckCustomerInfoDto? Customer { get; set; }

    /// <summary>
    /// Card details (if requested and available)
    /// </summary>
    [JsonPropertyName("cardDetails")]
    public CheckCardDetailsDto? CardDetails { get; set; }

    /// <summary>
    /// Transaction history (if requested)
    /// </summary>
    [JsonPropertyName("transactions")]
    public List<CheckTransactionDto>? Transactions { get; set; }

    /// <summary>
    /// Payment amounts breakdown
    /// </summary>
    [JsonPropertyName("amounts")]
    public PaymentAmountsDto? Amounts { get; set; }

    /// <summary>
    /// Payment URLs
    /// </summary>
    [JsonPropertyName("urls")]
    public PaymentUrlsDto? Urls { get; set; }

    /// <summary>
    /// Additional payment data
    /// </summary>
    [JsonPropertyName("data")]
    public Dictionary<string, string>? Data { get; set; }

    /// <summary>
    /// Receipt information (if requested)
    /// </summary>
    [JsonPropertyName("receipt")]
    public CheckReceiptDto? Receipt { get; set; }
}

/// <summary>
/// Customer information for check response
/// </summary>
public class CheckCustomerInfoDto
{
    /// <summary>
    /// Customer key
    /// </summary>
    [JsonPropertyName("customerKey")]
    public string? CustomerKey { get; set; }

    /// <summary>
    /// Customer email
    /// </summary>
    [JsonPropertyName("email")]
    public string? Email { get; set; }

    /// <summary>
    /// Customer phone
    /// </summary>
    [JsonPropertyName("phone")]
    public string? Phone { get; set; }

    /// <summary>
    /// Customer IP address
    /// </summary>
    [JsonPropertyName("ipAddress")]
    public string? IpAddress { get; set; }
}

/// <summary>
/// Card details for check response (masked)
/// </summary>
public class CheckCardDetailsDto
{
    /// <summary>
    /// Masked card number
    /// </summary>
    [JsonPropertyName("cardMask")]
    public string? CardMask { get; set; }

    /// <summary>
    /// Card type (Visa, MasterCard, etc.)
    /// </summary>
    [JsonPropertyName("cardType")]
    public string? CardType { get; set; }

    /// <summary>
    /// Card brand
    /// </summary>
    [JsonPropertyName("cardBrand")]
    public string? CardBrand { get; set; }

    /// <summary>
    /// Issuing bank name
    /// </summary>
    [JsonPropertyName("issuingBank")]
    public string? IssuingBank { get; set; }

    /// <summary>
    /// Card country
    /// </summary>
    [JsonPropertyName("cardCountry")]
    public string? CardCountry { get; set; }

    /// <summary>
    /// Card BIN
    /// </summary>
    [JsonPropertyName("cardBin")]
    public string? CardBin { get; set; }
}

/// <summary>
/// Transaction information for check response
/// </summary>
public class CheckTransactionDto
{
    /// <summary>
    /// Transaction identifier
    /// </summary>
    [JsonPropertyName("transactionId")]
    public string? TransactionId { get; set; }

    /// <summary>
    /// Transaction type
    /// </summary>
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    /// <summary>
    /// Transaction status
    /// </summary>
    [JsonPropertyName("status")]
    public string? Status { get; set; }

    /// <summary>
    /// Transaction amount
    /// </summary>
    [JsonPropertyName("amount")]
    public decimal? Amount { get; set; }

    /// <summary>
    /// Transaction timestamp
    /// </summary>
    [JsonPropertyName("timestamp")]
    public DateTime? Timestamp { get; set; }

    /// <summary>
    /// Bank response code
    /// </summary>
    [JsonPropertyName("responseCode")]
    public string? ResponseCode { get; set; }

    /// <summary>
    /// Bank response message
    /// </summary>
    [JsonPropertyName("responseMessage")]
    public string? ResponseMessage { get; set; }

    /// <summary>
    /// Authorization code
    /// </summary>
    [JsonPropertyName("authorizationCode")]
    public string? AuthorizationCode { get; set; }

    /// <summary>
    /// RRN
    /// </summary>
    [JsonPropertyName("rrn")]
    public string? Rrn { get; set; }
}

/// <summary>
/// Payment amounts breakdown
/// </summary>
public class PaymentAmountsDto
{
    /// <summary>
    /// Original payment amount
    /// </summary>
    [JsonPropertyName("originalAmount")]
    public decimal? OriginalAmount { get; set; }

    /// <summary>
    /// Authorized amount
    /// </summary>
    [JsonPropertyName("authorizedAmount")]
    public decimal? AuthorizedAmount { get; set; }

    /// <summary>
    /// Confirmed (captured) amount
    /// </summary>
    [JsonPropertyName("confirmedAmount")]
    public decimal? ConfirmedAmount { get; set; }

    /// <summary>
    /// Refunded amount
    /// </summary>
    [JsonPropertyName("refundedAmount")]
    public decimal? RefundedAmount { get; set; }

    /// <summary>
    /// Remaining amount available for capture
    /// </summary>
    [JsonPropertyName("remainingAmount")]
    public decimal? RemainingAmount { get; set; }

    /// <summary>
    /// Total fees
    /// </summary>
    [JsonPropertyName("totalFees")]
    public decimal? TotalFees { get; set; }
}

/// <summary>
/// Payment URLs
/// </summary>
public class PaymentUrlsDto
{
    /// <summary>
    /// Payment form URL
    /// </summary>
    [JsonPropertyName("paymentURL")]
    public string? PaymentURL { get; set; }

    /// <summary>
    /// Success callback URL
    /// </summary>
    [JsonPropertyName("successURL")]
    public string? SuccessURL { get; set; }

    /// <summary>
    /// Failure callback URL
    /// </summary>
    [JsonPropertyName("failURL")]
    public string? FailURL { get; set; }

    /// <summary>
    /// Notification URL
    /// </summary>
    [JsonPropertyName("notificationURL")]
    public string? NotificationURL { get; set; }
}

/// <summary>
/// Receipt information for check response
/// </summary>
public class CheckReceiptDto
{
    /// <summary>
    /// Receipt status
    /// </summary>
    [JsonPropertyName("status")]
    public string? Status { get; set; }

    /// <summary>
    /// Receipt creation timestamp
    /// </summary>
    [JsonPropertyName("createdAt")]
    public DateTime? CreatedAt { get; set; }

    /// <summary>
    /// Receipt content
    /// </summary>
    [JsonPropertyName("content")]
    public object? Content { get; set; }

    /// <summary>
    /// Receipt URL for viewing
    /// </summary>
    [JsonPropertyName("receiptUrl")]
    public string? ReceiptUrl { get; set; }
}