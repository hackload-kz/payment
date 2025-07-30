using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PaymentGateway.Core.Entities;

namespace PaymentGateway.Core.Data.Configurations;

public class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> builder)
    {
        builder.ToTable("Payments");
        
        // Primary key
        builder.HasKey(p => p.Id);
        
        // Indexes for high-performance queries
        builder.HasIndex(p => p.PaymentId)
            .IsUnique()
            .HasDatabaseName("IX_Payments_PaymentId");
            
        builder.HasIndex(p => p.OrderId)
            .HasDatabaseName("IX_Payments_OrderId");
            
        builder.HasIndex(p => new { p.TeamId, p.OrderId })
            .IsUnique()
            .HasDatabaseName("IX_Payments_TeamId_OrderId");
            
        builder.HasIndex(p => p.Status)
            .HasDatabaseName("IX_Payments_Status");
            
        builder.HasIndex(p => p.CreatedAt)
            .HasDatabaseName("IX_Payments_CreatedAt");
            
        builder.HasIndex(p => new { p.Status, p.CreatedAt })
            .HasDatabaseName("IX_Payments_Status_CreatedAt");
            
        // Additional performance indexes for common query patterns
        builder.HasIndex(p => new { p.TeamId, p.Status })
            .HasDatabaseName("IX_Payments_TeamId_Status");
            
        builder.HasIndex(p => new { p.TeamId, p.CreatedAt })
            .HasDatabaseName("IX_Payments_TeamId_CreatedAt");
            
        builder.HasIndex(p => new { p.TeamId, p.Status, p.CreatedAt })
            .HasDatabaseName("IX_Payments_TeamId_Status_CreatedAt");
        
        // Properties configuration
        builder.Property(p => p.PaymentId)
            .IsRequired()
            .HasMaxLength(50);
            
        builder.Property(p => p.OrderId)
            .IsRequired()
            .HasMaxLength(50);
            
        builder.Property(p => p.Amount)
            .HasPrecision(18, 2)
            .IsRequired();
            
        builder.Property(p => p.Currency)
            .IsRequired()
            .HasMaxLength(3)
            .IsFixedLength();
            
        builder.Property(p => p.Description)
            .IsRequired()
            .HasMaxLength(250);
            
        builder.Property(p => p.Status)
            .IsRequired()
            .HasConversion<int>();
            
        builder.Property(p => p.RefundedAmount)
            .HasPrecision(18, 2)
            .HasDefaultValue(0);
            
        builder.Property(p => p.RefundCount)
            .HasDefaultValue(0);
            
        // Audit properties
        builder.Property(p => p.RowVersion)
            .IsRowVersion();
            
        // Check constraints using modern syntax
        builder.ToTable(t =>
        {
            t.HasCheckConstraint("CK_Payments_Amount", "Amount > 0");
            t.HasCheckConstraint("CK_Payments_RefundedAmount", "RefundedAmount >= 0");
            t.HasCheckConstraint("CK_Payments_RefundedAmount_LessOrEqual_Amount", "RefundedAmount <= Amount");
            t.HasCheckConstraint("CK_Payments_RefundCount", "RefundCount >= 0");
            t.HasCheckConstraint("CK_Payments_AuthorizationAttempts", "AuthorizationAttempts >= 0");
        });
        
        // Foreign key relationships
        builder.HasOne(p => p.Team)
            .WithMany(t => t.Payments)
            .HasForeignKey(p => p.TeamId)
            .OnDelete(DeleteBehavior.Restrict);
            
        builder.HasOne(p => p.Customer)
            .WithMany(c => c.Payments)
            .HasForeignKey(p => p.CustomerId)
            .OnDelete(DeleteBehavior.SetNull);
            
        // JSON column for metadata
        builder.Property(p => p.Metadata)
            .HasConversion(
                v => System.Text.Json.JsonSerializer.Serialize(v, (System.Text.Json.JsonSerializerOptions)null),
                v => System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(v, (System.Text.Json.JsonSerializerOptions)null) ?? new Dictionary<string, string>()
            );
            
        // Soft delete filter
        builder.HasQueryFilter(p => !p.IsDeleted);
    }
}