using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PaymentGateway.Core.Services;

namespace PaymentGateway.Core.Data.Configurations;

public class AuditEntryConfiguration : IEntityTypeConfiguration<AuditEntry>
{
    public void Configure(EntityTypeBuilder<AuditEntry> builder)
    {
        builder.ToTable("AuditLog");
        
        // Primary key
        builder.HasKey(ae => ae.Id);
        
        // Indexes for audit queries
        builder.HasIndex(ae => ae.EntityId)
            .HasDatabaseName("IX_AuditLog_EntityId");
            
        builder.HasIndex(ae => ae.EntityType)
            .HasDatabaseName("IX_AuditLog_EntityType");
            
        builder.HasIndex(ae => new { ae.EntityType, ae.EntityId })
            .HasDatabaseName("IX_AuditLog_EntityType_EntityId");
            
        builder.HasIndex(ae => ae.UserId)
            .HasDatabaseName("IX_AuditLog_UserId");
            
        builder.HasIndex(ae => ae.Timestamp)
            .HasDatabaseName("IX_AuditLog_Timestamp");
            
        builder.HasIndex(ae => ae.Action)
            .HasDatabaseName("IX_AuditLog_Action");
            
        builder.HasIndex(ae => new { ae.Action, ae.Timestamp })
            .HasDatabaseName("IX_AuditLog_Action_Timestamp");
        
        // Properties configuration
        builder.Property(ae => ae.EntityId)
            .IsRequired();
            
        builder.Property(ae => ae.EntityType)
            .IsRequired()
            .HasMaxLength(100);
            
        builder.Property(ae => ae.Action)
            .IsRequired()
            .HasConversion<int>();
            
        builder.Property(ae => ae.UserId)
            .HasMaxLength(100);
            
        builder.Property(ae => ae.Timestamp)
            .IsRequired()
            .HasDefaultValueSql("CURRENT_TIMESTAMP");
            
        builder.Property(ae => ae.Details)
            .HasMaxLength(1000);
            
        builder.Property(ae => ae.EntitySnapshot)
            .IsRequired()
            .HasColumnType("jsonb"); // PostgreSQL JSONB for better performance
        
        // Table partitioning for large audit logs (by timestamp)
        // This would typically be done via migration scripts for PostgreSQL
        // builder.HasAnnotation("PostgreSQL:PartitionedBy", "RANGE (Timestamp)");
        
        // No soft delete for audit logs - they should be permanent
        // No update operations allowed on audit logs
        builder.Property<DateTime>("CreatedAt")
            .HasDefaultValueSql("CURRENT_TIMESTAMP");
    }
}