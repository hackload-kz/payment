using Microsoft.Extensions.Caching.Memory;
using System.Collections.Concurrent;
using System.Text.Json;

namespace PaymentGateway.API.Middleware;

/// <summary>
/// Middleware for authentication failure rate limiting
/// </summary>
public class AuthenticationRateLimitingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IMemoryCache _cache;
    private readonly ILogger<AuthenticationRateLimitingMiddleware> _logger;
    
    // Rate limiting configuration
    private readonly int _maxAttemptsPerMinute = 10;
    private readonly int _maxAttemptsPerHour = 50;
    private readonly TimeSpan _blockDuration = TimeSpan.FromMinutes(15);
    
    // Concurrent dictionaries for tracking attempts
    private readonly ConcurrentDictionary<string, RateLimitInfo> _rateLimitData = new();

    public AuthenticationRateLimitingMiddleware(
        RequestDelegate next,
        IMemoryCache cache,
        ILogger<AuthenticationRateLimitingMiddleware> logger)
    {
        _next = next;
        _cache = cache;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Check if this is an authentication request
        if (!IsAuthenticationRequest(context.Request.Path))
        {
            await _next(context);
            return;
        }

        var clientIdentifier = GetClientIdentifier(context);
        var teamSlug = await ExtractTeamSlugAsync(context.Request);

        // Check rate limits before processing
        if (await IsRateLimitedAsync(clientIdentifier, teamSlug))
        {
            await WriteRateLimitErrorAsync(context);
            return;
        }

        // Store original response stream to capture response
        var originalBodyStream = context.Response.Body;
        using var responseBodyStream = new MemoryStream();
        context.Response.Body = responseBodyStream;

        try
        {
            await _next(context);

            // Check if authentication failed based on response
            var isAuthenticationFailure = IsAuthenticationFailure(context.Response.StatusCode);
            
            if (isAuthenticationFailure)
            {
                await RecordFailedAttemptAsync(clientIdentifier, teamSlug);
            }
            else if (context.Response.StatusCode == 200)
            {
                // Reset rate limit on successful authentication
                await ResetRateLimitAsync(clientIdentifier, teamSlug);
            }
        }
        finally
        {
            // Copy the response back to the original stream
            responseBodyStream.Seek(0, SeekOrigin.Begin);
            await responseBodyStream.CopyToAsync(originalBodyStream);
            context.Response.Body = originalBodyStream;
        }
    }

    private bool IsAuthenticationRequest(PathString path)
    {
        var authenticationEndpoints = new[]
        {
            "/api/paymentinit/init",
            "/api/paymentconfirm/confirm",
            "/api/paymentcancel/cancel",
            "/api/paymentcheck/check"
        };

        return authenticationEndpoints.Any(endpoint => 
            path.StartsWithSegments(endpoint, StringComparison.OrdinalIgnoreCase));
    }

    private string GetClientIdentifier(HttpContext context)
    {
        // Use IP address as primary identifier
        var ipAddress = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        
        // Include User-Agent for additional fingerprinting
        var userAgent = context.Request.Headers.UserAgent.ToString();
        var userAgentHash = string.IsNullOrEmpty(userAgent) ? "none" : 
            Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(userAgent))).ToLowerInvariant()[..8];

        return $"{ipAddress}_{userAgentHash}";
    }

    private async Task<string?> ExtractTeamSlugAsync(HttpRequest request)
    {
        try
        {
            if (!request.ContentType?.Contains("application/json", StringComparison.OrdinalIgnoreCase) == true)
                return null;

            request.EnableBuffering();
            using var reader = new StreamReader(request.Body, leaveOpen: true);
            var body = await reader.ReadToEndAsync();
            request.Body.Position = 0;

            if (string.IsNullOrWhiteSpace(body))
                return null;

            using var jsonDoc = JsonDocument.Parse(body);
            if (jsonDoc.RootElement.TryGetProperty("TeamSlug", out var teamSlugElement))
            {
                return teamSlugElement.GetString();
            }

            return null;
        }
        catch
        {
            return null;
        }
    }

    private async Task<bool> IsRateLimitedAsync(string clientIdentifier, string? teamSlug)
    {
        var cacheKey = $"rate_limit_{clientIdentifier}";
        var teamCacheKey = !string.IsNullOrEmpty(teamSlug) ? $"rate_limit_team_{teamSlug}" : null;

        // Check IP-based rate limiting
        if (_cache.TryGetValue(cacheKey, out RateLimitInfo? rateLimitInfo))
        {
            if (IsBlocked(rateLimitInfo))
            {
                _logger.LogWarning("Rate limit exceeded for client: {ClientIdentifier}", clientIdentifier);
                return true;
            }
        }

        // Check team-based rate limiting if TeamSlug is available
        if (!string.IsNullOrEmpty(teamCacheKey) && _cache.TryGetValue(teamCacheKey, out RateLimitInfo? teamRateLimitInfo))
        {
            if (IsBlocked(teamRateLimitInfo))
            {
                _logger.LogWarning("Rate limit exceeded for team: {TeamSlug}", teamSlug);
                return true;
            }
        }

        await Task.CompletedTask;
        return false;
    }

    private async Task RecordFailedAttemptAsync(string clientIdentifier, string? teamSlug)
    {
        var now = DateTime.UtcNow;
        var cacheKey = $"rate_limit_{clientIdentifier}";

        // Update IP-based rate limiting
        var rateLimitInfo = _cache.Get<RateLimitInfo>(cacheKey) ?? new RateLimitInfo();
        
        // Clean old attempts (older than 1 hour)
        rateLimitInfo.Attempts = rateLimitInfo.Attempts
            .Where(attempt => now - attempt <= TimeSpan.FromHours(1))
            .ToList();

        rateLimitInfo.Attempts.Add(now);

        // Check if should be blocked
        var attemptsLastMinute = rateLimitInfo.Attempts.Count(a => now - a <= TimeSpan.FromMinutes(1));
        var attemptsLastHour = rateLimitInfo.Attempts.Count;

        if (attemptsLastMinute >= _maxAttemptsPerMinute || attemptsLastHour >= _maxAttemptsPerHour)
        {
            rateLimitInfo.BlockedUntil = now.Add(_blockDuration);
            _logger.LogWarning("Client {ClientIdentifier} blocked due to rate limit. Attempts in last minute: {AttemptsMinute}, last hour: {AttemptsHour}", 
                clientIdentifier, attemptsLastMinute, attemptsLastHour);
        }

        _cache.Set(cacheKey, rateLimitInfo, TimeSpan.FromHours(2));

        // Update team-based rate limiting if TeamSlug is available
        if (!string.IsNullOrEmpty(teamSlug))
        {
            var teamCacheKey = $"rate_limit_team_{teamSlug}";
            var teamRateLimitInfo = _cache.Get<RateLimitInfo>(teamCacheKey) ?? new RateLimitInfo();
            
            teamRateLimitInfo.Attempts = teamRateLimitInfo.Attempts
                .Where(attempt => now - attempt <= TimeSpan.FromHours(1))
                .ToList();

            teamRateLimitInfo.Attempts.Add(now);

            var teamAttemptsLastMinute = teamRateLimitInfo.Attempts.Count(a => now - a <= TimeSpan.FromMinutes(1));
            var teamAttemptsLastHour = teamRateLimitInfo.Attempts.Count;

            if (teamAttemptsLastMinute >= _maxAttemptsPerMinute || teamAttemptsLastHour >= _maxAttemptsPerHour)
            {
                teamRateLimitInfo.BlockedUntil = now.Add(_blockDuration);
                _logger.LogWarning("Team {TeamSlug} blocked due to rate limit. Attempts in last minute: {AttemptsMinute}, last hour: {AttemptsHour}", 
                    teamSlug, teamAttemptsLastMinute, teamAttemptsLastHour);
            }

            _cache.Set(teamCacheKey, teamRateLimitInfo, TimeSpan.FromHours(2));
        }

        await Task.CompletedTask;
    }

    private async Task ResetRateLimitAsync(string clientIdentifier, string? teamSlug)
    {
        var cacheKey = $"rate_limit_{clientIdentifier}";
        _cache.Remove(cacheKey);

        if (!string.IsNullOrEmpty(teamSlug))
        {
            var teamCacheKey = $"rate_limit_team_{teamSlug}";
            _cache.Remove(teamCacheKey);
        }

        await Task.CompletedTask;
    }

    private bool IsBlocked(RateLimitInfo rateLimitInfo)
    {
        return rateLimitInfo.BlockedUntil.HasValue && DateTime.UtcNow < rateLimitInfo.BlockedUntil.Value;
    }

    private bool IsAuthenticationFailure(int statusCode)
    {
        return statusCode == 401 || statusCode == 403;
    }

    private async Task WriteRateLimitErrorAsync(HttpContext context)
    {
        context.Response.StatusCode = 429; // Too Many Requests
        context.Response.ContentType = "application/json";

        var errorResponse = new
        {
            Success = false,
            ErrorCode = "RATE_LIMIT_EXCEEDED",
            Message = "Too many authentication attempts",
            Details = $"Rate limit exceeded. Please wait {_blockDuration.TotalMinutes} minutes before trying again.",
            Timestamp = DateTime.UtcNow,
            RetryAfter = _blockDuration.TotalSeconds
        };

        var jsonResponse = JsonSerializer.Serialize(errorResponse, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        });

        // Add Retry-After header
        context.Response.Headers.Add("Retry-After", ((int)_blockDuration.TotalSeconds).ToString());

        await context.Response.WriteAsync(jsonResponse);
    }
}

/// <summary>
/// Rate limit information for tracking authentication attempts
/// </summary>
public class RateLimitInfo
{
    public List<DateTime> Attempts { get; set; } = new();
    public DateTime? BlockedUntil { get; set; }
}

/// <summary>
/// Extension methods for registering the authentication rate limiting middleware
/// </summary>
public static class AuthenticationRateLimitingMiddlewareExtensions
{
    public static IApplicationBuilder UseAuthenticationRateLimit(this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<AuthenticationRateLimitingMiddleware>();
    }
}