using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using PaymentGateway.Core.Entities;
using PaymentGateway.Core.Services;
using System.Security.Claims;

namespace PaymentGateway.Core.Middleware;

/// <summary>
/// Middleware to establish audit context for incoming requests
/// </summary>
public class AuditContextMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<AuditContextMiddleware> _logger;

    public AuditContextMiddleware(RequestDelegate next, ILogger<AuditContextMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context, IComprehensiveAuditService auditService)
    {
        try
        {
            // Create audit context from HTTP request
            var auditContext = CreateAuditContext(context);
            
            // Set the audit context for this request
            auditService.SetAuditContext(auditContext);
            
            // Log the request start if it's a significant operation
            if (IsSignificantOperation(context))
            {
                await auditService.LogSystemEventAsync(
                    AuditAction.ApiCallMade,
                    "HttpRequest",
                    $"{context.Request.Method} {context.Request.Path}",
                    auditContext
                );
            }

            // Process the request
            await _next(context);
            
            // Log failed requests
            if (context.Response.StatusCode >= 400 && IsSignificantOperation(context))
            {
                await auditService.LogSystemEventAsync(
                    AuditAction.ApiCallFailed,
                    "HttpRequest",
                    $"{context.Request.Method} {context.Request.Path} - Status: {context.Response.StatusCode}",
                    auditContext
                );
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in audit context middleware");
            
            // Still try to log the error
            try
            {
                var auditContext = CreateAuditContext(context);
                await auditService.LogSystemEventAsync(
                    AuditAction.ApiCallFailed,
                    "HttpRequest",
                    $"{context.Request.Method} {context.Request.Path} - Exception: {ex.Message}",
                    auditContext
                );
            }
            catch
            {
                // Ignore audit errors during exception handling
            }
            
            throw;
        }
        finally
        {
            // Clear the audit context
            auditService.ClearAuditContext();
        }
    }

    private AuditContext CreateAuditContext(HttpContext context)
    {
        var auditContext = new AuditContext
        {
            RequestId = context.TraceIdentifier,
            IpAddress = GetClientIpAddress(context),
            UserAgent = context.Request.Headers.UserAgent.FirstOrDefault()?.Substring(0, Math.Min(500, context.Request.Headers.UserAgent.FirstOrDefault()?.Length ?? 0)),
            CorrelationId = GetCorrelationId(context)
        };

        // Extract user information if authenticated
        if (context.User?.Identity?.IsAuthenticated == true)
        {
            auditContext.UserId = context.User.Identity.Name;
            auditContext.TeamSlug = context.User.FindFirst("team_slug")?.Value;
            auditContext.SessionId = context.User.FindFirst("session_id")?.Value;
        }
        else
        {
            // For payment operations, try to extract team from request data
            auditContext.TeamSlug = ExtractTeamFromRequest(context);
        }

        // Add additional context data
        auditContext.AdditionalData = new Dictionary<string, object>
        {
            { "Path", context.Request.Path.Value ?? string.Empty },
            { "Method", context.Request.Method },
            { "QueryString", context.Request.QueryString.Value ?? string.Empty },
            { "ContentType", context.Request.ContentType ?? string.Empty },
            { "Host", context.Request.Host.Value },
            { "Scheme", context.Request.Scheme },
            { "Protocol", context.Request.Protocol }
        };

        // Add request headers (sanitized)
        var importantHeaders = new[] { "Authorization", "X-API-Key", "X-Correlation-ID", "X-Forwarded-For", "X-Real-IP" };
        foreach (var headerName in importantHeaders)
        {
            if (context.Request.Headers.ContainsKey(headerName))
            {
                var headerValue = context.Request.Headers[headerName].FirstOrDefault();
                if (!string.IsNullOrEmpty(headerValue))
                {
                    // Sanitize sensitive headers
                    if (headerName.Contains("Authorization") || headerName.Contains("Key"))
                    {
                        auditContext.AdditionalData[headerName] = "***REDACTED***";
                    }
                    else
                    {
                        auditContext.AdditionalData[headerName] = headerValue;
                    }
                }
            }
        }

        return auditContext;
    }

    private string GetClientIpAddress(HttpContext context)
    {
        // Try various headers in order of preference
        var ipHeaders = new[]
        {
            "X-Forwarded-For",
            "X-Real-IP",
            "X-Client-IP",
            "CF-Connecting-IP", // Cloudflare
            "True-Client-IP"    // Cloudflare Enterprise
        };

        foreach (var header in ipHeaders)
        {
            var value = context.Request.Headers[header].FirstOrDefault();
            if (!string.IsNullOrEmpty(value))
            {
                // X-Forwarded-For can contain multiple IPs, take the first one
                var ip = value.Split(',')[0].Trim();
                if (IsValidIpAddress(ip))
                {
                    return ip;
                }
            }
        }

        // Fallback to connection remote IP
        return context.Connection.RemoteIpAddress?.ToString() ?? "Unknown";
    }

    private string GetCorrelationId(HttpContext context)
    {
        // Try to get correlation ID from various sources
        var correlationId = context.Request.Headers["X-Correlation-ID"].FirstOrDefault()
                          ?? context.Request.Headers["X-Request-ID"].FirstOrDefault()
                          ?? context.TraceIdentifier;

        return correlationId;
    }

    private string? ExtractTeamFromRequest(HttpContext context)
    {
        // Try to extract team slug from request path
        var pathSegments = context.Request.Path.Value?.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (pathSegments?.Length > 0)
        {
            // Look for team slug in common patterns
            // e.g., /api/v1/teams/{teamSlug}/payments
            var teamIndex = Array.FindIndex(pathSegments, s => s.Equals("teams", StringComparison.OrdinalIgnoreCase));
            if (teamIndex >= 0 && teamIndex + 1 < pathSegments.Length)
            {
                return pathSegments[teamIndex + 1];
            }
        }

        // Try to extract from query parameters
        return context.Request.Query["team_slug"].FirstOrDefault()
               ?? context.Request.Query["teamSlug"].FirstOrDefault();
    }

    private bool IsSignificantOperation(HttpContext context)
    {
        var path = context.Request.Path.Value?.ToLowerInvariant();
        if (string.IsNullOrEmpty(path))
            return false;

        // Define significant operations that should be audited
        var significantPaths = new[]
        {
            "/api/payments",
            "/api/transactions",
            "/api/auth",
            "/api/teams",
            "/api/customers"
        };

        // Exclude health checks and metrics
        var excludedPaths = new[]
        {
            "/health",
            "/metrics",
            "/swagger",
            "/api/health"
        };

        if (excludedPaths.Any(excluded => path.StartsWith(excluded)))
            return false;

        return significantPaths.Any(significant => path.StartsWith(significant))
               || context.Request.Method != "GET"; // All non-GET requests are significant
    }

    private bool IsValidIpAddress(string ip)
    {
        return System.Net.IPAddress.TryParse(ip, out _);
    }
}

/// <summary>
/// Extension methods for registering audit context middleware
/// </summary>
public static class AuditContextMiddlewareExtensions
{
    public static IApplicationBuilder UseAuditContext(this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<AuditContextMiddleware>();
    }
}