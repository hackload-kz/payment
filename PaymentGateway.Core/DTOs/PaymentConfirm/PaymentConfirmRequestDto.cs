using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using PaymentGateway.Core.DTOs.Common;

namespace PaymentGateway.Core.DTOs.PaymentConfirm;

/// <summary>
/// Request DTO for payment confirmation (capture phase of two-stage payment)
/// </summary>
public class PaymentConfirmRequestDto : BaseRequestDto
{
    /// <summary>
    /// Payment identifier to confirm
    /// </summary>
    [Required(ErrorMessage = "PaymentId is required")]
    [StringLength(50, ErrorMessage = "PaymentId cannot exceed 50 characters")]
    [JsonPropertyName("paymentId")]
    public string PaymentId { get; set; } = string.Empty;

    /// <summary>
    /// Amount to capture in kopecks (must be <= authorized amount)
    /// If not specified, full authorized amount will be captured
    /// </summary>
    [Range(1, 50000000, ErrorMessage = "Amount must be between 1 kopeck and 50000000 kopecks (500000 RUB)")]
    [JsonPropertyName("amount")]
    public decimal? Amount { get; set; }

    /// <summary>
    /// Optional description for the confirmation
    /// </summary>
    [StringLength(255, ErrorMessage = "Description cannot exceed 255 characters")]
    [JsonPropertyName("description")]
    public string? Description { get; set; }

    /// <summary>
    /// Receipt information for fiscal compliance
    /// </summary>
    [JsonPropertyName("receipt")]
    public ConfirmReceiptDto? Receipt { get; set; }

    /// <summary>
    /// Items to confirm (for partial confirmations)
    /// </summary>
    [JsonPropertyName("items")]
    public List<ConfirmItemDto>? Items { get; set; }

    /// <summary>
    /// Additional data for confirmation
    /// </summary>
    [JsonPropertyName("data")]
    public Dictionary<string, string>? Data { get; set; }
}

/// <summary>
/// Receipt information for confirmation
/// </summary>
public class ConfirmReceiptDto
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
}

/// <summary>
/// Item information for confirmation
/// </summary>
public class ConfirmItemDto
{
    /// <summary>
    /// Item identifier from original order
    /// </summary>
    [Required(ErrorMessage = "Item ID is required")]
    [StringLength(50, ErrorMessage = "Item ID cannot exceed 50 characters")]
    [JsonPropertyName("itemId")]
    public string ItemId { get; set; } = string.Empty;

    /// <summary>
    /// Quantity to confirm
    /// </summary>
    [Range(0.001, 99999.999, ErrorMessage = "Quantity must be between 0.001 and 99999.999")]
    [JsonPropertyName("quantity")]
    public decimal Quantity { get; set; }

    /// <summary>
    /// Amount to confirm for this item in kopecks
    /// </summary>
    [Range(1, long.MaxValue, ErrorMessage = "Amount must be greater than 0")]
    [JsonPropertyName("amount")]
    public decimal Amount { get; set; }

    /// <summary>
    /// Tax information
    /// </summary>
    [JsonPropertyName("tax")]
    public string? Tax { get; set; }
}