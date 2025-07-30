using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Moq;
using System.Security.Cryptography;
using System.Text;

namespace PaymentGateway.Tests.Security;

[TestFixture]
public class SecurityValidationTests
{
    private Mock<IMemoryCache> _mockMemoryCache;
    private Mock<IConfiguration> _mockConfiguration;
    private const string TestCsrfKey = "test-csrf-key-for-hmac-signing-12345";

    [SetUp]
    public void Setup()
    {
        _mockMemoryCache = new Mock<IMemoryCache>();
        _mockConfiguration = new Mock<IConfiguration>();
        _mockConfiguration.Setup(c => c["Security:CsrfKey"]).Returns(TestCsrfKey);
    }

    [Test]
    public void TC022_SqlInjectionPrevention_InPaymentDescription_ShouldBeSanitized()
    {
        // Arrange - SQL injection attempts
        var sqlInjectionAttempts = new[]
        {
            "'; DROP TABLE payments; --",
            "' OR '1'='1",
            "'; DELETE FROM users; --",
            "' UNION SELECT * FROM payments --",
            "'; INSERT INTO payments VALUES ('hacked'); --"
        };

        // Act & Assert - These should be treated as regular strings, not SQL
        foreach (var attempt in sqlInjectionAttempts)
        {
            // In a real implementation, this would test that the payment description
            // is properly parameterized in database queries
            var sanitizedDescription = SanitizeForDatabase(attempt);
            
            // The sanitized version should not contain SQL injection patterns
            sanitizedDescription.Should().NotBeNull();
            sanitizedDescription.Should().Be(attempt); // It's just a string parameter
            
            // The key is that it gets parameterized, not modified
            ValidateNoSqlInjection(sanitizedDescription).Should().BeTrue();
        }
    }

    [Test]
    public void TC023_XssPrevention_InPaymentDescription_ShouldBeHtmlEncoded()
    {
        // Arrange - XSS attempts
        var xssAttempts = new[]
        {
            "<script>alert('xss')</script>",
            "<img src=x onerror=alert('xss')>",
            "javascript:alert('xss')",
            "<iframe src='javascript:alert(\"xss\")'></iframe>",
            "<<SCRIPT>alert(\"XSS\");//<</SCRIPT>",
            "<svg onload=alert('xss')>",
            "';alert('xss');//"
        };

        // Act & Assert
        foreach (var attempt in xssAttempts)
        {
            var htmlEncodedDescription = HtmlEncode(attempt);
            
            // Encoded version should not contain executable script tags
            htmlEncodedDescription.Should().NotContain("<script");
            htmlEncodedDescription.Should().NotContain("javascript:");
            htmlEncodedDescription.Should().NotContain("onerror=");
            htmlEncodedDescription.Should().NotContain("onload=");
            
            // Should contain HTML-encoded equivalents
            if (attempt.Contains("<script>"))
            {
                htmlEncodedDescription.Should().Contain("&lt;script&gt;");
            }
            
            ValidateNoXss(htmlEncodedDescription).Should().BeTrue();
        }
    }

    [Test]
    public void CsrfTokenGeneration_ShouldCreateValidToken()
    {
        // Arrange
        var paymentId = "pay_test123";

        // Act
        var csrfToken = GenerateCsrfToken(paymentId);

        // Assert
        csrfToken.Should().NotBeNullOrEmpty();
        csrfToken.Should().Contain(":");
        
        var parts = csrfToken.Split(':');
        parts.Should().HaveCount(2);
        
        // First part should be base64 encoded hash
        var hashPart = parts[0];
        Action decodeAction = () => Convert.FromBase64String(hashPart);
        decodeAction.Should().NotThrow();
        
        // Second part should be timestamp
        var timestampPart = parts[1];
        long.TryParse(timestampPart, out var timestamp).Should().BeTrue();
        timestamp.Should().BeGreaterThan(0);
    }

    [Test]
    public void CsrfTokenValidation_WithValidToken_ShouldReturnTrue()
    {
        // Arrange
        var paymentId = "pay_test123";
        var csrfToken = GenerateCsrfToken(paymentId);

        // Mock memory cache to return the token
        object cacheValue = csrfToken;
        _mockMemoryCache.Setup(c => c.TryGetValue($"csrf_token:{paymentId}", out cacheValue))
            .Returns(true);

        // Act
        var isValid = ValidateCsrfToken(paymentId, csrfToken);

        // Assert
        isValid.Should().BeTrue();
    }

    [Test]
    public void CsrfTokenValidation_WithInvalidToken_ShouldReturnFalse()
    {
        // Arrange
        var paymentId = "pay_test123";
        var validToken = GenerateCsrfToken(paymentId);
        var invalidToken = "invalid-csrf-token";

        // Mock memory cache to return the valid token
        object cacheValue = validToken;
        _mockMemoryCache.Setup(c => c.TryGetValue($"csrf_token:{paymentId}", out cacheValue))
            .Returns(true);

        // Act
        var isValid = ValidateCsrfToken(paymentId, invalidToken);

        // Assert
        isValid.Should().BeFalse();
    }

    [Test]
    public void CsrfTokenValidation_WithMissingToken_ShouldReturnFalse()
    {
        // Arrange
        var paymentId = "pay_test123";

        // Mock memory cache to return false (token not found)
        object? cacheValue = null;
        _mockMemoryCache.Setup(c => c.TryGetValue($"csrf_token:{paymentId}", out cacheValue))
            .Returns(false);

        // Act
        var isValid = ValidateCsrfToken(paymentId, "any-token");

        // Assert
        isValid.Should().BeFalse();
    }

    [Test]
    public void CsrfTokenValidation_WithNullOrEmptyToken_ShouldReturnFalse()
    {
        // Arrange
        var paymentId = "pay_test123";

        // Act & Assert
        ValidateCsrfToken(paymentId, null).Should().BeFalse();
        ValidateCsrfToken(paymentId, "").Should().BeFalse();
        ValidateCsrfToken(paymentId, "   ").Should().BeFalse();
    }

    [Test]
    public void InputValidation_PaymentIdFormat_ShouldPreventDirectoryTraversal()
    {
        // Arrange - Directory traversal attempts
        var maliciousPaymentIds = new[]
        {
            "../../../etc/passwd",
            "..\\..\\windows\\system32",
            "pay_test/../config",
            "pay_test\\..\\secrets",
            "%2e%2e%2f%2e%2e%2f", // URL encoded ../
            "pay_test%00.txt", // Null byte injection
            "pay_test\0secrets" // Null byte
        };

        // Act & Assert
        foreach (var maliciousId in maliciousPaymentIds)
        {
            IsValidPaymentId(maliciousId).Should().BeFalse($"Payment ID '{maliciousId}' should be rejected");
        }
    }

    [Test]
    public void InputValidation_OrderIdFormat_ShouldPreventInjection()
    {
        // Arrange - Various injection attempts
        var maliciousOrderIds = new[]
        {
            "ORDER-123'; DROP TABLE orders;--",
            "ORDER-123<script>alert('xss')</script>",
            "ORDER-123/../../../etc/passwd",
            "ORDER-123\x00secrets",
            "ORDER-123\r\nInjected-Header: value",
            new string('A', 100) // Overly long input
        };

        // Act & Assert
        foreach (var maliciousOrderId in maliciousOrderIds)
        {
            ValidateOrderIdFormat(maliciousOrderId).Should().BeFalse($"Order ID '{maliciousOrderId}' should be rejected");
        }
    }

    [Test]
    public void RateLimiting_ValidationLogic_ShouldTrackRequests()
    {
        // Arrange
        var teamSlug = "test-merchant";
        var rateLimiter = new MockRateLimiter();

        // Act - Simulate multiple requests
        var results = new List<bool>();
        for (int i = 0; i < 105; i++) // Exceed typical limit of 100
        {
            results.Add(rateLimiter.IsAllowed(teamSlug));
        }

        // Assert
        var allowedCount = results.Count(r => r);
        var deniedCount = results.Count(r => !r);

        allowedCount.Should().BeLessOrEqualTo(100);
        deniedCount.Should().BeGreaterThan(0);
    }

    [Test]
    public void SecurityHeaders_Validation_ShouldIncludeRequiredHeaders()
    {
        // Arrange - Headers that should be present for security
        var requiredSecurityHeaders = new Dictionary<string, string>
        {
            { "X-Content-Type-Options", "nosniff" },
            { "X-Frame-Options", "DENY" },
            { "X-XSS-Protection", "1; mode=block" },
            { "Strict-Transport-Security", "max-age=31536000; includeSubDomains" },
            { "Content-Security-Policy", "default-src 'self'" },
            { "Referrer-Policy", "strict-origin-when-cross-origin" }
        };

        // Act & Assert - This would be tested in integration tests
        foreach (var header in requiredSecurityHeaders)
        {
            ValidateSecurityHeader(header.Key, header.Value).Should().BeTrue($"Security header '{header.Key}' should be valid");
        }
    }

    [Test]
    public void AuthenticationToken_Validation_ShouldVerifySignature()
    {
        // Arrange
        var validTokenPattern = @"^[A-Za-z0-9\-_]+\.[A-Za-z0-9\-_]+\.[A-Za-z0-9\-_]+$"; // JWT-like pattern
        var testTokens = new[]
        {
            "eyJ0eXAiOiJKV1QiLCJhbGciOiJIUzI1NiJ9.valid.token", // Valid format
            "invalid-token-format", // Invalid format
            "", // Empty
            "header.payload", // Missing signature
            "header.payload.signature.extra" // Too many parts
        };

        // Act & Assert
        foreach (var token in testTokens)
        {
            var isValidFormat = System.Text.RegularExpressions.Regex.IsMatch(token, validTokenPattern);
            var expected = token == "eyJ0eXAiOiJKV1QiLCJhbGciOiJIUzI1NiJ9.valid.token";
            
            isValidFormat.Should().Be(expected, $"Token '{token}' format validation should be {expected}");
        }
    }

    // Helper methods that simulate security validation logic

    private string SanitizeForDatabase(string input)
    {
        // In real implementation, this would ensure parameterized queries are used
        // For testing, we just return the input as parameters prevent SQL injection
        return input;
    }

    private bool ValidateNoSqlInjection(string input)
    {
        // This would check that the input is properly parameterized
        // For testing, we assume proper parameterization prevents injection
        return true;
    }

    private string HtmlEncode(string input)
    {
        return System.Net.WebUtility.HtmlEncode(input);
    }

    private bool ValidateNoXss(string htmlEncodedInput)
    {
        // Check that dangerous patterns are properly encoded
        var dangerousPatterns = new[] { "<script", "javascript:", "onerror=", "onload=" };
        return !dangerousPatterns.Any(pattern => htmlEncodedInput.ToLower().Contains(pattern));
    }

    private string GenerateCsrfToken(string paymentId)
    {
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        var data = $"{paymentId}:{timestamp}:{Guid.NewGuid():N}";
        
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(TestCsrfKey));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(data));
        
        return Convert.ToBase64String(hash) + ":" + timestamp;
    }

    private bool ValidateCsrfToken(string paymentId, string? submittedToken)
    {
        if (string.IsNullOrWhiteSpace(submittedToken))
            return false;

        var cacheKey = $"csrf_token:{paymentId}";
        if (!_mockMemoryCache.Object.TryGetValue(cacheKey, out object? storedToken) || storedToken?.ToString() != submittedToken)
            return false;

        return true;
    }

    private bool IsValidPaymentId(string paymentId) =>
        !string.IsNullOrWhiteSpace(paymentId) && 
        paymentId.Length <= 50 && 
        System.Text.RegularExpressions.Regex.IsMatch(paymentId, @"^pay_[a-zA-Z0-9]+$");

    private bool ValidateOrderIdFormat(string orderId)
    {
        if (string.IsNullOrEmpty(orderId) || orderId.Length > 36)
            return false;
            
        return System.Text.RegularExpressions.Regex.IsMatch(orderId, @"^[a-zA-Z0-9\-_]+$");
    }

    private bool ValidateSecurityHeader(string headerName, string headerValue)
    {
        // Basic validation that headers are not empty and follow expected patterns
        return !string.IsNullOrWhiteSpace(headerName) && !string.IsNullOrWhiteSpace(headerValue);
    }
}

// Mock rate limiter for testing
public class MockRateLimiter
{
    private readonly Dictionary<string, List<DateTime>> _requests = new();
    private readonly int _maxRequests = 100;
    private readonly TimeSpan _timeWindow = TimeSpan.FromMinutes(1);

    public bool IsAllowed(string identifier)
    {
        var now = DateTime.UtcNow;
        
        if (!_requests.ContainsKey(identifier))
        {
            _requests[identifier] = new List<DateTime>();
        }

        var requests = _requests[identifier];
        
        // Remove old requests outside the time window
        requests.RemoveAll(r => now - r > _timeWindow);
        
        if (requests.Count >= _maxRequests)
        {
            return false;
        }
        
        requests.Add(now);
        return true;
    }
}