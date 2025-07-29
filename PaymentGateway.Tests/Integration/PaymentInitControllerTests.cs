// SPDX-License-Identifier: MIT
// Copyright (c) 2025 HackLoad Payment Gateway

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using PaymentGateway.Core.DTOs.PaymentInit;
using PaymentGateway.Core.Services;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;
using Moq;

namespace PaymentGateway.Tests.Integration;

public class PaymentInitControllerTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;
    private readonly JsonSerializerOptions _jsonOptions;

    public PaymentInitControllerTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                // Mock services for testing
                services.AddScoped<IPaymentAuthenticationService>(provider =>
                {
                    var mock = new Mock<IPaymentAuthenticationService>();
                    mock.Setup(x => x.AuthenticateAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()))
                        .ReturnsAsync(new { IsAuthenticated = true, FailureReason = (string?)null });
                    return mock.Object;
                });

                services.AddScoped<IBusinessRuleEngineService>(provider =>
                {
                    var mock = new Mock<IBusinessRuleEngineService>();
                    mock.Setup(x => x.EvaluatePaymentRulesAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()))
                        .ReturnsAsync(new 
                        { 
                            IsValid = true, 
                            Errors = new List<string>(), 
                            ViolatedRules = new List<object>(),
                            EvaluatedRules = new List<object>()
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
    public async Task InitializePayment_ValidRequest_ReturnsSuccess()
    {
        // Arrange
        var request = new PaymentInitRequestDto
        {
            TeamSlug = "test-team",
            Token = "valid-test-token",
            Amount = 150000, // 1500.00 RUB
            OrderId = "test-order-123",
            Currency = "RUB",
            Description = "Test payment",
            SuccessURL = "https://example.com/success",
            FailURL = "https://example.com/fail",
            NotificationURL = "https://example.com/webhook",
            PaymentExpiry = 30,
            Email = "test@example.com",
            Language = "ru"
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/v1/paymentinit/init", request, _jsonOptions);

        // Assert
        Assert.True(response.IsSuccessStatusCode);
        
        var content = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<PaymentInitResponseDto>(content, _jsonOptions);
        
        Assert.NotNull(result);
        Assert.True(result.Success);
        Assert.NotNull(result.PaymentId);
        Assert.Equal(request.OrderId, result.OrderId);
        Assert.Equal(request.Amount, result.Amount);
        Assert.Equal(request.Currency, result.Currency);
        Assert.NotNull(result.PaymentURL);
    }

    [Fact]
    public async Task InitializePayment_NullRequest_ReturnsBadRequest()
    {
        // Act
        var response = await _client.PostAsync("/api/v1/paymentinit/init", null);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        
        var content = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<PaymentInitResponseDto>(content, _jsonOptions);
        
        Assert.NotNull(result);
        Assert.False(result.Success);
        Assert.Equal("1000", result.ErrorCode);
    }

    [Fact]
    public async Task InitializePayment_InvalidAmount_ReturnsBadRequest()
    {
        // Arrange
        var request = new PaymentInitRequestDto
        {
            TeamSlug = "test-team",
            Token = "valid-test-token",
            Amount = -1000, // Invalid negative amount
            OrderId = "test-order-123",
            Currency = "RUB"
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/v1/paymentinit/init", request, _jsonOptions);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        
        var content = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<PaymentInitResponseDto>(content, _jsonOptions);
        
        Assert.NotNull(result);
        Assert.False(result.Success);
        Assert.Equal("1100", result.ErrorCode);
        Assert.Contains("Amount must be greater than zero", result.Details);
    }

    [Fact]
    public async Task InitializePayment_ExcessiveAmount_ReturnsBadRequest()
    {
        // Arrange
        var request = new PaymentInitRequestDto
        {
            TeamSlug = "test-team",
            Token = "valid-test-token",
            Amount = 200000000, // Exceeds 1M RUB limit
            OrderId = "test-order-123",
            Currency = "RUB"
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/v1/paymentinit/init", request, _jsonOptions);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        
        var content = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<PaymentInitResponseDto>(content, _jsonOptions);
        
        Assert.NotNull(result);
        Assert.False(result.Success);
        Assert.Equal("1100", result.ErrorCode);
        Assert.Contains("Amount exceeds maximum limit", result.Details);
    }

    [Fact]
    public async Task InitializePayment_InvalidCurrency_ReturnsBadRequest()
    {
        // Arrange
        var request = new PaymentInitRequestDto
        {
            TeamSlug = "test-team",
            Token = "valid-test-token",
            Amount = 150000,
            OrderId = "test-order-123",
            Currency = "XYZ" // Invalid currency
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/v1/paymentinit/init", request, _jsonOptions);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        
        var content = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<PaymentInitResponseDto>(content, _jsonOptions);
        
        Assert.NotNull(result);
        Assert.False(result.Success);
        Assert.Equal("1100", result.ErrorCode);
        Assert.Contains("Currency must be one of", result.Details);
    }

    [Fact]
    public async Task InitializePayment_InvalidOrderId_ReturnsBadRequest()
    {
        // Arrange
        var request = new PaymentInitRequestDto
        {
            TeamSlug = "test-team",
            Token = "valid-test-token",
            Amount = 150000,
            OrderId = "invalid-order-id-with-special-chars-@#$%", // Invalid characters
            Currency = "RUB"
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/v1/paymentinit/init", request, _jsonOptions);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        
        var content = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<PaymentInitResponseDto>(content, _jsonOptions);
        
        Assert.NotNull(result);
        Assert.False(result.Success);
        Assert.Equal("1100", result.ErrorCode);
        Assert.Contains("OrderId can only contain alphanumeric characters", result.Details);
    }

    [Fact]
    public async Task InitializePayment_InvalidURL_ReturnsBadRequest()
    {
        // Arrange
        var request = new PaymentInitRequestDto
        {
            TeamSlug = "test-team",
            Token = "valid-test-token",
            Amount = 150000,
            OrderId = "test-order-123",
            Currency = "RUB",
            SuccessURL = "not-a-valid-url", // Invalid URL
            FailURL = "also-not-valid"
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/v1/paymentinit/init", request, _jsonOptions);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        
        var content = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<PaymentInitResponseDto>(content, _jsonOptions);
        
        Assert.NotNull(result);
        Assert.False(result.Success);
        Assert.Equal("1100", result.ErrorCode);
        Assert.Contains("must be a valid absolute URL", result.Details);
    }

    [Fact]
    public async Task InitializePayment_InvalidEmail_ReturnsBadRequest()
    {
        // Arrange
        var request = new PaymentInitRequestDto
        {
            TeamSlug = "test-team",
            Token = "valid-test-token",
            Amount = 150000,
            OrderId = "test-order-123",
            Currency = "RUB",
            Email = "not-a-valid-email" // Invalid email format
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/v1/paymentinit/init", request, _jsonOptions);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        
        var content = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<PaymentInitResponseDto>(content, _jsonOptions);
        
        Assert.NotNull(result);
        Assert.False(result.Success);
        Assert.Equal("1100", result.ErrorCode);
        Assert.Contains("Invalid email format", result.Details);
    }

    [Fact]
    public async Task InitializePayment_ExcessivePaymentExpiry_ReturnsBadRequest()
    {
        // Arrange
        var request = new PaymentInitRequestDto
        {
            TeamSlug = "test-team",
            Token = "valid-test-token",
            Amount = 150000,
            OrderId = "test-order-123",
            Currency = "RUB",
            PaymentExpiry = 50000 // Exceeds 30 days limit
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/v1/paymentinit/init", request, _jsonOptions);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        
        var content = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<PaymentInitResponseDto>(content, _jsonOptions);
        
        Assert.NotNull(result);
        Assert.False(result.Success);
        Assert.Equal("1100", result.ErrorCode);
        Assert.Contains("PaymentExpiry cannot exceed 43200 minutes", result.Details);
    }

    [Fact]
    public async Task InitializePayment_WithValidItems_ReturnsSuccess()
    {
        // Arrange
        var request = new PaymentInitRequestDto
        {
            TeamSlug = "test-team",
            Token = "valid-test-token",
            Amount = 150000,
            OrderId = "test-order-123",
            Currency = "RUB",
            Items = new List<OrderItemDto>
            {
                new OrderItemDto
                {
                    Name = "Test Book",
                    Quantity = 2,
                    Price = 75000, // 750.00 RUB
                    Amount = 150000, // 1500.00 RUB total
                    Category = "Books"
                }
            }
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/v1/paymentinit/init", request, _jsonOptions);

        // Assert
        Assert.True(response.IsSuccessStatusCode);
        
        var content = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<PaymentInitResponseDto>(content, _jsonOptions);
        
        Assert.NotNull(result);
        Assert.True(result.Success);
        Assert.NotNull(result.Details?.Items);
        Assert.Single(result.Details.Items);
    }

    [Fact]
    public async Task InitializePayment_ItemsAmountMismatch_ReturnsBadRequest()
    {
        // Arrange
        var request = new PaymentInitRequestDto
        {
            TeamSlug = "test-team",
            Token = "valid-test-token",
            Amount = 150000,
            OrderId = "test-order-123",
            Currency = "RUB",
            Items = new List<OrderItemDto>
            {
                new OrderItemDto
                {
                    Name = "Test Book",
                    Quantity = 2,
                    Price = 50000, // 500.00 RUB
                    Amount = 100000, // 1000.00 RUB total (doesn't match payment amount)
                    Category = "Books"
                }
            }
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/v1/paymentinit/init", request, _jsonOptions);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        
        var content = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<PaymentInitResponseDto>(content, _jsonOptions);
        
        Assert.NotNull(result);
        Assert.False(result.Success);
        Assert.Equal("1100", result.ErrorCode);
        Assert.Contains("Total items amount does not match payment amount", result.Details);
    }

    [Fact]
    public async Task InitializePayment_ComprehensiveValidRequest_ReturnsSuccess()
    {
        // Arrange
        var request = new PaymentInitRequestDto
        {
            TeamSlug = "comprehensive-test",
            Token = "comprehensive-test-token",
            Amount = 299900, // 2999.00 RUB
            OrderId = "comprehensive-order-456",
            Currency = "RUB",
            PayType = "O", // Single-stage payment
            Description = "Comprehensive test payment for multiple books",
            CustomerKey = "customer-789",
            Email = "comprehensive@test.com",
            Phone = "+79991234567",
            Language = "ru",
            SuccessURL = "https://comprehensive.test.com/success",
            FailURL = "https://comprehensive.test.com/fail",
            NotificationURL = "https://comprehensive.test.com/webhook",
            PaymentExpiry = 60,
            RedirectMethod = "POST",
            Data = new Dictionary<string, string>
            {
                ["metadata1"] = "value1",
                ["metadata2"] = "value2"
            },
            Items = new List<OrderItemDto>
            {
                new OrderItemDto
                {
                    Name = "Programming Book",
                    Quantity = 1,
                    Price = 199900, // 1999.00 RUB
                    Amount = 199900,
                    Category = "Books",
                    Tax = "vat20"
                },
                new OrderItemDto
                {
                    Name = "Technical Manual",
                    Quantity = 1,
                    Price = 100000, // 1000.00 RUB
                    Amount = 100000,
                    Category = "Manuals",
                    Tax = "vat20"
                }
            },
            Receipt = new ReceiptDto
            {
                Email = "receipt@test.com",
                Phone = "+79991234567",
                Taxation = "usn_income"
            }
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/v1/paymentinit/init", request, _jsonOptions);

        // Assert
        Assert.True(response.IsSuccessStatusCode);
        
        var content = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<PaymentInitResponseDto>(content, _jsonOptions);
        
        Assert.NotNull(result);
        Assert.True(result.Success);
        Assert.NotNull(result.PaymentId);
        Assert.Equal(request.OrderId, result.OrderId);
        Assert.Equal(request.Amount, result.Amount);
        Assert.Equal(request.Currency, result.Currency);
        Assert.NotNull(result.PaymentURL);
        Assert.NotNull(result.Details);
        Assert.Equal(request.Description, result.Details.Description);
        Assert.Equal(request.PayType, result.Details.PayType);
        Assert.Equal(request.Language, result.Details.Language);
        Assert.Equal(2, result.Details.Items?.Count);
    }
}

/// <summary>
/// Test-specific stubs for services that may not have full implementations yet
/// </summary>
public static class TestServiceStubs
{
    public static Mock<IPaymentAuthenticationService> CreateAuthenticationServiceMock(bool isAuthenticated = true)
    {
        var mock = new Mock<IPaymentAuthenticationService>();
        mock.Setup(x => x.AuthenticateAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new { IsAuthenticated = isAuthenticated, FailureReason = isAuthenticated ? null : "Test authentication failure" });
        return mock;
    }

    public static Mock<IBusinessRuleEngineService> CreateBusinessRuleServiceMock(bool isValid = true)
    {
        var mock = new Mock<IBusinessRuleEngineService>();
        mock.Setup(x => x.EvaluatePaymentRulesAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new 
            { 
                IsValid = isValid, 
                Errors = isValid ? new List<string>() : new List<string> { "Test rule violation" },
                ViolatedRules = new List<object>(),
                EvaluatedRules = new List<object>()
            });
        return mock;
    }
}