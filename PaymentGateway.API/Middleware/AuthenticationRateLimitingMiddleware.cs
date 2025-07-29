using Microsoft.Extensions.Options;
using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;

namespace PaymentGateway.API.Middleware;

public class AuthenticationRateLimitingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<AuthenticationRateLimitingMiddleware> _logger;
    private readonly AuthenticationRateLimitingOptions _options;
    
    // Thread-safe collections for tracking requests
    private readonly ConcurrentDictionary<string, List<RequestRecord>> _ipRequests;
    private readonly ConcurrentDictionary<string, List<RequestRecord>> _teamRequests;
    private readonly ConcurrentDictionary<string, DateTime> _blockedIps;
    private readonly ConcurrentDictionary<string, DateTime> _blockedTeams;

    public AuthenticationRateLimitingMiddleware(
        RequestDelegate next,
        ILogger<AuthenticationRateLimitingMiddleware> logger,
        IOptions<AuthenticationRateLimitingOptions> options)
    {
        _next = next;
        _logger = logger;
        _options = options.Value;
        _ipRequests = new ConcurrentDictionary<string, List<RequestRecord>>();
        _teamRequests = new ConcurrentDictionary<string, List<RequestRecord>>();
        _blockedIps = new ConcurrentDictionary<string, DateTime>();
        _blockedTeams = new ConcurrentDictionary<string, DateTime>();

        // Start cleanup background task
        _ = Task.Run(CleanupExpiredRecords);
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Skip rate limiting for excluded paths
        if (ShouldSkipRateLimit(context))
        {
            await _next(context);
            return;
        }

        var clientIp = GetClientIpAddress(context);
        var teamSlug = await ExtractTeamSlugAsync(context);
        var now = DateTime.UtcNow;

        try
        {
            // Check if IP is currently blocked
            if (IsIpBlocked(clientIp, now))
            {
                await WriteRateLimitExceededResponse(context, "IP address is temporarily blocked");
                return;
            }

            // Check if team is currently blocked
            if (!string.IsNullOrEmpty(teamSlug) && IsTeamBlocked(teamSlug, now))
            {
                await WriteRateLimitExceededResponse(context, "Team is temporarily blocked");
                return;
            }

            // Check IP-based rate limit
            if (!string.IsNullOrEmpty(clientIp) && !CheckIpRateLimit(clientIp, now))
            {
                var blockDuration = CalculateBlockDuration(_ipRequests[clientIp].Count);
                _blockedIps[clientIp] = now.Add(blockDuration);
                
                _logger.LogWarning("IP {ClientIp} has been blocked for {BlockDuration} due to rate limit violation",
                    clientIp, blockDuration);

                await WriteRateLimitExceededResponse(context, "Rate limit exceeded for IP address");
                return;
            }

            // Check team-based rate limit for authentication endpoints
            if (!string.IsNullOrEmpty(teamSlug) && IsAuthenticationEndpoint(context))
            {
                if (!CheckTeamRateLimit(teamSlug, now))
                {
                    var blockDuration = CalculateBlockDuration(_teamRequests[teamSlug].Count);
                    _blockedTeams[teamSlug] = now.Add(blockDuration);
                    
                    _logger.LogWarning("Team {TeamSlug} has been blocked for {BlockDuration} due to authentication rate limit violation",
                        teamSlug, blockDuration);

                    await WriteRateLimitExceededResponse(context, "Authentication rate limit exceeded for team");
                    return;
                }
            }

            // Record the request
            RecordRequest(clientIp, teamSlug, now, context.Request.Path);

            // Add rate limit headers
            AddRateLimitHeaders(context, clientIp, teamSlug, now);

            await _next(context);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in rate limiting middleware");
            await _next(context);
        }
    }

    private bool ShouldSkipRateLimit(HttpContext context)
    {
        var path = context.Request.Path.Value?.ToLowerInvariant();
        return _options.ExcludedPaths.Any(excludedPath => 
            path?.StartsWith(excludedPath.ToLowerInvariant()) == true);
    }

    private static string? GetClientIpAddress(HttpContext context)
    {
        // Check for forwarded headers first (for load balancers/proxies)
        var forwardedFor = context.Request.Headers["X-Forwarded-For"].FirstOrDefault();
        if (!string.IsNullOrEmpty(forwardedFor))
        {
            var firstIp = forwardedFor.Split(',').FirstOrDefault()?.Trim();
            if (!string.IsNullOrEmpty(firstIp))
                return firstIp;
        }

        var realIp = context.Request.Headers["X-Real-IP"].FirstOrDefault();
        if (!string.IsNullOrEmpty(realIp))
            return realIp;

        return context.Connection.RemoteIpAddress?.ToString();
    }

    private async Task<string?> ExtractTeamSlugAsync(HttpContext context)
    {
        try
        {
            if (context.Request.ContentLength > 0 && context.Request.ContentType?.Contains("application/json") == true)
            {
                context.Request.EnableBuffering();
                context.Request.Body.Position = 0;

                using var reader = new StreamReader(context.Request.Body, leaveOpen: true);
                var body = await reader.ReadToEndAsync();
                context.Request.Body.Position = 0;

                if (!string.IsNullOrWhiteSpace(body))
                {
                    var jsonDoc = JsonDocument.Parse(body);
                    if (jsonDoc.RootElement.TryGetProperty("TeamSlug", out var teamSlugElement) ||
                        jsonDoc.RootElement.TryGetProperty("teamSlug", out teamSlugElement))
                    {
                        return teamSlugElement.GetString();
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not extract TeamSlug from request body");
        }

        return null;
    }

    private static bool IsAuthenticationEndpoint(HttpContext context)
    {
        var path = context.Request.Path.Value?.ToLowerInvariant();
        var authPaths = new[] { "/api/init", "/api/confirm", "/api/cancel", "/api/check" };
        return authPaths.Any(authPath => path?.Contains(authPath) == true);
    }

    private bool IsIpBlocked(string? clientIp, DateTime now)
    {
        if (string.IsNullOrEmpty(clientIp))
            return false;

        if (!_blockedIps.TryGetValue(clientIp, out var blockUntil))
            return false;

        if (now >= blockUntil)
        {
            _blockedIps.TryRemove(clientIp, out _);
            return false;
        }

        return true;
    }

    private bool IsTeamBlocked(string teamSlug, DateTime now)
    {
        if (!_blockedTeams.TryGetValue(teamSlug, out var blockUntil))
            return false;

        if (now >= blockUntil)
        {
            _blockedTeams.TryRemove(teamSlug, out _);
            return false;
        }

        return true;
    }

    private bool CheckIpRateLimit(string clientIp, DateTime now)
    {
        var windowStart = now - _options.IpRateLimit.Window;
        
        var requests = _ipRequests.AddOrUpdate(clientIp,
            new List<RequestRecord>(),
            (_, existing) => existing.Where(r => r.Timestamp > windowStart).ToList());

        return requests.Count < _options.IpRateLimit.MaxRequests;
    }

    private bool CheckTeamRateLimit(string teamSlug, DateTime now)
    {
        var windowStart = now - _options.AuthenticationRateLimit.Window;
        
        var requests = _teamRequests.AddOrUpdate(teamSlug,
            new List<RequestRecord>(),
            (_, existing) => existing.Where(r => r.Timestamp > windowStart).ToList());

        return requests.Count < _options.AuthenticationRateLimit.MaxRequests;
    }

    private void RecordRequest(string? clientIp, string? teamSlug, DateTime timestamp, string path)
    {
        var record = new RequestRecord(timestamp, path);

        // Record IP-based request
        if (!string.IsNullOrEmpty(clientIp))
        {
            _ipRequests.AddOrUpdate(clientIp,
                new List<RequestRecord> { record },
                (_, existing) =>
                {
                    existing.Add(record);
                    return existing;
                });
        }

        // Record team-based request for authentication endpoints
        if (!string.IsNullOrEmpty(teamSlug) && IsAuthenticationEndpoint(new DefaultHttpContext { Request = { Path = path } }))
        {
            _teamRequests.AddOrUpdate(teamSlug,
                new List<RequestRecord> { record },
                (_, existing) =>
                {
                    existing.Add(record);
                    return existing;
                });
        }
    }

    private void AddRateLimitHeaders(HttpContext context, string? clientIp, string? teamSlug, DateTime now)
    {
        if (!string.IsNullOrEmpty(clientIp) && _ipRequests.TryGetValue(clientIp, out var ipRequests))
        {
            var windowStart = now - _options.IpRateLimit.Window;
            var currentCount = ipRequests.Count(r => r.Timestamp > windowStart);
            var remaining = Math.Max(0, _options.IpRateLimit.MaxRequests - currentCount);
            var resetTime = now.Add(_options.IpRateLimit.Window);

            context.Response.Headers.Add("X-RateLimit-Limit", _options.IpRateLimit.MaxRequests.ToString());
            context.Response.Headers.Add("X-RateLimit-Remaining", remaining.ToString());
            context.Response.Headers.Add("X-RateLimit-Reset", ((DateTimeOffset)resetTime).ToUnixTimeSeconds().ToString());
        }

        if (!string.IsNullOrEmpty(teamSlug) && _teamRequests.TryGetValue(teamSlug, out var teamRequests))
        {
            var windowStart = now - _options.AuthenticationRateLimit.Window;
            var currentCount = teamRequests.Count(r => r.Timestamp > windowStart);
            var remaining = Math.Max(0, _options.AuthenticationRateLimit.MaxRequests - currentCount);

            context.Response.Headers.Add("X-Auth-RateLimit-Limit", _options.AuthenticationRateLimit.MaxRequests.ToString());
            context.Response.Headers.Add("X-Auth-RateLimit-Remaining", remaining.ToString());
        }
    }

    private TimeSpan CalculateBlockDuration(int violationCount)
    {
        return _options.EnableProgressiveBlocking && _options.ProgressiveBlockDurations.TryGetValue(violationCount, out var duration)
            ? duration
            : _options.DefaultBlockDuration;
    }

    private async Task WriteRateLimitExceededResponse(HttpContext context, string message)
    {
        context.Response.StatusCode = (int)HttpStatusCode.TooManyRequests;
        context.Response.ContentType = "application/json";

        var response = new
        {
            Success = false,
            ErrorCode = "RATE_LIMIT_EXCEEDED",
            Message = message,
            Details = "Too many requests. Please try again later.",
            RetryAfter = _options.DefaultBlockDuration.TotalSeconds,
            CorrelationId = context.TraceIdentifier,
            Timestamp = DateTime.UtcNow
        };

        var json = JsonSerializer.Serialize(response, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        await context.Response.WriteAsync(json);
        
        _logger.LogWarning("Rate limit exceeded: {Message}, IP: {IP}, CorrelationId: {CorrelationId}",
            message, GetClientIpAddress(context), context.TraceIdentifier);
    }

    private async Task CleanupExpiredRecords()
    {
        while (true)
        {
            try
            {
                var now = DateTime.UtcNow;
                var cleanupCutoff = now - TimeSpan.FromHours(1); // Keep records for 1 hour max

                // Cleanup IP requests
                var expiredIpKeys = _ipRequests
                    .Where(kvp => kvp.Value.All(r => r.Timestamp < cleanupCutoff))
                    .Select(kvp => kvp.Key)
                    .ToList();

                foreach (var key in expiredIpKeys)
                {
                    _ipRequests.TryRemove(key, out _);
                }

                // Cleanup team requests
                var expiredTeamKeys = _teamRequests
                    .Where(kvp => kvp.Value.All(r => r.Timestamp < cleanupCutoff))
                    .Select(kvp => kvp.Key)
                    .ToList();

                foreach (var key in expiredTeamKeys)
                {
                    _teamRequests.TryRemove(key, out _);
                }

                // Cleanup expired blocks
                var expiredBlockedIps = _blockedIps
                    .Where(kvp => now >= kvp.Value)
                    .Select(kvp => kvp.Key)
                    .ToList();

                foreach (var ip in expiredBlockedIps)
                {
                    _blockedIps.TryRemove(ip, out _);
                }

                var expiredBlockedTeams = _blockedTeams
                    .Where(kvp => now >= kvp.Value)
                    .Select(kvp => kvp.Key)
                    .ToList();

                foreach (var team in expiredBlockedTeams)
                {
                    _blockedTeams.TryRemove(team, out _);
                }

                if (expiredIpKeys.Count > 0 || expiredTeamKeys.Count > 0 || expiredBlockedIps.Count > 0 || expiredBlockedTeams.Count > 0)
                {
                    _logger.LogDebug("Cleaned up {IpRecords} IP records, {TeamRecords} team records, {BlockedIps} blocked IPs, {BlockedTeams} blocked teams",
                        expiredIpKeys.Count, expiredTeamKeys.Count, expiredBlockedIps.Count, expiredBlockedTeams.Count);
                }

                await Task.Delay(TimeSpan.FromMinutes(5)); // Run cleanup every 5 minutes
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during rate limiting cleanup");
                await Task.Delay(TimeSpan.FromMinutes(1)); // Shorter delay on error
            }
        }
    }

    private record RequestRecord(DateTime Timestamp, string Path);
}

public class AuthenticationRateLimitingOptions
{
    public RateLimitConfig IpRateLimit { get; set; } = new()
    {
        MaxRequests = 100,
        Window = TimeSpan.FromMinutes(1)
    };

    public RateLimitConfig AuthenticationRateLimit { get; set; } = new()
    {
        MaxRequests = 10,
        Window = TimeSpan.FromMinutes(1)
    };

    public List<string> ExcludedPaths { get; set; } = new()
    {
        "/health",
        "/metrics",
        "/swagger"
    };

    public bool EnableProgressiveBlocking { get; set; } = true;
    
    public Dictionary<int, TimeSpan> ProgressiveBlockDurations { get; set; } = new()
    {
        { 1, TimeSpan.FromMinutes(1) },
        { 2, TimeSpan.FromMinutes(5) },
        { 3, TimeSpan.FromMinutes(15) },
        { 4, TimeSpan.FromMinutes(30) },
        { 5, TimeSpan.FromHours(1) }
    };

    public TimeSpan DefaultBlockDuration { get; set; } = TimeSpan.FromMinutes(15);
    public bool EnableDetailedLogging { get; set; } = true;
}

public class RateLimitConfig
{
    public int MaxRequests { get; set; }
    public TimeSpan Window { get; set; }
}

// Extension methods for easier middleware registration
public static class AuthenticationRateLimitingMiddlewareExtensions
{
    public static IApplicationBuilder UseAuthenticationRateLimiting(
        this IApplicationBuilder builder,
        Action<AuthenticationRateLimitingOptions>? configureOptions = null)
    {
        var options = new AuthenticationRateLimitingOptions();
        configureOptions?.Invoke(options);

        return builder.UseMiddleware<AuthenticationRateLimitingMiddleware>(Options.Create(options));
    }

    public static IServiceCollection AddAuthenticationRateLimiting(
        this IServiceCollection services,
        Action<AuthenticationRateLimitingOptions>? configureOptions = null)
    {
        if (configureOptions != null)
        {
            services.Configure(configureOptions);
        }

        return services;
    }
}