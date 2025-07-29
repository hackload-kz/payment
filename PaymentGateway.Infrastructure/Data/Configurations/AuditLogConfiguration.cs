using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PaymentGateway.Core.Entities;

namespace PaymentGateway.Infrastructure.Data.Configurations;

public class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> builder)
    {
        builder.ToTable("audit_logs", "payment");

        builder.HasKey(a => a.Id);

        builder.Property(a => a.EntityName)
            .IsRequired()
            .HasMaxLength(100)
            .HasColumnName("entity_name");

        builder.Property(a => a.EntityId)
            .IsRequired()
            .HasMaxLength(50)
            .HasColumnName("entity_id");

        builder.Property(a => a.Operation)
            .IsRequired()
            .HasMaxLength(20)
            .HasColumnName("operation");

        builder.Property(a => a.OldValues)
            .HasColumnType("jsonb")
            .HasColumnName("old_values");

        builder.Property(a => a.NewValues)
            .HasColumnType("jsonb")
            .HasColumnName("new_values");

        builder.Property(a => a.Changes)
            .HasColumnType("jsonb")
            .HasColumnName("changes");

        builder.Property(a => a.UserId)
            .HasMaxLength(100)
            .HasColumnName("user_id");

        builder.Property(a => a.UserName)
            .HasMaxLength(200)
            .HasColumnName("user_name");

        builder.Property(a => a.IPAddress)
            .HasMaxLength(45)
            .HasColumnName("ip_address");

        builder.Property(a => a.UserAgent)
            .HasMaxLength(500)
            .HasColumnName("user_agent");

        builder.Property(a => a.CreatedAt)
            .IsRequired()
            .HasColumnName("created_at");

        builder.Property(a => a.UpdatedAt)
            .IsRequired()
            .HasColumnName("updated_at");

        builder.Property(a => a.CreatedBy)
            .HasMaxLength(100)
            .HasColumnName("created_by");

        builder.Property(a => a.UpdatedBy)
            .HasMaxLength(100)
            .HasColumnName("updated_by");

        // Indexes for performance
        builder.HasIndex(a => a.EntityName)
            .HasDatabaseName("ix_audit_logs_entity_name");

        builder.HasIndex(a => a.EntityId)
            .HasDatabaseName("ix_audit_logs_entity_id");

        builder.HasIndex(a => a.Operation)
            .HasDatabaseName("ix_audit_logs_operation");

        builder.HasIndex(a => a.CreatedAt)
            .HasDatabaseName("ix_audit_logs_created_at");

        builder.HasIndex(a => a.UserId)
            .HasDatabaseName("ix_audit_logs_user_id");

        builder.HasIndex(a => new { a.EntityName, a.EntityId })
            .HasDatabaseName("ix_audit_logs_entity_name_id");

        builder.HasIndex(a => new { a.EntityName, a.Operation, a.CreatedAt })
            .HasDatabaseName("ix_audit_logs_entity_operation_created");
    }
}