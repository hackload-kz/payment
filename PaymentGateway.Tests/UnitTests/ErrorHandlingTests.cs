// SPDX-License-Identifier: MIT
// Copyright (c) 2025 HackLoad Payment Gateway

using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using PaymentGateway.Core.DTOs;
using PaymentGateway.Core.Enums;
using PaymentGateway.Core.Services;
using PaymentGateway.API.Middleware;
using PaymentGateway.Tests.TestHelpers;
using System.Text.Json;

namespace PaymentGateway.Tests.UnitTests;

/// <summary>
/// Unit tests for error handling scenarios
/// </summary>
public class ErrorHandlingTests : BaseTest
{
    private readonly ErrorCategorizationService _errorCategorizationService;
    private readonly ErrorLocalizationService _errorLocalizationService;
    private readonly ErrorRetryService _errorRetryService;
    private readonly ErrorTrackingService _errorTrackingService;
    private readonly GlobalExceptionHandlingMiddleware _exceptionMiddleware;

    public ErrorHandlingTests()
    {
        _errorCategorizationService = new ErrorCategorizationService(
            GetService<ILogger<ErrorCategorizationService>>(),
            MockConfiguration.Object
        );

        _errorLocalizationService = new ErrorLocalizationService(
            GetService<ILogger<ErrorLocalizationService>>(),
            MockConfiguration.Object
        );

        _errorRetryService = new ErrorRetryService(
            GetService<ILogger<ErrorRetryService>>(),
            MockConfiguration.Object
        );

        _errorTrackingService = new ErrorTrackingService(
            GetService<ILogger<ErrorTrackingService>>(),
            MockConfiguration.Object
        );

        _exceptionMiddleware = new GlobalExceptionHandlingMiddleware(
            (RequestDelegate)((context) => Task.CompletedTask),
            GetService<ILogger<GlobalExceptionHandlingMiddleware>>(),
            _errorCategorizationService,
            _errorLocalizationService,
            _errorTrackingService
        );
    }

    [Theory]
    [InlineData(PaymentErrorCode.INVALID_AMOUNT, ErrorCategory.ValidationError, ErrorSeverity.Medium)]
    [InlineData(PaymentErrorCode.PAYMENT_NOT_FOUND, ErrorCategory.BusinessRuleViolation, ErrorSeverity.Medium)]
    [InlineData(PaymentErrorCode.AUTHENTICATION_FAILED, ErrorCategory.AuthenticationError, ErrorSeverity.High)]
    [InlineData(PaymentErrorCode.DATABASE_CONNECTION_FAILED, ErrorCategory.SystemError, ErrorSeverity.Critical)]
    [InlineData(PaymentErrorCode.RATE_LIMIT_EXCEEDED, ErrorCategory.SecurityViolation, ErrorSeverity.Medium)]
    public async Task ErrorCategorization_ShouldCategorizeErrorsCorrectly(
        PaymentErrorCode errorCode,
        ErrorCategory expectedCategory,
        ErrorSeverity expectedSeverity)
    {
        // Act
        var result = await _errorCategorizationService.CategorizeErrorAsync(errorCode);

        // Assert
        result.Should().NotBeNull();
        result.ErrorCode.Should().Be(errorCode);
        result.Category.Should().Be(expectedCategory);
        result.Severity.Should().Be(expectedSeverity);
    }

    [Theory]
    [InlineData(PaymentErrorCode.INVALID_AMOUNT, "en", "Invalid payment amount")]
    [InlineData(PaymentErrorCode.INVALID_AMOUNT, "ru", "Неверная сумма платежа")]
    [InlineData(PaymentErrorCode.PAYMENT_NOT_FOUND, "en", "Payment not found")]
    [InlineData(PaymentErrorCode.PAYMENT_NOT_FOUND, "ru", "Платеж не найден")]
    [InlineData(PaymentErrorCode.AUTHENTICATION_FAILED, "en", "Authentication failed")]
    [InlineData(PaymentErrorCode.AUTHENTICATION_FAILED, "ru", "Ошибка аутентификации")]
    public async Task ErrorLocalization_ShouldLocalizeErrorsCorrectly(
        PaymentErrorCode errorCode,
        string language,
        string expectedMessageSubstring)
    {
        // Act
        var result = await _errorLocalizationService.LocalizeErrorAsync(errorCode, language);

        // Assert
        result.Should().NotBeNull();
        result.ErrorCode.Should().Be(errorCode);
        result.Language.Should().Be(language);
        result.LocalizedMessage.Should().Contain(expectedMessageSubstring);
    }

    [Theory]
    [InlineData(ErrorCategory.NetworkError, true)]
    [InlineData(ErrorCategory.SystemError, true)]
    [InlineData(ErrorCategory.ValidationError, false)]
    [InlineData(ErrorCategory.AuthenticationError, false)]
    [InlineData(ErrorCategory.BusinessRuleViolation, false)]
    public async Task ErrorRetryPolicy_ShouldDetermineRetryabilityCorrectly(
        ErrorCategory category,
        bool expectedRetryable)
    {
        // Arrange
        var errorInfo = new ErrorCategorization
        {
            ErrorCode = PaymentErrorCode.NETWORK_ERROR,
            Category = category,
            Severity = ErrorSeverity.Medium
        };

        // Act
        var policy = await _errorRetryService.GetRetryPolicyAsync(errorInfo);

        // Assert
        policy.Should().NotBeNull();
        policy.IsRetryable.Should().Be(expectedRetryable);
        
        if (expectedRetryable)
        {
            policy.MaxRetryAttempts.Should().BeGreaterThan(0);
            policy.BaseDelayMs.Should().BeGreaterThan(0);
        }
    }

    [Fact]
    public async Task ErrorTracking_ShouldTrackErrorOccurrences()
    {
        // Arrange
        var errorCode = PaymentErrorCode.INVALID_AMOUNT;
        var correlationId = Guid.NewGuid().ToString();
        var context = new Dictionary<string, object>
        {
            ["PaymentId"] = "PAY_123",
            ["TeamSlug"] = "test_team",
            ["Amount"] = 1000m
        };

        // Act
        await _errorTrackingService.TrackErrorAsync(errorCode, correlationId, context);
        await _errorTrackingService.TrackErrorAsync(errorCode, Guid.NewGuid().ToString(), context);

        // Get statistics
        var stats = await _errorTrackingService.GetErrorStatisticsAsync(errorCode);

        // Assert
        stats.Should().NotBeNull();
        stats.ErrorCode.Should().Be(errorCode);
        stats.TotalOccurrences.Should().BeGreaterOrEqualTo(2);
        stats.FirstOccurrence.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(1));
        stats.LastOccurrence.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task ErrorRetryService_ShouldImplementExponentialBackoff()
    {
        // Arrange
        var errorInfo = new ErrorCategorization
        {
            ErrorCode = PaymentErrorCode.NETWORK_ERROR,
            Category = ErrorCategory.NetworkError,
            Severity = ErrorSeverity.Medium
        };

        // Act
        var policy = await _errorRetryService.GetRetryPolicyAsync(errorInfo);
        var delays = new List<int>();
        
        for (int attempt = 1; attempt <= policy.MaxRetryAttempts; attempt++)
        {
            var delay = await _errorRetryService.CalculateRetryDelayAsync(policy, attempt);
            delays.Add(delay);
        }

        // Assert
        delays.Should().NotBeEmpty();
        
        // Verify exponential backoff (each delay should be roughly double the previous)
        for (int i = 1; i < delays.Count; i++)
        {
            delays[i].Should().BeGreaterThan(delays[i - 1]);
        }
    }

    [Fact]
    public async Task ErrorRetryService_ShouldRespectMaxRetryAttempts()
    {
        // Arrange
        var errorInfo = new ErrorCategorization
        {
            ErrorCode = PaymentErrorCode.NETWORK_ERROR,
            Category = ErrorCategory.NetworkError,
            Severity = ErrorSeverity.Medium
        };

        var policy = await _errorRetryService.GetRetryPolicyAsync(errorInfo);
        var operation = new Mock<Func<Task<bool>>>();
        operation.Setup(o => o()).ReturnsAsync(false); // Always fails

        // Act
        var result = await _errorRetryService.ExecuteWithRetryAsync(operation.Object, policy);

        // Assert
        result.Should().BeFalse();
        operation.Verify(o => o(), Times.Exactly(policy.MaxRetryAttempts + 1)); // Initial attempt + retries
    }

    [Fact]
    public async Task ErrorRetryService_ShouldSucceedOnRetry()
    {
        // Arrange
        var errorInfo = new ErrorCategorization
        {
            ErrorCode = PaymentErrorCode.NETWORK_ERROR,
            Category = ErrorCategory.NetworkError,
            Severity = ErrorSeverity.Medium
        };

        var policy = await _errorRetryService.GetRetryPolicyAsync(errorInfo);
        var operation = new Mock<Func<Task<bool>>>();
        var callCount = 0;
        operation.Setup(o => o()).ReturnsAsync(() => ++callCount >= 3); // Succeeds on 3rd attempt

        // Act
        var result = await _errorRetryService.ExecuteWithRetryAsync(operation.Object, policy);

        // Assert
        result.Should().BeTrue();
        operation.Verify(o => o(), Times.Exactly(3)); // Should stop after success
    }

    [Fact]
    public async Task ErrorPatternAnalysis_ShouldDetectPatterns()
    {
        // Arrange
        var errorCode = PaymentErrorCode.CARD_DECLINED;
        var teamSlug = "test_team";

        // Track multiple errors from the same team
        for (int i = 0; i < 10; i++)
        {
            await _errorTrackingService.TrackErrorAsync(errorCode, Guid.NewGuid().ToString(), 
                new Dictionary<string, object> { ["TeamSlug"] = teamSlug });
        }

        // Act
        var patterns = await _errorTrackingService.AnalyzeErrorPatternsAsync(
            TimeSpan.FromHours(1));

        // Assert
        patterns.Should().NotBeEmpty();
        patterns.Should().Contain(p => 
            p.ErrorCode == errorCode && 
            p.Pattern.Contains(teamSlug));
    }

    [Fact]
    public async Task ErrorCorrelation_ShouldCorrelateRelatedErrors()
    {
        // Arrange
        var correlationId = Guid.NewGuid().ToString();
        var context = new Dictionary<string, object>
        {
            ["PaymentId"] = "PAY_123",
            ["CorrelationId"] = correlationId
        };

        // Track related errors
        await _errorTrackingService.TrackErrorAsync(
            PaymentErrorCode.CARD_VALIDATION_FAILED, correlationId, context);
        await _errorTrackingService.TrackErrorAsync(
            PaymentErrorCode.PAYMENT_PROCESSING_FAILED, correlationId, context);

        // Act
        var correlatedErrors = await _errorTrackingService.GetCorrelatedErrorsAsync(correlationId);

        // Assert
        correlatedErrors.Should().HaveCountGreaterOrEqualTo(2);
        correlatedErrors.Should().Contain(e => e.ErrorCode == PaymentErrorCode.CARD_VALIDATION_FAILED);
        correlatedErrors.Should().Contain(e => e.ErrorCode == PaymentErrorCode.PAYMENT_PROCESSING_FAILED);
    }

    [Fact]
    public async Task GlobalExceptionMiddleware_ShouldHandlePaymentExceptions()
    {
        // Arrange
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        
        var middleware = new GlobalExceptionHandlingMiddleware(
            (ctx) => throw new PaymentProcessingException(
                PaymentErrorCode.PAYMENT_PROCESSING_FAILED, 
                "Test payment processing error"),
            GetService<ILogger<GlobalExceptionHandlingMiddleware>>(),
            _errorCategorizationService,
            _errorLocalizationService,
            _errorTrackingService
        );

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        context.Response.StatusCode.Should().Be(422); // Unprocessable Entity
        context.Response.ContentType.Should().Contain("application/json");
        
        context.Response.Body.Position = 0;
        var responseContent = await new StreamReader(context.Response.Body).ReadToEndAsync();
        var errorResponse = JsonSerializer.Deserialize<ErrorResponseDto>(responseContent);
        
        errorResponse.Should().NotBeNull();
        errorResponse!.ErrorCode.Should().Be((int)PaymentErrorCode.PAYMENT_PROCESSING_FAILED);
        errorResponse.Message.Should().Contain("payment processing");
    }

    [Fact]
    public async Task GlobalExceptionMiddleware_ShouldHandleGenericExceptions()
    {
        // Arrange
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        
        var middleware = new GlobalExceptionHandlingMiddleware(
            (ctx) => throw new InvalidOperationException("Generic error"),
            GetService<ILogger<GlobalExceptionHandlingMiddleware>>(),
            _errorCategorizationService,
            _errorLocalizationService,
            _errorTrackingService
        );

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        context.Response.StatusCode.Should().Be(500); // Internal Server Error
        context.Response.ContentType.Should().Contain("application/json");
        
        context.Response.Body.Position = 0;
        var responseContent = await new StreamReader(context.Response.Body).ReadToEndAsync();
        var errorResponse = JsonSerializer.Deserialize<ErrorResponseDto>(responseContent);
        
        errorResponse.Should().NotBeNull();
        errorResponse!.ErrorCode.Should().Be((int)PaymentErrorCode.INTERNAL_SERVER_ERROR);
    }

    [Fact]
    public async Task ErrorRateAnalysis_ShouldCalculateErrorRates()
    {
        // Arrange
        var errorCode = PaymentErrorCode.CARD_DECLINED;
        var timeSpan = TimeSpan.FromHours(1);

        // Track multiple errors
        for (int i = 0; i < 20; i++)
        {
            await _errorTrackingService.TrackErrorAsync(errorCode, Guid.NewGuid().ToString(), 
                new Dictionary<string, object>());
        }

        // Act
        var errorRate = await _errorTrackingService.CalculateErrorRateAsync(errorCode, timeSpan);

        // Assert
        errorRate.Should().NotBeNull();
        errorRate.ErrorCode.Should().Be(errorCode);
        errorRate.TimeSpan.Should().Be(timeSpan);
        errorRate.ErrorsPerHour.Should().BeGreaterThan(0);
        errorRate.TotalErrors.Should().BeGreaterOrEqualTo(20);
    }

    [Fact]
    public async Task ErrorContextEnrichment_ShouldEnrichErrorContext()
    {
        // Arrange
        var errorCode = PaymentErrorCode.INVALID_CARD_NUMBER;
        var correlationId = Guid.NewGuid().ToString();
        var context = new Dictionary<string, object>
        {
            ["CardNumber"] = "4111111111111111",
            ["PaymentId"] = "PAY_123"
        };

        // Act
        await _errorTrackingService.TrackErrorAsync(errorCode, correlationId, context);
        var trackedError = await _errorTrackingService.GetErrorDetailsAsync(correlationId);

        // Assert
        trackedError.Should().NotBeNull();
        trackedError!.Context.Should().ContainKey("CardNumber");
        trackedError.Context.Should().ContainKey("PaymentId");
        trackedError.Context["CardNumber"].Should().NotBe("4111111111111111"); // Should be masked
        trackedError.Context["PaymentId"].Should().Be("PAY_123");
    }

    [Fact]
    public async Task ConcurrentErrorTracking_ShouldHandleCorrectly()
    {
        // Arrange
        var errorCode = PaymentErrorCode.NETWORK_ERROR;

        // Act
        var tasks = Enumerable.Range(0, 50).Select(async i =>
            await _errorTrackingService.TrackErrorAsync(errorCode, 
                Guid.NewGuid().ToString(), 
                new Dictionary<string, object> { ["Index"] = i })
        );

        await Task.WhenAll(tasks);

        // Get statistics
        var stats = await _errorTrackingService.GetErrorStatisticsAsync(errorCode);

        // Assert
        stats.Should().NotBeNull();
        stats.TotalOccurrences.Should().BeGreaterOrEqualTo(50);
    }
}