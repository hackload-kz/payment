using PaymentGateway.Core.Interfaces;

namespace PaymentGateway.Core.Entities;

public class AuditLog : IAuditableEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string EntityName { get; set; } = string.Empty;
    public string EntityId { get; set; } = string.Empty;
    public string Operation { get; set; } = string.Empty; // CREATE, UPDATE, DELETE
    public string? OldValues { get; set; } // JSON
    public string? NewValues { get; set; } // JSON
    public string? Changes { get; set; } // JSON of changed fields
    public string? UserId { get; set; }
    public string? UserName { get; set; }
    public string? IPAddress { get; set; }
    public string? UserAgent { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public string? UpdatedBy { get; set; }
}