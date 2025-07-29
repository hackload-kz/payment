using Microsoft.Extensions.Options;
using System.Net;

namespace PaymentGateway.API.Middleware;

public class HttpsEnforcementMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<HttpsEnforcementMiddleware> _logger;
    private readonly HttpsEnforcementOptions _options;

    public HttpsEnforcementMiddleware(
        RequestDelegate next,
        ILogger<HttpsEnforcementMiddleware> logger,
        IOptions<HttpsEnforcementOptions> options)
    {
        _next = next;
        _logger = logger;
        _options = options.Value;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Skip HTTPS enforcement for excluded paths (like health checks)
        if (ShouldSkipHttpsEnforcement(context))
        {
            await _next(context);
            return;
        }

        // Skip enforcement if disabled
        if (!_options.RequireHttps)
        {
            await _next(context);
            return;
        }

        // Check if request is already HTTPS
        if (context.Request.IsHttps)
        {
            // Add security headers for HTTPS requests
            AddSecurityHeaders(context);
            await _next(context);
            return;
        }

        // Check if we're behind a proxy that terminates SSL
        if (_options.TrustForwardedHeaders && IsHttpsViaProxy(context))
        {
            AddSecurityHeaders(context);
            await _next(context);
            return;
        }

        // Request is not HTTPS - handle based on configuration
        await HandleNonHttpsRequest(context);
    }

    private bool ShouldSkipHttpsEnforcement(HttpContext context)
    {
        var path = context.Request.Path.Value?.ToLowerInvariant();
        return _options.ExcludedPaths.Any(excludedPath => 
            path?.StartsWith(excludedPath.ToLowerInvariant()) == true);
    }

    private bool IsHttpsViaProxy(HttpContext context)
    {
        // Check various headers that proxies might set to indicate HTTPS
        var forwardedProto = context.Request.Headers["X-Forwarded-Proto"].FirstOrDefault();
        if (string.Equals(forwardedProto, "https", StringComparison.OrdinalIgnoreCase))
            return true;

        var forwardedScheme = context.Request.Headers["X-Forwarded-Scheme"].FirstOrDefault();
        if (string.Equals(forwardedScheme, "https", StringComparison.OrdinalIgnoreCase))
            return true;

        var originalProto = context.Request.Headers["X-Original-Proto"].FirstOrDefault();
        if (string.Equals(originalProto, "https", StringComparison.OrdinalIgnoreCase))
            return true;

        // Check for Azure Application Gateway header
        var frontEndHttps = context.Request.Headers["X-ARR-SSL"].FirstOrDefault();
        if (!string.IsNullOrEmpty(frontEndHttps))
            return true;

        // Check for CloudFlare
        var cfVisitor = context.Request.Headers["CF-Visitor"].FirstOrDefault();
        if (cfVisitor?.Contains("\"scheme\":\"https\"") == true)
            return true;

        return false;
    }

    private void AddSecurityHeaders(HttpContext context)
    {
        var response = context.Response;

        // Strict Transport Security (HSTS)
        if (_options.EnableHsts && !response.Headers.ContainsKey("Strict-Transport-Security"))
        {
            var hstsValue = $"max-age={_options.HstsMaxAge}";
            if (_options.HstsIncludeSubdomains)
                hstsValue += "; includeSubDomains";
            if (_options.HstsPreload)
                hstsValue += "; preload";

            response.Headers.Add("Strict-Transport-Security", hstsValue);
        }

        // Content Security Policy for mixed content protection
        if (_options.EnableContentSecurityPolicy && !response.Headers.ContainsKey("Content-Security-Policy"))
        {
            response.Headers.Add("Content-Security-Policy", _options.ContentSecurityPolicy);
        }

        // Secure cookie settings
        if (_options.ForceSecureCookies)
        {
            // This will be handled by cookie policy middleware typically
            context.Items["ForceSecureCookies"] = true;
        }

        // Referrer Policy
        if (!response.Headers.ContainsKey("Referrer-Policy"))
        {
            response.Headers.Add("Referrer-Policy", "strict-origin-when-cross-origin");
        }

        // X-Content-Type-Options
        if (!response.Headers.ContainsKey("X-Content-Type-Options"))
        {
            response.Headers.Add("X-Content-Type-Options", "nosniff");
        }

        // X-Frame-Options
        if (!response.Headers.ContainsKey("X-Frame-Options"))
        {
            response.Headers.Add("X-Frame-Options", "DENY");
        }

        // X-XSS-Protection (legacy browsers)
        if (!response.Headers.ContainsKey("X-XSS-Protection"))
        {
            response.Headers.Add("X-XSS-Protection", "1; mode=block");
        }
    }

    private async Task HandleNonHttpsRequest(HttpContext context)
    {
        var clientIp = GetClientIpAddress(context);
        var userAgent = context.Request.Headers.UserAgent.FirstOrDefault();

        _logger.LogWarning("Non-HTTPS request detected from IP {ClientIp}: {Method} {Path} (User-Agent: {UserAgent})",
            clientIp, context.Request.Method, context.Request.Path, userAgent);

        switch (_options.NonHttpsAction)
        {
            case NonHttpsAction.Redirect:
                await RedirectToHttps(context);
                break;

            case NonHttpsAction.Reject:
                await RejectRequest(context);
                break;

            case NonHttpsAction.Block:
                await BlockRequest(context);
                break;

            default:
                await _next(context);
                break;
        }
    }

    private async Task RedirectToHttps(HttpContext context)
    {
        var httpsUrl = BuildHttpsUrl(context);
        
        _logger.LogInformation("Redirecting non-HTTPS request to: {HttpsUrl}", httpsUrl);

        context.Response.StatusCode = (int)HttpStatusCode.MovedPermanently;
        context.Response.Headers.Location = httpsUrl;
        
        // Add security headers even for redirects
        AddSecurityHeaders(context);

        await context.Response.WriteAsync($"This resource is only available over HTTPS. Redirecting to: {httpsUrl}");
    }

    private async Task RejectRequest(HttpContext context)
    {
        context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
        context.Response.ContentType = "application/json";

        var errorResponse = new
        {
            Success = false,
            ErrorCode = "HTTPS_REQUIRED",
            Message = "HTTPS is required for all API requests",
            Details = "This API only accepts requests over HTTPS for security reasons",
            CorrelationId = context.TraceIdentifier,
            Timestamp = DateTime.UtcNow
        };

        var json = System.Text.Json.JsonSerializer.Serialize(errorResponse, new System.Text.Json.JsonSerializerOptions
        {
            PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
        });

        await context.Response.WriteAsync(json);
    }

    private async Task BlockRequest(HttpContext context)
    {
        context.Response.StatusCode = (int)HttpStatusCode.Forbidden;
        context.Response.ContentType = "text/plain";

        await context.Response.WriteAsync("Access denied. HTTPS is required.");
    }

    private string BuildHttpsUrl(HttpContext context)
    {
        var request = context.Request;
        var host = request.Host.Host;
        
        // Use configured HTTPS port or default to 443
        var port = _options.HttpsPort != 443 ? $":{_options.HttpsPort}" : "";
        
        return $"https://{host}{port}{request.PathBase}{request.Path}{request.QueryString}";
    }

    private static string? GetClientIpAddress(HttpContext context)
    {
        var forwardedFor = context.Request.Headers["X-Forwarded-For"].FirstOrDefault();
        if (!string.IsNullOrEmpty(forwardedFor))
        {
            return forwardedFor.Split(',').FirstOrDefault()?.Trim();
        }

        var realIp = context.Request.Headers["X-Real-IP"].FirstOrDefault();
        if (!string.IsNullOrEmpty(realIp))
            return realIp;

        return context.Connection.RemoteIpAddress?.ToString();
    }
}

public class HttpsEnforcementOptions
{
    public bool RequireHttps { get; set; } = true;
    public NonHttpsAction NonHttpsAction { get; set; } = NonHttpsAction.Redirect;
    public int HttpsPort { get; set; } = 443;
    public bool TrustForwardedHeaders { get; set; } = true;

    public List<string> ExcludedPaths { get; set; } = new()
    {
        "/health",
        "/metrics"
    };

    // HSTS (HTTP Strict Transport Security) settings
    public bool EnableHsts { get; set; } = true;
    public int HstsMaxAge { get; set; } = 31536000; // 1 year in seconds
    public bool HstsIncludeSubdomains { get; set; } = true;
    public bool HstsPreload { get; set; } = false;

    // Content Security Policy
    public bool EnableContentSecurityPolicy { get; set; } = true;
    public string ContentSecurityPolicy { get; set; } = "default-src 'self'; script-src 'self' 'unsafe-inline'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; connect-src 'self'";

    // Cookie security
    public bool ForceSecureCookies { get; set; } = true;

    // Development/testing overrides
    public bool AllowLocalhostHttp { get; set; } = false;
    public List<string> AllowedHttpHosts { get; set; } = new();
}

public enum NonHttpsAction
{
    Allow,
    Redirect,
    Reject,
    Block
}

// Extension methods for easier middleware registration
public static class HttpsEnforcementMiddlewareExtensions
{
    public static IApplicationBuilder UseHttpsEnforcement(
        this IApplicationBuilder builder,
        Action<HttpsEnforcementOptions>? configureOptions = null)
    {
        var options = new HttpsEnforcementOptions();
        configureOptions?.Invoke(options);

        return builder.UseMiddleware<HttpsEnforcementMiddleware>(Options.Create(options));
    }

    public static IServiceCollection AddHttpsEnforcement(
        this IServiceCollection services,
        Action<HttpsEnforcementOptions>? configureOptions = null)
    {
        if (configureOptions != null)
        {
            services.Configure(configureOptions);
        }

        // Add related services for HTTPS enforcement
        services.Configure<CookiePolicyOptions>(options =>
        {
            options.Secure = CookieSecurePolicy.Always;
            options.SameSite = SameSiteMode.Strict;
            options.HttpOnly = Microsoft.AspNetCore.CookiePolicy.HttpOnlyPolicy.Always;
        });

        return services;
    }

    public static IServiceCollection AddHttpsRedirection(
        this IServiceCollection services,
        int httpsPort = 443)
    {
        services.Configure<HttpsRedirectionOptions>(options =>
        {
            options.HttpsPort = httpsPort;
            options.RedirectStatusCode = (int)HttpStatusCode.MovedPermanently;
        });

        return services;
    }
}

// Security audit service to track HTTPS violations
public interface IHttpsSecurityAuditService
{
    Task LogHttpsViolationAsync(string clientIp, string path, string? userAgent = null);
    Task<List<HttpsViolationRecord>> GetRecentViolationsAsync(TimeSpan period);
    Task<Dictionary<string, int>> GetViolationsByIpAsync(TimeSpan period);
}

public record HttpsViolationRecord(
    DateTime Timestamp,
    string ClientIp,
    string Path,
    string? UserAgent,
    string Action);

public class HttpsSecurityAuditService : IHttpsSecurityAuditService
{
    private readonly ILogger<HttpsSecurityAuditService> _logger;
    private readonly List<HttpsViolationRecord> _violations = new();
    private readonly object _lock = new();

    public HttpsSecurityAuditService(ILogger<HttpsSecurityAuditService> logger)
    {
        _logger = logger;
    }

    public async Task LogHttpsViolationAsync(string clientIp, string path, string? userAgent = null)
    {
        var violation = new HttpsViolationRecord(
            DateTime.UtcNow,
            clientIp,
            path,
            userAgent,
            "HTTP_REQUEST_BLOCKED");

        lock (_lock)
        {
            _violations.Add(violation);
            
            // Keep only recent violations (last 24 hours)
            var cutoff = DateTime.UtcNow - TimeSpan.FromHours(24);
            _violations.RemoveAll(v => v.Timestamp < cutoff);
        }

        _logger.LogWarning("HTTPS violation recorded: IP={ClientIp}, Path={Path}, UserAgent={UserAgent}",
            clientIp, path, userAgent);

        await Task.CompletedTask;
    }

    public async Task<List<HttpsViolationRecord>> GetRecentViolationsAsync(TimeSpan period)
    {
        var cutoff = DateTime.UtcNow - period;
        
        lock (_lock)
        {
            return _violations.Where(v => v.Timestamp > cutoff).ToList();
        }
    }

    public async Task<Dictionary<string, int>> GetViolationsByIpAsync(TimeSpan period)
    {
        var violations = await GetRecentViolationsAsync(period);
        return violations
            .GroupBy(v => v.ClientIp)
            .ToDictionary(g => g.Key, g => g.Count());
    }
}