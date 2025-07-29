using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PaymentGateway.Core.Entities;

namespace PaymentGateway.Infrastructure.Data.Configurations;

public class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> builder)
    {
        builder.ToTable("payments", "payment");

        builder.HasKey(p => p.Id);

        builder.Property(p => p.PaymentId)
            .IsRequired()
            .HasMaxLength(50)
            .HasColumnName("payment_id");

        builder.Property(p => p.OrderId)
            .IsRequired()
            .HasMaxLength(50)
            .HasColumnName("order_id");

        builder.Property(p => p.TeamSlug)
            .IsRequired()
            .HasMaxLength(100)
            .HasColumnName("team_slug");

        builder.Property(p => p.Amount)
            .HasPrecision(18, 2)
            .HasColumnName("amount");

        builder.Property(p => p.Currency)
            .IsRequired()
            .HasMaxLength(3)
            .HasDefaultValue("RUB")
            .HasColumnName("currency");

        builder.Property(p => p.Status)
            .HasConversion<int>()
            .HasColumnName("status");

        builder.Property(p => p.Description)
            .HasMaxLength(500)
            .HasColumnName("description");

        builder.Property(p => p.CustomerEmail)
            .HasMaxLength(255)
            .HasColumnName("customer_email");

        builder.Property(p => p.PaymentURL)
            .HasMaxLength(1000)
            .HasColumnName("payment_url");

        builder.Property(p => p.AuthorizedAt)
            .HasColumnName("authorized_at");

        builder.Property(p => p.ConfirmedAt)
            .HasColumnName("confirmed_at");

        builder.Property(p => p.CancelledAt)
            .HasColumnName("cancelled_at");

        builder.Property(p => p.FailureReason)
            .HasMaxLength(1000)
            .HasColumnName("failure_reason");

        builder.Property(p => p.BankOrderId)
            .HasMaxLength(100)
            .HasColumnName("bank_order_id");

        builder.Property(p => p.CardMask)
            .HasMaxLength(20)
            .HasColumnName("card_mask");

        builder.Property(p => p.PaymentMethod)
            .HasConversion<int>()
            .HasColumnName("payment_method");

        // Audit fields
        builder.Property(p => p.CreatedAt)
            .IsRequired()
            .HasColumnName("created_at");

        builder.Property(p => p.UpdatedAt)
            .IsRequired()
            .HasColumnName("updated_at");

        builder.Property(p => p.CreatedBy)
            .HasMaxLength(100)
            .HasColumnName("created_by");

        builder.Property(p => p.UpdatedBy)
            .HasMaxLength(100)
            .HasColumnName("updated_by");

        builder.Property(p => p.RowVersion)
            .IsRowVersion()
            .HasColumnName("row_version");

        // Indexes
        builder.HasIndex(p => p.PaymentId)
            .IsUnique()
            .HasDatabaseName("ix_payments_payment_id");

        builder.HasIndex(p => p.OrderId)
            .HasDatabaseName("ix_payments_order_id");

        builder.HasIndex(p => p.TeamSlug)
            .HasDatabaseName("ix_payments_team_slug");

        builder.HasIndex(p => p.Status)
            .HasDatabaseName("ix_payments_status");

        builder.HasIndex(p => p.CreatedAt)
            .HasDatabaseName("ix_payments_created_at");

        builder.HasIndex(p => new { p.TeamSlug, p.OrderId })
            .IsUnique()
            .HasDatabaseName("ix_payments_team_slug_order_id");

        // Foreign key relationship with Team
        builder.HasOne<Team>()
            .WithMany(t => t.Payments)
            .HasForeignKey(p => p.TeamSlug)
            .HasPrincipalKey(t => t.TeamSlug)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_payments_team_slug");
    }
}