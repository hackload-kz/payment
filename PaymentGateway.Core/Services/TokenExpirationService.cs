using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace PaymentGateway.Core.Services;

public interface ITokenExpirationService
{
    Task<ExpiringToken> CreateExpiringTokenAsync(string teamSlug, Dictionary<string, object> requestParameters, string password, TimeSpan? customExpiry = null);
    Task<TokenValidationResult> ValidateExpiringTokenAsync(string teamSlug, Dictionary<string, object> requestParameters, string password, string token);
    Task<RefreshTokenResult> RefreshTokenAsync(string refreshToken, string teamSlug);
    Task<bool> RevokeTokenAsync(string token);
    Task<bool> RevokeAllTokensForTeamAsync(string teamSlug);
    Task<List<ActiveToken>> GetActiveTokensForTeamAsync(string teamSlug);
    Task CleanupExpiredTokensAsync();
}

public record ExpiringToken(
    string Token,
    string? RefreshToken,
    DateTime ExpiresAt,
    DateTime IssuedAt,
    string TeamSlug,
    Dictionary<string, string> Claims);

public record TokenValidationResult(
    bool IsValid,
    bool IsExpired,
    ExpiringToken? TokenInfo,
    string? ErrorMessage,
    TimeSpan? TimeUntilExpiry);

public record RefreshTokenResult(
    bool IsSuccessful,
    ExpiringToken? NewToken,
    string? ErrorMessage);

public record ActiveToken(
    string TokenId,
    string TeamSlug,
    DateTime IssuedAt,
    DateTime ExpiresAt,
    bool IsExpired,
    string? LastUsedAt,
    string? IpAddress);

public class TokenExpirationOptions
{
    public TimeSpan DefaultTokenExpiry { get; set; } = TimeSpan.FromHours(1);
    public TimeSpan RefreshTokenExpiry { get; set; } = TimeSpan.FromDays(30);
    public bool EnableRefreshTokens { get; set; } = true;
    public bool EnableTokenRevocation { get; set; } = true;
    public TimeSpan CleanupInterval { get; set; } = TimeSpan.FromMinutes(15);
    public int MaxTokensPerTeam { get; set; } = 10;
    public bool EnableTokenReuse { get; set; } = false;
    public TimeSpan TokenReuseTolerance { get; set; } = TimeSpan.FromMinutes(5);
    public string TokenSecretKey { get; set; } = "payment-gateway-token-secret-key-2024"; // Should be from secure config
}

public class TokenExpirationService : ITokenExpirationService
{
    private readonly ILogger<TokenExpirationService> _logger;
    private readonly ITokenGenerationService _tokenGenerationService;
    private readonly TokenExpirationOptions _options;

    // In-memory token storage (in production, use Redis or database)
    private readonly ConcurrentDictionary<string, StoredToken> _activeTokens;
    private readonly ConcurrentDictionary<string, RefreshTokenInfo> _refreshTokens;
    private readonly ConcurrentDictionary<string, List<string>> _teamTokens;

    public TokenExpirationService(
        ILogger<TokenExpirationService> logger,
        ITokenGenerationService tokenGenerationService,
        IOptions<TokenExpirationOptions> options)
    {
        _logger = logger;
        _tokenGenerationService = tokenGenerationService;
        _options = options.Value;
        _activeTokens = new ConcurrentDictionary<string, StoredToken>();
        _refreshTokens = new ConcurrentDictionary<string, RefreshTokenInfo>();
        _teamTokens = new ConcurrentDictionary<string, List<string>>();

        // Start background cleanup task
        _ = Task.Run(BackgroundCleanupAsync);
    }

    public async Task<ExpiringToken> CreateExpiringTokenAsync(
        string teamSlug, 
        Dictionary<string, object> requestParameters, 
        string password, 
        TimeSpan? customExpiry = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(teamSlug);
        ArgumentNullException.ThrowIfNull(requestParameters);
        ArgumentException.ThrowIfNullOrEmpty(password);

        try
        {
            var expiry = customExpiry ?? _options.DefaultTokenExpiry;
            var issuedAt = DateTime.UtcNow;
            var expiresAt = issuedAt.Add(expiry);

            // Generate the base token using existing service
            var baseToken = await _tokenGenerationService.GenerateTokenAsync(requestParameters, password);
            
            // Create expiring token with metadata
            var tokenId = Guid.NewGuid().ToString("N");
            var expiringTokenData = new Dictionary<string, object>(requestParameters)
            {
                ["TokenId"] = tokenId,
                ["IssuedAt"] = issuedAt.ToString("O"),
                ["ExpiresAt"] = expiresAt.ToString("O"),
                ["TeamSlug"] = teamSlug
            };

            var expiringToken = await _tokenGenerationService.GenerateTokenAsync(expiringTokenData, password);

            // Generate refresh token if enabled
            string? refreshToken = null;
            if (_options.EnableRefreshTokens)
            {
                refreshToken = GenerateRefreshToken();
                var refreshInfo = new RefreshTokenInfo(
                    refreshToken,
                    teamSlug,
                    tokenId,
                    issuedAt,
                    issuedAt.Add(_options.RefreshTokenExpiry));
                
                _refreshTokens.TryAdd(refreshToken, refreshInfo);
            }

            // Store token information
            var storedToken = new StoredToken(
                tokenId,
                expiringToken,
                teamSlug,
                issuedAt,
                expiresAt,
                refreshToken,
                requestParameters);

            _activeTokens.TryAdd(tokenId, storedToken);

            // Track tokens by team
            _teamTokens.AddOrUpdate(teamSlug,
                new List<string> { tokenId },
                (_, existing) =>
                {
                    existing.Add(tokenId);
                    // Enforce max tokens per team
                    if (existing.Count > _options.MaxTokensPerTeam)
                    {
                        var oldestTokenId = existing.First();
                        existing.Remove(oldestTokenId);
                        if (_activeTokens.TryRemove(oldestTokenId, out var _)) { }
                        _logger.LogInformation("Removed oldest token for team {TeamSlug} due to max token limit", teamSlug);
                    }
                    return existing;
                });

            var claims = new Dictionary<string, string>
            {
                ["TokenId"] = tokenId,
                ["TeamSlug"] = teamSlug,
                ["IssuedAt"] = issuedAt.ToString("O"),
                ["ExpiresAt"] = expiresAt.ToString("O")
            };

            _logger.LogInformation("Created expiring token for team {TeamSlug}, expires at {ExpiresAt}", 
                teamSlug, expiresAt);

            return new ExpiringToken(expiringToken, refreshToken, expiresAt, issuedAt, teamSlug, claims);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create expiring token for team {TeamSlug}", teamSlug);
            throw new InvalidOperationException("Token creation failed", ex);
        }
    }

    public async Task<TokenValidationResult> ValidateExpiringTokenAsync(
        string teamSlug, 
        Dictionary<string, object> requestParameters, 
        string password, 
        string token)
    {
        ArgumentException.ThrowIfNullOrEmpty(teamSlug);
        ArgumentNullException.ThrowIfNull(requestParameters);
        ArgumentException.ThrowIfNullOrEmpty(password);
        ArgumentException.ThrowIfNullOrEmpty(token);

        try
        {
            // First, validate the token signature
            var isValidSignature = await _tokenGenerationService.ValidateTokenAsync(requestParameters, password, token);
            if (!isValidSignature)
            {
                return new TokenValidationResult(false, false, null, "Invalid token signature", null);
            }

            // Try to find the token in our active tokens
            var storedToken = _activeTokens.Values.FirstOrDefault(t => t.Token == token && t.TeamSlug == teamSlug);
            if (storedToken == null)
            {
                // Token might be valid but not tracked (could be from before service restart)
                if (_options.EnableTokenReuse)
                {
                    return new TokenValidationResult(true, false, null, null, null);
                }
                return new TokenValidationResult(false, false, null, "Token not found in active tokens", null);
            }

            var now = DateTime.UtcNow;
            var isExpired = now > storedToken.ExpiresAt;
            var timeUntilExpiry = isExpired ? TimeSpan.Zero : storedToken.ExpiresAt - now;

            // Update last used time
            storedToken.LastUsedAt = now;

            if (isExpired)
            {
                _logger.LogWarning("Expired token used by team {TeamSlug}, expired at {ExpiresAt}", 
                    teamSlug, storedToken.ExpiresAt);
                return new TokenValidationResult(false, true, null, "Token has expired", timeUntilExpiry);
            }

            var expiringToken = new ExpiringToken(
                storedToken.Token,
                storedToken.RefreshToken,
                storedToken.ExpiresAt,
                storedToken.IssuedAt,
                storedToken.TeamSlug,
                new Dictionary<string, string>
                {
                    ["TokenId"] = storedToken.TokenId,
                    ["TeamSlug"] = storedToken.TeamSlug
                });

            return new TokenValidationResult(true, false, expiringToken, null, timeUntilExpiry);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to validate expiring token for team {TeamSlug}", teamSlug);
            return new TokenValidationResult(false, false, null, "Token validation error", null);
        }
    }

    public async Task<RefreshTokenResult> RefreshTokenAsync(string refreshToken, string teamSlug)
    {
        ArgumentException.ThrowIfNullOrEmpty(refreshToken);
        ArgumentException.ThrowIfNullOrEmpty(teamSlug);

        if (!_options.EnableRefreshTokens)
        {
            return new RefreshTokenResult(false, null, "Refresh tokens are not enabled");
        }

        try
        {
            if (!_refreshTokens.TryGetValue(refreshToken, out var refreshInfo))
            {
                return new RefreshTokenResult(false, null, "Invalid refresh token");
            }

            if (refreshInfo.TeamSlug != teamSlug)
            {
                return new RefreshTokenResult(false, null, "Refresh token does not belong to this team");
            }

            if (DateTime.UtcNow > refreshInfo.ExpiresAt)
            {
                _refreshTokens.TryRemove(refreshToken, out _);
                return new RefreshTokenResult(false, null, "Refresh token has expired");
            }

            // Find the original token
            if (!_activeTokens.TryGetValue(refreshInfo.OriginalTokenId, out var originalToken))
            {
                return new RefreshTokenResult(false, null, "Original token not found");
            }

            // Create new token with same parameters
            var newToken = await CreateExpiringTokenAsync(
                teamSlug, 
                originalToken.OriginalParameters, 
                "password", // This should come from secure storage
                _options.DefaultTokenExpiry);

            // Revoke old tokens
            await RevokeTokenAsync(originalToken.Token);
            _refreshTokens.TryRemove(refreshToken, out _);

            _logger.LogInformation("Refreshed token for team {TeamSlug}", teamSlug);

            return new RefreshTokenResult(true, newToken, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to refresh token for team {TeamSlug}", teamSlug);
            return new RefreshTokenResult(false, null, "Token refresh failed");
        }
    }

    public async Task<bool> RevokeTokenAsync(string token)
    {
        ArgumentException.ThrowIfNullOrEmpty(token);

        if (!_options.EnableTokenRevocation)
            return false;

        try
        {
            var tokenToRevoke = _activeTokens.Values.FirstOrDefault(t => t.Token == token);
            if (tokenToRevoke == null)
                return false;

            // Remove from active tokens
            _activeTokens.TryRemove(tokenToRevoke.TokenId, out _);

            // Remove from team tokens
            if (_teamTokens.TryGetValue(tokenToRevoke.TeamSlug, out var teamTokenList))
            {
                teamTokenList.Remove(tokenToRevoke.TokenId);
            }

            // Remove associated refresh token
            if (!string.IsNullOrEmpty(tokenToRevoke.RefreshToken))
            {
                _refreshTokens.TryRemove(tokenToRevoke.RefreshToken, out _);
            }

            _logger.LogInformation("Revoked token for team {TeamSlug}", tokenToRevoke.TeamSlug);
            return await Task.FromResult(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to revoke token");
            return false;
        }
    }

    public async Task<bool> RevokeAllTokensForTeamAsync(string teamSlug)
    {
        ArgumentException.ThrowIfNullOrEmpty(teamSlug);

        if (!_options.EnableTokenRevocation)
            return false;

        try
        {
            if (!_teamTokens.TryGetValue(teamSlug, out var tokenIds))
                return true; // No tokens to revoke

            var revokedCount = 0;
            foreach (var tokenId in tokenIds.ToList())
            {
                if (_activeTokens.TryRemove(tokenId, out var token))
                {
                    if (!string.IsNullOrEmpty(token.RefreshToken))
                    {
                        _refreshTokens.TryRemove(token.RefreshToken, out _);
                    }
                    revokedCount++;
                }
            }

            _teamTokens.TryRemove(teamSlug, out _);

            _logger.LogInformation("Revoked {Count} tokens for team {TeamSlug}", revokedCount, teamSlug);
            return await Task.FromResult(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to revoke all tokens for team {TeamSlug}", teamSlug);
            return false;
        }
    }

    public async Task<List<ActiveToken>> GetActiveTokensForTeamAsync(string teamSlug)
    {
        ArgumentException.ThrowIfNullOrEmpty(teamSlug);

        var activeTokens = _activeTokens.Values
            .Where(t => t.TeamSlug == teamSlug)
            .Select(t => new ActiveToken(
                t.TokenId,
                t.TeamSlug,
                t.IssuedAt,
                t.ExpiresAt,
                DateTime.UtcNow > t.ExpiresAt,
                t.LastUsedAt?.ToString("O"),
                t.LastUsedIpAddress))
            .OrderByDescending(t => t.IssuedAt)
            .ToList();

        return await Task.FromResult(activeTokens);
    }

    public async Task CleanupExpiredTokensAsync()
    {
        try
        {
            var now = DateTime.UtcNow;
            var expiredTokenIds = new List<string>();
            var expiredRefreshTokens = new List<string>();

            // Find expired tokens
            foreach (var kvp in _activeTokens)
            {
                if (now > kvp.Value.ExpiresAt)
                {
                    expiredTokenIds.Add(kvp.Key);
                    if (!string.IsNullOrEmpty(kvp.Value.RefreshToken))
                    {
                        expiredRefreshTokens.Add(kvp.Value.RefreshToken);
                    }
                }
            }

            // Find expired refresh tokens
            foreach (var kvp in _refreshTokens)
            {
                if (now > kvp.Value.ExpiresAt)
                {
                    expiredRefreshTokens.Add(kvp.Key);
                }
            }

            // Remove expired tokens
            foreach (var tokenId in expiredTokenIds)
            {
                if (_activeTokens.TryRemove(tokenId, out var token))
                {
                    // Remove from team tracking
                    if (_teamTokens.TryGetValue(token.TeamSlug, out var teamTokens))
                    {
                        teamTokens.Remove(tokenId);
                    }
                }
            }

            // Remove expired refresh tokens
            foreach (var refreshToken in expiredRefreshTokens)
            {
                _refreshTokens.TryRemove(refreshToken, out _);
            }

            if (expiredTokenIds.Count > 0 || expiredRefreshTokens.Count > 0)
            {
                _logger.LogInformation("Cleaned up {TokenCount} expired tokens and {RefreshTokenCount} expired refresh tokens",
                    expiredTokenIds.Count, expiredRefreshTokens.Count);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during token cleanup");
        }

        await Task.CompletedTask;
    }

    private string GenerateRefreshToken()
    {
        var bytes = new byte[32];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(bytes);
        return Convert.ToBase64String(bytes).Replace("+", "-").Replace("/", "_").Replace("=", "");
    }

    private async Task BackgroundCleanupAsync()
    {
        while (true)
        {
            try
            {
                await CleanupExpiredTokensAsync();
                await Task.Delay(_options.CleanupInterval);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in background token cleanup");
                await Task.Delay(TimeSpan.FromMinutes(1)); // Shorter delay on error
            }
        }
    }

    private record StoredToken(
        string TokenId,
        string Token,
        string TeamSlug,
        DateTime IssuedAt,
        DateTime ExpiresAt,
        string? RefreshToken,
        Dictionary<string, object> OriginalParameters)
    {
        public DateTime? LastUsedAt { get; set; }
        public string? LastUsedIpAddress { get; set; }
    }

    private record RefreshTokenInfo(
        string RefreshToken,
        string TeamSlug,
        string OriginalTokenId,
        DateTime IssuedAt,
        DateTime ExpiresAt);
}

// Extension methods for easier token expiration usage
public static class TokenExpirationExtensions
{
    public static async Task<ExpiringToken> CreateExpiringTokenForRequestAsync<T>(
        this ITokenExpirationService tokenService,
        string teamSlug,
        T request,
        string password,
        TimeSpan? customExpiry = null) where T : class
    {
        var parameters = ConvertRequestToDictionary(request);
        return await tokenService.CreateExpiringTokenAsync(teamSlug, parameters, password, customExpiry);
    }

    public static async Task<TokenValidationResult> ValidateExpiringTokenForRequestAsync<T>(
        this ITokenExpirationService tokenService,
        string teamSlug,
        T request,
        string password,
        string token) where T : class
    {
        var parameters = ConvertRequestToDictionary(request);
        return await tokenService.ValidateExpiringTokenAsync(teamSlug, parameters, password, token);
    }

    private static Dictionary<string, object> ConvertRequestToDictionary<T>(T request) where T : class
    {
        var properties = typeof(T).GetProperties();
        var dictionary = new Dictionary<string, object>();

        foreach (var property in properties)
        {
            var value = property.GetValue(request);
            if (value != null)
            {
                dictionary[property.Name] = value;
            }
        }

        return dictionary;
    }
}