using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PaymentGateway.Core.Entities;

namespace PaymentGateway.Core.Data.Configurations;

public class CustomerConfiguration : IEntityTypeConfiguration<Customer>
{
    public void Configure(EntityTypeBuilder<Customer> builder)
    {
        builder.ToTable("Customers");
        
        // Primary key
        builder.HasKey(c => c.Id);
        
        // Indexes for high-performance queries
        builder.HasIndex(c => c.CustomerId)
            .IsUnique()
            .HasDatabaseName("IX_Customers_CustomerId");
            
        builder.HasIndex(c => c.Email)
            .HasDatabaseName("IX_Customers_Email");
            
        builder.HasIndex(c => new { c.TeamId, c.Email })
            .HasDatabaseName("IX_Customers_TeamId_Email");
            
        builder.HasIndex(c => c.RiskLevel)
            .HasDatabaseName("IX_Customers_RiskLevel");
            
        builder.HasIndex(c => c.CreatedAt)
            .HasDatabaseName("IX_Customers_CreatedAt");
            
        builder.HasIndex(c => new { c.IsBlacklisted, c.CreatedAt })
            .HasDatabaseName("IX_Customers_IsBlacklisted_CreatedAt");
        
        // Properties configuration
        builder.Property(c => c.CustomerId)
            .IsRequired()
            .HasMaxLength(100);
            
        builder.Property(c => c.Email)
            .HasMaxLength(254);
            
        builder.Property(c => c.FirstName)
            .HasMaxLength(100);
            
        builder.Property(c => c.LastName)
            .HasMaxLength(100);
            
        builder.Property(c => c.Phone)
            .HasMaxLength(20);
            
        builder.Property(c => c.Address)
            .HasMaxLength(500);
            
        builder.Property(c => c.City)
            .HasMaxLength(100);
            
        builder.Property(c => c.Country)
            .HasMaxLength(100);
            
        builder.Property(c => c.PostalCode)
            .HasMaxLength(20);
            
        builder.Property(c => c.PreferredLanguage)
            .IsRequired()
            .HasMaxLength(10)
            .HasDefaultValue("en");
            
        builder.Property(c => c.PreferredCurrency)
            .IsRequired()
            .HasMaxLength(3)
            .IsFixedLength()
            .HasDefaultValue("RUB");
            
        builder.Property(c => c.RiskScore)
            .HasDefaultValue(0);
            
        builder.Property(c => c.RiskLevel)
            .IsRequired()
            .HasMaxLength(20)
            .HasDefaultValue("LOW");
            
        builder.Property(c => c.IsBlacklisted)
            .HasDefaultValue(false);
            
        builder.Property(c => c.TotalPaymentCount)
            .HasDefaultValue(0);
            
        builder.Property(c => c.TotalPaymentAmount)
            .HasPrecision(18, 2)
            .HasDefaultValue(0);
            
        builder.Property(c => c.IsKycVerified)
            .HasDefaultValue(false);
            
        // Audit properties
        builder.Property(c => c.RowVersion)
            .IsRowVersion();
            
        // Check constraints using modern syntax
        builder.ToTable(t =>
        {
            t.HasCheckConstraint("CK_Customers_RiskScore", "RiskScore >= 0 AND RiskScore <= 100");
            t.HasCheckConstraint("CK_Customers_TotalPaymentCount", "TotalPaymentCount >= 0");
            t.HasCheckConstraint("CK_Customers_TotalPaymentAmount", "TotalPaymentAmount >= 0");
            t.HasCheckConstraint("CK_Customers_RiskLevel", "RiskLevel IN ('VERY_LOW', 'LOW', 'MEDIUM', 'HIGH')");
        });
        
        // Foreign key relationships
        builder.HasOne(c => c.Team)
            .WithMany(t => t.Customers)
            .HasForeignKey(c => c.TeamId)
            .OnDelete(DeleteBehavior.Restrict);
            
        // JSON column for metadata
        builder.Property(c => c.Metadata)
            .HasConversion(
                v => System.Text.Json.JsonSerializer.Serialize(v, (System.Text.Json.JsonSerializerOptions)null),
                v => System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(v, (System.Text.Json.JsonSerializerOptions)null) ?? new Dictionary<string, string>()
            );
            
        // Soft delete filter
        builder.HasQueryFilter(c => !c.IsDeleted);
    }
}