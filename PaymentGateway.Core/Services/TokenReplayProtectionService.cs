using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Caching.Memory;
using PaymentGateway.Core.Interfaces;
using PaymentGateway.Core.Entities;
using System.Security.Cryptography;
using System.Text;

namespace PaymentGateway.Core.Services;

/// <summary>
/// Service interface for token replay protection
/// </summary>
public interface ITokenReplayProtectionService
{
    Task<bool> IsTokenReplayAsync(string teamSlug, string token, Dictionary<string, object> requestParameters, CancellationToken cancellationToken = default);
    Task RecordTokenUsageAsync(string teamSlug, string token, Dictionary<string, object> requestParameters, CancellationToken cancellationToken = default);
    Task<bool> ValidateTimestampAsync(Dictionary<string, object> requestParameters, CancellationToken cancellationToken = default);
    Task<bool> ValidateNonceAsync(string teamSlug, string nonce, CancellationToken cancellationToken = default);
    Task CleanupExpiredTokensAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Token replay protection service implementation
/// </summary>
public class TokenReplayProtectionService : ITokenReplayProtectionService
{
    private readonly IMemoryCache _cache;
    private readonly ILogger<TokenReplayProtectionService> _logger;
    private readonly IComprehensiveAuditService _auditService;
    private readonly IMetricsService _metricsService;

    // Replay protection settings
    private readonly TimeSpan _tokenValidityWindow = TimeSpan.FromMinutes(15);
    private readonly TimeSpan _timestampTolerance = TimeSpan.FromMinutes(5);
    private readonly TimeSpan _cacheExpiry = TimeSpan.FromHours(1);

    public TokenReplayProtectionService(
        IMemoryCache cache,
        ILogger<TokenReplayProtectionService> logger,
        IComprehensiveAuditService auditService,
        IMetricsService metricsService)
    {
        _cache = cache;
        _logger = logger;
        _auditService = auditService;
        _metricsService = metricsService;
    }

    public async Task<bool> IsTokenReplayAsync(string teamSlug, string token, Dictionary<string, object> requestParameters, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Checking token replay for TeamSlug: {TeamSlug}", teamSlug);

            // Create token fingerprint for unique identification
            var tokenFingerprint = GenerateTokenFingerprint(teamSlug, token, requestParameters);
            var cacheKey = $"token_replay_{tokenFingerprint}";

            // Check if token has been used before
            if (_cache.TryGetValue(cacheKey, out TokenUsageRecord? usageRecord))
            {
                _logger.LogWarning("Token replay detected for TeamSlug: {TeamSlug}, Fingerprint: {Fingerprint}", 
                    teamSlug, tokenFingerprint);

                await _auditService.LogSystemEventAsync(
                    AuditAction.TokenReplayDetected,
                    "TokenReplayProtection",
                    $"Token replay detected for TeamSlug: {teamSlug}, Fingerprint: {tokenFingerprint}");

                await RecordReplayMetricsAsync(teamSlug, "replay_detected");
                return true; // Token replay detected
            }

            // Validate timestamp to prevent old token reuse
            if (!await ValidateTimestampAsync(requestParameters, cancellationToken))
            {
                _logger.LogWarning("Token timestamp validation failed for TeamSlug: {TeamSlug}", teamSlug);
                
                await RecordReplayMetricsAsync(teamSlug, "timestamp_invalid");
                return true; // Consider invalid timestamp as potential replay
            }

            await RecordReplayMetricsAsync(teamSlug, "token_valid");
            return false; // No replay detected
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking token replay for TeamSlug: {TeamSlug}", teamSlug);
            await RecordReplayMetricsAsync(teamSlug, "check_error");
            return false; // Allow request on error to prevent blocking legitimate requests
        }
    }

    public async Task RecordTokenUsageAsync(string teamSlug, string token, Dictionary<string, object> requestParameters, CancellationToken cancellationToken = default)
    {
        try
        {
            // Create token fingerprint for unique identification
            var tokenFingerprint = GenerateTokenFingerprint(teamSlug, token, requestParameters);
            var cacheKey = $"token_replay_{tokenFingerprint}";

            var usageRecord = new TokenUsageRecord
            {
                TeamSlug = teamSlug,
                TokenFingerprint = tokenFingerprint,
                UsedAt = DateTime.UtcNow,
                RequestParameters = ExtractKeyParameters(requestParameters)
            };

            // Store token usage with expiry
            _cache.Set(cacheKey, usageRecord, _cacheExpiry);

            _logger.LogDebug("Recorded token usage for TeamSlug: {TeamSlug}, Fingerprint: {Fingerprint}", 
                teamSlug, tokenFingerprint);

            await _auditService.LogSystemEventAsync(
                AuditAction.TokenUsed,
                "TokenReplayProtection",
                $"Token recorded for replay protection - TeamSlug: {teamSlug}");

            await RecordReplayMetricsAsync(teamSlug, "token_recorded");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error recording token usage for TeamSlug: {TeamSlug}", teamSlug);
            await RecordReplayMetricsAsync(teamSlug, "record_error");
        }

        await Task.CompletedTask;
    }

    public async Task<bool> ValidateTimestampAsync(Dictionary<string, object> requestParameters, CancellationToken cancellationToken = default)
    {
        try
        {
            // Check if timestamp parameter exists
            if (!requestParameters.TryGetValue("Timestamp", out var timestampObj) && 
                !requestParameters.TryGetValue("timestamp", out timestampObj))
            {
                // If no timestamp provided, generate one based on current time for basic protection
                _logger.LogDebug("No timestamp provided in request parameters");
                return true; // Allow requests without timestamp for backwards compatibility
            }

            // Parse timestamp
            if (!DateTime.TryParse(timestampObj?.ToString(), out var requestTimestamp))
            {
                _logger.LogWarning("Invalid timestamp format: {Timestamp}", timestampObj);
                return false;
            }

            var now = DateTime.UtcNow;
            var timeDifference = Math.Abs((now - requestTimestamp).TotalMinutes);

            // Check if timestamp is within acceptable range
            if (timeDifference > _timestampTolerance.TotalMinutes)
            {
                _logger.LogWarning("Timestamp outside acceptable range. Difference: {TimeDifference} minutes", timeDifference);
                return false;
            }

            await Task.CompletedTask;
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating timestamp");
            return false;
        }
    }

    public async Task<bool> ValidateNonceAsync(string teamSlug, string nonce, CancellationToken cancellationToken = default)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(nonce))
            {
                return true; // Allow requests without nonce for backwards compatibility
            }

            var cacheKey = $"nonce_{teamSlug}_{nonce}";

            // Check if nonce has been used before
            if (_cache.TryGetValue(cacheKey, out _))
            {
                _logger.LogWarning("Nonce replay detected for TeamSlug: {TeamSlug}, Nonce: {Nonce}", teamSlug, nonce);
                
                await _auditService.LogSystemEventAsync(
                    AuditAction.NonceReplayDetected,
                    "TokenReplayProtection",
                    $"Nonce replay detected for TeamSlug: {teamSlug}, Nonce: {nonce}");

                return false; // Nonce replay detected
            }

            // Record nonce usage
            _cache.Set(cacheKey, DateTime.UtcNow, _tokenValidityWindow);

            await Task.CompletedTask;
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating nonce for TeamSlug: {TeamSlug}", teamSlug);
            return true; // Allow request on error
        }
    }

    public async Task CleanupExpiredTokensAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            // Note: IMemoryCache automatically handles expiration cleanup
            // This method can be extended to clean up persistent storage if used
            
            _logger.LogDebug("Token cleanup completed");
            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during token cleanup");
        }
    }

    private string GenerateTokenFingerprint(string teamSlug, string token, Dictionary<string, object> requestParameters)
    {
        // Create a unique fingerprint based on team, token, and key parameters
        var keyParameters = ExtractKeyParameters(requestParameters);
        var fingerprintData = new
        {
            TeamSlug = teamSlug,
            Token = token,
            Parameters = keyParameters
        };

        var fingerprintJson = System.Text.Json.JsonSerializer.Serialize(fingerprintData);
        
        using var sha256 = SHA256.Create();
        var hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(fingerprintJson));
        return Convert.ToHexString(hashBytes).ToLowerInvariant()[..16]; // Use first 16 characters
    }

    private Dictionary<string, object> ExtractKeyParameters(Dictionary<string, object> requestParameters)
    {
        // Extract key parameters that uniquely identify the request
        var keyParams = new Dictionary<string, object>();
        
        var importantKeys = new[] { "OrderId", "Amount", "TeamSlug", "Timestamp", "Nonce" };
        
        foreach (var key in importantKeys)
        {
            if (requestParameters.TryGetValue(key, out var value))
            {
                keyParams[key] = value;
            }
        }

        return keyParams;
    }

    private async Task RecordReplayMetricsAsync(string teamSlug, string operation)
    {
        try
        {
            await _metricsService.RecordCounterAsync("token_replay_protection_total", 1, new Dictionary<string, string>
            {
                { "team_slug", teamSlug },
                { "operation", operation }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error recording replay protection metrics for team {TeamSlug}", teamSlug);
        }
    }
}

// Supporting classes
public class TokenUsageRecord
{
    public string TeamSlug { get; set; } = string.Empty;
    public string TokenFingerprint { get; set; } = string.Empty;
    public DateTime UsedAt { get; set; }
    public Dictionary<string, object> RequestParameters { get; set; } = new();
}

// Additional audit actions for token replay protection
public enum TokenReplayAuditAction
{
    TokenUsed = 300,
    TokenReplayDetected = 301,
    NonceReplayDetected = 302,
    TimestampValidationFailed = 303
}