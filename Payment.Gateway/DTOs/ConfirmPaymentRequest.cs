using System.ComponentModel.DataAnnotations;

namespace Payment.Gateway.DTOs;

public class ConfirmPaymentRequest
{
    // Required Parameters
    [Required]
    [MaxLength(20)]
    public string TerminalKey { get; set; } = string.Empty;

    [Required]
    [MaxLength(20)]
    public string PaymentId { get; set; } = string.Empty;

    [Required]
    public string Token { get; set; } = string.Empty;

    // Optional Parameters
    public string? IP { get; set; }

    [Range(1, long.MaxValue, ErrorMessage = "Amount must be greater than 0")]
    public long? Amount { get; set; }

    // Fiscal Compliance
    public object? Receipt { get; set; }

    // Marketplace Support
    public object[]? Shops { get; set; }

    // Payment Method Routing
    public string? Route { get; set; }
    public string? Source { get; set; }
}