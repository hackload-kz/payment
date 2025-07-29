namespace PaymentGateway.Core.Interfaces;

public interface IAuditableEntity
{
    DateTime CreatedAt { get; set; }
    DateTime UpdatedAt { get; set; }
    string? CreatedBy { get; set; }
    string? UpdatedBy { get; set; }
}

public interface IVersionedEntity
{
    byte[] RowVersion { get; set; }
}