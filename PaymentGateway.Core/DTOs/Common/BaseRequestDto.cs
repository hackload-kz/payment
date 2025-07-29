using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace PaymentGateway.Core.DTOs.Common;

/// <summary>
/// Base class for all API request DTOs
/// </summary>
public abstract class BaseRequestDto
{
    /// <summary>
    /// Team identifier issued to merchant
    /// </summary>
    [Required(ErrorMessage = "TeamSlug is required")]
    [StringLength(50, ErrorMessage = "TeamSlug cannot exceed 50 characters")]
    [JsonPropertyName("teamSlug")]
    public string TeamSlug { get; set; } = string.Empty;

    /// <summary>
    /// Request signature for security validation
    /// </summary>
    [Required(ErrorMessage = "Token is required")]
    [StringLength(256, ErrorMessage = "Token cannot exceed 256 characters")]
    [JsonPropertyName("token")]
    public string Token { get; set; } = string.Empty;

    /// <summary>
    /// API version for versioning support
    /// </summary>
    [JsonPropertyName("version")]
    public string Version { get; set; } = "1.0";

    /// <summary>
    /// Request timestamp
    /// </summary>
    [JsonPropertyName("timestamp")]
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Correlation ID for request tracking
    /// </summary>
    [JsonPropertyName("correlationId")]
    public string? CorrelationId { get; set; }
}

/// <summary>
/// Base class for all API response DTOs
/// </summary>
public class BaseResponseDto
{
    /// <summary>
    /// Indicates if the operation was successful
    /// </summary>
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    /// <summary>
    /// Error code if operation failed
    /// </summary>
    [JsonPropertyName("errorCode")]
    public string? ErrorCode { get; set; }

    /// <summary>
    /// Human-readable error message
    /// </summary>
    [JsonPropertyName("message")]
    public string? Message { get; set; }

    /// <summary>
    /// Detailed error information
    /// </summary>
    [JsonPropertyName("details")]
    public object? Details { get; set; }

    /// <summary>
    /// Response timestamp
    /// </summary>
    [JsonPropertyName("timestamp")]
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Correlation ID from request
    /// </summary>
    [JsonPropertyName("correlationId")]
    public string? CorrelationId { get; set; }

    /// <summary>
    /// API version
    /// </summary>
    [JsonPropertyName("version")]
    public string Version { get; set; } = "1.0";
}

/// <summary>
/// Error detail information
/// </summary>
public class ErrorDetailDto
{
    /// <summary>
    /// Field name that caused the error
    /// </summary>
    [JsonPropertyName("field")]
    public string? Field { get; set; }

    /// <summary>
    /// Error code for this specific field
    /// </summary>
    [JsonPropertyName("code")]
    public string? Code { get; set; }

    /// <summary>
    /// Error message for this field
    /// </summary>
    [JsonPropertyName("message")]
    public string? Message { get; set; }

    /// <summary>
    /// Additional context information
    /// </summary>
    [JsonPropertyName("context")]
    public Dictionary<string, object>? Context { get; set; }
}