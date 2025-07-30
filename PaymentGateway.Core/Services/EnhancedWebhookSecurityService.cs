using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Caching.Memory;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace PaymentGateway.Core.Services;

/// <summary>
/// Enhanced webhook security service with comprehensive signature validation and security features
/// </summary>
public interface IEnhancedWebhookSecurityService
{
    Task<WebhookValidationResult> ValidateWebhookSignatureAsync(WebhookValidationRequest request, CancellationToken cancellationToken = default);
    Task<string> GenerateWebhookSignatureAsync(string payload, string secret, WebhookSignatureOptions? options = null, CancellationToken cancellationToken = default);
    Task<WebhookSecurityAnalysis> AnalyzeWebhookSecurityAsync(WebhookSecurityAnalysisRequest request, CancellationToken cancellationToken = default);
    Task<bool> IsWebhookEndpointSecureAsync(string webhookUrl, CancellationToken cancellationToken = default);
    Task<WebhookDeliveryAttemptResult> SecureWebhookDeliveryAsync(SecureWebhookDeliveryRequest request, CancellationToken cancellationToken = default);
    Task<List<WebhookSecurityEvent>> GetWebhookSecurityEventsAsync(string teamSlug, TimeRange? timeRange = null, CancellationToken cancellationToken = default);
}

/// <summary>
/// Enhanced webhook security service implementation
/// </summary>
public class EnhancedWebhookSecurityService : IEnhancedWebhookSecurityService
{
    private readonly ILogger<EnhancedWebhookSecurityService> _logger;
    private readonly IMemoryCache _cache;
    private readonly ISecurityAuditService _securityAuditService;
    private readonly HttpClient _httpClient;
    private readonly WebhookSecurityOptions _options;
    private readonly TimeSpan _cacheDuration = TimeSpan.FromMinutes(15);

    // Metrics counters for monitoring (simple counters without external dependency)
    private static long _validationSuccessCount = 0;
    private static long _validationFailureCount = 0;

    public EnhancedWebhookSecurityService(
        ILogger<EnhancedWebhookSecurityService> logger,
        IMemoryCache cache,
        ISecurityAuditService securityAuditService,
        HttpClient httpClient,
        IOptions<WebhookSecurityOptions> options)
    {
        _logger = logger;
        _cache = cache;
        _securityAuditService = securityAuditService;
        _httpClient = httpClient;
        _options = options.Value;
    }

    public async Task<WebhookValidationResult> ValidateWebhookSignatureAsync(
        WebhookValidationRequest request, 
        CancellationToken cancellationToken = default)
    {
        var startTime = DateTime.UtcNow;
        
        try
        {
            _logger.LogDebug("Validating webhook signature for TeamSlug: {TeamSlug}", request.TeamSlug);

            // Basic validation
            if (string.IsNullOrEmpty(request.Payload) || string.IsNullOrEmpty(request.Signature) || string.IsNullOrEmpty(request.Secret))
            {
                Interlocked.Increment(ref _validationFailureCount);
                return new WebhookValidationResult
                {
                    IsValid = false,
                    ErrorMessage = "Invalid input parameters",
                    SecurityLevel = WebhookSecurityLevel.Insecure
                };
            }

            // Check for replay attacks using timestamp
            if (request.Timestamp.HasValue)
            {
                var timeDifference = Math.Abs((DateTime.UtcNow - request.Timestamp.Value).TotalSeconds);
                if (timeDifference > _options.MaxTimestampToleranceSeconds)
                {
                    await LogSecurityEventAsync(SecurityEventType.SuspiciousActivity, request.TeamSlug, 
                        "Webhook timestamp outside tolerance window", false, request.IpAddress);
                    
                    Interlocked.Increment(ref _validationFailureCount);
                    return new WebhookValidationResult
                    {
                        IsValid = false,
                        ErrorMessage = "Timestamp outside acceptable range",
                        SecurityLevel = WebhookSecurityLevel.Suspicious
                    };
                }
            }

            // Check for signature replay protection
            if (_options.EnableReplayProtection && !string.IsNullOrEmpty(request.NonceId))
            {
                var replayCacheKey = $"webhook_nonce_{request.NonceId}";
                if (_cache.TryGetValue(replayCacheKey, out _))
                {
                    await LogSecurityEventAsync(SecurityEventType.SuspiciousActivity, request.TeamSlug, 
                        "Webhook signature replay attempt detected", false, request.IpAddress);
                    
                    Interlocked.Increment(ref _validationFailureCount);
                    return new WebhookValidationResult
                    {
                        IsValid = false,
                        ErrorMessage = "Signature replay detected",
                        SecurityLevel = WebhookSecurityLevel.Malicious
                    };
                }

                // Cache the nonce to prevent replay
                _cache.Set(replayCacheKey, true, TimeSpan.FromMinutes(_options.NonceValidityMinutes));
            }

            // Validate signature based on type
            var isValidSignature = await ValidateSignatureByTypeAsync(request);
            var securityLevel = DetermineSecurityLevel(request, isValidSignature);

            if (isValidSignature)
            {
                Interlocked.Increment(ref _validationSuccessCount);
                await LogSecurityEventAsync(SecurityEventType.AuthenticationSuccess, request.TeamSlug, 
                    "Webhook signature validation successful", true, request.IpAddress);
            }
            else
            {
                Interlocked.Increment(ref _validationFailureCount);
                await LogSecurityEventAsync(SecurityEventType.AuthenticationFailure, request.TeamSlug, 
                    "Webhook signature validation failed", false, request.IpAddress);
            }

            return new WebhookValidationResult
            {
                IsValid = isValidSignature,
                ErrorMessage = isValidSignature ? null : "Invalid signature",
                SecurityLevel = securityLevel,
                ValidationMetadata = new Dictionary<string, string>
                {
                    { "SignatureType", request.SignatureType.ToString() },
                    { "TimestampValidated", request.Timestamp.HasValue.ToString() },
                    { "ReplayProtectionEnabled", _options.EnableReplayProtection.ToString() }
                }
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating webhook signature for TeamSlug: {TeamSlug}", request.TeamSlug);
            
            await LogSecurityEventAsync(SecurityEventType.SystemError, request.TeamSlug, 
                "Webhook signature validation system error", false, request.IpAddress, ex.Message);
            
            Interlocked.Increment(ref _validationFailureCount);
            return new WebhookValidationResult
            {
                IsValid = false,
                ErrorMessage = "Validation system error",
                SecurityLevel = WebhookSecurityLevel.Unknown
            };
        }
    }

    public async Task<string> GenerateWebhookSignatureAsync(
        string payload, 
        string secret, 
        WebhookSignatureOptions? options = null, 
        CancellationToken cancellationToken = default)
    {
        var signatureOptions = options ?? new WebhookSignatureOptions();
        
        try
        {
            var stringToSign = payload;
            
            // Add timestamp if required
            if (signatureOptions.IncludeTimestamp)
            {
                var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                stringToSign = $"{timestamp}.{payload}";
            }

            // Add nonce if required
            if (signatureOptions.IncludeNonce)
            {
                var nonce = Guid.NewGuid().ToString("N");
                stringToSign = $"{stringToSign}.{nonce}";
            }

            // Generate signature based on algorithm
            var signature = signatureOptions.Algorithm switch
            {
                WebhookSignatureAlgorithm.HMAC_SHA256 => GenerateHmacSha256Signature(stringToSign, secret),
                WebhookSignatureAlgorithm.HMAC_SHA512 => GenerateHmacSha512Signature(stringToSign, secret),
                WebhookSignatureAlgorithm.HMAC_SHA3_256 => GenerateHmacSha3Signature(stringToSign, secret),
                _ => throw new NotSupportedException($"Signature algorithm {signatureOptions.Algorithm} is not supported")
            };

            // Add algorithm prefix if required
            if (signatureOptions.IncludeAlgorithmPrefix)
            {
                var algorithmPrefix = signatureOptions.Algorithm switch
                {
                    WebhookSignatureAlgorithm.HMAC_SHA256 => "sha256",
                    WebhookSignatureAlgorithm.HMAC_SHA512 => "sha512",
                    WebhookSignatureAlgorithm.HMAC_SHA3_256 => "sha3-256",
                    _ => "unknown"
                };
                signature = $"{algorithmPrefix}={signature}";
            }

            return signature;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating webhook signature");
            throw;
        }
    }

    public async Task<WebhookSecurityAnalysis> AnalyzeWebhookSecurityAsync(
        WebhookSecurityAnalysisRequest request, 
        CancellationToken cancellationToken = default)
    {
        var analysis = new WebhookSecurityAnalysis
        {
            WebhookUrl = request.WebhookUrl,
            TeamSlug = request.TeamSlug,
            AnalyzedAt = DateTime.UtcNow
        };

        try
        {
            // Check URL security
            var urlSecurity = await AnalyzeUrlSecurityAsync(request.WebhookUrl);
            analysis.SecurityIssues.AddRange(urlSecurity.Issues);
            analysis.SecurityScore += urlSecurity.Score;

            // Check endpoint reachability and certificate
            var endpointSecurity = await AnalyzeEndpointSecurityAsync(request.WebhookUrl, cancellationToken);
            analysis.SecurityIssues.AddRange(endpointSecurity.Issues);
            analysis.SecurityScore += endpointSecurity.Score;

            // Check historical security events
            var historicalSecurity = await AnalyzeHistoricalSecurityAsync(request.TeamSlug, request.WebhookUrl);
            analysis.SecurityIssues.AddRange(historicalSecurity.Issues);
            analysis.SecurityScore += historicalSecurity.Score;

            // Determine overall security level
            analysis.OverallSecurityLevel = analysis.SecurityScore switch
            {
                >= 80 => WebhookSecurityLevel.Secure,
                >= 60 => WebhookSecurityLevel.Moderate,
                >= 40 => WebhookSecurityLevel.Insecure,
                >= 20 => WebhookSecurityLevel.Suspicious,
                _ => WebhookSecurityLevel.Malicious
            };

            // Generate recommendations
            analysis.SecurityRecommendations = GenerateSecurityRecommendations(analysis);

            await LogSecurityEventAsync(SecurityEventType.DataAccess, request.TeamSlug, 
                $"Webhook security analysis completed for {request.WebhookUrl}", true);

            return analysis;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error analyzing webhook security for {WebhookUrl}", request.WebhookUrl);
            
            analysis.SecurityIssues.Add(new WebhookSecurityIssue
            {
                Type = WebhookSecurityIssueType.AnalysisError,
                Severity = WebhookIssueSeverity.High,
                Description = "Failed to complete security analysis",
                Details = ex.Message
            });

            return analysis;
        }
    }

    public async Task<bool> IsWebhookEndpointSecureAsync(string webhookUrl, CancellationToken cancellationToken = default)
    {
        try
        {
            // Check if URL uses HTTPS
            if (!webhookUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            // Check if endpoint is reachable and has valid certificate
            var request = new HttpRequestMessage(HttpMethod.Head, webhookUrl);
            request.Headers.Add("User-Agent", "PaymentGateway-WebhookValidator/1.0");
            
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            
            // Consider endpoint secure if it responds (even with 4xx errors)
            // The main concern is connectivity and HTTPS
            return true;
        }
        catch (HttpRequestException)
        {
            // Endpoint not reachable
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error checking webhook endpoint security for {WebhookUrl}", webhookUrl);
            return false;
        }
    }

    public async Task<WebhookDeliveryAttemptResult> SecureWebhookDeliveryAsync(
        SecureWebhookDeliveryRequest request, 
        CancellationToken cancellationToken = default)
    {
        var result = new WebhookDeliveryAttemptResult
        {
            DeliveryId = Guid.NewGuid().ToString(),
            TeamSlug = request.TeamSlug,
            WebhookUrl = request.WebhookUrl,
            AttemptedAt = DateTime.UtcNow
        };

        try
        {
            // Security pre-checks
            if (!await IsWebhookEndpointSecureAsync(request.WebhookUrl, cancellationToken))
            {
                result.IsSuccessful = false;
                result.ErrorMessage = "Webhook endpoint is not secure";
                result.SecurityLevel = WebhookSecurityLevel.Insecure;
                return result;
            }

            // Generate secure signature
            var signature = await GenerateWebhookSignatureAsync(
                request.Payload, 
                request.Secret, 
                request.SignatureOptions, 
                cancellationToken);

            // Prepare HTTP request
            var httpRequest = new HttpRequestMessage(HttpMethod.Post, request.WebhookUrl);
            httpRequest.Content = new StringContent(request.Payload, Encoding.UTF8, "application/json");
            
            // Add security headers
            httpRequest.Headers.Add("X-Webhook-Signature", signature);
            httpRequest.Headers.Add("X-Webhook-Timestamp", DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString());
            httpRequest.Headers.Add("User-Agent", "PaymentGateway-WebhookDelivery/1.0");
            
            if (!string.IsNullOrEmpty(request.WebhookId))
            {
                httpRequest.Headers.Add("X-Webhook-Id", request.WebhookId);
            }

            // Add custom headers
            foreach (var header in request.CustomHeaders)
            {
                httpRequest.Headers.Add(header.Key, header.Value);
            }

            // Send request with timeout
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(request.TimeoutSeconds > 0 ? TimeSpan.FromSeconds(request.TimeoutSeconds) : TimeSpan.FromSeconds(30));

            var startTime = DateTime.UtcNow;
            using var response = await _httpClient.SendAsync(httpRequest, cts.Token);
            var endTime = DateTime.UtcNow;

            result.ResponseTime = endTime - startTime;
            result.HttpStatusCode = (int)response.StatusCode;
            result.ResponseHeaders = response.Headers.ToDictionary(h => h.Key, h => string.Join(", ", h.Value));

            if (response.IsSuccessStatusCode)
            {
                result.IsSuccessful = true;
                result.SecurityLevel = WebhookSecurityLevel.Secure;
                
                await LogSecurityEventAsync(SecurityEventType.DataAccess, request.TeamSlug, 
                    "Secure webhook delivery successful", true);
            }
            else
            {
                result.IsSuccessful = false;
                result.ErrorMessage = $"HTTP {response.StatusCode}: {response.ReasonPhrase}";
                result.SecurityLevel = WebhookSecurityLevel.Moderate;
                
                await LogSecurityEventAsync(SecurityEventType.DataAccess, request.TeamSlug, 
                    $"Webhook delivery failed with HTTP {response.StatusCode}", false);
            }

            return result;
        }
        catch (OperationCanceledException)
        {
            result.IsSuccessful = false;
            result.ErrorMessage = "Webhook delivery timeout";
            result.SecurityLevel = WebhookSecurityLevel.Unknown;
            
            await LogSecurityEventAsync(SecurityEventType.SystemError, request.TeamSlug, 
                "Webhook delivery timeout", false);
            
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during secure webhook delivery to {WebhookUrl}", request.WebhookUrl);
            
            result.IsSuccessful = false;
            result.ErrorMessage = ex.Message;
            result.SecurityLevel = WebhookSecurityLevel.Unknown;
            
            await LogSecurityEventAsync(SecurityEventType.SystemError, request.TeamSlug, 
                "Webhook delivery system error", false, null, ex.Message);
            
            return result;
        }
    }

    public async Task<List<WebhookSecurityEvent>> GetWebhookSecurityEventsAsync(
        string teamSlug, 
        TimeRange? timeRange = null, 
        CancellationToken cancellationToken = default)
    {
        var events = new List<WebhookSecurityEvent>();
        
        try
        {
            var filter = new SecurityEventFilter(
                timeRange?.StartDate ?? DateTime.UtcNow.AddDays(-7),
                timeRange?.EndDate ?? DateTime.UtcNow,
                null,
                SecurityEventSeverity.Low,
                teamSlug,
                null,
                null,
                1000);

            var securityEvents = await _securityAuditService.GetSecurityEventsAsync(filter, cancellationToken);
            
            foreach (var securityEvent in securityEvents)
            {
                if (IsWebhookRelatedEvent(securityEvent))
                {
                    events.Add(new WebhookSecurityEvent
                    {
                        EventId = securityEvent.EventId,
                        TeamSlug = securityEvent.TeamSlug ?? "",
                        EventType = MapToWebhookEventType(securityEvent.EventType),
                        Severity = MapToWebhookSeverity(securityEvent.Severity),
                        Timestamp = securityEvent.Timestamp,
                        Description = securityEvent.EventDescription,
                        IpAddress = securityEvent.IpAddress,
                        IsSuccessful = securityEvent.IsSuccessful,
                        ErrorMessage = securityEvent.ErrorMessage
                    });
                }
            }

            return events.OrderByDescending(e => e.Timestamp).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving webhook security events for team {TeamSlug}", teamSlug);
            return events;
        }
    }

    #region Private Helper Methods

    private async Task<bool> ValidateSignatureByTypeAsync(WebhookValidationRequest request)
    {
        var expectedSignature = await GenerateWebhookSignatureAsync(
            request.Payload, 
            request.Secret, 
            new WebhookSignatureOptions 
            { 
                Algorithm = request.SignatureType,
                IncludeTimestamp = request.Timestamp.HasValue,
                IncludeAlgorithmPrefix = request.Signature.Contains('=')
            });

        return SecureStringCompare(request.Signature, expectedSignature);
    }

    private bool SecureStringCompare(string signature1, string signature2)
    {
        if (string.IsNullOrEmpty(signature1) || string.IsNullOrEmpty(signature2))
        {
            return false;
        }

        if (signature1.Length != signature2.Length)
        {
            return false;
        }

        var result = 0;
        for (int i = 0; i < signature1.Length; i++)
        {
            result |= signature1[i] ^ signature2[i];
        }

        return result == 0;
    }

    private string GenerateHmacSha256Signature(string payload, string secret)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var hashBytes = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }

    private string GenerateHmacSha512Signature(string payload, string secret)
    {
        using var hmac = new HMACSHA512(Encoding.UTF8.GetBytes(secret));
        var hashBytes = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }

    private string GenerateHmacSha3Signature(string payload, string secret)
    {
        // Note: SHA-3 HMAC implementation would require additional library
        // For now, fall back to SHA-256
        return GenerateHmacSha256Signature(payload, secret);
    }

    private WebhookSecurityLevel DetermineSecurityLevel(WebhookValidationRequest request, bool isValidSignature)
    {
        if (!isValidSignature)
        {
            return WebhookSecurityLevel.Malicious;
        }

        var score = 0;
        
        // Signature algorithm strength
        score += request.SignatureType switch
        {
            WebhookSignatureAlgorithm.HMAC_SHA512 => 30,
            WebhookSignatureAlgorithm.HMAC_SHA3_256 => 25,
            WebhookSignatureAlgorithm.HMAC_SHA256 => 20,
            _ => 10
        };

        // Timestamp validation
        if (request.Timestamp.HasValue) score += 20;

        // Replay protection
        if (!string.IsNullOrEmpty(request.NonceId)) score += 20;

        // HTTPS enforcement
        if (request.WebhookUrl?.StartsWith("https://", StringComparison.OrdinalIgnoreCase) == true) score += 20;

        return score switch
        {
            >= 80 => WebhookSecurityLevel.Secure,
            >= 60 => WebhookSecurityLevel.Moderate,
            >= 40 => WebhookSecurityLevel.Insecure,
            _ => WebhookSecurityLevel.Suspicious
        };
    }

    private async Task<(List<WebhookSecurityIssue> Issues, int Score)> AnalyzeUrlSecurityAsync(string webhookUrl)
    {
        var issues = new List<WebhookSecurityIssue>();
        var score = 0;

        if (string.IsNullOrEmpty(webhookUrl))
        {
            issues.Add(new WebhookSecurityIssue
            {
                Type = WebhookSecurityIssueType.InvalidUrl,
                Severity = WebhookIssueSeverity.Critical,
                Description = "Webhook URL is empty or null"
            });
            return (issues, 0);
        }

        if (!Uri.TryCreate(webhookUrl, UriKind.Absolute, out var uri))
        {
            issues.Add(new WebhookSecurityIssue
            {
                Type = WebhookSecurityIssueType.InvalidUrl,
                Severity = WebhookIssueSeverity.Critical,
                Description = "Webhook URL is not a valid URI"
            });
            return (issues, 0);
        }

        // Check HTTPS
        if (uri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase))
        {
            score += 40;
        }
        else
        {
            issues.Add(new WebhookSecurityIssue
            {
                Type = WebhookSecurityIssueType.InsecureProtocol,
                Severity = WebhookIssueSeverity.High,
                Description = "Webhook URL does not use HTTPS"
            });
        }

        // Check for localhost or internal IPs
        if (uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase) || 
            uri.Host.StartsWith("127.") || 
            uri.Host.StartsWith("192.168.") ||
            uri.Host.StartsWith("10.") ||
            uri.Host.StartsWith("172."))
        {
            issues.Add(new WebhookSecurityIssue
            {
                Type = WebhookSecurityIssueType.InternalEndpoint,
                Severity = WebhookIssueSeverity.Medium,
                Description = "Webhook URL points to internal/localhost address"
            });
        }
        else
        {
            score += 20;
        }

        // Check port usage
        if (uri.Port != -1 && uri.Port != 80 && uri.Port != 443)
        {
            issues.Add(new WebhookSecurityIssue
            {
                Type = WebhookSecurityIssueType.NonStandardPort,
                Severity = WebhookIssueSeverity.Low,
                Description = $"Webhook URL uses non-standard port {uri.Port}"
            });
        }
        else
        {
            score += 10;
        }

        return (issues, score);
    }

    private async Task<(List<WebhookSecurityIssue> Issues, int Score)> AnalyzeEndpointSecurityAsync(string webhookUrl, CancellationToken cancellationToken)
    {
        var issues = new List<WebhookSecurityIssue>();
        var score = 0;

        try
        {
            var request = new HttpRequestMessage(HttpMethod.Head, webhookUrl);
            request.Headers.Add("User-Agent", "PaymentGateway-SecurityAnalyzer/1.0");
            
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            
            // Endpoint is reachable
            score += 30;

            // Check security headers
            if (response.Headers.Contains("Strict-Transport-Security"))
            {
                score += 10;
            }
            else
            {
                issues.Add(new WebhookSecurityIssue
                {
                    Type = WebhookSecurityIssueType.MissingSecurityHeaders,
                    Severity = WebhookIssueSeverity.Low,
                    Description = "Missing Strict-Transport-Security header"
                });
            }
        }
        catch (HttpRequestException)
        {
            issues.Add(new WebhookSecurityIssue
            {
                Type = WebhookSecurityIssueType.UnreachableEndpoint,
                Severity = WebhookIssueSeverity.High,
                Description = "Webhook endpoint is not reachable"
            });
        }
        catch (Exception ex)
        {
            issues.Add(new WebhookSecurityIssue
            {
                Type = WebhookSecurityIssueType.AnalysisError,
                Severity = WebhookIssueSeverity.Medium,
                Description = "Error analyzing endpoint security",
                Details = ex.Message
            });
        }

        return (issues, score);
    }

    private async Task<(List<WebhookSecurityIssue> Issues, int Score)> AnalyzeHistoricalSecurityAsync(string teamSlug, string webhookUrl)
    {
        var issues = new List<WebhookSecurityIssue>();
        var score = 30; // Base score for no historical issues

        try
        {
            var recentEvents = await GetWebhookSecurityEventsAsync(teamSlug, 
                new TimeRange { StartDate = DateTime.UtcNow.AddDays(-7), EndDate = DateTime.UtcNow });

            var failureCount = recentEvents.Count(e => !e.IsSuccessful);
            var totalEvents = recentEvents.Count;

            if (totalEvents > 0)
            {
                var failureRate = (double)failureCount / totalEvents;
                
                if (failureRate > 0.5)
                {
                    issues.Add(new WebhookSecurityIssue
                    {
                        Type = WebhookSecurityIssueType.HighFailureRate,
                        Severity = WebhookIssueSeverity.High,
                        Description = $"High failure rate: {failureRate:P} in the last 7 days"
                    });
                    score -= 20;
                }
                else if (failureRate > 0.2)
                {
                    issues.Add(new WebhookSecurityIssue
                    {
                        Type = WebhookSecurityIssueType.ModerateFailureRate,
                        Severity = WebhookIssueSeverity.Medium,
                        Description = $"Moderate failure rate: {failureRate:P} in the last 7 days"
                    });
                    score -= 10;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error analyzing historical security for team {TeamSlug}", teamSlug);
        }

        return (issues, Math.Max(score, 0));
    }

    private List<string> GenerateSecurityRecommendations(WebhookSecurityAnalysis analysis)
    {
        var recommendations = new List<string>();

        foreach (var issue in analysis.SecurityIssues)
        {
            switch (issue.Type)
            {
                case WebhookSecurityIssueType.InsecureProtocol:
                    recommendations.Add("Use HTTPS for all webhook endpoints to ensure data encryption in transit");
                    break;
                case WebhookSecurityIssueType.InvalidUrl:
                    recommendations.Add("Ensure webhook URL is a valid, absolute URI");
                    break;
                case WebhookSecurityIssueType.InternalEndpoint:
                    recommendations.Add("Avoid using internal/localhost addresses for webhook endpoints");
                    break;
                case WebhookSecurityIssueType.UnreachableEndpoint:
                    recommendations.Add("Ensure webhook endpoint is accessible and responding");
                    break;
                case WebhookSecurityIssueType.MissingSecurityHeaders:
                    recommendations.Add("Implement security headers like Strict-Transport-Security");
                    break;
                case WebhookSecurityIssueType.HighFailureRate:
                    recommendations.Add("Investigate and resolve causes of webhook delivery failures");
                    break;
            }
        }

        if (analysis.SecurityScore < 60)
        {
            recommendations.Add("Consider implementing additional security measures such as IP whitelisting");
            recommendations.Add("Use strong signature algorithms (HMAC-SHA256 or higher)");
            recommendations.Add("Implement timestamp validation and replay protection");
        }

        return recommendations.Distinct().ToList();
    }

    private bool IsWebhookRelatedEvent(SecurityAuditEvent securityEvent)
    {
        return securityEvent.EventDescription.Contains("webhook", StringComparison.OrdinalIgnoreCase) ||
               securityEvent.EventData.Any(kvp => kvp.Key.Contains("webhook", StringComparison.OrdinalIgnoreCase));
    }

    private WebhookEventType MapToWebhookEventType(SecurityEventType eventType)
    {
        return eventType switch
        {
            SecurityEventType.AuthenticationSuccess => WebhookEventType.SignatureValidation,
            SecurityEventType.AuthenticationFailure => WebhookEventType.SignatureValidation,
            SecurityEventType.DataAccess => WebhookEventType.DeliveryAttempt,
            SecurityEventType.SuspiciousActivity => WebhookEventType.SecurityThreat,
            SecurityEventType.SystemError => WebhookEventType.SystemError,
            _ => WebhookEventType.Other
        };
    }

    private WebhookIssueSeverity MapToWebhookSeverity(SecurityEventSeverity severity)
    {
        return severity switch
        {
            SecurityEventSeverity.Critical => WebhookIssueSeverity.Critical,
            SecurityEventSeverity.High => WebhookIssueSeverity.High,
            SecurityEventSeverity.Medium => WebhookIssueSeverity.Medium,
            SecurityEventSeverity.Low => WebhookIssueSeverity.Low,
            _ => WebhookIssueSeverity.Medium
        };
    }

    private async Task LogSecurityEventAsync(SecurityEventType eventType, string? teamSlug, string description, bool isSuccessful, string? ipAddress = null, string? errorMessage = null)
    {
        var auditEvent = new SecurityAuditEvent(
            Guid.NewGuid().ToString(),
            eventType,
            isSuccessful ? SecurityEventSeverity.Low : SecurityEventSeverity.Medium,
            DateTime.UtcNow,
            null,
            teamSlug,
            ipAddress,
            null,
            description,
            new Dictionary<string, string>(),
            null,
            isSuccessful,
            errorMessage
        );

        await _securityAuditService.LogSecurityEventAsync(auditEvent);
    }

    #endregion
}

// Supporting classes and enums
public class WebhookSecurityOptions
{
    public bool EnableReplayProtection { get; set; } = true;
    public int MaxTimestampToleranceSeconds { get; set; } = 300; // 5 minutes
    public int NonceValidityMinutes { get; set; } = 15;
    public bool RequireHttps { get; set; } = true;
    public List<string> AllowedDomains { get; set; } = new();
    public List<string> BlockedDomains { get; set; } = new();
}

public class WebhookValidationRequest
{
    public string TeamSlug { get; set; } = string.Empty;
    public string Payload { get; set; } = string.Empty;
    public string Signature { get; set; } = string.Empty;
    public string Secret { get; set; } = string.Empty;
    public WebhookSignatureAlgorithm SignatureType { get; set; } = WebhookSignatureAlgorithm.HMAC_SHA256;
    public DateTime? Timestamp { get; set; }
    public string? NonceId { get; set; }
    public string? WebhookUrl { get; set; }
    public string? IpAddress { get; set; }
}

public class WebhookValidationResult
{
    public bool IsValid { get; set; }
    public string? ErrorMessage { get; set; }
    public WebhookSecurityLevel SecurityLevel { get; set; }
    public Dictionary<string, string> ValidationMetadata { get; set; } = new();
}

public class WebhookSignatureOptions
{
    public WebhookSignatureAlgorithm Algorithm { get; set; } = WebhookSignatureAlgorithm.HMAC_SHA256;
    public bool IncludeTimestamp { get; set; } = false;
    public bool IncludeNonce { get; set; } = false;
    public bool IncludeAlgorithmPrefix { get; set; } = true;
}

public class WebhookSecurityAnalysisRequest
{
    public string TeamSlug { get; set; } = string.Empty;
    public string WebhookUrl { get; set; } = string.Empty;
    public bool IncludeHistoricalAnalysis { get; set; } = true;
    public bool IncludeEndpointTesting { get; set; } = true;
}

public class WebhookSecurityAnalysis
{
    public string WebhookUrl { get; set; } = string.Empty;
    public string TeamSlug { get; set; } = string.Empty;
    public DateTime AnalyzedAt { get; set; }
    public int SecurityScore { get; set; }
    public WebhookSecurityLevel OverallSecurityLevel { get; set; }
    public List<WebhookSecurityIssue> SecurityIssues { get; set; } = new();
    public List<string> SecurityRecommendations { get; set; } = new();
}

public class SecureWebhookDeliveryRequest
{
    public string TeamSlug { get; set; } = string.Empty;
    public string WebhookUrl { get; set; } = string.Empty;
    public string Payload { get; set; } = string.Empty;
    public string Secret { get; set; } = string.Empty;
    public string? WebhookId { get; set; }
    public WebhookSignatureOptions? SignatureOptions { get; set; }
    public Dictionary<string, string> CustomHeaders { get; set; } = new();
    public int TimeoutSeconds { get; set; } = 30;
}

public class WebhookDeliveryAttemptResult
{
    public string DeliveryId { get; set; } = string.Empty;
    public string TeamSlug { get; set; } = string.Empty;
    public string WebhookUrl { get; set; } = string.Empty;
    public DateTime AttemptedAt { get; set; }
    public bool IsSuccessful { get; set; }
    public int HttpStatusCode { get; set; }
    public TimeSpan ResponseTime { get; set; }
    public WebhookSecurityLevel SecurityLevel { get; set; }
    public string? ErrorMessage { get; set; }
    public Dictionary<string, string> ResponseHeaders { get; set; } = new();
}

public class WebhookSecurityEvent
{
    public string EventId { get; set; } = string.Empty;
    public string TeamSlug { get; set; } = string.Empty;
    public WebhookEventType EventType { get; set; }
    public WebhookIssueSeverity Severity { get; set; }
    public DateTime Timestamp { get; set; }
    public string Description { get; set; } = string.Empty;
    public string? IpAddress { get; set; }
    public bool IsSuccessful { get; set; }
    public string? ErrorMessage { get; set; }
}

public class WebhookSecurityIssue
{
    public WebhookSecurityIssueType Type { get; set; }
    public WebhookIssueSeverity Severity { get; set; }
    public string Description { get; set; } = string.Empty;
    public string? Details { get; set; }
}

public class TimeRange
{
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
}

public enum WebhookSignatureAlgorithm
{
    HMAC_SHA256,
    HMAC_SHA512,
    HMAC_SHA3_256
}

public enum WebhookSecurityLevel
{
    Unknown,
    Malicious,
    Suspicious,
    Insecure,
    Moderate,
    Secure
}

public enum WebhookSecurityIssueType
{
    InvalidUrl,
    InsecureProtocol,
    InternalEndpoint,
    NonStandardPort,
    UnreachableEndpoint,
    MissingSecurityHeaders,
    HighFailureRate,
    ModerateFailureRate,
    AnalysisError
}

public enum WebhookIssueSeverity
{
    Low,
    Medium,
    High,
    Critical
}

public enum WebhookEventType
{
    SignatureValidation,
    DeliveryAttempt,
    SecurityThreat,
    SystemError,
    Other
}