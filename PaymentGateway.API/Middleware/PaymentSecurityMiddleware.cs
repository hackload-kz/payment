// SPDX-License-Identifier: MIT
// Copyright (c) 2025 HackLoad Payment Gateway

using Microsoft.Extensions.Primitives;
using Microsoft.Extensions.Caching.Memory;
using System.Net;
using System.Text.Json;
using System.Security.Cryptography;
using System.Text;

namespace PaymentGateway.API.Middleware;

/// <summary>
/// Payment Security Middleware for enforcing security policies on payment-related endpoints
/// 
/// This middleware implements:
/// - HTTPS enforcement for payment pages
/// - Content Security Policy (CSP) headers
/// - Security headers (HSTS, X-Frame-Options, etc.)
/// - Request validation and sanitization
/// - Rate limiting for payment endpoints
/// - Form tampering detection
/// - Security audit logging
/// </summary>
public class PaymentSecurityMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<PaymentSecurityMiddleware> _logger;
    private readonly IConfiguration _configuration;
    private readonly IMemoryCache _memoryCache;

    // Security configuration
    private readonly bool _enforceHttps;
    private readonly bool _enableCsp;
    private readonly bool _enableHsts;
    private readonly bool _enableAuditLogging;
    private readonly int _maxRequestsPerMinute;
    private readonly string _cspPolicy;

    // Payment-related URL patterns that require enhanced security
    private readonly string[] _paymentPaths = new[]
    {
        "/api/v1/paymentform/",
        "/api/v1/payment/",
        "/payment/",
        "/checkout/"
    };

    // Security headers to add to payment responses
    private readonly Dictionary<string, string> _securityHeaders;

    public PaymentSecurityMiddleware(
        RequestDelegate next,
        ILogger<PaymentSecurityMiddleware> logger,
        IConfiguration configuration,
        IMemoryCache memoryCache)
    {
        _next = next;
        _logger = logger;
        _configuration = configuration;
        _memoryCache = memoryCache;

        // Load security configuration
        _enforceHttps = _configuration.GetValue<bool>("Security:EnforceHttps", true);
        _enableCsp = _configuration.GetValue<bool>("Security:EnableCSP", true);
        _enableHsts = _configuration.GetValue<bool>("Security:EnableHSTS", true);
        _enableAuditLogging = _configuration.GetValue<bool>("Security:EnableAuditLogging", true);
        _maxRequestsPerMinute = _configuration.GetValue<int>("Security:MaxRequestsPerMinute", 60);

        // Configure Content Security Policy
        _cspPolicy = _configuration.GetValue<string>("Security:CSPPolicy") ?? 
            "default-src 'self'; " +
            "script-src 'self' 'unsafe-inline' https://js.stripe.com https://checkout.stripe.com; " +
            "style-src 'self' 'unsafe-inline'; " +
            "img-src 'self' data: https:; " +
            "font-src 'self' https://fonts.gstatic.com; " +
            "connect-src 'self' https://api.stripe.com; " +
            "frame-src https://checkout.stripe.com https://hooks.stripe.com; " +
            "form-action 'self'; " +
            "frame-ancestors 'none'; " +
            "base-uri 'self'; " +
            "object-src 'none';";

        // Initialize security headers
        _securityHeaders = new Dictionary<string, string>
        {
            { "X-Content-Type-Options", "nosniff" },
            { "X-Frame-Options", "DENY" },
            { "X-XSS-Protection", "1; mode=block" },
            { "Referrer-Policy", "strict-origin-when-cross-origin" },
            { "Permissions-Policy", "payment=(), microphone=(), camera=(), geolocation=()" },
            { "Cache-Control", "no-cache, no-store, must-revalidate, private" },
            { "Pragma", "no-cache" },
            { "Expires", "0" }
        };

        if (_enableHsts && _enforceHttps)
        {
            _securityHeaders["Strict-Transport-Security"] = "max-age=31536000; includeSubDomains; preload";
        }

        if (_enableCsp)
        {
            _securityHeaders["Content-Security-Policy"] = _cspPolicy;
        }
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value?.ToLowerInvariant() ?? "";
        var isPaymentEndpoint = _paymentPaths.Any(p => path.StartsWith(p));

        if (isPaymentEndpoint)
        {
            // Apply payment-specific security measures
            await ApplyPaymentSecurity(context);
        }

        // Continue with the request pipeline
        await _next(context);

        // Add security headers to response
        if (isPaymentEndpoint)
        {
            AddSecurityHeaders(context);
        }
    }

    private async Task ApplyPaymentSecurity(HttpContext context)
    {
        var clientIp = GetClientIpAddress(context);
        var userAgent = context.Request.Headers["User-Agent"].FirstOrDefault() ?? "Unknown";
        var requestId = Guid.NewGuid().ToString("N")[..8];

        // 1. HTTPS Enforcement
        if (_enforceHttps && !context.Request.IsHttps)
        {
            _logger.LogWarning("HTTPS enforcement: Blocking non-HTTPS request to payment endpoint. IP: {ClientIp}, Path: {Path}, RequestId: {RequestId}",
                clientIp, context.Request.Path, requestId);

            await LogSecurityEvent(context, "HTTPS_VIOLATION", "Non-HTTPS request to payment endpoint blocked", clientIp, requestId);

            context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
            await context.Response.WriteAsync(JsonSerializer.Serialize(new
            {
                error = "HTTPS required for payment processing",
                code = "HTTPS_REQUIRED",
                requestId = requestId
            }));
            return;
        }

        // 2. Rate Limiting
        if (!await CheckRateLimit(clientIp, context.Request.Path))
        {
            _logger.LogWarning("Rate limit exceeded for payment endpoint. IP: {ClientIp}, Path: {Path}, RequestId: {RequestId}",
                clientIp, context.Request.Path, requestId);

            await LogSecurityEvent(context, "RATE_LIMIT_EXCEEDED", "Rate limit exceeded for payment endpoint", clientIp, requestId);

            context.Response.StatusCode = (int)HttpStatusCode.TooManyRequests;
            context.Response.Headers["Retry-After"] = "60";
            await context.Response.WriteAsync(JsonSerializer.Serialize(new
            {
                error = "Too many requests. Please try again later.",
                code = "RATE_LIMIT_EXCEEDED",
                requestId = requestId
            }));
            return;
        }

        // 3. Request Validation and Sanitization
        await ValidateAndSanitizeRequest(context, requestId);

        // 4. Form Tampering Detection (for POST requests)
        if (context.Request.Method == "POST")
        {
            await DetectFormTampering(context, requestId);
        }

        // 5. Security Audit Logging
        if (_enableAuditLogging)
        {
            await LogSecurityEvent(context, "PAYMENT_REQUEST", "Payment endpoint accessed", clientIp, requestId);
        }
    }

    private async Task ValidateAndSanitizeRequest(HttpContext context, string requestId)
    {
        var clientIp = GetClientIpAddress(context);

        // Check for suspicious patterns in URL
        var path = context.Request.Path.Value ?? "";
        var query = context.Request.QueryString.Value ?? "";
        
        var suspiciousPatterns = new[]
        {
            "<script", "javascript:", "data:", "vbscript:", "onload=", "onerror=",
            "eval(", "alert(", "document.cookie", "window.location",
            "../", "..\\", "/etc/passwd", "/windows/system32",
            "union select", "drop table", "insert into", "delete from",
            "exec(", "system(", "cmd.exe", "powershell"
        };

        var fullUrl = path + query;
        foreach (var pattern in suspiciousPatterns)
        {
            if (fullUrl.Contains(pattern, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("Suspicious pattern detected in request URL: {Pattern}, IP: {ClientIp}, URL: {Url}, RequestId: {RequestId}",
                    pattern, clientIp, fullUrl, requestId);

                await LogSecurityEvent(context, "SUSPICIOUS_REQUEST", $"Suspicious pattern detected: {pattern}", clientIp, requestId);

                context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
                await context.Response.WriteAsync(JsonSerializer.Serialize(new
                {
                    error = "Invalid request format",
                    code = "INVALID_REQUEST",
                    requestId = requestId
                }));
                return;
            }
        }

        // Validate headers for suspicious content
        foreach (var header in context.Request.Headers)
        {
            var headerValue = header.Value.ToString();
            foreach (var pattern in suspiciousPatterns)
            {
                if (headerValue.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogWarning("Suspicious pattern detected in request header: {Header}={Value}, Pattern: {Pattern}, IP: {ClientIp}, RequestId: {RequestId}",
                        header.Key, headerValue, pattern, clientIp, requestId);

                    await LogSecurityEvent(context, "SUSPICIOUS_HEADER", $"Suspicious header content: {header.Key}", clientIp, requestId);
                    break;
                }
            }
        }

        // Check User-Agent for suspicious patterns
        var userAgent = context.Request.Headers["User-Agent"].FirstOrDefault() ?? "";
        var suspiciousUserAgents = new[]
        {
            "sqlmap", "nikto", "nmap", "masscan", "nessus", "openvas",
            "burpsuite", "owasp", "w3af", "skipfish", "webscarab",
            "curl", "wget", "python-requests", "postman"
        };

        foreach (var suspiciousAgent in suspiciousUserAgents)
        {
            if (userAgent.Contains(suspiciousAgent, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("Suspicious User-Agent detected: {UserAgent}, IP: {ClientIp}, RequestId: {RequestId}",
                    userAgent, clientIp, requestId);

                await LogSecurityEvent(context, "SUSPICIOUS_USER_AGENT", $"Suspicious User-Agent: {userAgent}", clientIp, requestId);
                break;
            }
        }
    }

    private async Task DetectFormTampering(HttpContext context, string requestId)
    {
        var clientIp = GetClientIpAddress(context);

        // Check for form tampering indicators
        if (context.Request.HasFormContentType)
        {
            try
            {
                var form = await context.Request.ReadFormAsync();
                
                // Check for unexpected form fields
                var expectedFields = new[]
                {
                    "PaymentId", "CardNumber", "ExpiryDate", "Cvv", "CardholderName",
                    "Email", "Phone", "SaveCard", "TermsAgreement", "CsrfToken"
                };

                var unexpectedFields = form.Keys.Except(expectedFields, StringComparer.OrdinalIgnoreCase).ToList();
                if (unexpectedFields.Any())
                {
                    _logger.LogWarning("Unexpected form fields detected: {UnexpectedFields}, IP: {ClientIp}, RequestId: {RequestId}",
                        string.Join(", ", unexpectedFields), clientIp, requestId);

                    await LogSecurityEvent(context, "FORM_TAMPERING", $"Unexpected form fields: {string.Join(", ", unexpectedFields)}", clientIp, requestId);
                }

                // Check for suspicious field values
                foreach (var field in form)
                {
                    var fieldValue = field.Value.ToString();
                    
                    // Check field length limits
                    if (fieldValue.Length > 1000) // Reasonable max length for payment form fields
                    {
                        _logger.LogWarning("Suspiciously long form field value: {FieldName}={Length} characters, IP: {ClientIp}, RequestId: {RequestId}",
                            field.Key, fieldValue.Length, clientIp, requestId);

                        await LogSecurityEvent(context, "FORM_TAMPERING", $"Oversized form field: {field.Key}", clientIp, requestId);
                    }

                    // Check for script injection attempts
                    if (fieldValue.Contains("<script", StringComparison.OrdinalIgnoreCase) ||
                        fieldValue.Contains("javascript:", StringComparison.OrdinalIgnoreCase))
                    {
                        _logger.LogWarning("Script injection attempt detected in form field: {FieldName}, IP: {ClientIp}, RequestId: {RequestId}",
                            field.Key, clientIp, requestId);

                        await LogSecurityEvent(context, "SCRIPT_INJECTION", $"Script injection in form field: {field.Key}", clientIp, requestId);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error reading form data for tampering detection. IP: {ClientIp}, RequestId: {RequestId}",
                    clientIp, requestId);
            }
        }
    }

    private async Task<bool> CheckRateLimit(string clientIp, string path)
    {
        var rateLimitKey = $"rate_limit:{clientIp}:{path}";
        var currentCount = _memoryCache.Get<int>(rateLimitKey);

        if (currentCount >= _maxRequestsPerMinute)
        {
            return false;
        }

        var newCount = currentCount + 1;
        var expiry = TimeSpan.FromMinutes(1);
        _memoryCache.Set(rateLimitKey, newCount, expiry);

        return true;
    }

    private void AddSecurityHeaders(HttpContext context)
    {
        foreach (var header in _securityHeaders)
        {
            if (!context.Response.Headers.ContainsKey(header.Key))
            {
                context.Response.Headers[header.Key] = header.Value;
            }
        }

        // Add CSP Report-To header if configured
        var cspReportEndpoint = _configuration.GetValue<string>("Security:CSPReportEndpoint");
        if (!string.IsNullOrEmpty(cspReportEndpoint))
        {
            context.Response.Headers["Report-To"] = JsonSerializer.Serialize(new
            {
                group = "csp-endpoint",
                max_age = 10886400,
                endpoints = new[] { new { url = cspReportEndpoint } }
            });
        }
    }

    private async Task LogSecurityEvent(HttpContext context, string eventType, string description, string clientIp, string requestId)
    {
        if (!_enableAuditLogging) return;

        var securityEvent = new
        {
            Timestamp = DateTime.UtcNow,
            RequestId = requestId,
            EventType = eventType,
            Description = description,
            ClientIp = clientIp,
            UserAgent = context.Request.Headers["User-Agent"].FirstOrDefault() ?? "Unknown",
            Path = context.Request.Path.Value,
            Method = context.Request.Method,
            Headers = context.Request.Headers.Where(h => 
                !h.Key.Equals("Authorization", StringComparison.OrdinalIgnoreCase) &&
                !h.Key.Equals("Cookie", StringComparison.OrdinalIgnoreCase))
                .ToDictionary(h => h.Key, h => h.Value.ToString()),
            QueryString = context.Request.QueryString.Value,
            Protocol = context.Request.Protocol,
            IsHttps = context.Request.IsHttps,
            Host = context.Request.Host.ToString()
        };

        _logger.LogInformation("Security Event: {SecurityEvent}", JsonSerializer.Serialize(securityEvent));

        // Store security events in memory cache for analysis (optional)
        var securityEventsKey = $"security_events:{clientIp}";
        var existingEvents = _memoryCache.Get<List<object>>(securityEventsKey) ?? new List<object>();
        existingEvents.Add(securityEvent);
        
        // Keep only last 100 events per IP
        if (existingEvents.Count > 100)
        {
            existingEvents = existingEvents.TakeLast(100).ToList();
        }
        
        _memoryCache.Set(securityEventsKey, existingEvents, TimeSpan.FromHours(24));
    }

    private string GetClientIpAddress(HttpContext context)
    {
        return context.Connection.RemoteIpAddress?.ToString() ??
               context.Request.Headers["X-Forwarded-For"].FirstOrDefault()?.Split(',')[0].Trim() ??
               context.Request.Headers["X-Real-IP"].FirstOrDefault() ??
               "unknown";
    }
}

/// <summary>
/// Extension methods for registering the Payment Security Middleware
/// </summary>
public static class PaymentSecurityMiddlewareExtensions
{
    public static IApplicationBuilder UsePaymentSecurity(this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<PaymentSecurityMiddleware>();
    }
}