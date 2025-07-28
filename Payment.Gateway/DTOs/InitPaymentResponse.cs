namespace Payment.Gateway.DTOs;

public class InitPaymentResponse
{
    // Required Fields
    public string TerminalKey { get; set; } = string.Empty;
    public long Amount { get; set; }
    public string OrderId { get; set; } = string.Empty;
    public bool Success { get; set; }
    public string Status { get; set; } = string.Empty;
    public string PaymentId { get; set; } = string.Empty;
    public string ErrorCode { get; set; } = "0";

    // Optional Fields
    public string? PaymentURL { get; set; }
    public string? Message { get; set; }
    public string? Details { get; set; }
}