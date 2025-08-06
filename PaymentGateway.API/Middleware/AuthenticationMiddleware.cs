using PaymentGateway.Core.Services;
using PaymentGateway.Core.Enums;
using PaymentGateway.Core.Models;
using Microsoft.Extensions.Options;
using System.Text.Json;
using System.Text;
using System.Diagnostics;

namespace PaymentGateway.API.Middleware;

public class AuthenticationMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<AuthenticationMiddleware> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly AuthenticationMiddlewareOptions _options;

    public AuthenticationMiddleware(
        RequestDelegate next,
        ILogger<AuthenticationMiddleware> logger,
        IServiceProvider serviceProvider,
        IOptions<AuthenticationMiddlewareOptions> options)
    {
        _next = next;
        _logger = logger;
        _serviceProvider = serviceProvider;
        _options = options.Value;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Skip authentication for excluded paths
        if (ShouldSkipAuthentication(context))
        {
            await _next(context);
            return;
        }

        var stopwatch = Stopwatch.StartNew();
        var correlationId = context.TraceIdentifier;

        try
        {
            // Extract authentication data from request
            var authenticationData = await ExtractAuthenticationDataAsync(context);
            
            if (authenticationData == null)
            {
                await WriteErrorResponseAsync(context, PaymentErrorCode.MissingRequiredParameters, 
                    "Authentication data is missing or invalid");
                return;
            }

            // Perform authentication
            using var scope = _serviceProvider.CreateScope();
            var authService = scope.ServiceProvider.GetRequiredService<IAuthenticationService>();
            
            var authResult = await authService.AuthenticateAsync(
                authenticationData.TeamSlug,
                authenticationData.Token,
                context.RequestAborted);

            stopwatch.Stop();

            if (!authResult.IsSuccessful)
            {
                _logger.LogWarning("Authentication failed for team {TeamSlug}: {FailureReason} (Processing time: {ProcessingTime}ms)",
                    authenticationData.TeamSlug, authResult.FailureReason, stopwatch.ElapsedMilliseconds);

                await WriteErrorResponseAsync(context, PaymentErrorCode.AUTHENTICATION_FAILED, authResult.FailureReason ?? "Authentication failed");
                return;
            }

            // Store authentication result in context for downstream middleware
            context.Items["AuthenticationResult"] = authResult;
            context.Items["TeamInfo"] = authResult.TeamInfo;
            context.Items["TeamSlug"] = authenticationData.TeamSlug;

            _logger.LogDebug("Authentication successful for team {TeamSlug} (Processing time: {ProcessingTime}ms)",
                authenticationData.TeamSlug, stopwatch.ElapsedMilliseconds);

            await _next(context);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex, "Authentication middleware error for correlation ID {CorrelationId} (Processing time: {ProcessingTime}ms)",
                correlationId, stopwatch.ElapsedMilliseconds);

            await WriteErrorResponseAsync(context, PaymentErrorCode.InternalRequestProcessingError, 
                "Authentication processing error");
        }
    }

    private bool ShouldSkipAuthentication(HttpContext context)
    {
        var path = context.Request.Path.Value?.ToLowerInvariant();
        
        return _options.ExcludedPaths.Any(excludedPath => 
            path?.StartsWith(excludedPath.ToLowerInvariant()) == true);
    }

    private async Task<AuthenticationData?> ExtractAuthenticationDataAsync(HttpContext context)
    {
        try
        {
            // Ensure request body can be read multiple times
            context.Request.EnableBuffering();

            // Read request body
            var body = await ReadRequestBodyAsync(context.Request);
            if (string.IsNullOrWhiteSpace(body))
            {
                return null;
            }

            // Parse JSON request
            var requestData = JsonSerializer.Deserialize<Dictionary<string, object>>(body, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                PropertyNameCaseInsensitive = true
            });

            if (requestData == null)
            {
                return null;
            }

            // Extract TeamSlug
            if (!requestData.TryGetValue("TeamSlug", out var teamSlugObj) || 
                teamSlugObj?.ToString() is not string teamSlug || 
                string.IsNullOrWhiteSpace(teamSlug))
            {
                _logger.LogWarning("TeamSlug is missing from request");
                return null;
            }

            // Extract Token
            if (!requestData.TryGetValue("Token", out var tokenObj) || 
                tokenObj?.ToString() is not string token || 
                string.IsNullOrWhiteSpace(token))
            {
                _logger.LogWarning("Token is missing from request for team {TeamSlug}", teamSlug);
                return null;
            }

            // Reset request body position for downstream middleware
            context.Request.Body.Position = 0;

            return new AuthenticationData(teamSlug, token, requestData, GetClientIpAddress(context));
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to parse request JSON for authentication");
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error extracting authentication data from request");
            return null;
        }
    }

    private static async Task<string> ReadRequestBodyAsync(HttpRequest request)
    {
        request.Body.Position = 0;
        using var reader = new StreamReader(request.Body, Encoding.UTF8, leaveOpen: true);
        var body = await reader.ReadToEndAsync();
        request.Body.Position = 0;
        return body;
    }

    private static string? GetClientIpAddress(HttpContext context)
    {
        // Check for forwarded headers first (for load balancers/proxies)
        var forwardedFor = context.Request.Headers["X-Forwarded-For"].FirstOrDefault();
        if (!string.IsNullOrEmpty(forwardedFor))
        {
            // Take the first IP in the chain
            var firstIp = forwardedFor.Split(',').FirstOrDefault()?.Trim();
            if (!string.IsNullOrEmpty(firstIp))
                return firstIp;
        }

        var realIp = context.Request.Headers["X-Real-IP"].FirstOrDefault();
        if (!string.IsNullOrEmpty(realIp))
            return realIp;

        // Fall back to connection remote IP
        return context.Connection.RemoteIpAddress?.ToString();
    }

    private async Task WriteErrorResponseAsync(HttpContext context, PaymentErrorCode errorCode, string? errorMessage)
    {
        context.Response.StatusCode = GetHttpStatusCode(errorCode);
        context.Response.ContentType = "application/json";

        var errorResponse = new
        {
            Success = false,
            ErrorCode = errorCode.ToString(),
            Message = errorMessage ?? "Authentication failed",
            Details = GetErrorDetails(errorCode),
            CorrelationId = context.TraceIdentifier,
            Timestamp = DateTime.UtcNow
        };

        var json = JsonSerializer.Serialize(errorResponse, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        });

        await context.Response.WriteAsync(json);
    }

    private static int GetHttpStatusCode(PaymentErrorCode errorCode)
    {
        return errorCode switch
        {
            PaymentErrorCode.MissingRequiredParameters => 400,
            PaymentErrorCode.InvalidParameterFormat => 400,
            PaymentErrorCode.InvalidToken => 401,
            PaymentErrorCode.TokenAuthenticationFailed => 401,
            PaymentErrorCode.TerminalNotFound => 401,
            PaymentErrorCode.TerminalAccessDenied => 403,
            PaymentErrorCode.TerminalBlocked => 403,
            PaymentErrorCode.InternalRequestProcessingError => 500,
            _ => 400
        };
    }

    private static string GetErrorDetails(PaymentErrorCode errorCode)
    {
        return errorCode switch
        {
            PaymentErrorCode.MissingRequiredParameters => "TeamSlug and Token are required for authentication",
            PaymentErrorCode.InvalidToken => "Invalid or malformed authentication token",
            PaymentErrorCode.TokenAuthenticationFailed => "Token validation failed - check signature generation",
            PaymentErrorCode.TerminalNotFound => "TeamSlug not found or not registered",
            PaymentErrorCode.TerminalAccessDenied => "Team access denied - may be blocked or inactive",
            PaymentErrorCode.TerminalBlocked => "Team is temporarily blocked due to multiple failed attempts",
            PaymentErrorCode.InternalRequestProcessingError => "Internal authentication processing error",
            _ => "Authentication error occurred"
        };
    }

    private record AuthenticationData(
        string TeamSlug,
        string Token,
        Dictionary<string, object> RequestParameters,
        string? ClientIpAddress);
}

public class AuthenticationMiddlewareOptions
{
    public List<string> ExcludedPaths { get; set; } = new()
    {
        "/health",
        "/metrics", 
        "/swagger",
        "/api/health",
        "/api/status"
    };
    
    public bool EnableDetailedErrorLogging { get; set; } = true;
    public bool EnablePerformanceLogging { get; set; } = true;
    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(30);
}

// Extension methods for easier middleware registration
public static class AuthenticationMiddlewareExtensions
{
    public static IApplicationBuilder UseAuthentication(
        this IApplicationBuilder builder, 
        Action<AuthenticationMiddlewareOptions>? configureOptions = null)
    {
        var options = new AuthenticationMiddlewareOptions();
        configureOptions?.Invoke(options);

        return builder.UseMiddleware<AuthenticationMiddleware>(Options.Create(options));
    }

    public static IServiceCollection AddAuthenticationMiddleware(
        this IServiceCollection services,
        Action<AuthenticationMiddlewareOptions>? configureOptions = null)
    {
        if (configureOptions != null)
        {
            services.Configure(configureOptions);
        }

        return services;
    }
}

// Extension methods for accessing authentication data in controllers
public static class HttpContextAuthenticationExtensions
{
    public static AuthenticationResult? GetAuthenticationResult(this HttpContext context)
    {
        return context.Items["AuthenticationResult"] as AuthenticationResult;
    }

    public static TeamInfo? GetTeamInfo(this HttpContext context)
    {
        return context.Items["TeamInfo"] as TeamInfo;
    }

    public static string? GetTeamSlug(this HttpContext context)
    {
        return context.Items["TeamSlug"] as string;
    }

    public static bool IsAuthenticated(this HttpContext context)
    {
        var authResult = context.GetAuthenticationResult();
        return authResult?.IsSuccessful == true;
    }
}