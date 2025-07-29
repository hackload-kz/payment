// SPDX-License-Identifier: MIT
// Copyright (c) 2025 HackLoad Payment Gateway

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using PaymentGateway.Core.DTOs.PaymentConfirm;
using PaymentGateway.Core.Services;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Caching.Memory;
using Moq;
using PaymentGateway.Core.Entities;

namespace PaymentGateway.Tests.Integration;

public class PaymentConfirmControllerTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;
    private readonly JsonSerializerOptions _jsonOptions;

    public PaymentConfirmControllerTests(WebApplicationFactory<Program> factory)
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

                // Mock payment confirmation service
                services.AddScoped<IPaymentConfirmationService>(provider =>
                {
                    var mock = new Mock<IPaymentConfirmationService>();
                    
                    // Setup successful confirmation
                    mock.Setup(x => x.ConfirmPaymentAsync(It.IsAny<long>(), It.IsAny<ConfirmationRequest>(), It.IsAny<CancellationToken>()))
                        .ReturnsAsync((long paymentId, ConfirmationRequest request, CancellationToken ct) => new ConfirmationResult
                        {
                            PaymentId = paymentId,
                            IsSuccess = true,
                            PreviousStatus = PaymentStatus.AUTHORIZED,
                            CurrentStatus = PaymentStatus.CONFIRMED,
                            ConfirmedAt = DateTime.UtcNow,
                            ConfirmationId = Guid.NewGuid().ToString(),
                            ProcessingDuration = TimeSpan.FromMilliseconds(250),
                            ResultMetadata = new Dictionary<string, object>
                            {
                                ["confirmed_amount"] = request.Amount ?? 150000m
                            }
                        });

                    // Setup failed confirmation for invalid payment ID
                    mock.Setup(x => x.ConfirmPaymentAsync(999999, It.IsAny<ConfirmationRequest>(), It.IsAny<CancellationToken>()))
                        .ReturnsAsync(new ConfirmationResult
                        {
                            PaymentId = 999999,
                            IsSuccess = false,
                            Errors = new List<string> { "Payment not found" },
                            ProcessingDuration = TimeSpan.FromMilliseconds(50)
                        });

                    // Setup failed confirmation for wrong status
                    mock.Setup(x => x.ConfirmPaymentAsync(777777, It.IsAny<ConfirmationRequest>(), It.IsAny<CancellationToken>()))
                        .ReturnsAsync(new ConfirmationResult
                        {
                            PaymentId = 777777,
                            IsSuccess = false,
                            PreviousStatus = PaymentStatus.NEW,
                            Errors = new List<string> { "Payment status must be AUTHORIZED for confirmation. Current status: NEW" },
                            ProcessingDuration = TimeSpan.FromMilliseconds(80)
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
    public async Task ConfirmPayment_ValidRequest_ReturnsSuccess()
    {
        // Arrange
        var request = new PaymentConfirmRequestDto
        {
            TeamSlug = "test-team",
            Token = "valid-test-token",
            PaymentId = "pay_123456789",
            Amount = 150000,
            Description = "Order confirmation for books",
            Receipt = new ConfirmReceiptDto
            {
                Email = "customer@example.com",
                Phone = "+79001234567"
            },
            Data = new Dictionary<string, string>
            {
                ["confirmationReason"] = "Customer payment approved",
                ["merchantReference"] = "ORDER-12345"
            }
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/v1/paymentconfirm/confirm", request, _jsonOptions);

        // Assert
        Assert.True(response.IsSuccessStatusCode);

        var content = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<PaymentConfirmResponseDto>(content, _jsonOptions);

        Assert.NotNull(result);
        Assert.True(result.Success);
        Assert.Equal(request.PaymentId, result.PaymentId);
        Assert.Equal("CONFIRMED", result.Status);
        Assert.Equal(150000, result.ConfirmedAmount);
        Assert.Equal(0, result.RemainingAmount);
        Assert.Equal("RUB", result.Currency);
        Assert.NotNull(result.ConfirmedAt);
        Assert.NotNull(result.BankDetails);
        Assert.Equal("00", result.BankDetails.ResponseCode);
        Assert.Equal("Approved", result.BankDetails.ResponseMessage);
    }

    [Fact]
    public async Task ConfirmPayment_WithIdempotencyKey_ReturnsCachedResult()
    {
        // Arrange
        var request = new PaymentConfirmRequestDto
        {
            TeamSlug = "test-team",
            Token = "valid-test-token",
            PaymentId = "pay_123456789",
            Amount = 150000,
            Description = "Idempotent confirmation",
            Data = new Dictionary<string, string>
            {
                ["idempotencyKey"] = "test-idempotency-key-123"
            }
        };

        // Act - First request
        var response1 = await _client.PostAsJsonAsync("/api/v1/paymentconfirm/confirm", request, _jsonOptions);
        var content1 = await response1.Content.ReadAsStringAsync();
        var result1 = JsonSerializer.Deserialize<PaymentConfirmResponseDto>(content1, _jsonOptions);

        // Act - Second request with same idempotency key
        var response2 = await _client.PostAsJsonAsync("/api/v1/paymentconfirm/confirm", request, _jsonOptions);
        var content2 = await response2.Content.ReadAsStringAsync();
        var result2 = JsonSerializer.Deserialize<PaymentConfirmResponseDto>(content2, _jsonOptions);

        // Assert
        Assert.True(response1.IsSuccessStatusCode);
        Assert.True(response2.IsSuccessStatusCode);
        Assert.NotNull(result1);
        Assert.NotNull(result2);
        Assert.True(result1.Success);
        Assert.True(result2.Success);
        
        // Results should be identical due to caching
        Assert.Equal(result1.PaymentId, result2.PaymentId);
        Assert.Equal(result1.ConfirmedAmount, result2.ConfirmedAmount);
        Assert.Equal(result1.Status, result2.Status);
    }

    [Fact]
    public async Task ConfirmPayment_NullRequest_ReturnsBadRequest()
    {
        // Act
        var response = await _client.PostAsync("/api/v1/paymentconfirm/confirm", null);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var content = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<PaymentConfirmResponseDto>(content, _jsonOptions);

        Assert.NotNull(result);
        Assert.False(result.Success);
        Assert.Equal("2000", result.ErrorCode);
        Assert.Equal("Invalid request", result.Message);
    }

    [Fact]
    public async Task ConfirmPayment_MissingPaymentId_ReturnsBadRequest()
    {
        // Arrange
        var request = new PaymentConfirmRequestDto
        {
            TeamSlug = "test-team",
            Token = "valid-test-token",
            // PaymentId missing
            Amount = 150000,
            Description = "Test confirmation"
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/v1/paymentconfirm/confirm", request, _jsonOptions);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var content = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<PaymentConfirmResponseDto>(content, _jsonOptions);

        Assert.NotNull(result);
        Assert.False(result.Success);
        Assert.Equal("2100", result.ErrorCode);
        Assert.Contains("PaymentId is required", result.Details);
    }

    [Fact]
    public async Task ConfirmPayment_InvalidPaymentIdFormat_ReturnsBadRequest()
    {
        // Arrange
        var request = new PaymentConfirmRequestDto
        {
            TeamSlug = "test-team",
            Token = "valid-test-token",
            PaymentId = "invalid-payment-id-with-special-chars-@#$%",
            Amount = 150000,
            Description = "Test confirmation"
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/v1/paymentconfirm/confirm", request, _jsonOptions);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var content = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<PaymentConfirmResponseDto>(content, _jsonOptions);

        Assert.NotNull(result);
        Assert.False(result.Success);
        Assert.Equal("2100", result.ErrorCode);
        Assert.Contains("PaymentId can only contain alphanumeric characters", result.Details);
    }

    [Fact]
    public async Task ConfirmPayment_InvalidAmount_ReturnsBadRequest()
    {
        // Arrange
        var request = new PaymentConfirmRequestDto
        {
            TeamSlug = "test-team",
            Token = "valid-test-token",
            PaymentId = "pay_123456789",
            Amount = -1000, // Invalid negative amount
            Description = "Test confirmation"
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/v1/paymentconfirm/confirm", request, _jsonOptions);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var content = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<PaymentConfirmResponseDto>(content, _jsonOptions);

        Assert.NotNull(result);
        Assert.False(result.Success);
        Assert.Equal("2100", result.ErrorCode);
        Assert.Contains("Amount must be greater than 0", result.Details);
    }

    [Fact]
    public async Task ConfirmPayment_ExcessiveAmount_ReturnsBadRequest()
    {
        // Arrange
        var request = new PaymentConfirmRequestDto
        {
            TeamSlug = "test-team",
            Token = "valid-test-token",
            PaymentId = "pay_123456789",
            Amount = 60000000, // Exceeds maximum allowed
            Description = "Test confirmation"
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/v1/paymentconfirm/confirm", request, _jsonOptions);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var content = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<PaymentConfirmResponseDto>(content, _jsonOptions);

        Assert.NotNull(result);
        Assert.False(result.Success);
        Assert.Equal("2100", result.ErrorCode);
        Assert.Contains("Amount cannot exceed 50000000 kopecks", result.Details);
    }

    [Fact]
    public async Task ConfirmPayment_MissingTeamSlug_ReturnsBadRequest()
    {
        // Arrange
        var request = new PaymentConfirmRequestDto
        {
            // TeamSlug missing
            Token = "valid-test-token",
            PaymentId = "pay_123456789",
            Amount = 150000,
            Description = "Test confirmation"
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/v1/paymentconfirm/confirm", request, _jsonOptions);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var content = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<PaymentConfirmResponseDto>(content, _jsonOptions);

        Assert.NotNull(result);
        Assert.False(result.Success);
        Assert.Equal("2100", result.ErrorCode);
        Assert.Contains("TeamSlug is required for authentication", result.Details);
    }

    [Fact]
    public async Task ConfirmPayment_MissingToken_ReturnsBadRequest()
    {
        // Arrange
        var request = new PaymentConfirmRequestDto
        {
            TeamSlug = "test-team",
            // Token missing
            PaymentId = "pay_123456789",
            Amount = 150000,
            Description = "Test confirmation"
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/v1/paymentconfirm/confirm", request, _jsonOptions);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var content = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<PaymentConfirmResponseDto>(content, _jsonOptions);

        Assert.NotNull(result);
        Assert.False(result.Success);
        Assert.Equal("2100", result.ErrorCode);
        Assert.Contains("Token is required for authentication", result.Details);
    }

    [Fact]
    public async Task ConfirmPayment_InvalidReceiptEmail_ReturnsBadRequest()
    {
        // Arrange
        var request = new PaymentConfirmRequestDto
        {
            TeamSlug = "test-team",
            Token = "valid-test-token",
            PaymentId = "pay_123456789",
            Amount = 150000,
            Description = "Test confirmation",
            Receipt = new ConfirmReceiptDto
            {
                Email = "invalid-email-format", // Invalid email
                Phone = "+79001234567"
            }
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/v1/paymentconfirm/confirm", request, _jsonOptions);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var content = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<PaymentConfirmResponseDto>(content, _jsonOptions);

        Assert.NotNull(result);
        Assert.False(result.Success);
        Assert.Equal("2100", result.ErrorCode);
        Assert.Contains("Invalid receipt email format", result.Details);
    }

    [Fact]
    public async Task ConfirmPayment_PaymentNotFound_ReturnsNotFound()
    {
        // Arrange
        var request = new PaymentConfirmRequestDto
        {
            TeamSlug = "test-team",
            Token = "valid-test-token",
            PaymentId = "pay_999999", // This will trigger payment not found
            Amount = 150000,
            Description = "Test confirmation"
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/v1/paymentconfirm/confirm", request, _jsonOptions);

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        var content = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<PaymentConfirmResponseDto>(content, _jsonOptions);

        Assert.NotNull(result);
        Assert.False(result.Success);
        Assert.Equal("2404", result.ErrorCode);
        Assert.Contains("Payment not found", result.Details);
    }

    [Fact]
    public async Task ConfirmPayment_PaymentInWrongStatus_ReturnsConflict()
    {
        // Arrange
        var request = new PaymentConfirmRequestDto
        {
            TeamSlug = "test-team",
            Token = "valid-test-token",
            PaymentId = "pay_777777", // This will trigger wrong status error
            Amount = 150000,
            Description = "Test confirmation"
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/v1/paymentconfirm/confirm", request, _jsonOptions);

        // Assert
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        var content = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<PaymentConfirmResponseDto>(content, _jsonOptions);

        Assert.NotNull(result);
        Assert.False(result.Success);
        Assert.Equal("2409", result.ErrorCode);
        Assert.Contains("Payment status must be AUTHORIZED", result.Details);
    }

    [Fact]
    public async Task ConfirmPayment_LongDescription_TruncatesGracefully()
    {
        // Arrange
        var longDescription = new string('A', 300); // Exceeds 255 character limit
        var request = new PaymentConfirmRequestDto
        {
            TeamSlug = "test-team",
            Token = "valid-test-token",
            PaymentId = "pay_123456789",
            Amount = 150000,
            Description = longDescription
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/v1/paymentconfirm/confirm", request, _jsonOptions);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var content = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<PaymentConfirmResponseDto>(content, _jsonOptions);

        Assert.NotNull(result);
        Assert.False(result.Success);
        Assert.Equal("2100", result.ErrorCode);
        Assert.Contains("Description cannot exceed 255 characters", result.Details);
    }

    [Fact]
    public async Task ConfirmPayment_WithReceiptAndItems_ReturnsSuccess()
    {
        // Arrange
        var request = new PaymentConfirmRequestDto
        {
            TeamSlug = "test-team",
            Token = "valid-test-token",
            PaymentId = "pay_123456789",
            Amount = 150000,
            Description = "Order confirmation with receipt",
            Receipt = new ConfirmReceiptDto
            {
                Email = "customer@example.com",
                Phone = "+79001234567",
                Receipt_FFD_105 = new
                {
                    inn = "1234567890",
                    receipt_type = "payment"
                }
            },
            Items = new List<ConfirmItemDto>
            {
                new ConfirmItemDto
                {
                    ItemId = "book-001",
                    Quantity = 2,
                    Amount = 75000,
                    Tax = "vat20"
                },
                new ConfirmItemDto
                {
                    ItemId = "book-002",
                    Quantity = 1,
                    Amount = 75000,
                    Tax = "vat20"
                }
            },
            Data = new Dictionary<string, string>
            {
                ["orderType"] = "books",
                ["customerType"] = "individual"
            }
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/v1/paymentconfirm/confirm", request, _jsonOptions);

        // Assert
        Assert.True(response.IsSuccessStatusCode);

        var content = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<PaymentConfirmResponseDto>(content, _jsonOptions);

        Assert.NotNull(result);
        Assert.True(result.Success);
        Assert.Equal(request.PaymentId, result.PaymentId);
        Assert.Equal("CONFIRMED", result.Status);
        Assert.Equal(150000, result.ConfirmedAmount);
        Assert.NotNull(result.Details);
        Assert.Equal(request.Description, result.Details.Description);
        Assert.NotNull(result.Details.Data);
        Assert.True(result.Details.Data.ContainsKey("orderType"));
    }

    [Fact]
    public async Task ConfirmPayment_ComprehensiveFieldValidation_ReturnsAllFields()
    {
        // Arrange
        var request = new PaymentConfirmRequestDto
        {
            TeamSlug = "test-team",
            Token = "valid-test-token",
            PaymentId = "pay_123456789",
            Amount = 250000,
            Description = "Comprehensive confirmation test",
            Receipt = new ConfirmReceiptDto
            {
                Email = "comprehensive@test.com",
                Phone = "+79998887766"
            },
            Data = new Dictionary<string, string>
            {
                ["merchantId"] = "MERCHANT-123",
                ["orderId"] = "ORDER-456",
                ["customerKey"] = "CUSTOMER-789"
            }
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/v1/paymentconfirm/confirm", request, _jsonOptions);

        // Assert
        Assert.True(response.IsSuccessStatusCode);

        var content = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<PaymentConfirmResponseDto>(content, _jsonOptions);

        Assert.NotNull(result);
        Assert.True(result.Success);
        
        // Verify all response fields are populated
        Assert.NotNull(result.PaymentId);
        Assert.NotNull(result.Status);
        Assert.True(result.AuthorizedAmount > 0);
        Assert.True(result.ConfirmedAmount > 0);
        Assert.Equal(0, result.RemainingAmount);
        Assert.NotNull(result.Currency);
        Assert.True(result.ConfirmedAt.HasValue);
        
        // Verify bank details
        Assert.NotNull(result.BankDetails);
        Assert.NotNull(result.BankDetails.BankTransactionId);
        Assert.NotNull(result.BankDetails.AuthorizationCode);
        Assert.NotNull(result.BankDetails.Rrn);
        Assert.Equal("00", result.BankDetails.ResponseCode);
        Assert.Equal("Approved", result.BankDetails.ResponseMessage);
        
        // Verify details
        Assert.NotNull(result.Details);
        Assert.Equal(request.Description, result.Details.Description);
        Assert.True(result.Details.ProcessingDuration.HasValue);
        Assert.NotNull(result.Details.Data);
    }

    [Fact]
    public async Task ConfirmPayment_MultipleRequestsWithDifferentIdempotencyKeys_ReturnsUniqueResults()
    {
        // Arrange
        var request1 = new PaymentConfirmRequestDto
        {
            TeamSlug = "test-team",
            Token = "valid-test-token",
            PaymentId = "pay_123456789",
            Amount = 150000,
            Description = "First confirmation",
            Data = new Dictionary<string, string>
            {
                ["idempotencyKey"] = "key-001"
            }
        };

        var request2 = new PaymentConfirmRequestDto
        {
            TeamSlug = "test-team",
            Token = "valid-test-token",
            PaymentId = "pay_123456789",
            Amount = 150000,
            Description = "Second confirmation",
            Data = new Dictionary<string, string>
            {
                ["idempotencyKey"] = "key-002"
            }
        };

        // Act
        var response1 = await _client.PostAsJsonAsync("/api/v1/paymentconfirm/confirm", request1, _jsonOptions);
        var response2 = await _client.PostAsJsonAsync("/api/v1/paymentconfirm/confirm", request2, _jsonOptions);

        // Assert
        Assert.True(response1.IsSuccessStatusCode);
        Assert.True(response2.IsSuccessStatusCode);

        var content1 = await response1.Content.ReadAsStringAsync();
        var result1 = JsonSerializer.Deserialize<PaymentConfirmResponseDto>(content1, _jsonOptions);

        var content2 = await response2.Content.ReadAsStringAsync();
        var result2 = JsonSerializer.Deserialize<PaymentConfirmResponseDto>(content2, _jsonOptions);

        Assert.NotNull(result1);
        Assert.NotNull(result2);
        Assert.True(result1.Success);
        Assert.True(result2.Success);
        
        // Different idempotency keys should allow both requests to succeed
        Assert.Equal(request1.PaymentId, result1.PaymentId);
        Assert.Equal(request2.PaymentId, result2.PaymentId);
    }
}

/// <summary>
/// Test utilities for PaymentConfirm testing
/// </summary>
public static class PaymentConfirmTestUtils
{
    public static PaymentConfirmRequestDto CreateValidConfirmRequest(string? paymentId = null, decimal? amount = null)
    {
        return new PaymentConfirmRequestDto
        {
            TeamSlug = "test-team",
            Token = "valid-test-token",
            PaymentId = paymentId ?? "pay_123456789",
            Amount = amount ?? 150000,
            Description = "Test payment confirmation",
            Data = new Dictionary<string, string>
            {
                ["testReason"] = "automated_test"
            }
        };
    }

    public static PaymentConfirmRequestDto CreateValidConfirmRequestWithReceipt(string? paymentId = null, decimal? amount = null)
    {
        var request = CreateValidConfirmRequest(paymentId, amount);
        request.Receipt = new ConfirmReceiptDto
        {
            Email = "test@example.com",
            Phone = "+79001234567"
        };
        return request;
    }

    public static PaymentConfirmRequestDto CreateValidConfirmRequestWithItems(string? paymentId = null, decimal? amount = null)
    {
        var request = CreateValidConfirmRequestWithReceipt(paymentId, amount);
        request.Items = new List<ConfirmItemDto>
        {
            new ConfirmItemDto
            {
                ItemId = "item-001",
                Quantity = 1,
                Amount = amount ?? 150000,
                Tax = "vat20"
            }
        };
        return request;
    }
}