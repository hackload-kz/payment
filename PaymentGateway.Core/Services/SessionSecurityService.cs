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
/// Session Security Service for managing secure payment session lifecycle
/// 
/// This service implements:
/// - Secure session creation and validation
/// - Session fixation protection
/// - Session timeout management
/// - Device binding and validation
/// - Session hijacking detection
/// - Secure session data encryption
/// </summary>
public class SessionSecurityService
{
    private readonly ILogger<SessionSecurityService> _logger;
    private readonly IConfiguration _configuration;
    private readonly IMemoryCache _memoryCache;

    // Session configuration
    private readonly int _sessionTimeoutMinutes;
    private readonly int _sessionWarningMinutes;
    private readonly int _maxConcurrentSessions;
    private readonly bool _enableDeviceBinding;
    private readonly bool _enableSessionFixationProtection;
    private readonly bool _enableHijackingDetection;
    private readonly string _sessionEncryptionKey;

    // Session constants
    private const string SESSION_PREFIX = "payment_session:";
    private const string DEVICE_SESSIONS_PREFIX = "device_sessions:";
    private const string IP_SESSIONS_PREFIX = "ip_sessions:";

    public SessionSecurityService(
        ILogger<SessionSecurityService> logger,
        IConfiguration configuration,
        IMemoryCache memoryCache)
    {
        _logger = logger;
        _configuration = configuration;
        _memoryCache = memoryCache;

        // Load configuration
        _sessionTimeoutMinutes = _configuration.GetValue<int>("Session:TimeoutMinutes", 15);
        _sessionWarningMinutes = _configuration.GetValue<int>("Session:WarningMinutes", 12);
        _maxConcurrentSessions = _configuration.GetValue<int>("Session:MaxConcurrentSessions", 3);
        _enableDeviceBinding = _configuration.GetValue<bool>("Session:EnableDeviceBinding", true);
        _enableSessionFixationProtection = _configuration.GetValue<bool>("Session:EnableFixationProtection", true);
        _enableHijackingDetection = _configuration.GetValue<bool>("Session:EnableHijackingDetection", true);
        _sessionEncryptionKey = _configuration.GetValue<string>("Session:EncryptionKey") ?? GenerateDefaultEncryptionKey();
    }

    /// <summary>
    /// Create a new secure payment session
    /// </summary>
    public async Task<PaymentSessionResult> CreateSessionAsync(CreateSessionRequest request)
    {
        try
        {
            _logger.LogDebug("Creating new payment session for PaymentId: {PaymentId}, IP: {ClientIp}",
                request.PaymentId, request.ClientIp);

            // Generate secure session ID
            var sessionId = GenerateSecureSessionId();
            
            // Check for session fixation attempts
            if (_enableSessionFixationProtection && !string.IsNullOrEmpty(request.ExistingSessionId))
            {
                await InvalidateSessionAsync(request.ExistingSessionId, "Session fixation protection");
            }

            // Enforce concurrent session limits
            if (_maxConcurrentSessions > 0)
            {
                await EnforceConcurrentSessionLimitAsync(request.ClientIp, request.DeviceFingerprint);
            }

            // Create session data
            var sessionData = new PaymentSession
            {
                SessionId = sessionId,
                PaymentId = request.PaymentId,
                ClientIp = request.ClientIp,
                UserAgent = request.UserAgent,
                DeviceFingerprint = CalculateDeviceFingerprint(request),
                CreatedAt = DateTime.UtcNow,
                LastAccessedAt = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow.AddMinutes(_sessionTimeoutMinutes),
                IsActive = true,
                SecurityLevel = DetermineSecurityLevel(request),
                AllowedOperations = new List<string> { "VIEW_FORM", "SUBMIT_PAYMENT" }
            };

            // Encrypt and store session
            await StoreSessionAsync(sessionData);

            // Track session by IP and device
            await TrackSessionByIpAsync(request.ClientIp, sessionId);
            if (_enableDeviceBinding && !string.IsNullOrEmpty(sessionData.DeviceFingerprint))
            {
                await TrackSessionByDeviceAsync(sessionData.DeviceFingerprint, sessionId);
            }

            _logger.LogInformation("Payment session created successfully. SessionId: {SessionId}, PaymentId: {PaymentId}, SecurityLevel: {SecurityLevel}",
                sessionId, request.PaymentId, sessionData.SecurityLevel);

            return new PaymentSessionResult
            {
                Success = true,
                SessionId = sessionId,
                ExpiresAt = sessionData.ExpiresAt,
                WarningAt = sessionData.ExpiresAt.AddMinutes(-_sessionWarningMinutes),
                SecurityLevel = sessionData.SecurityLevel,
                AllowedOperations = sessionData.AllowedOperations
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating payment session for PaymentId: {PaymentId}", request.PaymentId);
            return new PaymentSessionResult
            {
                Success = false,
                ErrorMessage = "Failed to create secure session"
            };
        }
    }

    /// <summary>
    /// Validate and refresh an existing payment session
    /// </summary>
    public async Task<SessionValidationResult> ValidateSessionAsync(ValidateSessionRequest request)
    {
        try
        {
            _logger.LogDebug("Validating payment session: {SessionId}, IP: {ClientIp}",
                request.SessionId, request.ClientIp);

            // Retrieve session data
            var sessionData = await GetSessionAsync(request.SessionId);
            if (sessionData == null)
            {
                _logger.LogWarning("Session not found: {SessionId}", request.SessionId);
                return new SessionValidationResult
                {
                    IsValid = false,
                    FailureReason = "Session not found",
                    RequiresNewSession = true
                };
            }

            // Check if session is expired
            if (!sessionData.IsActive || DateTime.UtcNow > sessionData.ExpiresAt)
            {
                _logger.LogWarning("Session expired: {SessionId}, ExpiresAt: {ExpiresAt}",
                    request.SessionId, sessionData.ExpiresAt);
                
                await InvalidateSessionAsync(request.SessionId, "Session expired");
                return new SessionValidationResult
                {
                    IsValid = false,
                    FailureReason = "Session expired",
                    RequiresNewSession = true
                };
            }

            // Validate IP address consistency
            if (sessionData.ClientIp != request.ClientIp)
            {
                _logger.LogWarning("IP address mismatch for session: {SessionId}, Original: {OriginalIp}, Current: {CurrentIp}",
                    request.SessionId, sessionData.ClientIp, request.ClientIp);

                if (_enableHijackingDetection)
                {
                    await InvalidateSessionAsync(request.SessionId, "IP address mismatch detected");
                    return new SessionValidationResult
                    {
                        IsValid = false,
                        FailureReason = "Session security violation",
                        RequiresNewSession = true
                    };
                }
            }

            // Validate User-Agent consistency
            if (!string.IsNullOrEmpty(sessionData.UserAgent) && 
                sessionData.UserAgent != request.UserAgent)
            {
                _logger.LogWarning("User-Agent mismatch for session: {SessionId}, Original: {OriginalUA}, Current: {CurrentUA}",
                    request.SessionId, sessionData.UserAgent, request.UserAgent);

                if (_enableHijackingDetection)
                {
                    await InvalidateSessionAsync(request.SessionId, "User-Agent mismatch detected");
                    return new SessionValidationResult
                    {
                        IsValid = false,
                        FailureReason = "Session security violation",
                        RequiresNewSession = true
                    };
                }
            }

            // Validate device fingerprint (if enabled)
            if (_enableDeviceBinding && !string.IsNullOrEmpty(sessionData.DeviceFingerprint))
            {
                var currentFingerprint = CalculateDeviceFingerprint(new CreateSessionRequest
                {
                    UserAgent = request.UserAgent,
                    AcceptLanguage = request.AcceptLanguage,
                    AcceptEncoding = request.AcceptEncoding
                });

                if (sessionData.DeviceFingerprint != currentFingerprint)
                {
                    _logger.LogWarning("Device fingerprint mismatch for session: {SessionId}",
                        request.SessionId);

                    await InvalidateSessionAsync(request.SessionId, "Device fingerprint mismatch");
                    return new SessionValidationResult
                    {
                        IsValid = false,
                        FailureReason = "Device validation failed",
                        RequiresNewSession = true
                    };
                }
            }

            // Check operation permissions
            if (!string.IsNullOrEmpty(request.RequestedOperation) &&
                !sessionData.AllowedOperations.Contains(request.RequestedOperation))
            {
                _logger.LogWarning("Operation not allowed for session: {SessionId}, Operation: {Operation}",
                    request.SessionId, request.RequestedOperation);

                return new SessionValidationResult
                {
                    IsValid = false,
                    FailureReason = "Operation not permitted",
                    RequiresNewSession = false
                };
            }

            // Update session access time
            sessionData.LastAccessedAt = DateTime.UtcNow;
            
            // Extend session if needed (sliding expiration)
            var remainingTime = sessionData.ExpiresAt - DateTime.UtcNow;
            if (remainingTime.TotalMinutes < _sessionWarningMinutes)
            {
                sessionData.ExpiresAt = DateTime.UtcNow.AddMinutes(_sessionTimeoutMinutes);
                _logger.LogDebug("Session extended: {SessionId}, NewExpiresAt: {ExpiresAt}",
                    request.SessionId, sessionData.ExpiresAt);
            }

            // Update stored session
            await StoreSessionAsync(sessionData);

            _logger.LogDebug("Session validation successful: {SessionId}, PaymentId: {PaymentId}",
                request.SessionId, sessionData.PaymentId);

            return new SessionValidationResult
            {
                IsValid = true,
                SessionData = sessionData,
                ExpiresAt = sessionData.ExpiresAt,
                WarningAt = sessionData.ExpiresAt.AddMinutes(-_sessionWarningMinutes),
                RequiresWarning = (sessionData.ExpiresAt - DateTime.UtcNow).TotalMinutes <= _sessionWarningMinutes
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating session: {SessionId}", request.SessionId);
            return new SessionValidationResult
            {
                IsValid = false,
                FailureReason = "Session validation error",
                RequiresNewSession = true
            };
        }
    }

    /// <summary>
    /// Invalidate a payment session
    /// </summary>
    public async Task<bool> InvalidateSessionAsync(string sessionId, string reason = "Manual invalidation")
    {
        try
        {
            _logger.LogInformation("Invalidating session: {SessionId}, Reason: {Reason}", sessionId, reason);

            var sessionData = await GetSessionAsync(sessionId);
            if (sessionData != null)
            {
                // Remove from tracking
                await RemoveSessionTrackingAsync(sessionData);
                
                // Remove from cache
                _memoryCache.Remove(SESSION_PREFIX + sessionId);
                
                _logger.LogInformation("Session invalidated successfully: {SessionId}", sessionId);
                return true;
            }

            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error invalidating session: {SessionId}", sessionId);
            return false;
        }
    }

    /// <summary>
    /// Clean up expired sessions
    /// </summary>
    public async Task CleanupExpiredSessionsAsync()
    {
        try
        {
            _logger.LogDebug("Starting expired session cleanup");

            // Note: In a real implementation, you'd iterate through stored sessions
            // Since we're using MemoryCache, expired entries are automatically removed
            // This is a placeholder for database-based session storage

            _logger.LogDebug("Expired session cleanup completed");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during session cleanup");
        }
    }

    // Private helper methods

    private string GenerateSecureSessionId()
    {
        using var rng = RandomNumberGenerator.Create();
        var bytes = new byte[32];
        rng.GetBytes(bytes);
        return Convert.ToBase64String(bytes).Replace("/", "_").Replace("+", "-").Replace("=", "");
    }

    private string GenerateDefaultEncryptionKey()
    {
        using var rng = RandomNumberGenerator.Create();
        var bytes = new byte[32];
        rng.GetBytes(bytes);
        return Convert.ToBase64String(bytes);
    }

    private string CalculateDeviceFingerprint(CreateSessionRequest request)
    {
        var fingerprintData = $"{request.UserAgent}|{request.AcceptLanguage}|{request.AcceptEncoding}|{request.ScreenResolution}";
        
        using var sha256 = SHA256.Create();
        var hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(fingerprintData));
        return Convert.ToBase64String(hash);
    }

    private SessionSecurityLevel DetermineSecurityLevel(CreateSessionRequest request)
    {
        var score = 0;

        // Check for HTTPS
        if (request.IsHttps) score += 2;

        // Check for complete device info
        if (!string.IsNullOrEmpty(request.UserAgent)) score++;
        if (!string.IsNullOrEmpty(request.AcceptLanguage)) score++;
        if (!string.IsNullOrEmpty(request.ScreenResolution)) score++;

        // Check for suspicious patterns
        if (request.UserAgent?.Contains("bot", StringComparison.OrdinalIgnoreCase) == true) score -= 2;

        return score switch
        {
            >= 4 => SessionSecurityLevel.High,
            >= 2 => SessionSecurityLevel.Medium,
            _ => SessionSecurityLevel.Low
        };
    }

    private async Task StoreSessionAsync(PaymentSession sessionData)
    {
        // Encrypt sensitive session data
        var encryptedData = EncryptSessionData(sessionData);
        
        var cacheKey = SESSION_PREFIX + sessionData.SessionId;
        var expiry = sessionData.ExpiresAt - DateTime.UtcNow;
        
        _memoryCache.Set(cacheKey, encryptedData, expiry);
    }

    private async Task<PaymentSession?> GetSessionAsync(string sessionId)
    {
        var cacheKey = SESSION_PREFIX + sessionId;
        
        if (_memoryCache.TryGetValue(cacheKey, out string? encryptedData))
        {
            return DecryptSessionData(encryptedData);
        }

        return null;
    }

    private string EncryptSessionData(PaymentSession sessionData)
    {
        var json = JsonSerializer.Serialize(sessionData);
        var keyBytes = Convert.FromBase64String(_sessionEncryptionKey);
        
        using var aes = Aes.Create();
        aes.Key = keyBytes;
        aes.GenerateIV();

        using var encryptor = aes.CreateEncryptor();
        var jsonBytes = Encoding.UTF8.GetBytes(json);
        var encryptedBytes = encryptor.TransformFinalBlock(jsonBytes, 0, jsonBytes.Length);
        
        var result = new byte[aes.IV.Length + encryptedBytes.Length];
        Array.Copy(aes.IV, 0, result, 0, aes.IV.Length);
        Array.Copy(encryptedBytes, 0, result, aes.IV.Length, encryptedBytes.Length);
        
        return Convert.ToBase64String(result);
    }

    private PaymentSession? DecryptSessionData(string encryptedData)
    {
        try
        {
            var data = Convert.FromBase64String(encryptedData);
            var keyBytes = Convert.FromBase64String(_sessionEncryptionKey);
            
            using var aes = Aes.Create();
            aes.Key = keyBytes;
            
            var iv = new byte[aes.IV.Length];
            var encryptedBytes = new byte[data.Length - iv.Length];
            
            Array.Copy(data, 0, iv, 0, iv.Length);
            Array.Copy(data, iv.Length, encryptedBytes, 0, encryptedBytes.Length);
            
            aes.IV = iv;
            using var decryptor = aes.CreateDecryptor();
            var decryptedBytes = decryptor.TransformFinalBlock(encryptedBytes, 0, encryptedBytes.Length);
            
            var json = Encoding.UTF8.GetString(decryptedBytes);
            return JsonSerializer.Deserialize<PaymentSession>(json);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error decrypting session data");
            return null;
        }
    }

    private async Task EnforceConcurrentSessionLimitAsync(string clientIp, string? deviceFingerprint)
    {
        var ipSessionsKey = IP_SESSIONS_PREFIX + clientIp;
        var sessions = _memoryCache.Get<List<string>>(ipSessionsKey) ?? new List<string>();
        
        // Clean up expired sessions from the list
        var validSessions = new List<string>();
        foreach (var sessionId in sessions)
        {
            var session = await GetSessionAsync(sessionId);
            if (session != null && session.IsActive && DateTime.UtcNow <= session.ExpiresAt)
            {
                validSessions.Add(sessionId);
            }
        }

        // If we're at the limit, invalidate the oldest session
        while (validSessions.Count >= _maxConcurrentSessions)
        {
            var oldestSessionId = validSessions.First();
            await InvalidateSessionAsync(oldestSessionId, "Concurrent session limit exceeded");
            validSessions.RemoveAt(0);
        }

        _memoryCache.Set(ipSessionsKey, validSessions, TimeSpan.FromHours(1));
    }

    private async Task TrackSessionByIpAsync(string clientIp, string sessionId)
    {
        var ipSessionsKey = IP_SESSIONS_PREFIX + clientIp;
        var sessions = _memoryCache.Get<List<string>>(ipSessionsKey) ?? new List<string>();
        sessions.Add(sessionId);
        _memoryCache.Set(ipSessionsKey, sessions, TimeSpan.FromHours(1));
    }

    private async Task TrackSessionByDeviceAsync(string deviceFingerprint, string sessionId)
    {
        var deviceSessionsKey = DEVICE_SESSIONS_PREFIX + deviceFingerprint;
        var sessions = _memoryCache.Get<List<string>>(deviceSessionsKey) ?? new List<string>();
        sessions.Add(sessionId);
        _memoryCache.Set(deviceSessionsKey, sessions, TimeSpan.FromHours(1));
    }

    private async Task RemoveSessionTrackingAsync(PaymentSession sessionData)
    {
        // Remove from IP tracking
        var ipSessionsKey = IP_SESSIONS_PREFIX + sessionData.ClientIp;
        var ipSessions = _memoryCache.Get<List<string>>(ipSessionsKey);
        if (ipSessions != null)
        {
            ipSessions.Remove(sessionData.SessionId);
            _memoryCache.Set(ipSessionsKey, ipSessions, TimeSpan.FromHours(1));
        }

        // Remove from device tracking
        if (!string.IsNullOrEmpty(sessionData.DeviceFingerprint))
        {
            var deviceSessionsKey = DEVICE_SESSIONS_PREFIX + sessionData.DeviceFingerprint;
            var deviceSessions = _memoryCache.Get<List<string>>(deviceSessionsKey);
            if (deviceSessions != null)
            {
                deviceSessions.Remove(sessionData.SessionId);
                _memoryCache.Set(deviceSessionsKey, deviceSessions, TimeSpan.FromHours(1));
            }
        }
    }
}

// Supporting classes

public class CreateSessionRequest
{
    public string PaymentId { get; set; } = string.Empty;
    public string ClientIp { get; set; } = string.Empty;
    public string UserAgent { get; set; } = string.Empty;
    public string? AcceptLanguage { get; set; }
    public string? AcceptEncoding { get; set; }
    public string? ScreenResolution { get; set; }
    public bool IsHttps { get; set; }
    public string? ExistingSessionId { get; set; }
    public string? DeviceFingerprint { get; set; }
}

public class ValidateSessionRequest
{
    public string SessionId { get; set; } = string.Empty;
    public string ClientIp { get; set; } = string.Empty;
    public string UserAgent { get; set; } = string.Empty;
    public string? AcceptLanguage { get; set; }
    public string? AcceptEncoding { get; set; }
    public string? RequestedOperation { get; set; }
}

public class PaymentSessionResult
{
    public bool Success { get; set; }
    public string? SessionId { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public DateTime? WarningAt { get; set; }
    public SessionSecurityLevel SecurityLevel { get; set; }
    public List<string> AllowedOperations { get; set; } = new();
    public string? ErrorMessage { get; set; }
}

public class SessionValidationResult
{
    public bool IsValid { get; set; }
    public string? FailureReason { get; set; }
    public bool RequiresNewSession { get; set; }
    public bool RequiresWarning { get; set; }
    public PaymentSession? SessionData { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public DateTime? WarningAt { get; set; }
}

public class PaymentSession
{
    public string SessionId { get; set; } = string.Empty;
    public string PaymentId { get; set; } = string.Empty;
    public string ClientIp { get; set; } = string.Empty;
    public string UserAgent { get; set; } = string.Empty;
    public string? DeviceFingerprint { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime LastAccessedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public bool IsActive { get; set; }
    public SessionSecurityLevel SecurityLevel { get; set; }
    public List<string> AllowedOperations { get; set; } = new();
    public Dictionary<string, object> CustomData { get; set; } = new();
}

public enum SessionSecurityLevel
{
    Low = 1,
    Medium = 2,
    High = 3
}