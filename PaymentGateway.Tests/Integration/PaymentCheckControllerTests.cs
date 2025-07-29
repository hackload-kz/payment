// SPDX-License-Identifier: MIT
// Copyright (c) 2025 HackLoad Payment Gateway

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using PaymentGateway.Core.DTOs.PaymentCheck;
using PaymentGateway.Core.Services;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Caching.Memory;
using Moq;

namespace PaymentGateway.Tests.Integration;

public class PaymentCheckControllerTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;
    private readonly JsonSerializerOptions _jsonOptions;

    public PaymentCheckControllerTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                // Mock authentication service
                services.AddScoped<IPaymentAuthenticationService>(provider =>
                {
                    var mock = new Mock<IPaymentAuthenticationService>();
                    mock.Setup(x => x.AuthenticateAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()))
                        .ReturnsAsync(new { IsAuthenticated = true, FailureReason = (string?)null });
                    return mock.Object;
                });

                // Mock payment status check service
                services.AddScoped<IPaymentStatusCheckService>(provider =>
                {
                    var mock = new Mock<IPaymentStatusCheckService>();
                    
                    // Setup successful payment check by ID
                    mock.Setup(x => x.CheckPaymentByIdAsync(It.IsAny<string>(), It.IsAny<PaymentCheckRequestDto>(), It.IsAny<CancellationToken>()))
                        .ReturnsAsync((string paymentId, PaymentCheckRequestDto request, CancellationToken ct) => new PaymentCheckResponseDto
                        {
                            Success = true,
                            Payments = new List<PaymentStatusDto>
                            {
                                new PaymentStatusDto
                                {
                                    PaymentId = paymentId,
                                    OrderId = "test-order-123",
                                    Status = "CONFIRMED",
                                    StatusDescription = "Payment confirmed successfully",
                                    Amount = 150000,
                                    Currency = "RUB",
                                    CreatedAt = DateTime.UtcNow.AddMinutes(-10),
                                    UpdatedAt = DateTime.UtcNow.AddMinutes(-5),
                                    ExpiresAt = DateTime.UtcNow.AddMinutes(20),
                                    Description = "Test payment",
                                    PayType = "O"
                                }
                            },
                            TotalCount = 1
                        });

                    // Setup successful payment check by OrderId
                    mock.Setup(x => x.CheckPaymentsByOrderIdAsync(It.IsAny<string>(), It.IsAny<PaymentCheckRequestDto>(), It.IsAny<CancellationToken>()))
                        .ReturnsAsync((string orderId, PaymentCheckRequestDto request, CancellationToken ct) => new PaymentCheckResponseDto
                        {
                            Success = true,
                            OrderId = orderId,
                            Payments = new List<PaymentStatusDto>
                            {
                                new PaymentStatusDto
                                {
                                    PaymentId = "pay_test_123",
                                    OrderId = orderId,
                                    Status = "CONFIRMED",
                                    StatusDescription = "Payment confirmed successfully",
                                    Amount = 150000,
                                    Currency = "RUB",
                                    CreatedAt = DateTime.UtcNow.AddMinutes(-10),
                                    UpdatedAt = DateTime.UtcNow.AddMinutes(-5),
                                    ExpiresAt = DateTime.UtcNow.AddMinutes(20),
                                    Description = "Test payment",
                                    PayType = "O"
                                }
                            },
                            TotalCount = 1
                        });

                    return mock.Object;
                });
            });
        });

        _client = _factory.CreateClient();
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
    }

    [Fact]
    public async Task CheckPaymentStatus_ByPaymentId_ReturnsSuccess()
    {
        // Arrange
        var request = new PaymentCheckRequestDto
        {
            TeamSlug = "test-team",
            Token = "valid-test-token",
            PaymentId = "pay_test_123",
            IncludeTransactions = true,
            IncludeCardDetails = true,
            IncludeCustomerInfo = false,
            IncludeReceipt = false,
            Language = "ru"
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/v1/paymentcheck/check", request, _jsonOptions);

        // Assert
        Assert.True(response.IsSuccessStatusCode);

        var content = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<PaymentCheckResponseDto>(content, _jsonOptions);

        Assert.NotNull(result);
        Assert.True(result.Success);
        Assert.Single(result.Payments);
        Assert.Equal(1, result.TotalCount);

        var payment = result.Payments.First();
        Assert.Equal(request.PaymentId, payment.PaymentId);
        Assert.Equal("test-order-123", payment.OrderId);
        Assert.Equal("CONFIRMED", payment.Status);
        Assert.Equal(150000, payment.Amount);
        Assert.Equal("RUB", payment.Currency);
    }

    [Fact]
    public async Task CheckPaymentStatus_ByOrderId_ReturnsSuccess()
    {
        // Arrange
        var request = new PaymentCheckRequestDto
        {
            TeamSlug = "test-team",
            Token = "valid-test-token",
            OrderId = "test-order-456",
            IncludeTransactions = false,
            IncludeCardDetails = false,
            IncludeCustomerInfo = true,
            IncludeReceipt = true,
            Language = "en"
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/v1/paymentcheck/check", request, _jsonOptions);

        // Assert
        Assert.True(response.IsSuccessStatusCode);

        var content = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<PaymentCheckResponseDto>(content, _jsonOptions);

        Assert.NotNull(result);
        Assert.True(result.Success);
        Assert.Single(result.Payments);
        Assert.Equal(1, result.TotalCount);
        Assert.Equal(request.OrderId, result.OrderId);

        var payment = result.Payments.First();
        Assert.Equal("pay_test_123", payment.PaymentId);
        Assert.Equal(request.OrderId, payment.OrderId);
        Assert.Equal("CONFIRMED", payment.Status);
    }

    [Fact]
    public async Task CheckPaymentStatus_NullRequest_ReturnsBadRequest()
    {
        // Act
        var response = await _client.PostAsync("/api/v1/paymentcheck/check", null);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var content = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<PaymentCheckResponseDto>(content, _jsonOptions);

        Assert.NotNull(result);
        Assert.False(result.Success);
        Assert.Equal("1000", result.ErrorCode);
    }

    [Fact]
    public async Task CheckPaymentStatus_NoPaymentIdOrOrderId_ReturnsBadRequest()
    {
        // Arrange
        var request = new PaymentCheckRequestDto
        {
            TeamSlug = "test-team",
            Token = "valid-test-token",
            // Neither PaymentId nor OrderId provided
            Language = "ru"
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/v1/paymentcheck/check", request, _jsonOptions);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var content = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<PaymentCheckResponseDto>(content, _jsonOptions);

        Assert.NotNull(result);
        Assert.False(result.Success);
        Assert.Equal("1100", result.ErrorCode);
        Assert.Contains("Either PaymentId or OrderId must be provided", result.Details);
    }

    [Fact]
    public async Task CheckPaymentStatus_InvalidPaymentIdFormat_ReturnsBadRequest()
    {
        // Arrange
        var request = new PaymentCheckRequestDto
        {
            TeamSlug = "test-team",
            Token = "valid-test-token",
            PaymentId = "invalid-payment-id-with-special-chars-@#$%", // Invalid characters
            Language = "ru"
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/v1/paymentcheck/check", request, _jsonOptions);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var content = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<PaymentCheckResponseDto>(content, _jsonOptions);

        Assert.NotNull(result);
        Assert.False(result.Success);
        Assert.Equal("1100", result.ErrorCode);
        Assert.Contains("PaymentId can only contain alphanumeric characters", result.Details);
    }

    [Fact]
    public async Task CheckPaymentStatus_InvalidOrderIdFormat_ReturnsBadRequest()
    {
        // Arrange
        var request = new PaymentCheckRequestDto
        {
            TeamSlug = "test-team",
            Token = "valid-test-token",
            OrderId = "invalid-order-id-with-special-chars-@#$%", // Invalid characters
            Language = "ru"
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/v1/paymentcheck/check", request, _jsonOptions);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var content = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<PaymentCheckResponseDto>(content, _jsonOptions);

        Assert.NotNull(result);
        Assert.False(result.Success);
        Assert.Equal("1100", result.ErrorCode);
        Assert.Contains("OrderId can only contain alphanumeric characters", result.Details);
    }

    [Fact]
    public async Task CheckPaymentStatus_InvalidLanguage_ReturnsBadRequest()
    {
        // Arrange
        var request = new PaymentCheckRequestDto
        {
            TeamSlug = "test-team",
            Token = "valid-test-token",
            PaymentId = "pay_test_123",
            Language = "fr" // Unsupported language
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/v1/paymentcheck/check", request, _jsonOptions);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var content = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<PaymentCheckResponseDto>(content, _jsonOptions);

        Assert.NotNull(result);
        Assert.False(result.Success);
        Assert.Equal("1100", result.ErrorCode);
        Assert.Contains("Language must be 'ru' or 'en'", result.Details);
    }

    [Fact]
    public async Task CheckPaymentStatus_MissingTeamSlug_ReturnsBadRequest()
    {
        // Arrange
        var request = new PaymentCheckRequestDto
        {
            // TeamSlug missing
            Token = "valid-test-token",
            PaymentId = "pay_test_123",
            Language = "ru"
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/v1/paymentcheck/check", request, _jsonOptions);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var content = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<PaymentCheckResponseDto>(content, _jsonOptions);

        Assert.NotNull(result);
        Assert.False(result.Success);
        Assert.Equal("1100", result.ErrorCode);
        Assert.Contains("TeamSlug is required for authentication", result.Details);
    }

    [Fact]
    public async Task CheckPaymentStatus_MissingToken_ReturnsBadRequest()
    {
        // Arrange
        var request = new PaymentCheckRequestDto
        {
            TeamSlug = "test-team",
            // Token missing
            PaymentId = "pay_test_123",
            Language = "ru"
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/v1/paymentcheck/check", request, _jsonOptions);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var content = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<PaymentCheckResponseDto>(content, _jsonOptions);

        Assert.NotNull(result);
        Assert.False(result.Success);
        Assert.Equal("1100", result.ErrorCode);
        Assert.Contains("Token is required for authentication", result.Details);
    }

    [Fact]
    public async Task CheckPaymentStatus_AllOptionalFlags_ReturnsSuccess()
    {
        // Arrange
        var request = new PaymentCheckRequestDto
        {
            TeamSlug = "test-team",
            Token = "valid-test-token",
            PaymentId = "pay_test_123",
            IncludeTransactions = true,
            IncludeCardDetails = true,
            IncludeCustomerInfo = true,
            IncludeReceipt = true,
            Language = "en"
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/v1/paymentcheck/check", request, _jsonOptions);

        // Assert
        Assert.True(response.IsSuccessStatusCode);

        var content = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<PaymentCheckResponseDto>(content, _jsonOptions);

        Assert.NotNull(result);
        Assert.True(result.Success);
        Assert.Single(result.Payments);
    }

    [Fact]
    public async Task GetPaymentStatus_ValidPaymentId_ReturnsSuccess()
    {
        // Act
        var response = await _client.GetAsync("/api/v1/paymentcheck/status?paymentId=pay_test_123&teamSlug=test-team&token=valid-test-token&language=ru");

        // Assert
        Assert.True(response.IsSuccessStatusCode);

        var content = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<PaymentCheckResponseDto>(content, _jsonOptions);

        Assert.NotNull(result);
        Assert.True(result.Success);
        Assert.Single(result.Payments);

        var payment = result.Payments.First();
        Assert.Equal("pay_test_123", payment.PaymentId);
        Assert.Equal("CONFIRMED", payment.Status);
    }

    [Fact]
    public async Task GetPaymentStatus_ValidOrderId_ReturnsSuccess()
    {
        // Act
        var response = await _client.GetAsync("/api/v1/paymentcheck/status?orderId=test-order-456&teamSlug=test-team&token=valid-test-token&language=en");

        // Assert
        Assert.True(response.IsSuccessStatusCode);

        var content = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<PaymentCheckResponseDto>(content, _jsonOptions);

        Assert.NotNull(result);
        Assert.True(result.Success);
        Assert.Single(result.Payments);
        Assert.Equal("test-order-456", result.OrderId);
    }

    [Fact]
    public async Task GetPaymentStatus_MissingParameters_ReturnsBadRequest()
    {
        // Act - No PaymentId or OrderId provided
        var response = await _client.GetAsync("/api/v1/paymentcheck/status?teamSlug=test-team&token=valid-test-token");

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var content = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<PaymentCheckResponseDto>(content, _jsonOptions);

        Assert.NotNull(result);
        Assert.False(result.Success);
        Assert.Equal("1100", result.ErrorCode);
    }

    [Fact]
    public async Task CheckPaymentStatus_BothPaymentIdAndOrderId_ReturnsSuccessWithPaymentIdPrecedence()
    {
        // Arrange
        var request = new PaymentCheckRequestDto
        {
            TeamSlug = "test-team",
            Token = "valid-test-token",
            PaymentId = "pay_test_123", // This should take precedence
            OrderId = "test-order-456",
            Language = "ru"
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/v1/paymentcheck/check", request, _jsonOptions);

        // Assert
        Assert.True(response.IsSuccessStatusCode);

        var content = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<PaymentCheckResponseDto>(content, _jsonOptions);

        Assert.NotNull(result);
        Assert.True(result.Success);
        Assert.Single(result.Payments);

        var payment = result.Payments.First();
        // Should return payment by PaymentId (which has OrderId "test-order-123")
        Assert.Equal("pay_test_123", payment.PaymentId);
        Assert.Equal("test-order-123", payment.OrderId);
    }

    [Fact]
    public async Task CheckPaymentStatus_MultipleResponseFields_ReturnsAllFields()
    {
        // Arrange
        var request = new PaymentCheckRequestDto
        {
            TeamSlug = "test-team",
            Token = "valid-test-token",
            PaymentId = "pay_test_123",
            IncludeTransactions = true,
            IncludeCardDetails = true,
            IncludeCustomerInfo = true,
            IncludeReceipt = true,
            Language = "ru"
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/v1/paymentcheck/check", request, _jsonOptions);

        // Assert
        Assert.True(response.IsSuccessStatusCode);

        var content = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<PaymentCheckResponseDto>(content, _jsonOptions);

        Assert.NotNull(result);
        Assert.True(result.Success);
        Assert.Single(result.Payments);

        var payment = result.Payments.First();
        Assert.NotNull(payment.PaymentId);
        Assert.NotNull(payment.OrderId);
        Assert.NotNull(payment.Status);
        Assert.NotNull(payment.StatusDescription);
        Assert.True(payment.Amount > 0);
        Assert.NotNull(payment.Currency);
        Assert.True(payment.CreatedAt.HasValue);
        Assert.True(payment.UpdatedAt.HasValue);
        Assert.True(payment.ExpiresAt.HasValue);
        Assert.NotNull(payment.Description);
        Assert.NotNull(payment.PayType);
    }
}

/// <summary>
/// Test utilities for PaymentCheck testing
/// </summary>
public static class PaymentCheckTestUtils
{
    public static PaymentCheckRequestDto CreateValidCheckRequest(string? paymentId = null, string? orderId = null)
    {
        return new PaymentCheckRequestDto
        {
            TeamSlug = "test-team",
            Token = "valid-test-token",
            PaymentId = paymentId,
            OrderId = orderId,
            IncludeTransactions = false,
            IncludeCardDetails = false,
            IncludeCustomerInfo = false,
            IncludeReceipt = false,
            Language = "ru"
        };
    }

    public static PaymentStatusDto CreateTestPaymentStatus(string paymentId, string orderId, string status = "CONFIRMED")
    {
        return new PaymentStatusDto
        {
            PaymentId = paymentId,
            OrderId = orderId,
            Status = status,
            StatusDescription = GetStatusDescription(status),
            Amount = 150000,
            Currency = "RUB",
            CreatedAt = DateTime.UtcNow.AddMinutes(-10),
            UpdatedAt = DateTime.UtcNow.AddMinutes(-5),
            ExpiresAt = DateTime.UtcNow.AddMinutes(20),
            Description = "Test payment",
            PayType = "O"
        };
    }

    private static string GetStatusDescription(string status)
    {
        return status switch
        {
            "NEW" => "Payment created",
            "AUTHORIZED" => "Payment authorized",
            "CONFIRMED" => "Payment confirmed successfully",
            "CANCELLED" => "Payment cancelled",
            "REFUNDED" => "Payment refunded",
            "FAILED" => "Payment failed",
            "REJECTED" => "Payment rejected",
            _ => "Unknown status"
        };
    }
}