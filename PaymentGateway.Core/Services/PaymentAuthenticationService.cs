using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Caching.Memory;
using PaymentGateway.Core.Repositories;
using PaymentGateway.Core.Entities;
using PaymentGateway.Core.Interfaces;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace PaymentGateway.Core.Services;

/// <summary>
/// Service interface for payment authentication using SHA-256 token generation
/// </summary>
public interface IPaymentAuthenticationService
{
    Task<PaymentAuthenticationResult> AuthenticateRequestAsync(Dictionary<string, object> requestParameters, CancellationToken cancellationToken = default);
    Task<PaymentAuthenticationResult> AuthenticateAsync(Dictionary<string, object> requestParameters, CancellationToken cancellationToken = default);
    Task<string> GenerateTokenAsync(string teamSlug, Dictionary<string, object> requestParameters, CancellationToken cancellationToken = default);
    Task<bool> ValidateTokenAsync(string teamSlug, string providedToken, Dictionary<string, object> requestParameters, CancellationToken cancellationToken = default);
    Task<Team?> GetTeamBySlugAsync(string teamSlug, CancellationToken cancellationToken = default);
    Task<PaymentAuthenticationMetrics> GetAuthenticationMetricsAsync(string teamSlug, CancellationToken cancellationToken = default);
}

/// <summary>
/// Payment authentication service implementation based on payment-authentication.md specification
/// </summary>
public class PaymentAuthenticationService : IPaymentAuthenticationService
{
    private readonly ITeamRepository _teamRepository;
    private readonly IMemoryCache _cache;
    private readonly ILogger<PaymentAuthenticationService> _logger;
    private readonly IComprehensiveAuditService _auditService;
    private readonly IMetricsService _metricsService;
    private readonly TimeSpan _cacheExpiration = TimeSpan.FromMinutes(15);

    public PaymentAuthenticationService(
        ITeamRepository teamRepository,
        IMemoryCache cache,
        ILogger<PaymentAuthenticationService> logger,
        IComprehensiveAuditService auditService,
        IMetricsService metricsService)
    {
        _teamRepository = teamRepository;
        _cache = cache;
        _logger = logger;
        _auditService = auditService;
        _metricsService = metricsService;
    }

    public async Task<PaymentAuthenticationResult> AuthenticateAsync(Dictionary<string, object> requestParameters, CancellationToken cancellationToken = default)
    {
        return await AuthenticateRequestAsync(requestParameters, cancellationToken);
    }

    public async Task<PaymentAuthenticationResult> AuthenticateRequestAsync(Dictionary<string, object> requestParameters, CancellationToken cancellationToken = default)
    {
        var authStartTime = DateTime.UtcNow;
        
        try
        {
            // Extract TeamSlug and Token from request parameters
            if (!requestParameters.TryGetValue("TeamSlug", out var teamSlugObj) || teamSlugObj == null)
            {
                await RecordAuthenticationMetricsAsync("unknown", false, DateTime.UtcNow - authStartTime);
                return CreateFailureResult("TEAM_SLUG_MISSING", "TeamSlug parameter is required");
            }

            if (!requestParameters.TryGetValue("Token", out var tokenObj) || tokenObj == null)
            {
                await RecordAuthenticationMetricsAsync(teamSlugObj.ToString()!, false, DateTime.UtcNow - authStartTime);
                return CreateFailureResult("TOKEN_MISSING", "Token parameter is required");
            }

            var teamSlug = teamSlugObj.ToString()!;
            var providedToken = tokenObj.ToString()!;

            _logger.LogDebug("Starting authentication for TeamSlug: {TeamSlug}", teamSlug);

            // Validate the token
            var isTokenValid = await ValidateTokenAsync(teamSlug, providedToken, requestParameters, cancellationToken);
            
            if (!isTokenValid)
            {
                await _auditService.LogSystemEventAsync(
                    AuditAction.AuthenticationFailed,
                    "PaymentAuthentication",
                    $"Token validation failed for TeamSlug: {teamSlug}");

                await RecordAuthenticationMetricsAsync(teamSlug, false, DateTime.UtcNow - authStartTime);
                return CreateFailureResult("TOKEN_INVALID", "Token validation failed");
            }

            // Get team information
            var team = await GetTeamBySlugAsync(teamSlug, cancellationToken);
            if (team == null)
            {
                await RecordAuthenticationMetricsAsync(teamSlug, false, DateTime.UtcNow - authStartTime);
                return CreateFailureResult("TEAM_NOT_FOUND", $"Team '{teamSlug}' not found or inactive");
            }

            // Log successful authentication
            await _auditService.LogSystemEventAsync(
                AuditAction.AuthenticationSucceeded,
                "PaymentAuthentication",
                $"Successful authentication for TeamSlug: {teamSlug}");

            await RecordAuthenticationMetricsAsync(teamSlug, true, DateTime.UtcNow - authStartTime);

            _logger.LogDebug("Authentication successful for TeamSlug: {TeamSlug}", teamSlug);

            return new PaymentAuthenticationResult
            {
                IsAuthenticated = true,
                TeamSlug = teamSlug,
                TeamId = team.Id,
                Team = team,
                AuthenticationTime = DateTime.UtcNow - authStartTime
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during payment authentication");
            await RecordAuthenticationMetricsAsync("unknown", false, DateTime.UtcNow - authStartTime);
            return CreateFailureResult("AUTHENTICATION_ERROR", "Internal authentication error");
        }
    }

    public async Task<string> GenerateTokenAsync(string teamSlug, Dictionary<string, object> requestParameters, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Generating token for TeamSlug: {TeamSlug}", teamSlug);

            // Get team's password from database
            var team = await GetTeamBySlugAsync(teamSlug, cancellationToken);
            if (team == null)
            {
                throw new ArgumentException($"Team '{teamSlug}' not found", nameof(teamSlug));
            }

            // Step 1: Extract root-level parameters only (exclude nested objects and arrays)
            var tokenParams = new Dictionary<string, string>();
            foreach (var kvp in requestParameters)
            {
                // Skip Token parameter itself if present
                if (kvp.Key.Equals("Token", StringComparison.OrdinalIgnoreCase))
                    continue;

                // Only include scalar values (not objects or arrays)
                if (kvp.Value != null && !IsComplexType(kvp.Value))
                {
                    tokenParams[kvp.Key] = kvp.Value.ToString()!;
                }
            }

            // Step 2: Add password
            tokenParams["Password"] = team.PasswordHash; // Using PasswordHash as the secret key

            // Step 3: Sort alphabetically by key
            var sortedKeys = tokenParams.Keys.OrderBy(k => k, StringComparer.Ordinal).ToList();

            // Step 4: Concatenate values in sorted order
            var concatenatedValues = string.Join("", sortedKeys.Select(key => tokenParams[key]));

            // Step 5: Generate SHA-256 hash
            using var sha256 = SHA256.Create();
            var hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(concatenatedValues));
            var token = Convert.ToHexString(hashBytes).ToLowerInvariant();

            _logger.LogDebug("Token generated successfully for TeamSlug: {TeamSlug}", teamSlug);
            return token;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating token for TeamSlug: {TeamSlug}", teamSlug);
            throw;
        }
    }

    public async Task<bool> ValidateTokenAsync(string teamSlug, string providedToken, Dictionary<string, object> requestParameters, CancellationToken cancellationToken = default)
    {
        try
        {
            if (string.IsNullOrEmpty(providedToken))
                return false;

            // Generate expected token
            var expectedToken = await GenerateTokenAsync(teamSlug, requestParameters, cancellationToken);
            
            // Perform secure token comparison (constant-time to prevent timing attacks)
            return SecureTokenCompare(providedToken, expectedToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating token for TeamSlug: {TeamSlug}", teamSlug);
            return false;
        }
    }

    public async Task<Team?> GetTeamBySlugAsync(string teamSlug, CancellationToken cancellationToken = default)
    {
        try
        {
            var cacheKey = $"team_auth_{teamSlug}";
            
            // Try to get from cache first
            if (_cache.TryGetValue(cacheKey, out Team? cachedTeam))
            {
                return cachedTeam;
            }

            // Get from database
            var team = await _teamRepository.GetByTeamSlugAsync(teamSlug, cancellationToken);
            
            // Cache the result if team exists and is active
            if (team != null && team.IsActive)
            {
                _cache.Set(cacheKey, team, _cacheExpiration);
            }

            return team?.IsActive == true ? team : null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting team by slug: {TeamSlug}", teamSlug);
            return null;
        }
    }

    public async Task<PaymentAuthenticationMetrics> GetAuthenticationMetricsAsync(string teamSlug, CancellationToken cancellationToken = default)
    {
        try
        {
            // In a real implementation, this would query metrics from a metrics store
            await Task.CompletedTask;
            
            return new PaymentAuthenticationMetrics
            {
                TeamSlug = teamSlug,
                TotalAuthenticationAttempts = 1000,
                SuccessfulAuthentications = 950,
                FailedAuthentications = 50,
                SuccessRate = 0.95,
                AverageAuthenticationTime = TimeSpan.FromMilliseconds(125),
                LastAuthenticationAt = DateTime.UtcNow,
                LastUpdated = DateTime.UtcNow
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving authentication metrics for TeamSlug: {TeamSlug}", teamSlug);
            return new PaymentAuthenticationMetrics
            {
                TeamSlug = teamSlug,
                LastUpdated = DateTime.UtcNow
            };
        }
    }

    private bool IsComplexType(object value)
    {
        // Check if the value is a complex type (object or array) that should be excluded from token generation
        var valueType = value.GetType();
        
        // Exclude arrays and collections
        if (valueType.IsArray || value is System.Collections.IEnumerable)
            return true;

        // Exclude custom objects (not primitive types or strings)
        if (!valueType.IsPrimitive && valueType != typeof(string) && valueType != typeof(decimal) && 
            valueType != typeof(DateTime) && valueType != typeof(Guid) && !valueType.IsEnum)
        {
            // Check if it's a simple value type we should include
            if (valueType == typeof(int) || valueType == typeof(long) || valueType == typeof(double) || 
                valueType == typeof(float) || valueType == typeof(bool))
                return false;

            return true;
        }

        return false;
    }

    private bool SecureTokenCompare(string token1, string token2)
    {
        if (string.IsNullOrEmpty(token1) || string.IsNullOrEmpty(token2))
            return false;

        if (token1.Length != token2.Length)
            return false;

        // Constant-time comparison to prevent timing attacks
        var result = 0;
        for (int i = 0; i < token1.Length; i++)
        {
            result |= token1[i] ^ token2[i];
        }

        return result == 0;
    }

    private PaymentAuthenticationResult CreateFailureResult(string errorCode, string errorMessage)
    {
        return new PaymentAuthenticationResult
        {
            IsAuthenticated = false,
            ErrorCode = errorCode,
            ErrorMessage = errorMessage,
            AuthenticationTime = TimeSpan.Zero
        };
    }

    private async Task RecordAuthenticationMetricsAsync(string teamSlug, bool success, TimeSpan duration)
    {
        try
        {
            await _metricsService.RecordCounterAsync("payment_authentications_total", 1, new Dictionary<string, string>
            {
                { "team_slug", teamSlug },
                { "success", success.ToString().ToLowerInvariant() }
            });

            await _metricsService.RecordHistogramAsync("payment_authentication_duration_seconds", duration.TotalSeconds, new Dictionary<string, string>
            {
                { "team_slug", teamSlug }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error recording authentication metrics for team {TeamSlug}", teamSlug);
        }
    }
}

// Supporting classes for payment authentication
public class PaymentAuthenticationResult
{
    public bool IsAuthenticated { get; set; }
    public string? TeamSlug { get; set; }
    public Guid? TeamId { get; set; }
    public Team? Team { get; set; }
    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public string? FailureReason => ErrorMessage; // Alias for controller compatibility
    public TimeSpan AuthenticationTime { get; set; }
}

public class PaymentAuthenticationMetrics
{
    public string TeamSlug { get; set; } = string.Empty;
    public long TotalAuthenticationAttempts { get; set; }
    public long SuccessfulAuthentications { get; set; }
    public long FailedAuthentications { get; set; }
    public double SuccessRate { get; set; }
    public TimeSpan AverageAuthenticationTime { get; set; }
    public DateTime? LastAuthenticationAt { get; set; }
    public DateTime LastUpdated { get; set; }
}