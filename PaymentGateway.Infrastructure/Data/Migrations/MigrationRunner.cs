using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace PaymentGateway.Infrastructure.Data.Migrations;

public class MigrationRunner
{
    private readonly PaymentGatewayDbContext _context;
    private readonly ILogger<MigrationRunner> _logger;

    public MigrationRunner(PaymentGatewayDbContext context, ILogger<MigrationRunner> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<bool> ApplyMigrationsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Starting database migration process...");

            // Check if database exists and can be reached
            var canConnect = await _context.Database.CanConnectAsync(cancellationToken);
            if (!canConnect)
            {
                _logger.LogError("Cannot connect to database. Migration aborted.");
                return false;
            }

            // Get pending migrations
            var pendingMigrations = await _context.Database.GetPendingMigrationsAsync(cancellationToken);
            var pendingMigrationsList = pendingMigrations.ToList();

            if (!pendingMigrationsList.Any())
            {
                _logger.LogInformation("Database is up to date. No migrations needed.");
                return true;
            }

            _logger.LogInformation("Found {Count} pending migrations: {Migrations}", 
                pendingMigrationsList.Count, string.Join(", ", pendingMigrationsList));

            // Validate migrations before applying
            var validationResult = await ValidateMigrationsAsync(pendingMigrationsList, cancellationToken);
            if (!validationResult.IsValid)
            {
                _logger.LogError("Migration validation failed: {Errors}", 
                    string.Join(", ", validationResult.Errors));
                return false;
            }

            // Apply migrations
            _logger.LogInformation("Applying migrations...");
            await _context.Database.MigrateAsync(cancellationToken);

            // Verify migration success
            var remainingPendingMigrations = await _context.Database.GetPendingMigrationsAsync(cancellationToken);
            if (remainingPendingMigrations.Any())
            {
                _logger.LogError("Migration incomplete. Remaining pending migrations: {Migrations}", 
                    string.Join(", ", remainingPendingMigrations));
                return false;
            }

            _logger.LogInformation("All migrations applied successfully.");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during migration process");
            return false;
        }
    }

    public async Task<MigrationInfo> GetMigrationInfoAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var appliedMigrations = await _context.Database.GetAppliedMigrationsAsync(cancellationToken);
            var pendingMigrations = await _context.Database.GetPendingMigrationsAsync(cancellationToken);
            var canConnect = await _context.Database.CanConnectAsync(cancellationToken);

            return new MigrationInfo
            {
                CanConnect = canConnect,
                AppliedMigrations = appliedMigrations.ToList(),
                PendingMigrations = pendingMigrations.ToList(),
                LastAppliedMigration = appliedMigrations.LastOrDefault(),
                TotalAppliedCount = appliedMigrations.Count(),
                TotalPendingCount = pendingMigrations.Count()
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting migration information");
            return new MigrationInfo
            {
                CanConnect = false,
                Error = ex.Message
            };
        }
    }

    public async Task<bool> RollbackToMigrationAsync(string migrationName, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogWarning("Rolling back to migration: {MigrationName}", migrationName);

            // Validate migration exists
            var appliedMigrations = await _context.Database.GetAppliedMigrationsAsync(cancellationToken);
            if (!appliedMigrations.Contains(migrationName))
            {
                _logger.LogError("Migration {MigrationName} not found in applied migrations", migrationName);
                return false;
            }

            // Perform rollback
            await _context.Database.MigrateAsync(migrationName, cancellationToken);

            _logger.LogInformation("Successfully rolled back to migration: {MigrationName}", migrationName);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during rollback to migration {MigrationName}", migrationName);
            return false;
        }
    }

    private async Task<ValidationResult> ValidateMigrationsAsync(IList<string> migrations, CancellationToken cancellationToken)
    {
        var errors = new List<string>();

        try
        {
            // Check if we can create a temporary connection
            using var connection = _context.Database.GetDbConnection();
            await connection.OpenAsync(cancellationToken);

            // Basic validation - check if migrations exist
            foreach (var migration in migrations)
            {
                if (string.IsNullOrWhiteSpace(migration))
                {
                    errors.Add("Empty migration name found");
                }
            }

            await connection.CloseAsync();
        }
        catch (Exception ex)
        {
            errors.Add($"Database connection validation failed: {ex.Message}");
        }

        return new ValidationResult
        {
            IsValid = !errors.Any(),
            Errors = errors
        };
    }
}

public class MigrationInfo
{
    public bool CanConnect { get; set; }
    public List<string> AppliedMigrations { get; set; } = new();
    public List<string> PendingMigrations { get; set; } = new();
    public string? LastAppliedMigration { get; set; }
    public int TotalAppliedCount { get; set; }
    public int TotalPendingCount { get; set; }
    public string? Error { get; set; }
}

public class ValidationResult
{
    public bool IsValid { get; set; }
    public List<string> Errors { get; set; } = new();
}