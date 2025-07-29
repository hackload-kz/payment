using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PaymentGateway.Infrastructure.Data;
using PaymentGateway.Infrastructure.Data.Migrations;
using Xunit;
using Microsoft.Extensions.DependencyInjection;

namespace PaymentGateway.Tests.Infrastructure;

public class MigrationRunnerTests : IDisposable
{
    private readonly PaymentGatewayDbContext _context;
    private readonly MigrationRunner _migrationRunner;
    private readonly ILogger<MigrationRunner> _logger;

    public MigrationRunnerTests()
    {
        // Create in-memory database for testing
        var options = new DbContextOptionsBuilder<PaymentGatewayDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _context = new PaymentGatewayDbContext(options);
        
        // Create logger
        var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        _logger = loggerFactory.CreateLogger<MigrationRunner>();
        
        _migrationRunner = new MigrationRunner(_context, _logger);
    }

    [Fact]
    public async Task GetMigrationInfoAsync_WithInMemoryDb_ShouldReturnBasicInfo()
    {
        // Act
        var migrationInfo = await _migrationRunner.GetMigrationInfoAsync();

        // Assert
        Assert.NotNull(migrationInfo);
        // In-memory database doesn't support migrations, so this might fail
        // But we should still get a migration info object
        Assert.NotNull(migrationInfo.AppliedMigrations);
        Assert.NotNull(migrationInfo.PendingMigrations);
    }

    [Fact]
    public async Task GetMigrationInfoAsync_WithInvalidContext_ShouldHandleError()
    {
        // Arrange
        var invalidOptions = new DbContextOptionsBuilder<PaymentGatewayDbContext>()
            .UseNpgsql("Host=invalid;Database=invalid;Username=invalid;Password=invalid")
            .Options;

        using var invalidContext = new PaymentGatewayDbContext(invalidOptions);
        var migrationRunner = new MigrationRunner(invalidContext, _logger);

        // Act
        var migrationInfo = await migrationRunner.GetMigrationInfoAsync();

        // Assert
        Assert.NotNull(migrationInfo);
        Assert.False(migrationInfo.CanConnect);
        Assert.NotNull(migrationInfo.Error);
    }

    public void Dispose()
    {
        _context.Dispose();
    }
}