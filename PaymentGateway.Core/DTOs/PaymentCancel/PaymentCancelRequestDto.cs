using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using PaymentGateway.Core.DTOs.Common;

namespace PaymentGateway.Core.DTOs.PaymentCancel;

/// <summary>
/// Request DTO for payment cancellation
/// </summary>
public class PaymentCancelRequestDto : BaseRequestDto
{
    /// <summary>
    /// Payment identifier to cancel
    /// </summary>
    [Required(ErrorMessage = "PaymentId is required")]
    [StringLength(50, ErrorMessage = "PaymentId cannot exceed 50 characters")]
    [JsonPropertyName("paymentId")]
    public string PaymentId { get; set; } = string.Empty;

    /// <summary>
    /// Reason for cancellation
    /// </summary>
    [StringLength(255, ErrorMessage = "Reason cannot exceed 255 characters")]
    [JsonPropertyName("reason")]
    public string? Reason { get; set; }

    /// <summary>
    /// Amount to refund in kopecks (for partial refunds of confirmed payments)
    /// If not specified for confirmed payments, full amount will be refunded
    /// </summary>
    [Range(1, 50000000, ErrorMessage = "Amount must be between 1 kopeck and 50000000 kopecks (500000 RUB)")]
    [JsonPropertyName("amount")]
    public decimal? Amount { get; set; }

    /// <summary>
    /// Receipt information for fiscal compliance (required for refunds)
    /// </summary>
    [JsonPropertyName("receipt")]
    public CancelReceiptDto? Receipt { get; set; }

    /// <summary>
    /// Items to cancel/refund (for partial cancellations)
    /// </summary>
    [JsonPropertyName("items")]
    public List<CancelItemDto>? Items { get; set; }

    /// <summary>
    /// Force cancellation even if payment is in processing state
    /// </summary>
    [JsonPropertyName("force")]
    public bool Force { get; set; } = false;

    /// <summary>
    /// Additional data for cancellation
    /// </summary>
    [JsonPropertyName("data")]
    public Dictionary<string, string>? Data { get; set; }
}

/// <summary>
/// Receipt information for cancellation/refund
/// </summary>
public class CancelReceiptDto
{
    /// <summary>
    /// Receipt in FFD 1.05 format
    /// </summary>
    [JsonPropertyName("Receipt_FFD_105")]
    public object? Receipt_FFD_105 { get; set; }

    /// <summary>
    /// Receipt in FFD 1.2 format
    /// </summary>
    [JsonPropertyName("Receipt_FFD_12")]
    public object? Receipt_FFD_12 { get; set; }

    /// <summary>
    /// Receipt email
    /// </summary>
    [EmailAddress(ErrorMessage = "Invalid receipt email format")]
    [JsonPropertyName("email")]
    public string? Email { get; set; }

    /// <summary>
    /// Receipt phone
    /// </summary>
    [JsonPropertyName("phone")]
    public string? Phone { get; set; }

    /// <summary>
    /// Taxation system
    /// </summary>
    [JsonPropertyName("taxation")]
    public string? Taxation { get; set; }
}

/// <summary>
/// Item information for cancellation/refund
/// </summary>
public class CancelItemDto
{
    /// <summary>
    /// Item identifier from original order
    /// </summary>
    [Required(ErrorMessage = "Item ID is required")]
    [StringLength(50, ErrorMessage = "Item ID cannot exceed 50 characters")]
    [JsonPropertyName("itemId")]
    public string ItemId { get; set; } = string.Empty;

    /// <summary>
    /// Quantity to cancel/refund
    /// </summary>
    [Range(0.001, 99999.999, ErrorMessage = "Quantity must be between 0.001 and 99999.999")]
    [JsonPropertyName("quantity")]
    public decimal Quantity { get; set; }

    /// <summary>
    /// Amount to cancel/refund for this item in kopecks
    /// </summary>
    [Range(1, long.MaxValue, ErrorMessage = "Amount must be greater than 0")]
    [JsonPropertyName("amount")]
    public decimal Amount { get; set; }

    /// <summary>
    /// Reason for cancelling this specific item
    /// </summary>
    [StringLength(100, ErrorMessage = "Item reason cannot exceed 100 characters")]
    [JsonPropertyName("reason")]
    public string? Reason { get; set; }

    /// <summary>
    /// Tax information
    /// </summary>
    [JsonPropertyName("tax")]
    public string? Tax { get; set; }
}