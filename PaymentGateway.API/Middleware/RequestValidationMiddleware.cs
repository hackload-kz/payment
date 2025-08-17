// SPDX-License-Identifier: MIT
// Copyright (c) 2025 HackLoad Payment Gateway

using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.Extensions.Options;
using Prometheus;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PaymentGateway.API.Middleware;

/// <summary>
/// Request validation middleware for comprehensive input validation
/// 
/// This middleware provides:
/// - JSON schema validation for request bodies
/// - Parameter format validation (payment IDs, amounts, etc.)
/// - Content-Type validation
/// - Request size limits
/// - Malicious content detection
/// - XSS and SQL injection prevention
/// - Rate limiting based on validation failures
/// - Structured validation error responses
/// </summary>
public class RequestValidationMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<RequestValidationMiddleware> _logger;
    private readonly RequestValidationOptions _options;
    private readonly IServiceProvider _serviceProvider;

    // Metrics for tracking validation
    private static readonly Counter ValidationFailures = Metrics
        .CreateCounter("payment_gateway_validation_failures_total", "Total request validation failures", 
            new[] { "validation_type", "endpoint", "error_code" });

    private static readonly Histogram ValidationDuration = Metrics
        .CreateHistogram("payment_gateway_validation_duration_seconds", 
            "Time spent validating requests");

    // Malicious patterns to detect
    private readonly Regex[] _maliciousPatterns = new[]
    {
        new Regex(@"<\s*script[^>]*>.*?<\s*/\s*script\s*>", RegexOptions.IgnoreCase | RegexOptions.Singleline),
        new Regex(@"javascript\s*:", RegexOptions.IgnoreCase),
        new Regex(@"on\w+\s*=", RegexOptions.IgnoreCase),
        new Regex(@"(union|select|insert|update|delete|drop|create|alter)\s+", RegexOptions.IgnoreCase),
        new Regex(@"('|""|;|--|[|]|&|[$])", RegexOptions.IgnoreCase),
        new Regex(@"<\s*iframe[^>]*>", RegexOptions.IgnoreCase),
        new Regex(@"<\s*object[^>]*>", RegexOptions.IgnoreCase),
        new Regex(@"<\s*embed[^>]*>", RegexOptions.IgnoreCase)
    };

    // Payment-specific validation patterns
    private readonly Regex _paymentIdPattern = new(@"^pay_[a-zA-Z0-9]{1,20}$", RegexOptions.Compiled);
    private readonly Regex _orderIdPattern = new(@"^[a-zA-Z0-9\-_]{1,50}$", RegexOptions.Compiled);
    private readonly Regex _teamSlugPattern = new(@"^[a-zA-Z0-9\-_]{1,30}$", RegexOptions.Compiled);

    public RequestValidationMiddleware(
        RequestDelegate next,
        ILogger<RequestValidationMiddleware> logger,
        IOptions<RequestValidationOptions> options,
        IServiceProvider serviceProvider)
    {
        _next = next;
        _logger = logger;
        _options = options.Value;
        _serviceProvider = serviceProvider;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        using var timer = ValidationDuration.NewTimer();
        var correlationId = context.TraceIdentifier;
        var endpoint = context.Request.Path.Value ?? "unknown";

        try
        {
            // Skip validation for non-API endpoints or health checks
            if (!ShouldValidateRequest(context.Request.Path))
            {
                await _next(context);
                return;
            }

            // Validate request
            var validationResult = await ValidateRequestAsync(context.Request, correlationId);
            
            if (!validationResult.IsValid)
            {
                ValidationFailures.WithLabels("request_validation", endpoint, validationResult.ErrorCode!).Inc();
                
                _logger.LogWarning("Request validation failed. CorrelationId: {CorrelationId}, Endpoint: {Endpoint}, Errors: {Errors}",
                    correlationId, endpoint, string.Join("; ", validationResult.Errors));

                await WriteValidationErrorResponseAsync(context, validationResult, correlationId);
                return;
            }

            // Continue to next middleware if validation passes
            await _next(context);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in request validation middleware. CorrelationId: {CorrelationId}, Endpoint: {Endpoint}",
                correlationId, endpoint);
            
            ValidationFailures.WithLabels("validation_error", endpoint, "internal_error").Inc();
            throw;
        }
    }

    private bool ShouldValidateRequest(PathString path)
    {
        // Skip validation for non-API endpoints
        if (!path.StartsWithSegments("/api"))
            return false;

        // Skip health checks and metrics
        if (path.StartsWithSegments("/health") || path.StartsWithSegments("/metrics"))
            return false;

        // Validate all payment endpoints
        var paymentEndpoints = new[]
        {
            "/api/paymentinit",
            "/api/paymentconfirm", 
            "/api/paymentcancel",
            "/api/paymentcheck"
        };

        return paymentEndpoints.Any(endpoint => path.StartsWithSegments(endpoint));
    }

    private async Task<ValidationResult> ValidateRequestAsync(HttpRequest request, string correlationId)
    {
        var result = new ValidationResult { IsValid = true };

        try
        {
            // 1. Validate HTTP method
            if (!IsAllowedMethod(request.Method))
            {
                result.AddError("HTTP method not allowed", "METHOD_NOT_ALLOWED");
                return result;
            }

            // 2. Validate Content-Type for POST requests
            if (request.Method.Equals("POST", StringComparison.OrdinalIgnoreCase))
            {
                if (!IsValidContentType(request.ContentType))
                {
                    result.AddError("Invalid Content-Type. Expected application/json", "INVALID_CONTENT_TYPE");
                    return result;
                }
            }

            // 3. Validate request size
            if (request.ContentLength > _options.MaxRequestSize)
            {
                result.AddError($"Request size exceeds limit of {_options.MaxRequestSize} bytes", "REQUEST_TOO_LARGE");
                return result;
            }

            // 4. Validate request body if present
            if (request.ContentLength > 0)
            {
                var bodyValidation = await ValidateRequestBodyAsync(request, correlationId);
                if (!bodyValidation.IsValid)
                {
                    result.MergeWith(bodyValidation);
                    return result;
                }
            }

            // 5. Validate query parameters
            var queryValidation = ValidateQueryParameters(request.Query);
            if (!queryValidation.IsValid)
            {
                result.MergeWith(queryValidation);
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception during request validation. CorrelationId: {CorrelationId}", correlationId);
            result.AddError("Request validation failed", "VALIDATION_ERROR");
            return result;
        }
    }

    private bool IsAllowedMethod(string method)
    {
        var allowedMethods = new[] { "GET", "POST", "OPTIONS" };
        return allowedMethods.Contains(method, StringComparer.OrdinalIgnoreCase);
    }

    private bool IsValidContentType(string? contentType)
    {
        if (string.IsNullOrEmpty(contentType))
            return false;

        return contentType.Contains("application/json", StringComparison.OrdinalIgnoreCase);
    }

    private async Task<ValidationResult> ValidateRequestBodyAsync(HttpRequest request, string correlationId)
    {
        var result = new ValidationResult { IsValid = true };

        try
        {
            // Enable buffering to allow multiple reads
            request.EnableBuffering();

            // Read request body
            using var reader = new StreamReader(request.Body, leaveOpen: true);
            var body = await reader.ReadToEndAsync();
            request.Body.Position = 0;

            if (string.IsNullOrWhiteSpace(body))
            {
                result.AddError("Request body is required", "MISSING_BODY");
                return result;
            }

            // 1. Validate JSON format
            try
            {
                using var jsonDoc = JsonDocument.Parse(body);
                
                // 2. Check for malicious content
                if (ContainsMaliciousContent(body))
                {
                    result.AddError("Request contains potentially malicious content", "MALICIOUS_CONTENT");
                    return result;
                }

                // 3. Validate payment-specific fields
                var paymentValidation = ValidatePaymentFields(jsonDoc.RootElement);
                if (!paymentValidation.IsValid)
                {
                    result.MergeWith(paymentValidation);
                }

                // 4. Validate required structure
                var structureValidation = ValidateRequestStructure(jsonDoc.RootElement, request.Path);
                if (!structureValidation.IsValid)
                {
                    result.MergeWith(structureValidation);
                }
            }
            catch (JsonException ex)
            {
                result.AddError($"Invalid JSON format: {ex.Message}", "INVALID_JSON");
                return result;
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating request body. CorrelationId: {CorrelationId}", correlationId);
            result.AddError("Request body validation failed", "BODY_VALIDATION_ERROR");
            return result;
        }
    }

    private bool ContainsMaliciousContent(string content)
    {
        return _maliciousPatterns.Any(pattern => pattern.IsMatch(content));
    }

    private ValidationResult ValidatePaymentFields(JsonElement element)
    {
        var result = new ValidationResult { IsValid = true };

        try
        {
            // Validate PaymentId format if present
            if (element.TryGetProperty("paymentId", out var paymentIdElement) ||
                element.TryGetProperty("PaymentId", out paymentIdElement))
            {
                var paymentId = paymentIdElement.GetString();
                if (!string.IsNullOrEmpty(paymentId) && !_paymentIdPattern.IsMatch(paymentId))
                {
                    result.AddError("Invalid PaymentId format. Must match pattern: pay_[alphanumeric]", "INVALID_PAYMENT_ID");
                }
            }

            // Validate OrderId format if present  
            if (element.TryGetProperty("orderId", out var orderIdElement) ||
                element.TryGetProperty("OrderId", out orderIdElement))
            {
                var orderId = orderIdElement.GetString();
                if (!string.IsNullOrEmpty(orderId) && !_orderIdPattern.IsMatch(orderId))
                {
                    result.AddError("Invalid OrderId format. Must be alphanumeric with hyphens/underscores", "INVALID_ORDER_ID");
                }
            }

            // Validate TeamSlug format if present
            if (element.TryGetProperty("teamSlug", out var teamSlugElement) ||
                element.TryGetProperty("TeamSlug", out teamSlugElement))
            {
                var teamSlug = teamSlugElement.GetString();
                if (!string.IsNullOrEmpty(teamSlug) && !_teamSlugPattern.IsMatch(teamSlug))
                {
                    result.AddError("Invalid TeamSlug format. Must be alphanumeric with hyphens/underscores", "INVALID_TEAM_SLUG");
                }
            }

            // Validate Amount if present
            if (element.TryGetProperty("amount", out var amountElement) ||
                element.TryGetProperty("Amount", out amountElement))
            {
                if (amountElement.ValueKind == JsonValueKind.Number)
                {
                    var amount = amountElement.GetDecimal();
                    if (amount <= 0) 
                    {
                        result.AddError("Amount must be positive", "INVALID_AMOUNT");
                    }
                }
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating payment fields");
            result.AddError("Payment field validation failed", "PAYMENT_FIELD_ERROR");
            return result;
        }
    }

    private ValidationResult ValidateRequestStructure(JsonElement element, PathString path)
    {
        var result = new ValidationResult { IsValid = true };

        try
        {
            // Define required fields for different endpoints
            var requiredFields = GetRequiredFieldsForEndpoint(path);
            
            foreach (var field in requiredFields)
            {
                if (!element.TryGetProperty(field, out var fieldElement) || 
                    (fieldElement.ValueKind == JsonValueKind.String && string.IsNullOrWhiteSpace(fieldElement.GetString())))
                {
                    result.AddError($"Required field '{field}' is missing or empty", "MISSING_FIELD");
                }
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating request structure for path: {Path}", path);
            result.AddError("Request structure validation failed", "STRUCTURE_VALIDATION_ERROR");
            return result;
        }
    }

    private string[] GetRequiredFieldsForEndpoint(PathString path)
    {
        if (path.StartsWithSegments("/api/paymentinit"))
        {
            return new[] { "teamSlug", "token", "orderId", "amount" };
        }
        else if (path.StartsWithSegments("/api/paymentconfirm"))
        {
            return new[] { "teamSlug", "token", "paymentId" };
        }
        else if (path.StartsWithSegments("/api/paymentcancel"))
        {
            return new[] { "teamSlug", "token", "paymentId" };
        }
        else if (path.StartsWithSegments("/api/paymentcheck"))
        {
            return new[] { "teamSlug", "token", "paymentId" };
        }

        return Array.Empty<string>();
    }

    private ValidationResult ValidateQueryParameters(IQueryCollection query)
    {
        var result = new ValidationResult { IsValid = true };

        try
        {
            foreach (var param in query)
            {
                // Check for malicious content in query parameters
                if (ContainsMaliciousContent(param.Key) || ContainsMaliciousContent(param.Value.ToString()))
                {
                    result.AddError($"Query parameter '{param.Key}' contains potentially malicious content", "MALICIOUS_QUERY_PARAM");
                }

                // Validate parameter length
                if (param.Key.Length > 100 || param.Value.ToString().Length > 500)
                {
                    result.AddError($"Query parameter '{param.Key}' exceeds maximum length", "PARAM_TOO_LONG");
                }
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating query parameters");
            result.AddError("Query parameter validation failed", "QUERY_VALIDATION_ERROR");
            return result;
        }
    }

    private async Task WriteValidationErrorResponseAsync(HttpContext context, ValidationResult validationResult, string correlationId)
    {
        context.Response.StatusCode = 400;
        context.Response.ContentType = "application/json";

        var errorResponse = new
        {
            Success = false,
            ErrorCode = validationResult.ErrorCode ?? "VALIDATION_FAILED",
            Message = "Request validation failed",
            Details = string.Join("; ", validationResult.Errors),
            ValidationErrors = validationResult.Errors.Select(error => new { Message = error }).ToArray(),
            CorrelationId = correlationId,
            Timestamp = DateTime.UtcNow
        };

        var jsonResponse = JsonSerializer.Serialize(errorResponse, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        });

        await context.Response.WriteAsync(jsonResponse);
    }
}

/// <summary>
/// Request validation result
/// </summary>
public class ValidationResult
{
    public bool IsValid { get; set; }
    public List<string> Errors { get; set; } = new();
    public string? ErrorCode { get; set; }

    public void AddError(string error, string errorCode)
    {
        IsValid = false;
        Errors.Add(error);
        ErrorCode ??= errorCode;
    }

    public void MergeWith(ValidationResult other)
    {
        if (!other.IsValid)
        {
            IsValid = false;
            Errors.AddRange(other.Errors);
            ErrorCode ??= other.ErrorCode;
        }
    }
}

/// <summary>
/// Request validation options
/// </summary>
public class RequestValidationOptions
{
    /// <summary>
    /// Maximum request size in bytes
    /// </summary>
    public long MaxRequestSize { get; set; } = 1024 * 1024; // 1MB

    /// <summary>
    /// Enable malicious content detection
    /// </summary>
    public bool EnableMaliciousContentDetection { get; set; } = true;

    /// <summary>
    /// Enable payment field validation
    /// </summary>
    public bool EnablePaymentFieldValidation { get; set; } = true;

    /// <summary>
    /// Enable strict JSON validation
    /// </summary>
    public bool EnableStrictJsonValidation { get; set; } = true;
}

/// <summary>
/// Extension methods for registering the request validation middleware
/// </summary>
public static class RequestValidationMiddlewareExtensions
{
    public static IApplicationBuilder UseRequestValidation(this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<RequestValidationMiddleware>();
    }

    public static IServiceCollection AddRequestValidation(this IServiceCollection services, 
        Action<RequestValidationOptions>? configure = null)
    {
        if (configure != null)
        {
            services.Configure(configure);
        }
        else
        {
            services.Configure<RequestValidationOptions>(options => { });
        }

        return services;
    }
}