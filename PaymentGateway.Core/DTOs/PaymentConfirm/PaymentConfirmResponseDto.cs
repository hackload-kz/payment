using System.Text.Json.Serialization;
using PaymentGateway.Core.DTOs.Common;

namespace PaymentGateway.Core.DTOs.PaymentConfirm;

/// <summary>
/// Response DTO for payment confirmation
/// </summary>
public class PaymentConfirmResponseDto : BaseResponseDto
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
    /// Current payment status after confirmation
    /// </summary>
    [JsonPropertyName("status")]
    public string? Status { get; set; }

    /// <summary>
    /// Original authorized amount in kopecks
    /// </summary>
    [JsonPropertyName("authorizedAmount")]
    public decimal? AuthorizedAmount { get; set; }

    /// <summary>
    /// Confirmed (captured) amount in kopecks
    /// </summary>
    [JsonPropertyName("confirmedAmount")]
    public decimal? ConfirmedAmount { get; set; }

    /// <summary>
    /// Remaining amount that can still be captured
    /// </summary>
    [JsonPropertyName("remainingAmount")]
    public decimal? RemainingAmount { get; set; }

    /// <summary>
    /// Currency code
    /// </summary>
    [JsonPropertyName("currency")]
    public string? Currency { get; set; }

    /// <summary>
    /// Confirmation timestamp
    /// </summary>
    [JsonPropertyName("confirmedAt")]
    public DateTime? ConfirmedAt { get; set; }

    /// <summary>
    /// Bank transaction details
    /// </summary>
    [JsonPropertyName("bankDetails")]
    public BankTransactionDetailsDto? BankDetails { get; set; }

    /// <summary>
    /// Fee information
    /// </summary>
    [JsonPropertyName("fees")]
    public FeeDetailsDto? Fees { get; set; }

    /// <summary>
    /// Settlement information
    /// </summary>
    [JsonPropertyName("settlement")]
    public SettlementDetailsDto? Settlement { get; set; }

    /// <summary>
    /// Additional confirmation details
    /// </summary>
    [JsonPropertyName("details")]
    public new ConfirmationDetailsDto? Details { get; set; }
}

/// <summary>
/// Bank transaction details
/// </summary>
public class BankTransactionDetailsDto
{
    /// <summary>
    /// Bank transaction identifier
    /// </summary>
    [JsonPropertyName("bankTransactionId")]
    public string? BankTransactionId { get; set; }

    /// <summary>
    /// Authorization code from bank
    /// </summary>
    [JsonPropertyName("authorizationCode")]
    public string? AuthorizationCode { get; set; }

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
/// Fee information
/// </summary>
public class FeeDetailsDto
{
    /// <summary>
    /// Processing fee in kopecks
    /// </summary>
    [JsonPropertyName("processingFee")]
    public decimal? ProcessingFee { get; set; }

    /// <summary>
    /// Acquirer fee in kopecks
    /// </summary>
    [JsonPropertyName("acquirerFee")]
    public decimal? AcquirerFee { get; set; }

    /// <summary>
    /// Network fee in kopecks
    /// </summary>
    [JsonPropertyName("networkFee")]
    public decimal? NetworkFee { get; set; }

    /// <summary>
    /// Total fees in kopecks
    /// </summary>
    [JsonPropertyName("totalFees")]
    public decimal? TotalFees { get; set; }

    /// <summary>
    /// Fee currency
    /// </summary>
    [JsonPropertyName("feeCurrency")]
    public string? FeeCurrency { get; set; }
}

/// <summary>
/// Settlement information
/// </summary>
public class SettlementDetailsDto
{
    /// <summary>
    /// Expected settlement date
    /// </summary>
    [JsonPropertyName("settlementDate")]
    public DateTime? SettlementDate { get; set; }

    /// <summary>
    /// Settlement amount after fees
    /// </summary>
    [JsonPropertyName("settlementAmount")]
    public decimal? SettlementAmount { get; set; }

    /// <summary>
    /// Settlement currency
    /// </summary>
    [JsonPropertyName("settlementCurrency")]
    public string? SettlementCurrency { get; set; }

    /// <summary>
    /// Exchange rate if applicable
    /// </summary>
    [JsonPropertyName("exchangeRate")]
    public decimal? ExchangeRate { get; set; }
}

/// <summary>
/// Confirmation details
/// </summary>
public class ConfirmationDetailsDto
{
    /// <summary>
    /// Confirmation description
    /// </summary>
    [JsonPropertyName("description")]
    public string? Description { get; set; }

    /// <summary>
    /// Items confirmed
    /// </summary>
    [JsonPropertyName("confirmedItems")]
    public List<ConfirmedItemDto>? ConfirmedItems { get; set; }

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
}

/// <summary>
/// Confirmed item details
/// </summary>
public class ConfirmedItemDto
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
    /// Confirmed quantity
    /// </summary>
    [JsonPropertyName("confirmedQuantity")]
    public decimal? ConfirmedQuantity { get; set; }

    /// <summary>
    /// Confirmed amount
    /// </summary>
    [JsonPropertyName("confirmedAmount")]
    public decimal? ConfirmedAmount { get; set; }

    /// <summary>
    /// Tax information
    /// </summary>
    [JsonPropertyName("tax")]
    public string? Tax { get; set; }
}