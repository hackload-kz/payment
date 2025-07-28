using System.ComponentModel.DataAnnotations;

namespace Payment.Gateway.Models;

public class Merchant
{
    [Key]
    [MaxLength(20)]
    public string TerminalKey { get; set; } = string.Empty;

    [Required]
    [MaxLength(50)]
    public string Password { get; set; } = string.Empty;

    [Required]
    public bool IsActive { get; set; } = true;

    public DateTime CreatedDate { get; set; } = DateTime.UtcNow;
    public DateTime? LastLoginDate { get; set; }
}