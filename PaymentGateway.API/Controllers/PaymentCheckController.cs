// SPDX-License-Identifier: MIT
// Copyright (c) 2025 HackLoad Payment Gateway

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using PaymentGateway.Core.DTOs.PaymentCheck;
using PaymentGateway.Core.Services;
using PaymentGateway.API.Middleware;
using Microsoft.Extensions.Caching.Memory;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using Prometheus;

namespace PaymentGateway.API.Controllers;

/// <summary>
/// Payment status checking API controller with comprehensive validation, caching, and monitoring
/// 
/// This endpoint provides detailed payment status information with support for multiple lookup methods,
/// optional additional data inclusion, and comprehensive caching for performance optimization.
/// 
/// ## Features:
/// - Payment lookup by PaymentId or OrderId
/// - Optional inclusion of customer info, card details, transactions, and receipts
/// - Intelligent caching for frequently accessed payments
/// - Rate limiting protection per team and endpoint
/// - Comprehensive error handling with specific error codes
/// - Performance monitoring and metrics collection
/// - Support for multiple payments per OrderId
/// 
/// ## Authentication:
/// Requires valid TeamSlug and Token for authentication. Uses the same SHA-256 HMAC authentication
/// as other endpoints with timestamp-based replay protection.
/// 
/// ## Rate Limiting:
/// - 1000 requests per minute per team for status checking
/// - Additional rate limiting based on specific PaymentId/OrderId combinations
/// 
/// ## Caching Strategy:
/// - Active payments cached for 30 seconds
/// - Final status payments (CONFIRMED, CANCELLED, REFUNDED, FAILED) cached for 5 minutes
/// - Cache invalidation on payment status changes
/// </summary>
[ApiController]
[Route("api/v1/[controller]")]
[Produces("application/json")]
[Tags("Payment Status Check")]
[ServiceFilter(typeof(PaymentAuthenticationMiddleware))]
[ServiceFilter(typeof(AuthenticationRateLimitingMiddleware))]
public class PaymentCheckController : ControllerBase
{
    private readonly IPaymentStatusCheckService _statusCheckService;
    private readonly IPaymentAuthenticationService _authenticationService;
    private readonly IMemoryCache _cache;
    private readonly ILogger<PaymentCheckController> _logger;

    // Metrics for monitoring
    private static readonly Counter PaymentCheckRequests = Metrics
        .CreateCounter("payment_check_requests_total", "Total payment status check requests", new[] { "team_id", "result", "lookup_type" });

    private static readonly Histogram PaymentCheckDuration = Metrics
        .CreateHistogram("payment_check_duration_seconds", "Payment status check request duration");

    private static readonly Counter PaymentCheckCacheHits = Metrics
        .CreateCounter("payment_check_cache_hits_total", "Payment status check cache hits", new[] { "team_id", "cache_type" });

    private static readonly Counter PaymentCheckCacheMisses = Metrics
        .CreateCounter("payment_check_cache_misses_total", "Payment status check cache misses", new[] { "team_id", "lookup_type" });

    private static readonly Gauge ActivePaymentChecks = Metrics
        .CreateGauge("active_payment_checks_total", "Total active payment status checks", new[] { "team_id" });

    // Cache configuration
    private static readonly TimeSpan ActivePaymentCacheDuration = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan FinalStatusCacheDuration = TimeSpan.FromMinutes(5);
    private static readonly HashSet<string> FinalStatuses = new() { "CONFIRMED", "CANCELLED", "REFUNDED", "FAILED", "REJECTED" };

    public PaymentCheckController(
        IPaymentStatusCheckService statusCheckService,
        IPaymentAuthenticationService authenticationService,
        IMemoryCache cache,
        ILogger<PaymentCheckController> logger)
    {
        _statusCheckService = statusCheckService;
        _authenticationService = authenticationService;
        _cache = cache;
        _logger = logger;
    }

    /// <summary>
    /// Check payment status by PaymentId or OrderId with optional additional information
    /// 
    /// This endpoint provides comprehensive payment status information with support for multiple
    /// lookup methods and optional inclusion of detailed payment data.
    /// 
    /// ## Lookup Methods:
    /// - **By PaymentId**: Returns specific payment information
    /// - **By OrderId**: Returns all payments associated with the order
    /// 
    /// ## Optional Information:
    /// - **Customer Information**: Basic customer data (email, phone, etc.)
    /// - **Card Details**: Masked card information for card payments
    /// - **Transaction History**: Complete transaction log with bank responses
    /// - **Receipt Information**: Receipt data and download URLs
    /// 
    /// ## Performance Features:
    /// - Intelligent caching based on payment status
    /// - Rate limiting to prevent abuse
    /// - Efficient database queries with minimal data transfer
    /// </summary>
    /// <param name="request">Payment status check request with lookup criteria and options</param>
    /// <param name="cancellationToken">Cancellation token for request timeout handling</param>
    /// <returns>Payment status check response with payment information</returns>
    /// <response code="200">Payment status retrieved successfully</response>
    /// <response code="400">Invalid request parameters or validation failed</response>
    /// <response code="401">Authentication failed</response>
    /// <response code="403">Authorization failed</response>
    /// <response code="404">Payment not found</response>
    /// <response code="429">Rate limit exceeded</response>
    /// <response code="500">Internal server error</response>
    /// <remarks>
    /// ### Example Request (by PaymentId):
    /// ```json
    /// {
    ///   "teamSlug": "my-store",
    ///   "token": "eyJ0eXAiOiJKV1QiLCJhbGciOiJIUzI1NiJ9...",
    ///   "paymentId": "pay_123456789",
    ///   "includeTransactions": true,
    ///   "includeCardDetails": true,
    ///   "includeCustomerInfo": false,
    ///   "includeReceipt": false,
    ///   "language": "ru"
    /// }
    /// ```
    /// 
    /// ### Example Request (by OrderId):
    /// ```json
    /// {
    ///   "teamSlug": "my-store", 
    ///   "token": "eyJ0eXAiOiJKV1QiLCJhbGciOiJIUzI1NiJ9...",
    ///   "orderId": "order-12345",
    ///   "includeTransactions": false,
    ///   "includeCardDetails": false,
    ///   "includeCustomerInfo": true,
    ///   "includeReceipt": true,
    ///   "language": "en"
    /// }
    /// ```
    /// 
    /// ### Example Response:
    /// ```json
    /// {
    ///   "success": true,
    ///   "payments": [
    ///     {
    ///       "paymentId": "pay_123456789",
    ///       "orderId": "order-12345",
    ///       "status": "CONFIRMED",
    ///       "statusDescription": "Payment confirmed successfully",
    ///       "amount": 150000,
    ///       "currency": "RUB",
    ///       "createdAt": "2025-01-30T12:00:00Z",
    ///       "updatedAt": "2025-01-30T12:05:00Z",
    ///       "expiresAt": "2025-01-30T12:30:00Z",
    ///       "description": "Book purchase",
    ///       "payType": "O"
    ///     }
    ///   ],
    ///   "totalCount": 1,
    ///   "orderId": "order-12345"
    /// }
    /// ```
    /// 
    /// ### Error Codes:
    /// - **1000**: Invalid request body or missing required parameters
    /// - **1001**: Authentication failed
    /// - **1100**: Validation failed
    /// - **1404**: Payment not found
    /// - **1429**: Rate limit exceeded
    /// - **9999**: Internal server error
    /// </remarks>
    [HttpPost("check")]
    [ProducesResponseType(typeof(PaymentCheckResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(PaymentCheckResponseDto), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(PaymentCheckResponseDto), StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType(typeof(PaymentCheckResponseDto), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<PaymentCheckResponseDto>> CheckPaymentStatus(
        [FromBody] PaymentCheckRequestDto request,
        CancellationToken cancellationToken = default)
    {
        using var activity = PaymentCheckDuration.NewTimer();
        using var activitySource = new ActivitySource("PaymentGateway.API");
        using var traceActivity = activitySource.StartActivity("PaymentCheck.CheckPaymentStatus");

        var requestId = Guid.NewGuid().ToString();
        var startTime = DateTime.UtcNow;
        var teamId = HttpContext.Items["TeamId"] as int? ?? 0;
        var teamSlug = HttpContext.Items["TeamSlug"] as string ?? "";
        var lookupType = !string.IsNullOrEmpty(request?.PaymentId) ? "payment_id" : "order_id";

        traceActivity?.SetTag("payment_check.request_id", requestId);
        traceActivity?.SetTag("payment_check.team_id", teamId.ToString());
        traceActivity?.SetTag("payment_check.team_slug", teamSlug);
        traceActivity?.SetTag("payment_check.lookup_type", lookupType);

        _logger.LogInformation("Payment status check request received. RequestId: {RequestId}, TeamSlug: {TeamSlug}, PaymentId: {PaymentId}, OrderId: {OrderId}",
            requestId, teamSlug, request?.PaymentId, request?.OrderId);

        try
        {
            ActivePaymentChecks.WithLabels(teamId.ToString()).Inc();

            // 1. Validate request model
            if (request == null)
            {
                PaymentCheckRequests.WithLabels(teamId.ToString(), "null_request", lookupType).Inc();
                _logger.LogWarning("Payment status check request is null. RequestId: {RequestId}", requestId);
                return BadRequest(CreateErrorResponse("1000", "Invalid request", "Request body is required"));
            }

            traceActivity?.SetTag("payment_check.payment_id", request.PaymentId ?? "");
            traceActivity?.SetTag("payment_check.order_id", request.OrderId ?? "");

            // 2. Comprehensive validation
            var validationResult = await ValidatePaymentCheckRequestAsync(request, teamId, cancellationToken);
            if (!validationResult.IsValid)
            {
                PaymentCheckRequests.WithLabels(teamId.ToString(), "validation_failed", lookupType).Inc();
                var errorMessage = string.Join("; ", validationResult.Errors);

                _logger.LogWarning("Payment status check validation failed. RequestId: {RequestId}, Errors: {Errors}",
                    requestId, errorMessage);

                traceActivity?.SetTag("payment_check.validation_error", errorMessage);
                return BadRequest(CreateErrorResponse("1100", "Validation failed", errorMessage));
            }

            // 3. Check cache first
            var cacheKey = GenerateCacheKey(request, teamId);
            if (_cache.TryGetValue(cacheKey, out PaymentCheckResponseDto? cachedResponse))
            {
                PaymentCheckCacheHits.WithLabels(teamId.ToString(), "memory_cache").Inc();
                _logger.LogDebug("Payment status check cache hit. RequestId: {RequestId}, CacheKey: {CacheKey}",
                    requestId, cacheKey);

                traceActivity?.SetTag("payment_check.cache_hit", "true");
                return Ok(cachedResponse);
            }

            PaymentCheckCacheMisses.WithLabels(teamId.ToString(), lookupType).Inc();

            // 4. Enhanced authentication validation
            var authContext = new PaymentCheckAuthContext
            {
                TeamSlug = request.TeamSlug,
                Token = request.Token,
                RequestId = requestId,
                PaymentId = request.PaymentId,
                OrderId = request.OrderId,
                ClientIp = GetClientIpAddress(),
                UserAgent = Request.Headers.UserAgent.ToString(),
                Timestamp = DateTime.UtcNow
            };

            var authResult = await _authenticationService.AuthenticateAsync(authContext, cancellationToken);
            if (!authResult.IsAuthenticated)
            {
                PaymentCheckRequests.WithLabels(teamId.ToString(), "auth_failed", lookupType).Inc();
                _logger.LogWarning("Payment status check authentication failed. RequestId: {RequestId}, TeamSlug: {TeamSlug}, Reason: {Reason}",
                    requestId, request.TeamSlug, authResult.FailureReason);

                traceActivity?.SetTag("payment_check.auth_error", authResult.FailureReason ?? "");
                return Unauthorized(CreateErrorResponse("1001", "Authentication failed", authResult.FailureReason ?? "Authentication failed"));
            }

            // 5. Perform payment status check
            PaymentCheckResponseDto response;
            if (!string.IsNullOrEmpty(request.PaymentId))
            {
                response = await _statusCheckService.CheckPaymentByIdAsync(request.PaymentId, request, cancellationToken);
            }
            else if (!string.IsNullOrEmpty(request.OrderId))
            {
                response = await _statusCheckService.CheckPaymentsByOrderIdAsync(request.OrderId, request, cancellationToken);
            }
            else
            {
                // This should not happen due to validation, but safety check
                PaymentCheckRequests.WithLabels(teamId.ToString(), "no_identifier", lookupType).Inc();
                return BadRequest(CreateErrorResponse("1100", "Validation failed", "Either PaymentId or OrderId must be provided"));
            }

            var processingDuration = DateTime.UtcNow - startTime;

            // 6. Handle response and caching
            if (response.Success && response.Payments.Any())
            {
                // Cache successful responses
                var cacheDuration = DetermineCacheDuration(response.Payments);
                _cache.Set(cacheKey, response, cacheDuration);

                PaymentCheckRequests.WithLabels(teamId.ToString(), "success", lookupType).Inc();

                _logger.LogInformation("Payment status check successful. RequestId: {RequestId}, PaymentCount: {PaymentCount}, Duration: {Duration}ms",
                    requestId, response.TotalCount, processingDuration.TotalMilliseconds);

                traceActivity?.SetTag("payment_check.success", "true");
                traceActivity?.SetTag("payment_check.payment_count", response.TotalCount.ToString());
                traceActivity?.SetTag("payment_check.duration_ms", processingDuration.TotalMilliseconds.ToString());

                return Ok(response);
            }
            else if (response.Success && !response.Payments.Any())
            {
                PaymentCheckRequests.WithLabels(teamId.ToString(), "not_found", lookupType).Inc();

                _logger.LogInformation("Payment status check - no payments found. RequestId: {RequestId}, PaymentId: {PaymentId}, OrderId: {OrderId}",
                    requestId, request.PaymentId, request.OrderId);

                traceActivity?.SetTag("payment_check.found", "false");
                return NotFound(CreateErrorResponse("1404", "Payment not found", $"No payments found for the provided {lookupType}"));
            }
            else
            {
                PaymentCheckRequests.WithLabels(teamId.ToString(), "service_failed", lookupType).Inc();

                _logger.LogWarning("Payment status check service failed. RequestId: {RequestId}, ErrorCode: {ErrorCode}, Message: {Message}",
                    requestId, response.ErrorCode, response.Message);

                traceActivity?.SetTag("payment_check.success", "false");
                traceActivity?.SetTag("payment_check.error_code", response.ErrorCode ?? "");
                traceActivity?.SetTag("payment_check.error_message", response.Message ?? "");

                var statusCode = GetHttpStatusCodeFromErrorCode(response.ErrorCode);
                return StatusCode(statusCode, response);
            }
        }
        catch (ValidationException ex)
        {
            PaymentCheckRequests.WithLabels(teamId.ToString(), "validation_exception", lookupType).Inc();
            _logger.LogError(ex, "Validation error during payment status check. RequestId: {RequestId}", requestId);
            traceActivity?.SetTag("payment_check.error", "validation_exception");
            return BadRequest(CreateErrorResponse("1100", "Validation error", ex.Message));
        }
        catch (UnauthorizedAccessException ex)
        {
            PaymentCheckRequests.WithLabels(teamId.ToString(), "unauthorized_exception", lookupType).Inc();
            _logger.LogError(ex, "Authorization error during payment status check. RequestId: {RequestId}", requestId);
            traceActivity?.SetTag("payment_check.error", "unauthorized_exception");
            return Unauthorized(CreateErrorResponse("1001", "Authorization error", ex.Message));
        }
        catch (TimeoutException ex)
        {
            PaymentCheckRequests.WithLabels(teamId.ToString(), "timeout_exception", lookupType).Inc();
            _logger.LogError(ex, "Timeout error during payment status check. RequestId: {RequestId}", requestId);
            traceActivity?.SetTag("payment_check.error", "timeout_exception");
            return StatusCode(StatusCodes.Status408RequestTimeout,
                CreateErrorResponse("1408", "Request timeout", "The request timed out. Please try again."));
        }
        catch (ArgumentException ex)
        {
            PaymentCheckRequests.WithLabels(teamId.ToString(), "argument_exception", lookupType).Inc();
            _logger.LogError(ex, "Argument error during payment status check. RequestId: {RequestId}", requestId);
            traceActivity?.SetTag("payment_check.error", "argument_exception");
            return BadRequest(CreateErrorResponse("1003", "Invalid argument", ex.Message));
        }
        catch (Exception ex)
        {
            PaymentCheckRequests.WithLabels(teamId.ToString(), "internal_exception", lookupType).Inc();
            _logger.LogError(ex, "Unexpected error during payment status check. RequestId: {RequestId}", requestId);
            traceActivity?.SetTag("payment_check.error", "internal_exception");
            return StatusCode(StatusCodes.Status500InternalServerError,
                CreateErrorResponse("9999", "Internal error", "An unexpected error occurred"));
        }
        finally
        {
            ActivePaymentChecks.WithLabels(teamId.ToString()).Dec();
        }
    }

    /// <summary>
    /// Get payment status using GET method (simplified version)
    /// 
    /// This endpoint provides a simplified GET-based payment status check for easier integration
    /// with systems that prefer GET requests over POST.
    /// </summary>
    /// <param name="paymentId">Payment identifier to check</param>
    /// <param name="orderId">Order identifier to check (alternative to paymentId)</param>
    /// <param name="teamSlug">Team slug for authentication</param>
    /// <param name="token">Authentication token</param>
    /// <param name="language">Response language (ru or en)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Payment status information</returns>
    [HttpGet("status")]
    [ProducesResponseType(typeof(PaymentCheckResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(PaymentCheckResponseDto), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(PaymentCheckResponseDto), StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<PaymentCheckResponseDto>> GetPaymentStatus(
        [FromQuery] string? paymentId = null,
        [FromQuery] string? orderId = null,
        [FromQuery] string? teamSlug = null,
        [FromQuery] string? token = null,
        [FromQuery] string language = "ru",
        CancellationToken cancellationToken = default)
    {
        // Convert GET parameters to POST request model
        var request = new PaymentCheckRequestDto
        {
            PaymentId = paymentId,
            OrderId = orderId,
            TeamSlug = teamSlug ?? "",
            Token = token ?? "",
            Language = language,
            IncludeTransactions = false,
            IncludeCardDetails = false,
            IncludeCustomerInfo = false,
            IncludeReceipt = false
        };

        return await CheckPaymentStatus(request, cancellationToken);
    }

    private async Task<PaymentCheckValidationResult> ValidatePaymentCheckRequestAsync(PaymentCheckRequestDto request, int teamId, CancellationToken cancellationToken)
    {
        var errors = new List<string>();
        var warnings = new List<string>();

        // Validate that either PaymentId or OrderId is provided
        if (string.IsNullOrWhiteSpace(request.PaymentId) && string.IsNullOrWhiteSpace(request.OrderId))
        {
            errors.Add("Either PaymentId or OrderId must be provided");
        }

        // Validate both are not provided
        if (!string.IsNullOrWhiteSpace(request.PaymentId) && !string.IsNullOrWhiteSpace(request.OrderId))
        {
            warnings.Add("Both PaymentId and OrderId provided. PaymentId will take precedence.");
        }

        // PaymentId format validation
        if (!string.IsNullOrEmpty(request.PaymentId))
        {
            if (request.PaymentId.Length > 50)
                errors.Add("PaymentId cannot exceed 50 characters");
            if (!System.Text.RegularExpressions.Regex.IsMatch(request.PaymentId, @"^[a-zA-Z0-9\-_]+$"))
                errors.Add("PaymentId can only contain alphanumeric characters, hyphens, and underscores");
        }

        // OrderId format validation
        if (!string.IsNullOrEmpty(request.OrderId))
        {
            if (request.OrderId.Length > 36)
                errors.Add("OrderId cannot exceed 36 characters");
            if (!System.Text.RegularExpressions.Regex.IsMatch(request.OrderId, @"^[a-zA-Z0-9\-_]+$"))
                errors.Add("OrderId can only contain alphanumeric characters, hyphens, and underscores");
        }

        // Language validation
        if (!string.IsNullOrEmpty(request.Language))
        {
            var allowedLanguages = new[] { "ru", "en" };
            if (!allowedLanguages.Contains(request.Language.ToLower()))
                errors.Add("Language must be 'ru' or 'en'");
        }

        // Team access validation (would be enhanced with actual team validation)
        if (string.IsNullOrWhiteSpace(request.TeamSlug))
        {
            errors.Add("TeamSlug is required for authentication");
        }

        if (string.IsNullOrWhiteSpace(request.Token))
        {
            errors.Add("Token is required for authentication");
        }

        return new PaymentCheckValidationResult
        {
            IsValid = errors.Count == 0,
            Errors = errors,
            Warnings = warnings
        };
    }

    private string GenerateCacheKey(PaymentCheckRequestDto request, int teamId)
    {
        var keyBuilder = new System.Text.StringBuilder();
        keyBuilder.Append($"payment_check:{teamId}:");

        if (!string.IsNullOrEmpty(request.PaymentId))
        {
            keyBuilder.Append($"pid:{request.PaymentId}");
        }
        else if (!string.IsNullOrEmpty(request.OrderId))
        {
            keyBuilder.Append($"oid:{request.OrderId}");
        }

        // Include optional data flags in cache key
        keyBuilder.Append($":inc:{request.IncludeTransactions}:{request.IncludeCardDetails}:{request.IncludeCustomerInfo}:{request.IncludeReceipt}");
        keyBuilder.Append($":lang:{request.Language}");

        return keyBuilder.ToString();
    }

    private TimeSpan DetermineCacheDuration(List<PaymentStatusDto> payments)
    {
        // Use longer cache duration for final status payments
        var hasFinalStatus = payments.Any(p => FinalStatuses.Contains(p.Status?.ToUpper() ?? ""));
        return hasFinalStatus ? FinalStatusCacheDuration : ActivePaymentCacheDuration;
    }

    private PaymentCheckResponseDto CreateErrorResponse(string errorCode, string message, string details)
    {
        return new PaymentCheckResponseDto
        {
            Success = false,
            ErrorCode = errorCode,
            Message = message,
            Details = details,
            Payments = new List<PaymentStatusDto>(),
            TotalCount = 0
        };
    }

    private int GetHttpStatusCodeFromErrorCode(string? errorCode)
    {
        return errorCode switch
        {
            "1001" => StatusCodes.Status401Unauthorized,
            "1003" => StatusCodes.Status400BadRequest,
            "1100" => StatusCodes.Status400BadRequest,
            "1404" => StatusCodes.Status404NotFound,
            "1408" => StatusCodes.Status408RequestTimeout,
            "1429" => StatusCodes.Status429TooManyRequests,
            "9999" => StatusCodes.Status500InternalServerError,
            _ => StatusCodes.Status400BadRequest
        };
    }

    private string GetClientIpAddress()
    {
        var forwardedFor = Request.Headers["X-Forwarded-For"].FirstOrDefault();
        if (!string.IsNullOrEmpty(forwardedFor))
        {
            return forwardedFor.Split(',')[0].Trim();
        }

        var realIp = Request.Headers["X-Real-IP"].FirstOrDefault();
        if (!string.IsNullOrEmpty(realIp))
        {
            return realIp;
        }

        return HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    }
}

// Supporting classes for enhanced controller functionality

public class PaymentCheckValidationResult
{
    public bool IsValid { get; set; }
    public List<string> Errors { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
}

public class PaymentCheckAuthContext
{
    public string TeamSlug { get; set; } = string.Empty;
    public string Token { get; set; } = string.Empty;
    public string RequestId { get; set; } = string.Empty;
    public string? PaymentId { get; set; }
    public string? OrderId { get; set; }
    public string ClientIp { get; set; } = string.Empty;
    public string UserAgent { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
}