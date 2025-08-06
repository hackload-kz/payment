using Microsoft.Extensions.Options;

namespace PaymentGateway.API.Middleware;

/// <summary>
/// Middleware for admin authentication using token-based authentication
/// </summary>
public class AdminAuthenticationMiddleware
{
    private readonly RequestDelegate _next;
    private readonly AdminAuthenticationOptions _options;
    private readonly ILogger<AdminAuthenticationMiddleware> _logger;

    public AdminAuthenticationMiddleware(
        RequestDelegate next, 
        IOptions<AdminAuthenticationOptions> options, 
        ILogger<AdminAuthenticationMiddleware> logger)
    {
        _next = next;
        _options = options.Value;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value;
        
        // Check if this is an admin endpoint
        if (_options.EnableAdminEndpoints && IsAdminEndpoint(path))
        {
            _logger.LogDebug("Admin endpoint accessed: {Path}", path);

            // Check for admin token
            var tokenHeaderName = _options.TokenHeaderName ?? "X-Admin-Token";
            var providedToken = context.Request.Headers[tokenHeaderName].FirstOrDefault();

            if (string.IsNullOrEmpty(providedToken))
            {
                _logger.LogWarning("Admin endpoint accessed without token: {Path} from {IP}", 
                    path, GetClientIpAddress(context));
                
                context.Response.StatusCode = 401;
                await context.Response.WriteAsync("Admin authentication required. Provide admin token in header.");
                return;
            }

            if (providedToken != _options.AdminToken)
            {
                _logger.LogWarning("Admin endpoint accessed with invalid token: {Path} from {IP}", 
                    path, GetClientIpAddress(context));
                
                context.Response.StatusCode = 403;
                await context.Response.WriteAsync("Invalid admin token provided.");
                return;
            }

            _logger.LogInformation("Admin authentication successful for endpoint: {Path} from {IP}", 
                path, GetClientIpAddress(context));
        }

        await _next(context);
    }

    private bool IsAdminEndpoint(string path)
    {
        if (string.IsNullOrEmpty(path) || _options.AdminEndpointPaths == null)
            return false;

        return _options.AdminEndpointPaths.Any(adminPath => 
            path.StartsWith(adminPath, StringComparison.OrdinalIgnoreCase));
    }

    private string GetClientIpAddress(HttpContext context)
    {
        var forwardedFor = context.Request.Headers["X-Forwarded-For"].FirstOrDefault();
        if (!string.IsNullOrEmpty(forwardedFor))
        {
            return forwardedFor.Split(',')[0].Trim();
        }

        var realIp = context.Request.Headers["X-Real-IP"].FirstOrDefault();
        if (!string.IsNullOrEmpty(realIp))
        {
            return realIp;
        }

        return context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    }
}

/// <summary>
/// Configuration options for admin authentication
/// </summary>
public class AdminAuthenticationOptions
{
    public const string SectionName = "AdminAuthentication";

    /// <summary>
    /// Admin authentication token
    /// </summary>
    public string AdminToken { get; set; } = string.Empty;

    /// <summary>
    /// Header name for admin token (default: X-Admin-Token)
    /// </summary>
    public string TokenHeaderName { get; set; } = "X-Admin-Token";

    /// <summary>
    /// Enable admin endpoints protection
    /// </summary>
    public bool EnableAdminEndpoints { get; set; } = true;

    /// <summary>
    /// List of paths that require admin authentication
    /// </summary>
    public string[] AdminEndpointPaths { get; set; } = Array.Empty<string>();
}