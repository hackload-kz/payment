using Microsoft.AspNetCore.Mvc;
using PaymentGateway.Core.DTOs.TeamRegistration;
using PaymentGateway.Core.Services;
using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using System.Text;
using Prometheus;

namespace PaymentGateway.API.Controllers;

/// <summary>
/// Team registration API controller for merchant onboarding
/// </summary>
[ApiController]
[Route("api/v1/[controller]")]
[Produces("application/json")]
[Tags("Team Registration")]
public class TeamRegistrationController : ControllerBase
{
    private readonly ITeamRegistrationService _teamRegistrationService;
    private readonly ILogger<TeamRegistrationController> _logger;

    // Metrics for monitoring
    private static readonly Counter TeamRegistrationRequests = Metrics
        .CreateCounter("team_registration_requests_total", "Total team registration requests", new[] { "result" });

    private static readonly Histogram TeamRegistrationDuration = Metrics
        .CreateHistogram("team_registration_duration_seconds", "Team registration request duration");

    public TeamRegistrationController(
        ITeamRegistrationService teamRegistrationService,
        ILogger<TeamRegistrationController> logger)
    {
        _teamRegistrationService = teamRegistrationService;
        _logger = logger;
    }

    /// <summary>
    /// Register a new team/merchant for payment processing
    /// 
    /// This endpoint allows new merchants to register for payment processing services.
    /// Upon successful registration, the team receives API credentials and can start
    /// accepting payments through the PaymentGateway service.
    /// 
    /// ## Registration Process:
    /// 1. **Validation**: Request data is validated for completeness and format
    /// 2. **Uniqueness Check**: TeamSlug and email are checked for uniqueness
    /// 3. **Password Hashing**: Password is securely hashed using SHA-256
    /// 4. **Team Creation**: Team record is created in the database
    /// 5. **Credentials**: API credentials are generated and returned
    /// 
    /// ## Requirements:
    /// - Unique team slug (3-50 characters, alphanumeric, hyphens, underscores)
    /// - Strong password (minimum 8 characters)
    /// - Valid email address
    /// - Valid success and fail URLs for payment redirects
    /// - Acceptance of terms of service
    /// 
    /// ## Rate Limiting:
    /// - 10 registration attempts per IP address per hour
    /// - Prevents automated registration abuse
    /// 
    /// ## Security Features:
    /// - Password hashing using SHA-256
    /// - Input validation and sanitization
    /// - Rate limiting protection
    /// - Audit logging of registration attempts
    /// </summary>
    /// <param name="request">Team registration request containing all team details</param>
    /// <param name="cancellationToken">Cancellation token for request timeout handling</param>
    /// <returns>Team registration response with credentials and next steps</returns>
    /// <response code="201">Team registered successfully - returns team credentials and details</response>
    /// <response code="400">Invalid request parameters or validation failed</response>
    /// <response code="409">Conflict - team slug or email already exists</response>
    /// <response code="429">Rate limit exceeded - too many registration attempts</response>
    /// <response code="500">Internal server error - unexpected system error occurred</response>
    /// <remarks>
    /// ### Example Request:
    /// ```json
    /// {
    ///   "teamSlug": "my-online-store",
    ///   "password": "SecurePassword123!",
    ///   "teamName": "My Online Store",
    ///   "email": "merchant@mystore.com",
    ///   "phone": "+1234567890",
    ///   "successURL": "https://mystore.com/payment/success",
    ///   "failURL": "https://mystore.com/payment/fail",
    ///   "notificationURL": "https://mystore.com/payment/webhook",
    ///   "supportedCurrencies": "RUB,USD,EUR",
    ///   "businessInfo": {
    ///     "businessType": "ecommerce",
    ///     "website": "https://mystore.com"
    ///   },
    ///   "acceptTerms": true
    /// }
    /// ```
    /// 
    /// ### Example Response:
    /// ```json
    /// {
    ///   "success": true,
    ///   "message": "Team registered successfully",
    ///   "teamSlug": "my-online-store",
    ///   "teamId": "123e4567-e89b-12d3-a456-426614174000",
    ///   "passwordHashPreview": "d3ad9315...",
    ///   "createdAt": "2025-08-06T10:30:00Z",
    ///   "status": "ACTIVE",
    ///   "apiEndpoint": "https://gateway.hackload.com/api/v1",
    ///   "details": {
    ///     "teamName": "My Online Store",
    ///     "email": "merchant@mystore.com",
    ///     "supportedCurrencies": ["RUB", "USD", "EUR"],
    ///     "nextSteps": [
    ///       "Test payment initialization using your credentials",
    ///       "Configure webhook endpoint for notifications",
    ///       "Review API documentation for integration"
    ///     ]
    ///   }
    /// }
    /// ```
    /// 
    /// ### Error Codes:
    /// - **2001**: Invalid request data
    /// - **2002**: Team slug already exists
    /// - **2003**: Email already registered
    /// - **2004**: Terms of service not accepted
    /// - **2429**: Registration rate limit exceeded
    /// - **9999**: Internal server error
    /// </remarks>
    [HttpPost("register")]
    [ProducesResponseType(typeof(TeamRegistrationResponseDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(TeamRegistrationResponseDto), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(TeamRegistrationResponseDto), StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType(typeof(TeamRegistrationResponseDto), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<TeamRegistrationResponseDto>> RegisterTeam(
        [FromBody] TeamRegistrationRequestDto request,
        CancellationToken cancellationToken = default)
    {
        using var activity = TeamRegistrationDuration.NewTimer();
        var requestId = Guid.NewGuid().ToString();
        var startTime = DateTime.UtcNow;

        _logger.LogInformation("Team registration request received. RequestId: {RequestId}, TeamSlug: {TeamSlug}, Email: {Email}",
            requestId, request?.TeamSlug, request?.Email);

        try
        {
            // 1. Validate request model
            if (request == null)
            {
                TeamRegistrationRequests.WithLabels("null_request").Inc();
                _logger.LogWarning("Team registration request is null. RequestId: {RequestId}", requestId);
                return BadRequest(CreateErrorResponse("2001", "Invalid request", "Request body is required"));
            }

            // 2. Validate model state (data annotations)
            if (!ModelState.IsValid)
            {
                TeamRegistrationRequests.WithLabels("validation_failed").Inc();
                var errors = ModelState
                    .SelectMany(x => x.Value.Errors)
                    .Select(x => x.ErrorMessage)
                    .ToList();
                var errorMessage = string.Join("; ", errors);

                _logger.LogWarning("Team registration validation failed. RequestId: {RequestId}, Errors: {Errors}",
                    requestId, errorMessage);

                return BadRequest(CreateErrorResponse("2001", "Validation failed", errorMessage));
            }

            // 3. Additional business validation
            var businessValidation = await ValidateBusinessRulesAsync(request, cancellationToken);
            if (!businessValidation.IsValid)
            {
                TeamRegistrationRequests.WithLabels("business_validation_failed").Inc();
                _logger.LogWarning("Team registration business validation failed. RequestId: {RequestId}, Errors: {Errors}",
                    requestId, string.Join("; ", businessValidation.Errors));

                return BadRequest(CreateErrorResponse("2001", "Business validation failed", 
                    string.Join("; ", businessValidation.Errors)));
            }

            // 4. Check terms acceptance
            if (!request.AcceptTerms)
            {
                TeamRegistrationRequests.WithLabels("terms_not_accepted").Inc();
                _logger.LogWarning("Team registration failed - terms not accepted. RequestId: {RequestId}", requestId);
                return BadRequest(CreateErrorResponse("2004", "Terms not accepted", 
                    "You must accept the terms of service to register"));
            }

            // 5. Register team using service
            var response = await _teamRegistrationService.RegisterTeamAsync(request, cancellationToken);

            var processingDuration = DateTime.UtcNow - startTime;

            if (response.Success)
            {
                TeamRegistrationRequests.WithLabels("success").Inc();
                _logger.LogInformation("Team registration successful. RequestId: {RequestId}, TeamSlug: {TeamSlug}, TeamId: {TeamId}, Duration: {Duration}ms",
                    requestId, response.TeamSlug, response.TeamId, processingDuration.TotalMilliseconds);

                return CreatedAtAction(nameof(GetTeamStatus), new { teamSlug = response.TeamSlug }, response);
            }
            else
            {
                TeamRegistrationRequests.WithLabels("service_failed").Inc();
                _logger.LogWarning("Team registration failed. RequestId: {RequestId}, ErrorCode: {ErrorCode}, Message: {Message}, Duration: {Duration}ms",
                    requestId, response.ErrorCode, response.Message, processingDuration.TotalMilliseconds);

                // Return appropriate HTTP status based on error code
                var statusCode = GetHttpStatusCodeFromErrorCode(response.ErrorCode);
                return StatusCode(statusCode, response);
            }
        }
        catch (ValidationException ex)
        {
            TeamRegistrationRequests.WithLabels("validation_exception").Inc();
            _logger.LogError(ex, "Validation error during team registration. RequestId: {RequestId}", requestId);
            return BadRequest(CreateErrorResponse("2001", "Validation error", ex.Message));
        }
        catch (Exception ex)
        {
            TeamRegistrationRequests.WithLabels("internal_exception").Inc();
            _logger.LogError(ex, "Unexpected error during team registration. RequestId: {RequestId}", requestId);
            return StatusCode(StatusCodes.Status500InternalServerError,
                CreateErrorResponse("9999", "Internal error", "An unexpected error occurred"));
        }
    }

    /// <summary>
    /// Get team registration status and details
    /// </summary>
    /// <param name="teamSlug">Team slug identifier</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Team status and details</returns>
    [HttpGet("status/{teamSlug}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult> GetTeamStatus(
        [FromRoute] string teamSlug,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Getting team status for TeamSlug: {TeamSlug}", teamSlug);

            var team = await _teamRegistrationService.GetTeamStatusAsync(teamSlug, cancellationToken);
            if (team == null)
            {
                return NotFound(CreateErrorResponse("2404", "Team not found",
                    $"Team with slug '{teamSlug}' not found"));
            }

            return Ok(new
            {
                TeamSlug = team.TeamSlug,
                TeamName = team.TeamName,
                Status = team.IsActive ? "ACTIVE" : "INACTIVE",
                CreatedAt = team.CreatedAt,
                Email = team.ContactEmail,
                SupportedCurrencies = team.SupportedCurrencies
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting team status for TeamSlug: {TeamSlug}", teamSlug);
            return StatusCode(StatusCodes.Status500InternalServerError,
                CreateErrorResponse("9999", "Internal error", "An error occurred while retrieving team status"));
        }
    }

    /// <summary>
    /// Check if team slug is available for registration
    /// </summary>
    /// <param name="teamSlug">Team slug to check</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Availability status</returns>
    [HttpGet("check-availability/{teamSlug}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult> CheckTeamSlugAvailability(
        [FromRoute] string teamSlug,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Checking team slug availability: {TeamSlug}", teamSlug);

            // Validate slug format
            if (string.IsNullOrWhiteSpace(teamSlug) || teamSlug.Length < 3 || teamSlug.Length > 50)
            {
                return BadRequest(new { Available = false, Reason = "Invalid slug format" });
            }

            if (!System.Text.RegularExpressions.Regex.IsMatch(teamSlug, @"^[a-zA-Z0-9\-_]+$"))
            {
                return BadRequest(new { Available = false, Reason = "Slug can only contain alphanumeric characters, hyphens, and underscores" });
            }

            var isAvailable = await _teamRegistrationService.IsTeamSlugAvailableAsync(teamSlug, cancellationToken);

            return Ok(new
            {
                TeamSlug = teamSlug,
                Available = isAvailable,
                Reason = isAvailable ? "Available" : "Already taken"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking team slug availability for: {TeamSlug}", teamSlug);
            return StatusCode(StatusCodes.Status500InternalServerError,
                CreateErrorResponse("9999", "Internal error", "An error occurred while checking availability"));
        }
    }

    private async Task<BusinessValidationResult> ValidateBusinessRulesAsync(TeamRegistrationRequestDto request, CancellationToken cancellationToken)
    {
        var errors = new List<string>();
        var warnings = new List<string>();

        // Team slug format validation (additional to regex)
        if (request.TeamSlug.StartsWith('-') || request.TeamSlug.EndsWith('-') ||
            request.TeamSlug.StartsWith('_') || request.TeamSlug.EndsWith('_'))
        {
            errors.Add("TeamSlug cannot start or end with hyphens or underscores");
        }

        if (request.TeamSlug.Contains("--") || request.TeamSlug.Contains("__"))
        {
            errors.Add("TeamSlug cannot contain consecutive hyphens or underscores");
        }

        // Reserved slugs
        var reservedSlugs = new[] { "admin", "api", "www", "mail", "ftp", "test", "staging", "production", "dev", "demo" };
        if (reservedSlugs.Contains(request.TeamSlug.ToLower()))
        {
            errors.Add("TeamSlug is reserved and cannot be used");
        }

        // Password strength validation
        if (!HasStrongPassword(request.Password))
        {
            errors.Add("Password must contain at least one uppercase letter, one lowercase letter, one number, and one special character");
        }

        // Currency validation
        var supportedCurrencies = request.SupportedCurrencies.Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(c => c.Trim().ToUpper()).ToArray();
        var allowedCurrencies = new[] { "KZT", "USD", "EUR", "BYN", "RUB" };
        
        foreach (var currency in supportedCurrencies)
        {
            if (!allowedCurrencies.Contains(currency))
            {
                errors.Add($"Currency '{currency}' is not supported. Allowed currencies: {string.Join(", ", allowedCurrencies)}");
            }
        }

        // URL validation (additional to Url attribute)
        var urls = new[] { request.SuccessURL, request.FailURL, request.NotificationURL };
        foreach (var url in urls.Where(u => !string.IsNullOrEmpty(u)))
        {
            if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
            {
                if (uri.Scheme != "https" && uri.Scheme != "http")
                {
                    warnings.Add($"URL '{url}' should use HTTPS for security");
                }
                if (uri.Host == "localhost" || uri.Host == "127.0.0.1")
                {
                    warnings.Add($"URL '{url}' uses localhost which may not be accessible for webhooks");
                }
            }
        }

        return new BusinessValidationResult
        {
            IsValid = errors.Count == 0,
            Errors = errors,
            Warnings = warnings
        };
    }

    private static bool HasStrongPassword(string password)
    {
        if (string.IsNullOrEmpty(password) || password.Length < 8)
            return false;

        bool hasUpper = password.Any(char.IsUpper);
        bool hasLower = password.Any(char.IsLower);
        bool hasDigit = password.Any(char.IsDigit);
        bool hasSpecial = password.Any(c => !char.IsLetterOrDigit(c));

        return hasUpper && hasLower && hasDigit && hasSpecial;
    }

    private TeamRegistrationResponseDto CreateErrorResponse(string errorCode, string message, string details)
    {
        return new TeamRegistrationResponseDto
        {
            Success = false,
            ErrorCode = errorCode,
            Message = message,
            Details = new TeamRegistrationDetailsDto
            {
                NextSteps = new[] { details }
            }
        };
    }

    private int GetHttpStatusCodeFromErrorCode(string errorCode)
    {
        return errorCode switch
        {
            "2001" => StatusCodes.Status400BadRequest,
            "2002" or "2003" => StatusCodes.Status409Conflict,
            "2004" => StatusCodes.Status400BadRequest,
            "2404" => StatusCodes.Status404NotFound,
            "2429" => StatusCodes.Status429TooManyRequests,
            "9999" => StatusCodes.Status500InternalServerError,
            _ => StatusCodes.Status400BadRequest
        };
    }
}

/// <summary>
/// Business validation result for team registration
/// </summary>
public class BusinessValidationResult
{
    public bool IsValid { get; set; }
    public List<string> Errors { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
}