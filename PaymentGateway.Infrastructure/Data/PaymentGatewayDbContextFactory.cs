using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace PaymentGateway.Infrastructure.Data;

/// <summary>
/// Design-time factory for PaymentGatewayDbContext to support EF migrations
/// </summary>
public class PaymentGatewayDbContextFactory : IDesignTimeDbContextFactory<PaymentGatewayDbContext>
{
    public PaymentGatewayDbContext CreateDbContext(string[] args)
    {
        // Build configuration
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Path.Combine(Directory.GetCurrentDirectory(), "../PaymentGateway.API"))
            .AddJsonFile("appsettings.json", optional: false)
            .AddJsonFile("appsettings.Development.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        // Get connection string
        var connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? "Host=localhost;Port=5432;Database=paymentgateway;Username=postgres;Password=postgres";

        // Create options builder
        var optionsBuilder = new DbContextOptionsBuilder<PaymentGatewayDbContext>();
        
        // Configure with PostgreSQL without interceptors (for design time)
        optionsBuilder.UseNpgsql(connectionString, npgsqlOptions =>
        {
            npgsqlOptions.MigrationsAssembly(typeof(PaymentGatewayDbContext).Assembly.GetName().Name);
            npgsqlOptions.EnableRetryOnFailure(
                maxRetryCount: 3,
                maxRetryDelay: TimeSpan.FromSeconds(5),
                errorCodesToAdd: null);
        });

        // Enable sensitive data logging in development
        if (args.Contains("--verbose"))
        {
            optionsBuilder.EnableSensitiveDataLogging();
            optionsBuilder.EnableDetailedErrors();
        }

        return new PaymentGatewayDbContext(optionsBuilder.Options);
    }
}