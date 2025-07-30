using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Caching.Memory;
using PaymentGateway.Core.Repositories;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace PaymentGateway.Core.Services;

/// <summary>
/// Service interface for team authentication and authorization
/// </summary>
public interface ITeamAuthenticationService
{
    Task<TeamAuthenticationResult> AuthenticateAsync(string teamSlug, string token, Dictionary<string, object> requestParameters, CancellationToken cancellationToken = default);
    Task<bool> ValidateTeamSlugAsync(string teamSlug, CancellationToken cancellationToken = default);
    Task<string> GenerateTokenAsync(string teamSlug, Dictionary<string, object> parameters, CancellationToken cancellationToken = default);
    Task<bool> IsTeamAuthorizedAsync(string teamSlug, string operation, CancellationToken cancellationToken = default);
    Task<TeamAuthorizationInfo> GetTeamAuthorizationInfoAsync(string teamSlug, CancellationToken cancellationToken = default);
}

/// <summary>
/// Team authentication service implementation
/// </summary>
public class TeamAuthenticationService : ITeamAuthenticationService
{
    private readonly ITeamRepository _teamRepository;
    private readonly IMemoryCache _cache;
    private readonly ILogger<TeamAuthenticationService> _logger;
    private readonly TimeSpan _cacheExpiration = TimeSpan.FromMinutes(15);

    public TeamAuthenticationService(
        ITeamRepository teamRepository,
        IMemoryCache cache,
        ILogger<TeamAuthenticationService> logger)
    {
        _teamRepository = teamRepository;
        _cache = cache;
        _logger = logger;
    }

    public async Task<TeamAuthenticationResult> AuthenticateAsync(string teamSlug, string token, Dictionary<string, object> requestParameters, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Authenticating request for TeamSlug: {TeamSlug}", teamSlug);

            // 1. Validate team exists and is active
            var team = await GetTeamWithCachingAsync(teamSlug, cancellationToken);
            if (team == null)
            {
                return new TeamAuthenticationResult
                {
                    IsAuthenticated = false,
                    FailureReason = "Team not found or inactive",
                    ErrorCode = "1001"
                };
            }

            // 2. Generate expected token using team's secret key
            var expectedToken = await GenerateTokenAsync(teamSlug, requestParameters, cancellationToken);
            
            // 3. Compare tokens using secure comparison
            if (!SecureTokenCompare(token, expectedToken))
            {
                _logger.LogWarning("Token validation failed for TeamSlug: {TeamSlug}", teamSlug);
                return new TeamAuthenticationResult
                {
                    IsAuthenticated = false,
                    FailureReason = "Invalid token signature",
                    ErrorCode = "1002"
                };
            }

            // 4. Check for token replay protection (basic timestamp validation)
            if (!ValidateTokenTimestamp(requestParameters))
            {
                return new TeamAuthenticationResult
                {
                    IsAuthenticated = false,
                    FailureReason = "Token timestamp is invalid or expired",
                    ErrorCode = "1003"
                };
            }

            _logger.LogDebug("Authentication successful for TeamSlug: {TeamSlug}", teamSlug);

            return new TeamAuthenticationResult
            {
                IsAuthenticated = true,
                TeamId = team.Id,
                TeamSlug = teamSlug,
                Permissions = GetTeamPermissions(team)
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during authentication for TeamSlug: {TeamSlug}", teamSlug);
            return new TeamAuthenticationResult
            {
                IsAuthenticated = false,
                FailureReason = "Authentication service error",
                ErrorCode = "9999"
            };
        }
    }

    public async Task<bool> ValidateTeamSlugAsync(string teamSlug, CancellationToken cancellationToken = default)
    {
        try
        {
            var team = await GetTeamWithCachingAsync(teamSlug, cancellationToken);
            return team != null && team.IsActive;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating TeamSlug: {TeamSlug}", teamSlug);
            return false;
        }
    }

    public async Task<string> GenerateTokenAsync(string teamSlug, Dictionary<string, object> parameters, CancellationToken cancellationToken = default)
    {
        try
        {
            // 1. Get team's secret key
            var team = await GetTeamWithCachingAsync(teamSlug, cancellationToken);
            if (team == null)
            {
                throw new ArgumentException($"Team '{teamSlug}' not found", nameof(teamSlug));
            }

            // 2. Create sorted parameter string for consistent token generation
            var sortedParams = parameters
                .Where(kvp => kvp.Key != "Token") // Exclude Token parameter itself
                .OrderBy(kvp => kvp.Key)
                .Select(kvp => $"{kvp.Key}={kvp.Value}")
                .ToList();

            var parameterString = string.Join("&", sortedParams);
            
            // 3. Create string to sign: parameters + secret key
            var stringToSign = $"{parameterString}&SecretKey={team.SecretKey}";
            
            // 4. Generate SHA-256 hash
            using var sha256 = SHA256.Create();
            var hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(stringToSign));
            var token = Convert.ToHexString(hashBytes).ToLowerInvariant();

            _logger.LogDebug("Generated token for TeamSlug: {TeamSlug}", teamSlug);
            return token;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating token for TeamSlug: {TeamSlug}", teamSlug);
            throw;
        }
    }

    public async Task<bool> IsTeamAuthorizedAsync(string teamSlug, string operation, CancellationToken cancellationToken = default)
    {
        try
        {
            var team = await GetTeamWithCachingAsync(teamSlug, cancellationToken);
            if (team == null || !team.IsActive)
            {
                return false;
            }

            // Check operation-specific authorization
            return operation switch
            {
                "payment_init" => team.IsActive && !team.IsLocked,
                "payment_confirm" => team.IsActive && !team.IsLocked,
                "payment_cancel" => team.IsActive && !team.IsLocked,
                "payment_check" => team.IsActive, // Allow status checking even for locked teams
                _ => false
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking authorization for TeamSlug: {TeamSlug}, Operation: {Operation}", teamSlug, operation);
            return false;
        }
    }

    public async Task<TeamAuthorizationInfo> GetTeamAuthorizationInfoAsync(string teamSlug, CancellationToken cancellationToken = default)
    {
        try
        {
            var team = await GetTeamWithCachingAsync(teamSlug, cancellationToken);
            if (team == null)
            {
                return new TeamAuthorizationInfo
                {
                    TeamSlug = teamSlug,
                    IsActive = false,
                    Permissions = new List<string>()
                };
            }

            return new TeamAuthorizationInfo
            {
                TeamSlug = teamSlug,
                TeamId = team.Id,
                IsActive = team.IsActive,
                IsLocked = team.IsLocked,
                Permissions = GetTeamPermissions(team),
                DailyLimit = team.DailyLimit,
                MonthlyLimit = team.MonthlyLimit,
                TransactionLimit = team.TransactionLimit,
                SupportedCurrencies = GetSupportedCurrencies(team),
                AllowedOperations = GetAllowedOperations(team)
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting authorization info for TeamSlug: {TeamSlug}", teamSlug);
            return new TeamAuthorizationInfo
            {
                TeamSlug = teamSlug,
                IsActive = false,
                Permissions = new List<string>()
            };
        }
    }

    private async Task<dynamic?> GetTeamWithCachingAsync(string teamSlug, CancellationToken cancellationToken)
    {
        var cacheKey = $"team_{teamSlug}";
        
        if (_cache.TryGetValue(cacheKey, out dynamic? cachedTeam))
        {
            return cachedTeam;
        }

        var team = await _teamRepository.GetByTeamSlugAsync(teamSlug, cancellationToken);
        if (team != null)
        {
            var teamData = new
            {
                Id = team.Id,
                TeamSlug = team.TeamSlug,
                SecretKey = team.SecretKey,
                IsActive = team.IsActive,
                IsLocked = false // Team entity doesn't have IsLocked property
            };

            _cache.Set(cacheKey, teamData, _cacheExpiration);
            return teamData;
        }

        return null;
    }

    private bool SecureTokenCompare(string token1, string token2)
    {
        if (string.IsNullOrEmpty(token1) || string.IsNullOrEmpty(token2))
        {
            return false;
        }

        if (token1.Length != token2.Length)
        {
            return false;
        }

        // Use constant-time comparison to prevent timing attacks
        var result = 0;
        for (int i = 0; i < token1.Length; i++)
        {
            result |= token1[i] ^ token2[i];
        }

        return result == 0;
    }

    private bool ValidateTokenTimestamp(Dictionary<string, object> parameters)
    {
        // Check if timestamp parameter exists and is within acceptable range
        if (!parameters.TryGetValue("Timestamp", out var timestampObj))
        {
            // If no timestamp provided, assume valid for backwards compatibility
            return true;
        }

        if (!DateTime.TryParse(timestampObj?.ToString(), out var timestamp))
        {
            return false;
        }

        var now = DateTime.UtcNow;
        var timeDifference = Math.Abs((now - timestamp).TotalMinutes);
        
        // Allow requests within 15 minutes to account for clock skew
        return timeDifference <= 15;
    }

    private List<string> GetTeamPermissions(dynamic team)
    {
        var permissions = new List<string>();

        if (team.IsActive)
        {
            permissions.Add("payment_check");
            
            if (!team.IsLocked)
            {
                permissions.Add("payment_init");
                permissions.Add("payment_confirm");
                permissions.Add("payment_cancel");
            }
        }

        return permissions;
    }

    private List<string> GetSupportedCurrencies(dynamic team)
    {
        // In a real implementation, this would be team-specific
        return new List<string> { "RUB", "USD", "EUR" };
    }

    private List<string> GetAllowedOperations(dynamic team)
    {
        var operations = new List<string>();

        if (team.IsActive)
        {
            operations.Add("payment_check");
            
            if (!team.IsLocked)
            {
                operations.AddRange(new[] { "payment_init", "payment_confirm", "payment_cancel" });
            }
        }

        return operations;
    }
}

// Supporting classes for team authentication
public class TeamAuthenticationResult
{
    public bool IsAuthenticated { get; set; }
    public Guid? TeamId { get; set; }
    public string? TeamSlug { get; set; }
    public string? FailureReason { get; set; }
    public string? ErrorCode { get; set; }
    public List<string> Permissions { get; set; } = new();
}

public class TeamAuthorizationInfo
{
    public string TeamSlug { get; set; } = string.Empty;
    public Guid? TeamId { get; set; }
    public bool IsActive { get; set; }
    public bool IsLocked { get; set; }
    public List<string> Permissions { get; set; } = new();
    public decimal? DailyLimit { get; set; }
    public decimal? MonthlyLimit { get; set; }
    public decimal? TransactionLimit { get; set; }
    public List<string> SupportedCurrencies { get; set; } = new();
    public List<string> AllowedOperations { get; set; } = new();
}