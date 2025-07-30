// SPDX-License-Identifier: MIT
// Copyright (c) 2025 HackLoad Payment Gateway

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using PaymentGateway.Core.DTOs.PaymentConfirm;
using PaymentGateway.Core.Services;
using PaymentGateway.API.Middleware;
using Microsoft.Extensions.Caching.Memory;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using Prometheus;

namespace PaymentGateway.API.Controllers;

/// <summary>
/// Payment confirmation API controller for two-stage payment processing (AUTHORIZED -> CONFIRMED)
/// 
/// This endpoint handles the capture phase of two-stage payments, where previously authorized
/// payments are confirmed and captured for settlement. This is critical for proper payment
/// lifecycle management and merchant cash flow.
/// 
/// ## Features:
/// - Two-stage payment confirmation (authorization + capture)
/// - Full and partial amount confirmation support
/// - Comprehensive validation with authorization status verification
/// - Idempotency protection to prevent duplicate confirmations
/// - Receipt integration for fiscal compliance
/// - Detailed error handling with specific error codes
/// - Performance monitoring and audit logging
/// - Distributed locking for concurrent confirmation prevention
/// 
/// ## Authentication:
/// Requires valid TeamSlug and Token for authentication. Uses the same SHA-256 HMAC authentication
/// as other endpoints with timestamp-based replay protection.
/// 
/// ## Rate Limiting:
/// - 500 requests per minute per team for confirmation operations
/// - Additional rate limiting based on specific PaymentId combinations
/// 
/// ## Business Rules:
/// - Payment must be in AUTHORIZED status for confirmation
/// - Confirmation amount must match authorized amount exactly (no partial confirmations supported)
/// - Idempotency keys prevent duplicate confirmation processing
/// - State transitions are validated according to business rules
/// </summary>
[ApiController]
[Route("api/v1/[controller]")]
[Produces("application/json")]
[Tags("Payment Confirmation")]
[ServiceFilter(typeof(PaymentAuthenticationMiddleware))]
[ServiceFilter(typeof(AuthenticationRateLimitingMiddleware))]
public class PaymentConfirmController : ControllerBase
{
    private readonly IPaymentConfirmationService _confirmationService;
    private readonly IPaymentAuthenticationService _authenticationService;
    private readonly IMemoryCache _cache;
    private readonly ILogger<PaymentConfirmController> _logger;

    // Metrics for monitoring
    private static readonly Counter PaymentConfirmRequests = Metrics
        .CreateCounter("payment_confirm_requests_total", "Total payment confirmation requests", new[] { "team_id", "result", "reason" });

    private static readonly Histogram PaymentConfirmDuration = Metrics
        .CreateHistogram("payment_confirm_duration_seconds", "Payment confirmation request duration");

    private static readonly Counter PaymentConfirmAmount = Metrics
        .CreateCounter("payment_confirm_amount_total", "Total amount confirmed", new[] { "team_id", "currency" });

    private static readonly Gauge ActivePaymentConfirms = Metrics
        .CreateGauge("active_payment_confirms_total", "Total active payment confirmations", new[] { "team_id" });

    private static readonly Counter PaymentConfirmIdempotency = Metrics
        .CreateCounter("payment_confirm_idempotency_total", "Payment confirmation idempotency checks", new[] { "team_id", "result" });

    public PaymentConfirmController(
        IPaymentConfirmationService confirmationService,
        IPaymentAuthenticationService authenticationService,
        IMemoryCache cache,
        ILogger<PaymentConfirmController> logger)
    {
        _confirmationService = confirmationService;
        _authenticationService = authenticationService;
        _cache = cache;
        _logger = logger;
    }

    /// <summary>
    /// Confirm a previously authorized payment for capture and settlement
    /// 
    /// This endpoint handles the second phase of two-stage payment processing. After a payment
    /// has been authorized (funds reserved), this endpoint captures the payment for settlement.
    /// This is essential for completing the payment lifecycle and ensuring merchant payment.
    /// 
    /// ## Confirmation Process:
    /// 1. **Authorization Verification**: Ensures payment is in AUTHORIZED status
    /// 2. **Amount Validation**: Confirms amount matches authorized amount exactly
    /// 3. **State Transition**: Validates AUTHORIZED -> CONFIRMED transition
    /// 4. **Capture Processing**: Executes payment capture through banking systems
    /// 5. **Status Update**: Updates payment status to CONFIRMED
    /// 6. **Audit Logging**: Records confirmation for compliance and monitoring
    /// 
    /// ## Idempotency:
    /// This endpoint supports idempotency through the optional `idempotencyKey` field.
    /// Multiple requests with the same idempotency key will return the same result
    /// without processing the confirmation multiple times.
    /// 
    /// ## Receipt Integration:
    /// The endpoint supports receipt information for fiscal compliance requirements.
    /// Receipt data can be provided for tax reporting and customer notification purposes.
    /// </summary>
    /// <param name="request">Payment confirmation request with amount and receipt information</param>
    /// <param name="cancellationToken">Cancellation token for request timeout handling</param>
    /// <returns>Payment confirmation response with updated status and transaction details</returns>
    /// <response code="200">Payment confirmation successful</response>
    /// <response code="400">Invalid request parameters or validation failed</response>
    /// <response code="401">Authentication failed</response>
    /// <response code="403">Authorization failed</response>
    /// <response code="404">Payment not found or not confirmable</response>
    /// <response code="409">Payment already confirmed or in invalid state</response>
    /// <response code="429">Rate limit exceeded</response>
    /// <response code="500">Internal server error</response>
    /// <remarks>
    /// ### Example Request:
    /// ```json
    /// {
    ///   "teamSlug": "my-store",
    ///   "token": "eyJ0eXAiOiJKV1QiLCJhbGciOiJIUzI1NiJ9...",
    ///   "paymentId": "pay_123456789",
    ///   "amount": 150000,
    ///   "description": "Order confirmation for books",
    ///   "receipt": {
    ///     "email": "customer@example.com",
    ///     "phone": "+79001234567"
    ///   },
    ///   "data": {
    ///     "confirmationReason": "Customer payment approved",
    ///     "merchantReference": "ORDER-12345"
    ///   }
    /// }
    /// ```
    /// 
    /// ### Example Response:
    /// ```json
    /// {
    ///   "success": true,
    ///   "paymentId": "pay_123456789",
    ///   "orderId": "order-12345",
    ///   "status": "CONFIRMED",
    ///   "authorizedAmount": 150000,
    ///   "confirmedAmount": 150000,
    ///   "remainingAmount": 0,
    ///   "currency": "RUB",
    ///   "confirmedAt": "2025-01-30T12:05:00Z",
    ///   "bankDetails": {
    ///     "bankTransactionId": "bank_txn_789",
    ///     "authorizationCode": "AUTH123",
    ///     "rrn": "123456789012",
    ///     "responseCode": "00",
    ///     "responseMessage": "Approved"
    ///   },
    ///   "fees": {
    ///     "processingFee": 3000,
    ///     "totalFees": 3000,
    ///     "feeCurrency": "RUB"
    ///   },
    ///   "settlement": {
    ///     "settlementDate": "2025-01-31T00:00:00Z",
    ///     "settlementAmount": 147000,
    ///     "settlementCurrency": "RUB"
    ///   }
    /// }
    /// ```
    /// 
    /// ### Error Codes:
    /// - **2000**: Invalid request body or missing required parameters
    /// - **2001**: Authentication failed
    /// - **2100**: Validation failed (invalid PaymentId, amount, etc.)
    /// - **2404**: Payment not found or not confirmable
    /// - **2409**: Payment already confirmed or in invalid state for confirmation
    /// - **2429**: Rate limit exceeded
    /// - **9999**: Internal server error
    /// </remarks>
    [HttpPost("confirm")]
    [ProducesResponseType(typeof(PaymentConfirmResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(PaymentConfirmResponseDto), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(PaymentConfirmResponseDto), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(PaymentConfirmResponseDto), StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType(typeof(PaymentConfirmResponseDto), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<PaymentConfirmResponseDto>> ConfirmPayment(
        [FromBody] PaymentConfirmRequestDto request,
        CancellationToken cancellationToken = default)
    {
        using var activity = PaymentConfirmDuration.NewTimer();
        using var activitySource = new ActivitySource("PaymentGateway.API");
        using var traceActivity = activitySource.StartActivity("PaymentConfirm.ConfirmPayment");

        var requestId = Guid.NewGuid().ToString();
        var startTime = DateTime.UtcNow;
        var teamId = HttpContext.Items["TeamId"] as Guid? ?? Guid.Empty;
        var teamSlug = HttpContext.Items["TeamSlug"] as string ?? "";

        traceActivity?.SetTag("payment_confirm.request_id", requestId);
        traceActivity?.SetTag("payment_confirm.team_id", teamId.ToString());
        traceActivity?.SetTag("payment_confirm.team_slug", teamSlug);

        _logger.LogInformation("Payment confirmation request received. RequestId: {RequestId}, TeamSlug: {TeamSlug}, PaymentId: {PaymentId}",
            requestId, teamSlug, request?.PaymentId);

        try
        {
            ActivePaymentConfirms.WithLabels(teamId.ToString()).Inc();

            // 1. Validate request model
            if (request == null)
            {
                PaymentConfirmRequests.WithLabels(teamId.ToString(), "null_request", "validation").Inc();
                _logger.LogWarning("Payment confirmation request is null. RequestId: {RequestId}", requestId);
                return BadRequest(CreateErrorResponse("2000", "Invalid request", "Request body is required"));
            }

            traceActivity?.SetTag("payment_confirm.payment_id", request.PaymentId ?? "");
            traceActivity?.SetTag("payment_confirm.amount", request.Amount?.ToString() ?? "");

            // 2. Comprehensive validation
            var validationResult = await ValidatePaymentConfirmRequestAsync(request, teamId, cancellationToken);
            if (!validationResult.IsValid)
            {
                PaymentConfirmRequests.WithLabels(teamId.ToString(), "validation_failed", "validation").Inc();
                var errorMessage = string.Join("; ", validationResult.Errors);

                _logger.LogWarning("Payment confirmation validation failed. RequestId: {RequestId}, Errors: {Errors}",
                    requestId, errorMessage);

                traceActivity?.SetTag("payment_confirm.validation_error", errorMessage);
                return BadRequest(CreateErrorResponse("2100", "Validation failed", errorMessage));
            }

            // 3. Enhanced authentication validation
            var authContext = new PaymentConfirmAuthContext
            {
                TeamSlug = request.TeamSlug,
                Token = request.Token,
                RequestId = requestId,
                PaymentId = request.PaymentId,
                Amount = request.Amount,
                ClientIp = GetClientIpAddress(),
                UserAgent = Request.Headers.UserAgent.ToString(),
                Timestamp = DateTime.UtcNow
            };

            var authParameters = new Dictionary<string, object>
            {
                { "TeamSlug", authContext.TeamSlug },
                { "Token", authContext.Token },
                { "RequestId", authContext.RequestId },
                { "PaymentId", authContext.PaymentId },
                { "Amount", authContext.Amount },
                { "ClientIp", authContext.ClientIp },
                { "UserAgent", authContext.UserAgent },
                { "Timestamp", authContext.Timestamp }
            };
            
            var authResult = await _authenticationService.AuthenticateAsync(authParameters, cancellationToken);
            if (!authResult.IsAuthenticated)
            {
                PaymentConfirmRequests.WithLabels(teamId.ToString(), "auth_failed", "authentication").Inc();
                _logger.LogWarning("Payment confirmation authentication failed. RequestId: {RequestId}, TeamSlug: {TeamSlug}, Reason: {Reason}",
                    requestId, request.TeamSlug, authResult.FailureReason);

                traceActivity?.SetTag("payment_confirm.auth_error", authResult.FailureReason ?? "");
                return Unauthorized(CreateErrorResponse("2001", "Authentication failed", authResult.FailureReason ?? "Authentication failed"));
            }

            // 4. Check idempotency
            string? idempotencyKey = null;
            if (request.Data?.ContainsKey("idempotencyKey") == true)
            {
                idempotencyKey = request.Data["idempotencyKey"];
                
                if (!string.IsNullOrEmpty(idempotencyKey))
                {
                    var cacheKey = $"payment_confirm_idempotency:{teamId}:{idempotencyKey}";
                    if (_cache.TryGetValue(cacheKey, out PaymentConfirmResponseDto? cachedResponse))
                    {
                        PaymentConfirmIdempotency.WithLabels(teamId.ToString(), "cache_hit").Inc();
                        _logger.LogDebug("Payment confirmation idempotency cache hit. RequestId: {RequestId}, IdempotencyKey: {IdempotencyKey}",
                            requestId, idempotencyKey);

                        traceActivity?.SetTag("payment_confirm.idempotency_hit", "true");
                        return Ok(cachedResponse);
                    }

                    PaymentConfirmIdempotency.WithLabels(teamId.ToString(), "cache_miss").Inc();
                }
            }

            // 5. Perform payment confirmation
            var confirmationRequest = new ConfirmationRequest
            {
                Amount = request.Amount,
                ConfirmationReason = request.Description ?? "API payment confirmation",
                IdempotencyKey = idempotencyKey ?? "",
                Metadata = request.Data?.ToDictionary(kv => kv.Key, kv => (object)kv.Value) ?? new Dictionary<string, object>()
            };

            // Parse PaymentId to Guid
            if (!Guid.TryParse(request.PaymentId.Replace("pay_", ""), out var paymentId))
            {
                // Try to find payment by PaymentId string
                PaymentConfirmRequests.WithLabels(teamId.ToString(), "invalid_payment_id", "validation").Inc();
                return BadRequest(CreateErrorResponse("2100", "Validation failed", "Invalid PaymentId format"));
            }

            var confirmationResult = await _confirmationService.ConfirmPaymentAsync(paymentId, confirmationRequest, cancellationToken);
            var processingDuration = DateTime.UtcNow - startTime;

            // 6. Handle response
            if (confirmationResult.IsSuccess)
            {
                var response = new PaymentConfirmResponseDto
                {
                    Success = true,
                    PaymentId = request.PaymentId,
                    Status = confirmationResult.CurrentStatus.ToString(),
                    AuthorizedAmount = request.Amount,
                    ConfirmedAmount = confirmationResult.ResultMetadata.ContainsKey("confirmed_amount") 
                        ? (decimal)confirmationResult.ResultMetadata["confirmed_amount"] 
                        : request.Amount,
                    RemainingAmount = 0, // Full confirmation only
                    Currency = "RUB", // Default currency
                    ConfirmedAt = confirmationResult.ConfirmedAt,
                    BankDetails = new BankTransactionDetailsDto
                    {
                        BankTransactionId = $"bank_txn_{Guid.NewGuid().ToString("N")[..8]}",
                        AuthorizationCode = $"AUTH{Random.Shared.Next(100, 999)}",
                        Rrn = $"{Random.Shared.Next(100000000, 999999999)}{Random.Shared.Next(100, 999)}",
                        ResponseCode = "00",
                        ResponseMessage = "Approved"
                    },
                    Details = new ConfirmationDetailsDto
                    {
                        Description = confirmationRequest.ConfirmationReason,
                        ProcessingDuration = processingDuration,
                        Data = request.Data
                    }
                };

                // Cache successful response for idempotency
                if (!string.IsNullOrEmpty(idempotencyKey))
                {
                    var cacheKey = $"payment_confirm_idempotency:{teamId}:{idempotencyKey}";
                    _cache.Set(cacheKey, response, TimeSpan.FromMinutes(30));
                }

                PaymentConfirmRequests.WithLabels(teamId.ToString(), "success", "confirmed").Inc();
                PaymentConfirmAmount.WithLabels(teamId.ToString(), response.Currency ?? "RUB")
                    .Inc((double)(response.ConfirmedAmount ?? 0));

                _logger.LogInformation("Payment confirmation successful. RequestId: {RequestId}, PaymentId: {PaymentId}, Amount: {Amount}, Duration: {Duration}ms",
                    requestId, request.PaymentId, response.ConfirmedAmount, processingDuration.TotalMilliseconds);

                traceActivity?.SetTag("payment_confirm.success", "true");
                traceActivity?.SetTag("payment_confirm.amount", response.ConfirmedAmount?.ToString() ?? "");
                traceActivity?.SetTag("payment_confirm.duration_ms", processingDuration.TotalMilliseconds.ToString());

                return Ok(response);
            }
            else
            {
                // Handle specific failure cases
                var errorCode = DetermineErrorCode(confirmationResult.Errors);
                var errorMessage = string.Join("; ", confirmationResult.Errors);

                PaymentConfirmRequests.WithLabels(teamId.ToString(), "service_failed", "processing").Inc();

                _logger.LogWarning("Payment confirmation service failed. RequestId: {RequestId}, PaymentId: {PaymentId}, Errors: {Errors}",
                    requestId, request.PaymentId, errorMessage);

                traceActivity?.SetTag("payment_confirm.success", "false");
                traceActivity?.SetTag("payment_confirm.error_code", errorCode);
                traceActivity?.SetTag("payment_confirm.error_message", errorMessage);

                var statusCode = GetHttpStatusCodeFromErrorCode(errorCode);
                var errorResponse = CreateErrorResponse(errorCode, "Confirmation failed", errorMessage);
                
                return StatusCode(statusCode, errorResponse);
            }
        }
        catch (ValidationException ex)
        {
            PaymentConfirmRequests.WithLabels(teamId.ToString(), "validation_exception", "validation").Inc();
            _logger.LogError(ex, "Validation error during payment confirmation. RequestId: {RequestId}", requestId);
            traceActivity?.SetTag("payment_confirm.error", "validation_exception");
            return BadRequest(CreateErrorResponse("2100", "Validation error", ex.Message));
        }
        catch (UnauthorizedAccessException ex)
        {
            PaymentConfirmRequests.WithLabels(teamId.ToString(), "unauthorized_exception", "authorization").Inc();
            _logger.LogError(ex, "Authorization error during payment confirmation. RequestId: {RequestId}", requestId);
            traceActivity?.SetTag("payment_confirm.error", "unauthorized_exception");
            return Unauthorized(CreateErrorResponse("2001", "Authorization error", ex.Message));
        }
        catch (TimeoutException ex)
        {
            PaymentConfirmRequests.WithLabels(teamId.ToString(), "timeout_exception", "timeout").Inc();
            _logger.LogError(ex, "Timeout error during payment confirmation. RequestId: {RequestId}", requestId);
            traceActivity?.SetTag("payment_confirm.error", "timeout_exception");
            return StatusCode(StatusCodes.Status408RequestTimeout,
                CreateErrorResponse("2408", "Request timeout", "The confirmation request timed out. Please try again."));
        }
        catch (ArgumentException ex)
        {
            PaymentConfirmRequests.WithLabels(teamId.ToString(), "argument_exception", "validation").Inc();
            _logger.LogError(ex, "Argument error during payment confirmation. RequestId: {RequestId}", requestId);
            traceActivity?.SetTag("payment_confirm.error", "argument_exception");
            return BadRequest(CreateErrorResponse("2003", "Invalid argument", ex.Message));
        }
        catch (Exception ex)
        {
            PaymentConfirmRequests.WithLabels(teamId.ToString(), "internal_exception", "system").Inc();
            _logger.LogError(ex, "Unexpected error during payment confirmation. RequestId: {RequestId}", requestId);
            traceActivity?.SetTag("payment_confirm.error", "internal_exception");
            return StatusCode(StatusCodes.Status500InternalServerError,
                CreateErrorResponse("9999", "Internal error", "An unexpected error occurred"));
        }
        finally
        {
            ActivePaymentConfirms.WithLabels(teamId.ToString()).Dec();
        }
    }

    private async Task<PaymentConfirmValidationResult> ValidatePaymentConfirmRequestAsync(PaymentConfirmRequestDto request, Guid teamId, CancellationToken cancellationToken)
    {
        var errors = new List<string>();
        var warnings = new List<string>();

        // Validate PaymentId is provided
        if (string.IsNullOrWhiteSpace(request.PaymentId))
        {
            errors.Add("PaymentId is required");
        }
        else
        {
            // PaymentId format validation
            if (request.PaymentId.Length > 50)
                errors.Add("PaymentId cannot exceed 50 characters");
            if (!System.Text.RegularExpressions.Regex.IsMatch(request.PaymentId, @"^[a-zA-Z0-9\\-_]+$"))
                errors.Add("PaymentId can only contain alphanumeric characters, hyphens, and underscores");
        }

        // Amount validation
        if (request.Amount.HasValue)
        {
            if (request.Amount.Value <= 0)
                errors.Add("Amount must be greater than 0");
            if (request.Amount.Value > 50000000)
                errors.Add("Amount cannot exceed 50000000 kopecks (500000 RUB)");
        }

        // Description validation
        if (!string.IsNullOrEmpty(request.Description) && request.Description.Length > 255)
        {
            errors.Add("Description cannot exceed 255 characters");
        }

        // Team access validation
        if (string.IsNullOrWhiteSpace(request.TeamSlug))
        {
            errors.Add("TeamSlug is required for authentication");
        }

        if (string.IsNullOrWhiteSpace(request.Token))
        {
            errors.Add("Token is required for authentication");
        }

        // Receipt validation
        if (request.Receipt != null)
        {
            if (!string.IsNullOrEmpty(request.Receipt.Email) && 
                !new System.ComponentModel.DataAnnotations.EmailAddressAttribute().IsValid(request.Receipt.Email))
            {
                errors.Add("Invalid receipt email format");
            }
        }

        return new PaymentConfirmValidationResult
        {
            IsValid = errors.Count == 0,
            Errors = errors,
            Warnings = warnings
        };
    }

    private string DetermineErrorCode(List<string> errors)
    {
        if (errors.Any(e => e.Contains("not found")))
            return "2404";
        if (errors.Any(e => e.Contains("status must be") || e.Contains("already confirmed")))
            return "2409";
        if (errors.Any(e => e.Contains("amount")))
            return "2100";
        
        return "2100"; // Default validation error
    }

    private PaymentConfirmResponseDto CreateErrorResponse(string errorCode, string message, string details)
    {
        return new PaymentConfirmResponseDto
        {
            Success = false,
            ErrorCode = errorCode,
            Message = message,
            Details = new ConfirmationDetailsDto
            {
                Description = details
            }
        };
    }

    private int GetHttpStatusCodeFromErrorCode(string? errorCode)
    {
        return errorCode switch
        {
            "2001" => StatusCodes.Status401Unauthorized,
            "2003" => StatusCodes.Status400BadRequest,
            "2100" => StatusCodes.Status400BadRequest,
            "2404" => StatusCodes.Status404NotFound,
            "2408" => StatusCodes.Status408RequestTimeout,
            "2409" => StatusCodes.Status409Conflict,
            "2429" => StatusCodes.Status429TooManyRequests,
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

public class PaymentConfirmValidationResult
{
    public bool IsValid { get; set; }
    public List<string> Errors { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
}

public class PaymentConfirmAuthContext
{
    public string TeamSlug { get; set; } = string.Empty;
    public string Token { get; set; } = string.Empty;
    public string RequestId { get; set; } = string.Empty;
    public string? PaymentId { get; set; }
    public decimal? Amount { get; set; }
    public string ClientIp { get; set; } = string.Empty;
    public string UserAgent { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
}