using Microsoft.AspNetCore.Mvc;
using PaymentGateway.Infrastructure.Data.Migrations;

namespace PaymentGateway.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class MigrationController : ControllerBase
{
    private readonly MigrationRunner _migrationRunner;
    private readonly ILogger<MigrationController> _logger;

    public MigrationController(MigrationRunner migrationRunner, ILogger<MigrationController> logger)
    {
        _migrationRunner = migrationRunner;
        _logger = logger;
    }

    /// <summary>
    /// Get current migration status
    /// </summary>
    [HttpGet("status")]
    public async Task<ActionResult<MigrationStatusResponse>> GetStatus()
    {
        try
        {
            var migrationInfo = await _migrationRunner.GetMigrationInfoAsync();
            
            return Ok(new MigrationStatusResponse
            {
                CanConnect = migrationInfo.CanConnect,
                AppliedMigrations = migrationInfo.AppliedMigrations,
                PendingMigrations = migrationInfo.PendingMigrations,
                LastAppliedMigration = migrationInfo.LastAppliedMigration,
                TotalAppliedCount = migrationInfo.TotalAppliedCount,
                TotalPendingCount = migrationInfo.TotalPendingCount,
                IsUpToDate = migrationInfo.TotalPendingCount == 0,
                Error = migrationInfo.Error
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting migration status");
            return StatusCode(500, new { error = "Failed to get migration status", details = ex.Message });
        }
    }

    /// <summary>
    /// Apply pending migrations
    /// </summary>
    [HttpPost("apply")]
    public async Task<ActionResult<MigrationOperationResponse>> ApplyMigrations()
    {
        try
        {
            _logger.LogInformation("Migration apply requested via API");
            
            var success = await _migrationRunner.ApplyMigrationsAsync();
            
            if (success)
            {
                return Ok(new MigrationOperationResponse
                {
                    Success = true,
                    Message = "Migrations applied successfully"
                });
            }
            else
            {
                return BadRequest(new MigrationOperationResponse
                {
                    Success = false,
                    Message = "Failed to apply migrations"
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error applying migrations");
            return StatusCode(500, new MigrationOperationResponse
            {
                Success = false,
                Message = "Error applying migrations",
                Error = ex.Message
            });
        }
    }

    /// <summary>
    /// Rollback to a specific migration
    /// </summary>
    [HttpPost("rollback/{migrationName}")]
    public async Task<ActionResult<MigrationOperationResponse>> RollbackToMigration(string migrationName)
    {
        try
        {
            _logger.LogWarning("Migration rollback requested via API to migration: {MigrationName}", migrationName);
            
            if (string.IsNullOrWhiteSpace(migrationName))
            {
                return BadRequest(new MigrationOperationResponse
                {
                    Success = false,
                    Message = "Migration name is required"
                });
            }

            var success = await _migrationRunner.RollbackToMigrationAsync(migrationName);
            
            if (success)
            {
                return Ok(new MigrationOperationResponse
                {
                    Success = true,
                    Message = $"Successfully rolled back to migration: {migrationName}"
                });
            }
            else
            {
                return BadRequest(new MigrationOperationResponse
                {
                    Success = false,
                    Message = $"Failed to rollback to migration: {migrationName}"
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error rolling back to migration {MigrationName}", migrationName);
            return StatusCode(500, new MigrationOperationResponse
            {
                Success = false,
                Message = "Error during rollback",
                Error = ex.Message
            });
        }
    }
}

public class MigrationStatusResponse
{
    public bool CanConnect { get; set; }
    public List<string> AppliedMigrations { get; set; } = new();
    public List<string> PendingMigrations { get; set; } = new();
    public string? LastAppliedMigration { get; set; }
    public int TotalAppliedCount { get; set; }
    public int TotalPendingCount { get; set; }
    public bool IsUpToDate { get; set; }
    public string? Error { get; set; }
}

public class MigrationOperationResponse
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? Error { get; set; }
}