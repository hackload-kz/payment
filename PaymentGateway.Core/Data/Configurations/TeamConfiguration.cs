using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PaymentGateway.Core.Entities;
using PaymentGateway.Core.Enums;

namespace PaymentGateway.Core.Data.Configurations;

public class TeamConfiguration : IEntityTypeConfiguration<Team>
{
    public void Configure(EntityTypeBuilder<Team> builder)
    {
        builder.ToTable("teams", "payment");
        
        // Primary key
        builder.HasKey(t => t.Id);
        
        // Indexes for high-performance queries
        builder.HasIndex(t => t.TeamSlug)
            .IsUnique()
            .HasDatabaseName("ix_teams_team_slug");
            
        builder.HasIndex(t => t.TeamName)
            .HasDatabaseName("ix_teams_team_name");
            
        builder.HasIndex(t => t.IsActive)
            .HasDatabaseName("ix_teams_is_active");
            
        builder.HasIndex(t => t.CreatedAt)
            .HasDatabaseName("ix_teams_created_at");
            
        builder.HasIndex(t => new { t.IsActive, t.CreatedAt })
            .HasDatabaseName("ix_teams_is_active_created_at");
        
        // Properties configuration (snake_case column names handled automatically)
        builder.Property(t => t.TeamSlug)
            .IsRequired()
            .HasMaxLength(100);
            
        builder.Property(t => t.TeamName)
            .IsRequired()
            .HasMaxLength(200);
            
        builder.Property(t => t.PasswordHash)
            .IsRequired()
            .HasMaxLength(500);
            
        builder.Property(t => t.IsActive)
            .IsRequired()
            .HasDefaultValue(true);
            
        builder.Property(t => t.FailedAuthenticationAttempts)
            .HasDefaultValue(0);
            
        // Payment limits with precision
        builder.Property(t => t.MinPaymentAmount)
            .HasPrecision(18, 2);
            
        builder.Property(t => t.MaxPaymentAmount)
            .HasPrecision(18, 2);
            
        builder.Property(t => t.DailyPaymentLimit)
            .HasPrecision(18, 2);
            
        builder.Property(t => t.MonthlyPaymentLimit)
            .HasPrecision(18, 2);
            
        // Audit properties
        builder.Property(t => t.RowVersion)
            .IsRowVersion();
            
        // Check constraints using modern syntax
        builder.ToTable("teams", "payment", t =>
        {
            t.HasCheckConstraint("CK_Teams_MinPaymentAmount", "MinPaymentAmount >= 0");
            t.HasCheckConstraint("CK_Teams_MaxPaymentAmount", "MaxPaymentAmount >= 0");
            t.HasCheckConstraint("CK_Teams_DailyPaymentLimit", "DailyPaymentLimit >= 0");
            t.HasCheckConstraint("CK_Teams_MonthlyPaymentLimit", "MonthlyPaymentLimit >= 0");
            t.HasCheckConstraint("CK_Teams_FailedAuthenticationAttempts", "FailedAuthenticationAttempts >= 0");
            t.HasCheckConstraint("CK_Teams_MinMax_PaymentAmount", "MinPaymentAmount IS NULL OR MaxPaymentAmount IS NULL OR MinPaymentAmount <= MaxPaymentAmount");
            t.HasCheckConstraint("CK_Teams_DailyMonthly_PaymentLimit", "DailyPaymentLimit IS NULL OR MonthlyPaymentLimit IS NULL OR DailyPaymentLimit <= MonthlyPaymentLimit");
        });
        
        // PostgreSQL array columns  
        builder.Property(t => t.SupportedCurrencies)
            .HasConversion(
                v => v.ToArray(), 
                v => v.ToList(),
                new Microsoft.EntityFrameworkCore.ChangeTracking.ValueComparer<List<string>>(
                    (c1, c2) => c1!.SequenceEqual(c2!),
                    c => c.Aggregate(0, (a, v) => HashCode.Combine(a, v.GetHashCode())),
                    c => c.ToList()))
            .HasColumnType("text[]");
            
        builder.Property(t => t.SupportedPaymentMethods)
            .HasConversion(
                v => v.Select(e => e.ToString()).ToArray(),
                v => v.Select(s => Enum.Parse<PaymentMethod>(s)).ToList(),
                new Microsoft.EntityFrameworkCore.ChangeTracking.ValueComparer<List<PaymentMethod>>(
                    (c1, c2) => c1!.SequenceEqual(c2!),
                    c => c.Aggregate(0, (a, v) => HashCode.Combine(a, v.GetHashCode())),
                    c => c.ToList()))
            .HasColumnType("text[]");
            
        builder.Property(t => t.BusinessInfo)
            .HasConversion(
                v => System.Text.Json.JsonSerializer.Serialize(v, (System.Text.Json.JsonSerializerOptions?)null),
                v => System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(v, (System.Text.Json.JsonSerializerOptions?)null) ?? new Dictionary<string, string>())
            .HasColumnType("jsonb");
            
        builder.Property(t => t.Metadata)
            .HasConversion(
                v => System.Text.Json.JsonSerializer.Serialize(v, (System.Text.Json.JsonSerializerOptions?)null),
                v => System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(v, (System.Text.Json.JsonSerializerOptions?)null) ?? new Dictionary<string, string>())
            .HasColumnType("jsonb");
            
        // Soft delete filter
        builder.HasQueryFilter(t => !t.IsDeleted);
    }
}