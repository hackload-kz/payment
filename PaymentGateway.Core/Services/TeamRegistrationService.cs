using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PaymentGateway.Core.Data;
using PaymentGateway.Core.DTOs.TeamRegistration;
using PaymentGateway.Core.Entities;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace PaymentGateway.Core.Services;

/// <summary>
/// Team registration service implementation
/// </summary>
public class TeamRegistrationService : ITeamRegistrationService
{
    private readonly PaymentGatewayDbContext _context;
    private readonly ILogger<TeamRegistrationService> _logger;
    private readonly IAuditLoggingService _auditLogger;

    public TeamRegistrationService(
        PaymentGatewayDbContext context,
        ILogger<TeamRegistrationService> logger,
        IAuditLoggingService auditLogger)
    {
        _context = context;
        _logger = logger;
        _auditLogger = auditLogger;
    }

    public async Task<TeamRegistrationResponseDto> RegisterTeamAsync(TeamRegistrationRequestDto request, CancellationToken cancellationToken = default)
    {
        var requestId = Guid.NewGuid().ToString();
        
        _logger.LogInformation("Starting team registration for {TeamSlug}", request.TeamSlug);

        try
        {
            // 1. Check if team slug already exists
            var existingTeamBySlug = await _context.Teams
                .FirstOrDefaultAsync(t => t.TeamSlug == request.TeamSlug, cancellationToken);

            if (existingTeamBySlug != null)
            {
                _logger.LogWarning("Team slug {TeamSlug} already exists", request.TeamSlug);
                return new TeamRegistrationResponseDto
                {
                    Success = false,
                    ErrorCode = "2002",
                    Message = "Team slug already exists",
                    Details = new TeamRegistrationDetailsDto
                    {
                        NextSteps = new[] { "Please choose a different team slug" }
                    }
                };
            }

            // 2. Check if email already exists
            var existingTeamByEmail = await _context.Teams
                .FirstOrDefaultAsync(t => t.ContactEmail == request.Email, cancellationToken);

            if (existingTeamByEmail != null)
            {
                _logger.LogWarning("Email {Email} already registered", request.Email);
                return new TeamRegistrationResponseDto
                {
                    Success = false,
                    ErrorCode = "2003",
                    Message = "Email already registered",
                    Details = new TeamRegistrationDetailsDto
                    {
                        NextSteps = new[] { "Please use a different email address or reset password if this is your account" }
                    }
                };
            }

            // 3. Hash password
            var passwordHash = HashPassword(request.Password);
            var passwordHashPreview = passwordHash.Substring(0, 8);

            // 4. Parse supported currencies
            var currencyArray = request.SupportedCurrencies
                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(c => c.Trim().ToUpper())
                .ToArray();

            // 5. Create team entity
            var team = new Team
            {
                Id = Guid.NewGuid(),
                TeamSlug = request.TeamSlug,
                TeamName = request.TeamName,
                ContactEmail = request.Email,
                ContactPhone = request.Phone,
                PasswordHash = passwordHash,
                SuccessUrl = request.SuccessURL,
                FailUrl = request.FailURL,
                NotificationUrl = request.NotificationURL,
                SupportedCurrencies = currencyArray.ToList(),
                BusinessInfo = request.BusinessInfo ?? new Dictionary<string, string>(),
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            // 6. Save to database
            _context.Teams.Add(team);
            await _context.SaveChangesAsync(cancellationToken);

            // 7. Log audit event
            await _auditLogger.LogSystemEventAsync("TEAM_REGISTRATION", 
                $"Team registration successful for {team.TeamSlug}", 
                new Dictionary<string, object>
                {
                    ["team_slug"] = team.TeamSlug,
                    ["team_name"] = team.TeamName,
                    ["email"] = team.ContactEmail,
                    ["supported_currencies"] = string.Join(",", currencyArray),
                    ["request_id"] = requestId
                });

            _logger.LogInformation("Team registration successful for {TeamSlug} with ID {TeamId}", 
                team.TeamSlug, team.Id);

            // 8. Return successful response
            return new TeamRegistrationResponseDto
            {
                Success = true,
                Message = "Team registered successfully",
                TeamSlug = team.TeamSlug,
                TeamId = team.Id,
                PasswordHashPreview = passwordHashPreview,
                CreatedAt = team.CreatedAt,
                Status = team.IsActive ? "ACTIVE" : "INACTIVE",
                ApiEndpoint = "https://gateway.hackload.com/api/v1", // From configuration
                Details = new TeamRegistrationDetailsDto
                {
                    TeamName = team.TeamName,
                    Email = team.ContactEmail,
                    Phone = team.ContactPhone,
                    SuccessURL = team.SuccessUrl,
                    FailURL = team.FailUrl,
                    NotificationURL = team.NotificationUrl,
                    SupportedCurrencies = currencyArray,
                    NextSteps = new[]
                    {
                        "Test payment initialization using your TeamSlug and password",
                        "Configure webhook endpoint for payment notifications",
                        "Review API documentation for integration details",
                        "Test payment flow with small amounts first"
                    }
                }
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during team registration for {TeamSlug}", request.TeamSlug);
            
            // Log audit event for failed registration
            await _auditLogger.LogSystemEventAsync("TEAM_REGISTRATION_FAILED", 
                $"Team registration failed for {request.TeamSlug}: {ex.Message}",
                new Dictionary<string, object>
                {
                    ["team_slug"] = request.TeamSlug,
                    ["error"] = ex.Message,
                    ["request_id"] = requestId
                });

            return new TeamRegistrationResponseDto
            {
                Success = false,
                ErrorCode = "9999",
                Message = "Internal error during registration",
                Details = new TeamRegistrationDetailsDto
                {
                    NextSteps = new[] { "Please try again later or contact support if the problem persists" }
                }
            };
        }
    }

    public async Task<Team?> GetTeamStatusAsync(string teamSlug, CancellationToken cancellationToken = default)
    {
        try
        {
            return await _context.Teams
                .FirstOrDefaultAsync(t => t.TeamSlug == teamSlug, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting team status for {TeamSlug}", teamSlug);
            return null;
        }
    }

    public async Task<bool> IsTeamSlugAvailableAsync(string teamSlug, CancellationToken cancellationToken = default)
    {
        try
        {
            var exists = await _context.Teams
                .AnyAsync(t => t.TeamSlug == teamSlug, cancellationToken);
            
            return !exists;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking team slug availability for {TeamSlug}", teamSlug);
            return false; // Err on the side of caution
        }
    }

    public async Task<Team> UpdateTeamAsync(Guid teamId, Dictionary<string, object> updateData, CancellationToken cancellationToken = default)
    {
        var team = await _context.Teams.FindAsync(new object[] { teamId }, cancellationToken);
        if (team == null)
        {
            throw new ArgumentException($"Team with ID {teamId} not found");
        }

        var originalData = new Dictionary<string, object>();

        foreach (var kvp in updateData)
        {
            switch (kvp.Key.ToLower())
            {
                case "teamname":
                    originalData["team_name"] = team.TeamName;
                    team.TeamName = kvp.Value?.ToString() ?? team.TeamName;
                    break;
                case "email":
                    originalData["email"] = team.ContactEmail;
                    team.ContactEmail = kvp.Value?.ToString() ?? team.ContactEmail;
                    break;
                case "phone":
                    originalData["phone"] = team.ContactPhone;
                    team.ContactPhone = kvp.Value?.ToString();
                    break;
                case "successurl":
                    originalData["success_url"] = team.SuccessUrl;
                    team.SuccessUrl = kvp.Value?.ToString() ?? team.SuccessUrl;
                    break;
                case "failurl":
                    originalData["fail_url"] = team.FailUrl;
                    team.FailUrl = kvp.Value?.ToString() ?? team.FailUrl;
                    break;
                case "notificationurl":
                    originalData["notification_url"] = team.NotificationUrl;
                    team.NotificationUrl = kvp.Value?.ToString();
                    break;
                case "supportedcurrencies":
                    originalData["supported_currencies"] = string.Join(",", team.SupportedCurrencies);
                    if (kvp.Value is string[] currencies)
                    {
                        team.SupportedCurrencies = currencies.ToList();
                    }
                    else if (kvp.Value is List<string> currencyList)
                    {
                        team.SupportedCurrencies = currencyList;
                    }
                    break;
                case "businessinfo":
                    originalData["business_info"] = JsonSerializer.Serialize(team.BusinessInfo);
                    if (kvp.Value is Dictionary<string, string> businessInfo)
                    {
                        team.BusinessInfo = businessInfo;
                    }
                    break;
            }
        }

        team.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync(cancellationToken);

        // Log audit event
        await _auditLogger.LogDatabaseChangeAsync("Team", team.Id.ToString(), "UPDATE", 
            originalData, updateData);

        return team;
    }

    public async Task<bool> SetTeamActiveStatusAsync(string teamSlug, bool isActive, CancellationToken cancellationToken = default)
    {
        try
        {
            var team = await _context.Teams
                .FirstOrDefaultAsync(t => t.TeamSlug == teamSlug, cancellationToken);

            if (team == null)
            {
                _logger.LogWarning("Team not found for activation status change: {TeamSlug}", teamSlug);
                return false;
            }

            var originalStatus = team.IsActive;
            team.IsActive = isActive;
            team.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync(cancellationToken);

            // Log audit event
            await _auditLogger.LogSystemEventAsync("TEAM_STATUS_CHANGED", 
                $"Team {teamSlug} status changed to {(isActive ? "ACTIVE" : "INACTIVE")}", 
                new Dictionary<string, object>
                {
                    ["team_slug"] = team.TeamSlug,
                    ["original_status"] = originalStatus,
                    ["new_status"] = isActive
                });

            _logger.LogInformation("Team {TeamSlug} status changed to {Status}", 
                teamSlug, isActive ? "ACTIVE" : "INACTIVE");

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error changing team status for {TeamSlug}", teamSlug);
            return false;
        }
    }

    /// <summary>
    /// Hash password using SHA-256
    /// </summary>
    /// <param name="password">Plain text password</param>
    /// <returns>Hashed password</returns>
    private static string HashPassword(string password)
    {
        using var sha256 = SHA256.Create();
        var hashedBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(password));
        return Convert.ToHexString(hashedBytes).ToLower();
    }
}