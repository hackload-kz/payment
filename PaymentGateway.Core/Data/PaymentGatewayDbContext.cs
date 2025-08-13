using Microsoft.EntityFrameworkCore;
using PaymentGateway.Core.Configuration;
using PaymentGateway.Core.Data.Configurations;
using PaymentGateway.Core.Entities;
using PaymentGateway.Core.Services;
using System.Linq.Expressions;

namespace PaymentGateway.Core.Data;

public class PaymentGatewayDbContext : DbContext
{
    public PaymentGatewayDbContext(DbContextOptions<PaymentGatewayDbContext> options)
        : base(options)
    {
    }

    // Domain entities
    public DbSet<Payment> Payments { get; set; } = null!;
    public DbSet<Transaction> Transactions { get; set; } = null!;
    public DbSet<Team> Teams { get; set; } = null!;
    public DbSet<Customer> Customers { get; set; } = null!;
    public DbSet<PaymentMethodInfo> PaymentMethods { get; set; } = null!;
    
    // State machine
    public DbSet<PaymentStateTransition> PaymentStateTransitions { get; set; } = null!;
    
    // Audit tables
    public DbSet<AuditEntry> AuditLog { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Apply entity configurations
        modelBuilder.ApplyConfiguration(new PaymentConfiguration());
        modelBuilder.ApplyConfiguration(new TransactionConfiguration());
        modelBuilder.ApplyConfiguration(new TeamConfiguration());
        modelBuilder.ApplyConfiguration(new CustomerConfiguration());
        modelBuilder.ApplyConfiguration(new PaymentMethodInfoConfiguration());
        modelBuilder.ApplyConfiguration(new PaymentStateTransitionConfiguration());
        modelBuilder.ApplyConfiguration(new AuditEntryConfiguration());

        // Global query filters for soft delete
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (typeof(BaseEntity).IsAssignableFrom(entityType.ClrType))
            {
                var parameter = Expression.Parameter(entityType.ClrType, "e");
                var property = Expression.Property(parameter, nameof(BaseEntity.IsDeleted));
                var filter = Expression.Lambda(Expression.Not(property), parameter);
                
                modelBuilder.Entity(entityType.ClrType).HasQueryFilter(filter);
            }
        }

        // Configure PostgreSQL-specific features
        ConfigurePostgreSqlFeatures(modelBuilder);
    }

    // OnConfiguring removed - conflicts with DbContext pooling
    // Configuration is handled in DatabaseConfiguration.AddDatabase()

    private static void ConfigurePostgreSqlFeatures(ModelBuilder modelBuilder)
    {
        // Configure PostgreSQL schema
        modelBuilder.HasDefaultSchema("payment");
        
        // Configure naming conventions (snake_case for PostgreSQL)
        foreach (var entity in modelBuilder.Model.GetEntityTypes())
        {
            // Convert table names to snake_case
            if (entity.GetTableName() != null)
            {
                entity.SetTableName(entity.GetTableName()!.ToSnakeCase());
            }

            // Convert column names to snake_case
            foreach (var property in entity.GetProperties())
            {
                if (property.GetColumnName() != null)
                {
                    property.SetColumnName(property.GetColumnName().ToSnakeCase());
                }
            }

            // Convert key names to snake_case
            foreach (var key in entity.GetKeys())
            {
                if (key.GetName() != null)
                {
                    key.SetName(key.GetName()!.ToSnakeCase());
                }
            }

            // Convert foreign key names to snake_case
            foreach (var foreignKey in entity.GetForeignKeys())
            {
                if (foreignKey.GetConstraintName() != null)
                {
                    foreignKey.SetConstraintName(foreignKey.GetConstraintName()!.ToSnakeCase());
                }
            }

            // Convert index names to snake_case
            foreach (var index in entity.GetIndexes())
            {
                if (index.GetDatabaseName() != null)
                {
                    index.SetDatabaseName(index.GetDatabaseName()!.ToSnakeCase());
                }
            }
        }
        
        // Configure PostgreSQL-specific data types and features
        // (Array configuration is handled in individual entity configurations)
            
        // Configure HSTORE columns with proper value converters
        // All metadata columns in the database are created as hstore
        // Dictionary<string, string> properties work natively with hstore in modern Npgsql
        
        // Note: Modern Npgsql Entity Framework provider automatically handles
        // Dictionary<string, string> to hstore mapping, so we just need to specify the column type

        // Configure PostgreSQL sequences for ID generation
        modelBuilder.HasSequence<long>("payment_id_seq")
            .StartsAt(1000000)
            .IncrementsBy(1);
            
        modelBuilder.HasSequence<long>("transaction_id_seq")
            .StartsAt(1000000)
            .IncrementsBy(1);

        // Configure table partitioning for large tables (would be done in migrations)
        // This is just a placeholder - actual partitioning is done via SQL scripts
        modelBuilder.Entity<AuditEntry>()
            .HasAnnotation("PostgreSQL:PartitionedBy", "RANGE (timestamp)");
            
        modelBuilder.Entity<Transaction>()
            .HasAnnotation("PostgreSQL:PartitionedBy", "RANGE (created_at)");
    }

    public override int SaveChanges()
    {
        UpdateAuditFields();
        return base.SaveChanges();
    }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        UpdateAuditFields();
        return await base.SaveChangesAsync(cancellationToken);
    }

    private void UpdateAuditFields()
    {
        var entries = ChangeTracker.Entries<BaseEntity>();
        var currentTime = DateTime.UtcNow;
        
        foreach (var entry in entries)
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    entry.Entity.CreatedAt = currentTime;
                    entry.Entity.UpdatedAt = currentTime;
                    entry.Entity.IsDeleted = false;
                    
                    // Set ID if not already set
                    if (entry.Entity.Id == Guid.Empty)
                    {
                        entry.Entity.Id = Guid.NewGuid();
                    }
                    break;
                    
                case EntityState.Modified:
                    entry.Entity.UpdatedAt = currentTime;
                    
                    // Prevent modification of audit fields
                    entry.Property(e => e.CreatedAt).IsModified = false;
                    entry.Property(e => e.Id).IsModified = false;
                    
                    // Handle soft delete
                    if (entry.Entity.IsDeleted && entry.Entity.DeletedAt == null)
                    {
                        entry.Entity.DeletedAt = currentTime;
                    }
                    break;
                    
                case EntityState.Deleted:
                    // Convert hard delete to soft delete
                    entry.State = EntityState.Modified;
                    entry.Entity.IsDeleted = true;
                    entry.Entity.DeletedAt = currentTime;
                    entry.Entity.UpdatedAt = currentTime;
                    break;
            }
        }
    }

    /// <summary>
    /// Override to add enhanced change tracking and concurrency control
    /// </summary>
    public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        // Pre-save validation and optimistic concurrency handling
        await ValidateConcurrencyAsync(cancellationToken);
        
        UpdateAuditFields();
        
        try
        {
            return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            // Handle concurrency conflicts with retry logic
            await HandleConcurrencyConflictAsync(ex, cancellationToken);
            throw; // Re-throw after logging
        }
    }

    /// <summary>
    /// Override to add enhanced change tracking and concurrency control
    /// </summary>
    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        ValidateConcurrencyAsync(CancellationToken.None).GetAwaiter().GetResult();
        UpdateAuditFields();
        
        try
        {
            return base.SaveChanges(acceptAllChangesOnSuccess);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            HandleConcurrencyConflictAsync(ex, CancellationToken.None).GetAwaiter().GetResult();
            throw;
        }
    }

    private async Task ValidateConcurrencyAsync(CancellationToken cancellationToken)
    {
        await Task.CompletedTask; // Make method async

        var criticalEntities = ChangeTracker.Entries()
            .Where(e => e.State == EntityState.Modified || e.State == EntityState.Deleted)
            .Where(e => e.Entity is Payment or Transaction)
            .ToList();

        foreach (var entry in criticalEntities)
        {
            // Additional validation for critical entities can be added here
            // This is a placeholder for business-specific concurrency validation
        }
    }

    private async Task HandleConcurrencyConflictAsync(DbUpdateConcurrencyException exception, CancellationToken cancellationToken)
    {
        await Task.CompletedTask; // Make method async

        foreach (var entry in exception.Entries)
        {
            var entityType = entry.Entity.GetType().Name;
            var entityId = entry.Entity is BaseEntity baseEntity ? baseEntity.Id.ToString() : "Unknown";

            // Log detailed concurrency conflict information
            var currentValues = entry.CurrentValues?.ToObject();
            var originalValues = entry.OriginalValues?.ToObject();
            var databaseValues = await entry.GetDatabaseValuesAsync(cancellationToken);

            // This would typically integrate with logging service
            // For now, we'll use a simple approach
        }
    }

    /// <summary>
    /// Get change tracking information for monitoring
    /// </summary>
    public ChangeTrackingInfo GetChangeTrackingInfo()
    {
        var entries = ChangeTracker.Entries().ToList();
        
        return new ChangeTrackingInfo
        {
            TotalEntries = entries.Count,
            AddedEntries = entries.Count(e => e.State == EntityState.Added),
            ModifiedEntries = entries.Count(e => e.State == EntityState.Modified),
            DeletedEntries = entries.Count(e => e.State == EntityState.Deleted),
            UnchangedEntries = entries.Count(e => e.State == EntityState.Unchanged),
            DetachedEntries = entries.Count(e => e.State == EntityState.Detached),
            HasChanges = ChangeTracker.HasChanges()
        };
    }
}

/// <summary>
/// Information about change tracking state for monitoring
/// </summary>
public record ChangeTrackingInfo
{
    public int TotalEntries { get; init; }
    public int AddedEntries { get; init; }
    public int ModifiedEntries { get; init; }
    public int DeletedEntries { get; init; }
    public int UnchangedEntries { get; init; }
    public int DetachedEntries { get; init; }
    public bool HasChanges { get; init; }
}

// Extension method for snake_case conversion
public static class StringExtensions
{
    public static string ToSnakeCase(this string input)
    {
        if (string.IsNullOrEmpty(input)) return input;

        var result = new System.Text.StringBuilder();
        var isFirst = true;

        foreach (var c in input)
        {
            if (char.IsUpper(c) && !isFirst)
            {
                result.Append('_');
            }
            result.Append(char.ToLower(c));
            isFirst = false;
        }

        return result.ToString();
    }
}