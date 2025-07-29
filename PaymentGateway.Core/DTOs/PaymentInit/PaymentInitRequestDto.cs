using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using PaymentGateway.Core.DTOs.Common;

namespace PaymentGateway.Core.DTOs.PaymentInit;

/// <summary>
/// Request DTO for payment initialization
/// </summary>
public class PaymentInitRequestDto : BaseRequestDto
{
    /// <summary>
    /// Payment amount in kopecks (e.g., 312 for 3.12 RUB)
    /// </summary>
    [Required(ErrorMessage = "Amount is required")]
    [Range(1000, 50000000, ErrorMessage = "Amount must be between 1000 kopecks (10 RUB) and 50000000 kopecks (500000 RUB)")]
    [JsonPropertyName("amount")]
    public decimal Amount { get; set; }

    /// <summary>
    /// Unique order identifier in merchant system
    /// </summary>
    [Required(ErrorMessage = "OrderId is required")]
    [StringLength(36, ErrorMessage = "OrderId cannot exceed 36 characters")]
    [JsonPropertyName("orderId")]
    public string OrderId { get; set; } = string.Empty;

    /// <summary>
    /// Currency code (ISO 4217)
    /// </summary>
    [StringLength(3, MinimumLength = 3, ErrorMessage = "Currency must be exactly 3 characters")]
    [JsonPropertyName("currency")]
    public string Currency { get; set; } = "RUB";

    /// <summary>
    /// Payment type: O = Single-stage payment (immediate capture), T = Two-stage payment (auth + capture)
    /// </summary>
    [RegularExpression("^[OT]$", ErrorMessage = "PayType must be 'O' (single-stage) or 'T' (two-stage)")]
    [JsonPropertyName("payType")]
    public string? PayType { get; set; }

    /// <summary>
    /// Order description displayed on payment form
    /// </summary>
    [StringLength(140, ErrorMessage = "Description cannot exceed 140 characters")]
    [JsonPropertyName("description")]
    public string? Description { get; set; }

    /// <summary>
    /// Customer identifier in merchant system
    /// </summary>
    [StringLength(36, ErrorMessage = "CustomerKey cannot exceed 36 characters")]
    [JsonPropertyName("customerKey")]
    public string? CustomerKey { get; set; }

    /// <summary>
    /// Customer email address
    /// </summary>
    [EmailAddress(ErrorMessage = "Invalid email format")]
    [StringLength(254, ErrorMessage = "Email cannot exceed 254 characters")]
    [JsonPropertyName("email")]
    public string? Email { get; set; }

    /// <summary>
    /// Customer phone number
    /// </summary>
    [RegularExpression(@"^\+?[1-9]\d{6,19}$", ErrorMessage = "Phone must be 7-20 digits with optional leading +")]
    [JsonPropertyName("phone")]
    public string? Phone { get; set; }

    /// <summary>
    /// Language for payment form (ru, en)
    /// </summary>
    [StringLength(2, MinimumLength = 2, ErrorMessage = "Language must be exactly 2 characters")]
    [RegularExpression("^(ru|en)$", ErrorMessage = "Language must be 'ru' or 'en'")]
    [JsonPropertyName("language")]
    public string Language { get; set; } = "ru";

    /// <summary>
    /// Success callback URL
    /// </summary>
    [Url(ErrorMessage = "SuccessURL must be a valid URL")]
    [StringLength(2048, ErrorMessage = "SuccessURL cannot exceed 2048 characters")]
    [JsonPropertyName("successURL")]
    public string? SuccessURL { get; set; }

    /// <summary>
    /// Failure callback URL
    /// </summary>
    [Url(ErrorMessage = "FailURL must be a valid URL")]
    [StringLength(2048, ErrorMessage = "FailURL cannot exceed 2048 characters")]
    [JsonPropertyName("failURL")]
    public string? FailURL { get; set; }

    /// <summary>
    /// Notification URL for webhooks
    /// </summary>
    [Url(ErrorMessage = "NotificationURL must be a valid URL")]
    [StringLength(2048, ErrorMessage = "NotificationURL cannot exceed 2048 characters")]
    [JsonPropertyName("notificationURL")]
    public string? NotificationURL { get; set; }

    /// <summary>
    /// Payment expiration time in minutes (default: 30)
    /// </summary>
    [Range(1, 43200, ErrorMessage = "PaymentExpiry must be between 1 and 43200 minutes (30 days)")]
    [JsonPropertyName("paymentExpiry")]
    public int PaymentExpiry { get; set; } = 30;

    /// <summary>
    /// Additional data for payment processing
    /// </summary>
    [JsonPropertyName("data")]
    public Dictionary<string, string>? Data { get; set; }

    /// <summary>
    /// Fiscal receipt information
    /// </summary>
    [JsonPropertyName("receipt")]
    public ReceiptDto? Receipt { get; set; }

    /// <summary>
    /// Items in the order
    /// </summary>
    [JsonPropertyName("items")]
    public List<OrderItemDto>? Items { get; set; }

    /// <summary>
    /// Dynamic merchant descriptor for payment display
    /// </summary>
    [StringLength(50, ErrorMessage = "Descriptor cannot exceed 50 characters")]
    [JsonPropertyName("descriptor")]
    public string? Descriptor { get; set; }

    /// <summary>
    /// Redirect method (GET or POST)
    /// </summary>
    [RegularExpression("^(GET|POST)$", ErrorMessage = "RedirectDueDate must be 'GET' or 'POST'")]
    [JsonPropertyName("redirectMethod")]
    public string RedirectMethod { get; set; } = "POST";
}

/// <summary>
/// Fiscal receipt information
/// </summary>
public class ReceiptDto
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
    /// Taxation system
    /// </summary>
    [JsonPropertyName("taxation")]
    public string? Taxation { get; set; }

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
/// Order item information
/// </summary>
public class OrderItemDto
{
    /// <summary>
    /// Item name
    /// </summary>
    [Required(ErrorMessage = "Item name is required")]
    [StringLength(100, ErrorMessage = "Item name cannot exceed 100 characters")]
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Item quantity
    /// </summary>
    [Range(0.001, 99999.999, ErrorMessage = "Quantity must be between 0.001 and 99999.999")]
    [JsonPropertyName("quantity")]
    public decimal Quantity { get; set; }

    /// <summary>
    /// Item price in kopecks
    /// </summary>
    [Range(1, int.MaxValue, ErrorMessage = "Price must be greater than 0")]
    [JsonPropertyName("price")]
    public decimal Price { get; set; }

    /// <summary>
    /// Total amount for this item in kopecks
    /// </summary>
    [Range(1, long.MaxValue, ErrorMessage = "Amount must be greater than 0")]
    [JsonPropertyName("amount")]
    public decimal Amount { get; set; }

    /// <summary>
    /// Tax type
    /// </summary>
    [JsonPropertyName("tax")]
    public string? Tax { get; set; }

    /// <summary>
    /// Item category
    /// </summary>
    [StringLength(50, ErrorMessage = "Category cannot exceed 50 characters")]
    [JsonPropertyName("category")]
    public string? Category { get; set; }
}