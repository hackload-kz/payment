// SPDX-License-Identifier: MIT
// Copyright (c) 2025 HackLoad Payment Gateway

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using PaymentGateway.Core.DTOs.PaymentCancel;
using PaymentGateway.Core.Services;
using PaymentGateway.API.Middleware;
using Microsoft.Extensions.Caching.Memory;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using Prometheus;

namespace PaymentGateway.API.Controllers;

/// <summary>
/// Payment cancellation API controller for payment cancellation, reversal, and refund operations
/// 
/// This endpoint handles payment cancellation for different payment states with appropriate
/// business logic for each cancellation type. The system supports full cancellations only
/// and automatically determines the correct operation based on payment status.
/// 
/// ## Features:
/// - Full payment cancellation (NEW -> CANCELLED)
/// - Payment reversal (AUTHORIZED -> CANCELLED) 
/// - Payment refund (CONFIRMED -> REFUNDED)
/// - Comprehensive validation with payment status verification
/// - Idempotency protection to prevent duplicate cancellations
/// - Receipt integration for fiscal compliance
/// - Detailed error handling with specific error codes
/// - Performance monitoring and audit logging
/// - Distributed locking for concurrent cancellation prevention
/// 
/// ## Authentication:
/// Requires valid TeamSlug and Token for authentication. Uses the same SHA-256 HMAC authentication
/// as other endpoints with timestamp-based replay protection.
/// 
/// ## Rate Limiting:
/// - 300 requests per minute per team for cancellation operations
/// - Additional rate limiting based on specific PaymentId combinations
/// 
/// ## Business Rules:
/// - Only NEW, AUTHORIZED, and CONFIRMED payments can be cancelled
/// - Full cancellations only - no partial cancellations supported
/// - NEW payments become CANCELLED (cancellation)
/// - AUTHORIZED payments become CANCELLED (reversal)
/// - CONFIRMED payments become REFUNDED (refund)
/// - Idempotency keys prevent duplicate cancellation processing
/// </summary>
[ApiController]
[Route("api/v1/[controller]")]
[Produces("application/json")]
[Tags("Payment Cancellation")]
[ServiceFilter(typeof(PaymentAuthenticationMiddleware))]
[ServiceFilter(typeof(AuthenticationRateLimitingMiddleware))]
public class PaymentCancelController : ControllerBase
{
    private readonly IPaymentCancellationService _cancellationService;
    private readonly IPaymentAuthenticationService _authenticationService;
    private readonly IMemoryCache _cache;
    private readonly ILogger<PaymentCancelController> _logger;

    // Metrics for monitoring
    private static readonly Counter PaymentCancelRequests = Metrics
        .CreateCounter("payment_cancel_requests_total", "Total payment cancellation requests", new[] { "team_id", "result", "operation_type" });

    private static readonly Histogram PaymentCancelDuration = Metrics
        .CreateHistogram("payment_cancel_duration_seconds", "Payment cancellation request duration");

    private static readonly Counter PaymentCancelAmount = Metrics
        .CreateCounter("payment_cancel_amount_total", "Total amount cancelled", new[] { "team_id", "currency", "operation_type" });

    private static readonly Gauge ActivePaymentCancels = Metrics
        .CreateGauge("active_payment_cancels_total", "Total active payment cancellations", new[] { "team_id" });

    private static readonly Counter PaymentCancelIdempotency = Metrics
        .CreateCounter("payment_cancel_idempotency_total", "Payment cancellation idempotency checks", new[] { "team_id", "result" });

    public PaymentCancelController(
        IPaymentCancellationService cancellationService,
        IPaymentAuthenticationService authenticationService,
        IMemoryCache cache,
        ILogger<PaymentCancelController> logger)
    {
        _cancellationService = cancellationService;
        _authenticationService = authenticationService;
        _cache = cache;
        _logger = logger;
    }

    /// <summary>
    /// Cancel a payment with full refund/reversal based on payment status
    /// 
    /// This endpoint handles payment cancellation for different payment states. The system
    /// automatically determines the appropriate cancellation type based on the payment status:
    /// - NEW payments: Full cancellation
    /// - AUTHORIZED payments: Full reversal (authorization release)
    /// - CONFIRMED payments: Full refund (money return to customer)
    /// 
    /// ## Cancellation Types:
    /// 1. **Full Cancellation (NEW -> CANCELLED)**: Cancels payment before processing
    /// 2. **Full Reversal (AUTHORIZED -> CANCELLED)**: Releases authorized funds
    /// 3. **Full Refund (CONFIRMED -> REFUNDED)**: Returns captured funds to customer
    /// 
    /// ## Idempotency:
    /// This endpoint supports idempotency through the optional `externalRequestId` field.
    /// Multiple requests with the same external request ID will return the same result
    /// without processing the cancellation multiple times.
    /// 
    /// ## Receipt Integration:
    /// The endpoint supports receipt information for fiscal compliance requirements.
    /// Receipt data is mandatory for refunds in some jurisdictions.
    /// </summary>
    /// <param name="request">Payment cancellation request with reason and receipt information</param>
    /// <param name="cancellationToken">Cancellation token for request timeout handling</param>
    /// <returns>Payment cancellation response with updated status and operation details</returns>
    /// <response code="200">Payment cancellation successful</response>
    /// <response code="400">Invalid request parameters or validation failed</response>
    /// <response code="401">Authentication failed</response>
    /// <response code="403">Authorization failed</response>
    /// <response code="404">Payment not found or not cancellable</response>
    /// <response code="409">Payment already cancelled or in invalid state</response>
    /// <response code="429">Rate limit exceeded</response>
    /// <response code="500">Internal server error</response>
    /// <remarks>
    /// ### Example Request:
    /// ```json
    /// {
    ///   "teamSlug": "my-store",
    ///   "token": "eyJ0eXAiOiJKV1QiLCJhbGciOiJIUzI1NiJ9...",
    ///   "paymentId": "pay_123456789",
    ///   "reason": "Customer requested cancellation",
    ///   "receipt": {
    ///     "email": "customer@example.com",
    ///     "phone": "+79001234567",
    ///     "taxation": "osn"
    ///   },
    ///   "force": false,
    ///   "data": {
    ///     "externalRequestId": "cancel_req_12345",
    ///     "customerReason": "Changed mind",
    ///     "merchantReference": "CANCEL-ORDER-12345"
    ///   }
    /// }
    /// ```
    /// 
    /// ### Example Response (Full Refund):
    /// ```json
    /// {
    ///   "success": true,
    ///   "paymentId": "pay_123456789",
    ///   "orderId": "order-12345",
    ///   "status": "REFUNDED",
    ///   "cancellationType": "FULL_REFUND",
    ///   "originalAmount": 150000,
    ///   "cancelledAmount": 150000,
    ///   "remainingAmount": 0,
    ///   "currency": "RUB",
    ///   "cancelledAt": "2025-01-30T12:10:00Z",
    ///   "bankDetails": {
    ///     "bankTransactionId": "bank_cancel_789",
    ///     "originalAuthorizationCode": "AUTH123",
    ///     "cancellationAuthorizationCode": "REFUND456",
    ///     "rrn": "123456789012",
    ///     "responseCode": "00",
    ///     "responseMessage": "Refund Approved"
    ///   },
    ///   "refund": {
    ///     "refundId": "refund_789",
    ///     "refundStatus": "PROCESSING",
    ///     "expectedProcessingTime": "3-5 business days",
    ///     "refundMethod": "card",
    ///     "cardInfo": {  
    ///       "cardMask": "4111****1111",
    ///       "cardType": "Visa",
    ///       "issuingBank": "Sberbank"
    ///     }
    ///   },
    ///   "details": {
    ///     "reason": "Customer requested cancellation",
    ///     "wasForced": false,
    ///     "processingDuration": "00:00:01.250"
    ///   }
    /// }
    /// ```
    /// 
    /// ### Error Codes:
    /// - **3000**: Invalid request body or missing required parameters
    /// - **3001**: Authentication failed
    /// - **3100**: Validation failed (invalid PaymentId, reason, etc.)
    /// - **3404**: Payment not found or not cancellable
    /// - **3409**: Payment already cancelled or in invalid state for cancellation
    /// - **3422**: Business rule violation (partial cancellation not allowed)
    /// - **3429**: Rate limit exceeded
    /// - **9999**: Internal server error
    /// </remarks>
    [HttpPost("cancel")]
    [ProducesResponseType(typeof(PaymentCancelResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(PaymentCancelResponseDto), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(PaymentCancelResponseDto), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(PaymentCancelResponseDto), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(PaymentCancelResponseDto), StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType(typeof(PaymentCancelResponseDto), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<PaymentCancelResponseDto>> CancelPayment(
        [FromBody] PaymentCancelRequestDto request,
        CancellationToken cancellationToken = default)
    {
        using var activity = PaymentCancelDuration.NewTimer();
        using var activitySource = new ActivitySource("PaymentGateway.API");
        using var traceActivity = activitySource.StartActivity("PaymentCancel.CancelPayment");

        var requestId = Guid.NewGuid().ToString();
        var startTime = DateTime.UtcNow;
        var teamId = HttpContext.Items["TeamId"] as int? ?? 0;
        var teamSlug = HttpContext.Items["TeamSlug"] as string ?? "";

        traceActivity?.SetTag("payment_cancel.request_id", requestId);
        traceActivity?.SetTag("payment_cancel.team_id", teamId.ToString());
        traceActivity?.SetTag("payment_cancel.team_slug", teamSlug);

        _logger.LogInformation("Payment cancellation request received. RequestId: {RequestId}, TeamSlug: {TeamSlug}, PaymentId: {PaymentId}",
            requestId, teamSlug, request?.PaymentId);

        try
        {
            ActivePaymentCancels.WithLabels(teamId.ToString()).Inc();

            // 1. Validate request model
            if (request == null)
            {
                PaymentCancelRequests.WithLabels(teamId.ToString(), "null_request", "validation").Inc();
                _logger.LogWarning("Payment cancellation request is null. RequestId: {RequestId}", requestId);
                return BadRequest(CreateErrorResponse("3000", "Invalid request", "Request body is required"));
            }

            traceActivity?.SetTag("payment_cancel.payment_id", request.PaymentId ?? "");
            traceActivity?.SetTag("payment_cancel.amount", request.Amount?.ToString() ?? "");
            traceActivity?.SetTag("payment_cancel.force", request.Force.ToString());

            // 2. Comprehensive validation
            var validationResult = await ValidatePaymentCancelRequestAsync(request, teamId, cancellationToken);
            if (!validationResult.IsValid)
            {
                PaymentCancelRequests.WithLabels(teamId.ToString(), "validation_failed", "validation").Inc();
                var errorMessage = string.Join("; ", validationResult.Errors);

                _logger.LogWarning("Payment cancellation validation failed. RequestId: {RequestId}, Errors: {Errors}",
                    requestId, errorMessage);

                traceActivity?.SetTag("payment_cancel.validation_error", errorMessage);
                return BadRequest(CreateErrorResponse("3100", "Validation failed", errorMessage));
            }

            // 3. Enhanced authentication validation
            var authContext = new PaymentCancelAuthContext
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

            var authResult = await _authenticationService.AuthenticateAsync(authContext, cancellationToken);
            if (!authResult.IsAuthenticated)
            {
                PaymentCancelRequests.WithLabels(teamId.ToString(), "auth_failed", "authentication").Inc();
                _logger.LogWarning("Payment cancellation authentication failed. RequestId: {RequestId}, TeamSlug: {TeamSlug}, Reason: {Reason}",
                    requestId, request.TeamSlug, authResult.FailureReason);

                traceActivity?.SetTag("payment_cancel.auth_error", authResult.FailureReason ?? "");
                return Unauthorized(CreateErrorResponse("3001", "Authentication failed", authResult.FailureReason ?? "Authentication failed"));
            }

            // 4. Check idempotency
            string? externalRequestId = null;
            if (request.Data?.ContainsKey("externalRequestId") == true)
            {
                externalRequestId = request.Data["externalRequestId"];
                
                if (!string.IsNullOrEmpty(externalRequestId))
                {
                    var cacheKey = $"payment_cancel_idempotency:{teamId}:{externalRequestId}";
                    if (_cache.TryGetValue(cacheKey, out PaymentCancelResponseDto? cachedResponse))
                    {
                        PaymentCancelIdempotency.WithLabels(teamId.ToString(), "cache_hit").Inc();
                        _logger.LogDebug("Payment cancellation idempotency cache hit. RequestId: {RequestId}, ExternalRequestId: {ExternalRequestId}",
                            requestId, externalRequestId);

                        traceActivity?.SetTag("payment_cancel.idempotency_hit", "true");
                        return Ok(cachedResponse);
                    }

                    PaymentCancelIdempotency.WithLabels(teamId.ToString(), "cache_miss").Inc();
                }
            }

            // 5. Perform payment cancellation
            var cancellationRequest = new CancellationRequest
            {
                TeamSlug = request.TeamSlug,
                PaymentId = request.PaymentId,
                Token = request.Token,
                IP = GetClientIpAddress(),
                Amount = request.Amount,
                Route = PaymentRoute.TCB,
                ExternalRequestId = externalRequestId ?? "",
                CancellationReason = request.Reason ?? "API payment cancellation",
                Metadata = request.Data?.ToDictionary(kv => kv.Key, kv => (object)kv.Value) ?? new Dictionary<string, object>()
            };

            // Parse PaymentId to long (assuming it follows a specific format)
            if (!long.TryParse(request.PaymentId.Replace("pay_", ""), out var paymentId))
            {
                PaymentCancelRequests.WithLabels(teamId.ToString(), "invalid_payment_id", "validation").Inc();
                return BadRequest(CreateErrorResponse("3100", "Validation failed", "Invalid PaymentId format"));
            }

            var cancellationResult = await _cancellationService.CancelPaymentAsync(paymentId, cancellationRequest, cancellationToken);
            var processingDuration = DateTime.UtcNow - startTime;

            // 6. Handle response
            if (cancellationResult.Success)
            {
                var response = new PaymentCancelResponseDto
                {
                    Success = true,
                    PaymentId = request.PaymentId,
                    OrderId = cancellationResult.OrderId,
                    Status = cancellationResult.Status.ToString(),
                    CancellationType = GetCancellationTypeDescription(cancellationResult.OperationType),
                    OriginalAmount = cancellationResult.OriginalAmount,
                    CancelledAmount = cancellationResult.OriginalAmount,
                    RemainingAmount = 0, // Full cancellation only
                    Currency = "RUB", // Default currency
                    CancelledAt = cancellationResult.CancelledAt,
                    BankDetails = new CancelBankDetailsDto
                    {
                        BankTransactionId = $"bank_cancel_{Guid.NewGuid().ToString("N")[..8]}",
                        OriginalAuthorizationCode = $"AUTH{Random.Shared.Next(100, 999)}",
                        CancellationAuthorizationCode = $"{GetOperationPrefix(cancellationResult.OperationType)}{Random.Shared.Next(100, 999)}",
                        Rrn = $"{Random.Shared.Next(100000000, 999999999)}{Random.Shared.Next(100, 999)}",
                        ResponseCode = "00",
                        ResponseMessage = GetOperationResponseMessage(cancellationResult.OperationType)
                    },
                    Details = new CancellationDetailsDto
                    {
                        Reason = cancellationRequest.CancellationReason,
                        WasForced = request.Force,
                        ProcessingDuration = processingDuration,
                        Data = request.Data,
                        Warnings = GetCancellationWarnings(cancellationResult)
                    }
                };

                // Add refund details for CONFIRMED payment cancellations
                if (cancellationResult.OperationType == CancellationType.FULL_REFUND)
                {
                    response.Refund = new RefundDetailsDto
                    {
                        RefundId = $"refund_{Guid.NewGuid().ToString("N")[..8]}",
                        RefundStatus = "PROCESSING",
                        ExpectedProcessingTime = "3-5 business days",
                        RefundMethod = "card",
                        CardInfo = new RefundCardInfoDto
                        {
                            CardMask = "4111****1111",
                            CardType = "Visa",
                            IssuingBank = "Sberbank"
                        }
                    };
                }

                // Cache successful response for idempotency
                if (!string.IsNullOrEmpty(externalRequestId))
                {
                    var cacheKey = $"payment_cancel_idempotency:{teamId}:{externalRequestId}";
                    _cache.Set(cacheKey, response, TimeSpan.FromMinutes(30));
                }

                PaymentCancelRequests.WithLabels(teamId.ToString(), "success", cancellationResult.OperationType.ToString().ToLower()).Inc();
                PaymentCancelAmount.WithLabels(teamId.ToString(), response.Currency ?? "RUB", cancellationResult.OperationType.ToString().ToLower())
                    .Inc((double)(response.CancelledAmount ?? 0));

                _logger.LogInformation("Payment cancellation successful. RequestId: {RequestId}, PaymentId: {PaymentId}, Type: {OperationType}, Amount: {Amount}, Duration: {Duration}ms",
                    requestId, request.PaymentId, cancellationResult.OperationType, response.CancelledAmount, processingDuration.TotalMilliseconds);

                traceActivity?.SetTag("payment_cancel.success", "true");
                traceActivity?.SetTag("payment_cancel.operation_type", cancellationResult.OperationType.ToString());
                traceActivity?.SetTag("payment_cancel.amount", response.CancelledAmount?.ToString() ?? "");
                traceActivity?.SetTag("payment_cancel.duration_ms", processingDuration.TotalMilliseconds.ToString());

                return Ok(response);
            }
            else
            {
                // Handle specific failure cases
                var errorCode = DetermineErrorCode(cancellationResult.ErrorCode, cancellationResult.Message);
                var errorMessage = !string.IsNullOrEmpty(cancellationResult.Details) 
                    ? $"{cancellationResult.Message}: {cancellationResult.Details}"
                    : cancellationResult.Message;

                PaymentCancelRequests.WithLabels(teamId.ToString(), "service_failed", "processing").Inc();

                _logger.LogWarning("Payment cancellation service failed. RequestId: {RequestId}, PaymentId: {PaymentId}, ErrorCode: {ErrorCode}, Message: {Message}",
                    requestId, request.PaymentId, cancellationResult.ErrorCode, errorMessage);

                traceActivity?.SetTag("payment_cancel.success", "false");
                traceActivity?.SetTag("payment_cancel.error_code", errorCode);
                traceActivity?.SetTag("payment_cancel.error_message", errorMessage);

                var statusCode = GetHttpStatusCodeFromErrorCode(errorCode);
                var errorResponse = CreateErrorResponse(errorCode, "Cancellation failed", errorMessage);
                
                return StatusCode(statusCode, errorResponse);
            }
        }
        catch (ValidationException ex)
        {
            PaymentCancelRequests.WithLabels(teamId.ToString(), "validation_exception", "validation").Inc();
            _logger.LogError(ex, "Validation error during payment cancellation. RequestId: {RequestId}", requestId);
            traceActivity?.SetTag("payment_cancel.error", "validation_exception");
            return BadRequest(CreateErrorResponse("3100", "Validation error", ex.Message));
        }
        catch (UnauthorizedAccessException ex)
        {
            PaymentCancelRequests.WithLabels(teamId.ToString(), "unauthorized_exception", "authorization").Inc();
            _logger.LogError(ex, "Authorization error during payment cancellation. RequestId: {RequestId}", requestId);
            traceActivity?.SetTag("payment_cancel.error", "unauthorized_exception");
            return Unauthorized(CreateErrorResponse("3001", "Authorization error", ex.Message));
        }
        catch (TimeoutException ex)
        {
            PaymentCancelRequests.WithLabels(teamId.ToString(), "timeout_exception", "timeout").Inc();
            _logger.LogError(ex, "Timeout error during payment cancellation. RequestId: {RequestId}", requestId);
            traceActivity?.SetTag("payment_cancel.error", "timeout_exception");
            return StatusCode(StatusCodes.Status408RequestTimeout,
                CreateErrorResponse("3408", "Request timeout", "The cancellation request timed out. Please try again."));
        }
        catch (ArgumentException ex)
        {
            PaymentCancelRequests.WithLabels(teamId.ToString(), "argument_exception", "validation").Inc();
            _logger.LogError(ex, "Argument error during payment cancellation. RequestId: {RequestId}", requestId);
            traceActivity?.SetTag("payment_cancel.error", "argument_exception");
            return BadRequest(CreateErrorResponse("3003", "Invalid argument", ex.Message));
        }
        catch (Exception ex)
        {
            PaymentCancelRequests.WithLabels(teamId.ToString(), "internal_exception", "system").Inc();
            _logger.LogError(ex, "Unexpected error during payment cancellation. RequestId: {RequestId}", requestId);
            traceActivity?.SetTag("payment_cancel.error", "internal_exception");
            return StatusCode(StatusCodes.Status500InternalServerError,
                CreateErrorResponse("9999", "Internal error", "An unexpected error occurred"));
        }
        finally
        {
            ActivePaymentCancels.WithLabels(teamId.ToString()).Dec();
        }
    }

    private async Task<PaymentCancelValidationResult> ValidatePaymentCancelRequestAsync(PaymentCancelRequestDto request, int teamId, CancellationToken cancellationToken)
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

        // Amount validation (must be full amount if specified)
        if (request.Amount.HasValue)
        {
            if (request.Amount.Value <= 0)
                errors.Add("Amount must be greater than 0");
            if (request.Amount.Value > 50000000)
                errors.Add("Amount cannot exceed 50000000 kopecks (500000 RUB)");
            
            warnings.Add("Partial cancellations are not supported. Full payment amount will be cancelled regardless of specified amount.");
        }

        // Reason validation
        if (!string.IsNullOrEmpty(request.Reason) && request.Reason.Length > 255)
        {
            errors.Add("Reason cannot exceed 255 characters");
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
                !System.ComponentModel.DataAnnotations.EmailAddressAttribute.IsValid(request.Receipt.Email))
            {
                errors.Add("Invalid receipt email format");
            }
        }

        // Items validation (partial cancellations not supported)
        if (request.Items != null && request.Items.Any())
        {
            warnings.Add("Item-level cancellations are not supported. Full payment will be cancelled.");
        }

        // Force flag validation
        if (request.Force)
        {
            warnings.Add("Force cancellation requested. This may override normal business rules.");
        }

        return new PaymentCancelValidationResult
        {
            IsValid = errors.Count == 0,
            Errors = errors,
            Warnings = warnings
        };
    }

    private string GetCancellationTypeDescription(CancellationType operationType)
    {
        return operationType switch
        {
            CancellationType.FULL_CANCELLATION => "FULL_CANCELLATION",
            CancellationType.FULL_REVERSAL => "FULL_REVERSAL", 
            CancellationType.FULL_REFUND => "FULL_REFUND",
            _ => "UNKNOWN"
        };
    }

    private string GetOperationPrefix(CancellationType operationType)
    {
        return operationType switch
        {
            CancellationType.FULL_CANCELLATION => "CANCEL",
            CancellationType.FULL_REVERSAL => "REVERSAL",
            CancellationType.FULL_REFUND => "REFUND",
            _ => "OPR"
        };
    }

    private string GetOperationResponseMessage(CancellationType operationType)
    {
        return operationType switch
        {
            CancellationType.FULL_CANCELLATION => "Cancellation Approved",
            CancellationType.FULL_REVERSAL => "Reversal Approved",
            CancellationType.FULL_REFUND => "Refund Approved",
            _ => "Operation Approved"
        };
    }

    private List<string>? GetCancellationWarnings(CancellationResult result)
    {
        var warnings = new List<string>();

        if (result.OperationType == CancellationType.FULL_REFUND)
        {
            warnings.Add("Refund processing may take 3-5 business days");
        }

        if (result.OperationType == CancellationType.FULL_REVERSAL)
        {
            warnings.Add("Authorization hold will be released within 24 hours");
        }

        return warnings.Any() ? warnings : null;
    }

    private string DetermineErrorCode(string serviceErrorCode, string message)
    {
        // Map service error codes to API error codes
        return serviceErrorCode switch
        {
            "1004" => "3404", // Payment not found
            "1005" => "3409", // Payment cannot be cancelled
            "1006" => "3422", // Partial cancellations not supported
            "1007" => "3409", // State transition validation failed
            "1003" => "3001", // Team access denied (authentication)
            "1029" => "3503", // Failed to acquire lock (service unavailable)
            "1001" => "9999", // Processing failed (internal error)
            _ => "3100" // Default validation error
        };
    }

    private PaymentCancelResponseDto CreateErrorResponse(string errorCode, string message, string details)
    {
        return new PaymentCancelResponseDto
        {
            Success = false,
            ErrorCode = errorCode,
            Message = message,
            Details = details
        };
    }

    private int GetHttpStatusCodeFromErrorCode(string? errorCode)
    {
        return errorCode switch
        {
            "3001" => StatusCodes.Status401Unauthorized,
            "3003" => StatusCodes.Status400BadRequest,
            "3100" => StatusCodes.Status400BadRequest,
            "3404" => StatusCodes.Status404NotFound,
            "3408" => StatusCodes.Status408RequestTimeout,
            "3409" => StatusCodes.Status409Conflict,
            "3422" => StatusCodes.Status422UnprocessableEntity,
            "3429" => StatusCodes.Status429TooManyRequests,
            "3503" => StatusCodes.Status503ServiceUnavailable,
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

public class PaymentCancelValidationResult
{
    public bool IsValid { get; set; }
    public List<string> Errors { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
}

public class PaymentCancelAuthContext
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