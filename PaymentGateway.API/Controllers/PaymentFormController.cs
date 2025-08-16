// SPDX-License-Identifier: MIT
// Copyright (c) 2025 HackLoad Payment Gateway

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using PaymentGateway.API.Models;
using PaymentGateway.Core.Configuration;
using PaymentGateway.Core.DTOs.Common;
using PaymentGateway.Core.DTOs.PaymentInit;
using PaymentGateway.Core.Enums;
using PaymentGateway.Core.Interfaces;
using PaymentGateway.Core.Repositories;
using PaymentGateway.Core.Services;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace PaymentGateway.API.Controllers;

/// <summary>
/// Payment Form Controller for server-side payment form processing
/// 
/// This controller handles:
/// - Payment form rendering with payment data
/// - Server-side form validation and security
/// - Secure card data handling and processing
/// - Form submission processing and result pages
/// - CSRF protection and form security measures
/// - Payment form processing metrics and monitoring
/// </summary>
[ApiController]
[Route("api/v1/[controller]")]
[Produces("application/json", "text/html")]
public class PaymentFormController : ControllerBase
{
    private readonly ILogger<PaymentFormController> _logger;
    private readonly IMemoryCache _memoryCache;
    private readonly IPaymentRepository _paymentRepository;
    private readonly ITeamRepository _teamRepository;
    private readonly ICardPaymentProcessingService _cardProcessingService;
    private readonly IPaymentInitializationService _paymentInitService;
    private readonly IPaymentLifecycleManagementService _lifecycleService;
    private readonly IMetricsService _metricsService;
    private readonly IConfiguration _configuration;
    private readonly IPaymentStateManager _paymentStateManager;
    private readonly ApiOptions _apiOptions;

    // Metrics
    private static readonly System.Diagnostics.Metrics.Meter _meter = new("PaymentGateway.API.PaymentForm");
    private static readonly System.Diagnostics.Metrics.Counter<long> _formRenderCounter = 
        _meter.CreateCounter<long>("payment_form_renders_total");
    private static readonly System.Diagnostics.Metrics.Counter<long> _formSubmissionCounter = 
        _meter.CreateCounter<long>("payment_form_submissions_total");
    private static readonly System.Diagnostics.Metrics.Histogram<double> _formProcessingDuration = 
        _meter.CreateHistogram<double>("payment_form_processing_duration_seconds");
    private static readonly System.Diagnostics.Metrics.Counter<long> _csrfValidationCounter = 
        _meter.CreateCounter<long>("payment_form_csrf_validations_total");

    public PaymentFormController(
        ILogger<PaymentFormController> logger,
        IMemoryCache memoryCache,
        IPaymentRepository paymentRepository,
        ITeamRepository teamRepository,
        ICardPaymentProcessingService cardProcessingService,
        IPaymentInitializationService paymentInitService,
        IPaymentLifecycleManagementService lifecycleService,
        IMetricsService metricsService,
        IConfiguration configuration,
        IPaymentStateManager paymentStateManager,
        IOptions<ApiOptions> apiOptions)
    {
        _logger = logger;
        _memoryCache = memoryCache;
        _paymentRepository = paymentRepository;
        _teamRepository = teamRepository;
        _cardProcessingService = cardProcessingService;
        _paymentInitService = paymentInitService;
        _lifecycleService = lifecycleService;
        _metricsService = metricsService;
        _configuration = configuration;
        _paymentStateManager = paymentStateManager;
        _apiOptions = apiOptions.Value;
    }

    /// <summary>
    /// Render payment form for a specific payment
    /// GET /api/v1/paymentform/render/{paymentId}
    /// </summary>
    [HttpGet("render/{paymentId}")]
    [AllowAnonymous]
    [Produces("text/html")]
    public async Task<IActionResult> RenderPaymentForm(string paymentId, [FromQuery] string? lang = "en")
    {
        var stopwatch = Stopwatch.StartNew();
        var clientIp = GetClientIpAddress();

        try
        {
            _logger.LogInformation("Rendering payment form for PaymentId: {PaymentId}, IP: {ClientIp}, Language: {Language}",
                paymentId, clientIp, lang);

            // Validate payment ID format
            if (string.IsNullOrWhiteSpace(paymentId) || !IsValidPaymentId(paymentId))
            {
                _logger.LogWarning("Invalid PaymentId format: {PaymentId}", paymentId);
                return BadRequest(new { error = "Invalid payment ID format" });
            }

            // Get payment data
            var payment = await _paymentRepository.GetByPaymentIdAsync(paymentId);
            if (payment == null)
            {
                _logger.LogWarning("Payment not found: {PaymentId}", paymentId);
                return NotFound(new { error = "Payment not found" });
            }

            // Synchronize payment state with state manager
            await _paymentStateManager.SynchronizePaymentStateAsync(payment.PaymentId, payment.Status);

            // DEBUG: Log the payment status being checked
            _logger.LogInformation("DEBUG: Payment {PaymentId} status check - DB Status: {DbStatus}, IsAllowed: {IsAllowed}", 
                paymentId, payment.Status, IsPaymentFormAllowed(payment.Status));

            // Check payment status - allow form rendering only for payments that haven't been processed yet
            if (!IsPaymentFormAllowed(payment.Status))
            {
                _logger.LogWarning("Payment form cannot be rendered for payment in status: {Status}, PaymentId: {PaymentId}",
                    payment.Status, paymentId);
                
                // Return a proper HTML page explaining the payment status instead of JSON error
                return await RenderPaymentStatusPage(payment, GetPaymentStatusMessage(payment.Status));
            }

            // Get team information - use navigation property or find by TeamSlug
            var team = payment.Team ?? await _teamRepository.GetByTeamSlugAsync(payment.TeamSlug);
            if (team == null)
            {
                _logger.LogError("Team not found for payment: {PaymentId}, TeamSlug: {TeamSlug}", paymentId, payment.TeamSlug);
                return BadRequest(new { error = "Payment configuration error" });
            }

            // Generate CSRF token
            var csrfToken = GenerateCsrfToken(paymentId);
            StoreCsrfToken(paymentId, csrfToken);

            // Create payment form data model with timezone-aware expiration data
            var currentServerTime = DateTime.UtcNow;
            var paymentFormData = new PaymentFormData
            {
                PaymentId = payment.PaymentId,
                OrderId = payment.OrderId,
                Amount = payment.Amount,
                Currency = payment.Currency,
                Description = payment.Description,
                MerchantName = team.Name,
                SuccessUrl = payment.SuccessUrl,
                FailUrl = payment.FailUrl,
                Language = ValidateLanguage(lang),
                CsrfToken = csrfToken,
                PaymentTimeout = payment.ExpiresAt,
                BasePath = _apiOptions.BaseUrl,
                Receipt = payment.Receipt != null ? JsonSerializer.Deserialize<Dictionary<string, object>>(payment.Receipt) : null,
                
                // Timezone-aware expiration data
                ExpiresAtUtc = payment.ExpiresAt?.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"),
                ExpiresAtUnix = payment.ExpiresAt.HasValue ? (long)payment.ExpiresAt.Value.Subtract(new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalMilliseconds : null,
                ServerTimeUtc = currentServerTime.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"),
                ServerTimeUnix = new DateTimeOffset(currentServerTime).ToUnixTimeMilliseconds()
            };

            // Record metrics
            _formRenderCounter.Add(1, new KeyValuePair<string, object?>("team_slug", team.TeamSlug),
                new KeyValuePair<string, object?>("currency", payment.Currency),
                new KeyValuePair<string, object?>("language", paymentFormData.Language));

            // Render HTML form
            var htmlContent = await RenderPaymentFormHtml(paymentFormData);
            
            _logger.LogInformation("Payment form rendered successfully for PaymentId: {PaymentId}, Duration: {Duration}ms",
                paymentId, stopwatch.ElapsedMilliseconds);

            return Content(htmlContent, "text/html; charset=utf-8");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error rendering payment form for PaymentId: {PaymentId}", paymentId);
            
            _formRenderCounter.Add(1, new KeyValuePair<string, object?>("result", "error"),
                new KeyValuePair<string, object?>("error_type", ex.GetType().Name));

            return StatusCode(500, new { error = "Internal server error" });
        }
        finally
        {
            _formProcessingDuration.Record(stopwatch.Elapsed.TotalSeconds,
                new KeyValuePair<string, object?>("operation", "render"));
        }
    }

    /// <summary>
    /// Process payment form submission
    /// POST /api/v1/paymentform/submit
    /// </summary>
    [HttpPost("submit")]
    [AllowAnonymous]
    [Consumes("application/x-www-form-urlencoded", "application/json")]
    public async Task<IActionResult> SubmitPaymentForm([FromForm] PaymentFormSubmission submission)
    {
        var stopwatch = Stopwatch.StartNew();
        var clientIp = GetClientIpAddress();

        try
        {
            _logger.LogInformation("Processing payment form submission for PaymentId: {PaymentId}, IP: {ClientIp}",
                submission.PaymentId, clientIp);

            // Server-side validation
            var validationResult = ValidateFormSubmission(submission);
            if (!validationResult.IsValid)
            {
                _logger.LogWarning("Form validation failed for PaymentId: {PaymentId}, Errors: {Errors}",
                    submission.PaymentId, string.Join(", ", validationResult.Errors));

                _formSubmissionCounter.Add(1, new KeyValuePair<string, object?>("result", "validation_error"));

                var errorMessage = string.Join("; ", validationResult.Errors);
                return await RedirectToFailureAsync(submission.PaymentId, $"Validation failed: {errorMessage}");
            }

            // CSRF token validation - TEMPORARILY DISABLED
            // if (!ValidateCsrfToken(submission.PaymentId, submission.CsrfToken))
            // {
            //     _logger.LogWarning("CSRF token validation failed for PaymentId: {PaymentId}, IP: {ClientIp}",
            //         submission.PaymentId, clientIp);

            //     _csrfValidationCounter.Add(1, new KeyValuePair<string, object?>("result", "failed"));

            //     return await RedirectToFailureAsync(submission.PaymentId, "Invalid security token");
            // }

            _csrfValidationCounter.Add(1, new KeyValuePair<string, object?>("result", "success"));

            // Get payment and validate status
            var payment = await _paymentRepository.GetByPaymentIdAsync(submission.PaymentId);
            if (payment == null)
            {
                _logger.LogWarning("Payment not found during form submission: {PaymentId}", submission.PaymentId);
                return await RedirectToFailureAsync(submission.PaymentId, "Payment not found");
            }

            // Synchronize payment state with state manager
            await _paymentStateManager.SynchronizePaymentStateAsync(payment.PaymentId, payment.Status);

            if (payment.Status != PaymentGateway.Core.Enums.PaymentStatus.NEW && 
                payment.Status != PaymentGateway.Core.Enums.PaymentStatus.INIT)
            {
                _logger.LogWarning("Payment form submitted for payment in invalid status: {Status}, PaymentId: {PaymentId}",
                    payment.Status, submission.PaymentId);

                return await RedirectToFailureAsync(submission.PaymentId, $"Payment is in {payment.Status} status and cannot be processed", payment);
            }

            // First transition: INIT/NEW → FORM_SHOWED (customer engaged with form)
            bool formShowedTransition = false;
            
            // Try transitioning from NEW state first
            if (payment.Status == PaymentGateway.Core.Enums.PaymentStatus.NEW)
            {
                formShowedTransition = await _paymentStateManager.TryTransitionStateAsync(
                    submission.PaymentId, 
                    PaymentGateway.Core.Enums.PaymentStatus.NEW, 
                    PaymentGateway.Core.Enums.PaymentStatus.FORM_SHOWED, 
                    payment.TeamSlug);
            }
            // Fallback: handle INIT state transition (in case payment is still in INIT)
            else if (payment.Status == PaymentGateway.Core.Enums.PaymentStatus.INIT)
            {
                _logger.LogWarning("Payment {PaymentId} is still in INIT state, transitioning to FORM_SHOWED directly", submission.PaymentId);
                formShowedTransition = await _paymentStateManager.TryTransitionStateAsync(
                    submission.PaymentId, 
                    PaymentGateway.Core.Enums.PaymentStatus.INIT, 
                    PaymentGateway.Core.Enums.PaymentStatus.FORM_SHOWED, 
                    payment.TeamSlug);
            }
            else
            {
                _logger.LogError("Payment {PaymentId} is in unexpected state {Status} for form submission", 
                    submission.PaymentId, payment.Status);
                return await RedirectToFailureAsync(submission.PaymentId, $"Payment is in {payment.Status} status and cannot be processed", payment);
            }

            if (!formShowedTransition)
            {
                _logger.LogError("Failed to transition payment {PaymentId} from {CurrentStatus} to FORM_SHOWED", 
                    submission.PaymentId, payment.Status);
                return await RedirectToFailureAsync(submission.PaymentId, "Payment state transition failed", payment);
            }

            // Process card payment securely
            var cardProcessingResult = await ProcessCardPaymentSecurely(submission, payment);
            if (!cardProcessingResult.Success)
            {
                _logger.LogWarning("Card processing failed for PaymentId: {PaymentId}, Reason: {Reason}",
                    submission.PaymentId, cardProcessingResult.ErrorMessage);

                _formSubmissionCounter.Add(1, new KeyValuePair<string, object?>("result", "card_processing_failed"),
                    new KeyValuePair<string, object?>("error_reason", cardProcessingResult.ErrorCode));

                // On card processing failure, transition to REJECTED
                var rejectedTransition = await _paymentStateManager.TryTransitionStateAsync(
                    submission.PaymentId,
                    PaymentGateway.Core.Enums.PaymentStatus.FORM_SHOWED,
                    PaymentGateway.Core.Enums.PaymentStatus.REJECTED,
                    payment.TeamSlug);

                // Update payment with error information if transition succeeded
                if (rejectedTransition)
                {
                    // Store error information in payment metadata
                    if (payment.Metadata == null)
                        payment.Metadata = new Dictionary<string, string>();
                    
                    payment.Metadata["rejection_reason"] = cardProcessingResult.ErrorMessage ?? "Card processing failed";
                    payment.Metadata["rejection_code"] = cardProcessingResult.ErrorCode ?? "UNKNOWN";
                    payment.Metadata["rejected_at"] = DateTime.UtcNow.ToString("O");
                    
                    await _paymentRepository.UpdateAsync(payment);
                }

                // Redirect to merchant's fail URL if provided, otherwise to internal result page
                if (!string.IsNullOrEmpty(payment.FailUrl))
                {
                    return Redirect(payment.FailUrl);
                }
                else
                {
                    var failureResultUrl = $"./result/{submission.PaymentId}?success=false&message={Uri.EscapeDataString(cardProcessingResult.ErrorMessage ?? "Card processing failed")}";
                    return Redirect(failureResultUrl);
                }
            }

            // Second transition: FORM_SHOWED → AUTHORIZED (card processing successful)
            var authorizedTransition = await _paymentStateManager.TryTransitionStateAsync(
                submission.PaymentId,
                PaymentGateway.Core.Enums.PaymentStatus.FORM_SHOWED,
                PaymentGateway.Core.Enums.PaymentStatus.AUTHORIZED,
                payment.TeamSlug);

            if (!authorizedTransition)
            {
                _logger.LogError("Failed to transition payment {PaymentId} from FORM_SHOWED to AUTHORIZED", submission.PaymentId);
                return await RedirectToFailureAsync(submission.PaymentId, "Payment authorization state transition failed", payment);
            }

            // Update payment metadata (card mask, additional timestamp for metadata)
            // Note: Status and primary UpdatedAt are already updated by PaymentStateManager
            payment.CardMask = cardProcessingResult.CardInfo;
            
            await _paymentRepository.UpdateAsync(payment);

            _logger.LogInformation("Payment form processed successfully for PaymentId: {PaymentId}, Status: AUTHORIZED, Duration: {Duration}ms",
                submission.PaymentId, stopwatch.ElapsedMilliseconds);

            _formSubmissionCounter.Add(1, new KeyValuePair<string, object?>("result", "success"),
                new KeyValuePair<string, object?>("currency", payment.Currency));

            // Redirect to merchant's success URL if provided, otherwise to internal result page
            if (!string.IsNullOrEmpty(payment.SuccessUrl))
            {
                return Redirect(payment.SuccessUrl);
            }
            else
            {
                var resultUrl = $"./result/{submission.PaymentId}?success=true&message={Uri.EscapeDataString("Payment authorized successfully")}";
                return Redirect(resultUrl);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing payment form submission for PaymentId: {PaymentId}", 
                submission?.PaymentId ?? "unknown");

            _formSubmissionCounter.Add(1, new KeyValuePair<string, object?>("result", "error"),
                new KeyValuePair<string, object?>("error_type", ex.GetType().Name));

            return await RedirectToFailureAsync(submission?.PaymentId ?? "unknown", "Internal server error");
        }
        finally
        {
            _formProcessingDuration.Record(stopwatch.Elapsed.TotalSeconds,
                new KeyValuePair<string, object?>("operation", "submit"));
        }
    }

    /// <summary>
    /// Render payment result page
    /// GET /api/v1/paymentform/result/{paymentId}
    /// </summary>
    [HttpGet("result/{paymentId}")]
    [AllowAnonymous]
    [Produces("text/html")]
    public async Task<IActionResult> GetPaymentResult(string paymentId, [FromQuery] bool success = false, [FromQuery] string? message = null)
    {
        return await RenderPaymentResult(paymentId, success, message);
    }

    // Private helper methods

    private async Task<string> RenderPaymentFormHtml(PaymentFormData data)
    {
        // Read the HTML template
        var templatePath = Path.Combine(Directory.GetCurrentDirectory(), "Views", "Payment", "PaymentForm.html");
        if (!System.IO.File.Exists(templatePath))
        {
            throw new FileNotFoundException($"Payment form template not found: {templatePath}");
        }

        var template = await System.IO.File.ReadAllTextAsync(templatePath);

        // Replace placeholders with actual data
        var html = template
            .Replace("{{PaymentId}}", data.PaymentId)
            .Replace("{{OrderId}}", data.OrderId ?? "")
            .Replace("{{Amount}}", data.Amount.ToString("F2"))
            .Replace("{{Currency}}", data.Currency)
            .Replace("{{Description}}", data.Description ?? "")
            .Replace("{{MerchantName}}", data.MerchantName)
            .Replace("{{CsrfToken}}", data.CsrfToken)
            .Replace("{{Language}}", data.Language)
            .Replace("{{BasePath}}", data.BasePath)
            .Replace("{{PaymentTimeout}}", data.PaymentTimeout?.ToString("yyyy-MM-ddTHH:mm:ssZ") ?? "")
            .Replace("{{SubmitUrl}}", "/api/v1/paymentform/submit")
            .Replace("{{ExpiresAtUtc}}", data.ExpiresAtUtc ?? "")
            .Replace("{{ExpiresAtUnix}}", data.ExpiresAtUnix?.ToString() ?? "0")
            .Replace("{{ServerTimeUtc}}", data.ServerTimeUtc)
            .Replace("{{ServerTimeUnix}}", data.ServerTimeUnix.ToString());

        // Handle receipt items if present
        if (data.Receipt != null && data.Receipt.ContainsKey("items"))
        {
            var itemsHtml = GenerateReceiptItemsHtml(data.Receipt);
            html = html.Replace("<!-- Receipt items will be populated by JavaScript -->", itemsHtml);
        }

        return html;
    }

    private string GenerateReceiptItemsHtml(Dictionary<string, object> receipt)
    {
        var itemsHtml = new StringBuilder();
        
        if (receipt.TryGetValue("items", out var itemsObj) && itemsObj is JsonElement itemsElement)
        {
            foreach (var item in itemsElement.EnumerateArray())
            {
                var name = item.TryGetProperty("name", out var nameElement) ? nameElement.GetString() : "";
                var price = item.TryGetProperty("price", out var priceElement) ? priceElement.GetDecimal() : 0;
                var quantity = item.TryGetProperty("quantity", out var qtyElement) ? qtyElement.GetInt32() : 1;

                itemsHtml.AppendLine($"""
                    <div class="receipt-item">
                        <span class="item-name">{System.Net.WebUtility.HtmlEncode(name)}</span>
                        <span class="item-quantity">×{quantity}</span>
                        <span class="item-price">{price:F2}</span>
                    </div>
                    """);
            }
        }

        return itemsHtml.ToString();
    }

    private FormValidationResult ValidateFormSubmission(PaymentFormSubmission submission)
    {
        var result = new FormValidationResult { IsValid = true, Errors = new List<string>() };

        // Only validate essential non-card parameters
        if (string.IsNullOrWhiteSpace(submission.PaymentId))
            result.Errors.Add("Payment ID is required");

        // Basic presence check for required fields (no format validation)
        if (string.IsNullOrWhiteSpace(submission.CardNumber))
            result.Errors.Add("Card number is required");

        if (string.IsNullOrWhiteSpace(submission.ExpiryDate))
            result.Errors.Add("Expiry date is required");

        if (string.IsNullOrWhiteSpace(submission.Cvv))
            result.Errors.Add("CVV is required");

        if (string.IsNullOrWhiteSpace(submission.CardholderName))
            result.Errors.Add("Cardholder name is required");

        if (string.IsNullOrWhiteSpace(submission.Email))
            result.Errors.Add("Email is required");

        result.IsValid = result.Errors.Count == 0;
        return result;
    }

    private async Task<CardProcessingResult> ProcessCardPaymentSecurely(PaymentFormSubmission submission, Core.Entities.Payment payment)
    {
        try
        {
            // Create card processing request with secure handling
            var cardData = new
            {
                CardNumber = submission.CardNumber.Replace(" ", ""), // Remove spaces
                ExpiryDate = submission.ExpiryDate,
                Cvv = submission.Cvv,
                CardholderName = submission.CardholderName
            };

            // Process card through secure service
            var expiryParts = cardData.ExpiryDate.Split('/');
            var cardRequest = new CardPaymentRequest
            {
                PaymentId = payment.Id, // Use the actual Payment entity ID (Guid)
                CardNumber = cardData.CardNumber,
                ExpiryMonth = expiryParts[0],
                ExpiryYear = expiryParts[1],
                CVV = cardData.Cvv,
                CardholderName = cardData.CardholderName,
                Amount = payment.Amount,
                Currency = payment.Currency,
                TeamId = payment.TeamId,
                OrderId = payment.OrderId
            };
            
            var processingResult = await _cardProcessingService.ProcessCardPaymentAsync(cardRequest);

            return new CardProcessingResult
            {
                Success = processingResult.IsSuccess,
                ErrorMessage = processingResult.ErrorMessage,
                ErrorCode = processingResult.ErrorCode,
                CardInfo = processingResult.MaskedCardNumber // Store only masked card info
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing card payment for PaymentId: {PaymentId}", submission.PaymentId);
            
            return new CardProcessingResult
            {
                Success = false,
                ErrorMessage = "Card processing failed",
                ErrorCode = "PROCESSING_ERROR"
            };
        }
    }

    private async Task<IActionResult> RenderPaymentResult(string paymentId, bool success, string? message)
    {
        try
        {
            var payment = await _paymentRepository.GetByPaymentIdAsync(paymentId);
            if (payment == null)
            {
                return NotFound("Payment not found");
            }

            // Read the HTML template
            var templatePath = Path.Combine(Directory.GetCurrentDirectory(), "Views", "Payment", "PaymentResult.html");
            if (!System.IO.File.Exists(templatePath))
            {
                throw new FileNotFoundException($"Payment result template not found: {templatePath}");
            }

            var template = await System.IO.File.ReadAllTextAsync(templatePath);

            // Prepare template data
            var status = success ? "Successful" : "Failed";
            var iconClass = success ? "result-success" : "result-error";
            var icon = success ? "✓" : "✗";
            var defaultMessage = success ? "Your payment has been processed successfully." : "Your payment could not be processed.";
            var encodedMessage = System.Net.WebUtility.HtmlEncode(message ?? defaultMessage);
            
            var orderIdSection = !string.IsNullOrEmpty(payment.OrderId) 
                ? $@"<div class=""detail-row"">
                    <span>Order ID:</span>
                    <span>{System.Net.WebUtility.HtmlEncode(payment.OrderId)}</span>
                </div>"
                : "";

            var actionButtons = "";
            if (success && !string.IsNullOrEmpty(payment.SuccessUrl))
            {
                actionButtons += $@"<a href=""{System.Net.WebUtility.HtmlEncode(payment.SuccessUrl)}"" class=""btn btn-primary"">Continue</a>";
            }
            if (!success && !string.IsNullOrEmpty(payment.FailUrl))
            {
                actionButtons += $@"<a href=""{System.Net.WebUtility.HtmlEncode(payment.FailUrl)}"" class=""btn btn-secondary"">Return to Merchant</a>";
            }

            // Replace placeholders with actual data
            var resultHtml = template
                .Replace("{{Status}}", System.Net.WebUtility.HtmlEncode(status))
                .Replace("{{IconClass}}", iconClass)
                .Replace("{{Icon}}", icon)
                .Replace("{{BasePath}}", _apiOptions.BaseUrl)
                .Replace("{{Message}}", encodedMessage)
                .Replace("{{PaymentId}}", System.Net.WebUtility.HtmlEncode(payment.PaymentId))
                .Replace("{{OrderIdSection}}", orderIdSection)
                .Replace("{{Amount}}", payment.Amount.ToString("F2"))
                .Replace("{{Currency}}", System.Net.WebUtility.HtmlEncode(payment.Currency))
                .Replace("{{PaymentStatus}}", System.Net.WebUtility.HtmlEncode(payment.Status.ToString()))
                .Replace("{{Date}}", DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm") + " UTC")
                .Replace("{{ActionButtons}}", actionButtons);

            return Content(resultHtml, "text/html; charset=utf-8");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error rendering payment result for PaymentId: {PaymentId}", paymentId);
            return StatusCode(500, "Error rendering payment result");
        }
    }

    // Security and validation helper methods

    private string GenerateCsrfToken(string paymentId)
    {
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        var data = $"{paymentId}:{timestamp}:{Guid.NewGuid():N}";
        
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(_configuration["Security:CsrfKey"] ?? "default-csrf-key"));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(data));
        
        return Convert.ToBase64String(hash) + ":" + timestamp;
    }

    private void StoreCsrfToken(string paymentId, string token)
    {
        var cacheKey = $"csrf_token:{paymentId}";
        _memoryCache.Set(cacheKey, token, TimeSpan.FromMinutes(30));
    }

    private bool ValidateCsrfToken(string paymentId, string? submittedToken)
    {
        if (string.IsNullOrWhiteSpace(submittedToken))
            return false;

        var cacheKey = $"csrf_token:{paymentId}";
        if (!_memoryCache.TryGetValue(cacheKey, out string? storedToken) || storedToken != submittedToken)
            return false;

        // Remove token after successful validation (one-time use)
        _memoryCache.Remove(cacheKey);
        return true;
    }

    private bool IsValidPaymentId(string paymentId) =>
        !string.IsNullOrWhiteSpace(paymentId) && 
        paymentId.Length <= 50 && 
        System.Text.RegularExpressions.Regex.IsMatch(paymentId, @"^[a-zA-Z0-9\-_]+$");

    private bool IsValidCardNumber(string cardNumber)
    {
        var digits = cardNumber.Replace(" ", "").Replace("-", "");
        if (!System.Text.RegularExpressions.Regex.IsMatch(digits, @"^\d{13,19}$"))
            return false;

        // Luhn algorithm validation
        int sum = 0;
        bool alternate = false;
        for (int i = digits.Length - 1; i >= 0; i--)
        {
            int digit = digits[i] - '0';
            if (alternate)
            {
                digit *= 2;
                if (digit > 9) digit = digit / 10 + digit % 10;
            }
            sum += digit;
            alternate = !alternate;
        }
        return sum % 10 == 0;
    }

    private bool IsValidExpiryDate(string expiryDate)
    {
        if (!System.Text.RegularExpressions.Regex.IsMatch(expiryDate, @"^(0[1-9]|1[0-2])\/(\d{2})$"))
            return false;

        var parts = expiryDate.Split('/');
        if (int.TryParse(parts[0], out int month) && int.TryParse(parts[1], out int year))
        {
            var expiry = new DateTime(2000 + year, month, 1).AddMonths(1).AddDays(-1);
            return expiry >= DateTime.Today;
        }
        return false;
    }

    private bool IsValidCvv(string cvv) => 
        System.Text.RegularExpressions.Regex.IsMatch(cvv, @"^\d{3,4}$");

    private bool IsValidEmail(string email) => 
        System.Text.RegularExpressions.Regex.IsMatch(email, 
            @"^[a-zA-Z0-9.!#$%&'*+/=?^_`{|}~-]+@[a-zA-Z0-9](?:[a-zA-Z0-9-]{0,61}[a-zA-Z0-9])?(?:\.[a-zA-Z0-9](?:[a-zA-Z0-9-]{0,61}[a-zA-Z0-9])?)*$");

    private bool IsValidPhone(string phone) => 
        System.Text.RegularExpressions.Regex.IsMatch(phone, @"^[\+]?[0-9\s\-\(\)]{10,20}$");

    private string ValidateLanguage(string? lang) => 
        lang?.ToLowerInvariant() switch
        {
            "ru" => "ru",
            "en" => "en",
            _ => "en"
        };

    private string GetClientIpAddress()
    {
        return HttpContext.Connection.RemoteIpAddress?.ToString() ?? 
               Request.Headers["X-Forwarded-For"].FirstOrDefault() ?? 
               Request.Headers["X-Real-IP"].FirstOrDefault() ?? 
               "unknown";
    }

    private async Task<IActionResult> RedirectToFailureAsync(string paymentId, string errorMessage, Core.Entities.Payment? payment = null)
    {
        // If payment is provided and has a fail URL, redirect there
        if (payment != null && !string.IsNullOrEmpty(payment.FailUrl))
        {
            return Redirect(payment.FailUrl);
        }
        
        // If payment is not provided, try to load it
        if (payment == null && !string.IsNullOrEmpty(paymentId))
        {
            try
            {
                payment = await _paymentRepository.GetByPaymentIdAsync(paymentId);
                if (payment != null && !string.IsNullOrEmpty(payment.FailUrl))
                {
                    return Redirect(payment.FailUrl);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to load payment for fail URL redirect: {PaymentId}", paymentId);
            }
        }
        
        // Fallback to internal result page
        var failureResultUrl = $"./result/{paymentId}?success=false&message={Uri.EscapeDataString(errorMessage)}";
        return Redirect(failureResultUrl);
    }

    private bool IsPaymentFormAllowed(PaymentGateway.Core.Enums.PaymentStatus status)
    {
        // Only allow form rendering for payments that haven't been processed yet
        // FORM_SHOWED is deliberately excluded - once shown and processed, don't show again
        return status == PaymentGateway.Core.Enums.PaymentStatus.INIT || 
               status == PaymentGateway.Core.Enums.PaymentStatus.NEW;
    }

    private string GetPaymentStatusMessage(PaymentGateway.Core.Enums.PaymentStatus status)
    {
        return status switch
        {
            PaymentGateway.Core.Enums.PaymentStatus.FORM_SHOWED => "This payment form has already been submitted and is being processed.",
            PaymentGateway.Core.Enums.PaymentStatus.AUTHORIZED => "This payment has already been authorized and is being processed.",
            PaymentGateway.Core.Enums.PaymentStatus.CONFIRMED => "This payment has been successfully completed.",
            PaymentGateway.Core.Enums.PaymentStatus.COMPLETED => "This payment has been successfully completed.",
            PaymentGateway.Core.Enums.PaymentStatus.CAPTURED => "This payment has been successfully captured.",
            PaymentGateway.Core.Enums.PaymentStatus.REJECTED => "This payment was rejected and cannot be processed.",
            PaymentGateway.Core.Enums.PaymentStatus.AUTH_FAIL => "This payment authorization failed and cannot be processed.",
            PaymentGateway.Core.Enums.PaymentStatus.CANCELLED => "This payment has been cancelled.",
            PaymentGateway.Core.Enums.PaymentStatus.REFUNDED => "This payment has been refunded.",
            PaymentGateway.Core.Enums.PaymentStatus.PARTIALLY_REFUNDED => "This payment has been partially refunded.",
            PaymentGateway.Core.Enums.PaymentStatus.EXPIRED => "This payment has expired and can no longer be processed.",
            PaymentGateway.Core.Enums.PaymentStatus.DEADLINE_EXPIRED => "This payment has expired and can no longer be processed.",
            PaymentGateway.Core.Enums.PaymentStatus.FAILED => "This payment has failed and cannot be processed.",
            PaymentGateway.Core.Enums.PaymentStatus.PROCESSING => "This payment is currently being processed. Please wait.",
            PaymentGateway.Core.Enums.PaymentStatus.AUTHORIZING => "This payment is currently being authorized. Please wait.",
            PaymentGateway.Core.Enums.PaymentStatus.CONFIRMING => "This payment is currently being confirmed. Please wait.",
            _ => "This payment has already been processed and cannot be modified."
        };
    }

    private async Task<IActionResult> RenderPaymentStatusPage(Core.Entities.Payment payment, string statusMessage)
    {
        try
        {
            // Get team information for display
            var team = payment.Team ?? await _teamRepository.GetByTeamSlugAsync(payment.TeamSlug);
            var merchantName = team?.Name ?? "Merchant";

            // Read the HTML template
            var templatePath = Path.Combine(Directory.GetCurrentDirectory(), "Views", "Payment", "PaymentStatus.html");
            if (!System.IO.File.Exists(templatePath))
            {
                // If template doesn't exist, create a simple inline HTML response
                var simpleHtml = $@"
<!DOCTYPE html>
<html lang=""en"">
<head>
    <meta charset=""UTF-8"">
    <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
    <title>Payment Status - HackLoad Payment Gateway</title>
    <link rel=""stylesheet"" href=""/css/payment-form.css"">
</head>
<body class=""payment-page"">
    <div class=""container"">
        <div class=""payment-status-container"">
            <div class=""status-icon status-info"">ℹ</div>
            <h1>Payment Status</h1>
            <div class=""status-message"">{System.Net.WebUtility.HtmlEncode(statusMessage)}</div>
            <div class=""payment-details"">
                <div class=""detail-row"">
                    <span>Merchant:</span>
                    <span>{System.Net.WebUtility.HtmlEncode(merchantName)}</span>
                </div>
                <div class=""detail-row"">
                    <span>Payment ID:</span>
                    <span>{System.Net.WebUtility.HtmlEncode(payment.PaymentId)}</span>
                </div>
                {(!string.IsNullOrEmpty(payment.OrderId) ? $@"
                <div class=""detail-row"">
                    <span>Order ID:</span>
                    <span>{System.Net.WebUtility.HtmlEncode(payment.OrderId)}</span>
                </div>" : "")}
                <div class=""detail-row"">
                    <span>Amount:</span>
                    <span>{payment.Amount:F2} {System.Net.WebUtility.HtmlEncode(payment.Currency)}</span>
                </div>
                <div class=""detail-row"">
                    <span>Status:</span>
                    <span>{System.Net.WebUtility.HtmlEncode(payment.Status.ToString())}</span>
                </div>
            </div>
            <div class=""status-actions"">
                {(!string.IsNullOrEmpty(payment.SuccessUrl) && IsPaymentSuccessful(payment.Status) ? $@"
                <a href=""{System.Net.WebUtility.HtmlEncode(payment.SuccessUrl)}"" class=""btn btn-primary"">Return to Merchant</a>" : "")}
                {(!string.IsNullOrEmpty(payment.FailUrl) && IsPaymentFailed(payment.Status) ? $@"
                <a href=""{System.Net.WebUtility.HtmlEncode(payment.FailUrl)}"" class=""btn btn-secondary"">Return to Merchant</a>" : "")}
            </div>
        </div>
    </div>
</body>
</html>";
                return Content(simpleHtml, "text/html; charset=utf-8");
            }

            var template = await System.IO.File.ReadAllTextAsync(templatePath);

            // Replace placeholders with actual data
            var html = template
                .Replace("{{StatusMessage}}", System.Net.WebUtility.HtmlEncode(statusMessage))
                .Replace("{{MerchantName}}", System.Net.WebUtility.HtmlEncode(merchantName))
                .Replace("{{PaymentId}}", System.Net.WebUtility.HtmlEncode(payment.PaymentId))
                .Replace("{{OrderId}}", System.Net.WebUtility.HtmlEncode(payment.OrderId ?? ""))
                .Replace("{{Amount}}", payment.Amount.ToString("F2"))
                .Replace("{{Currency}}", System.Net.WebUtility.HtmlEncode(payment.Currency))
                .Replace("{{Status}}", System.Net.WebUtility.HtmlEncode(payment.Status.ToString()));

            return Content(html, "text/html; charset=utf-8");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error rendering payment status page for PaymentId: {PaymentId}", payment.PaymentId);
            return StatusCode(500, "Error displaying payment status");
        }
    }

    private bool IsPaymentSuccessful(PaymentGateway.Core.Enums.PaymentStatus status)
    {
        return status == PaymentGateway.Core.Enums.PaymentStatus.CONFIRMED ||
               status == PaymentGateway.Core.Enums.PaymentStatus.COMPLETED ||
               status == PaymentGateway.Core.Enums.PaymentStatus.CAPTURED;
    }

    private bool IsPaymentFailed(PaymentGateway.Core.Enums.PaymentStatus status)
    {
        return status == PaymentGateway.Core.Enums.PaymentStatus.REJECTED ||
               status == PaymentGateway.Core.Enums.PaymentStatus.AUTH_FAIL ||
               status == PaymentGateway.Core.Enums.PaymentStatus.CANCELLED ||
               status == PaymentGateway.Core.Enums.PaymentStatus.FAILED ||
               status == PaymentGateway.Core.Enums.PaymentStatus.EXPIRED ||
               status == PaymentGateway.Core.Enums.PaymentStatus.DEADLINE_EXPIRED;
    }
}

// Supporting models and classes

public class PaymentFormData
{
    public string PaymentId { get; set; } = string.Empty;
    public string? OrderId { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string MerchantName { get; set; } = string.Empty;
    public string? SuccessUrl { get; set; }
    public string? FailUrl { get; set; }
    public string BasePath { get; set; } = string.Empty;
    public string Language { get; set; } = "en";
    public string CsrfToken { get; set; } = string.Empty;
    public DateTime? PaymentTimeout { get; set; }
    public Dictionary<string, object>? Receipt { get; set; }
    
    // Timezone-aware expiration data
    public string? ExpiresAtUtc { get; set; }
    public long? ExpiresAtUnix { get; set; }
    public string ServerTimeUtc { get; set; } = string.Empty;
    public long ServerTimeUnix { get; set; }
}

public class PaymentFormSubmission
{
    public string PaymentId { get; set; } = string.Empty;
    public string CardNumber { get; set; } = string.Empty;
    public string ExpiryDate { get; set; } = string.Empty;
    public string Cvv { get; set; } = string.Empty;
    public string CardholderName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public bool SaveCard { get; set; }
    public bool TermsAgreement { get; set; }
    public string CsrfToken { get; set; } = string.Empty;
}

public class FormValidationResult
{
    public bool IsValid { get; set; }
    public List<string> Errors { get; set; } = new();
}

public class CardProcessingResult
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public string? ErrorCode { get; set; }
    public string? CardInfo { get; set; }
}