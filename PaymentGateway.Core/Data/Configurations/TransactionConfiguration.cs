using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PaymentGateway.Core.Entities;

namespace PaymentGateway.Core.Data.Configurations;

public class TransactionConfiguration : IEntityTypeConfiguration<Transaction>
{
    public void Configure(EntityTypeBuilder<Transaction> builder)
    {
        builder.ToTable("Transactions");
        
        // Primary key
        builder.HasKey(t => t.Id);
        
        // Indexes for high-performance queries
        builder.HasIndex(t => t.TransactionId)
            .IsUnique()
            .HasDatabaseName("IX_Transactions_TransactionId");
            
        builder.HasIndex(t => t.PaymentId)
            .HasDatabaseName("IX_Transactions_PaymentId");
            
        builder.HasIndex(t => new { t.Type, t.Status })
            .HasDatabaseName("IX_Transactions_Type_Status");
            
        builder.HasIndex(t => t.CreatedAt)
            .HasDatabaseName("IX_Transactions_CreatedAt");
            
        builder.HasIndex(t => t.ProcessingStartedAt)
            .HasDatabaseName("IX_Transactions_ProcessingStartedAt");
            
        builder.HasIndex(t => new { t.Status, t.ProcessingStartedAt })
            .HasDatabaseName("IX_Transactions_Status_ProcessingStartedAt");
        
        // Properties configuration
        builder.Property(t => t.TransactionId)
            .IsRequired()
            .HasMaxLength(50);
            
        builder.Property(t => t.PaymentId)
            .IsRequired()
            .HasMaxLength(50);
            
        builder.Property(t => t.Amount)
            .HasPrecision(18, 2)
            .IsRequired();
            
        builder.Property(t => t.Currency)
            .IsRequired()
            .HasMaxLength(3)
            .IsFixedLength();
            
        builder.Property(t => t.Type)
            .IsRequired()
            .HasConversion<int>();
            
        builder.Property(t => t.Status)
            .IsRequired()
            .HasConversion<int>();
            
        builder.Property(t => t.TotalFees)
            .HasPrecision(18, 2)
            .HasDefaultValue(0);
            
        builder.Property(t => t.ProcessingFee)
            .HasPrecision(18, 2)
            .HasDefaultValue(0);
            
        builder.Property(t => t.AcquirerFee)
            .HasPrecision(18, 2)
            .HasDefaultValue(0);
            
        builder.Property(t => t.FraudScore)
            .HasDefaultValue(0);
            
        // Audit properties
        builder.Property(t => t.RowVersion)
            .IsRowVersion();
            
        // Check constraints
        builder.HasCheckConstraint("CK_Transactions_Amount", "Amount > 0");
        builder.HasCheckConstraint("CK_Transactions_TotalFees", "TotalFees >= 0");
        builder.HasCheckConstraint("CK_Transactions_ProcessingFee", "ProcessingFee >= 0");
        builder.HasCheckConstraint("CK_Transactions_AcquirerFee", "AcquirerFee >= 0");
        builder.HasCheckConstraint("CK_Transactions_FraudScore", "FraudScore >= 0 AND FraudScore <= 100");
        builder.HasCheckConstraint("CK_Transactions_RetryAttempts", "RetryAttempts >= 0");
        
        // Foreign key relationships
        builder.HasOne(t => t.Payment)
            .WithMany(p => p.Transactions)
            .HasForeignKey(t => t.PaymentId)
            .HasPrincipalKey(p => p.PaymentId)
            .OnDelete(DeleteBehavior.Cascade);
            
        // JSON column for additional data
        builder.Property(t => t.AdditionalData)
            .HasConversion(
                v => System.Text.Json.JsonSerializer.Serialize(v, (System.Text.Json.JsonSerializerOptions)null),
                v => System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(v, (System.Text.Json.JsonSerializerOptions)null) ?? new Dictionary<string, string>()
            );
            
        // Soft delete filter
        builder.HasQueryFilter(t => !t.IsDeleted);
    }
}