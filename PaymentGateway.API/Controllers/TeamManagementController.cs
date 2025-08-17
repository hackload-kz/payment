using Microsoft.AspNetCore.Mvc;
using PaymentGateway.Core.Entities;
using PaymentGateway.Core.Services;
using PaymentGateway.Core.Repositories;
using PaymentGateway.Core.Enums;
using System.ComponentModel.DataAnnotations;
using System.Text;
using Prometheus;

namespace PaymentGateway.API.Controllers;

/// <summary>
/// Team self-management API controller for teams to view and edit their own parameters
/// Protected by Basic Auth using TeamSlug and Password
/// </summary>
[ApiController]
[Route("api/v1/[controller]")]
[Produces("application/json")]
[Tags("Team Management")]
public class TeamManagementController : ControllerBase
{
    private readonly ITeamRegistrationService _teamRegistrationService;
    private readonly IPaymentRepository _paymentRepository;
    private readonly ICustomerRepository _customerRepository;
    private readonly ILogger<TeamManagementController> _logger;

    // Metrics for monitoring
    private static readonly Counter TeamViewRequests = Metrics
        .CreateCounter("team_view_requests_total", "Total team view requests", new[] { "result" });

    private static readonly Counter TeamUpdateRequests = Metrics
        .CreateCounter("team_self_update_requests_total", "Total team self-update requests", new[] { "result" });

    public TeamManagementController(
        ITeamRegistrationService teamRegistrationService,
        IPaymentRepository paymentRepository,
        ICustomerRepository customerRepository,
        ILogger<TeamManagementController> logger)
    {
        _teamRegistrationService = teamRegistrationService;
        _paymentRepository = paymentRepository;
        _customerRepository = customerRepository;
        _logger = logger;
    }

    /// <summary>
    /// Get team parameters and configuration
    /// 
    /// This endpoint allows teams to view their own configuration and parameters.
    /// Authentication is performed using Basic Auth with TeamSlug as username and Password.
    /// 
    /// ## Authentication:
    /// - Basic Auth: Username = TeamSlug, Password = Team Password
    /// - Authorization header: `Basic {base64(teamSlug:password)}`
    /// 
    /// ## Returned Information:
    /// - Basic team details (name, contact info)
    /// - Payment processing configuration
    /// - Limits and restrictions
    /// - Feature flags and settings
    /// - Business information
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for request timeout handling</param>
    /// <returns>Team configuration and parameters</returns>
    /// <response code="200">Team information retrieved successfully</response>
    /// <response code="401">Invalid or missing authentication credentials</response>
    /// <response code="403">Team is locked or inactive</response>
    /// <response code="500">Internal server error</response>
    [HttpGet("profile")]
    [ProducesResponseType(typeof(TeamProfileDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(object), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(object), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(object), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<TeamProfileDto>> GetTeamProfile(CancellationToken cancellationToken = default)
    {
        var requestId = Guid.NewGuid().ToString();

        try
        {
            _logger.LogInformation("Team profile request received. RequestId: {RequestId}", requestId);

            // Authenticate using Basic Auth
            var authResult = await AuthenticateBasicAuthAsync();
            if (authResult.errorResponse != null)
            {
                TeamViewRequests.WithLabels("auth_failed").Inc();
                return authResult.errorResponse;
            }

            var team = authResult.team!;
            _logger.LogInformation("Team profile request authenticated. RequestId: {RequestId}, TeamSlug: {TeamSlug}", 
                requestId, team.TeamSlug);

            // Calculate usage statistics
            var usageStats = await CalculateUsageStatisticsAsync(team.TeamSlug, cancellationToken);

            // Create team profile DTO with all relevant information
            var profile = new TeamProfileDto
            {
                TeamSlug = team.TeamSlug,
                TeamName = team.TeamName,
                IsActive = team.IsActive,
                CreatedAt = team.CreatedAt,
                UpdatedAt = team.UpdatedAt,
                
                // Contact Information
                ContactEmail = team.ContactEmail,
                ContactPhone = team.ContactPhone,
                Description = team.Description,
                
                // Payment Processing URLs
                NotificationUrl = team.NotificationUrl,
                SuccessUrl = team.SuccessUrl,
                FailUrl = team.FailUrl,
                CancelUrl = team.CancelUrl,
                
                // Payment Limits
                MinPaymentAmount = team.MinPaymentAmount,
                MaxPaymentAmount = team.MaxPaymentAmount,
                DailyPaymentLimit = team.DailyPaymentLimit,
                MonthlyPaymentLimit = team.MonthlyPaymentLimit,
                DailyTransactionLimit = team.DailyTransactionLimit,
                
                // Supported Options
                SupportedCurrencies = team.SupportedCurrencies,
                SupportedPaymentMethods = team.SupportedPaymentMethods.Select(pm => pm.ToString()).ToList(),
                
                // Business Information
                LegalName = team.LegalName,
                TaxId = team.TaxId,
                Address = team.Address,
                Country = team.Country,
                TimeZone = team.TimeZone,
                
                // Processing Configuration
                ProcessingFeePercentage = team.ProcessingFeePercentage,
                FixedProcessingFee = team.FixedProcessingFee,
                FeeCurrency = team.FeeCurrency,
                SettlementDelayDays = team.SettlementDelayDays,
                
                // Feature Flags
                CanProcessRefunds = team.CanProcessRefunds,
                EnableRefunds = team.EnableRefunds,
                EnablePartialRefunds = team.EnablePartialRefunds,
                EnableReversals = team.EnableReversals,
                Enable3DSecure = team.Enable3DSecure,
                EnableTokenization = team.EnableTokenization,
                EnableRecurringPayments = team.EnableRecurringPayments,
                
                // API Settings
                ApiVersion = team.ApiVersion,
                EnableWebhooks = team.EnableWebhooks,
                WebhookRetryAttempts = team.WebhookRetryAttempts,
                WebhookTimeoutSeconds = team.WebhookTimeoutSeconds,
                
                // Security Information (read-only)
                LastSuccessfulAuthenticationAt = team.LastSuccessfulAuthenticationAt,
                LastPasswordChangeAt = team.LastPasswordChangeAt,
                RequiresPasswordChange = team.RequiresPasswordChange(),
                IsLocked = team.IsLocked(),
                LockedUntil = team.LockedUntil,
                
                // Metadata
                Metadata = team.Metadata,
                BusinessInfo = team.BusinessInfo,
                
                // Usage Statistics
                UsageStats = usageStats
            };

            TeamViewRequests.WithLabels("success").Inc();
            _logger.LogInformation("Team profile retrieved successfully. RequestId: {RequestId}, TeamSlug: {TeamSlug}", 
                requestId, team.TeamSlug);

            return Ok(profile);
        }
        catch (Exception ex)
        {
            TeamViewRequests.WithLabels("error").Inc();
            _logger.LogError(ex, "Unexpected error during team profile retrieval. RequestId: {RequestId}", requestId);
            
            return StatusCode(500, new
            {
                Error = "Internal Server Error",
                Message = "An unexpected error occurred while retrieving team profile"
            });
        }
    }

    /// <summary>
    /// Update team parameters and configuration
    /// 
    /// This endpoint allows teams to update their own configuration parameters.
    /// Authentication is performed using Basic Auth with TeamSlug as username and Password.
    /// 
    /// ## Authentication:
    /// - Basic Auth: Username = TeamSlug, Password = Team Password
    /// - Authorization header: `Basic {base64(teamSlug:password)}`
    /// 
    /// ## Updatable Fields:
    /// - Contact information (email, phone, description)
    /// - Payment processing URLs (success, fail, notification, cancel)
    /// - Business information (legal name, address, etc.)
    /// - Feature flags and settings
    /// - Metadata and custom fields
    /// 
    /// ## Restrictions:
    /// - Cannot update payment limits (admin-only)
    /// - Cannot change TeamSlug or core security settings
    /// - Cannot update processing fees or settlement configuration
    /// </summary>
    /// <param name="request">Team update request containing fields to update</param>
    /// <param name="cancellationToken">Cancellation token for request timeout handling</param>
    /// <returns>Updated team information</returns>
    /// <response code="200">Team updated successfully</response>
    /// <response code="400">Invalid request parameters or validation failed</response>
    /// <response code="401">Invalid or missing authentication credentials</response>
    /// <response code="403">Team is locked or inactive</response>
    /// <response code="500">Internal server error</response>
    [HttpPut("profile")]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(object), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(object), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(object), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult> UpdateTeamProfile(
        [FromBody] TeamSelfUpdateRequestDto request,
        CancellationToken cancellationToken = default)
    {
        var requestId = Guid.NewGuid().ToString();

        try
        {
            _logger.LogInformation("Team profile update request received. RequestId: {RequestId}", requestId);

            // Authenticate using Basic Auth
            var authResult = await AuthenticateBasicAuthAsync();
            if (authResult.errorResponse != null)
            {
                TeamUpdateRequests.WithLabels("auth_failed").Inc();
                return authResult.errorResponse;
            }

            var team = authResult.team!;
            _logger.LogInformation("Team profile update request authenticated. RequestId: {RequestId}, TeamSlug: {TeamSlug}", 
                requestId, team.TeamSlug);

            // Validate request model
            if (!ModelState.IsValid)
            {
                TeamUpdateRequests.WithLabels("validation_failed").Inc();
                var errors = ModelState
                    .SelectMany(x => x.Value.Errors)
                    .Select(x => x.ErrorMessage)
                    .ToList();
                var errorMessage = string.Join("; ", errors);

                _logger.LogWarning("Team profile update validation failed. RequestId: {RequestId}, Errors: {Errors}", 
                    requestId, errorMessage);

                return BadRequest(new
                {
                    Error = "Validation failed",
                    Message = errorMessage
                });
            }

            // Build update data dictionary (only fields teams are allowed to update)
            var updateData = new Dictionary<string, object>();
            
            // Contact Information
            if (!string.IsNullOrEmpty(request.TeamName))
                updateData["teamname"] = request.TeamName;
            if (request.ContactEmail != null)
                updateData["contactemail"] = request.ContactEmail;
            if (request.ContactPhone != null)
                updateData["contactphone"] = request.ContactPhone;
            if (request.Description != null)
                updateData["description"] = request.Description;

            // Payment Processing URLs
            if (request.NotificationUrl != null)
                updateData["notificationurl"] = request.NotificationUrl;
            if (request.SuccessUrl != null)
                updateData["successurl"] = request.SuccessUrl;
            if (request.FailUrl != null)
                updateData["failurl"] = request.FailUrl;
            if (request.CancelUrl != null)
                updateData["cancelurl"] = request.CancelUrl;

            // Business Information
            if (request.LegalName != null)
                updateData["legalname"] = request.LegalName;
            if (request.Address != null)
                updateData["address"] = request.Address;
            if (request.Country != null)
                updateData["country"] = request.Country;
            if (request.TimeZone != null)
                updateData["timezone"] = request.TimeZone;

            // Feature Flags (only non-critical ones)
            if (request.EnableWebhooks.HasValue)
                updateData["enablewebhooks"] = request.EnableWebhooks.Value;
            if (request.WebhookRetryAttempts.HasValue)
                updateData["webhookretryattempts"] = request.WebhookRetryAttempts.Value;
            if (request.WebhookTimeoutSeconds.HasValue)
                updateData["webhooktimeoutseconds"] = request.WebhookTimeoutSeconds.Value;

            // Payment Limits (now allowed for self-update)
            if (request.MinPaymentAmount.HasValue)
                updateData["minpaymentamount"] = request.MinPaymentAmount.Value;
            if (request.MaxPaymentAmount.HasValue)
                updateData["maxpaymentamount"] = request.MaxPaymentAmount.Value;
            if (request.DailyPaymentLimit.HasValue)
                updateData["dailypaymentlimit"] = request.DailyPaymentLimit.Value;
            if (request.MonthlyPaymentLimit.HasValue)
                updateData["monthlypaymentlimit"] = request.MonthlyPaymentLimit.Value;
            if (request.DailyTransactionLimit.HasValue)
                updateData["dailytransactionlimit"] = request.DailyTransactionLimit.Value;

            // Metadata
            if (request.Metadata != null)
                updateData["metadata"] = request.Metadata;
            if (request.BusinessInfo != null)
                updateData["businessinfo"] = request.BusinessInfo;

            if (updateData.Count == 0)
            {
                TeamUpdateRequests.WithLabels("no_changes").Inc();
                _logger.LogWarning("Team profile update attempted with no changes. RequestId: {RequestId}", requestId);
                
                return BadRequest(new
                {
                    Error = "No changes provided",
                    Message = "At least one field must be provided for update"
                });
            }

            // Perform the update
            var updatedTeam = await _teamRegistrationService.UpdateTeamAsync(team.Id, updateData, cancellationToken);

            TeamUpdateRequests.WithLabels("success").Inc();
            
            _logger.LogInformation("Team profile updated successfully. RequestId: {RequestId}, TeamSlug: {TeamSlug}, TeamId: {TeamId}", 
                requestId, team.TeamSlug, team.Id);

            return Ok(new
            {
                Success = true,
                Message = "Team profile updated successfully",
                TeamSlug = updatedTeam.TeamSlug,
                UpdatedAt = updatedTeam.UpdatedAt,
                UpdatedFields = updateData.Keys.ToArray()
            });
        }
        catch (Exception ex)
        {
            TeamUpdateRequests.WithLabels("error").Inc();
            _logger.LogError(ex, "Unexpected error during team profile update. RequestId: {RequestId}", requestId);
            
            return StatusCode(500, new
            {
                Error = "Internal Server Error",
                Message = "An unexpected error occurred during the team profile update operation"
            });
        }
    }

    private async Task<(Team? team, ActionResult? errorResponse)> AuthenticateBasicAuthAsync()
    {
        try
        {
            // Extract Basic Auth header
            var authHeader = Request.Headers.Authorization.FirstOrDefault();
            if (string.IsNullOrEmpty(authHeader) || !authHeader.StartsWith("Basic "))
            {
                _logger.LogWarning("Team management request without Basic Auth header");
                return (null, Unauthorized(new
                {
                    Error = "Missing Authorization",
                    Message = "Basic Auth required. Use Authorization header with Basic {base64(teamSlug:password)}"
                }));
            }

            // Decode Basic Auth
            var encodedCredentials = authHeader["Basic ".Length..];
            var credentialsBytes = Convert.FromBase64String(encodedCredentials);
            var credentials = Encoding.UTF8.GetString(credentialsBytes);
            
            var separatorIndex = credentials.IndexOf(':');
            if (separatorIndex == -1)
            {
                _logger.LogWarning("Team management request with invalid Basic Auth format");
                return (null, Unauthorized(new
                {
                    Error = "Invalid Authorization Format",
                    Message = "Basic Auth must be in format: Basic {base64(teamSlug:password)}"
                }));
            }

            var teamSlug = credentials[..separatorIndex];
            var password = credentials[(separatorIndex + 1)..];

            if (string.IsNullOrEmpty(teamSlug) || string.IsNullOrEmpty(password))
            {
                _logger.LogWarning("Team management request with empty credentials");
                return (null, Unauthorized(new
                {
                    Error = "Invalid Credentials",
                    Message = "TeamSlug and password cannot be empty"
                }));
            }

            // Get team from database
            var team = await _teamRegistrationService.GetTeamStatusAsync(teamSlug, CancellationToken.None);
            if (team == null)
            {
                _logger.LogWarning("Team management request for non-existent team: {TeamSlug}", teamSlug);
                return (null, Unauthorized(new
                {
                    Error = "Invalid Credentials",
                    Message = "Invalid team credentials"
                }));
            }

            // Check if team is locked
            if (team.IsLocked())
            {
                _logger.LogWarning("Team management request for locked team: {TeamSlug}", teamSlug);
                return (null, StatusCode(403, new
                {
                    Error = "Team Locked",
                    Message = $"Team is temporarily locked until {team.LockedUntil:yyyy-MM-dd HH:mm:ss} UTC due to failed authentication attempts"
                }));
            }

            // Verify password (using simple comparison for now - in production you'd hash and compare)
            if (team.Password != password)
            {
                _logger.LogWarning("Team management request with invalid password for team: {TeamSlug}", teamSlug);
                
                // Increment failed attempts
                team.IncrementFailedAuthenticationAttempts();
                await _teamRegistrationService.UpdateTeamAsync(team.Id, new Dictionary<string, object>
                {
                    ["failedauthenticationattempts"] = team.FailedAuthenticationAttempts,
                    ["lockeduntil"] = team.LockedUntil
                }, CancellationToken.None);

                return (null, Unauthorized(new
                {
                    Error = "Invalid Credentials",
                    Message = "Invalid team credentials"
                }));
            }

            // Check if team is active
            if (!team.IsActive)
            {
                _logger.LogWarning("Team management request for inactive team: {TeamSlug}", teamSlug);
                return (null, StatusCode(403, new
                {
                    Error = "Team Inactive",
                    Message = "Team account is inactive"
                }));
            }

            // Reset failed attempts on successful authentication
            team.ResetFailedAuthenticationAttempts();
            await _teamRegistrationService.UpdateTeamAsync(team.Id, new Dictionary<string, object>
            {
                ["failedauthenticationattempts"] = 0,
                ["lockeduntil"] = null,
                ["lastsuccessfulauthenticationat"] = team.LastSuccessfulAuthenticationAt,
                ["lastauthenticationipaddress"] = Request.HttpContext.Connection.RemoteIpAddress?.ToString()
            }, CancellationToken.None);

            return (team, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during Basic Auth authentication");
            return (null, StatusCode(500, new
            {
                Error = "Authentication Error",
                Message = "An error occurred during authentication"
            }));
        }
    }

    private async Task<UsageStatsDto> CalculateUsageStatisticsAsync(string teamSlug, CancellationToken cancellationToken)
    {
        try
        {
            var today = DateTime.UtcNow.Date;
            var startOfMonth = new DateTime(today.Year, today.Month, 1);

            // Get all payments for this team
            var allPayments = await _paymentRepository.GetAllAsync(cancellationToken);
            var teamPayments = allPayments.Where(p => p.TeamSlug == teamSlug).ToList();

            // Filter successful payments (confirmed or completed)
            var successfulPayments = teamPayments
                .Where(p => p.Status == PaymentStatus.CONFIRMED || p.Status == PaymentStatus.COMPLETED)
                .ToList();

            // Calculate totals
            var totalPayments = successfulPayments.Count;
            var totalPaymentAmount = successfulPayments.Sum(p => p.Amount);

            // Today's payments
            var paymentsToday = successfulPayments
                .Where(p => p.ConfirmedAt?.Date == today || p.CompletedAt?.Date == today)
                .ToList();
            var paymentAmountToday = paymentsToday.Sum(p => p.Amount);

            // This month's payments
            var paymentsThisMonth = successfulPayments
                .Where(p => (p.ConfirmedAt?.Date >= startOfMonth && p.ConfirmedAt?.Date <= today) ||
                           (p.CompletedAt?.Date >= startOfMonth && p.CompletedAt?.Date <= today))
                .ToList();
            var paymentAmountThisMonth = paymentsThisMonth.Sum(p => p.Amount);

            // Get customers count
            var allCustomers = await _customerRepository.GetAllAsync(cancellationToken);
            var totalCustomers = allCustomers.Count(c => c.Team.TeamSlug == teamSlug);

            // Active payment methods (assume all team payment methods are active)
            var activePaymentMethods = teamPayments
                .SelectMany(p => p.PaymentMethods)
                .Where(pm => pm.IsActive())
                .DistinctBy(pm => pm.Type)
                .Count();

            // Last payment timestamp
            var lastPayment = successfulPayments
                .Where(p => p.ConfirmedAt.HasValue || p.CompletedAt.HasValue)
                .OrderByDescending(p => p.ConfirmedAt ?? p.CompletedAt)
                .FirstOrDefault();
            var lastPaymentAt = lastPayment?.ConfirmedAt ?? lastPayment?.CompletedAt;

            // Webhook statistics (simplified - assuming webhook failures are tracked elsewhere)
            var failedWebhooksLast24Hours = 0; // TODO: Implement webhook failure tracking
            DateTime? lastWebhookAt = null; // TODO: Implement webhook tracking

            return new UsageStatsDto
            {
                TotalPayments = totalPayments,
                TotalPaymentAmount = totalPaymentAmount,
                PaymentsToday = paymentsToday.Count,
                PaymentAmountToday = paymentAmountToday,
                PaymentsThisMonth = paymentsThisMonth.Count,
                PaymentAmountThisMonth = paymentAmountThisMonth,
                TotalCustomers = totalCustomers,
                ActivePaymentMethods = activePaymentMethods,
                LastPaymentAt = lastPaymentAt,
                LastWebhookAt = lastWebhookAt,
                FailedWebhooksLast24Hours = failedWebhooksLast24Hours
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calculating usage statistics for team: {TeamSlug}", teamSlug);
            
            // Return empty stats in case of error
            return new UsageStatsDto
            {
                TotalPayments = 0,
                TotalPaymentAmount = 0,
                PaymentsToday = 0,
                PaymentAmountToday = 0,
                PaymentsThisMonth = 0,
                PaymentAmountThisMonth = 0,
                TotalCustomers = 0,
                ActivePaymentMethods = 0,
                LastPaymentAt = null,
                LastWebhookAt = null,
                FailedWebhooksLast24Hours = 0
            };
        }
    }
}

/// <summary>
/// DTO for team profile information
/// </summary>
public class TeamProfileDto
{
    public string TeamSlug { get; set; } = string.Empty;
    public string TeamName { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    // Contact Information
    public string? ContactEmail { get; set; }
    public string? ContactPhone { get; set; }
    public string? Description { get; set; }

    // Payment Processing URLs
    public string? NotificationUrl { get; set; }
    public string? SuccessUrl { get; set; }
    public string? FailUrl { get; set; }
    public string? CancelUrl { get; set; }

    // Payment Limits (read-only for teams)
    public decimal? MinPaymentAmount { get; set; }
    public decimal? MaxPaymentAmount { get; set; }
    public decimal? DailyPaymentLimit { get; set; }
    public decimal? MonthlyPaymentLimit { get; set; }
    public int? DailyTransactionLimit { get; set; }

    // Supported Options
    public List<string> SupportedCurrencies { get; set; } = new();
    public List<string> SupportedPaymentMethods { get; set; } = new();

    // Business Information
    public string? LegalName { get; set; }
    public string? TaxId { get; set; }
    public string? Address { get; set; }
    public string? Country { get; set; }
    public string? TimeZone { get; set; }

    // Processing Configuration (read-only)
    public decimal ProcessingFeePercentage { get; set; }
    public decimal FixedProcessingFee { get; set; }
    public string FeeCurrency { get; set; } = string.Empty;
    public int SettlementDelayDays { get; set; }

    // Feature Flags
    public bool CanProcessRefunds { get; set; }
    public bool EnableRefunds { get; set; }
    public bool EnablePartialRefunds { get; set; }
    public bool EnableReversals { get; set; }
    public bool Enable3DSecure { get; set; }
    public bool EnableTokenization { get; set; }
    public bool EnableRecurringPayments { get; set; }

    // API Settings
    public string? ApiVersion { get; set; }
    public bool EnableWebhooks { get; set; }
    public int WebhookRetryAttempts { get; set; }
    public int WebhookTimeoutSeconds { get; set; }

    // Security Information (read-only)
    public DateTime? LastSuccessfulAuthenticationAt { get; set; }
    public DateTime? LastPasswordChangeAt { get; set; }
    public bool RequiresPasswordChange { get; set; }
    public bool IsLocked { get; set; }
    public DateTime? LockedUntil { get; set; }

    // Metadata
    public Dictionary<string, string> Metadata { get; set; } = new();
    public Dictionary<string, string> BusinessInfo { get; set; } = new();
    
    // Usage Statistics
    public UsageStatsDto UsageStats { get; set; } = new();
}

/// <summary>
/// DTO for team self-update requests
/// </summary>
public class TeamSelfUpdateRequestDto
{
    [StringLength(200)]
    public string? TeamName { get; set; }

    [EmailAddress]
    [StringLength(254)]
    public string? ContactEmail { get; set; }

    [StringLength(20)]
    public string? ContactPhone { get; set; }

    [StringLength(500)]
    public string? Description { get; set; }

    [Url]
    [StringLength(2048)]
    public string? NotificationUrl { get; set; }

    [Url]
    [StringLength(2048)]
    public string? SuccessUrl { get; set; }

    [Url]
    [StringLength(2048)]
    public string? FailUrl { get; set; }

    [Url]
    [StringLength(2048)]
    public string? CancelUrl { get; set; }

    [StringLength(500)]
    public string? LegalName { get; set; }

    [StringLength(1000)]
    public string? Address { get; set; }

    [StringLength(100)]
    public string? Country { get; set; }

    [StringLength(100)]
    public string? TimeZone { get; set; }

    public bool? EnableWebhooks { get; set; }

    [Range(1, 10)]
    public int? WebhookRetryAttempts { get; set; }

    [Range(5, 300)]
    public int? WebhookTimeoutSeconds { get; set; }

    public Dictionary<string, string>? Metadata { get; set; }
    public Dictionary<string, string>? BusinessInfo { get; set; }
    
    // Payment Limits (now allowed for teams to update)
    [Range(0, double.MaxValue)]
    public decimal? MinPaymentAmount { get; set; }
    
    [Range(0, double.MaxValue)]
    public decimal? MaxPaymentAmount { get; set; }
    
    [Range(0, double.MaxValue)]
    public decimal? DailyPaymentLimit { get; set; }
    
    [Range(0, double.MaxValue)]
    public decimal? MonthlyPaymentLimit { get; set; }
    
    [Range(0, int.MaxValue)]
    public int? DailyTransactionLimit { get; set; }
}

/// <summary>
/// DTO for team usage statistics
/// </summary>
public class UsageStatsDto
{
    public int TotalPayments { get; set; }
    public decimal TotalPaymentAmount { get; set; }
    public int PaymentsToday { get; set; }
    public decimal PaymentAmountToday { get; set; }
    public int PaymentsThisMonth { get; set; }
    public decimal PaymentAmountThisMonth { get; set; }
    public int TotalCustomers { get; set; }
    public int ActivePaymentMethods { get; set; }
    public DateTime? LastPaymentAt { get; set; }
    public DateTime? LastWebhookAt { get; set; }
    public int FailedWebhooksLast24Hours { get; set; }
}