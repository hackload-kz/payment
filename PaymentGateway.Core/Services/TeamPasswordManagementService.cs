using Microsoft.Extensions.Logging;
using PaymentGateway.Core.Repositories;
using PaymentGateway.Core.Entities;
using PaymentGateway.Core.Interfaces;
using System.Security.Cryptography;
using System.Text;

namespace PaymentGateway.Core.Services;

/// <summary>
/// Service interface for team password management
/// </summary>
public interface ITeamPasswordManagementService
{
    Task<bool> ValidatePasswordAsync(string teamSlug, string password, CancellationToken cancellationToken = default);
    Task<bool> UpdatePasswordAsync(string teamSlug, string newPassword, CancellationToken cancellationToken = default);
    Task<string> GenerateSecurePasswordAsync(int length = 32);
    Task<bool> IsPasswordExpiredAsync(string teamSlug, CancellationToken cancellationToken = default);
    Task<TeamPasswordInfo> GetPasswordInfoAsync(string teamSlug, CancellationToken cancellationToken = default);
    Task<bool> LockTeamAccountAsync(string teamSlug, TimeSpan lockDuration, CancellationToken cancellationToken = default);
    Task<bool> UnlockTeamAccountAsync(string teamSlug, CancellationToken cancellationToken = default);
}

/// <summary>
/// Team password management service implementation
/// </summary>
public class TeamPasswordManagementService : ITeamPasswordManagementService
{
    private readonly ITeamRepository _teamRepository;
    private readonly ILogger<TeamPasswordManagementService> _logger;
    private readonly IComprehensiveAuditService _auditService;
    private readonly IMetricsService _metricsService;

    // Password policy settings
    private readonly int _maxFailedAttempts = 5;
    private readonly TimeSpan _accountLockDuration = TimeSpan.FromMinutes(30);
    private readonly TimeSpan _passwordExpiryPeriod = TimeSpan.FromDays(90);

    public TeamPasswordManagementService(
        ITeamRepository teamRepository,
        ILogger<TeamPasswordManagementService> logger,
        IComprehensiveAuditService auditService,
        IMetricsService metricsService)
    {
        _teamRepository = teamRepository;
        _logger = logger;
        _auditService = auditService;
        _metricsService = metricsService;
    }

    public async Task<bool> ValidatePasswordAsync(string teamSlug, string password, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Validating password for TeamSlug: {TeamSlug}", teamSlug);

            var team = await _teamRepository.GetByTeamSlugAsync(teamSlug, cancellationToken);
            if (team == null || !team.IsActive)
            {
                _logger.LogWarning("Team not found or inactive for TeamSlug: {TeamSlug}", teamSlug);
                return false;
            }

            // Check if account is locked
            if (IsAccountLocked(team))
            {
                _logger.LogWarning("Account is locked for TeamSlug: {TeamSlug}", teamSlug);
                await _auditService.LogSystemEventAsync(
                    AuditAction.AuthenticationBlocked,
                    "TeamPasswordManagement",
                    $"Authentication attempt blocked for locked team: {teamSlug}");
                return false;
            }

            // Validate password (now using plain text comparison for simplicity)
            var isPasswordValid = password == team.Password;

            if (isPasswordValid)
            {
                // Reset failed attempts on successful authentication
                if (team.FailedAuthenticationAttempts > 0)
                {
                    team.FailedAuthenticationAttempts = 0;
                    team.LastSuccessfulAuthenticationAt = DateTime.UtcNow;
                    await _teamRepository.UpdateAsync(team, cancellationToken);
                }

                await _auditService.LogSystemEventAsync(
                    AuditAction.AuthenticationSucceeded,
                    "TeamPasswordManagement",
                    $"Password validation successful for TeamSlug: {teamSlug}");

                await RecordPasswordMetricsAsync(teamSlug, "validation_success");
                return true;
            }
            else
            {
                // Increment failed attempts
                team.FailedAuthenticationAttempts++;
                
                // Lock account if max attempts exceeded
                if (team.FailedAuthenticationAttempts >= _maxFailedAttempts)
                {
                    team.LockedUntil = DateTime.UtcNow.Add(_accountLockDuration);
                    
                    await _auditService.LogSystemEventAsync(
                        AuditAction.AccountLocked,
                        "TeamPasswordManagement",
                        $"Account locked due to {_maxFailedAttempts} failed attempts for TeamSlug: {teamSlug}");
                }

                await _teamRepository.UpdateAsync(team, cancellationToken);

                await _auditService.LogSystemEventAsync(
                    AuditAction.AuthenticationFailed,
                    "TeamPasswordManagement",
                    $"Password validation failed for TeamSlug: {teamSlug}. Failed attempts: {team.FailedAuthenticationAttempts}");

                await RecordPasswordMetricsAsync(teamSlug, "validation_failure");
                return false;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating password for TeamSlug: {TeamSlug}", teamSlug);
            await RecordPasswordMetricsAsync(teamSlug, "validation_error");
            return false;
        }
    }

    public async Task<bool> UpdatePasswordAsync(string teamSlug, string newPassword, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Updating password for TeamSlug: {TeamSlug}", teamSlug);

            var team = await _teamRepository.GetByTeamSlugAsync(teamSlug, cancellationToken);
            if (team == null || !team.IsActive)
            {
                _logger.LogWarning("Team not found or inactive for TeamSlug: {TeamSlug}", teamSlug);
                return false;
            }

            // Validate password complexity (basic validation)
            if (!IsPasswordComplex(newPassword))
            {
                _logger.LogWarning("Password does not meet complexity requirements for TeamSlug: {TeamSlug}", teamSlug);
                return false;
            }

            // Update team record with new password (plain text for simplicity)
            team.Password = newPassword;
            team.LastPasswordChangeAt = DateTime.UtcNow;
            team.FailedAuthenticationAttempts = 0; // Reset failed attempts
            team.LockedUntil = null; // Unlock account

            await _teamRepository.UpdateAsync(team, cancellationToken);

            await _auditService.LogSystemEventAsync(
                AuditAction.PasswordChanged,
                "TeamPasswordManagement",
                $"Password updated successfully for TeamSlug: {teamSlug}");

            await RecordPasswordMetricsAsync(teamSlug, "password_update");

            _logger.LogInformation("Password updated successfully for TeamSlug: {TeamSlug}", teamSlug);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating password for TeamSlug: {TeamSlug}", teamSlug);
            await RecordPasswordMetricsAsync(teamSlug, "password_update_error");
            return false;
        }
    }

    public async Task<string> GenerateSecurePasswordAsync(int length = 32)
    {
        const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789!@#$%^&*";
        using var rng = RandomNumberGenerator.Create();
        var bytes = new byte[length];
        rng.GetBytes(bytes);

        await Task.CompletedTask; // Make method async
        return new string(bytes.Select(b => chars[b % chars.Length]).ToArray());
    }

    public async Task<bool> IsPasswordExpiredAsync(string teamSlug, CancellationToken cancellationToken = default)
    {
        try
        {
            var team = await _teamRepository.GetByTeamSlugAsync(teamSlug, cancellationToken);
            if (team == null)
                return true; // Consider missing team as expired

            if (!team.LastPasswordChangeAt.HasValue)
                return true; // No password change date means expired

            var timeSinceLastChange = DateTime.UtcNow - team.LastPasswordChangeAt.Value;
            return timeSinceLastChange > _passwordExpiryPeriod;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking password expiry for TeamSlug: {TeamSlug}", teamSlug);
            return true; // Default to expired on error
        }
    }

    public async Task<TeamPasswordInfo> GetPasswordInfoAsync(string teamSlug, CancellationToken cancellationToken = default)
    {
        try
        {
            var team = await _teamRepository.GetByTeamSlugAsync(teamSlug, cancellationToken);
            if (team == null)
            {
                return new TeamPasswordInfo
                {
                    TeamSlug = teamSlug,
                    IsExpired = true,
                    IsLocked = false,
                    FailedAttempts = 0
                };
            }

            var isExpired = await IsPasswordExpiredAsync(teamSlug, cancellationToken);
            var isLocked = IsAccountLocked(team);

            return new TeamPasswordInfo
            {
                TeamSlug = teamSlug,
                LastPasswordChangeAt = team.LastPasswordChangeAt,
                IsExpired = isExpired,
                IsLocked = isLocked,
                LockedUntil = team.LockedUntil,
                FailedAttempts = team.FailedAuthenticationAttempts,
                LastSuccessfulAuthenticationAt = team.LastSuccessfulAuthenticationAt,
                LastAuthenticationIpAddress = team.LastAuthenticationIpAddress
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting password info for TeamSlug: {TeamSlug}", teamSlug);
            return new TeamPasswordInfo
            {
                TeamSlug = teamSlug,
                IsExpired = true,
                IsLocked = false,
                FailedAttempts = 0
            };
        }
    }

    public async Task<bool> LockTeamAccountAsync(string teamSlug, TimeSpan lockDuration, CancellationToken cancellationToken = default)
    {
        try
        {
            var team = await _teamRepository.GetByTeamSlugAsync(teamSlug, cancellationToken);
            if (team == null)
                return false;

            team.LockedUntil = DateTime.UtcNow.Add(lockDuration);
            await _teamRepository.UpdateAsync(team, cancellationToken);

            await _auditService.LogSystemEventAsync(
                AuditAction.AccountLocked,
                "TeamPasswordManagement",
                $"Account manually locked for TeamSlug: {teamSlug}, Duration: {lockDuration}");

            await RecordPasswordMetricsAsync(teamSlug, "account_locked");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error locking team account for TeamSlug: {TeamSlug}", teamSlug);
            return false;
        }
    }

    public async Task<bool> UnlockTeamAccountAsync(string teamSlug, CancellationToken cancellationToken = default)
    {
        try
        {
            var team = await _teamRepository.GetByTeamSlugAsync(teamSlug, cancellationToken);
            if (team == null)
                return false;

            team.LockedUntil = null;
            team.FailedAuthenticationAttempts = 0;
            await _teamRepository.UpdateAsync(team, cancellationToken);

            await _auditService.LogSystemEventAsync(
                AuditAction.AccountUnlocked,
                "TeamPasswordManagement",
                $"Account manually unlocked for TeamSlug: {teamSlug}");

            await RecordPasswordMetricsAsync(teamSlug, "account_unlocked");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error unlocking team account for TeamSlug: {TeamSlug}", teamSlug);
            return false;
        }
    }

    private bool IsAccountLocked(Team team)
    {
        return team.LockedUntil.HasValue && team.LockedUntil.Value > DateTime.UtcNow;
    }


    private bool IsPasswordComplex(string password)
    {
        // Basic password complexity validation
        if (string.IsNullOrWhiteSpace(password) || password.Length < 8)
            return false;

        var hasUpper = password.Any(char.IsUpper);
        var hasLower = password.Any(char.IsLower);
        var hasDigit = password.Any(char.IsDigit);
        var hasSpecial = password.Any(c => "!@#$%^&*()_+-=[]{}|;:,.<>?".Contains(c));

        return hasUpper && hasLower && hasDigit && hasSpecial;
    }

    private async Task RecordPasswordMetricsAsync(string teamSlug, string operation)
    {
        try
        {
            await _metricsService.RecordCounterAsync("team_password_operations_total", 1, new Dictionary<string, string>
            {
                { "team_slug", teamSlug },
                { "operation", operation }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error recording password metrics for team {TeamSlug}", teamSlug);
        }
    }
}

// Supporting classes
public class TeamPasswordInfo
{
    public string TeamSlug { get; set; } = string.Empty;
    public DateTime? LastPasswordChangeAt { get; set; }
    public bool IsExpired { get; set; }
    public bool IsLocked { get; set; }
    public DateTime? LockedUntil { get; set; }
    public int FailedAttempts { get; set; }
    public DateTime? LastSuccessfulAuthenticationAt { get; set; }
    public string? LastAuthenticationIpAddress { get; set; }
}