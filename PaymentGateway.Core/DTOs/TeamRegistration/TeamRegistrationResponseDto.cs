using PaymentGateway.Core.DTOs.Common;

namespace PaymentGateway.Core.DTOs.TeamRegistration;

/// <summary>
/// Response DTO for team registration
/// </summary>
public class TeamRegistrationResponseDto
{
    /// <summary>
    /// Indicates if the registration was successful
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Error code if registration failed
    /// </summary>
    public string? ErrorCode { get; set; }

    /// <summary>
    /// Human-readable message
    /// </summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// The registered team slug
    /// </summary>
    public string? TeamSlug { get; set; }

    /// <summary>
    /// Generated team ID
    /// </summary>
    public Guid? TeamId { get; set; }

    /// <summary>
    /// Password hash (for confirmation - first 8 characters)
    /// </summary>
    public string? PasswordHashPreview { get; set; }

    /// <summary>
    /// Full password hash for token generation (development/testing only)
    /// WARNING: This should not be exposed in production
    /// </summary>
    public string? PasswordHashFull { get; set; }

    /// <summary>
    /// Team creation timestamp
    /// </summary>
    public DateTime? CreatedAt { get; set; }

    /// <summary>
    /// Team status (PENDING, ACTIVE, SUSPENDED)
    /// </summary>
    public string? Status { get; set; }

    /// <summary>
    /// API endpoint for this team's payments
    /// </summary>
    public string? ApiEndpoint { get; set; }

    /// <summary>
    /// Additional details about the registration
    /// </summary>
    public TeamRegistrationDetailsDto? Details { get; set; }
}

/// <summary>
/// Additional team registration details
/// </summary>
public class TeamRegistrationDetailsDto
{
    /// <summary>
    /// Team display name
    /// </summary>
    public string? TeamName { get; set; }

    /// <summary>
    /// Contact email
    /// </summary>
    public string? Email { get; set; }

    /// <summary>
    /// Contact phone
    /// </summary>
    public string? Phone { get; set; }

    /// <summary>
    /// Configured success URL
    /// </summary>
    public string? SuccessURL { get; set; }

    /// <summary>
    /// Configured fail URL
    /// </summary>
    public string? FailURL { get; set; }

    /// <summary>
    /// Configured notification URL
    /// </summary>
    public string? NotificationURL { get; set; }

    /// <summary>
    /// Supported currencies
    /// </summary>
    public string[]? SupportedCurrencies { get; set; }

    /// <summary>
    /// Next steps for team activation
    /// </summary>
    public string[]? NextSteps { get; set; }
}