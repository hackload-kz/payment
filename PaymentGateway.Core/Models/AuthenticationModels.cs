namespace PaymentGateway.Core.Models;

public class AuthenticationResult
{
    public bool IsAuthenticated { get; set; }
    public bool IsSuccessful => IsAuthenticated;
    public string? TeamSlug { get; set; }
    public string? FailureReason { get; set; }
    public TeamInfo? TeamInfo { get; set; }
}

public class TeamInfo
{
    public string TeamSlug { get; set; } = string.Empty;
    public string TeamId { get; set; } = string.Empty;
    public string TeamName { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public Dictionary<string, string> Properties { get; set; } = new();
}