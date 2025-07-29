using PaymentGateway.Core.Interfaces;

namespace PaymentGateway.Core.Entities;

public class Team : BaseEntity
{
    public string TeamSlug { get; set; } = string.Empty;
    public string TeamName { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public string? NotificationUrl { get; set; }
    public string? SuccessUrl { get; set; }
    public string? FailUrl { get; set; }
    
    // Navigation properties
    public ICollection<Payment> Payments { get; set; } = new List<Payment>();
}