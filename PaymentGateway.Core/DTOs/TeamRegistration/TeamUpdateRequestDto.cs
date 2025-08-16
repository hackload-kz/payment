using System.ComponentModel.DataAnnotations;

namespace PaymentGateway.Core.DTOs.TeamRegistration;

/// <summary>
/// Request DTO for updating team information and limits
/// </summary>
public class TeamUpdateRequestDto
{
    /// <summary>
    /// Team display name
    /// </summary>
    [StringLength(100, MinimumLength = 2, ErrorMessage = "TeamName must be between 2 and 100 characters")]
    public string? TeamName { get; set; }

    /// <summary>
    /// Contact email address
    /// </summary>
    [EmailAddress(ErrorMessage = "Invalid email format")]
    [StringLength(255, ErrorMessage = "Email cannot exceed 255 characters")]
    public string? Email { get; set; }

    /// <summary>
    /// Contact phone number
    /// </summary>
    [Phone(ErrorMessage = "Invalid phone number format")]
    [StringLength(20, ErrorMessage = "Phone cannot exceed 20 characters")]
    public string? Phone { get; set; }

    /// <summary>
    /// Success URL for redirects after successful payments
    /// </summary>
    [Url(ErrorMessage = "SuccessURL must be a valid URL")]
    [StringLength(500, ErrorMessage = "SuccessURL cannot exceed 500 characters")]
    public string? SuccessURL { get; set; }

    /// <summary>
    /// Fail URL for redirects after failed payments
    /// </summary>
    [Url(ErrorMessage = "FailURL must be a valid URL")]
    [StringLength(500, ErrorMessage = "FailURL cannot exceed 500 characters")]
    public string? FailURL { get; set; }

    /// <summary>
    /// Webhook URL for payment notifications
    /// </summary>
    [Url(ErrorMessage = "NotificationURL must be a valid URL")]
    [StringLength(500, ErrorMessage = "NotificationURL cannot exceed 500 characters")]
    public string? NotificationURL { get; set; }

    /// <summary>
    /// Supported currencies (comma-separated, e.g., "RUB,USD,EUR")
    /// </summary>
    public string? SupportedCurrencies { get; set; }

    /// <summary>
    /// Business information (optional JSON metadata)
    /// </summary>
    public Dictionary<string, string>? BusinessInfo { get; set; }

    /// <summary>
    /// Minimum payment amount allowed
    /// </summary>
    [Range(0, double.MaxValue, ErrorMessage = "MinPaymentAmount must be greater than or equal to 0")]
    public decimal? MinPaymentAmount { get; set; }

    /// <summary>
    /// Maximum payment amount allowed
    /// </summary>
    [Range(0, double.MaxValue, ErrorMessage = "MaxPaymentAmount must be greater than or equal to 0")]
    public decimal? MaxPaymentAmount { get; set; }

    /// <summary>
    /// Daily payment limit (total amount per day)
    /// </summary>
    [Range(0, 999999999999999999.99, ErrorMessage = "DailyPaymentLimit must be between 0 and 999,999,999,999,999,999.99")]
    public decimal? DailyPaymentLimit { get; set; }

    /// <summary>
    /// Monthly payment limit (total amount per month)
    /// </summary>
    [Range(0, double.MaxValue, ErrorMessage = "MonthlyPaymentLimit must be greater than or equal to 0")]
    public decimal? MonthlyPaymentLimit { get; set; }

    /// <summary>
    /// Daily transaction count limit
    /// </summary>
    [Range(0, int.MaxValue, ErrorMessage = "DailyTransactionLimit must be greater than or equal to 0")]
    public int? DailyTransactionLimit { get; set; }

}