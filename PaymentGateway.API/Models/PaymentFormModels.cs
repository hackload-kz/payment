// SPDX-License-Identifier: MIT
// Copyright (c) 2025 HackLoad Payment Gateway

using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace PaymentGateway.API.Models;

/// <summary>
/// Payment form rendering data model
/// Contains all data needed to render a payment form
/// </summary>
public class PaymentFormViewModel
{
    [Required]
    public string PaymentId { get; set; } = string.Empty;
    
    public string? OrderId { get; set; }
    
    [Required]
    [Range(0.01, double.MaxValue, ErrorMessage = "Amount must be greater than 0")]
    public decimal Amount { get; set; }
    
    [Required]
    [StringLength(3, MinimumLength = 3)]
    public string Currency { get; set; } = string.Empty;
    
    public string? Description { get; set; }
    
    [Required]
    [StringLength(100, MinimumLength = 1)]
    public string MerchantName { get; set; } = string.Empty;
    
    [Url]
    public string? SuccessUrl { get; set; }
    
    [Url]
    public string? FailUrl { get; set; }
    
    [StringLength(2, MinimumLength = 2)]
    public string Language { get; set; } = "en";
    
    [Required]
    public string CsrfToken { get; set; } = string.Empty;
    
    public DateTime? PaymentTimeout { get; set; }
    
    [JsonIgnore]
    public Dictionary<string, object>? Receipt { get; set; }
    
    public string? ReceiptJson => Receipt != null ? 
        System.Text.Json.JsonSerializer.Serialize(Receipt) : null;
}

/// <summary>
/// Payment form submission model
/// Represents data submitted from the payment form
/// </summary>
public class PaymentFormSubmissionModel
{
    [Required(ErrorMessage = "Payment ID is required")]
    [StringLength(50, MinimumLength = 1)]
    public string PaymentId { get; set; } = string.Empty;
    
    [Required(ErrorMessage = "Card number is required")]
    [CreditCard(ErrorMessage = "Invalid card number format")]
    [StringLength(25, MinimumLength = 13)]
    public string CardNumber { get; set; } = string.Empty;
    
    [Required(ErrorMessage = "Expiry date is required")]
    [RegularExpression(@"^(0[1-9]|1[0-2])\/([0-9]{2})$", ErrorMessage = "Invalid expiry date format (MM/YY)")]
    public string ExpiryDate { get; set; } = string.Empty;
    
    [Required(ErrorMessage = "CVV is required")]
    [RegularExpression(@"^[0-9]{3,4}$", ErrorMessage = "CVV must be 3 or 4 digits")]
    public string Cvv { get; set; } = string.Empty;
    
    [Required(ErrorMessage = "Cardholder name is required")]
    [StringLength(100, MinimumLength = 2, ErrorMessage = "Cardholder name must be between 2 and 100 characters")]
    [RegularExpression(@"^[A-Za-z\s\-\.]{2,100}$", ErrorMessage = "Invalid cardholder name format")]
    public string CardholderName { get; set; } = string.Empty;
    
    [Required(ErrorMessage = "Email is required")]
    [EmailAddress(ErrorMessage = "Invalid email format")]
    [StringLength(100, MinimumLength = 5)]
    public string Email { get; set; } = string.Empty;
    
    [Phone(ErrorMessage = "Invalid phone number format")]
    [StringLength(20, MinimumLength = 10)]
    public string? Phone { get; set; }
    
    public bool SaveCard { get; set; } = false;
    
    [Required(ErrorMessage = "You must agree to the terms and conditions")]
    [Range(typeof(bool), "true", "true", ErrorMessage = "You must agree to the terms and conditions")]
    public bool TermsAgreement { get; set; } = false;
    
    [Required(ErrorMessage = "Security token is required")]
    public string CsrfToken { get; set; } = string.Empty;
    
    /// <summary>
    /// Get masked card number for logging and display
    /// </summary>
    [JsonIgnore]
    public string MaskedCardNumber
    {
        get
        {
            if (string.IsNullOrWhiteSpace(CardNumber))
                return string.Empty;
            
            var digits = CardNumber.Replace(" ", "").Replace("-", "");
            if (digits.Length < 8)
                return new string('*', digits.Length);
            
            return digits.Substring(0, 4) + 
                   new string('*', digits.Length - 8) + 
                   digits.Substring(digits.Length - 4);
        }
    }
    
    /// <summary>
    /// Get sanitized submission data for logging
    /// </summary>
    public PaymentFormSubmissionModel GetSanitizedForLogging()
    {
        return new PaymentFormSubmissionModel
        {
            PaymentId = this.PaymentId,
            CardNumber = this.MaskedCardNumber,
            ExpiryDate = this.ExpiryDate,
            Cvv = new string('*', this.Cvv?.Length ?? 0),
            CardholderName = this.CardholderName,
            Email = this.Email,
            Phone = this.Phone,
            SaveCard = this.SaveCard,
            TermsAgreement = this.TermsAgreement,
            CsrfToken = "***REDACTED***"
        };
    }
}

/// <summary>
/// Payment form validation result
/// Contains validation status and error details
/// </summary>
public class PaymentFormValidationResult
{
    public bool IsValid { get; set; } = true;
    public List<ValidationError> Errors { get; set; } = new();
    public Dictionary<string, List<string>> FieldErrors { get; set; } = new();
    
    public void AddError(string field, string message)
    {
        IsValid = false;
        Errors.Add(new ValidationError { Field = field, Message = message });
        
        if (!FieldErrors.ContainsKey(field))
            FieldErrors[field] = new List<string>();
        
        FieldErrors[field].Add(message);
    }
    
    public void AddGeneralError(string message)
    {
        IsValid = false;
        Errors.Add(new ValidationError { Field = "general", Message = message });
    }
}

/// <summary>
/// Individual validation error
/// </summary>
public class ValidationError
{
    public string Field { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string? ErrorCode { get; set; }
    public Dictionary<string, object>? AdditionalInfo { get; set; }
}

/// <summary>
/// Payment processing result for form submissions
/// </summary>
public class PaymentFormProcessingResult
{
    public bool Success { get; set; }
    public string? TransactionId { get; set; }
    public string? PaymentId { get; set; }
    public PaymentStatus Status { get; set; }
    public string? ErrorMessage { get; set; }
    public string? ErrorCode { get; set; }
    public List<string> Warnings { get; set; } = new();
    public Dictionary<string, object>? AdditionalData { get; set; }
    
    public DateTime ProcessedAt { get; set; } = DateTime.UtcNow;
    public TimeSpan ProcessingDuration { get; set; }
    
    /// <summary>
    /// Create a successful result
    /// </summary>
    public static PaymentFormProcessingResult CreateSuccess(string paymentId, string? transactionId = null)
    {
        return new PaymentFormProcessingResult
        {
            Success = true,
            PaymentId = paymentId,
            TransactionId = transactionId,
            Status = PaymentStatus.AUTHORIZED
        };
    }
    
    /// <summary>
    /// Create a failed result
    /// </summary>
    public static PaymentFormProcessingResult Failure(string paymentId, string errorMessage, string? errorCode = null)
    {
        return new PaymentFormProcessingResult
        {
            Success = false,
            PaymentId = paymentId,
            ErrorMessage = errorMessage,
            ErrorCode = errorCode,
            Status = PaymentStatus.FAILED
        };
    }
}

/// <summary>
/// Payment result page data
/// Used for rendering success/failure pages
/// </summary>
public class PaymentResultViewModel
{
    [Required]
    public string PaymentId { get; set; } = string.Empty;
    
    public string? OrderId { get; set; }
    
    [Required]
    public bool Success { get; set; }
    
    public string? Message { get; set; }
    
    [Required]
    public decimal Amount { get; set; }
    
    [Required]
    public string Currency { get; set; } = string.Empty;
    
    [Required]
    public PaymentStatus Status { get; set; }
    
    public DateTime ProcessedAt { get; set; } = DateTime.UtcNow;
    
    public string? MerchantName { get; set; }
    
    [Url]
    public string? SuccessUrl { get; set; }
    
    [Url]
    public string? FailUrl { get; set; }
    
    [Url]
    public string? ReturnUrl => Success ? SuccessUrl : FailUrl;
    
    public string? MaskedCardNumber { get; set; }
    
    public List<string> Warnings { get; set; } = new();
    
    public Dictionary<string, object>? AdditionalData { get; set; }
    
    /// <summary>
    /// Get display-friendly status text
    /// </summary>
    public string StatusDisplayText => Status switch
    {
        PaymentStatus.AUTHORIZED => "Authorized",
        PaymentStatus.CONFIRMED => "Confirmed",
        PaymentStatus.FAILED => "Failed",
        PaymentStatus.CANCELLED => "Cancelled",
        PaymentStatus.REFUNDED => "Refunded",
        _ => Status.ToString()
    };
    
    /// <summary>
    /// Get CSS class for status styling
    /// </summary>
    public string StatusCssClass => Success ? "status-success" : "status-error";
    
    /// <summary>
    /// Get icon for result display
    /// </summary>
    public string ResultIcon => Success ? "✓" : "✗";
}

/// <summary>
/// CSRF token validation model
/// </summary>
public class CsrfTokenModel
{
    [Required]
    public string PaymentId { get; set; } = string.Empty;
    
    [Required]
    public string Token { get; set; } = string.Empty;
    
    public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;
    
    public DateTime ExpiresAt { get; set; } = DateTime.UtcNow.AddMinutes(30);
    
    public bool IsExpired => DateTime.UtcNow > ExpiresAt;
    
    public string SessionId { get; set; } = string.Empty;
    
    public string? ClientIpAddress { get; set; }
}

/// <summary>
/// Payment form configuration model
/// Contains settings for form behavior
/// </summary>
public class PaymentFormConfiguration
{
    public bool EnableCardSaving { get; set; } = true;
    public bool RequirePhoneNumber { get; set; } = false;
    public bool EnableAddressCollection { get; set; } = false;
    public int CsrfTokenExpiryMinutes { get; set; } = 30;
    public int FormSessionTimeoutMinutes { get; set; } = 15;
    public List<string> SupportedCardTypes { get; set; } = new()
    {
        "visa", "mastercard", "amex", "discover", "jcb", "diners", "unionpay", "mir"
    };
    public List<string> SupportedLanguages { get; set; } = new() { "en", "ru" };
    public string DefaultLanguage { get; set; } = "en";
    public bool EnableRecaptcha { get; set; } = false;
    public string? RecaptchaSiteKey { get; set; }
    public bool EnableAuditLogging { get; set; } = true;
    public bool EnableMetrics { get; set; } = true;
}

/// <summary>
/// Payment status enumeration for form processing
/// </summary>
public enum PaymentStatus
{
    NEW = 0,
    AUTHORIZED = 1,
    CONFIRMED = 2,
    FAILED = 3,
    CANCELLED = 4,
    REFUNDED = 5,
    EXPIRED = 6
}