using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using PaymentGateway.Core.Repositories;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace PaymentGateway.Core.Services;

/// <summary>
/// Service interface for multi-factor authentication
/// </summary>
public interface IMultiFactorAuthenticationService
{
    Task<MfaInitiationResult> InitiateMfaAsync(string teamSlug, MfaMethod method, Dictionary<string, object> context, CancellationToken cancellationToken = default);
    Task<MfaVerificationResult> VerifyMfaAsync(string teamSlug, string challengeId, string userResponse, CancellationToken cancellationToken = default);
    Task<List<MfaMethod>> GetAvailableMfaMethodsAsync(string teamSlug, CancellationToken cancellationToken = default);
    Task<bool> SetupMfaMethodAsync(string teamSlug, MfaMethod method, Dictionary<string, string> configuration, CancellationToken cancellationToken = default);
    Task<bool> DisableMfaMethodAsync(string teamSlug, MfaMethod method, CancellationToken cancellationToken = default);
    Task<MfaSessionInfo> GetMfaSessionInfoAsync(string teamSlug, CancellationToken cancellationToken = default);
    Task<bool> IsMfaValidForOperationAsync(string teamSlug, PaymentOperation operation, CancellationToken cancellationToken = default);
}

/// <summary>
/// Multi-factor authentication service implementation
/// </summary>
public class MultiFactorAuthenticationService : IMultiFactorAuthenticationService
{
    private readonly ITeamRepository _teamRepository;
    private readonly IMemoryCache _cache;
    private readonly ILogger<MultiFactorAuthenticationService> _logger;
    private readonly ISecurityAuditService _securityAuditService;
    private readonly MfaOptions _options;
    private readonly TimeSpan _challengeCacheDuration = TimeSpan.FromMinutes(5);
    private readonly TimeSpan _sessionCacheDuration = TimeSpan.FromMinutes(30);

    public MultiFactorAuthenticationService(
        ITeamRepository teamRepository,
        IMemoryCache cache,
        ILogger<MultiFactorAuthenticationService> logger,
        ISecurityAuditService securityAuditService,
        IOptions<MfaOptions> options)
    {
        _teamRepository = teamRepository;
        _cache = cache;
        _logger = logger;
        _securityAuditService = securityAuditService;
        _options = options.Value;
    }

    public async Task<MfaInitiationResult> InitiateMfaAsync(
        string teamSlug, 
        MfaMethod method, 
        Dictionary<string, object> context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Initiating MFA for TeamSlug: {TeamSlug}, Method: {Method}", teamSlug, method);

            var team = await _teamRepository.GetByTeamSlugAsync(teamSlug, cancellationToken);
            if (team == null)
            {
                return new MfaInitiationResult
                {
                    IsSuccessful = false,
                    ErrorMessage = "Team not found",
                    Method = method
                };
            }

            // Check if MFA method is configured for the team
            var availableMethods = await GetAvailableMfaMethodsAsync(teamSlug, cancellationToken);
            if (!availableMethods.Contains(method))
            {
                return new MfaInitiationResult
                {
                    IsSuccessful = false,
                    ErrorMessage = "MFA method not configured for this team",
                    Method = method
                };
            }

            var challengeId = Guid.NewGuid().ToString();
            var challenge = await GenerateMfaChallengeAsync(team, method, challengeId, context);

            // Cache the challenge for verification
            var cacheKey = $"mfa_challenge_{challengeId}";
            _cache.Set(cacheKey, challenge, _challengeCacheDuration);

            // Send the challenge (implementation depends on method)
            await SendMfaChallengeAsync(team, method, challenge, context);

            await _securityAuditService.LogSecurityEventAsync(new SecurityAuditEvent(
                Guid.NewGuid().ToString(),
                SecurityEventType.TokenGeneration,
                SecurityEventSeverity.Low,
                DateTime.UtcNow,
                null,
                teamSlug,
                context.GetValueOrDefault("IpAddress")?.ToString(),
                null,
                $"MFA challenge initiated for method {method}",
                new Dictionary<string, string>
                {
                    { "Method", method.ToString() },
                    { "ChallengeId", challengeId }
                },
                challengeId,
                true,
                null
            ));

            return new MfaInitiationResult
            {
                IsSuccessful = true,
                ChallengeId = challengeId,
                Method = method,
                ExpiresAt = DateTime.UtcNow.Add(_challengeCacheDuration),
                Instructions = GetMfaInstructions(method),
                AdditionalData = GetAdditionalMfaData(method, challenge)
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error initiating MFA for TeamSlug: {TeamSlug}, Method: {Method}", teamSlug, method);
            
            await _securityAuditService.LogSecurityEventAsync(new SecurityAuditEvent(
                Guid.NewGuid().ToString(),
                SecurityEventType.SystemError,
                SecurityEventSeverity.High,
                DateTime.UtcNow,
                null,
                teamSlug,
                null,
                null,
                $"MFA initiation error for method {method}",
                new Dictionary<string, string>(),
                null,
                false,
                ex.Message
            ));

            return new MfaInitiationResult
            {
                IsSuccessful = false,
                ErrorMessage = "MFA initiation failed",
                Method = method
            };
        }
    }

    public async Task<MfaVerificationResult> VerifyMfaAsync(
        string teamSlug, 
        string challengeId, 
        string userResponse, 
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Verifying MFA for TeamSlug: {TeamSlug}, ChallengeId: {ChallengeId}", teamSlug, challengeId);

            // Get the cached challenge
            var cacheKey = $"mfa_challenge_{challengeId}";
            if (!_cache.TryGetValue(cacheKey, out MfaChallenge? challenge) || challenge == null)
            {
                await _securityAuditService.LogSecurityEventAsync(new SecurityAuditEvent(
                    Guid.NewGuid().ToString(),
                    SecurityEventType.TokenValidationFailure,
                    SecurityEventSeverity.Medium,
                    DateTime.UtcNow,
                    null,
                    teamSlug,
                    null,
                    null,
                    "MFA verification failed: Challenge not found or expired",
                    new Dictionary<string, string> { { "ChallengeId", challengeId } },
                    challengeId,
                    false,
                    "Challenge not found or expired"
                ));

                return new MfaVerificationResult
                {
                    IsSuccessful = false,
                    ErrorMessage = "Challenge not found or expired",
                    TeamSlug = teamSlug
                };
            }

            // Verify the response
            var isValid = await VerifyMfaResponseAsync(challenge, userResponse);
            
            if (isValid)
            {
                // Remove the challenge from cache
                _cache.Remove(cacheKey);

                // Create MFA session
                var sessionId = Guid.NewGuid().ToString();
                var session = new MfaSession
                {
                    SessionId = sessionId,
                    TeamSlug = teamSlug,
                    Method = challenge.Method,
                    AuthenticatedAt = DateTime.UtcNow,
                    ExpiresAt = DateTime.UtcNow.Add(_sessionCacheDuration),
                    IsValid = true
                };

                var sessionCacheKey = $"mfa_session_{teamSlug}";
                _cache.Set(sessionCacheKey, session, _sessionCacheDuration);

                await _securityAuditService.LogSecurityEventAsync(new SecurityAuditEvent(
                    Guid.NewGuid().ToString(),
                    SecurityEventType.AuthenticationSuccess,
                    SecurityEventSeverity.Low,
                    DateTime.UtcNow,
                    null,
                    teamSlug,
                    null,
                    null,
                    $"MFA verification successful for method {challenge.Method}",
                    new Dictionary<string, string>
                    {
                        { "Method", challenge.Method.ToString() },
                        { "SessionId", sessionId }
                    },
                    challengeId,
                    true,
                    null
                ));

                return new MfaVerificationResult
                {
                    IsSuccessful = true,
                    TeamSlug = teamSlug,
                    SessionId = sessionId,
                    ValidUntil = session.ExpiresAt
                };
            }
            else
            {
                await _securityAuditService.LogSecurityEventAsync(new SecurityAuditEvent(
                    Guid.NewGuid().ToString(),
                    SecurityEventType.AuthenticationFailure,
                    SecurityEventSeverity.Medium,
                    DateTime.UtcNow,
                    null,
                    teamSlug,
                    null,
                    null,
                    "MFA verification failed: Invalid response",
                    new Dictionary<string, string> { { "Method", challenge.Method.ToString() } },
                    challengeId,
                    false,
                    "Invalid MFA response"
                ));

                return new MfaVerificationResult
                {
                    IsSuccessful = false,
                    ErrorMessage = "Invalid verification code",
                    TeamSlug = teamSlug
                };
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error verifying MFA for TeamSlug: {TeamSlug}, ChallengeId: {ChallengeId}", teamSlug, challengeId);
            
            await _securityAuditService.LogSecurityEventAsync(new SecurityAuditEvent(
                Guid.NewGuid().ToString(),
                SecurityEventType.SystemError,
                SecurityEventSeverity.High,
                DateTime.UtcNow,
                null,
                teamSlug,
                null,
                null,
                "MFA verification system error",
                new Dictionary<string, string>(),
                challengeId,
                false,
                ex.Message
            ));

            return new MfaVerificationResult
            {
                IsSuccessful = false,
                ErrorMessage = "MFA verification failed",
                TeamSlug = teamSlug
            };
        }
    }

    public async Task<List<MfaMethod>> GetAvailableMfaMethodsAsync(string teamSlug, CancellationToken cancellationToken = default)
    {
        var team = await _teamRepository.GetByTeamSlugAsync(teamSlug, cancellationToken);
        if (team == null)
        {
            return new List<MfaMethod>();
        }

        var availableMethods = new List<MfaMethod>();

        // Check which methods are configured in team metadata
        if (team.Metadata.ContainsKey("MFA_TOTP_Enabled") && team.Metadata["MFA_TOTP_Enabled"] == "true")
        {
            availableMethods.Add(MfaMethod.TOTP);
        }

        if (team.Metadata.ContainsKey("MFA_SMS_Enabled") && team.Metadata["MFA_SMS_Enabled"] == "true")
        {
            availableMethods.Add(MfaMethod.SMS);
        }

        if (team.Metadata.ContainsKey("MFA_Email_Enabled") && team.Metadata["MFA_Email_Enabled"] == "true")
        {
            availableMethods.Add(MfaMethod.Email);
        }

        if (team.Metadata.ContainsKey("MFA_Hardware_Enabled") && team.Metadata["MFA_Hardware_Enabled"] == "true")
        {
            availableMethods.Add(MfaMethod.HardwareToken);
        }

        return availableMethods;
    }

    public async Task<bool> SetupMfaMethodAsync(
        string teamSlug, 
        MfaMethod method, 
        Dictionary<string, string> configuration, 
        CancellationToken cancellationToken = default)
    {
        try
        {
            var team = await _teamRepository.GetByTeamSlugAsync(teamSlug, cancellationToken);
            if (team == null)
            {
                return false;
            }

            // Store MFA configuration in team metadata
            var methodKey = $"MFA_{method}_Enabled";
            team.Metadata[methodKey] = "true";

            // Store method-specific configuration
            foreach (var config in configuration)
            {
                var configKey = $"MFA_{method}_{config.Key}";
                team.Metadata[configKey] = config.Value;
            }

            await _teamRepository.UpdateAsync(team);

            _logger.LogInformation("MFA method {Method} setup for team {TeamSlug}", method, teamSlug);
            
            await _securityAuditService.LogSecurityEventAsync(new SecurityAuditEvent(
                Guid.NewGuid().ToString(),
                SecurityEventType.ConfigurationChange,
                SecurityEventSeverity.Low,
                DateTime.UtcNow,
                null,
                teamSlug,
                null,
                null,
                $"MFA method {method} configured for team",
                new Dictionary<string, string> { { "Method", method.ToString() } },
                null,
                true,
                null
            ));

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error setting up MFA method {Method} for team {TeamSlug}", method, teamSlug);
            return false;
        }
    }

    public async Task<bool> DisableMfaMethodAsync(string teamSlug, MfaMethod method, CancellationToken cancellationToken = default)
    {
        try
        {
            var team = await _teamRepository.GetByTeamSlugAsync(teamSlug, cancellationToken);
            if (team == null)
            {
                return false;
            }

            // Remove MFA configuration from team metadata
            var methodKey = $"MFA_{method}_Enabled";
            team.Metadata.Remove(methodKey);

            // Remove method-specific configuration
            var keysToRemove = team.Metadata.Keys
                .Where(k => k.StartsWith($"MFA_{method}_"))
                .ToList();

            foreach (var key in keysToRemove)
            {
                team.Metadata.Remove(key);
            }

            await _teamRepository.UpdateAsync(team);

            _logger.LogInformation("MFA method {Method} disabled for team {TeamSlug}", method, teamSlug);
            
            await _securityAuditService.LogSecurityEventAsync(new SecurityAuditEvent(
                Guid.NewGuid().ToString(),
                SecurityEventType.ConfigurationChange,
                SecurityEventSeverity.Medium,
                DateTime.UtcNow,
                null,
                teamSlug,
                null,
                null,
                $"MFA method {method} disabled for team",
                new Dictionary<string, string> { { "Method", method.ToString() } },
                null,
                true,
                null
            ));

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error disabling MFA method {Method} for team {TeamSlug}", method, teamSlug);
            return false;
        }
    }

    public async Task<MfaSessionInfo> GetMfaSessionInfoAsync(string teamSlug, CancellationToken cancellationToken = default)
    {
        var sessionCacheKey = $"mfa_session_{teamSlug}";
        
        if (_cache.TryGetValue(sessionCacheKey, out MfaSession? session) && session != null)
        {
            return new MfaSessionInfo
            {
                TeamSlug = teamSlug,
                IsActive = session.IsValid && DateTime.UtcNow < session.ExpiresAt,
                AuthenticatedAt = session.AuthenticatedAt,
                ExpiresAt = session.ExpiresAt,
                Method = session.Method,
                SessionId = session.SessionId
            };
        }

        return new MfaSessionInfo
        {
            TeamSlug = teamSlug,
            IsActive = false
        };
    }

    public async Task<bool> IsMfaValidForOperationAsync(string teamSlug, PaymentOperation operation, CancellationToken cancellationToken = default)
    {
        // Check if MFA is required for this operation
        if (!IsOperationRequireMfa(operation))
        {
            return true; // MFA not required for this operation
        }

        var sessionInfo = await GetMfaSessionInfoAsync(teamSlug, cancellationToken);
        return sessionInfo.IsActive;
    }

    #region Private Helper Methods

    private async Task<MfaChallenge> GenerateMfaChallengeAsync(dynamic team, MfaMethod method, string challengeId, Dictionary<string, object> context)
    {
        var challenge = new MfaChallenge
        {
            ChallengeId = challengeId,
            TeamSlug = team.TeamSlug,
            Method = method,
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.Add(_challengeCacheDuration)
        };

        switch (method)
        {
            case MfaMethod.TOTP:
                // For TOTP, no challenge data needed - user provides current TOTP code
                challenge.ExpectedResponse = ""; // TOTP validation is time-based
                break;

            case MfaMethod.SMS:
            case MfaMethod.Email:
                // Generate a random 6-digit code
                var code = GenerateRandomCode(6);
                challenge.ExpectedResponse = code;
                challenge.ChallengeData = new Dictionary<string, string> { { "Code", code } };
                break;

            case MfaMethod.HardwareToken:
                // Generate a challenge string for hardware token
                var hardwareChallenge = GenerateRandomCode(8);
                challenge.ExpectedResponse = ""; // Hardware token response will be calculated
                challenge.ChallengeData = new Dictionary<string, string> { { "Challenge", hardwareChallenge } };
                break;

            default:
                throw new NotSupportedException($"MFA method {method} is not supported");
        }

        return challenge;
    }

    private async Task SendMfaChallengeAsync(dynamic team, MfaMethod method, MfaChallenge challenge, Dictionary<string, object> context)
    {
        switch (method)
        {
            case MfaMethod.SMS:
                // In a real implementation, send SMS
                _logger.LogInformation("SMS MFA code sent to team {TeamSlug}: {Code}", (string)team.TeamSlug, challenge.ExpectedResponse);
                break;

            case MfaMethod.Email:
                // In a real implementation, send email
                _logger.LogInformation("Email MFA code sent to team {TeamSlug}: {Code}", (string)team.TeamSlug, challenge.ExpectedResponse);
                break;

            case MfaMethod.TOTP:
            case MfaMethod.HardwareToken:
                // No sending required - user has the device/app
                break;
        }

        await Task.CompletedTask;
    }

    private async Task<bool> VerifyMfaResponseAsync(MfaChallenge challenge, string userResponse)
    {
        switch (challenge.Method)
        {
            case MfaMethod.SMS:
            case MfaMethod.Email:
                return string.Equals(challenge.ExpectedResponse, userResponse, StringComparison.Ordinal);

            case MfaMethod.TOTP:
                return await VerifyTotpCodeAsync(challenge.TeamSlug, userResponse);

            case MfaMethod.HardwareToken:
                return await VerifyHardwareTokenResponseAsync(challenge, userResponse);

            default:
                return false;
        }
    }

    private async Task<bool> VerifyTotpCodeAsync(string teamSlug, string totpCode)
    {
        // In a real implementation, verify TOTP code against stored secret
        // This is a simplified implementation
        var isValid = totpCode.Length == 6 && totpCode.All(char.IsDigit);
        
        _logger.LogDebug("TOTP verification for team {TeamSlug}: {IsValid}", teamSlug, isValid);
        return await Task.FromResult(isValid);
    }

    private async Task<bool> VerifyHardwareTokenResponseAsync(MfaChallenge challenge, string response)
    {
        // In a real implementation, verify hardware token response
        // This is a simplified implementation
        var isValid = response.Length >= 6 && response.All(char.IsDigit);
        
        _logger.LogDebug("Hardware token verification for team {TeamSlug}: {IsValid}", challenge.TeamSlug, isValid);
        return await Task.FromResult(isValid);
    }

    private string GenerateRandomCode(int length)
    {
        const string chars = "0123456789";
        var random = new Random();
        var code = new StringBuilder(length);
        
        for (int i = 0; i < length; i++)
        {
            code.Append(chars[random.Next(chars.Length)]);
        }
        
        return code.ToString();
    }

    private string GetMfaInstructions(MfaMethod method)
    {
        return method switch
        {
            MfaMethod.SMS => "A verification code has been sent to your registered mobile number. Please enter the 6-digit code.",
            MfaMethod.Email => "A verification code has been sent to your registered email address. Please enter the 6-digit code.",
            MfaMethod.TOTP => "Please enter the 6-digit code from your authenticator app.",
            MfaMethod.HardwareToken => "Please use your hardware token to generate a response code.",
            _ => "Please complete the multi-factor authentication challenge."
        };
    }

    private Dictionary<string, string> GetAdditionalMfaData(MfaMethod method, MfaChallenge challenge)
    {
        var data = new Dictionary<string, string>();

        switch (method)
        {
            case MfaMethod.HardwareToken:
                if (challenge.ChallengeData.TryGetValue("Challenge", out var hardwareChallenge))
                {
                    data["Challenge"] = hardwareChallenge;
                }
                break;
        }

        return data;
    }

    private bool IsOperationRequireMfa(PaymentOperation operation)
    {
        return operation switch
        {
            PaymentOperation.HighValueTransaction => true,
            PaymentOperation.ProcessRefund => true,
            PaymentOperation.PartialRefund => true,
            _ => false
        };
    }

    #endregion
}

// Supporting enums and classes
public enum MfaMethod
{
    SMS,
    Email,
    TOTP,
    HardwareToken
}

public class MfaOptions
{
    public bool EnableMfa { get; set; } = true;
    public TimeSpan ChallengeValidityDuration { get; set; } = TimeSpan.FromMinutes(5);
    public TimeSpan SessionValidityDuration { get; set; } = TimeSpan.FromMinutes(30);
    public int MaxVerificationAttempts { get; set; } = 3;
    public bool EnableAuditLogging { get; set; } = true;
}

public class MfaChallenge
{
    public string ChallengeId { get; set; } = string.Empty;
    public string TeamSlug { get; set; } = string.Empty;
    public MfaMethod Method { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public string ExpectedResponse { get; set; } = string.Empty;
    public Dictionary<string, string> ChallengeData { get; set; } = new();
}

public class MfaSession
{
    public string SessionId { get; set; } = string.Empty;
    public string TeamSlug { get; set; } = string.Empty;
    public MfaMethod Method { get; set; }
    public DateTime AuthenticatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public bool IsValid { get; set; }
}

public class MfaInitiationResult
{
    public bool IsSuccessful { get; set; }
    public string? ChallengeId { get; set; }
    public MfaMethod Method { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public string? Instructions { get; set; }
    public string? ErrorMessage { get; set; }
    public Dictionary<string, string> AdditionalData { get; set; } = new();
}

public class MfaVerificationResult
{
    public bool IsSuccessful { get; set; }
    public string TeamSlug { get; set; } = string.Empty;
    public string? SessionId { get; set; }
    public DateTime? ValidUntil { get; set; }
    public string? ErrorMessage { get; set; }
}

public class MfaSessionInfo
{
    public string TeamSlug { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public DateTime? AuthenticatedAt { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public MfaMethod? Method { get; set; }
    public string? SessionId { get; set; }
}