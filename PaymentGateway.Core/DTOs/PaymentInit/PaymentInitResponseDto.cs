using System.Text.Json.Serialization;
using PaymentGateway.Core.DTOs.Common;

namespace PaymentGateway.Core.DTOs.PaymentInit;

/// <summary>
/// Response DTO for payment initialization
/// </summary>
public class PaymentInitResponseDto : BaseResponseDto
{
    /// <summary>
    /// Payment identifier in the system
    /// </summary>
    [JsonPropertyName("paymentId")]
    public string? PaymentId { get; set; }

    /// <summary>
    /// Order identifier from request
    /// </summary>
    [JsonPropertyName("orderId")]
    public string? OrderId { get; set; }

    /// <summary>
    /// Current payment status
    /// </summary>
    [JsonPropertyName("status")]
    public string? Status { get; set; }

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
    /// URL for redirecting customer to payment form
    /// </summary>
    [JsonPropertyName("paymentURL")]
    public string? PaymentURL { get; set; }

    /// <summary>
    /// Payment expiration date and time
    /// </summary>
    [JsonPropertyName("expiresAt")]
    public DateTime? ExpiresAt { get; set; }

    /// <summary>
    /// Payment creation date and time
    /// </summary>
    [JsonPropertyName("createdAt")]
    public DateTime? CreatedAt { get; set; }

    /// <summary>
    /// Customer information
    /// </summary>
    [JsonPropertyName("customer")]
    public CustomerInfoDto? Customer { get; set; }

    /// <summary>
    /// Payment method information
    /// </summary>
    [JsonPropertyName("paymentMethod")]
    public PaymentMethodInfoDto? PaymentMethod { get; set; }

    /// <summary>
    /// Additional payment details
    /// </summary>
    [JsonPropertyName("details")]
    public new PaymentDetailsDto? Details { get; set; }
}

/// <summary>
/// Customer information
/// </summary>
public class CustomerInfoDto
{
    /// <summary>
    /// Customer key from request
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

    /// <summary>
    /// Customer user agent
    /// </summary>
    [JsonPropertyName("userAgent")]
    public string? UserAgent { get; set; }
}

/// <summary>
/// Payment method information
/// </summary>
public class PaymentMethodInfoDto
{
    /// <summary>
    /// Payment method type (Card, SBP, Wallet, etc.)
    /// </summary>
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    /// <summary>
    /// Available payment methods
    /// </summary>
    [JsonPropertyName("availableMethods")]
    public List<string>? AvailableMethods { get; set; }

    /// <summary>
    /// Default payment method
    /// </summary>
    [JsonPropertyName("defaultMethod")]
    public string? DefaultMethod { get; set; }

    /// <summary>
    /// Saved cards for customer (if any)
    /// </summary>
    [JsonPropertyName("savedCards")]
    public List<SavedCardDto>? SavedCards { get; set; }
}

/// <summary>
/// Saved card information
/// </summary>
public class SavedCardDto
{
    /// <summary>
    /// Card identifier
    /// </summary>
    [JsonPropertyName("cardId")]
    public string? CardId { get; set; }

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
    /// Card expiration date (MM/YY format)
    /// </summary>
    [JsonPropertyName("expiryDate")]
    public string? ExpiryDate { get; set; }

    /// <summary>
    /// Is default card
    /// </summary>
    [JsonPropertyName("isDefault")]
    public bool IsDefault { get; set; }
}

/// <summary>
/// Payment details
/// </summary>
public class PaymentDetailsDto
{
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
    /// Language setting
    /// </summary>
    [JsonPropertyName("language")]
    public string? Language { get; set; }

    /// <summary>
    /// Redirect method
    /// </summary>
    [JsonPropertyName("redirectMethod")]
    public string? RedirectMethod { get; set; }

    /// <summary>
    /// Success URL
    /// </summary>
    [JsonPropertyName("successURL")]
    public string? SuccessURL { get; set; }

    /// <summary>
    /// Failure URL
    /// </summary>
    [JsonPropertyName("failURL")]
    public string? FailURL { get; set; }

    /// <summary>
    /// Notification URL
    /// </summary>
    [JsonPropertyName("notificationURL")]
    public string? NotificationURL { get; set; }

    /// <summary>
    /// Additional data
    /// </summary>
    [JsonPropertyName("data")]
    public Dictionary<string, string>? Data { get; set; }

    /// <summary>
    /// Order items
    /// </summary>
    [JsonPropertyName("items")]
    public List<OrderItemDto>? Items { get; set; }
}