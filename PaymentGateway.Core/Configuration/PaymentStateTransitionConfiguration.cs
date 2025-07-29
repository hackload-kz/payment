using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PaymentGateway.Core.Services;
using System.Text.Json;

namespace PaymentGateway.Core.Configuration;

public class PaymentStateTransitionConfiguration : IEntityTypeConfiguration<PaymentStateTransition>
{
    public void Configure(EntityTypeBuilder<PaymentStateTransition> builder)
    {
        // Table configuration
        builder.ToTable("PaymentStateTransitions");
        
        // Primary key
        builder.HasKey(t => t.Id);
        
        // Properties
        builder.Property(t => t.Id)
            .IsRequired()
            .ValueGeneratedOnAdd();
            
        builder.Property(t => t.PaymentId)
            .IsRequired();
            
        builder.Property(t => t.FromStatus)
            .IsRequired()
            .HasConversion<int>();
            
        builder.Property(t => t.ToStatus)
            .IsRequired()
            .HasConversion<int>();
            
        builder.Property(t => t.TransitionId)
            .IsRequired()
            .HasMaxLength(100);
            
        builder.Property(t => t.TransitionedAt)
            .IsRequired()
            .HasColumnType("timestamp with time zone");
            
        builder.Property(t => t.UserId)
            .HasMaxLength(100);
            
        builder.Property(t => t.Reason)
            .HasMaxLength(1000);
            
        builder.Property(t => t.IsRollback)
            .IsRequired()
            .HasDefaultValue(false);
            
        builder.Property(t => t.RollbackFromTransitionId)
            .HasMaxLength(100);

        // JSON column for Context (PostgreSQL JSONB)
        builder.Property(t => t.Context)
            .HasColumnType("jsonb")
            .HasConversion(
                v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                v => JsonSerializer.Deserialize<Dictionary<string, object>>(v, (JsonSerializerOptions?)null) ?? new Dictionary<string, object>()
            );

        // Indexes for performance
        builder.HasIndex(t => t.PaymentId)
            .HasDatabaseName("IX_PaymentStateTransitions_PaymentId");
            
        builder.HasIndex(t => t.TransitionId)
            .HasDatabaseName("IX_PaymentStateTransitions_TransitionId")
            .IsUnique();
            
        builder.HasIndex(t => t.TransitionedAt)
            .HasDatabaseName("IX_PaymentStateTransitions_TransitionedAt");
            
        builder.HasIndex(t => t.UserId)
            .HasDatabaseName("IX_PaymentStateTransitions_UserId");
            
        builder.HasIndex(t => new { t.FromStatus, t.ToStatus })
            .HasDatabaseName("IX_PaymentStateTransitions_StatusTransition");
            
        builder.HasIndex(t => t.IsRollback)
            .HasDatabaseName("IX_PaymentStateTransitions_IsRollback")
            .HasFilter("\"IsRollback\" = true");
            
        builder.HasIndex(t => t.RollbackFromTransitionId)
            .HasDatabaseName("IX_PaymentStateTransitions_RollbackFromTransitionId")
            .HasFilter("\"RollbackFromTransitionId\" IS NOT NULL");

        // Composite indexes for common queries
        builder.HasIndex(t => new { t.PaymentId, t.TransitionedAt })
            .HasDatabaseName("IX_PaymentStateTransitions_PaymentId_TransitionedAt");
            
        builder.HasIndex(t => new { t.ToStatus, t.TransitionedAt })
            .HasDatabaseName("IX_PaymentStateTransitions_ToStatus_TransitionedAt");

        // Foreign key relationships
        builder.HasOne(t => t.Payment)
            .WithMany()
            .HasForeignKey(t => t.PaymentId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("FK_PaymentStateTransitions_Payments");

        // Check constraints
        builder.HasCheckConstraint("CK_PaymentStateTransitions_TransitionedAt_Valid", 
            "\"TransitionedAt\" >= '2020-01-01'::timestamp");
            
        builder.HasCheckConstraint("CK_PaymentStateTransitions_FromStatus_ToStatus_Different", 
            "\"FromStatus\" != \"ToStatus\"");
    }
}