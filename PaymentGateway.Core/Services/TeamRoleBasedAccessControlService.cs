using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Caching.Memory;
using PaymentGateway.Core.Repositories;
using PaymentGateway.Core.Entities;
using PaymentGateway.Core.Enums;
using System.Security.Cryptography;
using System.Text;

namespace PaymentGateway.Core.Services;

/// <summary>
/// Service interface for role-based access control in payment operations
/// </summary>
public interface ITeamRoleBasedAccessControlService
{
    Task<AccessControlResult> CheckPaymentOperationAccessAsync(string teamSlug, PaymentOperation operation, Dictionary<string, object> context, CancellationToken cancellationToken = default);
    Task<List<TeamRole>> GetTeamRolesAsync(string teamSlug, CancellationToken cancellationToken = default);
    Task<bool> AssignRoleToTeamAsync(string teamSlug, TeamRole role, CancellationToken cancellationToken = default);
    Task<bool> RevokeRoleFromTeamAsync(string teamSlug, TeamRole role, CancellationToken cancellationToken = default);
    Task<AccessControlSettings> GetAccessControlSettingsAsync(string teamSlug, CancellationToken cancellationToken = default);
    Task<bool> UpdateAccessControlSettingsAsync(string teamSlug, AccessControlSettings settings, CancellationToken cancellationToken = default);
    Task<List<SecurityPermission>> GetEffectivePermissionsAsync(string teamSlug, CancellationToken cancellationToken = default);
}

/// <summary>
/// Enhanced role-based access control service for payment operations
/// </summary>
public class TeamRoleBasedAccessControlService : ITeamRoleBasedAccessControlService
{
    private readonly ITeamRepository _teamRepository;
    private readonly IMemoryCache _cache;
    private readonly ILogger<TeamRoleBasedAccessControlService> _logger;
    private readonly ISecurityAuditService _securityAuditService;
    private readonly TimeSpan _cacheExpiration = TimeSpan.FromMinutes(10);

    public TeamRoleBasedAccessControlService(
        ITeamRepository teamRepository,
        IMemoryCache cache,
        ILogger<TeamRoleBasedAccessControlService> logger,
        ISecurityAuditService securityAuditService)
    {
        _teamRepository = teamRepository;
        _cache = cache;
        _logger = logger;
        _securityAuditService = securityAuditService;
    }

    public async Task<AccessControlResult> CheckPaymentOperationAccessAsync(
        string teamSlug, 
        PaymentOperation operation, 
        Dictionary<string, object> context, 
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Checking access for TeamSlug: {TeamSlug}, Operation: {Operation}", teamSlug, operation);

            // Get team information with caching
            var team = await GetTeamWithCachingAsync(teamSlug, cancellationToken);
            if (team == null)
            {
                await _securityAuditService.LogSecurityEventAsync(CreateSecurityEvent(
                    SecurityEventType.SecurityPolicyViolation,
                    teamSlug,
                    context.GetValueOrDefault("IpAddress")?.ToString(),
                    $"Access denied: Team {teamSlug} not found",
                    false,
                    "Team not found"
                ));

                return new AccessControlResult
                {
                    IsAllowed = false,
                    Reason = "Team not found or inactive",
                    RequiredPermissions = GetRequiredPermissions(operation),
                    ActualPermissions = new List<SecurityPermission>()
                };
            }

            // Check if team is active and not locked
            if (!team.IsActive || team.IsLocked())
            {
                await _securityAuditService.LogSecurityEventAsync(CreateSecurityEvent(
                    SecurityEventType.SecurityPolicyViolation,
                    teamSlug,
                    context.GetValueOrDefault("IpAddress")?.ToString(),
                    $"Access denied: Team {teamSlug} is inactive or locked",
                    false,
                    "Team inactive or locked"
                ));

                return new AccessControlResult
                {
                    IsAllowed = false,
                    Reason = team.IsLocked() ? "Team is temporarily locked" : "Team is inactive",
                    RequiredPermissions = GetRequiredPermissions(operation),
                    ActualPermissions = new List<SecurityPermission>()
                };
            }

            // Get effective permissions for the team
            var effectivePermissions = await GetEffectivePermissionsAsync(teamSlug, cancellationToken);
            var requiredPermissions = GetRequiredPermissions(operation);

            // Check if team has all required permissions
            var hasAllPermissions = requiredPermissions.All(required => 
                effectivePermissions.Any(effective => effective.PermissionType == required.PermissionType));

            // Perform operation-specific validation
            var operationValidation = await ValidateOperationSpecificRulesAsync(team, operation, context);

            var isAllowed = hasAllPermissions && operationValidation.IsValid;

            // Log access control decision
            await _securityAuditService.LogSecurityEventAsync(CreateSecurityEvent(
                SecurityEventType.DataAccess,
                teamSlug,
                context.GetValueOrDefault("IpAddress")?.ToString(),
                $"Access control check for operation {operation}: {(isAllowed ? "Allowed" : "Denied")}",
                isAllowed,
                isAllowed ? null : (operationValidation.Reason ?? "Insufficient permissions")
            ));

            return new AccessControlResult
            {
                IsAllowed = isAllowed,
                Reason = isAllowed ? "Access granted" : (operationValidation.Reason ?? "Insufficient permissions"),
                RequiredPermissions = requiredPermissions,
                ActualPermissions = effectivePermissions,
                AdditionalContext = operationValidation.AdditionalContext
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking access control for TeamSlug: {TeamSlug}, Operation: {Operation}", teamSlug, operation);
            
            await _securityAuditService.LogSecurityEventAsync(CreateSecurityEvent(
                SecurityEventType.SystemError,
                teamSlug,
                context.GetValueOrDefault("IpAddress")?.ToString(),
                $"Access control system error for operation {operation}",
                false,
                ex.Message
            ));

            return new AccessControlResult
            {
                IsAllowed = false,
                Reason = "Access control system error",
                RequiredPermissions = new List<SecurityPermission>(),
                ActualPermissions = new List<SecurityPermission>()
            };
        }
    }

    public async Task<List<TeamRole>> GetTeamRolesAsync(string teamSlug, CancellationToken cancellationToken = default)
    {
        var cacheKey = $"team_roles_{teamSlug}";
        
        if (_cache.TryGetValue(cacheKey, out List<TeamRole>? cachedRoles))
        {
            return cachedRoles ?? new List<TeamRole>();
        }

        var team = await _teamRepository.GetByTeamSlugAsync(teamSlug, cancellationToken);
        if (team == null)
        {
            return new List<TeamRole>();
        }

        // Determine roles based on team configuration and permissions
        var roles = DetermineTeamRoles(team);
        
        _cache.Set(cacheKey, roles, _cacheExpiration);
        return roles;
    }

    public async Task<bool> AssignRoleToTeamAsync(string teamSlug, TeamRole role, CancellationToken cancellationToken = default)
    {
        try
        {
            var team = await _teamRepository.GetByTeamSlugAsync(teamSlug, cancellationToken);
            if (team == null)
            {
                return false;
            }

            // Add role to team metadata
            var currentRoles = await GetTeamRolesAsync(teamSlug, cancellationToken);
            if (!currentRoles.Contains(role))
            {
                currentRoles.Add(role);
                await UpdateTeamRolesAsync(team, currentRoles);
                
                // Clear cache
                _cache.Remove($"team_roles_{teamSlug}");
                _cache.Remove($"team_permissions_{teamSlug}");

                _logger.LogInformation("Role {Role} assigned to team {TeamSlug}", role, teamSlug);
                
                await _securityAuditService.LogSecurityEventAsync(CreateSecurityEvent(
                    SecurityEventType.ConfigurationChange,
                    teamSlug,
                    null,
                    $"Role {role} assigned to team {teamSlug}",
                    true
                ));
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error assigning role {Role} to team {TeamSlug}", role, teamSlug);
            return false;
        }
    }

    public async Task<bool> RevokeRoleFromTeamAsync(string teamSlug, TeamRole role, CancellationToken cancellationToken = default)
    {
        try
        {
            var team = await _teamRepository.GetByTeamSlugAsync(teamSlug, cancellationToken);
            if (team == null)
            {
                return false;
            }

            var currentRoles = await GetTeamRolesAsync(teamSlug, cancellationToken);
            if (currentRoles.Contains(role))
            {
                currentRoles.Remove(role);
                await UpdateTeamRolesAsync(team, currentRoles);
                
                // Clear cache
                _cache.Remove($"team_roles_{teamSlug}");
                _cache.Remove($"team_permissions_{teamSlug}");

                _logger.LogInformation("Role {Role} revoked from team {TeamSlug}", role, teamSlug);
                
                await _securityAuditService.LogSecurityEventAsync(CreateSecurityEvent(
                    SecurityEventType.ConfigurationChange,
                    teamSlug,
                    null,
                    $"Role {role} revoked from team {teamSlug}",
                    true
                ));
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error revoking role {Role} from team {TeamSlug}", role, teamSlug);
            return false;
        }
    }

    public async Task<AccessControlSettings> GetAccessControlSettingsAsync(string teamSlug, CancellationToken cancellationToken = default)
    {
        var team = await _teamRepository.GetByTeamSlugAsync(teamSlug, cancellationToken);
        if (team == null)
        {
            return new AccessControlSettings();
        }

        return new AccessControlSettings
        {
            TeamSlug = teamSlug,
            RequireMultiFactorAuthentication = team.Metadata.GetValueOrDefault("RequireMFA", "false") == "true",
            AllowedIpRanges = team.Metadata.GetValueOrDefault("AllowedIpRanges", "").Split(',', StringSplitOptions.RemoveEmptyEntries).ToList(),
            SessionTimeoutMinutes = int.TryParse(team.Metadata.GetValueOrDefault("SessionTimeoutMinutes", "30"), out var timeout) ? timeout : 30,
            RequireStrongAuthentication = team.Metadata.GetValueOrDefault("RequireStrongAuth", "true") == "true",
            EnableRiskBasedAuthentication = team.Metadata.GetValueOrDefault("EnableRiskAuth", "true") == "true",
            MaxConcurrentSessions = int.TryParse(team.Metadata.GetValueOrDefault("MaxConcurrentSessions", "5"), out var maxSessions) ? maxSessions : 5,
            RestrictedOperationHours = ParseOperationHours(team.Metadata.GetValueOrDefault("RestrictedHours", "")),
            RequireApprovalForHighValueTransactions = team.Metadata.GetValueOrDefault("RequireApprovalHighValue", "true") == "true",
            HighValueTransactionThreshold = decimal.TryParse(team.Metadata.GetValueOrDefault("HighValueThreshold", "100000"), out var threshold) ? threshold : 100000
        };
    }

    public async Task<bool> UpdateAccessControlSettingsAsync(string teamSlug, AccessControlSettings settings, CancellationToken cancellationToken = default)
    {
        try
        {
            var team = await _teamRepository.GetByTeamSlugAsync(teamSlug, cancellationToken);
            if (team == null)
            {
                return false;
            }

            // Update team metadata with new settings
            team.Metadata["RequireMFA"] = settings.RequireMultiFactorAuthentication.ToString().ToLower();
            team.Metadata["AllowedIpRanges"] = string.Join(",", settings.AllowedIpRanges);
            team.Metadata["SessionTimeoutMinutes"] = settings.SessionTimeoutMinutes.ToString();
            team.Metadata["RequireStrongAuth"] = settings.RequireStrongAuthentication.ToString().ToLower();
            team.Metadata["EnableRiskAuth"] = settings.EnableRiskBasedAuthentication.ToString().ToLower();
            team.Metadata["MaxConcurrentSessions"] = settings.MaxConcurrentSessions.ToString();
            team.Metadata["RestrictedHours"] = SerializeOperationHours(settings.RestrictedOperationHours);
            team.Metadata["RequireApprovalHighValue"] = settings.RequireApprovalForHighValueTransactions.ToString().ToLower();
            team.Metadata["HighValueThreshold"] = settings.HighValueTransactionThreshold.ToString();

            await _teamRepository.UpdateAsync(team);

            // Clear relevant caches
            _cache.Remove($"team_{teamSlug}");
            _cache.Remove($"team_settings_{teamSlug}");

            _logger.LogInformation("Access control settings updated for team {TeamSlug}", teamSlug);
            
            await _securityAuditService.LogSecurityEventAsync(CreateSecurityEvent(
                SecurityEventType.ConfigurationChange,
                teamSlug,
                null,
                $"Access control settings updated for team {teamSlug}",
                true
            ));

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating access control settings for team {TeamSlug}", teamSlug);
            return false;
        }
    }

    public async Task<List<SecurityPermission>> GetEffectivePermissionsAsync(string teamSlug, CancellationToken cancellationToken = default)
    {
        var cacheKey = $"team_permissions_{teamSlug}";
        
        if (_cache.TryGetValue(cacheKey, out List<SecurityPermission>? cachedPermissions))
        {
            return cachedPermissions ?? new List<SecurityPermission>();
        }

        var team = await _teamRepository.GetByTeamSlugAsync(teamSlug, cancellationToken);
        if (team == null)
        {
            return new List<SecurityPermission>();
        }

        var roles = await GetTeamRolesAsync(teamSlug, cancellationToken);
        var permissions = new List<SecurityPermission>();

        foreach (var role in roles)
        {
            permissions.AddRange(GetPermissionsForRole(role));
        }

        // Add team-specific permissions based on configuration
        permissions.AddRange(GetTeamSpecificPermissions(team));

        // Remove duplicates and sort by permission level
        var effectivePermissions = permissions
            .GroupBy(p => p.PermissionType)
            .Select(g => g.OrderByDescending(p => p.Level).First())
            .ToList();

        _cache.Set(cacheKey, effectivePermissions, _cacheExpiration);
        return effectivePermissions;
    }

    #region Private Helper Methods

    private async Task<Team?> GetTeamWithCachingAsync(string teamSlug, CancellationToken cancellationToken)
    {
        var cacheKey = $"team_{teamSlug}";
        
        if (_cache.TryGetValue(cacheKey, out Team? cachedTeam))
        {
            return cachedTeam;
        }

        var team = await _teamRepository.GetByTeamSlugAsync(teamSlug, cancellationToken);
        if (team != null)
        {
            _cache.Set(cacheKey, team, _cacheExpiration);
        }

        return team;
    }

    private List<TeamRole> DetermineTeamRoles(Team team)
    {
        var roles = new List<TeamRole>();

        // Basic roles based on team status
        if (team.IsActive)
        {
            roles.Add(TeamRole.BasicUser);
        }

        // Payment processing roles
        if (team.SupportedPaymentMethods.Contains(PaymentMethod.Card))
        {
            roles.Add(TeamRole.PaymentProcessor);
        }

        // Refund handling roles
        if (team.CanProcessRefunds)
        {
            roles.Add(TeamRole.RefundProcessor);
        }

        // High-value transaction roles
        if (team.MaxPaymentAmount.HasValue && team.MaxPaymentAmount.Value > 100000)
        {
            roles.Add(TeamRole.HighValueTransactionProcessor);
        }

        // Administrative roles (based on metadata or specific flags)
        if (team.Metadata.GetValueOrDefault("IsAdminTeam", "false") == "true")
        {
            roles.Add(TeamRole.Administrator);
        }

        return roles;
    }

    private List<SecurityPermission> GetRequiredPermissions(PaymentOperation operation)
    {
        return operation switch
        {
            PaymentOperation.InitiatePayment => new List<SecurityPermission>
            {
                new SecurityPermission(PermissionType.PaymentInitiation, PermissionLevel.Standard)
            },
            PaymentOperation.ConfirmPayment => new List<SecurityPermission>
            {
                new SecurityPermission(PermissionType.PaymentConfirmation, PermissionLevel.Standard)
            },
            PaymentOperation.CancelPayment => new List<SecurityPermission>
            {
                new SecurityPermission(PermissionType.PaymentCancellation, PermissionLevel.Standard)
            },
            PaymentOperation.CheckPaymentStatus => new List<SecurityPermission>
            {
                new SecurityPermission(PermissionType.PaymentStatusQuery, PermissionLevel.Read)
            },
            PaymentOperation.ProcessRefund => new List<SecurityPermission>
            {
                new SecurityPermission(PermissionType.RefundProcessing, PermissionLevel.Standard)
            },
            PaymentOperation.PartialRefund => new List<SecurityPermission>
            {
                new SecurityPermission(PermissionType.RefundProcessing, PermissionLevel.Advanced)
            },
            PaymentOperation.HighValueTransaction => new List<SecurityPermission>
            {
                new SecurityPermission(PermissionType.PaymentInitiation, PermissionLevel.Advanced),
                new SecurityPermission(PermissionType.HighValueTransactionApproval, PermissionLevel.Standard)
            },
            _ => new List<SecurityPermission>()
        };
    }

    private List<SecurityPermission> GetPermissionsForRole(TeamRole role)
    {
        return role switch
        {
            TeamRole.BasicUser => new List<SecurityPermission>
            {
                new SecurityPermission(PermissionType.PaymentStatusQuery, PermissionLevel.Read)
            },
            TeamRole.PaymentProcessor => new List<SecurityPermission>
            {
                new SecurityPermission(PermissionType.PaymentInitiation, PermissionLevel.Standard),
                new SecurityPermission(PermissionType.PaymentConfirmation, PermissionLevel.Standard),
                new SecurityPermission(PermissionType.PaymentCancellation, PermissionLevel.Standard),
                new SecurityPermission(PermissionType.PaymentStatusQuery, PermissionLevel.Read)
            },
            TeamRole.RefundProcessor => new List<SecurityPermission>
            {
                new SecurityPermission(PermissionType.RefundProcessing, PermissionLevel.Standard)
            },
            TeamRole.HighValueTransactionProcessor => new List<SecurityPermission>
            {
                new SecurityPermission(PermissionType.PaymentInitiation, PermissionLevel.Advanced),
                new SecurityPermission(PermissionType.HighValueTransactionApproval, PermissionLevel.Standard)
            },
            TeamRole.Administrator => new List<SecurityPermission>
            {
                new SecurityPermission(PermissionType.PaymentInitiation, PermissionLevel.Advanced),
                new SecurityPermission(PermissionType.PaymentConfirmation, PermissionLevel.Advanced),
                new SecurityPermission(PermissionType.PaymentCancellation, PermissionLevel.Advanced),
                new SecurityPermission(PermissionType.RefundProcessing, PermissionLevel.Advanced),
                new SecurityPermission(PermissionType.HighValueTransactionApproval, PermissionLevel.Advanced),
                new SecurityPermission(PermissionType.SystemConfiguration, PermissionLevel.Standard)
            },
            _ => new List<SecurityPermission>()
        };
    }

    private List<SecurityPermission> GetTeamSpecificPermissions(Team team)
    {
        var permissions = new List<SecurityPermission>();

        // Add permissions based on team configuration
        if (team.EnableRefunds)
        {
            permissions.Add(new SecurityPermission(PermissionType.RefundProcessing, PermissionLevel.Standard));
        }

        if (team.EnablePartialRefunds)
        {
            permissions.Add(new SecurityPermission(PermissionType.RefundProcessing, PermissionLevel.Advanced));
        }

        if (team.EnableRecurringPayments)
        {
            permissions.Add(new SecurityPermission(PermissionType.RecurringPaymentManagement, PermissionLevel.Standard));
        }

        return permissions;
    }

    private async Task<OperationValidationResult> ValidateOperationSpecificRulesAsync(Team team, PaymentOperation operation, Dictionary<string, object> context)
    {
        var result = new OperationValidationResult { IsValid = true };

        switch (operation)
        {
            case PaymentOperation.InitiatePayment:
                if (context.TryGetValue("Amount", out var amountObj) && decimal.TryParse(amountObj.ToString(), out var amount))
                {
                    if (team.MaxPaymentAmount.HasValue && amount > team.MaxPaymentAmount.Value)
                    {
                        result.IsValid = false;
                        result.Reason = "Transaction amount exceeds team limit";
                    }
                }
                break;

            case PaymentOperation.HighValueTransaction:
                var settings = await GetAccessControlSettingsAsync(team.TeamSlug, CancellationToken.None);
                if (settings.RequireApprovalForHighValueTransactions)
                {
                    result.AdditionalContext["RequiresManualApproval"] = "true";
                }
                break;

            case PaymentOperation.ProcessRefund:
                if (!team.EnableRefunds)
                {
                    result.IsValid = false;
                    result.Reason = "Refunds are not enabled for this team";
                }
                break;

            case PaymentOperation.PartialRefund:
                if (!team.EnablePartialRefunds)
                {
                    result.IsValid = false;
                    result.Reason = "Partial refunds are not enabled for this team";
                }
                break;
        }

        return result;
    }

    private async Task UpdateTeamRolesAsync(Team team, List<TeamRole> roles)
    {
        var roleNames = roles.Select(r => r.ToString()).ToList();
        team.Metadata["AssignedRoles"] = string.Join(",", roleNames);
        await _teamRepository.UpdateAsync(team);
    }

    private List<OperationHour> ParseOperationHours(string operationHoursString)
    {
        if (string.IsNullOrEmpty(operationHoursString))
        {
            return new List<OperationHour>();
        }

        // Simple parsing logic - can be enhanced based on requirements
        return new List<OperationHour>();
    }

    private string SerializeOperationHours(List<OperationHour> operationHours)
    {
        // Simple serialization - can be enhanced based on requirements
        return "";
    }

    private SecurityAuditEvent CreateSecurityEvent(SecurityEventType eventType, string? teamSlug, string? ipAddress, string description, bool isSuccessful, string? errorMessage = null)
    {
        return new SecurityAuditEvent(
            Guid.NewGuid().ToString(),
            eventType,
            isSuccessful ? SecurityEventSeverity.Low : SecurityEventSeverity.Medium,
            DateTime.UtcNow,
            null,
            teamSlug,
            ipAddress,
            null,
            description,
            new Dictionary<string, string>(),
            null,
            isSuccessful,
            errorMessage
        );
    }

    #endregion
}

// Supporting enums and classes
public enum PaymentOperation
{
    InitiatePayment,
    ConfirmPayment,
    CancelPayment,
    CheckPaymentStatus,
    ProcessRefund,
    PartialRefund,
    HighValueTransaction,
    RecurringPaymentSetup,
    TokenizedPayment
}

public enum TeamRole
{
    BasicUser,
    PaymentProcessor,
    RefundProcessor,
    HighValueTransactionProcessor,
    Administrator
}

public enum PermissionType
{
    PaymentInitiation,
    PaymentConfirmation,
    PaymentCancellation,
    PaymentStatusQuery,
    RefundProcessing,
    HighValueTransactionApproval,
    RecurringPaymentManagement,
    SystemConfiguration
}

public enum PermissionLevel
{
    Read = 1,
    Standard = 2,
    Advanced = 3,
    Administrative = 4
}

public record SecurityPermission(PermissionType PermissionType, PermissionLevel Level);

public class AccessControlResult
{
    public bool IsAllowed { get; set; }
    public string Reason { get; set; } = string.Empty;
    public List<SecurityPermission> RequiredPermissions { get; set; } = new();
    public List<SecurityPermission> ActualPermissions { get; set; } = new();
    public Dictionary<string, string> AdditionalContext { get; set; } = new();
}

public class AccessControlSettings
{
    public string TeamSlug { get; set; } = string.Empty;
    public bool RequireMultiFactorAuthentication { get; set; }
    public List<string> AllowedIpRanges { get; set; } = new();
    public int SessionTimeoutMinutes { get; set; } = 30;
    public bool RequireStrongAuthentication { get; set; } = true;
    public bool EnableRiskBasedAuthentication { get; set; } = true;
    public int MaxConcurrentSessions { get; set; } = 5;
    public List<OperationHour> RestrictedOperationHours { get; set; } = new();
    public bool RequireApprovalForHighValueTransactions { get; set; } = true;
    public decimal HighValueTransactionThreshold { get; set; } = 100000;
}

public class OperationHour
{
    public DayOfWeek DayOfWeek { get; set; }
    public TimeSpan StartTime { get; set; }
    public TimeSpan EndTime { get; set; }
}

public class OperationValidationResult
{
    public bool IsValid { get; set; }
    public string? Reason { get; set; }
    public Dictionary<string, string> AdditionalContext { get; set; } = new();
}