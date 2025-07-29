using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using PaymentGateway.Core.DTOs.PaymentInit;
using PaymentGateway.Core.Services;
using PaymentGateway.Core.Validation.Simplified;
using System.ComponentModel.DataAnnotations;

namespace PaymentGateway.API.Controllers;

/// <summary>
/// Payment initialization API controller
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
[Tags("Payment Initialization")]
public class PaymentInitController : ControllerBase
{
    private readonly IPaymentInitializationService _paymentInitializationService;
    private readonly IValidationFramework _validationFramework;
    private readonly ILogger<PaymentInitController> _logger;

    public PaymentInitController(
        IPaymentInitializationService paymentInitializationService,
        IValidationFramework validationFramework,
        ILogger<PaymentInitController> logger)
    {
        _paymentInitializationService = paymentInitializationService;
        _validationFramework = validationFramework;
        _logger = logger;
    }

    /// <summary>
    /// Initialize a new payment
    /// </summary>
    /// <param name="request">Payment initialization request</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Payment initialization response</returns>
    /// <response code="200">Payment initialized successfully</response>
    /// <response code="400">Invalid request parameters</response>
    /// <response code="401">Authentication failed</response>
    /// <response code="403">Authorization failed</response>
    /// <response code="429">Rate limit exceeded</response>
    /// <response code="500">Internal server error</response>
    [HttpPost("init")]
    [ProducesResponseType(typeof(PaymentInitResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(PaymentInitResponseDto), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType(typeof(PaymentInitResponseDto), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<PaymentInitResponseDto>> InitializePayment(
        [FromBody] PaymentInitRequestDto request,
        CancellationToken cancellationToken = default)
    {
        var requestId = Guid.NewGuid().ToString();
        
        _logger.LogInformation("Payment initialization request received. RequestId: {RequestId}, OrderId: {OrderId}, TeamSlug: {TeamSlug}", 
            requestId, request?.OrderId, request?.TeamSlug);

        try
        {
            // 1. Validate request model
            if (request == null)
            {
                _logger.LogWarning("Payment initialization request is null. RequestId: {RequestId}", requestId);
                return BadRequest(CreateErrorResponse("1000", "Invalid request", "Request body is required"));
            }

            // 2. Basic validation using validation framework
            if (!_validationFramework.IsValid(request))
            {
                var validationErrors = _validationFramework.GetValidationErrors(request);
                var errorMessage = string.Join("; ", validationErrors);
                
                _logger.LogWarning("Payment initialization request validation failed. RequestId: {RequestId}, Errors: {Errors}", 
                    requestId, errorMessage);
                
                return BadRequest(CreateErrorResponse("1100", "Validation failed", errorMessage));
            }

            // 3. Authenticate request (TeamSlug and Token validation)
            var authResult = await AuthenticateRequestAsync(request, cancellationToken);
            if (!authResult.IsAuthenticated)
            {
                _logger.LogWarning("Payment initialization authentication failed. RequestId: {RequestId}, TeamSlug: {TeamSlug}, Reason: {Reason}", 
                    requestId, request.TeamSlug, authResult.FailureReason);
                
                return Unauthorized(CreateErrorResponse("1001", "Authentication failed", authResult.FailureReason));
            }

            // 4. Rate limiting check
            var rateLimitResult = await CheckRateLimitAsync(request.TeamSlug, cancellationToken);
            if (!rateLimitResult.IsAllowed)
            {
                _logger.LogWarning("Payment initialization rate limit exceeded. RequestId: {RequestId}, TeamSlug: {TeamSlug}", 
                    requestId, request.TeamSlug);
                
                return StatusCode(StatusCodes.Status429TooManyRequests, 
                    CreateErrorResponse("1429", "Rate limit exceeded", "Too many requests. Please try again later."));
            }

            // 5. Initialize payment using the service
            var response = await _paymentInitializationService.InitializePaymentAsync(request, cancellationToken);

            // 6. Log result and return response
            if (response.Success)
            {
                _logger.LogInformation("Payment initialization successful. RequestId: {RequestId}, PaymentId: {PaymentId}, OrderId: {OrderId}", 
                    requestId, response.PaymentId, response.OrderId);
                
                return Ok(response);
            }
            else
            {
                _logger.LogWarning("Payment initialization failed. RequestId: {RequestId}, ErrorCode: {ErrorCode}, Message: {Message}", 
                    requestId, response.ErrorCode, response.Message);
                
                // Return appropriate HTTP status based on error code
                var statusCode = GetHttpStatusCodeFromErrorCode(response.ErrorCode);
                return StatusCode(statusCode, response);
            }
        }
        catch (ValidationException ex)
        {
            _logger.LogError(ex, "Validation error during payment initialization. RequestId: {RequestId}", requestId);
            return BadRequest(CreateErrorResponse("1100", "Validation error", ex.Message));
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogError(ex, "Authorization error during payment initialization. RequestId: {RequestId}", requestId);
            return Unauthorized(CreateErrorResponse("1001", "Authorization error", ex.Message));
        }
        catch (TimeoutException ex)
        {
            _logger.LogError(ex, "Timeout error during payment initialization. RequestId: {RequestId}", requestId);
            return StatusCode(StatusCodes.Status408RequestTimeout, 
                CreateErrorResponse("1408", "Request timeout", "The request timed out. Please try again."));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error during payment initialization. RequestId: {RequestId}", requestId);
            return StatusCode(StatusCodes.Status500InternalServerError, 
                CreateErrorResponse("9999", "Internal error", "An unexpected error occurred"));
        }
    }

    /// <summary>
    /// Check payment initialization status
    /// </summary>
    /// <param name="paymentId">Payment identifier</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Payment session information</returns>
    [HttpGet("session/{paymentId}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult> GetPaymentSession(
        [FromRoute] string paymentId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Getting payment session for PaymentId: {PaymentId}", paymentId);
            
            var session = await _paymentInitializationService.GetPaymentSessionAsync(paymentId, cancellationToken);
            if (session == null)
            {
                return NotFound(CreateErrorResponse("1404", "Payment not found", 
                    $"Payment session for PaymentId '{paymentId}' not found"));
            }

            return Ok(new
            {
                PaymentId = session.PaymentId,
                CreatedAt = session.CreatedAt,
                ExpiresAt = session.ExpiresAt,
                IsActive = session.IsActive
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting payment session for PaymentId: {PaymentId}", paymentId);
            return StatusCode(StatusCodes.Status500InternalServerError, 
                CreateErrorResponse("9999", "Internal error", "An error occurred while retrieving payment session"));
        }
    }

    /// <summary>
    /// Get payment initialization metrics for a team
    /// </summary>
    /// <param name="teamSlug">Team slug</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Payment initialization metrics</returns>
    [HttpGet("metrics/{teamSlug}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult> GetInitializationMetrics(
        [FromRoute] string teamSlug,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Getting initialization metrics for TeamSlug: {TeamSlug}", teamSlug);
            
            var metrics = await _paymentInitializationService.GetInitializationMetricsAsync(teamSlug, cancellationToken);
            
            return Ok(metrics);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting initialization metrics for TeamSlug: {TeamSlug}", teamSlug);
            return StatusCode(StatusCodes.Status500InternalServerError, 
                CreateErrorResponse("9999", "Internal error", "An error occurred while retrieving metrics"));
        }
    }

    private async Task<AuthenticationResult> AuthenticateRequestAsync(PaymentInitRequestDto request, CancellationToken cancellationToken)
    {
        try
        {
            // In a real implementation, this would:
            // 1. Validate the Token using SHA-256 signature verification
            // 2. Check that the TeamSlug exists and is active
            // 3. Verify that the token was generated using the team's secret key
            // 4. Check token replay protection (timestamp, nonce)
            
            // For now, simplified validation
            if (string.IsNullOrEmpty(request.TeamSlug))
            {
                return new AuthenticationResult
                {
                    IsAuthenticated = false,
                    FailureReason = "TeamSlug is required"
                };
            }

            if (string.IsNullOrEmpty(request.Token))
            {
                return new AuthenticationResult
                {
                    IsAuthenticated = false,
                    FailureReason = "Token is required"
                };
            }

            // Simulate token validation
            await Task.Delay(10, cancellationToken); // Simulate async validation
            
            return new AuthenticationResult
            {
                IsAuthenticated = true,
                TeamSlug = request.TeamSlug
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during authentication for TeamSlug: {TeamSlug}", request.TeamSlug);
            return new AuthenticationResult
            {
                IsAuthenticated = false,
                FailureReason = "Authentication service error"
            };
        }
    }

    private async Task<RateLimitResult> CheckRateLimitAsync(string teamSlug, CancellationToken cancellationToken)
    {
        try
        {
            // In a real implementation, this would check rate limits using:
            // 1. Redis or in-memory cache for rate limit tracking
            // 2. Sliding window or token bucket algorithm
            // 3. Team-specific rate limit configuration
            
            // For now, always allow (simplified)
            await Task.CompletedTask;
            
            return new RateLimitResult
            {
                IsAllowed = true,
                RemainingRequests = 100,
                ResetTime = DateTime.UtcNow.AddHours(1)
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking rate limit for TeamSlug: {TeamSlug}", teamSlug);
            return new RateLimitResult
            {
                IsAllowed = false,
                RemainingRequests = 0,
                ResetTime = DateTime.UtcNow.AddHours(1)
            };
        }
    }

    private PaymentInitResponseDto CreateErrorResponse(string errorCode, string message, string details)
    {
        return new PaymentInitResponseDto
        {
            TeamSlug = string.Empty,
            Amount = 0,
            OrderId = string.Empty,
            Success = false,
            Status = "ERROR",
            PaymentId = string.Empty,
            ErrorCode = errorCode,
            PaymentURL = null,
            Message = message,
            Details = details
        };
    }

    private int GetHttpStatusCodeFromErrorCode(string errorCode)
    {
        return errorCode switch
        {
            "1001" => StatusCodes.Status401Unauthorized,
            "1002" => StatusCodes.Status409Conflict,
            "1003" => StatusCodes.Status400BadRequest,
            "1004" or "1005" or "1006" => StatusCodes.Status400BadRequest,
            "1007" => StatusCodes.Status402PaymentRequired,
            "1100" => StatusCodes.Status400BadRequest,
            "1404" => StatusCodes.Status404NotFound,
            "1408" => StatusCodes.Status408RequestTimeout,
            "1429" => StatusCodes.Status429TooManyRequests,
            "9998" or "9999" => StatusCodes.Status500InternalServerError,
            _ => StatusCodes.Status400BadRequest
        };
    }
}

// Supporting classes for API controller
public class AuthenticationResult
{
    public bool IsAuthenticated { get; set; }
    public string? TeamSlug { get; set; }
    public string? FailureReason { get; set; }
}

public class RateLimitResult
{
    public bool IsAllowed { get; set; }
    public int RemainingRequests { get; set; }
    public DateTime ResetTime { get; set; }
}