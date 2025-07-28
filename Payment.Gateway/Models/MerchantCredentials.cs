using System.ComponentModel.DataAnnotations;

namespace Payment.Gateway.Models;

public class MerchantCredentials
{
    [Required]
    [MaxLength(20)]
    public string TerminalKey { get; set; } = string.Empty;

    [Required]
    [MaxLength(50)]
    public string Password { get; set; } = string.Empty;
}