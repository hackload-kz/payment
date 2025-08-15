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
            // Extract teamSlug and token from request parameters (case-insensitive)
            if (!requestParameters.TryGetValue("teamSlug", out var teamSlugObj) || teamSlugObj == null)
            {
                await RecordAuthenticationMetricsAsync("unknown", false, DateTime.UtcNow - authStartTime);
                return CreateFailureResult("TEAM_SLUG_MISSING", "teamSlug parameter is required");
            }

            if (!requestParameters.TryGetValue("token", out var tokenObj) || tokenObj == null)
            {
                await RecordAuthenticationMetricsAsync(teamSlugObj.ToString()!, false, DateTime.UtcNow - authStartTime);
                return CreateFailureResult("TOKEN_MISSING", "token parameter is required");
            }

            var teamSlug = teamSlugObj.ToString()!;
            var providedToken = tokenObj.ToString()!;

            _logger.LogDebug("Starting authentication for TeamSlug: {TeamSlug}", teamSlug);

            // Validate the token using the raw password from database
            var isTokenValid = await ValidateTokenAsync(teamSlug, providedToken, requestParameters, cancellationToken);
            
            if (!isTokenValid)
            {
                try
                {
                    await _auditService.LogSystemEventAsync(
                        AuditAction.AuthenticationFailed,
                        "PaymentAuthentication",
                        $"Token validation failed for TeamSlug: {teamSlug}");
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to log audit event for token validation failure");
                }

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
            try
            {
                await _auditService.LogSystemEventAsync(
                    AuditAction.AuthenticationSucceeded,
                    "PaymentAuthentication",
                    $"Successful authentication for TeamSlug: {teamSlug}");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to log audit event for successful authentication");
            }

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

            // DEBUG: Log received parameters from filter
            _logger.LogInformation("SERVICE DEBUG: Received {Count} parameters from filter", requestParameters.Count);
            foreach (var kvp in requestParameters.OrderBy(x => x.Key))
            {
                _logger.LogInformation("SERVICE DEBUG: Received parameter {Key} = {Value} (Type: {Type})", 
                    kvp.Key, kvp.Value, kvp.Value?.GetType().Name ?? "null");
            }

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
                // Skip token parameter itself if present
                if (kvp.Key.Equals("token", StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogInformation("SERVICE DEBUG: Skipping token parameter");
                    continue;
                }

                // Only include scalar values (not objects or arrays)
                if (kvp.Value != null && !IsComplexType(kvp.Value))
                {
                    tokenParams[kvp.Key] = kvp.Value.ToString()!;
                    _logger.LogInformation("SERVICE DEBUG: Including parameter {Key} = {Value}", kvp.Key, kvp.Value);
                }
                else
                {
                    _logger.LogInformation("SERVICE DEBUG: Excluding parameter {Key} = {Value} (complex type or null)", kvp.Key, kvp.Value);
                }
            }

            // Step 2: Add password (now using raw password for simplicity)
            tokenParams["Password"] = team.Password;

            // Step 3: SPECIAL HANDLING FOR PaymentCheck - NON-ALPHABETICAL ORDER
            // Check if this is a PaymentCheck request by looking for PaymentId parameter
            var isPaymentCheckRequest = tokenParams.ContainsKey("PaymentId") && tokenParams.ContainsKey("TeamSlug") && !tokenParams.ContainsKey("Amount");
            
            string concatenatedValues;
            if (isPaymentCheckRequest)
            {
                // SPECIAL ORDER for PaymentCheck: PaymentId + Password + TeamSlug (NOT alphabetical)
                _logger.LogInformation("DEBUG: Detected PaymentCheck request - using SPECIAL non-alphabetical order");
                _logger.LogInformation("DEBUG: PaymentCheck formula: PaymentId + Password + TeamSlug");
                
                var paymentId = tokenParams["PaymentId"];
                var password = tokenParams["Password"];
                var teamSlugParam = tokenParams["TeamSlug"];
                
                concatenatedValues = paymentId + password + teamSlugParam;
                
                _logger.LogInformation("DEBUG: PaymentCheck token string: PaymentId({PaymentId}) + Password(***) + TeamSlug({TeamSlug})", 
                    paymentId, teamSlugParam);
            }
            else
            {
                // Standard alphabetical order for all other endpoints
                var sortedKeys = tokenParams.Keys.OrderBy(k => k, StringComparer.Ordinal).ToList();
                concatenatedValues = string.Join("", sortedKeys.Select(key => tokenParams[key]));
                
                _logger.LogInformation("DEBUG: Standard alphabetical order for non-PaymentCheck request");
                foreach (var key in sortedKeys)
                {
                    _logger.LogInformation("DEBUG: Parameter {Key} = {Value}", key, key == "Password" ? "***" : tokenParams[key]);
                }
            }

            _logger.LogInformation("DEBUG: Final concatenated string: {ConcatenatedString}", 
                concatenatedValues.Contains(team.Password) ? concatenatedValues.Replace(team.Password, "***PASSWORD***") : concatenatedValues);

            // Step 5: Generate SHA-256 hash
            using var sha256 = SHA256.Create();
            var hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(concatenatedValues));
            var token = Convert.ToHexString(hashBytes).ToLowerInvariant();

            _logger.LogInformation("DEBUG: Generated token: {Token}", token);
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

            // Generate expected token using raw password from database
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
        
        // CRITICAL FIX: Exclude arrays and collections but NOT strings
        // String implements IEnumerable<char> but should not be considered complex
        if (valueType.IsArray)
            return true;
            
        // Exclude collections but not strings
        if (value is System.Collections.IEnumerable && valueType != typeof(string))
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