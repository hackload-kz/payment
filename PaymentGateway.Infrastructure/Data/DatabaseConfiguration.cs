using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;
using PaymentGateway.Infrastructure.Interceptors;
using PaymentGateway.Infrastructure.Services;
using System.Data;

namespace PaymentGateway.Infrastructure.Data;

public static class DatabaseConfiguration
{
    public static IServiceCollection AddDatabase(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("Database connection string 'DefaultConnection' not found.");

        // Register database interceptors as singletons to avoid scoping issues
        services.AddSingleton<DatabaseMetricsInterceptor>();
        services.AddSingleton<PerformanceInterceptor>();
        services.AddSingleton<ConcurrencyInterceptor>();

        // Configure connection pooling options
        var connectionPoolOptions = configuration.GetSection("Database:ConnectionPool");
        var maxPoolSize = connectionPoolOptions.GetValue<int>("MaxPoolSize", 100);
        var minPoolSize = connectionPoolOptions.GetValue<int>("MinPoolSize", 10);
        var connectionLifetime = connectionPoolOptions.GetValue<int>("ConnectionLifetimeMinutes", 30);
        
        // Build enhanced connection string with pooling parameters
        var builder = new NpgsqlConnectionStringBuilder(connectionString)
        {
            MaxPoolSize = maxPoolSize,
            MinPoolSize = minPoolSize,
            ConnectionLifetime = connectionLifetime * 60, // Convert to seconds
            Pooling = true,
            CommandTimeout = 30,
            Timeout = 15,
            MaxAutoPrepare = 20,
            AutoPrepareMinUsages = 2,
            KeepAlive = 30
        };
        
        var optimizedConnectionString = builder.ToString();

        // Register the Infrastructure DbContext for migrations and data access
        services.AddDbContextPool<PaymentGatewayDbContext>((serviceProvider, options) =>
        {
            options.UseNpgsql(optimizedConnectionString, npgsqlOptions =>
            {
                // Configure PostgreSQL-specific options for concurrency
                npgsqlOptions.EnableRetryOnFailure(
                    maxRetryCount: 3,
                    maxRetryDelay: TimeSpan.FromSeconds(30),
                    errorCodesToAdd: null);

                npgsqlOptions.CommandTimeout(30);
                npgsqlOptions.MigrationsAssembly(typeof(PaymentGatewayDbContext).Assembly.FullName);
                npgsqlOptions.MigrationsHistoryTable("__ef_migrations_history", "payment");
                
                // Enable query splitting for better performance
                npgsqlOptions.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery);
            });

            // Add database interceptors
            var metricsInterceptor = serviceProvider.GetService<DatabaseMetricsInterceptor>();
            var performanceInterceptor = serviceProvider.GetService<PerformanceInterceptor>();
            var concurrencyInterceptor = serviceProvider.GetService<ConcurrencyInterceptor>();

            if (metricsInterceptor != null)
                options.AddInterceptors(metricsInterceptor);
            if (performanceInterceptor != null)
                options.AddInterceptors(performanceInterceptor);
            if (concurrencyInterceptor != null)
                options.AddInterceptors(concurrencyInterceptor);

            // Configure logging and monitoring
            if (environment.IsDevelopment())
            {
                options.EnableSensitiveDataLogging();
                options.EnableDetailedErrors();
                options.LogTo(Console.WriteLine, LogLevel.Information);
            }

            // Performance optimizations for high concurrency
            options.EnableServiceProviderCaching();
            options.EnableSensitiveDataLogging(environment.IsDevelopment());
            
            // Configure tracking behavior for better performance
            options.UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking);
            
            // Suppress pending model changes warning for dynamic seeding
            options.ConfigureWarnings(warnings =>
                warnings.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning));
        }, poolSize: maxPoolSize);

        // Also register the Core DbContext with the same configuration for repositories
        services.AddDbContextPool<PaymentGateway.Core.Data.PaymentGatewayDbContext>((serviceProvider, options) =>
        {
            options.UseNpgsql(optimizedConnectionString, npgsqlOptions =>
            {
                npgsqlOptions.EnableRetryOnFailure(
                    maxRetryCount: 3,
                    maxRetryDelay: TimeSpan.FromSeconds(30),
                    errorCodesToAdd: null);
                npgsqlOptions.CommandTimeout(30);
            });

            if (environment.IsDevelopment())
            {
                options.EnableSensitiveDataLogging();
                options.EnableDetailedErrors();
            }

            options.EnableServiceProviderCaching();
            options.UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking);
            
            // Suppress pending model changes warning for dynamic seeding
            options.ConfigureWarnings(warnings =>
                warnings.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning));
        }, poolSize: maxPoolSize);

        // Register database health checks
        services.AddHealthChecks()
            .AddDbContextCheck<PaymentGatewayDbContext>(
                name: "PaymentGateway-Database",
                failureStatus: Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Degraded,
                tags: new[] { "database", "postgresql" });

        // Register database services
        services.AddScoped<IDatabaseHealthService, DatabaseHealthService>();
        services.AddScoped<IDatabaseMigrationService, DatabaseMigrationService>();
        services.AddScoped<IBatchOperationsService, BatchOperationsService>();
        
        // Register migration services - commented out as services don't exist
        // services.AddScoped<Migrations.MigrationRunner>();
        // services.AddHostedService<Migrations.MigrationMonitoringService>();

        return services;
    }

    public static async Task<IHost> MigrateDatabase(this IHost host)
    {
        using var scope = host.Services.CreateScope();
        try
        {
            var migrationService = scope.ServiceProvider.GetRequiredService<IDatabaseMigrationService>();
            await migrationService.MigrateAsync();
        }
        catch (Exception ex)
        {
            var logger = scope.ServiceProvider.GetRequiredService<ILogger<DatabaseMigrationService>>();
            logger.LogError(ex, "An error occurred while migrating the database");
            throw;
        }

        return host;
    }
}

// Database health service interface
public interface IDatabaseHealthService
{
    Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default);
    Task<DatabaseHealthInfo> GetHealthInfoAsync(CancellationToken cancellationToken = default);
}

// Database migration service interface
public interface IDatabaseMigrationService
{
    Task MigrateAsync(CancellationToken cancellationToken = default);
    Task<bool> CanConnectAsync(CancellationToken cancellationToken = default);
    Task<IEnumerable<string>> GetPendingMigrationsAsync(CancellationToken cancellationToken = default);
}

// Database health information
public record DatabaseHealthInfo(
    bool IsHealthy,
    string ConnectionString,
    string DatabaseName,
    int ActiveConnections,
    TimeSpan ResponseTime,
    string? ErrorMessage = null);

// Database health service implementation
public class DatabaseHealthService : IDatabaseHealthService
{
    private readonly PaymentGatewayDbContext _context;
    private readonly ILogger<DatabaseHealthService> _logger;

    public DatabaseHealthService(PaymentGatewayDbContext context, ILogger<DatabaseHealthService> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var startTime = DateTime.UtcNow;
            await _context.Database.CanConnectAsync(cancellationToken);
            var endTime = DateTime.UtcNow;
            
            var responseTime = endTime - startTime;
            _logger.LogInformation("Database health check successful. Response time: {ResponseTime}ms", responseTime.TotalMilliseconds);
            
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Database health check failed");
            return false;
        }
    }

    public async Task<DatabaseHealthInfo> GetHealthInfoAsync(CancellationToken cancellationToken = default)
    {
        var startTime = DateTime.UtcNow;
        try
        {
            var canConnect = await _context.Database.CanConnectAsync(cancellationToken);
            var endTime = DateTime.UtcNow;
            var responseTime = endTime - startTime;

            if (!canConnect)
            {
                return new DatabaseHealthInfo(false, GetConnectionStringWithoutPassword(), "", 0, responseTime, "Cannot connect to database");
            }

            // Get active connections count
            var activeConnections = await GetActiveConnectionsAsync(cancellationToken);
            var databaseName = _context.Database.GetConnectionString()?.Split(';')
                .FirstOrDefault(x => x.Contains("Database="))?.Split('=')[1] ?? "Unknown";

            return new DatabaseHealthInfo(true, GetConnectionStringWithoutPassword(), databaseName, activeConnections, responseTime);
        }
        catch (Exception ex)
        {
            var endTime = DateTime.UtcNow;
            var responseTime = endTime - startTime;
            _logger.LogError(ex, "Failed to get database health information");
            return new DatabaseHealthInfo(false, GetConnectionStringWithoutPassword(), "", 0, responseTime, ex.Message);
        }
    }

    private async Task<int> GetActiveConnectionsAsync(CancellationToken cancellationToken)
    {
        try
        {
            var connection = _context.Database.GetDbConnection();
            if (connection.State != ConnectionState.Open)
            {
                await connection.OpenAsync(cancellationToken);
            }

            using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM pg_stat_activity WHERE state = 'active'";
            var result = await command.ExecuteScalarAsync(cancellationToken);
            
            return Convert.ToInt32(result);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get active connections count");
            return 0;
        }
    }

    private string GetConnectionStringWithoutPassword()
    {
        var connectionString = _context.Database.GetConnectionString() ?? "";
        var builder = new NpgsqlConnectionStringBuilder(connectionString) { Password = "***" };
        return builder.ToString();
    }
}

// Database migration service implementation
public class DatabaseMigrationService : IDatabaseMigrationService
{
    private readonly PaymentGatewayDbContext _context;
    private readonly ILogger<DatabaseMigrationService> _logger;

    public DatabaseMigrationService(PaymentGatewayDbContext context, ILogger<DatabaseMigrationService> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task MigrateAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Starting database migration...");

            // Check if database can be reached
            var canConnect = await CanConnectAsync(cancellationToken);
            if (!canConnect)
            {
                throw new InvalidOperationException("Cannot connect to the database for migration");
            }

            // Get pending migrations
            var pendingMigrations = await GetPendingMigrationsAsync(cancellationToken);
            var pendingMigrationsList = pendingMigrations.ToList();

            if (pendingMigrationsList.Any())
            {
                _logger.LogInformation("Found {Count} pending migrations: {Migrations}", 
                    pendingMigrationsList.Count, string.Join(", ", pendingMigrationsList));

                // Apply migrations
                await _context.Database.MigrateAsync(cancellationToken);
                
                _logger.LogInformation("Database migration completed successfully");
            }
            else
            {
                _logger.LogInformation("Database is up to date, no migrations needed");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Database migration failed");
            throw;
        }
    }

    public async Task<bool> CanConnectAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await _context.Database.CanConnectAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Cannot connect to database");
            return false;
        }
    }

    public async Task<IEnumerable<string>> GetPendingMigrationsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await _context.Database.GetPendingMigrationsAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get pending migrations");
            return Array.Empty<string>();
        }
    }
}