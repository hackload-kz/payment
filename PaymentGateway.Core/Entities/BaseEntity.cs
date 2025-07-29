using PaymentGateway.Core.Interfaces;

namespace PaymentGateway.Core.Entities;

public abstract class BaseEntity : IAuditableEntity, IVersionedEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public string? UpdatedBy { get; set; }
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();
}