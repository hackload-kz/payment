using Microsoft.Extensions.Primitives;
using PaymentGateway.Core.Services;
using System.Text.Json;

namespace PaymentGateway.API.Middleware;

/// <summary>
/// Middleware for payment authentication using SHA-256 token validation
/// </summary>
public class PaymentAuthenticationMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<PaymentAuthenticationMiddleware> _logger;

    // Endpoints that require authentication
    private readonly HashSet<string> _protectedEndpoints = new(StringComparer.OrdinalIgnoreCase)
    {
        "/api/paymentinit/init",
        "/api/paymentconfirm/confirm",
        "/api/paymentcancel/cancel",
        "/api/paymentcheck/check"
    };

    public PaymentAuthenticationMiddleware(
        RequestDelegate next,
        IServiceProvider serviceProvider,
        ILogger<PaymentAuthenticationMiddleware> logger)
    {
        _next = next;
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Check if this endpoint requires authentication
        if (!RequiresAuthentication(context.Request.Path))
        {
            await _next(context);
            return;
        }

        // Skip authentication for preflight requests
        if (context.Request.Method.Equals("OPTIONS", StringComparison.OrdinalIgnoreCase))
        {
            await _next(context);
            return;
        }

        var correlationId = context.TraceIdentifier;
        
        try
        {
            _logger.LogDebug("Starting payment authentication for request {CorrelationId} to {Path}", 
                correlationId, context.Request.Path);

            // Extract request parameters from body
            var requestParameters = await ExtractRequestParametersAsync(context.Request);
            if (requestParameters == null)
            {
                await WriteAuthenticationErrorAsync(context, "INVALID_REQUEST", "Invalid request format", 400);
                return;
            }

            // Perform authentication
            using var scope = _serviceProvider.CreateScope();
            var authenticationService = scope.ServiceProvider.GetRequiredService<IPaymentAuthenticationService>();
            
            var authResult = await authenticationService.AuthenticateRequestAsync(requestParameters);
            
            if (!authResult.IsAuthenticated)
            {
                _logger.LogWarning("Authentication failed for request {CorrelationId}: {ErrorCode} - {ErrorMessage}", 
                    correlationId, authResult.ErrorCode, authResult.ErrorMessage);

                await WriteAuthenticationErrorAsync(context, authResult.ErrorCode!, authResult.ErrorMessage!, 401);
                return;
            }

            // Store authentication information in context for later use
            context.Items["AuthenticatedTeam"] = authResult.Team;
            context.Items["TeamSlug"] = authResult.TeamSlug;
            context.Items["TeamId"] = authResult.TeamId;
            context.Items["AuthenticationTime"] = authResult.AuthenticationTime;

            _logger.LogDebug("Authentication successful for request {CorrelationId}, TeamSlug: {TeamSlug}", 
                correlationId, authResult.TeamSlug);

            // Continue to next middleware
            await _next(context);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in payment authentication middleware for request {CorrelationId}", correlationId);
            await WriteAuthenticationErrorAsync(context, "AUTHENTICATION_ERROR", "Internal authentication error", 500);
        }
    }

    private bool RequiresAuthentication(PathString path)
    {
        return _protectedEndpoints.Any(endpoint => path.StartsWithSegments(endpoint, StringComparison.OrdinalIgnoreCase));
    }

    private async Task<Dictionary<string, object>?> ExtractRequestParametersAsync(HttpRequest request)
    {
        try
        {
            // Only process JSON requests
            if (!request.ContentType?.Contains("application/json", StringComparison.OrdinalIgnoreCase) == true)
            {
                return null;
            }

            // Enable buffering to allow multiple reads of the request body
            request.EnableBuffering();

            // Read the request body
            using var reader = new StreamReader(request.Body, leaveOpen: true);
            var body = await reader.ReadToEndAsync();
            
            // Reset the stream position for downstream middleware
            request.Body.Position = 0;

            if (string.IsNullOrWhiteSpace(body))
            {
                return new Dictionary<string, object>();
            }

            // Parse JSON to dictionary
            var jsonDocument = JsonDocument.Parse(body);
            var parameters = new Dictionary<string, object>();

            foreach (var property in jsonDocument.RootElement.EnumerateObject())
            {
                parameters[property.Name] = ExtractJsonValue(property.Value);
            }

            return parameters;
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to parse request JSON for authentication");
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error extracting request parameters for authentication");
            return null;
        }
    }

    private object ExtractJsonValue(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString()!,
            JsonValueKind.Number => element.TryGetInt64(out var longValue) ? longValue : element.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Object => ExtractJsonObject(element),
            JsonValueKind.Array => ExtractJsonArray(element),
            JsonValueKind.Null => null!,
            _ => element.ToString()
        };
    }

    private Dictionary<string, object> ExtractJsonObject(JsonElement element)
    {
        var obj = new Dictionary<string, object>();
        foreach (var property in element.EnumerateObject())
        {
            obj[property.Name] = ExtractJsonValue(property.Value);
        }
        return obj;
    }

    private List<object> ExtractJsonArray(JsonElement element)
    {
        var array = new List<object>();
        foreach (var item in element.EnumerateArray())
        {
            array.Add(ExtractJsonValue(item));
        }
        return array;
    }

    private async Task WriteAuthenticationErrorAsync(HttpContext context, string errorCode, string errorMessage, int statusCode)
    {
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/json";

        var errorResponse = new
        {
            Success = false,
            ErrorCode = errorCode,
            Message = errorMessage,
            Details = "Authentication failed",
            Timestamp = DateTime.UtcNow
        };

        var jsonResponse = JsonSerializer.Serialize(errorResponse, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        });

        await context.Response.WriteAsync(jsonResponse);
    }
}

/// <summary>
/// Extension methods for registering the payment authentication middleware
/// </summary>
public static class PaymentAuthenticationMiddlewareExtensions
{
    public static IApplicationBuilder UsePaymentAuthentication(this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<PaymentAuthenticationMiddleware>();
    }
}