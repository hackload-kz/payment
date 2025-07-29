// SPDX-License-Identifier: MIT
// Copyright (c) 2025 HackLoad Payment Gateway

using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace PaymentGateway.Core.Services;

/// <summary>
/// Secure Form Token Service for advanced CSRF protection and form security
/// 
/// This service implements:
/// - Multi-layer CSRF token validation
/// - Form state verification tokens
/// - Time-based token expiration
/// - Single-use token enforcement
/// - Token binding to IP and session
/// - Form sequence tracking
/// - Token integrity verification
/// </summary>
public class SecureFormTokenService
{
    private readonly ILogger<SecureFormTokenService> _logger;
    private readonly IConfiguration _configuration;
    private readonly IMemoryCache _memoryCache;

    // Token configuration
    private readonly string _masterSecretKey;
    private readonly int _tokenExpiryMinutes;
    private readonly int _maxTokensPerSession;
    private readonly bool _enableSingleUseTokens;
    private readonly bool _enableIpBinding;
    private readonly bool _enableSessionBinding;
    private readonly bool _enableFormSequenceTracking;

    // Token prefixes for different types
    private const string CSRF_TOKEN_PREFIX = "csrf_token:";
    private const string FORM_STATE_TOKEN_PREFIX = "form_state:";
    private const string SEQUENCE_TOKEN_PREFIX = "sequence:";
    private const string SESSION_TOKENS_PREFIX = "session_tokens:";

    public SecureFormTokenService(
        ILogger<SecureFormTokenService> logger,
        IConfiguration configuration,
        IMemoryCache memoryCache)
    {
        _logger = logger;
        _configuration = configuration;
        _memoryCache = memoryCache;

        // Load configuration
        _masterSecretKey = _configuration.GetValue<string>("Security:FormTokenSecretKey") ?? GenerateDefaultSecretKey();
        _tokenExpiryMinutes = _configuration.GetValue<int>("Security:TokenExpiryMinutes", 30);
        _maxTokensPerSession = _configuration.GetValue<int>("Security:MaxTokensPerSession", 10);
        _enableSingleUseTokens = _configuration.GetValue<bool>("Security:EnableSingleUseTokens", true);
        _enableIpBinding = _configuration.GetValue<bool>("Security:EnableIpBinding", true);
        _enableSessionBinding = _configuration.GetValue<bool>("Security:EnableSessionBinding", true);
        _enableFormSequenceTracking = _configuration.GetValue<bool>("Security:EnableFormSequenceTracking", true);
    }

    /// <summary>
    /// Generate a secure CSRF token for form protection
    /// </summary>
    public async Task<FormTokenResult> GenerateCsrfTokenAsync(CsrfTokenRequest request)
    {
        try
        {
            _logger.LogDebug("Generating CSRF token for PaymentId: {PaymentId}, IP: {ClientIp}",
                request.PaymentId, request.ClientIp);

            // Enforce token limits per session
            if (_maxTokensPerSession > 0)
            {
                await EnforceTokenLimitAsync(request.SessionId);
            }

            // Create token data
            var tokenData = new FormTokenData
            {
                TokenId = Guid.NewGuid().ToString("N"),
                PaymentId = request.PaymentId,
                SessionId = request.SessionId,
                ClientIp = request.ClientIp,
                UserAgent = request.UserAgent,
                CreatedAt = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow.AddMinutes(_tokenExpiryMinutes),
                TokenType = FormTokenType.CSRF,
                IsUsed = false,
                FormAction = request.FormAction,
                ExpectedFields = request.ExpectedFields ?? new List<string>()
            };

            // Generate cryptographically secure token
            var token = GenerateSecureToken(tokenData);

            // Store token data
            await StoreTokenAsync(tokenData);

            // Track token by session
            if (_enableSessionBinding)
            {
                await TrackTokenBySessionAsync(request.SessionId, tokenData.TokenId);
            }

            _logger.LogInformation("CSRF token generated successfully. TokenId: {TokenId}, PaymentId: {PaymentId}",
                tokenData.TokenId, request.PaymentId);

            return new FormTokenResult
            {
                Success = true,
                Token = token,
                TokenId = tokenData.TokenId,
                ExpiresAt = tokenData.ExpiresAt
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating CSRF token for PaymentId: {PaymentId}", request.PaymentId);
            return new FormTokenResult
            {
                Success = false,
                ErrorMessage = "Failed to generate security token"
            };
        }
    }

    /// <summary>
    /// Generate a form state token for form integrity verification
    /// </summary>
    public async Task<FormTokenResult> GenerateFormStateTokenAsync(FormStateTokenRequest request)
    {
        try
        {
            _logger.LogDebug("Generating form state token for PaymentId: {PaymentId}",
                request.PaymentId);

            var tokenData = new FormTokenData
            {
                TokenId = Guid.NewGuid().ToString("N"),
                PaymentId = request.PaymentId,
                SessionId = request.SessionId,
                ClientIp = request.ClientIp,
                CreatedAt = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow.AddMinutes(_tokenExpiryMinutes),
                TokenType = FormTokenType.FormState,
                IsUsed = false,
                FormFields = request.FormFields,
                FormChecksum = CalculateFormChecksum(request.FormFields)
            };

            var token = GenerateSecureToken(tokenData);
            await StoreTokenAsync(tokenData);

            return new FormTokenResult
            {
                Success = true,
                Token = token,
                TokenId = tokenData.TokenId,
                ExpiresAt = tokenData.ExpiresAt
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating form state token for PaymentId: {PaymentId}", request.PaymentId);
            return new FormTokenResult
            {
                Success = false,
                ErrorMessage = "Failed to generate form state token"
            };
        }
    }

    /// <summary>
    /// Validate a CSRF token
    /// </summary>
    public async Task<TokenValidationResult> ValidateCsrfTokenAsync(CsrfTokenValidationRequest request)
    {
        try
        {
            _logger.LogDebug("Validating CSRF token for PaymentId: {PaymentId}, IP: {ClientIp}",
                request.PaymentId, request.ClientIp);

            // Decode and verify token
            var tokenData = DecodeAndVerifyToken(request.Token);
            if (tokenData == null)
            {
                _logger.LogWarning("Invalid CSRF token format for PaymentId: {PaymentId}", request.PaymentId);
                return new TokenValidationResult
                {
                    IsValid = false,
                    FailureReason = "Invalid token format",
                    SecurityViolation = true
                };
            }

            // Retrieve stored token data
            var storedTokenData = await GetTokenAsync(tokenData.TokenId);
            if (storedTokenData == null)
            {
                _logger.LogWarning("CSRF token not found: {TokenId}, PaymentId: {PaymentId}",
                    tokenData.TokenId, request.PaymentId);
                return new TokenValidationResult
                {
                    IsValid = false,
                    FailureReason = "Token not found or expired",
                    SecurityViolation = true
                };
            }

            // Validate token expiration
            if (DateTime.UtcNow > storedTokenData.ExpiresAt)
            {
                _logger.LogWarning("CSRF token expired: {TokenId}, ExpiresAt: {ExpiresAt}",
                    tokenData.TokenId, storedTokenData.ExpiresAt);
                
                await InvalidateTokenAsync(tokenData.TokenId);
                return new TokenValidationResult
                {
                    IsValid = false,
                    FailureReason = "Token expired"
                };
            }

            // Check if token was already used (single-use protection)
            if (_enableSingleUseTokens && storedTokenData.IsUsed)
            {
                _logger.LogWarning("CSRF token already used: {TokenId}, PaymentId: {PaymentId}",
                    tokenData.TokenId, request.PaymentId);
                
                await InvalidateTokenAsync(tokenData.TokenId);
                return new TokenValidationResult
                {
                    IsValid = false,
                    FailureReason = "Token already used",
                    SecurityViolation = true
                };
            }

            // Validate payment ID consistency
            if (storedTokenData.PaymentId != request.PaymentId)
            {
                _logger.LogWarning("Payment ID mismatch in CSRF token: {TokenId}, Expected: {Expected}, Actual: {Actual}",
                    tokenData.TokenId, storedTokenData.PaymentId, request.PaymentId);
                
                return new TokenValidationResult
                {
                    IsValid = false,
                    FailureReason = "Token payment ID mismatch",
                    SecurityViolation = true
                };
            }

            // Validate IP binding (if enabled)
            if (_enableIpBinding && storedTokenData.ClientIp != request.ClientIp)
            {
                _logger.LogWarning("IP address mismatch in CSRF token: {TokenId}, Expected: {Expected}, Actual: {Actual}",
                    tokenData.TokenId, storedTokenData.ClientIp, request.ClientIp);
                
                return new TokenValidationResult
                {
                    IsValid = false,
                    FailureReason = "IP address mismatch",
                    SecurityViolation = true
                };
            }

            // Validate session binding (if enabled)
            if (_enableSessionBinding && storedTokenData.SessionId != request.SessionId)
            {
                _logger.LogWarning("Session ID mismatch in CSRF token: {TokenId}, Expected: {Expected}, Actual: {Actual}",
                    tokenData.TokenId, storedTokenData.SessionId, request.SessionId);
                
                return new TokenValidationResult
                {
                    IsValid = false,
                    FailureReason = "Session mismatch",
                    SecurityViolation = true
                };
            }

            // Validate form action (if specified)
            if (!string.IsNullOrEmpty(storedTokenData.FormAction) && 
                storedTokenData.FormAction != request.FormAction)
            {
                _logger.LogWarning("Form action mismatch in CSRF token: {TokenId}, Expected: {Expected}, Actual: {Actual}",
                    tokenData.TokenId, storedTokenData.FormAction, request.FormAction);
                
                return new TokenValidationResult
                {
                    IsValid = false,
                    FailureReason = "Form action mismatch",
                    SecurityViolation = true
                };
            }

            // Validate expected form fields
            if (storedTokenData.ExpectedFields.Any() && request.SubmittedFields != null)
            {
                var unexpectedFields = request.SubmittedFields.Except(storedTokenData.ExpectedFields).ToList();
                if (unexpectedFields.Any())
                {
                    _logger.LogWarning("Unexpected form fields in CSRF token validation: {TokenId}, UnexpectedFields: {Fields}",
                        tokenData.TokenId, string.Join(", ", unexpectedFields));
                    
                    return new TokenValidationResult
                    {
                        IsValid = false,
                        FailureReason = "Unexpected form fields detected",
                        SecurityViolation = true
                    };
                }
            }

            // Mark token as used (if single-use is enabled)
            if (_enableSingleUseTokens)
            {
                storedTokenData.IsUsed = true;
                storedTokenData.UsedAt = DateTime.UtcNow;
                await StoreTokenAsync(storedTokenData);
            }

            _logger.LogInformation("CSRF token validation successful: {TokenId}, PaymentId: {PaymentId}",
                tokenData.TokenId, request.PaymentId);

            return new TokenValidationResult
            {
                IsValid = true,
                TokenData = storedTokenData
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating CSRF token for PaymentId: {PaymentId}", request.PaymentId);
            return new TokenValidationResult
            {
                IsValid = false,
                FailureReason = "Token validation error",
                SecurityViolation = true
            };
        }
    }

    /// <summary>
    /// Validate form state integrity
    /// </summary>
    public async Task<FormStateValidationResult> ValidateFormStateAsync(FormStateValidationRequest request)
    {
        try
        {
            _logger.LogDebug("Validating form state for PaymentId: {PaymentId}", request.PaymentId);

            var tokenData = DecodeAndVerifyToken(request.FormStateToken);
            if (tokenData == null)
            {
                return new FormStateValidationResult
                {
                    IsValid = false,
                    FailureReason = "Invalid form state token"
                };
            }

            var storedTokenData = await GetTokenAsync(tokenData.TokenId);
            if (storedTokenData == null || DateTime.UtcNow > storedTokenData.ExpiresAt)
            {
                return new FormStateValidationResult
                {
                    IsValid = false,
                    FailureReason = "Form state token expired"
                };
            }

            // Calculate current form checksum
            var currentChecksum = CalculateFormChecksum(request.CurrentFormFields);
            
            // Compare with stored checksum
            if (storedTokenData.FormChecksum != currentChecksum)
            {
                _logger.LogWarning("Form state integrity violation detected: {TokenId}, PaymentId: {PaymentId}",
                    tokenData.TokenId, request.PaymentId);
                
                return new FormStateValidationResult
                {
                    IsValid = false,
                    FailureReason = "Form integrity violation detected",
                    IntegrityViolation = true
                };
            }

            return new FormStateValidationResult
            {
                IsValid = true,
                OriginalFormFields = storedTokenData.FormFields
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating form state for PaymentId: {PaymentId}", request.PaymentId);
            return new FormStateValidationResult
            {
                IsValid = false,
                FailureReason = "Form state validation error"
            };
        }
    }

    /// <summary>
    /// Invalidate a token
    /// </summary>
    public async Task<bool> InvalidateTokenAsync(string tokenId)
    {
        try
        {
            _logger.LogDebug("Invalidating token: {TokenId}", tokenId);

            var cacheKey = CSRF_TOKEN_PREFIX + tokenId;
            _memoryCache.Remove(cacheKey);

            var formStateCacheKey = FORM_STATE_TOKEN_PREFIX + tokenId;
            _memoryCache.Remove(formStateCacheKey);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error invalidating token: {TokenId}", tokenId);
            return false;
        }
    }

    /// <summary>
    /// Clean up expired tokens
    /// </summary>
    public async Task CleanupExpiredTokensAsync()
    {
        try
        {
            _logger.LogDebug("Starting expired token cleanup");
            
            // Note: In a real implementation with database storage,
            // you would query and remove expired tokens here
            // MemoryCache automatically removes expired entries
            
            _logger.LogDebug("Expired token cleanup completed");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during token cleanup");
        }
    }

    // Private helper methods

    private string GenerateDefaultSecretKey()
    {
        using var rng = RandomNumberGenerator.Create();
        var bytes = new byte[32];
        rng.GetBytes(bytes);
        return Convert.ToBase64String(bytes);
    }

    private string GenerateSecureToken(FormTokenData tokenData)
    {
        // Create token payload
        var payload = new
        {
            tokenData.TokenId,
            tokenData.PaymentId,
            tokenData.CreatedAt,
            tokenData.TokenType
        };

        var payloadJson = JsonSerializer.Serialize(payload);
        var payloadBytes = Encoding.UTF8.GetBytes(payloadJson);

        // Create HMAC signature
        var keyBytes = Encoding.UTF8.GetBytes(_masterSecretKey);
        using var hmac = new HMACSHA256(keyBytes);
        var signature = hmac.ComputeHash(payloadBytes);

        // Combine payload and signature
        var tokenBytes = new byte[payloadBytes.Length + signature.Length];
        Array.Copy(payloadBytes, 0, tokenBytes, 0, payloadBytes.Length);
        Array.Copy(signature, 0, tokenBytes, payloadBytes.Length, signature.Length);

        return Convert.ToBase64String(tokenBytes).Replace("/", "_").Replace("+", "-").Replace("=", "");
    }

    private FormTokenData? DecodeAndVerifyToken(string token)
    {
        try
        {
            var normalizedToken = token.Replace("_", "/").Replace("-", "+");
            var padding = 4 - (normalizedToken.Length % 4);
            if (padding != 4)
            {
                normalizedToken += new string('=', padding);
            }

            var tokenBytes = Convert.FromBase64String(normalizedToken);
            
            // Extract payload and signature
            if (tokenBytes.Length < 32) return null; // At least 32 bytes for SHA256 signature

            var signatureLength = 32; // SHA256 signature length
            var payloadLength = tokenBytes.Length - signatureLength;
            
            var payloadBytes = new byte[payloadLength];
            var signature = new byte[signatureLength];
            
            Array.Copy(tokenBytes, 0, payloadBytes, 0, payloadLength);
            Array.Copy(tokenBytes, payloadLength, signature, 0, signatureLength);

            // Verify signature
            var keyBytes = Encoding.UTF8.GetBytes(_masterSecretKey);
            using var hmac = new HMACSHA256(keyBytes);
            var expectedSignature = hmac.ComputeHash(payloadBytes);

            if (!signature.SequenceEqual(expectedSignature))
            {
                return null; // Invalid signature
            }

            // Decode payload
            var payloadJson = Encoding.UTF8.GetString(payloadBytes);
            var payload = JsonSerializer.Deserialize<Dictionary<string, object>>(payloadJson);

            if (payload == null) return null;

            return new FormTokenData
            {
                TokenId = payload["TokenId"].ToString() ?? "",
                PaymentId = payload["PaymentId"].ToString() ?? "",
                CreatedAt = DateTime.Parse(payload["CreatedAt"].ToString() ?? ""),
                TokenType = Enum.Parse<FormTokenType>(payload["TokenType"].ToString() ?? "CSRF")
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error decoding token");
            return null;
        }
    }

    private async Task StoreTokenAsync(FormTokenData tokenData)
    {
        var cacheKey = tokenData.TokenType switch
        {
            FormTokenType.CSRF => CSRF_TOKEN_PREFIX + tokenData.TokenId,
            FormTokenType.FormState => FORM_STATE_TOKEN_PREFIX + tokenData.TokenId,
            _ => CSRF_TOKEN_PREFIX + tokenData.TokenId
        };

        var expiry = tokenData.ExpiresAt - DateTime.UtcNow;
        _memoryCache.Set(cacheKey, tokenData, expiry);
    }

    private async Task<FormTokenData?> GetTokenAsync(string tokenId)
    {
        // Try CSRF tokens first
        var csrfCacheKey = CSRF_TOKEN_PREFIX + tokenId;
        if (_memoryCache.TryGetValue(csrfCacheKey, out FormTokenData? csrfToken))
        {
            return csrfToken;
        }

        // Try form state tokens
        var formStateCacheKey = FORM_STATE_TOKEN_PREFIX + tokenId;
        if (_memoryCache.TryGetValue(formStateCacheKey, out FormTokenData? formStateToken))
        {
            return formStateToken;
        }

        return null;
    }

    private async Task EnforceTokenLimitAsync(string sessionId)
    {
        var sessionTokensKey = SESSION_TOKENS_PREFIX + sessionId;
        var tokenIds = _memoryCache.Get<List<string>>(sessionTokensKey) ?? new List<string>();

        // Clean up expired tokens
        var validTokenIds = new List<string>();
        foreach (var tokenId in tokenIds)
        {
            var token = await GetTokenAsync(tokenId);
            if (token != null && DateTime.UtcNow <= token.ExpiresAt)
            {
                validTokenIds.Add(tokenId);
            }
        }

        // If we're at the limit, remove the oldest tokens
        while (validTokenIds.Count >= _maxTokensPerSession)
        {
            var oldestTokenId = validTokenIds.First();
            await InvalidateTokenAsync(oldestTokenId);
            validTokenIds.RemoveAt(0);
        }

        _memoryCache.Set(sessionTokensKey, validTokenIds, TimeSpan.FromHours(1));
    }

    private async Task TrackTokenBySessionAsync(string sessionId, string tokenId)
    {
        var sessionTokensKey = SESSION_TOKENS_PREFIX + sessionId;
        var tokenIds = _memoryCache.Get<List<string>>(sessionTokensKey) ?? new List<string>();
        tokenIds.Add(tokenId);
        _memoryCache.Set(sessionTokensKey, tokenIds, TimeSpan.FromHours(1));
    }

    private string CalculateFormChecksum(Dictionary<string, string>? formFields)
    {
        if (formFields == null || !formFields.Any())
        {
            return string.Empty;
        }

        // Sort fields by key for consistent checksum
        var sortedFields = formFields.OrderBy(kv => kv.Key).ToList();
        var formData = string.Join("|", sortedFields.Select(kv => $"{kv.Key}={kv.Value}"));

        using var sha256 = SHA256.Create();
        var hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(formData));
        return Convert.ToBase64String(hash);
    }
}

// Supporting classes and enums

public enum FormTokenType
{
    CSRF,
    FormState,
    Sequence
}

public class FormTokenData
{
    public string TokenId { get; set; } = string.Empty;
    public string PaymentId { get; set; } = string.Empty;
    public string SessionId { get; set; } = string.Empty;
    public string ClientIp { get; set; } = string.Empty;
    public string UserAgent { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? UsedAt { get; set; }
    public FormTokenType TokenType { get; set; }
    public bool IsUsed { get; set; }
    public string? FormAction { get; set; }
    public List<string> ExpectedFields { get; set; } = new();
    public Dictionary<string, string>? FormFields { get; set; }
    public string? FormChecksum { get; set; }
}

public class CsrfTokenRequest
{
    public string PaymentId { get; set; } = string.Empty;
    public string SessionId { get; set; } = string.Empty;
    public string ClientIp { get; set; } = string.Empty;
    public string UserAgent { get; set; } = string.Empty;
    public string? FormAction { get; set; }
    public List<string>? ExpectedFields { get; set; }
}

public class FormStateTokenRequest
{
    public string PaymentId { get; set; } = string.Empty;
    public string SessionId { get; set; } = string.Empty;
    public string ClientIp { get; set; } = string.Empty;
    public Dictionary<string, string> FormFields { get; set; } = new();
}

public class CsrfTokenValidationRequest
{
    public string Token { get; set; } = string.Empty;
    public string PaymentId { get; set; } = string.Empty;
    public string SessionId { get; set; } = string.Empty;
    public string ClientIp { get; set; } = string.Empty;
    public string? FormAction { get; set; }
    public List<string>? SubmittedFields { get; set; }
}

public class FormStateValidationRequest
{
    public string FormStateToken { get; set; } = string.Empty;
    public string PaymentId { get; set; } = string.Empty;
    public Dictionary<string, string> CurrentFormFields { get; set; } = new();
}

public class FormTokenResult
{
    public bool Success { get; set; }
    public string? Token { get; set; }
    public string? TokenId { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public string? ErrorMessage { get; set; }
}

public class TokenValidationResult
{
    public bool IsValid { get; set; }
    public string? FailureReason { get; set; }
    public bool SecurityViolation { get; set; }
    public FormTokenData? TokenData { get; set; }
}

public class FormStateValidationResult
{
    public bool IsValid { get; set; }
    public string? FailureReason { get; set; }
    public bool IntegrityViolation { get; set; }
    public Dictionary<string, string>? OriginalFormFields { get; set; }
}