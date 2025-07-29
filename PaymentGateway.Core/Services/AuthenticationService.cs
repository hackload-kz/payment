using PaymentGateway.Core.Enums;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace PaymentGateway.Core.Services;

public interface IAuthenticationService
{
    Task<AuthenticationResult> AuthenticateAsync(string teamSlug, Dictionary<string, object> requestParameters, string providedToken, CancellationToken cancellationToken = default);
    Task<bool> ValidateTeamSlugAsync(string teamSlug, CancellationToken cancellationToken = default);
    Task<TeamInfo?> GetTeamInfoAsync(string teamSlug, CancellationToken cancellationToken = default);
    Task<AuthenticationAttemptResult> RecordAuthenticationAttemptAsync(string teamSlug, bool wasSuccessful, string? ipAddress = null);
    Task<bool> IsTeamBlockedAsync(string teamSlug, CancellationToken cancellationToken = default);
}

public record AuthenticationResult(
    bool IsSuccessful,
    PaymentErrorCode ErrorCode,
    string? ErrorMessage,
    TeamInfo? TeamInfo,
    TimeSpan ProcessingTime);

public record AuthenticationAttemptResult(
    string TeamSlug,
    DateTime AttemptTime,
    bool WasSuccessful,
    string? IpAddress,
    int RecentFailureCount,
    TimeSpan? BlockDuration);

public record TeamInfo(
    string TeamSlug,
    string HashedPassword,
    bool IsActive,
    bool IsBlocked,
    DateTime CreatedAt,
    DateTime? LastLoginAt,
    Dictionary<string, string> Settings);

public class AuthenticationOptions
{
    public int MaxFailedAttempts { get; set; } = 5;
    public TimeSpan FailedAttemptsWindow { get; set; } = TimeSpan.FromMinutes(15);
    public TimeSpan BlockDuration { get; set; } = TimeSpan.FromMinutes(30);
    public bool EnableProgressiveBlocking { get; set; } = true;
    public Dictionary<int, TimeSpan> ProgressiveBlockDurations { get; set; } = new()
    {
        { 1, TimeSpan.FromMinutes(5) },
        { 2, TimeSpan.FromMinutes(15) },
        { 3, TimeSpan.FromMinutes(30) },
        { 4, TimeSpan.FromHours(1) },
        { 5, TimeSpan.FromHours(2) }
    };
    public bool EnableIpBlocking { get; set; } = true;
    public int MaxAttemptsPerIp { get; set; } = 20;
    public bool EnableAuditLogging { get; set; } = true;
}

public class AuthenticationService : IAuthenticationService
{
    private readonly ILogger<AuthenticationService> _logger;
    private readonly ITokenGenerationService _tokenGenerationService;
    private readonly AuthenticationOptions _options;

    // In-memory team storage (in production, this would be database-backed)
    private readonly ConcurrentDictionary<string, TeamInfo> _teams;
    private readonly ConcurrentDictionary<string, List<AuthenticationAttemptResult>> _authenticationAttempts;
    private readonly ConcurrentDictionary<string, DateTime> _blockedTeams;
    private readonly ConcurrentDictionary<string, List<AuthenticationAttemptResult>> _ipAttempts;

    public AuthenticationService(
        ILogger<AuthenticationService> logger,
        ITokenGenerationService tokenGenerationService,
        IOptions<AuthenticationOptions> options)
    {
        _logger = logger;
        _tokenGenerationService = tokenGenerationService;
        _options = options.Value;
        _teams = new ConcurrentDictionary<string, TeamInfo>();
        _authenticationAttempts = new ConcurrentDictionary<string, List<AuthenticationAttemptResult>>();
        _blockedTeams = new ConcurrentDictionary<string, DateTime>();
        _ipAttempts = new ConcurrentDictionary<string, List<AuthenticationAttemptResult>>();

        InitializeDefaultTeams();
    }

    public async Task<AuthenticationResult> AuthenticateAsync(
        string teamSlug, 
        Dictionary<string, object> requestParameters, 
        string providedToken, 
        CancellationToken cancellationToken = default)
    {
        var startTime = DateTime.UtcNow;
        
        try
        {
            // Validate input parameters
            if (string.IsNullOrWhiteSpace(teamSlug))
            {
                return CreateFailureResult(PaymentErrorCode.MissingRequiredParameters, 
                    "TeamSlug is required", startTime);
            }

            if (string.IsNullOrWhiteSpace(providedToken))
            {
                return CreateFailureResult(PaymentErrorCode.InvalidToken, 
                    "Token is required", startTime);
            }

            // Check if team is blocked
            if (await IsTeamBlockedAsync(teamSlug, cancellationToken))
            {
                await RecordAuthenticationAttemptAsync(teamSlug, false);
                return CreateFailureResult(PaymentErrorCode.TerminalAccessDenied, 
                    "Team is temporarily blocked due to multiple failed authentication attempts", startTime);
            }

            // Get team information
            var teamInfo = await GetTeamInfoAsync(teamSlug, cancellationToken);
            if (teamInfo == null)
            {
                await RecordAuthenticationAttemptAsync(teamSlug, false);
                return CreateFailureResult(PaymentErrorCode.TerminalNotFound, 
                    "Team not found", startTime);
            }

            if (!teamInfo.IsActive || teamInfo.IsBlocked)
            {
                await RecordAuthenticationAttemptAsync(teamSlug, false);
                return CreateFailureResult(PaymentErrorCode.TerminalAccessDenied, 
                    "Team is inactive or blocked", startTime);
            }

            // Validate token
            var password = DecryptPassword(teamInfo.HashedPassword);
            var isTokenValid = await _tokenGenerationService.ValidateTokenAsync(
                requestParameters, password, providedToken);

            if (!isTokenValid)
            {
                await RecordAuthenticationAttemptAsync(teamSlug, false);
                return CreateFailureResult(PaymentErrorCode.TokenAuthenticationFailed, 
                    "Token validation failed", startTime);
            }

            // Authentication successful
            await RecordAuthenticationAttemptAsync(teamSlug, true);
            await UpdateLastLoginAsync(teamSlug);

            var processingTime = DateTime.UtcNow - startTime;
            
            _logger.LogInformation("Authentication successful for team {TeamSlug} in {ProcessingTime}ms", 
                teamSlug, processingTime.TotalMilliseconds);

            return new AuthenticationResult(
                true, 
                PaymentErrorCode.Success, 
                null, 
                teamInfo, 
                processingTime);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Authentication failed for team {TeamSlug}", teamSlug);
            await RecordAuthenticationAttemptAsync(teamSlug, false);
            return CreateFailureResult(PaymentErrorCode.InternalRequestProcessingError, 
                "Authentication processing error", startTime);
        }
    }

    public async Task<bool> ValidateTeamSlugAsync(string teamSlug, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(teamSlug))
            return false;

        if (teamSlug.Length > 20)
            return false;

        var teamInfo = await GetTeamInfoAsync(teamSlug, cancellationToken);
        return teamInfo != null && teamInfo.IsActive && !teamInfo.IsBlocked;
    }

    public async Task<TeamInfo?> GetTeamInfoAsync(string teamSlug, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(teamSlug))
            return null;

        _teams.TryGetValue(teamSlug, out var teamInfo);
        return await Task.FromResult(teamInfo);
    }

    public async Task<AuthenticationAttemptResult> RecordAuthenticationAttemptAsync(
        string teamSlug, 
        bool wasSuccessful, 
        string? ipAddress = null)
    {
        var attemptTime = DateTime.UtcNow;
        var attempt = new AuthenticationAttemptResult(
            teamSlug, 
            attemptTime, 
            wasSuccessful, 
            ipAddress, 
            0, 
            null);

        // Record team-based attempt
        var teamAttempts = _authenticationAttempts.AddOrUpdate(
            teamSlug,
            new List<AuthenticationAttemptResult> { attempt },
            (_, existing) =>
            {
                existing.Add(attempt);
                
                // Keep only recent attempts within the window
                var cutoffTime = attemptTime - _options.FailedAttemptsWindow;
                var recentAttempts = existing.Where(a => a.AttemptTime > cutoffTime).ToList();
                return recentAttempts;
            });

        // Record IP-based attempt if IP address is provided
        if (!string.IsNullOrEmpty(ipAddress))
        {
            _ipAttempts.AddOrUpdate(
                ipAddress,
                new List<AuthenticationAttemptResult> { attempt },
                (_, existing) =>
                {
                    existing.Add(attempt);
                    var cutoffTime = attemptTime - _options.FailedAttemptsWindow;
                    return existing.Where(a => a.AttemptTime > cutoffTime).ToList();
                });
        }

        // Calculate recent failure count
        var recentFailures = teamAttempts.Count(a => !a.WasSuccessful);
        
        // Check if team should be blocked
        TimeSpan? blockDuration = null;
        if (!wasSuccessful && recentFailures >= _options.MaxFailedAttempts)
        {
            blockDuration = CalculateBlockDuration(recentFailures);
            var blockUntil = attemptTime + blockDuration.Value;
            _blockedTeams.TryAdd(teamSlug, blockUntil);

            _logger.LogWarning("Team {TeamSlug} blocked for {BlockDuration} after {FailureCount} failed attempts",
                teamSlug, blockDuration, recentFailures);
        }

        // Update attempt with failure count and block duration
        attempt = attempt with { 
            RecentFailureCount = recentFailures, 
            BlockDuration = blockDuration 
        };

        if (_options.EnableAuditLogging)
        {
            _logger.LogInformation("Authentication attempt recorded: Team={TeamSlug}, Success={Success}, IP={IP}, Failures={Failures}",
                teamSlug, wasSuccessful, ipAddress ?? "unknown", recentFailures);
        }

        return await Task.FromResult(attempt);
    }

    public async Task<bool> IsTeamBlockedAsync(string teamSlug, CancellationToken cancellationToken = default)
    {
        if (!_blockedTeams.TryGetValue(teamSlug, out var blockUntil))
            return false;

        var now = DateTime.UtcNow;
        if (now >= blockUntil)
        {
            // Block has expired, remove it
            _blockedTeams.TryRemove(teamSlug, out _);
            return false;
        }

        return await Task.FromResult(true);
    }

    private AuthenticationResult CreateFailureResult(PaymentErrorCode errorCode, string errorMessage, DateTime startTime)
    {
        var processingTime = DateTime.UtcNow - startTime;
        return new AuthenticationResult(false, errorCode, errorMessage, null, processingTime);
    }

    private TimeSpan CalculateBlockDuration(int failureCount)
    {
        if (!_options.EnableProgressiveBlocking)
            return _options.BlockDuration;

        // Find the appropriate block duration based on failure count
        var applicableDurations = _options.ProgressiveBlockDurations
            .Where(kvp => failureCount >= kvp.Key)
            .OrderByDescending(kvp => kvp.Key);

        return applicableDurations.FirstOrDefault().Value != default 
            ? applicableDurations.First().Value 
            : _options.BlockDuration;
    }

    private async Task UpdateLastLoginAsync(string teamSlug)
    {
        if (_teams.TryGetValue(teamSlug, out var teamInfo))
        {
            var updatedTeam = teamInfo with { LastLoginAt = DateTime.UtcNow };
            _teams.TryUpdate(teamSlug, updatedTeam, teamInfo);
        }

        await Task.CompletedTask;
    }

    private string DecryptPassword(string hashedPassword)
    {
        // In a real implementation, this would decrypt the stored password
        // For this implementation, we're storing passwords in plain text (not recommended for production)
        return hashedPassword;
    }

    private void InitializeDefaultTeams()
    {
        // Initialize some default teams for testing
        var defaultTeams = new[]
        {
            new TeamInfo(
                "TestMerchant",
                "test_password_123",
                true,
                false,
                DateTime.UtcNow.AddDays(-30),
                null,
                new Dictionary<string, string>
                {
                    { "MaxTransactionAmount", "1000000" },
                    { "AllowedPaymentMethods", "card,wallet" }
                }
            ),
            new TeamInfo(
                "DemoStore",
                "demo_secret_key",
                true,
                false,
                DateTime.UtcNow.AddDays(-60),
                null,
                new Dictionary<string, string>
                {
                    { "MaxTransactionAmount", "500000" },
                    { "AllowedPaymentMethods", "card" }
                }
            )
        };

        foreach (var team in defaultTeams)
        {
            _teams.TryAdd(team.TeamSlug, team);
        }

        _logger.LogInformation("Initialized {TeamCount} default teams for authentication", defaultTeams.Length);
    }

    // Methods for managing teams (would typically be in a separate service)
    public async Task<bool> CreateTeamAsync(string teamSlug, string password, Dictionary<string, string>? settings = null)
    {
        if (string.IsNullOrWhiteSpace(teamSlug) || teamSlug.Length > 20)
            return false;

        if (string.IsNullOrWhiteSpace(password))
            return false;

        var hashedPassword = HashPassword(password);
        var teamInfo = new TeamInfo(
            teamSlug,
            hashedPassword,
            true,
            false,
            DateTime.UtcNow,
            null,
            settings ?? new Dictionary<string, string>());

        var added = _teams.TryAdd(teamSlug, teamInfo);
        
        if (added)
        {
            _logger.LogInformation("Created new team: {TeamSlug}", teamSlug);
        }

        return await Task.FromResult(added);
    }

    public async Task<bool> UpdateTeamPasswordAsync(string teamSlug, string newPassword)
    {
        if (!_teams.TryGetValue(teamSlug, out var existingTeam))
            return false;

        var hashedPassword = HashPassword(newPassword);
        var updatedTeam = existingTeam with { HashedPassword = hashedPassword };
        var updated = _teams.TryUpdate(teamSlug, updatedTeam, existingTeam);

        if (updated)
        {
            _logger.LogInformation("Updated password for team: {TeamSlug}", teamSlug);
        }

        return await Task.FromResult(updated);
    }

    public async Task<bool> BlockTeamAsync(string teamSlug, bool isBlocked)
    {
        if (!_teams.TryGetValue(teamSlug, out var existingTeam))
            return false;

        var updatedTeam = existingTeam with { IsBlocked = isBlocked };
        var updated = _teams.TryUpdate(teamSlug, updatedTeam, existingTeam);

        if (updated)
        {
            _logger.LogInformation("Team {TeamSlug} block status changed to: {IsBlocked}", teamSlug, isBlocked);
        }

        return await Task.FromResult(updated);
    }

    private static string HashPassword(string password)
    {
        // In production, use proper password hashing like bcrypt, scrypt, or Argon2
        // This is a simplified implementation for demonstration
        return password; // Return plain text for this implementation
    }
}

// Extensions for easier authentication usage
public static class AuthenticationExtensions
{
    public static async Task<AuthenticationResult> AuthenticateRequestAsync<T>(
        this IAuthenticationService authService,
        T request,
        string teamSlug,
        string token) where T : class
    {
        var requestDict = ConvertRequestToDictionary(request);
        return await authService.AuthenticateAsync(teamSlug, requestDict, token);
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