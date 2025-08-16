using Microsoft.AspNetCore.Mvc;
using PaymentGateway.Core.Services;
using System.ComponentModel.DataAnnotations;
using Prometheus;

namespace PaymentGateway.API.Controllers;

/// <summary>
/// Admin API controller for administrative operations
/// </summary>
[ApiController]
[Route("api/v1/[controller]")]
[Produces("application/json")]
[Tags("Admin")]
public class AdminController : ControllerBase
{
    private readonly IAdminAuthenticationService _adminAuthService;
    private readonly IAdminDataClearService _dataClearService;
    private readonly ILogger<AdminController> _logger;

    // Metrics for monitoring
    private static readonly Counter AdminOperations = Metrics
        .CreateCounter("admin_operations_total", "Total admin operations", new[] { "operation", "result" });

    private static readonly Histogram AdminOperationDuration = Metrics
        .CreateHistogram("admin_operation_duration_seconds", "Admin operation duration", new[] { "operation" });

    public AdminController(
        IAdminAuthenticationService adminAuthService,
        IAdminDataClearService dataClearService,
        ILogger<AdminController> logger)
    {
        _adminAuthService = adminAuthService;
        _dataClearService = dataClearService;
        _logger = logger;
    }

    /// <summary>
    /// Clear database tables while preserving team/merchant data
    /// 
    /// This endpoint allows administrators to clear payment, transaction, and order data
    /// from the database while preserving team/merchant information. This is useful for
    /// cleaning up test data or resetting the system for new environments.
    /// 
    /// ## Security:
    /// - Requires admin token via one of these methods:
    ///   - Authorization header: `Bearer {admin-token}`
    ///   - Custom header: `X-Admin-Token: {admin-token}`
    /// - Admin token must be configured in application settings
    /// - All operations are logged for audit purposes
    /// 
    /// ## Data Cleared:
    /// - **Payments**: All payment records and their lifecycle data
    /// - **Transactions**: All transaction records and processing details
    /// - **Orders**: Order data embedded within payment records (OrderId field)
    /// - **Related Audit Logs**: Audit trails for cleared payment/transaction data
    /// 
    /// ## Data Preserved:
    /// - **Teams**: Merchant/team configurations and settings
    /// - **Customers**: Customer profile information
    /// - **System Configuration**: Application settings and configurations
    /// 
    /// ## Response:
    /// Returns statistics about the clearing operation including counts of deleted records
    /// and operation timing information.
    /// </summary>
    /// <returns>Statistics about the database clearing operation</returns>
    /// <response code="200">Database cleared successfully</response>
    /// <response code="401">Invalid or missing admin token</response>
    /// <response code="403">Admin functionality not configured</response>
    /// <response code="500">Internal server error during clearing operation</response>
    [HttpPost("clear-database")]
    [ProducesResponseType(typeof(AdminDataClearResponse), 200)]
    [ProducesResponseType(typeof(ErrorResponse), 401)]
    [ProducesResponseType(typeof(ErrorResponse), 403)]
    [ProducesResponseType(typeof(ErrorResponse), 500)]
    public async Task<ActionResult<AdminDataClearResponse>> ClearDatabase(
        CancellationToken cancellationToken = default)
    {
        using var timer = AdminOperationDuration.WithLabels("clear_database").NewTimer();

        try
        {
            // Check if admin functionality is configured
            if (!_adminAuthService.IsAdminTokenConfigured())
            {
                AdminOperations.WithLabels("clear_database", "forbidden").Inc();
                _logger.LogWarning("Admin database clear attempted but admin token not configured");
                
                return StatusCode(403, new ErrorResponse
                {
                    Error = "Admin functionality not configured",
                    Message = "Admin token must be configured in application settings to use admin endpoints"
                });
            }

            // Validate admin token - check both Bearer token and custom header
            string? token = null;
            
            // Try Bearer token first
            var authHeader = Request.Headers.Authorization.FirstOrDefault();
            if (!string.IsNullOrEmpty(authHeader) && authHeader.StartsWith("Bearer "))
            {
                token = authHeader["Bearer ".Length..];
            }
            
            // If no Bearer token, try custom header
            if (string.IsNullOrEmpty(token))
            {
                var headerName = _adminAuthService.GetAdminTokenHeaderName();
                token = Request.Headers[headerName].FirstOrDefault();
            }
            
            if (string.IsNullOrEmpty(token))
            {
                AdminOperations.WithLabels("clear_database", "unauthorized").Inc();
                _logger.LogWarning("Admin database clear attempted without admin token");
                
                return Unauthorized(new ErrorResponse
                {
                    Error = "Missing Authorization",
                    Message = $"Admin token required. Use Authorization header with Bearer token or {_adminAuthService.GetAdminTokenHeaderName()} header."
                });
            }

            if (!_adminAuthService.ValidateAdminToken(token))
            {
                AdminOperations.WithLabels("clear_database", "unauthorized").Inc();
                _logger.LogWarning("Admin database clear attempted with invalid token from IP {RemoteIP}", 
                    Request.HttpContext.Connection.RemoteIpAddress);
                
                return Unauthorized(new ErrorResponse
                {
                    Error = "Invalid Token",
                    Message = "Invalid admin token provided"
                });
            }

            _logger.LogWarning("Admin database clear operation initiated by IP {RemoteIP}", 
                Request.HttpContext.Connection.RemoteIpAddress);

            // Perform the clear operation
            var result = await _dataClearService.ClearDatabaseAsync(cancellationToken);

            if (result.Success)
            {
                AdminOperations.WithLabels("clear_database", "success").Inc();
                
                var response = new AdminDataClearResponse
                {
                    Success = true,
                    Message = "Database cleared successfully",
                    Statistics = new DatabaseClearStatistics
                    {
                        DeletedPayments = result.DeletedPayments,
                        DeletedTransactions = result.DeletedTransactions,
                        DeletedOrders = result.DeletedOrders,
                        OperationDurationMs = (int)result.OperationDuration.TotalMilliseconds,
                        ClearTimestamp = result.ClearTimestamp
                    }
                };

                _logger.LogWarning("Admin database clear completed successfully: {Statistics}", 
                    System.Text.Json.JsonSerializer.Serialize(response.Statistics));

                return Ok(response);
            }
            else
            {
                AdminOperations.WithLabels("clear_database", "error").Inc();
                
                _logger.LogError("Admin database clear operation failed: {ErrorMessage}", result.ErrorMessage);
                
                return StatusCode(500, new ErrorResponse
                {
                    Error = "Clear Operation Failed",
                    Message = result.ErrorMessage ?? "Unknown error occurred during database clear operation"
                });
            }
        }
        catch (Exception ex)
        {
            AdminOperations.WithLabels("clear_database", "error").Inc();
            _logger.LogError(ex, "Unexpected error during admin database clear operation");
            
            return StatusCode(500, new ErrorResponse
            {
                Error = "Internal Server Error",
                Message = "An unexpected error occurred during the clear operation"
            });
        }
    }

    /// <summary>
    /// Clear data for a specific team/merchant
    /// 
    /// This endpoint allows administrators to clear payment, transaction, and order data
    /// for a specific team while preserving the team configuration and data for other teams.
    /// This is useful for cleaning up test data for a specific merchant.
    /// 
    /// ## Security:
    /// - Requires admin token via one of these methods:
    ///   - Authorization header: `Bearer {admin-token}`
    ///   - Custom header: `X-Admin-Token: {admin-token}`
    /// - Admin token must be configured in application settings
    /// - All operations are logged for audit purposes
    /// 
    /// ## Data Cleared (for specified team only):
    /// - **Payments**: All payment records for the team
    /// - **Transactions**: All transaction records related to team's payments
    /// - **Orders**: Order data embedded within payment records (OrderId field)
    /// 
    /// ## Data Preserved:
    /// - **Team Configuration**: Team/merchant settings and configuration
    /// - **Other Teams**: Data for all other teams remains untouched
    /// - **Customers**: Customer profile information
    /// - **System Configuration**: Application settings and configurations
    /// </summary>
    /// <param name="teamSlug">The team slug identifier</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Statistics about the team data clearing operation</returns>
    /// <response code="200">Team data cleared successfully</response>
    /// <response code="401">Invalid or missing admin token</response>
    /// <response code="403">Admin functionality not configured</response>
    /// <response code="404">Team not found</response>
    /// <response code="500">Internal server error during clearing operation</response>
    [HttpPost("clear-team-data/{teamSlug}")]
    [ProducesResponseType(typeof(AdminDataClearResponse), 200)]
    [ProducesResponseType(typeof(ErrorResponse), 401)]
    [ProducesResponseType(typeof(ErrorResponse), 403)]
    [ProducesResponseType(typeof(ErrorResponse), 404)]
    [ProducesResponseType(typeof(ErrorResponse), 500)]
    public async Task<ActionResult<AdminDataClearResponse>> ClearTeamData(
        [FromRoute] [Required] string teamSlug,
        CancellationToken cancellationToken = default)
    {
        using var timer = AdminOperationDuration.WithLabels("clear_team_data").NewTimer();

        try
        {
            // Check if admin functionality is configured
            if (!_adminAuthService.IsAdminTokenConfigured())
            {
                AdminOperations.WithLabels("clear_team_data", "forbidden").Inc();
                _logger.LogWarning("Admin team data clear attempted but admin token not configured for team {TeamSlug}", teamSlug);
                
                return StatusCode(403, new ErrorResponse
                {
                    Error = "Admin functionality not configured",
                    Message = "Admin token must be configured in application settings to use admin endpoints"
                });
            }

            // Validate admin token - check both Bearer token and custom header
            string? token = null;
            
            // Try Bearer token first
            var authHeader = Request.Headers.Authorization.FirstOrDefault();
            if (!string.IsNullOrEmpty(authHeader) && authHeader.StartsWith("Bearer "))
            {
                token = authHeader["Bearer ".Length..];
            }
            
            // If no Bearer token, try custom header
            if (string.IsNullOrEmpty(token))
            {
                var headerName = _adminAuthService.GetAdminTokenHeaderName();
                token = Request.Headers[headerName].FirstOrDefault();
            }
            
            if (string.IsNullOrEmpty(token))
            {
                AdminOperations.WithLabels("clear_team_data", "unauthorized").Inc();
                _logger.LogWarning("Admin team data clear attempted without admin token for team {TeamSlug}", teamSlug);
                
                return Unauthorized(new ErrorResponse
                {
                    Error = "Missing Authorization",
                    Message = $"Admin token required. Use Authorization header with Bearer token or {_adminAuthService.GetAdminTokenHeaderName()} header."
                });
            }

            if (!_adminAuthService.ValidateAdminToken(token))
            {
                AdminOperations.WithLabels("clear_team_data", "unauthorized").Inc();
                _logger.LogWarning("Admin team data clear attempted with invalid token from IP {RemoteIP} for team {TeamSlug}", 
                    Request.HttpContext.Connection.RemoteIpAddress, teamSlug);
                
                return Unauthorized(new ErrorResponse
                {
                    Error = "Invalid Token",
                    Message = "Invalid admin token provided"
                });
            }

            _logger.LogWarning("Admin team data clear operation initiated by IP {RemoteIP} for team {TeamSlug}", 
                Request.HttpContext.Connection.RemoteIpAddress, teamSlug);

            // Perform the clear operation for specific team
            var result = await _dataClearService.ClearTeamDataAsync(teamSlug, cancellationToken);

            if (result.Success)
            {
                AdminOperations.WithLabels("clear_team_data", "success").Inc();
                
                var response = new AdminDataClearResponse
                {
                    Success = true,
                    Message = $"Team data cleared successfully for '{teamSlug}'",
                    Statistics = new DatabaseClearStatistics
                    {
                        DeletedPayments = result.DeletedPayments,
                        DeletedTransactions = result.DeletedTransactions,
                        DeletedOrders = result.DeletedOrders,
                        OperationDurationMs = (int)result.OperationDuration.TotalMilliseconds,
                        ClearTimestamp = result.ClearTimestamp
                    }
                };

                _logger.LogWarning("Admin team data clear completed successfully for team {TeamSlug}: {Statistics}", 
                    teamSlug, System.Text.Json.JsonSerializer.Serialize(response.Statistics));

                return Ok(response);
            }
            else
            {
                // Check if the error was due to team not found
                if (result.ErrorMessage?.Contains("not found") == true)
                {
                    AdminOperations.WithLabels("clear_team_data", "not_found").Inc();
                    
                    return NotFound(new ErrorResponse
                    {
                        Error = "Team Not Found",
                        Message = result.ErrorMessage
                    });
                }
                
                AdminOperations.WithLabels("clear_team_data", "error").Inc();
                
                _logger.LogError("Admin team data clear operation failed for team {TeamSlug}: {ErrorMessage}", teamSlug, result.ErrorMessage);
                
                return StatusCode(500, new ErrorResponse
                {
                    Error = "Clear Operation Failed",
                    Message = result.ErrorMessage ?? "Unknown error occurred during team data clear operation"
                });
            }
        }
        catch (Exception ex)
        {
            AdminOperations.WithLabels("clear_team_data", "error").Inc();
            _logger.LogError(ex, "Unexpected error during admin team data clear operation for team {TeamSlug}", teamSlug);
            
            return StatusCode(500, new ErrorResponse
            {
                Error = "Internal Server Error",
                Message = "An unexpected error occurred during the clear operation"
            });
        }
    }

    /// <summary>
    /// Check admin service status and configuration
    /// </summary>
    /// <returns>Admin service status information</returns>
    [HttpGet("status")]
    [ProducesResponseType(typeof(AdminStatusResponse), 200)]
    public ActionResult<AdminStatusResponse> GetStatus()
    {
        return Ok(new AdminStatusResponse
        {
            AdminTokenConfigured = _adminAuthService.IsAdminTokenConfigured(),
            ServiceVersion = "1.0",
            ServerTime = DateTime.UtcNow
        });
    }
}

/// <summary>
/// Response model for database clear operation
/// </summary>
public class AdminDataClearResponse
{
    /// <summary>
    /// Whether the operation was successful
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Operation result message
    /// </summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// Statistics about the clearing operation
    /// </summary>
    public DatabaseClearStatistics? Statistics { get; set; }
}

/// <summary>
/// Statistics about database clearing operation
/// </summary>
public class DatabaseClearStatistics
{
    /// <summary>
    /// Number of payment records deleted
    /// </summary>
    public int DeletedPayments { get; set; }

    /// <summary>
    /// Number of transaction records deleted
    /// </summary>
    public int DeletedTransactions { get; set; }

    /// <summary>
    /// Number of unique orders deleted (based on OrderId field in payments)
    /// </summary>
    public int DeletedOrders { get; set; }

    /// <summary>
    /// Operation duration in milliseconds
    /// </summary>
    public int OperationDurationMs { get; set; }

    /// <summary>
    /// Timestamp when the clear operation was initiated
    /// </summary>
    public DateTime ClearTimestamp { get; set; }
}

/// <summary>
/// Response model for admin status endpoint
/// </summary>
public class AdminStatusResponse
{
    /// <summary>
    /// Whether admin token is configured
    /// </summary>
    public bool AdminTokenConfigured { get; set; }

    /// <summary>
    /// Admin service version
    /// </summary>
    public string ServiceVersion { get; set; } = string.Empty;

    /// <summary>
    /// Current server time
    /// </summary>
    public DateTime ServerTime { get; set; }
}

/// <summary>
/// Error response model
/// </summary>
public class ErrorResponse
{
    /// <summary>
    /// Error type
    /// </summary>
    public string Error { get; set; } = string.Empty;

    /// <summary>
    /// Error message
    /// </summary>
    public string Message { get; set; } = string.Empty;
}