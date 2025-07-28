using System.ComponentModel.DataAnnotations;

namespace Payment.Gateway.Models;

public class PaymentEntity
{
    [Key]
    [MaxLength(20)]
    public string PaymentId { get; set; } = string.Empty;

    [Required]
    [MaxLength(36)]
    public string OrderId { get; set; } = string.Empty;

    [Required]
    [MaxLength(20)]
    public string TerminalKey { get; set; } = string.Empty;

    [Required]
    public long Amount { get; set; }

    [Required]
    public PaymentStatus CurrentStatus { get; set; }

    [MaxLength(500)]
    public string? Description { get; set; }

    [MaxLength(36)]
    public string? CustomerKey { get; set; }

    [MaxLength(1)]
    public string? PayType { get; set; }

    [MaxLength(2)]
    public string? Language { get; set; }

    public string? NotificationURL { get; set; }
    public string? SuccessURL { get; set; }
    public string? FailURL { get; set; }

    public DateTime CreatedDate { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedDate { get; set; } = DateTime.UtcNow;
    public DateTime? ExpirationDate { get; set; }

    public int AttemptCount { get; set; } = 0;
    public int MaxAttempts { get; set; } = 3;

    public bool Recurrent { get; set; } = false;

    public string? PaymentURL { get; set; }
    public string? ErrorCode { get; set; }
    public string? Message { get; set; }

    // Serialized JSON data
    public string? DataJson { get; set; }
    public string? ReceiptJson { get; set; }
    public string? ShopsJson { get; set; }
}