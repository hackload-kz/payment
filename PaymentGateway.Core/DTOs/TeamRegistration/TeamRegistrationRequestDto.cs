using System.ComponentModel.DataAnnotations;

namespace PaymentGateway.Core.DTOs.TeamRegistration;

/// <summary>
/// Request DTO for team registration (does not inherit from BaseRequestDto 
/// as it doesn't require TeamSlug/Token authentication)
/// </summary>
public class TeamRegistrationRequestDto
{
    /// <summary>
    /// Unique team slug identifier (3-50 characters, alphanumeric, hyphens, underscores)
    /// </summary>
    [Required(ErrorMessage = "TeamSlug is required")]
    [StringLength(50, MinimumLength = 3, ErrorMessage = "TeamSlug must be between 3 and 50 characters")]
    [RegularExpression(@"^[a-zA-Z0-9\-_]+$", ErrorMessage = "TeamSlug can only contain alphanumeric characters, hyphens, and underscores")]
    public string TeamSlug { get; set; } = string.Empty;

    /// <summary>
    /// Plain text password (will be hashed server-side)
    /// </summary>
    [Required(ErrorMessage = "Password is required")]
    [StringLength(100, MinimumLength = 8, ErrorMessage = "Password must be between 8 and 100 characters")]
    public string Password { get; set; } = string.Empty;

    /// <summary>
    /// Team display name
    /// </summary>
    [Required(ErrorMessage = "TeamName is required")]
    [StringLength(100, MinimumLength = 2, ErrorMessage = "TeamName must be between 2 and 100 characters")]
    public string TeamName { get; set; } = string.Empty;

    /// <summary>
    /// Contact email address
    /// </summary>
    [Required(ErrorMessage = "Email is required")]
    [EmailAddress(ErrorMessage = "Invalid email format")]
    [StringLength(255, ErrorMessage = "Email cannot exceed 255 characters")]
    public string Email { get; set; } = string.Empty;

    /// <summary>
    /// Contact phone number (optional)
    /// </summary>
    [Phone(ErrorMessage = "Invalid phone number format")]
    [StringLength(20, ErrorMessage = "Phone cannot exceed 20 characters")]
    public string? Phone { get; set; }

    /// <summary>
    /// Success URL for redirects after successful payments
    /// </summary>
    [Required(ErrorMessage = "SuccessURL is required")]
    [Url(ErrorMessage = "SuccessURL must be a valid URL")]
    [StringLength(500, ErrorMessage = "SuccessURL cannot exceed 500 characters")]
    public string SuccessURL { get; set; } = string.Empty;

    /// <summary>
    /// Fail URL for redirects after failed payments
    /// </summary>
    [Required(ErrorMessage = "FailURL is required")]
    [Url(ErrorMessage = "FailURL must be a valid URL")]
    [StringLength(500, ErrorMessage = "FailURL cannot exceed 500 characters")]
    public string FailURL { get; set; } = string.Empty;

    /// <summary>
    /// Webhook URL for payment notifications
    /// </summary>
    [Url(ErrorMessage = "NotificationURL must be a valid URL")]
    [StringLength(500, ErrorMessage = "NotificationURL cannot exceed 500 characters")]
    public string? NotificationURL { get; set; }

    /// <summary>
    /// Supported currencies (comma-separated, e.g., "RUB,USD,EUR")
    /// </summary>
    [Required(ErrorMessage = "SupportedCurrencies is required")]
    public string SupportedCurrencies { get; set; } = "RUB";

    /// <summary>
    /// Business information (optional JSON metadata)
    /// </summary>
    public Dictionary<string, string>? BusinessInfo { get; set; }

    /// <summary>
    /// Accept terms of service
    /// </summary>
    [Range(typeof(bool), "true", "true", ErrorMessage = "You must accept the terms of service")]
    public bool AcceptTerms { get; set; } = false;
}