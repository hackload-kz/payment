using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PaymentGateway.Core.Entities;

namespace PaymentGateway.Core.Data.Configurations;

public class PaymentMethodInfoConfiguration : IEntityTypeConfiguration<PaymentMethodInfo>
{
    public void Configure(EntityTypeBuilder<PaymentMethodInfo> builder)
    {
        builder.ToTable("PaymentMethods");
        
        // Primary key
        builder.HasKey(pm => pm.Id);
        
        // Indexes for high-performance queries
        builder.HasIndex(pm => pm.PaymentMethodId)
            .IsUnique()
            .HasDatabaseName("IX_PaymentMethods_PaymentMethodId");
            
        builder.HasIndex(pm => new { pm.CustomerId, pm.Type })
            .HasDatabaseName("IX_PaymentMethods_CustomerId_Type");
            
        builder.HasIndex(pm => new { pm.TeamId, pm.Type })
            .HasDatabaseName("IX_PaymentMethods_TeamId_Type");
            
        builder.HasIndex(pm => pm.Status)
            .HasDatabaseName("IX_PaymentMethods_Status");
            
        builder.HasIndex(pm => pm.Token)
            .HasDatabaseName("IX_PaymentMethods_Token");
            
        builder.HasIndex(pm => pm.CreatedAt)
            .HasDatabaseName("IX_PaymentMethods_CreatedAt");
            
        builder.HasIndex(pm => new { pm.Status, pm.CreatedAt })
            .HasDatabaseName("IX_PaymentMethods_Status_CreatedAt");
        
        // Properties configuration
        builder.Property(pm => pm.PaymentMethodId)
            .IsRequired()
            .HasMaxLength(100);
            
        builder.Property(pm => pm.Type)
            .IsRequired()
            .HasConversion<int>();
            
        builder.Property(pm => pm.Status)
            .IsRequired()
            .HasConversion<int>()
            .HasDefaultValue(PaymentMethodStatus.ACTIVE);
            
        // Card-specific properties
        builder.Property(pm => pm.CardMask)
            .HasMaxLength(50);
            
        builder.Property(pm => pm.CardType)
            .HasMaxLength(100);
            
        builder.Property(pm => pm.CardBrand)
            .HasMaxLength(100);
            
        builder.Property(pm => pm.CardBin)
            .HasMaxLength(10);
            
        builder.Property(pm => pm.CardLast4)
            .HasMaxLength(4);
            
        builder.Property(pm => pm.CardHolderName)
            .HasMaxLength(100);
            
        builder.Property(pm => pm.IssuingBank)
            .HasMaxLength(100);
            
        builder.Property(pm => pm.CardCountry)
            .HasMaxLength(100);
            
        // Tokenization properties
        builder.Property(pm => pm.Token)
            .HasMaxLength(200);
            
        builder.Property(pm => pm.TokenProvider)
            .HasMaxLength(100);
            
        // Digital wallet properties
        builder.Property(pm => pm.WalletId)
            .HasMaxLength(200);
            
        builder.Property(pm => pm.WalletProvider)
            .HasMaxLength(100);
            
        builder.Property(pm => pm.WalletEmail)
            .HasMaxLength(254);
            
        // Bank transfer properties
        builder.Property(pm => pm.BankAccountNumber)
            .HasMaxLength(100);
            
        builder.Property(pm => pm.BankCode)
            .HasMaxLength(100);
            
        builder.Property(pm => pm.BankName)
            .HasMaxLength(200);
            
        // SBP properties
        builder.Property(pm => pm.SbpPhoneNumber)
            .HasMaxLength(20);
            
        builder.Property(pm => pm.SbpBankCode)
            .HasMaxLength(100);
            
        // Usage statistics
        builder.Property(pm => pm.UsageCount)
            .HasDefaultValue(0);
            
        builder.Property(pm => pm.TotalAmountProcessed)
            .HasPrecision(18, 2)
            .HasDefaultValue(0);
            
        builder.Property(pm => pm.SuccessfulTransactions)
            .HasDefaultValue(0);
            
        builder.Property(pm => pm.FailedTransactions)
            .HasDefaultValue(0);
            
        // Security properties
        builder.Property(pm => pm.IsFraudulent)
            .HasDefaultValue(false);
            
        builder.Property(pm => pm.RequiresVerification)
            .HasDefaultValue(false);
            
        builder.Property(pm => pm.IsVerified)
            .HasDefaultValue(false);
            
        // Audit properties
        builder.Property(pm => pm.RowVersion)
            .IsRowVersion();
            
        // Check constraints
        builder.HasCheckConstraint("CK_PaymentMethods_UsageCount", "UsageCount >= 0");
        builder.HasCheckConstraint("CK_PaymentMethods_TotalAmountProcessed", "TotalAmountProcessed >= 0");
        builder.HasCheckConstraint("CK_PaymentMethods_SuccessfulTransactions", "SuccessfulTransactions >= 0");
        builder.HasCheckConstraint("CK_PaymentMethods_FailedTransactions", "FailedTransactions >= 0");
        builder.HasCheckConstraint("CK_PaymentMethods_CardExpiryMonth", "CardExpiryMonth IS NULL OR (CardExpiryMonth >= 1 AND CardExpiryMonth <= 12)");
        builder.HasCheckConstraint("CK_PaymentMethods_CardExpiryYear", "CardExpiryYear IS NULL OR CardExpiryYear >= 2024");
        
        // Foreign key relationships
        builder.HasOne(pm => pm.Customer)
            .WithMany(c => c.PaymentMethods)
            .HasForeignKey(pm => pm.CustomerId)
            .OnDelete(DeleteBehavior.SetNull);
            
        builder.HasOne(pm => pm.Team)
            .WithMany(t => t.PaymentMethods)
            .HasForeignKey(pm => pm.TeamId)
            .OnDelete(DeleteBehavior.Restrict);
            
        // JSON column for metadata
        builder.Property(pm => pm.Metadata)
            .HasConversion(
                v => System.Text.Json.JsonSerializer.Serialize(v, (System.Text.Json.JsonSerializerOptions)null),
                v => System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(v, (System.Text.Json.JsonSerializerOptions)null) ?? new Dictionary<string, string>()
            );
            
        // Soft delete filter
        builder.HasQueryFilter(pm => !pm.IsDeleted);
    }
}