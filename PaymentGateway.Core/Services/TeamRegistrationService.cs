using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PaymentGateway.Core.Configuration;
using PaymentGateway.Core.Data;
using PaymentGateway.Core.DTOs.TeamRegistration;
using PaymentGateway.Core.Entities;
using System.ComponentModel.DataAnnotations;
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
    private readonly ApiOptions _apiOptions;

    public TeamRegistrationService(
        PaymentGatewayDbContext context,
        ILogger<TeamRegistrationService> logger,
        IAuditLoggingService auditLogger,
        IOptions<ApiOptions> apiOptions)
    {
        _context = context;
        _logger = logger;
        _auditLogger = auditLogger;
        _apiOptions = apiOptions.Value;
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

            // 3. Use raw password (for simplicity in hackathon project)
            var password = request.Password;
            var passwordPreview = password.Length > 8 ? password.Substring(0, 8) : password;

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
                Password = password,
                SuccessUrl = request.SuccessURL,
                FailUrl = request.FailURL,
                NotificationUrl = request.NotificationURL,
                SupportedCurrencies = currencyArray.ToList(),
                BusinessInfo = request.BusinessInfo ?? new Dictionary<string, string>(),
                FailedAuthenticationAttempts = 0, // Explicitly set to avoid NOT NULL constraint issues
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
                PasswordHashPreview = passwordPreview,
                PasswordHashFull = password, // Raw password for development/testing only
                CreatedAt = team.CreatedAt,
                Status = team.IsActive ? "ACTIVE" : "INACTIVE",
                ApiEndpoint = _apiOptions.GetApiEndpoint(),
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
        // Use AsNoTracking to get a fresh copy, then attach and modify
        var team = await _context.Teams
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == teamId, cancellationToken);
            
        if (team == null)
        {
            throw new ArgumentException($"Team with ID {teamId} not found");
        }
        
        // Attach the entity to the context for tracking
        _context.Teams.Attach(team);

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
                case "minpaymentamount":
                    originalData["min_payment_amount"] = team.MinPaymentAmount;
                    if (kvp.Value != null && decimal.TryParse(kvp.Value.ToString(), out var minAmount))
                    {
                        team.MinPaymentAmount = minAmount;
                    }
                    break;
                case "maxpaymentamount":
                    originalData["max_payment_amount"] = team.MaxPaymentAmount;
                    if (kvp.Value != null && decimal.TryParse(kvp.Value.ToString(), out var maxAmount))
                    {
                        team.MaxPaymentAmount = maxAmount;
                    }
                    break;
                case "dailypaymentlimit":
                    originalData["daily_payment_limit"] = team.DailyPaymentLimit;
                    if (kvp.Value != null && decimal.TryParse(kvp.Value.ToString(), out var dailyLimit))
                    {
                        team.DailyPaymentLimit = dailyLimit;
                    }
                    break;
                case "monthlypaymentlimit":
                    originalData["monthly_payment_limit"] = team.MonthlyPaymentLimit;
                    if (kvp.Value != null && decimal.TryParse(kvp.Value.ToString(), out var monthlyLimit))
                    {
                        team.MonthlyPaymentLimit = monthlyLimit;
                    }
                    break;
                case "dailytransactionlimit":
                    originalData["daily_transaction_limit"] = team.DailyTransactionLimit;
                    if (kvp.Value != null && int.TryParse(kvp.Value.ToString(), out var dailyTxnLimit))
                    {
                        team.DailyTransactionLimit = dailyTxnLimit;
                    }
                    break;
            }
        }

        // Validate business rules after all updates
        var (isValid, validationErrors) = team.ValidateForCreation();
        if (!isValid)
        {
            throw new ValidationException(string.Join("; ", validationErrors));
        }

        team.UpdatedAt = DateTime.UtcNow;
        
        // Explicitly mark the entity as modified to ensure EF tracks changes
        _context.Entry(team).State = Microsoft.EntityFrameworkCore.EntityState.Modified;
        
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

    public async Task<TeamInfoDto?> GetTeamInfoAsync(string teamSlug, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Getting comprehensive team information for {TeamSlug}", teamSlug);

        try
        {
            // Get team with related data
            var team = await _context.Teams
                .Include(t => t.Payments)
                .Include(t => t.Customers)
                .Include(t => t.PaymentMethods)
                .FirstOrDefaultAsync(t => t.TeamSlug == teamSlug, cancellationToken);

            if (team == null)
            {
                _logger.LogWarning("Team not found for slug: {TeamSlug}", teamSlug);
                return null;
            }

            // Calculate usage statistics
            var usageStats = await CalculateUsageStatsAsync(team, cancellationToken);
            
            // Determine team status
            var status = CalculateTeamStatus(team, usageStats);

            var teamInfo = new TeamInfoDto
            {
                // Basic team information
                Id = team.Id,
                TeamSlug = team.TeamSlug,
                TeamName = team.TeamName,
                IsActive = team.IsActive,
                CreatedAt = team.CreatedAt,
                UpdatedAt = team.UpdatedAt,
                
                // Contact information
                ContactEmail = team.ContactEmail,
                ContactPhone = team.ContactPhone,
                Description = team.Description,
                
                // Authentication and security settings
                SecretKey = team.SecretKey,
                LastPasswordChangeAt = team.LastPasswordChangeAt,
                FailedAuthenticationAttempts = team.FailedAuthenticationAttempts,
                LockedUntil = team.LockedUntil,
                LastSuccessfulAuthenticationAt = team.LastSuccessfulAuthenticationAt,
                LastAuthenticationIpAddress = team.LastAuthenticationIpAddress,
                
                // Payment processing URLs
                NotificationUrl = team.NotificationUrl,
                SuccessUrl = team.SuccessUrl,
                FailUrl = team.FailUrl,
                CancelUrl = team.CancelUrl,
                
                // Payment limits and restrictions
                MinPaymentAmount = team.MinPaymentAmount,
                MaxPaymentAmount = team.MaxPaymentAmount,
                DailyPaymentLimit = team.DailyPaymentLimit,
                MonthlyPaymentLimit = team.MonthlyPaymentLimit,
                DailyTransactionLimit = team.DailyTransactionLimit,
                
                // Supported currencies and payment methods
                SupportedCurrencies = team.SupportedCurrencies,
                SupportedPaymentMethods = team.SupportedPaymentMethods,
                
                // Processing permissions
                CanProcessRefunds = team.CanProcessRefunds,
                
                // Business information
                LegalName = team.LegalName,
                TaxId = team.TaxId,
                Address = team.Address,
                Country = team.Country,
                TimeZone = team.TimeZone,
                
                // Fee and pricing configuration
                ProcessingFeePercentage = team.ProcessingFeePercentage,
                FixedProcessingFee = team.FixedProcessingFee,
                FeeCurrency = team.FeeCurrency,
                
                // Settlement configuration
                SettlementDelayDays = team.SettlementDelayDays,
                SettlementAccountNumber = team.SettlementAccountNumber,
                SettlementBankCode = team.SettlementBankCode,
                
                // Risk and fraud settings
                EnableFraudDetection = team.EnableFraudDetection,
                MaxFraudScore = team.MaxFraudScore,
                RequireManualReviewForHighRisk = team.RequireManualReviewForHighRisk,
                
                // Feature flags
                EnableRefunds = team.EnableRefunds,
                EnablePartialRefunds = team.EnablePartialRefunds,
                EnableReversals = team.EnableReversals,
                Enable3DSecure = team.Enable3DSecure,
                EnableTokenization = team.EnableTokenization,
                EnableRecurringPayments = team.EnableRecurringPayments,
                
                // API and webhook settings
                ApiVersion = team.ApiVersion,
                EnableWebhooks = team.EnableWebhooks,
                WebhookRetryAttempts = team.WebhookRetryAttempts,
                WebhookTimeoutSeconds = team.WebhookTimeoutSeconds,
                WebhookSecret = team.WebhookSecret,
                
                // Metadata and custom fields
                Metadata = team.Metadata,
                BusinessInfo = team.BusinessInfo,
                
                // Calculated fields
                UsageStats = usageStats,
                Status = status
            };

            _logger.LogInformation("Successfully retrieved team information for {TeamSlug}", teamSlug);
            return teamInfo;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting team information for {TeamSlug}", teamSlug);
            throw;
        }
    }

    private async Task<TeamUsageStatsDto> CalculateUsageStatsAsync(Team team, CancellationToken cancellationToken)
    {
        var today = DateTime.UtcNow.Date;
        var monthStart = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1);

        // Calculate payment statistics
        var totalPayments = team.Payments.Count;
        var totalPaymentAmount = team.Payments.Sum(p => p.Amount);
        
        var paymentsToday = team.Payments.Count(p => p.CreatedAt.Date == today);
        var paymentAmountToday = team.Payments
            .Where(p => p.CreatedAt.Date == today)
            .Sum(p => p.Amount);
            
        var paymentsThisMonth = team.Payments.Count(p => p.CreatedAt >= monthStart);
        var paymentAmountThisMonth = team.Payments
            .Where(p => p.CreatedAt >= monthStart)
            .Sum(p => p.Amount);

        var lastPaymentAt = team.Payments
            .OrderByDescending(p => p.CreatedAt)
            .FirstOrDefault()?.CreatedAt;

        // Calculate webhook failures (this would require a webhook log table in a real implementation)
        var failedWebhooksLast24Hours = 0; // Placeholder - would need webhook logging

        return new TeamUsageStatsDto
        {
            TotalPayments = totalPayments,
            TotalPaymentAmount = totalPaymentAmount,
            PaymentsToday = paymentsToday,
            PaymentAmountToday = paymentAmountToday,
            PaymentsThisMonth = paymentsThisMonth,
            PaymentAmountThisMonth = paymentAmountThisMonth,
            TotalCustomers = team.Customers.Count,
            ActivePaymentMethods = team.PaymentMethods.Count,
            LastPaymentAt = lastPaymentAt,
            LastWebhookAt = null, // Placeholder - would need webhook logging
            FailedWebhooksLast24Hours = failedWebhooksLast24Hours
        };
    }

    private TeamStatusDto CalculateTeamStatus(Team team, TeamUsageStatsDto usageStats)
    {
        var status = new TeamStatusDto
        {
            IsLocked = team.IsLocked(),
            RequiresPasswordChange = team.RequiresPasswordChange(),
            IsHealthy = true
        };

        // Check daily limit
        if (team.DailyPaymentLimit.HasValue)
        {
            status.HasReachedDailyLimit = usageStats.PaymentAmountToday >= team.DailyPaymentLimit.Value;
        }

        // Check monthly limit
        if (team.MonthlyPaymentLimit.HasValue)
        {
            status.HasReachedMonthlyLimit = usageStats.PaymentAmountThisMonth >= team.MonthlyPaymentLimit.Value;
        }

        // Determine health issues
        if (status.IsLocked)
        {
            status.IsHealthy = false;
            status.HealthIssues.Add("Team is locked due to authentication failures");
        }

        if (!team.IsActive)
        {
            status.IsHealthy = false;
            status.HealthIssues.Add("Team is not active");
        }

        if (status.HasReachedDailyLimit)
        {
            status.IsHealthy = false;
            status.HealthIssues.Add("Daily payment limit reached");
        }

        if (status.HasReachedMonthlyLimit)
        {
            status.IsHealthy = false;
            status.HealthIssues.Add("Monthly payment limit reached");
        }

        // Add warnings
        if (status.RequiresPasswordChange)
        {
            status.Warnings.Add("Password change required (older than 90 days)");
        }

        if (team.FailedAuthenticationAttempts > 2)
        {
            status.Warnings.Add($"Multiple failed authentication attempts ({team.FailedAuthenticationAttempts})");
        }

        if (team.DailyPaymentLimit.HasValue && 
            usageStats.PaymentAmountToday > team.DailyPaymentLimit.Value * 0.8m)
        {
            status.Warnings.Add("Approaching daily payment limit (80% reached)");
        }

        if (team.MonthlyPaymentLimit.HasValue && 
            usageStats.PaymentAmountThisMonth > team.MonthlyPaymentLimit.Value * 0.8m)
        {
            status.Warnings.Add("Approaching monthly payment limit (80% reached)");
        }

        return status;
    }

}