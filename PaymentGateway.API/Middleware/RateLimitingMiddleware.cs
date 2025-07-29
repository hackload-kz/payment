using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using PaymentGateway.Core.Services;
using System.Net;
using System.Text.Json;

namespace PaymentGateway.API.Middleware;

public class RateLimitingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IRateLimitingService _rateLimitingService;
    private readonly ILogger<RateLimitingMiddleware> _logger;

    private static readonly Dictionary<string, RateLimitPolicy> EndpointPolicies = new()
    {
        { "/api/payment/init", RateLimitingService.PaymentInitPolicy },
        { "/api/payment/confirm", RateLimitingService.PaymentProcessingPolicy },
        { "/api/payment/cancel", RateLimitingService.PaymentProcessingPolicy },
        { "/api/payment/check", RateLimitingService.DefaultApiPolicy }
    };

    public RateLimitingMiddleware(
        RequestDelegate next,
        IRateLimitingService rateLimitingService,
        ILogger<RateLimitingMiddleware> logger)
    {
        _next = next;
        _rateLimitingService = rateLimitingService;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            var policy = GetPolicyForRequest(context);
            if (policy != null)
            {
                var identifier = GetClientIdentifier(context);
                var result = await _rateLimitingService.CheckRateLimitAsync(identifier, policy);

                if (!result.IsAllowed)
                {
                    await HandleRateLimitExceeded(context, result);
                    return;
                }

                // Add rate limit headers
                context.Response.Headers.Add("X-RateLimit-Limit", policy.MaxRequests.ToString());
                context.Response.Headers.Add("X-RateLimit-Remaining", result.RemainingRequests.ToString());
                context.Response.Headers.Add("X-RateLimit-Reset", DateTimeOffset.UtcNow.Add(policy.WindowSize).ToUnixTimeSeconds().ToString());
            }

            await _next(context);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in rate limiting middleware");
            await _next(context);
        }
    }

    private RateLimitPolicy? GetPolicyForRequest(HttpContext context)
    {
        var path = context.Request.Path.Value?.ToLowerInvariant();
        
        if (string.IsNullOrEmpty(path))
            return null;

        foreach (var kvp in EndpointPolicies)
        {
            if (path.StartsWith(kvp.Key, StringComparison.OrdinalIgnoreCase))
            {
                return kvp.Value;
            }
        }

        // Default policy for API endpoints
        if (path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase))
        {
            return RateLimitingService.DefaultApiPolicy;
        }

        return null;
    }

    private string GetClientIdentifier(HttpContext context)
    {
        // Try to get TeamSlug from authentication
        var teamSlug = context.User?.FindFirst("TeamSlug")?.Value;
        if (!string.IsNullOrEmpty(teamSlug))
        {
            return $"team:{teamSlug}";
        }

        // Fall back to IP address
        var ipAddress = GetClientIpAddress(context);
        return $"ip:{ipAddress}";
    }

    private string GetClientIpAddress(HttpContext context)
    {
        // Check X-Forwarded-For header (for load balancers/proxies)
        var forwardedFor = context.Request.Headers["X-Forwarded-For"].FirstOrDefault();
        if (!string.IsNullOrEmpty(forwardedFor))
        {
            return forwardedFor.Split(',')[0].Trim();
        }

        // Check X-Real-IP header
        var realIp = context.Request.Headers["X-Real-IP"].FirstOrDefault();
        if (!string.IsNullOrEmpty(realIp))
        {
            return realIp;
        }

        // Fall back to RemoteIpAddress
        return context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    }

    private async Task HandleRateLimitExceeded(HttpContext context, RateLimitResult result)
    {
        context.Response.StatusCode = (int)HttpStatusCode.TooManyRequests;
        context.Response.ContentType = "application/json";

        // Add rate limit headers
        context.Response.Headers.Add("Retry-After", ((int)result.RetryAfter.TotalSeconds).ToString());
        context.Response.Headers.Add("X-RateLimit-Remaining", "0");

        var errorResponse = new
        {
            Success = false,
            ErrorCode = "RATE_LIMIT_EXCEEDED",
            Message = result.RejectReason ?? "Rate limit exceeded",
            Details = new
            {
                RetryAfterSeconds = (int)result.RetryAfter.TotalSeconds,
                RetryAfter = DateTimeOffset.UtcNow.Add(result.RetryAfter).ToString("O")
            }
        };

        var json = JsonSerializer.Serialize(errorResponse, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        await context.Response.WriteAsync(json);

        _logger.LogWarning("Rate limit exceeded for client {ClientId} on endpoint {Endpoint}. Reason: {Reason}",
            GetClientIdentifier(context), context.Request.Path, result.RejectReason);
    }
}