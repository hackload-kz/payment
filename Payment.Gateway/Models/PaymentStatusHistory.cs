using System.ComponentModel.DataAnnotations;

namespace Payment.Gateway.Models;

public class PaymentStatusHistory
{
    [Key]
    public int Id { get; set; }

    [Required]
    [MaxLength(20)]
    public string PaymentId { get; set; } = string.Empty;

    [Required]
    public PaymentStatus Status { get; set; }

    [Required]
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    [MaxLength(10)]
    public string? ErrorCode { get; set; }

    [MaxLength(500)]
    public string? Message { get; set; }
}