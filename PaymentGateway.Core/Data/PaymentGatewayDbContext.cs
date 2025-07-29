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

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        if (!optionsBuilder.IsConfigured)
        {
            optionsBuilder.UseNpgsql("Host=localhost;Database=PaymentGateway;Username=postgres;Password=password");
        }

        // Enable sensitive data logging in development
        optionsBuilder.EnableSensitiveDataLogging();
        optionsBuilder.EnableDetailedErrors();
        
        // Configure connection pooling
        optionsBuilder.UseNpgsql(options =>
        {
            options.EnableRetryOnFailure(
                maxRetryCount: 3,
                maxRetryDelay: TimeSpan.FromSeconds(5),
                errorCodesToAdd: null);
        });
    }

    private static void ConfigurePostgreSqlFeatures(ModelBuilder modelBuilder)
    {
        // Configure PostgreSQL-specific data types and features
        
        // Use PostgreSQL arrays for currency lists
        modelBuilder.Entity<Team>()
            .Property(t => t.SupportedCurrencies)
            .HasColumnType("text[]");
            
        // Configure JSONB columns for better performance
        modelBuilder.Entity<Payment>()
            .Property(p => p.Metadata)
            .HasColumnType("jsonb");
            
        modelBuilder.Entity<Transaction>()
            .Property(t => t.AdditionalData)
            .HasColumnType("jsonb");
            
        modelBuilder.Entity<Customer>()
            .Property(c => c.Metadata)
            .HasColumnType("jsonb");
            
        modelBuilder.Entity<PaymentMethodInfo>()
            .Property(pm => pm.Metadata)
            .HasColumnType("jsonb");
            
        modelBuilder.Entity<Team>()
            .Property(t => t.BusinessInfo)
            .HasColumnType("jsonb");

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
                    break;
                    
                case EntityState.Modified:
                    entry.Entity.UpdatedAt = currentTime;
                    entry.Property(e => e.CreatedAt).IsModified = false; // Prevent modification of CreatedAt
                    break;
            }
        }
    }
}