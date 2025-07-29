using PaymentGateway.Core.Services;
using System.Diagnostics;

namespace PaymentGateway.API.Middleware;

public class MetricsMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<MetricsMiddleware> _logger;

    public MetricsMiddleware(RequestDelegate next, ILogger<MetricsMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context, IPrometheusMetricsService metricsService)
    {
        var stopwatch = Stopwatch.StartNew();
        var endpoint = GetEndpointName(context);
        var method = context.Request.Method;

        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception in request pipeline");
            context.Response.StatusCode = 500;
            
            // Record error metrics
            metricsService.IncrementApiErrors(endpoint, method, 500, "UnhandledException");
            throw;
        }
        finally
        {
            stopwatch.Stop();
            var statusCode = context.Response.StatusCode;
            var duration = stopwatch.Elapsed.TotalMilliseconds;

            // Record request metrics
            metricsService.RecordApiRequestDuration(duration, endpoint, method, statusCode);
            metricsService.IncrementApiRequests(endpoint, method, statusCode);

            // Record error metrics for 4xx and 5xx status codes
            if (statusCode >= 400)
            {
                var errorCode = GetErrorCodeFromResponse(context, statusCode);
                metricsService.IncrementApiErrors(endpoint, method, statusCode, errorCode);
            }
        }
    }

    private static string GetEndpointName(HttpContext context)
    {
        var endpoint = context.GetEndpoint();
        if (endpoint?.DisplayName != null)
        {
            // Extract controller and action from endpoint display name
            var parts = endpoint.DisplayName.Split('.');
            if (parts.Length >= 2)
            {
                return $"{parts[^2]}.{parts[^1]}";
            }
        }

        // Fallback to path-based endpoint name
        var path = context.Request.Path.Value ?? "/";
        
        // Normalize path to remove IDs and other variable parts
        return NormalizePath(path);
    }

    private static string NormalizePath(string path)
    {
        // Replace common ID patterns with placeholders
        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var normalizedSegments = new List<string>();

        foreach (var segment in segments)
        {
            // Check if segment looks like an ID (GUID, number, etc.)
            if (IsIdSegment(segment))
            {
                normalizedSegments.Add("{id}");
            }
            else
            {
                normalizedSegments.Add(segment.ToLowerInvariant());
            }
        }

        return "/" + string.Join("/", normalizedSegments);
    }

    private static bool IsIdSegment(string segment)
    {
        // Check for GUID pattern
        if (Guid.TryParse(segment, out _))
            return true;

        // Check for numeric ID
        if (long.TryParse(segment, out _))
            return true;

        // Check for common ID patterns
        if (segment.Length > 10 && segment.All(c => char.IsLetterOrDigit(c) || c == '-' || c == '_'))
            return true;

        return false;
    }

    private static string GetErrorCodeFromResponse(HttpContext context, int statusCode)
    {
        // Try to extract error code from response headers or body
        if (context.Response.Headers.TryGetValue("X-Error-Code", out var errorCodeHeader))
        {
            return errorCodeHeader.FirstOrDefault() ?? statusCode.ToString();
        }

        // Fallback to HTTP status code
        return statusCode switch
        {
            400 => "BadRequest",
            401 => "Unauthorized",
            403 => "Forbidden",
            404 => "NotFound",
            409 => "Conflict",
            422 => "ValidationError",
            429 => "RateLimited",
            500 => "InternalServerError",
            502 => "BadGateway",
            503 => "ServiceUnavailable",
            504 => "GatewayTimeout",
            _ => statusCode.ToString()
        };
    }
}