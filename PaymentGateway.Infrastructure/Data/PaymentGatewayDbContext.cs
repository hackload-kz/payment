using Microsoft.EntityFrameworkCore;
using PaymentGateway.Core.Entities;
using PaymentGateway.Core.Interfaces;
using PaymentGateway.Infrastructure.Data.Seed;

namespace PaymentGateway.Infrastructure.Data;

public class PaymentGatewayDbContext : DbContext
{
    public PaymentGatewayDbContext(DbContextOptions<PaymentGatewayDbContext> options) : base(options)
    {
    }

    // DbSets
    public DbSet<Payment> Payments { get; set; } = null!;
    public DbSet<Team> Teams { get; set; } = null!;
    public DbSet<Customer> Customers { get; set; } = null!;
    public DbSet<Transaction> Transactions { get; set; } = null!;
    public DbSet<PaymentMethodInfo> PaymentMethods { get; set; } = null!;
    public DbSet<AuditLog> AuditLogs { get; set; } = null!;
    public DbSet<AuditEntry> AuditEntries { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Apply all configurations from the assembly
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(PaymentGatewayDbContext).Assembly);

        // Configure database-specific settings
        ConfigureDatabase(modelBuilder);

        // Seed initial data
        SeedData.Seed(modelBuilder);
    }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        base.OnConfiguring(optionsBuilder);

        // Enable sensitive data logging in development
        if (System.Diagnostics.Debugger.IsAttached)
        {
            optionsBuilder.EnableSensitiveDataLogging();
        }

        // Enable detailed errors
        optionsBuilder.EnableDetailedErrors();

        // Configure connection pooling and resilience
        optionsBuilder.EnableServiceProviderCaching();
        optionsBuilder.EnableSensitiveDataLogging(false); // Disable in production
    }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        // Add audit information before saving
        AddAuditInformation();
        
        return await base.SaveChangesAsync(cancellationToken);
    }

    public override int SaveChanges()
    {
        // Add audit information before saving
        AddAuditInformation();
        
        return base.SaveChanges();
    }

    private void AddAuditInformation()
    {
        var entries = ChangeTracker.Entries()
            .Where(e => e.State == EntityState.Added || e.State == EntityState.Modified);

        var utcNow = DateTime.UtcNow;

        foreach (var entry in entries)
        {
            // Set audit fields if entity implements IAuditableEntity
            if (entry.Entity is IAuditableEntity auditableEntity)
            {
                if (entry.State == EntityState.Added)
                {
                    auditableEntity.CreatedAt = utcNow;
                    auditableEntity.UpdatedAt = utcNow;
                }
                else if (entry.State == EntityState.Modified)
                {
                    auditableEntity.UpdatedAt = utcNow;
                }
            }

            // Set row version for optimistic concurrency
            if (entry.Entity is IVersionedEntity versionedEntity && entry.State == EntityState.Modified)
            {
                entry.OriginalValues[nameof(IVersionedEntity.RowVersion)] = versionedEntity.RowVersion;
            }
        }
    }

    private void ConfigureDatabase(ModelBuilder modelBuilder)
    {
        // Configure PostgreSQL-specific settings
        modelBuilder.HasDefaultSchema("payment");

        // Configure naming conventions (snake_case for PostgreSQL)
        foreach (var entity in modelBuilder.Model.GetEntityTypes())
        {
            // Convert table names to snake_case
            entity.SetTableName(entity.GetTableName()?.ToSnakeCase());

            // Convert column names to snake_case
            foreach (var property in entity.GetProperties())
            {
                property.SetColumnName(property.GetColumnName().ToSnakeCase());
            }

            // Convert key names to snake_case
            foreach (var key in entity.GetKeys())
            {
                key.SetName(key.GetName()?.ToSnakeCase());
            }

            // Convert foreign key names to snake_case
            foreach (var foreignKey in entity.GetForeignKeys())
            {
                foreignKey.SetConstraintName(foreignKey.GetConstraintName()?.ToSnakeCase());
            }

            // Convert index names to snake_case
            foreach (var index in entity.GetIndexes())
            {
                index.SetDatabaseName(index.GetDatabaseName()?.ToSnakeCase());
            }
        }
    }
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