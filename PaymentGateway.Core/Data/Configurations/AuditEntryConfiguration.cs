using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PaymentGateway.Core.Entities;

namespace PaymentGateway.Core.Data.Configurations;

public class AuditEntryConfiguration : IEntityTypeConfiguration<AuditEntry>
{
    public void Configure(EntityTypeBuilder<AuditEntry> builder)
    {
        builder.ToTable("audit_entries", "payment");
        
        // Primary key
        builder.HasKey(ae => ae.Id);
        
        // Core indexes for audit queries
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
            
        // Additional indexes for enhanced functionality
        builder.HasIndex(ae => ae.TeamSlug)
            .HasDatabaseName("IX_AuditLog_TeamSlug");
            
        builder.HasIndex(ae => ae.CorrelationId)
            .HasDatabaseName("IX_AuditLog_CorrelationId");
            
        builder.HasIndex(ae => ae.RequestId)
            .HasDatabaseName("IX_AuditLog_RequestId");
            
        builder.HasIndex(ae => ae.Severity)
            .HasDatabaseName("IX_AuditLog_Severity");
            
        builder.HasIndex(ae => ae.Category)
            .HasDatabaseName("IX_AuditLog_Category");
            
        builder.HasIndex(ae => ae.IsSensitive)
            .HasDatabaseName("IX_AuditLog_IsSensitive");
            
        builder.HasIndex(ae => ae.IsArchived)
            .HasDatabaseName("IX_AuditLog_IsArchived");
            
        builder.HasIndex(ae => new { ae.Category, ae.Severity, ae.Timestamp })
            .HasDatabaseName("IX_AuditLog_Category_Severity_Timestamp");
            
        builder.HasIndex(ae => new { ae.IsSensitive, ae.Timestamp })
            .HasDatabaseName("IX_AuditLog_Sensitive_Timestamp");
        
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
            
        builder.Property(ae => ae.TeamSlug)
            .HasMaxLength(50);
            
        builder.Property(ae => ae.Timestamp)
            .IsRequired()
            .HasDefaultValueSql("CURRENT_TIMESTAMP");
            
        builder.Property(ae => ae.Details)
            .HasMaxLength(1000);
            
        builder.Property(ae => ae.EntitySnapshotBefore)
            .HasColumnType("jsonb"); // PostgreSQL JSONB for better performance
            
        builder.Property(ae => ae.EntitySnapshotAfter)
            .IsRequired()
            .HasColumnType("jsonb"); // PostgreSQL JSONB for better performance
            
        builder.Property(ae => ae.CorrelationId)
            .HasMaxLength(100);
            
        builder.Property(ae => ae.RequestId)
            .HasMaxLength(100);
            
        builder.Property(ae => ae.IpAddress)
            .HasMaxLength(45); // IPv6 max length
            
        builder.Property(ae => ae.UserAgent)
            .HasMaxLength(500);
            
        builder.Property(ae => ae.SessionId)
            .HasMaxLength(100);
            
        builder.Property(ae => ae.RiskScore)
            .HasPrecision(5, 2); // 999.99 max
            
        builder.Property(ae => ae.Severity)
            .IsRequired()
            .HasConversion<int>();
            
        builder.Property(ae => ae.Category)
            .IsRequired()
            .HasConversion<int>();
            
        builder.Property(ae => ae.Metadata)
            .HasColumnType("jsonb");
            
        builder.Property(ae => ae.IntegrityHash)
            .HasMaxLength(64);
            
        builder.Property(ae => ae.IsSensitive)
            .IsRequired()
            .HasDefaultValue(false);
            
        builder.Property(ae => ae.IsArchived)
            .IsRequired()
            .HasDefaultValue(false);
            
        builder.Property(ae => ae.ArchivedAt);
        
        // Table partitioning for large audit logs (by timestamp)
        // This would typically be done via migration scripts for PostgreSQL
        builder.HasAnnotation("PostgreSQL:PartitionedBy", "RANGE (timestamp)");
        
        // No soft delete for audit logs - they should be permanent
        // No update operations allowed on audit logs
        builder.Property<DateTime>("CreatedAt")
            .HasDefaultValueSql("CURRENT_TIMESTAMP");
            
        // Add check constraints for data integrity using modern syntax
        builder.ToTable(t =>
        {
            t.HasCheckConstraint("CK_AuditLog_RiskScore", "\"RiskScore\" IS NULL OR (\"RiskScore\" >= 0 AND \"RiskScore\" <= 999.99)");
            t.HasCheckConstraint("CK_AuditLog_ArchivedConstraint", "(\"IsArchived\" = false AND \"ArchivedAt\" IS NULL) OR (\"IsArchived\" = true AND \"ArchivedAt\" IS NOT NULL)");
            t.HasCheckConstraint("CK_AuditLog_Timestamp", "\"Timestamp\" <= CURRENT_TIMESTAMP");
        });
    }
}