using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using PaymentGateway.Core.DTOs.Common;

namespace PaymentGateway.Core.DTOs.PaymentCheck;

/// <summary>
/// Request DTO for checking payment status
/// </summary>
public class PaymentCheckRequestDto : BaseRequestDto
{
    /// <summary>
    /// Payment identifier to check (either PaymentId or OrderId must be provided)
    /// </summary>
    [StringLength(50, ErrorMessage = "PaymentId cannot exceed 50 characters")]
    [JsonPropertyName("paymentId")]
    public string? PaymentId { get; set; }

    /// <summary>
    /// Order identifier to check (either PaymentId or OrderId must be provided)
    /// Note: Multiple payments can exist for the same OrderId
    /// </summary>
    [StringLength(36, ErrorMessage = "OrderId cannot exceed 36 characters")]
    [JsonPropertyName("orderId")]
    public string? OrderId { get; set; }

    /// <summary>
    /// Include transaction history in response
    /// </summary>
    [JsonPropertyName("includeTransactions")]
    public bool IncludeTransactions { get; set; } = false;

    /// <summary>
    /// Include card details in response (masked)
    /// </summary>
    [JsonPropertyName("includeCardDetails")]
    public bool IncludeCardDetails { get; set; } = false;

    /// <summary>
    /// Include customer information in response
    /// </summary>
    [JsonPropertyName("includeCustomerInfo")]
    public bool IncludeCustomerInfo { get; set; } = false;

    /// <summary>
    /// Include receipt information in response
    /// </summary>
    [JsonPropertyName("includeReceipt")]
    public bool IncludeReceipt { get; set; } = false;

    /// <summary>
    /// Language for localized messages
    /// </summary>
    [StringLength(2, MinimumLength = 2, ErrorMessage = "Language must be exactly 2 characters")]
    [RegularExpression("^(ru|en)$", ErrorMessage = "Language must be 'ru' or 'en'")]
    [JsonPropertyName("language")]
    public string Language { get; set; } = "ru";
}

/// <summary>
/// Custom validation attribute to ensure either PaymentId or OrderId is provided
/// </summary>
public class PaymentOrOrderIdRequiredAttribute : ValidationAttribute
{
    public override bool IsValid(object? value)
    {
        if (value is PaymentCheckRequestDto dto)
        {
            return !string.IsNullOrWhiteSpace(dto.PaymentId) || !string.IsNullOrWhiteSpace(dto.OrderId);
        }
        return false;
    }

    public override string FormatErrorMessage(string name)
    {
        return "Either PaymentId or OrderId must be provided";
    }
}