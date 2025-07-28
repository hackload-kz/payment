using System.ComponentModel.DataAnnotations;

namespace Payment.Gateway.DTOs;

public class InitPaymentRequest
{
    // Core Payment Parameters
    [Required]
    [MaxLength(20)]
    public string TerminalKey { get; set; } = string.Empty;

    [Required]
    [Range(1000, long.MaxValue, ErrorMessage = "Amount must be at least 1000 kopecks (10 RUB)")]
    public long Amount { get; set; }

    [Required]
    [MaxLength(36)]
    public string OrderId { get; set; } = string.Empty;

    [Required]
    public string Token { get; set; } = string.Empty;

    // Optional Payment Configuration
    [MaxLength(1)]
    public string? PayType { get; set; }

    [MaxLength(140)]
    public string? Description { get; set; }

    // Customer Management
    [MaxLength(36)]
    public string? CustomerKey { get; set; }

    [MaxLength(1)]
    public string? Recurrent { get; set; }

    // Localization
    [MaxLength(2)]
    public string? Language { get; set; }

    // URL Configuration
    public string? NotificationURL { get; set; }
    public string? SuccessURL { get; set; }
    public string? FailURL { get; set; }

    // Session Management
    public DateTime? RedirectDueDate { get; set; }

    // Complex Parameters
    public Dictionary<string, string>? DATA { get; set; }
    public object? Receipt { get; set; }
    public object[]? Shops { get; set; }
    public string? Descriptor { get; set; }
}