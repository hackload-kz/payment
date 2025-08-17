using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using PaymentGateway.Core.Configuration;

namespace PaymentGateway.API.Controllers;

/// <summary>
/// Controller for serving the Team Management Dashboard web interface
/// </summary>
[ApiController]
[Route("team")]
[Tags("Team Dashboard")]
public class TeamDashboardController : ControllerBase
{
    private readonly ILogger<TeamDashboardController> _logger;
    private readonly IWebHostEnvironment _environment;
    private readonly ApiOptions _apiOptions;

    public TeamDashboardController(
        ILogger<TeamDashboardController> logger,
        IWebHostEnvironment environment,
        IOptions<ApiOptions> apiOptions)
    {
        _logger = logger;
        _environment = environment;
        _apiOptions = apiOptions.Value;
    }

    /// <summary>
    /// Serves the Team Management Dashboard web interface
    /// 
    /// This endpoint provides a user-friendly web interface for teams to:
    /// - View their team information and configuration
    /// - Edit team settings and payment limits
    /// - Monitor usage statistics
    /// - Manage URLs and webhook settings
    /// 
    /// ## Features:
    /// - **Authentication**: Secure login using team slug and password
    /// - **Real-time Data**: Live updates from the Team Management API
    /// - **Responsive Design**: Works on desktop, tablet, and mobile devices
    /// - **Professional UI**: Modern, clean interface with intuitive navigation
    /// 
    /// ## Security:
    /// - Session-based authentication (credentials stored in sessionStorage)
    /// - All API calls use Basic Auth with team credentials
    /// - Automatic logout on authentication failure
    /// - HTTPS recommended for production use
    /// 
    /// ## Browser Support:
    /// - Modern browsers with ES6+ support
    /// - Chrome 60+, Firefox 55+, Safari 12+, Edge 79+
    /// </summary>
    /// <returns>Team Management Dashboard HTML page</returns>
    /// <response code="200">Dashboard page served successfully</response>
    /// <response code="404">Dashboard page not found</response>
    /// <response code="500">Internal server error</response>
    [HttpGet("dashboard")]
    [HttpGet("")]
    [Produces("text/html")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> Dashboard()
    {
        try
        {
            var requestId = Guid.NewGuid().ToString();
            var clientIp = Request.HttpContext.Connection.RemoteIpAddress?.ToString();
            
            _logger.LogInformation("Team dashboard page requested. RequestId: {RequestId}, ClientIP: {ClientIP}", 
                requestId, clientIp);

            var dashboardPath = Path.Combine(_environment.ContentRootPath, "Views", "TeamManagement", "Dashboard.html");
            
            // Check if the dashboard file exists
            if (!System.IO.File.Exists(dashboardPath))
            {
                _logger.LogWarning("Dashboard page not found at path: {Path}. RequestId: {RequestId}", 
                    dashboardPath, requestId);
                return NotFound("Team dashboard page not found");
            }

            // Read and serve the HTML file
            var htmlContent = await System.IO.File.ReadAllTextAsync(dashboardPath);
            
            // Replace template placeholders
            // Use ApiOptions.BaseUrl if available, otherwise construct from request
            var basePath = !string.IsNullOrEmpty(_apiOptions.BaseUrl) ? 
                _apiOptions.BaseUrl.TrimEnd('/') + "/" : 
                $"{Request.Scheme}://{Request.Host}{Request.PathBase}/";
            
            htmlContent = htmlContent.Replace("{{BasePath}}", basePath);
            
            // Add cache headers for static content
            Response.Headers.Append("Cache-Control", "no-cache, no-store, must-revalidate");
            Response.Headers.Append("Pragma", "no-cache");
            Response.Headers.Append("Expires", "0");
            
            // Security headers
            Response.Headers.Append("X-Content-Type-Options", "nosniff");
            Response.Headers.Append("X-Frame-Options", "DENY");
            Response.Headers.Append("X-XSS-Protection", "1; mode=block");
            Response.Headers.Append("Referrer-Policy", "strict-origin-when-cross-origin");
            
            _logger.LogInformation("Team dashboard page served successfully. RequestId: {RequestId}", requestId);
            
            return Content(htmlContent, "text/html");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error serving team dashboard page");
            return StatusCode(500, "Internal server error while loading dashboard");
        }
    }

    /// <summary>
    /// Health check endpoint for the dashboard service
    /// 
    /// This endpoint can be used to verify that the dashboard service is running
    /// and that required files are accessible.
    /// </summary>
    /// <returns>Health status information</returns>
    /// <response code="200">Service is healthy</response>
    /// <response code="503">Service is unhealthy</response>
    [HttpGet("health")]
    [Produces("application/json")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public IActionResult Health()
    {
        try
        {
            var dashboardPath = Path.Combine(_environment.ContentRootPath, "Views", "TeamManagement", "Dashboard.html");
            var cssPath = Path.Combine(_environment.WebRootPath, "css", "team-dashboard.css");
            var jsPath = Path.Combine(_environment.WebRootPath, "js", "team-dashboard.js");

            var health = new
            {
                Status = "Healthy",
                Timestamp = DateTime.UtcNow,
                Service = "Team Dashboard",
                Version = "1.0.0",
                Files = new
                {
                    Dashboard = System.IO.File.Exists(dashboardPath),
                    CSS = System.IO.File.Exists(cssPath),
                    JavaScript = System.IO.File.Exists(jsPath)
                },
                Environment = _environment.EnvironmentName
            };

            var allFilesExist = System.IO.File.Exists(dashboardPath) && 
                               System.IO.File.Exists(cssPath) && 
                               System.IO.File.Exists(jsPath);

            if (!allFilesExist)
            {
                _logger.LogWarning("Dashboard health check failed - missing files. Dashboard: {Dashboard}, CSS: {CSS}, JS: {JS}",
                    System.IO.File.Exists(dashboardPath), System.IO.File.Exists(cssPath), System.IO.File.Exists(jsPath));
                
                return StatusCode(503, new { 
                    Status = "Unhealthy", 
                    Reason = "Missing required files",
                    Details = health 
                });
            }

            return Ok(health);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during dashboard health check");
            return StatusCode(503, new { 
                Status = "Unhealthy", 
                Reason = "Health check failed",
                Error = ex.Message 
            });
        }
    }

    /// <summary>
    /// Get dashboard configuration and metadata
    /// 
    /// This endpoint provides configuration information about the dashboard,
    /// including available features, API endpoints, and version information.
    /// </summary>
    /// <returns>Dashboard configuration</returns>
    /// <response code="200">Configuration retrieved successfully</response>
    [HttpGet("config")]
    [Produces("application/json")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult GetConfig()
    {
        // Get the base URL from the request
        var scheme = Request.Scheme;
        var host = Request.Host.Value;
        var baseUrl = $"{scheme}://{host}";
        
        var config = new
        {
            Version = "1.0.0",
            ApiVersion = "v1",
            BaseUrl = baseUrl,
            Endpoints = new
            {
                TeamManagement = $"{baseUrl}/api/v1/TeamManagement",
                Profile = $"{baseUrl}/api/v1/TeamManagement/profile"
            },
            Features = new
            {
                TeamInformationEdit = true,
                PaymentLimitsEdit = true,
                UsageStatistics = true,
                URLConfiguration = true,
                RealTimeUpdates = true,
                SessionAuthentication = true
            },
            UI = new
            {
                Theme = "Professional",
                ResponsiveDesign = true,
                DarkModeSupport = false,
                MultiLanguageSupport = false
            },
            Security = new
            {
                AuthenticationType = "Basic",
                SessionTimeout = "Browser session",
                HTTPSRecommended = true,
                CSRFProtection = false
            },
            Browser = new
            {
                MinimumRequirements = "ES6+ support",
                Recommended = new[] { "Chrome 60+", "Firefox 55+", "Safari 12+", "Edge 79+" }
            }
        };

        return Ok(config);
    }
}

/// <summary>
/// Extension methods for registering dashboard routes
/// </summary>
public static class TeamDashboardExtensions
{
    /// <summary>
    /// Maps team dashboard routes
    /// </summary>
    /// <param name="app">The application builder</param>
    /// <returns>The application builder</returns>
    public static IApplicationBuilder UseTeamDashboard(this IApplicationBuilder app)
    {
        return app;
    }
}