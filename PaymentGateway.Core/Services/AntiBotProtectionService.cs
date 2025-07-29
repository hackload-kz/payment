// SPDX-License-Identifier: MIT
// Copyright (c) 2025 HackLoad Payment Gateway

using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Net.Http;
using System.Text.Json;
using System.Security.Cryptography;
using System.Text;

namespace PaymentGateway.Core.Services;

/// <summary>
/// Anti-Bot Protection Service for preventing automated attacks on payment forms
/// 
/// This service implements:
/// - CAPTCHA validation (Google reCAPTCHA v2/v3)
/// - Bot behavior detection
/// - Request frequency analysis
/// - Honeypot field validation
/// - JavaScript challenge validation
/// - Device fingerprinting
/// </summary>
public class AntiBotProtectionService
{
    private readonly ILogger<AntiBotProtectionService> _logger;
    private readonly IConfiguration _configuration;
    private readonly IMemoryCache _memoryCache;
    private readonly HttpClient _httpClient;

    // Configuration
    private readonly bool _enableCaptcha;
    private readonly bool _enableHoneypot;
    private readonly bool _enableJsChallenge;
    private readonly bool _enableBehaviorAnalysis;
    private readonly string _recaptchaSecretKey;
    private readonly string _recaptchaSiteKey;
    private readonly double _recaptchaScoreThreshold;
    private readonly int _maxRequestsPerMinute;
    private readonly int _maxFailedAttemptsPerHour;

    // Bot detection patterns
    private readonly string[] _botUserAgents = new[]
    {
        "bot", "crawler", "spider", "scraper", "curl", "wget", "python", "java",
        "perl", "php", "ruby", "scanner", "check", "monitor", "test", "robot"
    };

    private readonly string[] _suspiciousHeaders = new[]
    {
        "X-Forwarded-For", "X-Real-IP", "X-Originating-IP", "CF-Connecting-IP"
    };

    public AntiBotProtectionService(
        ILogger<AntiBotProtectionService> logger,
        IConfiguration configuration,
        IMemoryCache memoryCache,
        HttpClient httpClient)
    {
        _logger = logger;
        _configuration = configuration;
        _memoryCache = memoryCache;
        _httpClient = httpClient;

        // Load configuration
        _enableCaptcha = _configuration.GetValue<bool>("AntiBot:EnableCaptcha", false);
        _enableHoneypot = _configuration.GetValue<bool>("AntiBot:EnableHoneypot", true);
        _enableJsChallenge = _configuration.GetValue<bool>("AntiBot:EnableJsChallenge", true);
        _enableBehaviorAnalysis = _configuration.GetValue<bool>("AntiBot:EnableBehaviorAnalysis", true);
        _recaptchaSecretKey = _configuration.GetValue<string>("AntiBot:RecaptchaSecretKey") ?? "";
        _recaptchaSiteKey = _configuration.GetValue<string>("AntiBot:RecaptchaSiteKey") ?? "";
        _recaptchaScoreThreshold = _configuration.GetValue<double>("AntiBot:RecaptchaScoreThreshold", 0.5);
        _maxRequestsPerMinute = _configuration.GetValue<int>("AntiBot:MaxRequestsPerMinute", 10);
        _maxFailedAttemptsPerHour = _configuration.GetValue<int>("AntiBot:MaxFailedAttemptsPerHour", 5);
    }

    /// <summary>
    /// Validate anti-bot protection for a payment form submission
    /// </summary>
    public async Task<AntiBotValidationResult> ValidateAsync(AntiBotValidationRequest request)
    {
        var result = new AntiBotValidationResult
        {
            IsValid = true,
            ValidationDetails = new List<string>()
        };

        try
        {
            _logger.LogDebug("Starting anti-bot validation for IP: {ClientIp}, PaymentId: {PaymentId}",
                request.ClientIp, request.PaymentId);

            // 1. Rate limiting check
            var rateLimitResult = await CheckRateLimit(request.ClientIp);
            if (!rateLimitResult.IsValid)
            {
                result.IsValid = false;
                result.FailureReason = "Rate limit exceeded";
                result.ValidationDetails.Add("Request frequency too high");
                return result;
            }

            // 2. User Agent analysis
            var userAgentResult = AnalyzeUserAgent(request.UserAgent);
            if (!userAgentResult.IsValid)
            {
                result.IsValid = false;
                result.FailureReason = "Suspicious user agent detected";
                result.ValidationDetails.Add($"Bot-like user agent: {request.UserAgent}");
                return result;
            }

            // 3. Honeypot validation
            if (_enableHoneypot)
            {
                var honeypotResult = ValidateHoneypot(request.HoneypotValues);
                if (!honeypotResult.IsValid)
                {
                    result.IsValid = false;
                    result.FailureReason = "Honeypot validation failed";
                    result.ValidationDetails.Add("Bot detected via honeypot fields");
                    return result;
                }
            }

            // 4. JavaScript challenge validation
            if (_enableJsChallenge)
            {
                var jsResult = await ValidateJavaScriptChallenge(request.JsChallengeResponse, request.PaymentId);
                if (!jsResult.IsValid)
                {
                    result.IsValid = false;
                    result.FailureReason = "JavaScript challenge failed";
                    result.ValidationDetails.Add("Invalid or missing JavaScript challenge response");
                    return result;
                }
            }

            // 5. CAPTCHA validation
            if (_enableCaptcha && !string.IsNullOrEmpty(request.CaptchaResponse))
            {
                var captchaResult = await ValidateCaptchaAsync(request.CaptchaResponse, request.ClientIp);
                if (!captchaResult.IsValid)
                {
                    result.IsValid = false;
                    result.FailureReason = "CAPTCHA validation failed";
                    result.ValidationDetails.Add($"CAPTCHA validation failed: {captchaResult.FailureReason}");
                    return result;
                }
                result.CaptchaScore = captchaResult.Score;
            }

            // 6. Behavior analysis
            if (_enableBehaviorAnalysis)
            {
                var behaviorResult = await AnalyzeBehavior(request);
                if (!behaviorResult.IsValid)
                {
                    result.IsValid = false;
                    result.FailureReason = "Suspicious behavior detected";
                    result.ValidationDetails.AddRange(behaviorResult.ValidationDetails);
                    return result;
                }
            }

            // 7. Device fingerprinting analysis
            var fingerprintResult = AnalyzeDeviceFingerprint(request.DeviceFingerprint);
            result.ValidationDetails.AddRange(fingerprintResult.ValidationDetails);

            result.RiskScore = CalculateRiskScore(request, rateLimitResult, userAgentResult, fingerprintResult);
            
            _logger.LogInformation("Anti-bot validation completed. IP: {ClientIp}, PaymentId: {PaymentId}, Valid: {IsValid}, RiskScore: {RiskScore}",
                request.ClientIp, request.PaymentId, result.IsValid, result.RiskScore);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during anti-bot validation for IP: {ClientIp}, PaymentId: {PaymentId}",
                request.ClientIp, request.PaymentId);
            
            // Fail safe: allow request but log the error
            result.IsValid = true;
            result.FailureReason = "Validation service error";
            result.ValidationDetails.Add("Anti-bot service temporarily unavailable");
            return result;
        }
    }

    /// <summary>
    /// Generate JavaScript challenge for client-side validation
    /// </summary>
    public string GenerateJavaScriptChallenge(string paymentId)
    {
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var challengeData = $"{paymentId}:{timestamp}:{Guid.NewGuid():N}";
        
        using var sha256 = SHA256.Create();
        var hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(challengeData));
        var challenge = Convert.ToBase64String(hash);
        
        // Store challenge in cache for validation
        var cacheKey = $"js_challenge:{paymentId}";
        _memoryCache.Set(cacheKey, challenge, TimeSpan.FromMinutes(10));
        
        return challenge;
    }

    /// <summary>
    /// Get CAPTCHA configuration for client-side rendering
    /// </summary>
    public CaptchaConfiguration GetCaptchaConfiguration()
    {
        return new CaptchaConfiguration
        {
            Enabled = _enableCaptcha,
            SiteKey = _recaptchaSiteKey,
            Type = "v3", // Could be v2 or v3
            ScoreThreshold = _recaptchaScoreThreshold
        };
    }

    private async Task<ValidationResult> CheckRateLimit(string clientIp)
    {
        var rateLimitKey = $"antibot_rate:{clientIp}";
        var requestCount = _memoryCache.Get<int>(rateLimitKey);
        
        if (requestCount >= _maxRequestsPerMinute)
        {
            return new ValidationResult
            {
                IsValid = false,
                ValidationDetails = new List<string> { $"Rate limit exceeded: {requestCount} requests per minute" }
            };
        }

        // Increment counter
        _memoryCache.Set(rateLimitKey, requestCount + 1, TimeSpan.FromMinutes(1));
        
        return new ValidationResult
        {
            IsValid = true,
            ValidationDetails = new List<string> { $"Rate limit OK: {requestCount + 1}/{_maxRequestsPerMinute}" }
        };
    }

    private ValidationResult AnalyzeUserAgent(string userAgent)
    {
        if (string.IsNullOrWhiteSpace(userAgent))
        {
            return new ValidationResult
            {
                IsValid = false,
                ValidationDetails = new List<string> { "Missing User-Agent header" }
            };
        }

        // Check for bot-like user agents
        foreach (var botPattern in _botUserAgents)
        {
            if (userAgent.Contains(botPattern, StringComparison.OrdinalIgnoreCase))
            {
                return new ValidationResult
                {
                    IsValid = false,
                    ValidationDetails = new List<string> { $"Bot-like User-Agent detected: {botPattern}" }
                };
            }
        }

        // Check for suspiciously short or long user agents
        if (userAgent.Length < 10 || userAgent.Length > 500)
        {
            return new ValidationResult
            {
                IsValid = false,
                ValidationDetails = new List<string> { $"Suspicious User-Agent length: {userAgent.Length}" }
            };
        }

        return new ValidationResult
        {
            IsValid = true,
            ValidationDetails = new List<string> { "User-Agent analysis passed" }
        };
    }

    private ValidationResult ValidateHoneypot(Dictionary<string, string>? honeypotValues)
    {
        if (!_enableHoneypot || honeypotValues == null)
        {
            return new ValidationResult { IsValid = true, ValidationDetails = new List<string>() };
        }

        // Honeypot fields should be empty (filled by bots but hidden from users)
        var honeypotFields = new[] { "website", "url", "homepage", "comment", "phone2", "email2" };
        
        foreach (var field in honeypotFields)
        {
            if (honeypotValues.TryGetValue(field, out var value) && !string.IsNullOrWhiteSpace(value))
            {
                return new ValidationResult
                {
                    IsValid = false,
                    ValidationDetails = new List<string> { $"Honeypot field '{field}' was filled: {value}" }
                };
            }
        }

        return new ValidationResult
        {
            IsValid = true,
            ValidationDetails = new List<string> { "Honeypot validation passed" }
        };
    }

    private async Task<ValidationResult> ValidateJavaScriptChallenge(string? challengeResponse, string paymentId)
    {
        if (!_enableJsChallenge)
        {
            return new ValidationResult { IsValid = true, ValidationDetails = new List<string>() };
        }

        if (string.IsNullOrWhiteSpace(challengeResponse))
        {
            return new ValidationResult
            {
                IsValid = false,
                ValidationDetails = new List<string> { "Missing JavaScript challenge response" }
            };
        }

        var cacheKey = $"js_challenge:{paymentId}";
        if (!_memoryCache.TryGetValue(cacheKey, out string? storedChallenge))
        {
            return new ValidationResult
            {
                IsValid = false,
                ValidationDetails = new List<string> { "JavaScript challenge expired or not found" }
            };
        }

        if (challengeResponse != storedChallenge)
        {
            return new ValidationResult
            {
                IsValid = false,
                ValidationDetails = new List<string> { "Invalid JavaScript challenge response" }
            };
        }

        // Remove used challenge
        _memoryCache.Remove(cacheKey);

        return new ValidationResult
        {
            IsValid = true,
            ValidationDetails = new List<string> { "JavaScript challenge validation passed" }
        };
    }

    private async Task<CaptchaValidationResult> ValidateCaptchaAsync(string captchaResponse, string clientIp)
    {
        if (string.IsNullOrWhiteSpace(_recaptchaSecretKey))
        {
            return new CaptchaValidationResult
            {
                IsValid = false,
                FailureReason = "CAPTCHA service not configured"
            };
        }

        try
        {
            var requestBody = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("secret", _recaptchaSecretKey),
                new KeyValuePair<string, string>("response", captchaResponse),
                new KeyValuePair<string, string>("remoteip", clientIp)
            });

            var response = await _httpClient.PostAsync("https://www.google.com/recaptcha/api/siteverify", requestBody);
            var responseContent = await response.Content.ReadAsStringAsync();
            
            var captchaResult = JsonSerializer.Deserialize<RecaptchaResponse>(responseContent);
            
            if (captchaResult?.Success == true)
            {
                var score = captchaResult.Score ?? 1.0;
                if (score >= _recaptchaScoreThreshold)
                {
                    return new CaptchaValidationResult
                    {
                        IsValid = true,
                        Score = score
                    };
                }
                else
                {
                    return new CaptchaValidationResult
                    {
                        IsValid = false,
                        FailureReason = $"CAPTCHA score too low: {score}",
                        Score = score
                    };
                }
            }
            else
            {
                var errors = captchaResult?.ErrorCodes != null ? string.Join(", ", captchaResult.ErrorCodes) : "Unknown error";
                return new CaptchaValidationResult
                {
                    IsValid = false,
                    FailureReason = $"CAPTCHA validation failed: {errors}"
                };
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating CAPTCHA for IP: {ClientIp}", clientIp);
            return new CaptchaValidationResult
            {
                IsValid = false,
                FailureReason = "CAPTCHA service error"
            };
        }
    }

    private async Task<ValidationResult> AnalyzeBehavior(AntiBotValidationRequest request)
    {
        var details = new List<string>();
        var suspiciousIndicators = 0;

        // Check form submission timing
        if (request.FormLoadTime.HasValue && request.FormSubmitTime.HasValue)
        {
            var formFillTime = request.FormSubmitTime.Value - request.FormLoadTime.Value;
            
            if (formFillTime.TotalSeconds < 5)
            {
                suspiciousIndicators++;
                details.Add($"Form filled too quickly: {formFillTime.TotalSeconds:F1} seconds");
            }
            else if (formFillTime.TotalMinutes > 30)
            {
                suspiciousIndicators++;
                details.Add($"Form filled too slowly: {formFillTime.TotalMinutes:F1} minutes");
            }
            else
            {
                details.Add($"Form fill time normal: {formFillTime.TotalSeconds:F1} seconds");
            }
        }

        // Check mouse movements and keyboard events
        if (request.MouseMovements < 10)
        {
            suspiciousIndicators++;
            details.Add($"Too few mouse movements: {request.MouseMovements}");
        }

        if (request.KeyboardEvents < 20)
        {
            suspiciousIndicators++;
            details.Add($"Too few keyboard events: {request.KeyboardEvents}");
        }

        // Check failed attempts history
        var failedAttemptsKey = $"failed_attempts:{request.ClientIp}";
        var failedAttempts = _memoryCache.Get<int>(failedAttemptsKey);
        
        if (failedAttempts >= _maxFailedAttemptsPerHour)
        {
            suspiciousIndicators++;
            details.Add($"Too many failed attempts: {failedAttempts}");
        }

        var isValid = suspiciousIndicators < 2; // Allow some suspicious behavior

        return new ValidationResult
        {
            IsValid = isValid,
            ValidationDetails = details
        };
    }

    private ValidationResult AnalyzeDeviceFingerprint(DeviceFingerprint? fingerprint)
    {
        var details = new List<string>();
        
        if (fingerprint == null)
        {
            details.Add("No device fingerprint provided");
            return new ValidationResult { IsValid = true, ValidationDetails = details };
        }

        // Analyze fingerprint components
        details.Add($"Screen resolution: {fingerprint.ScreenWidth}x{fingerprint.ScreenHeight}");
        details.Add($"Browser: {fingerprint.UserAgent}");
        details.Add($"Language: {fingerprint.Language}");
        details.Add($"Timezone: {fingerprint.Timezone}");
        details.Add($"Plugins: {fingerprint.Plugins?.Count ?? 0}");

        // Check for suspicious patterns
        var suspiciousIndicators = 0;

        if (fingerprint.PluginsEnabled == false)
        {
            suspiciousIndicators++;
            details.Add("Plugins disabled (suspicious)");
        }

        if (fingerprint.CookiesEnabled == false)
        {
            suspiciousIndicators++;
            details.Add("Cookies disabled (suspicious)");
        }

        if (string.IsNullOrWhiteSpace(fingerprint.Language))
        {
            suspiciousIndicators++;
            details.Add("No language detected (suspicious)");
        }

        return new ValidationResult
        {
            IsValid = suspiciousIndicators < 2,
            ValidationDetails = details
        };
    }

    private double CalculateRiskScore(AntiBotValidationRequest request, params ValidationResult[] validationResults)
    {
        var riskScore = 0.0;
        var maxScore = 100.0;

        // Base risk from validation failures
        foreach (var result in validationResults)
        {
            if (!result.IsValid)
            {
                riskScore += 25.0;
            }
        }

        // Additional risk factors
        if (string.IsNullOrWhiteSpace(request.UserAgent))
            riskScore += 10.0;

        if (request.MouseMovements < 5)
            riskScore += 15.0;

        if (request.KeyboardEvents < 10)
            riskScore += 15.0;

        return Math.Min(riskScore, maxScore);
    }
}

// Supporting classes

public class AntiBotValidationRequest
{
    public string PaymentId { get; set; } = string.Empty;
    public string ClientIp { get; set; } = string.Empty;
    public string UserAgent { get; set; } = string.Empty;
    public string? CaptchaResponse { get; set; }
    public string? JsChallengeResponse { get; set; }
    public Dictionary<string, string>? HoneypotValues { get; set; }
    public DateTime? FormLoadTime { get; set; }
    public DateTime? FormSubmitTime { get; set; }
    public int MouseMovements { get; set; }
    public int KeyboardEvents { get; set; }
    public DeviceFingerprint? DeviceFingerprint { get; set; }
}

public class AntiBotValidationResult
{
    public bool IsValid { get; set; }
    public string? FailureReason { get; set; }
    public double RiskScore { get; set; }
    public double? CaptchaScore { get; set; }
    public List<string> ValidationDetails { get; set; } = new();
}

public class ValidationResult
{
    public bool IsValid { get; set; }
    public List<string> ValidationDetails { get; set; } = new();
}

public class CaptchaValidationResult : ValidationResult
{
    public string? FailureReason { get; set; }
    public double? Score { get; set; }
}

public class CaptchaConfiguration
{
    public bool Enabled { get; set; }
    public string SiteKey { get; set; } = string.Empty;
    public string Type { get; set; } = "v3";
    public double ScoreThreshold { get; set; } = 0.5;
}

public class DeviceFingerprint
{
    public string UserAgent { get; set; } = string.Empty;
    public int ScreenWidth { get; set; }
    public int ScreenHeight { get; set; }
    public int ColorDepth { get; set; }
    public string Language { get; set; } = string.Empty;
    public string Timezone { get; set; } = string.Empty;
    public bool? CookiesEnabled { get; set; }
    public bool? PluginsEnabled { get; set; }
    public List<string>? Plugins { get; set; }
    public string? CanvasFingerprint { get; set; }
    public string? WebGLFingerprint { get; set; }
}

public class RecaptchaResponse
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("score")]
    public double? Score { get; set; }

    [JsonPropertyName("action")]
    public string? Action { get; set; }

    [JsonPropertyName("challenge_ts")]
    public string? ChallengeTimestamp { get; set; }

    [JsonPropertyName("hostname")]
    public string? Hostname { get; set; }

    [JsonPropertyName("error-codes")]
    public string[]? ErrorCodes { get; set; }
}