// SPDX-License-Identifier: MIT
// Copyright (c) 2025 HackLoad Payment Gateway

using Microsoft.Extensions.Primitives;
using Microsoft.Extensions.Options;

namespace PaymentGateway.API.Middleware;

/// <summary>
/// Security headers middleware for comprehensive web security
/// 
/// This middleware adds essential security headers to protect against:
/// - Cross-Site Scripting (XSS) attacks
/// - Clickjacking attacks
/// - Content type sniffing
/// - HTTPS downgrade attacks
/// - Content Security Policy violations
/// - Referrer information leakage
/// - Cross-Origin attacks
/// 
/// Configured specifically for payment gateway security requirements.
/// </summary>
public class SecurityHeadersMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<SecurityHeadersMiddleware> _logger;
    private readonly SecurityHeadersOptions _options;
    private readonly IWebHostEnvironment _environment;

    public SecurityHeadersMiddleware(
        RequestDelegate next,
        ILogger<SecurityHeadersMiddleware> logger,
        IOptions<SecurityHeadersOptions> options,
        IWebHostEnvironment environment)
    {
        _next = next;
        _logger = logger;
        _options = options.Value;
        _environment = environment;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Add security headers before processing the request
        AddSecurityHeaders(context);
        
        // Add post-processing headers before the request continues
        // This ensures headers are set before response starts
        AddPostProcessingHeaders(context);

        await _next(context);
    }

    private void AddSecurityHeaders(HttpContext context)
    {
        var response = context.Response;
        var request = context.Request;

        try
        {
            // 1. Strict Transport Security (HSTS)
            if (_options.EnableHsts && request.IsHttps)
            {
                var hstsValue = $"max-age={_options.HstsMaxAge}";
                if (_options.HstsIncludeSubdomains)
                    hstsValue += "; includeSubDomains";
                if (_options.HstsPreload)
                    hstsValue += "; preload";

                SetHeaderIfNotExists(response, "Strict-Transport-Security", hstsValue);
            }

            // 2. Content Security Policy (CSP)
            if (_options.EnableContentSecurityPolicy)
            {
                var cspValue = BuildContentSecurityPolicy();
                SetHeaderIfNotExists(response, "Content-Security-Policy", cspValue);
            }

            // 3. X-Frame-Options (Clickjacking protection)
            if (_options.EnableXFrameOptions)
            {
                SetHeaderIfNotExists(response, "X-Frame-Options", _options.XFrameOptionsValue);
            }

            // 4. X-Content-Type-Options (MIME sniffing protection)
            if (_options.EnableXContentTypeOptions)
            {
                SetHeaderIfNotExists(response, "X-Content-Type-Options", "nosniff");
            }

            // 5. X-XSS-Protection
            if (_options.EnableXXssProtection)
            {
                SetHeaderIfNotExists(response, "X-XSS-Protection", "1; mode=block");
            }

            // 6. Referrer Policy
            if (_options.EnableReferrerPolicy)
            {
                SetHeaderIfNotExists(response, "Referrer-Policy", _options.ReferrerPolicyValue);
            }

            // 7. Permissions Policy (Feature Policy)
            if (_options.EnablePermissionsPolicy)
            {
                var permissionsValue = BuildPermissionsPolicy();
                SetHeaderIfNotExists(response, "Permissions-Policy", permissionsValue);
            }

            // 8. Cross-Origin Embedder Policy
            if (_options.EnableCrossOriginEmbedderPolicy)
            {
                SetHeaderIfNotExists(response, "Cross-Origin-Embedder-Policy", _options.CrossOriginEmbedderPolicyValue);
            }

            // 9. Cross-Origin Opener Policy
            if (_options.EnableCrossOriginOpenerPolicy)
            {
                SetHeaderIfNotExists(response, "Cross-Origin-Opener-Policy", _options.CrossOriginOpenerPolicyValue);
            }

            // 10. Cross-Origin Resource Policy
            if (_options.EnableCrossOriginResourcePolicy)
            {
                SetHeaderIfNotExists(response, "Cross-Origin-Resource-Policy", _options.CrossOriginResourcePolicyValue);
            }

            // 11. Custom security headers for payment gateway
            AddPaymentGatewaySpecificHeaders(response);

            // 12. Remove server information headers
            if (_options.RemoveServerHeader)
            {
                response.Headers.Remove("Server");
                response.Headers.Remove("X-Powered-By");
                response.Headers.Remove("X-AspNet-Version");
                response.Headers.Remove("X-AspNetMvc-Version");
            }

            // 13. Add correlation ID header for security tracking
            var correlationId = context.TraceIdentifier;
            SetHeaderIfNotExists(response, "X-Correlation-ID", correlationId);

        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error adding security headers for request {Path}", request.Path);
            // Don't let security header errors break the request
        }
    }

    private string BuildContentSecurityPolicy()
    {
        var policies = new List<string>();

        // Default source policy - restrict to same origin
        policies.Add("default-src 'self'");

        // Script sources - allow self and specific trusted CDNs for payment processing
        var scriptSources = new List<string> { "'self'" };
        if (_environment.IsDevelopment())
        {
            scriptSources.Add("'unsafe-inline'"); // Allow inline scripts in development
        }
        scriptSources.AddRange(_options.CspScriptSources);
        policies.Add($"script-src {string.Join(" ", scriptSources)}");

        // Style sources - allow self and inline styles for UI components
        var styleSources = new List<string> { "'self'", "'unsafe-inline'" };
        styleSources.AddRange(_options.CspStyleSources);
        policies.Add($"style-src {string.Join(" ", styleSources)}");

        // Image sources - allow self, data URIs, and trusted sources
        var imgSources = new List<string> { "'self'", "data:" };
        imgSources.AddRange(_options.CspImgSources);
        policies.Add($"img-src {string.Join(" ", imgSources)}");

        // Font sources - allow self and data URIs
        policies.Add("font-src 'self' data:");

        // Connect sources - allow API endpoints
        var connectSources = new List<string> { "'self'" };
        connectSources.AddRange(_options.CspConnectSources);
        policies.Add($"connect-src {string.Join(" ", connectSources)}");

        // Frame sources - restrict iframe embedding
        policies.Add("frame-src 'none'");

        // Object sources - block plugins
        policies.Add("object-src 'none'");

        // Media sources - block audio/video
        policies.Add("media-src 'none'");

        // Worker sources - allow self for service workers
        policies.Add("worker-src 'self'");

        // Manifest sources - allow self
        policies.Add("manifest-src 'self'");

        // Base URI - restrict to self
        policies.Add("base-uri 'self'");

        // Form action - restrict form submissions
        policies.Add($"form-action 'self' {string.Join(" ", _options.CspFormActionSources)}");

        // Frame ancestors - prevent embedding in iframes
        policies.Add("frame-ancestors 'none'");

        // Block mixed content
        if (!_environment.IsDevelopment())
        {
            policies.Add("block-all-mixed-content");
        }

        // Upgrade insecure requests
        if (_options.CspUpgradeInsecureRequests)
        {
            policies.Add("upgrade-insecure-requests");
        }

        return string.Join("; ", policies);
    }

    private string BuildPermissionsPolicy()
    {
        var policies = new List<string>
        {
            "accelerometer=()",      // Block accelerometer access
            "ambient-light-sensor=()", // Block ambient light sensor
            "autoplay=()",           // Block autoplay
            "battery=()",            // Block battery API
            "camera=()",             // Block camera access
            "cross-origin-isolated=()", // Block cross-origin isolation
            "display-capture=()",    // Block display capture
            "document-domain=()",    // Block document.domain
            "encrypted-media=()",    // Block encrypted media
            "execution-while-not-rendered=()", // Block execution while not rendered
            "execution-while-out-of-viewport=()", // Block execution while out of viewport
            "fullscreen=()",         // Block fullscreen
            "geolocation=()",        // Block geolocation
            "gyroscope=()",          // Block gyroscope
            "magnetometer=()",       // Block magnetometer
            "microphone=()",         // Block microphone access
            "midi=()",               // Block MIDI access
            "navigation-override=()", // Block navigation override
            "payment=(self)",        // Allow payment API only on same origin
            "picture-in-picture=()", // Block picture-in-picture
            "publickey-credentials-get=(self)", // Allow WebAuthn on same origin
            "screen-wake-lock=()",   // Block screen wake lock
            "sync-xhr=()",           // Block synchronous XHR
            "usb=()",                // Block USB access
            "web-share=()",          // Block web share
            "xr-spatial-tracking=()" // Block XR spatial tracking
        };

        return string.Join(", ", policies);
    }

    private void AddPaymentGatewaySpecificHeaders(HttpResponse response)
    {
        // Custom payment gateway security headers
        SetHeaderIfNotExists(response, "X-Payment-Gateway-Version", "1.0");
        SetHeaderIfNotExists(response, "X-Content-Security-Policy", "payment-gateway");
        SetHeaderIfNotExists(response, "X-Payment-Processing-Mode", "secure");
        
        // Cache control for sensitive endpoints
        if (IsPaymentEndpoint(response.HttpContext.Request.Path))
        {
            SetHeaderIfNotExists(response, "Cache-Control", "no-store, no-cache, must-revalidate, private");
            SetHeaderIfNotExists(response, "Pragma", "no-cache");
            SetHeaderIfNotExists(response, "Expires", "0");
        }

        // Security headers for API responses
        SetHeaderIfNotExists(response, "X-Robots-Tag", "noindex, nofollow, nosnippet, noarchive");
    }

    private bool IsPaymentEndpoint(PathString path)
    {
        var paymentEndpoints = new[]
        {
            "/api/paymentinit",
            "/api/paymentconfirm",
            "/api/paymentcancel",
            "/api/paymentcheck"
        };

        return paymentEndpoints.Any(endpoint => path.StartsWithSegments(endpoint));
    }

    private void AddPostProcessingHeaders(HttpContext context)
    {
        // Add headers before request processing if needed
        var response = context.Response;

        // Only add headers if the response hasn't started
        if (!response.HasStarted)
        {
            // Add timing attack protection header
            SetHeaderIfNotExists(response, "X-Processing-Time", "normalized");

            // Add anti-fingerprinting headers
            if (_options.EnableAntiFingerprinting)
            {
                response.Headers.Remove("ETag");
                response.Headers.Remove("Last-Modified");
            }
        }
    }

    private void SetHeaderIfNotExists(HttpResponse response, string name, string value)
    {
        if (!response.Headers.ContainsKey(name))
        {
            response.Headers.Add(name, new StringValues(value));
        }
    }
}

/// <summary>
/// Security headers configuration options
/// </summary>
public class SecurityHeadersOptions
{
    // HSTS Configuration
    public bool EnableHsts { get; set; } = true;
    public int HstsMaxAge { get; set; } = 31536000; // 1 year
    public bool HstsIncludeSubdomains { get; set; } = true;
    public bool HstsPreload { get; set; } = false;

    // Content Security Policy
    public bool EnableContentSecurityPolicy { get; set; } = true;
    public bool CspUpgradeInsecureRequests { get; set; } = true;
    public string[] CspScriptSources { get; set; } = Array.Empty<string>();
    public string[] CspStyleSources { get; set; } = Array.Empty<string>();
    public string[] CspImgSources { get; set; } = Array.Empty<string>();
    public string[] CspConnectSources { get; set; } = Array.Empty<string>();
    public string[] CspFormActionSources { get; set; } = Array.Empty<string>();

    // X-Frame-Options
    public bool EnableXFrameOptions { get; set; } = true;
    public string XFrameOptionsValue { get; set; } = "DENY";

    // X-Content-Type-Options
    public bool EnableXContentTypeOptions { get; set; } = true;

    // X-XSS-Protection
    public bool EnableXXssProtection { get; set; } = true;

    // Referrer Policy
    public bool EnableReferrerPolicy { get; set; } = true;
    public string ReferrerPolicyValue { get; set; } = "strict-origin-when-cross-origin";

    // Permissions Policy
    public bool EnablePermissionsPolicy { get; set; } = true;

    // Cross-Origin Policies
    public bool EnableCrossOriginEmbedderPolicy { get; set; } = true;
    public string CrossOriginEmbedderPolicyValue { get; set; } = "require-corp";

    public bool EnableCrossOriginOpenerPolicy { get; set; } = true;
    public string CrossOriginOpenerPolicyValue { get; set; } = "same-origin";

    public bool EnableCrossOriginResourcePolicy { get; set; } = true;
    public string CrossOriginResourcePolicyValue { get; set; } = "same-origin";

    // Server Header Removal
    public bool RemoveServerHeader { get; set; } = true;

    // Anti-Fingerprinting
    public bool EnableAntiFingerprinting { get; set; } = true;
}

/// <summary>
/// Extension methods for registering the security headers middleware
/// </summary>
public static class SecurityHeadersMiddlewareExtensions
{
    public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<SecurityHeadersMiddleware>();
    }

    public static IServiceCollection AddSecurityHeaders(this IServiceCollection services, 
        Action<SecurityHeadersOptions>? configure = null)
    {
        if (configure != null)
        {
            services.Configure(configure);
        }
        else
        {
            services.Configure<SecurityHeadersOptions>(options => { });
        }

        return services;
    }

    /// <summary>
    /// Add security headers with payment gateway specific defaults
    /// </summary>
    public static IServiceCollection AddPaymentGatewaySecurityHeaders(this IServiceCollection services)
    {
        return services.AddSecurityHeaders(options =>
        {
            // Configure CSP for payment gateway specific needs
            options.CspScriptSources = [
                "'unsafe-inline'",
                "'sha256-8UeAfAS+DZjqKZMN2Jzy6YTc7YQBr46yHkXIkTphW90='"
            ];

            options.CspConnectSources = new[]
            {
                "https://api.hackload.com"
            };

            options.CspFormActionSources = Array.Empty<string>();

            // Strict frame options for payment security
            options.XFrameOptionsValue = "DENY";
            
            // Strict referrer policy for payment data protection
            options.ReferrerPolicyValue = "no-referrer";
        });
    }
}