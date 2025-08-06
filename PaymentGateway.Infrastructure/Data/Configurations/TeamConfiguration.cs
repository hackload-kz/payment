using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PaymentGateway.Core.Entities;

namespace PaymentGateway.Infrastructure.Data.Configurations;

public class TeamConfiguration : IEntityTypeConfiguration<Team>
{
    public void Configure(EntityTypeBuilder<Team> builder)
    {
        builder.ToTable("teams", "payment");

        builder.HasKey(t => t.Id);

        builder.Property(t => t.TeamSlug)
            .IsRequired()
            .HasMaxLength(100)
            .HasColumnName("team_slug");

        builder.Property(t => t.TeamName)
            .IsRequired()
            .HasMaxLength(200)
            .HasColumnName("team_name");

        builder.Property(t => t.PasswordHash)
            .IsRequired()
            .HasMaxLength(255)
            .HasColumnName("password_hash");

        builder.Property(t => t.IsActive)
            .IsRequired()
            .HasDefaultValue(true)
            .HasColumnName("is_active");

        builder.Property(t => t.NotificationUrl)
            .HasMaxLength(1000)
            .HasColumnName("notification_url");

        builder.Property(t => t.SuccessUrl)
            .HasMaxLength(1000)
            .HasColumnName("success_url");

        builder.Property(t => t.FailUrl)
            .HasMaxLength(1000)
            .HasColumnName("fail_url");

        // Audit fields
        builder.Property(t => t.CreatedAt)
            .IsRequired()
            .HasColumnName("created_at");

        builder.Property(t => t.UpdatedAt)
            .IsRequired()
            .HasColumnName("updated_at");

        builder.Property(t => t.CreatedBy)
            .HasMaxLength(100)
            .HasColumnName("created_by");

        builder.Property(t => t.UpdatedBy)
            .HasMaxLength(100)
            .HasColumnName("updated_by");

        builder.Property(t => t.RowVersion)
            .IsRowVersion()
            .HasColumnName("row_version");

        // Indexes
        builder.HasIndex(t => t.TeamSlug)
            .IsUnique()
            .HasDatabaseName("ix_teams_team_slug");

        builder.HasIndex(t => t.TeamName)
            .HasDatabaseName("ix_teams_team_name");

        builder.HasIndex(t => t.IsActive)
            .HasDatabaseName("ix_teams_is_active");

        // PostgreSQL array columns
        builder.Property(t => t.SupportedCurrencies)
            .HasConversion(
                v => v.ToArray(), 
                v => v.ToList(),
                new Microsoft.EntityFrameworkCore.ChangeTracking.ValueComparer<List<string>>(
                    (c1, c2) => c1!.SequenceEqual(c2!),
                    c => c.Aggregate(0, (a, v) => HashCode.Combine(a, v.GetHashCode())),
                    c => c.ToList()))
            .HasColumnType("text[]")
            .HasColumnName("supported_currencies");
            
        builder.Property(t => t.SupportedPaymentMethods)
            .HasConversion(
                v => v.Select(e => e.ToString()).ToArray(),
                v => v.Select(s => Enum.Parse<PaymentGateway.Core.Enums.PaymentMethod>(s)).ToList(),
                new Microsoft.EntityFrameworkCore.ChangeTracking.ValueComparer<List<PaymentGateway.Core.Enums.PaymentMethod>>(
                    (c1, c2) => c1!.SequenceEqual(c2!),
                    c => c.Aggregate(0, (a, v) => HashCode.Combine(a, v.GetHashCode())),
                    c => c.ToList()))
            .HasColumnType("text[]")
            .HasColumnName("supported_payment_methods");

        // Navigation properties
        builder.HasMany(t => t.Payments)
            .WithOne()
            .HasForeignKey(p => p.TeamSlug)
            .HasPrincipalKey(t => t.TeamSlug)
            .OnDelete(DeleteBehavior.Restrict);
    }
}