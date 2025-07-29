// SPDX-License-Identifier: MIT
// Copyright (c) 2025 HackLoad Payment Gateway

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using PaymentGateway.Core.DTOs.PaymentCancel;
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

public class PaymentCancelControllerTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;
    private readonly JsonSerializerOptions _jsonOptions;

    public PaymentCancelControllerTests(WebApplicationFactory<Program> factory)
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

                // Mock payment cancellation service
                services.AddScoped<IPaymentCancellationService>(provider =>
                {
                    var mock = new Mock<IPaymentCancellationService>();
                    
                    // Setup successful cancellation for NEW payment (FULL_CANCELLATION)
                    mock.Setup(x => x.CancelPaymentAsync(123456789, It.IsAny<CancellationRequest>(), It.IsAny<CancellationToken>()))
                        .ReturnsAsync((long paymentId, CancellationRequest request, CancellationToken ct) => new CancellationResult
                        {
                            TeamSlug = request.TeamSlug,
                            OrderId = "test-order-123",
                            Success = true,
                            Status = PaymentStatus.CANCELLED,
                            OriginalAmount = 150000,
                            NewAmount = 0,
                            PaymentId = paymentId,
                            ErrorCode = "0",
                            Message = "Cancellation successful",
                            ExternalRequestId = request.ExternalRequestId,
                            OperationType = CancellationType.FULL_CANCELLATION,
                            CancelledAt = DateTime.UtcNow,
                            ProcessingDuration = TimeSpan.FromMilliseconds(250),
                            ResultMetadata = new Dictionary<string, object>
                            {
                                ["cancelled_amount"] = 150000m,
                                ["cancellation_reason"] = request.CancellationReason
                            }
                        });

                    // Setup successful reversal for AUTHORIZED payment (FULL_REVERSAL)
                    mock.Setup(x => x.CancelPaymentAsync(987654321, It.IsAny<CancellationRequest>(), It.IsAny<CancellationToken>()))
                        .ReturnsAsync((long paymentId, CancellationRequest request, CancellationToken ct) => new CancellationResult
                        {
                            TeamSlug = request.TeamSlug,
                            OrderId = "test-order-456",
                            Success = true,
                            Status = PaymentStatus.CANCELLED,
                            OriginalAmount = 250000,
                            NewAmount = 0,
                            PaymentId = paymentId,
                            ErrorCode = "0",
                            Message = "Reversal successful",
                            ExternalRequestId = request.ExternalRequestId,
                            OperationType = CancellationType.FULL_REVERSAL,
                            CancelledAt = DateTime.UtcNow,
                            ProcessingDuration = TimeSpan.FromMilliseconds(180)
                        });

                    // Setup successful refund for CONFIRMED payment (FULL_REFUND)
                    mock.Setup(x => x.CancelPaymentAsync(555666777, It.IsAny<CancellationRequest>(), It.IsAny<CancellationToken>()))
                        .ReturnsAsync(new CancellationResult
                        {
                            TeamSlug = "test-team",
                            OrderId = "test-order-789",
                            Success = true,
                            Status = PaymentStatus.REFUNDED,
                            OriginalAmount = 350000,
                            NewAmount = 0,
                            PaymentId = 555666777,
                            ErrorCode = "0",
                            Message = "Refund successful",
                            OperationType = CancellationType.FULL_REFUND,
                            CancelledAt = DateTime.UtcNow,
                            ProcessingDuration = TimeSpan.FromMilliseconds(320)
                        });

                    // Setup failed cancellation for payment not found
                    mock.Setup(x => x.CancelPaymentAsync(999999999, It.IsAny<CancellationRequest>(), It.IsAny<CancellationToken>()))
                        .ReturnsAsync(new CancellationResult
                        {
                            PaymentId = 999999999,
                            Success = false,
                            ErrorCode = "1004",
                            Message = "Payment not found",
                            ProcessingDuration = TimeSpan.FromMilliseconds(50)
                        });

                    // Setup failed cancellation for wrong status
                    mock.Setup(x => x.CancelPaymentAsync(777888999, It.IsAny<CancellationRequest>(), It.IsAny<CancellationToken>()))
                        .ReturnsAsync(new CancellationResult
                        {
                            PaymentId = 777888999,
                            Success = false,
                            Status = PaymentStatus.FAILED,
                            ErrorCode = "1005",
                            Message = "Payment cannot be cancelled",
                            Details = "Payment in FAILED status cannot be cancelled. Only NEW, AUTHORIZED, and CONFIRMED payments can be cancelled.",
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
    public async Task CancelPayment_NewPaymentFullCancellation_ReturnsSuccess()
    {
        // Arrange
        var request = new PaymentCancelRequestDto
        {
            TeamSlug = "test-team",
            Token = "valid-test-token",
            PaymentId = "pay_123456789",
            Reason = "Customer requested cancellation",
            Receipt = new CancelReceiptDto
            {
                Email = "customer@example.com",
                Phone = "+79001234567",
                Taxation = "osn"
            },
            Data = new Dictionary<string, string>
            {
                ["customerReason"] = "Changed mind",
                ["merchantReference"] = "CANCEL-ORDER-12345"
            }
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/v1/paymentcancel/cancel", request, _jsonOptions);

        // Assert
        Assert.True(response.IsSuccessStatusCode);

        var content = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<PaymentCancelResponseDto>(content, _jsonOptions);

        Assert.NotNull(result);
        Assert.True(result.Success);
        Assert.Equal(request.PaymentId, result.PaymentId);
        Assert.Equal("CANCELLED", result.Status);
        Assert.Equal("FULL_CANCELLATION", result.CancellationType);
        Assert.Equal(150000, result.OriginalAmount);
        Assert.Equal(150000, result.CancelledAmount);
        Assert.Equal(0, result.RemainingAmount);
        Assert.Equal("RUB", result.Currency);
        Assert.NotNull(result.CancelledAt);
        Assert.NotNull(result.BankDetails);
        Assert.Equal("00", result.BankDetails.ResponseCode);
        Assert.Equal("Cancellation Approved", result.BankDetails.ResponseMessage);
        Assert.NotNull(result.Details);
        Assert.Equal(request.Reason, result.Details.Reason);
        Assert.False(result.Details.WasForced);
        Assert.Null(result.Refund); // No refund for cancellation
    }

    [Fact]
    public async Task CancelPayment_AuthorizedPaymentFullReversal_ReturnsSuccess()
    {
        // Arrange
        var request = new PaymentCancelRequestDto
        {
            TeamSlug = "test-team",
            Token = "valid-test-token",
            PaymentId = "pay_987654321",
            Reason = "Merchant cancellation",
            Force = false
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/v1/paymentcancel/cancel", request, _jsonOptions);

        // Assert
        Assert.True(response.IsSuccessStatusCode);

        var content = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<PaymentCancelResponseDto>(content, _jsonOptions);

        Assert.NotNull(result);
        Assert.True(result.Success);
        Assert.Equal(request.PaymentId, result.PaymentId);
        Assert.Equal("CANCELLED", result.Status);
        Assert.Equal("FULL_REVERSAL", result.CancellationType);
        Assert.Equal(250000, result.OriginalAmount);
        Assert.Equal(250000, result.CancelledAmount);
        Assert.Equal(0, result.RemainingAmount);
        Assert.NotNull(result.BankDetails);
        Assert.Contains("REVERSAL", result.BankDetails.CancellationAuthorizationCode);
        Assert.Equal("Reversal Approved", result.BankDetails.ResponseMessage);
        Assert.NotNull(result.Details?.Warnings);
        Assert.Contains("Authorization hold will be released within 24 hours", result.Details.Warnings);
    }

    [Fact]
    public async Task CancelPayment_ConfirmedPaymentFullRefund_ReturnsSuccessWithRefundDetails()
    {
        // Arrange
        var request = new PaymentCancelRequestDto
        {
            TeamSlug = "test-team",
            Token = "valid-test-token",
            PaymentId = "pay_555666777",
            Reason = "Product return",
            Receipt = new CancelReceiptDto
            {
                Email = "customer@example.com",
                Taxation = "osn"
            },
            Data = new Dictionary<string, string>
            {
                ["externalRequestId"] = "refund_req_12345",
                ["returnReason"] = "Defective product"
            }
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/v1/paymentcancel/cancel", request, _jsonOptions);

        // Assert
        Assert.True(response.IsSuccessStatusCode);

        var content = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<PaymentCancelResponseDto>(content, _jsonOptions);

        Assert.NotNull(result);
        Assert.True(result.Success);
        Assert.Equal(request.PaymentId, result.PaymentId);
        Assert.Equal("REFUNDED", result.Status);
        Assert.Equal("FULL_REFUND", result.CancellationType);
        Assert.Equal(350000, result.OriginalAmount);
        Assert.Equal(350000, result.CancelledAmount);
        Assert.Equal(0, result.RemainingAmount);
        Assert.NotNull(result.BankDetails);
        Assert.Contains("REFUND", result.BankDetails.CancellationAuthorizationCode);
        Assert.Equal("Refund Approved", result.BankDetails.ResponseMessage);
        
        // Verify refund details
        Assert.NotNull(result.Refund);
        Assert.NotNull(result.Refund.RefundId);
        Assert.Equal("PROCESSING", result.Refund.RefundStatus);
        Assert.Equal("3-5 business days", result.Refund.ExpectedProcessingTime);
        Assert.Equal("card", result.Refund.RefundMethod);
        Assert.NotNull(result.Refund.CardInfo);
        Assert.Equal("4111****1111", result.Refund.CardInfo.CardMask);
        Assert.Equal("Visa", result.Refund.CardInfo.CardType);
        
        // Verify warnings
        Assert.NotNull(result.Details?.Warnings);
        Assert.Contains("Refund processing may take 3-5 business days", result.Details.Warnings);
    }

    [Fact]
    public async Task CancelPayment_WithIdempotencyKey_ReturnsCachedResult()
    {
        // Arrange
        var request = new PaymentCancelRequestDto
        {
            TeamSlug = "test-team",
            Token = "valid-test-token",
            PaymentId = "pay_123456789",
            Reason = "Idempotent cancellation",
            Data = new Dictionary<string, string>
            {
                ["externalRequestId"] = "test-idempotency-key-123"
            }
        };

        // Act - First request
        var response1 = await _client.PostAsJsonAsync("/api/v1/paymentcancel/cancel", request, _jsonOptions);
        var content1 = await response1.Content.ReadAsStringAsync();
        var result1 = JsonSerializer.Deserialize<PaymentCancelResponseDto>(content1, _jsonOptions);

        // Act - Second request with same idempotency key
        var response2 = await _client.PostAsJsonAsync("/api/v1/paymentcancel/cancel", request, _jsonOptions);
        var content2 = await response2.Content.ReadAsStringAsync();
        var result2 = JsonSerializer.Deserialize<PaymentCancelResponseDto>(content2, _jsonOptions);

        // Assert
        Assert.True(response1.IsSuccessStatusCode);
        Assert.True(response2.IsSuccessStatusCode);
        Assert.NotNull(result1);
        Assert.NotNull(result2);
        Assert.True(result1.Success);
        Assert.True(result2.Success);
        
        // Results should be identical due to caching
        Assert.Equal(result1.PaymentId, result2.PaymentId);
        Assert.Equal(result1.CancelledAmount, result2.CancelledAmount);
        Assert.Equal(result1.Status, result2.Status);
        Assert.Equal(result1.CancellationType, result2.CancellationType);
    }

    [Fact]
    public async Task CancelPayment_NullRequest_ReturnsBadRequest()
    {
        // Act
        var response = await _client.PostAsync("/api/v1/paymentcancel/cancel", null);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var content = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<PaymentCancelResponseDto>(content, _jsonOptions);

        Assert.NotNull(result);
        Assert.False(result.Success);
        Assert.Equal("3000", result.ErrorCode);
        Assert.Equal("Invalid request", result.Message);
    }

    [Fact]
    public async Task CancelPayment_MissingPaymentId_ReturnsBadRequest()
    {
        // Arrange
        var request = new PaymentCancelRequestDto
        {
            TeamSlug = "test-team",
            Token = "valid-test-token",
            // PaymentId missing
            Reason = "Test cancellation"
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/v1/paymentcancel/cancel", request, _jsonOptions);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var content = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<PaymentCancelResponseDto>(content, _jsonOptions);

        Assert.NotNull(result);
        Assert.False(result.Success);
        Assert.Equal("3100", result.ErrorCode);
        Assert.Contains("PaymentId is required", result.Details);
    }

    [Fact]
    public async Task CancelPayment_InvalidPaymentIdFormat_ReturnsBadRequest()
    {
        // Arrange
        var request = new PaymentCancelRequestDto
        {
            TeamSlug = "test-team",
            Token = "valid-test-token",
            PaymentId = "invalid-payment-id-with-special-chars-@#$%",
            Reason = "Test cancellation"
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/v1/paymentcancel/cancel", request, _jsonOptions);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var content = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<PaymentCancelResponseDto>(content, _jsonOptions);

        Assert.NotNull(result);
        Assert.False(result.Success);
        Assert.Equal("3100", result.ErrorCode);
        Assert.Contains("PaymentId can only contain alphanumeric characters", result.Details);
    }

    [Fact]
    public async Task CancelPayment_InvalidAmount_ReturnsBadRequest()
    {
        // Arrange
        var request = new PaymentCancelRequestDto
        {
            TeamSlug = "test-team",
            Token = "valid-test-token",
            PaymentId = "pay_123456789",
            Amount = -1000, // Invalid negative amount
            Reason = "Test cancellation"
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/v1/paymentcancel/cancel", request, _jsonOptions);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var content = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<PaymentCancelResponseDto>(content, _jsonOptions);

        Assert.NotNull(result);
        Assert.False(result.Success);
        Assert.Equal("3100", result.ErrorCode);
        Assert.Contains("Amount must be greater than 0", result.Details);
    }

    [Fact]
    public async Task CancelPayment_ExcessiveAmount_ReturnsBadRequest()
    {
        // Arrange
        var request = new PaymentCancelRequestDto
        {
            TeamSlug = "test-team",
            Token = "valid-test-token",
            PaymentId = "pay_123456789",
            Amount = 60000000, // Exceeds maximum allowed
            Reason = "Test cancellation"
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/v1/paymentcancel/cancel", request, _jsonOptions);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var content = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<PaymentCancelResponseDto>(content, _jsonOptions);

        Assert.NotNull(result);
        Assert.False(result.Success);
        Assert.Equal("3100", result.ErrorCode);
        Assert.Contains("Amount cannot exceed 50000000 kopecks", result.Details);
    }

    [Fact]
    public async Task CancelPayment_MissingTeamSlug_ReturnsBadRequest()
    {
        // Arrange
        var request = new PaymentCancelRequestDto
        {
            // TeamSlug missing
            Token = "valid-test-token",
            PaymentId = "pay_123456789",
            Reason = "Test cancellation"
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/v1/paymentcancel/cancel", request, _jsonOptions);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var content = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<PaymentCancelResponseDto>(content, _jsonOptions);

        Assert.NotNull(result);
        Assert.False(result.Success);
        Assert.Equal("3100", result.ErrorCode);
        Assert.Contains("TeamSlug is required for authentication", result.Details);
    }

    [Fact]
    public async Task CancelPayment_MissingToken_ReturnsBadRequest()
    {
        // Arrange
        var request = new PaymentCancelRequestDto
        {
            TeamSlug = "test-team",
            // Token missing
            PaymentId = "pay_123456789",
            Reason = "Test cancellation"
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/v1/paymentcancel/cancel", request, _jsonOptions);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var content = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<PaymentCancelResponseDto>(content, _jsonOptions);

        Assert.NotNull(result);
        Assert.False(result.Success);
        Assert.Equal("3100", result.ErrorCode);
        Assert.Contains("Token is required for authentication", result.Details);
    }

    [Fact]
    public async Task CancelPayment_InvalidReceiptEmail_ReturnsBadRequest()
    {
        // Arrange
        var request = new PaymentCancelRequestDto
        {
            TeamSlug = "test-team",
            Token = "valid-test-token",
            PaymentId = "pay_123456789",
            Reason = "Test cancellation",
            Receipt = new CancelReceiptDto
            {
                Email = "invalid-email-format", // Invalid email
                Phone = "+79001234567"
            }
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/v1/paymentcancel/cancel", request, _jsonOptions);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var content = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<PaymentCancelResponseDto>(content, _jsonOptions);

        Assert.NotNull(result);
        Assert.False(result.Success);
        Assert.Equal("3100", result.ErrorCode);
        Assert.Contains("Invalid receipt email format", result.Details);
    }

    [Fact]
    public async Task CancelPayment_PaymentNotFound_ReturnsNotFound()
    {
        // Arrange
        var request = new PaymentCancelRequestDto
        {
            TeamSlug = "test-team",
            Token = "valid-test-token",
            PaymentId = "pay_999999999", // This will trigger payment not found
            Reason = "Test cancellation"
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/v1/paymentcancel/cancel", request, _jsonOptions);

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        var content = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<PaymentCancelResponseDto>(content, _jsonOptions);

        Assert.NotNull(result);
        Assert.False(result.Success);
        Assert.Equal("3404", result.ErrorCode);
        Assert.Contains("Payment not found", result.Details);
    }

    [Fact]
    public async Task CancelPayment_PaymentInWrongStatus_ReturnsConflict()
    {
        // Arrange
        var request = new PaymentCancelRequestDto
        {
            TeamSlug = "test-team",
            Token = "valid-test-token",
            PaymentId = "pay_777888999", // This will trigger wrong status error
            Reason = "Test cancellation"
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/v1/paymentcancel/cancel", request, _jsonOptions);

        // Assert
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        var content = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<PaymentCancelResponseDto>(content, _jsonOptions);

        Assert.NotNull(result);
        Assert.False(result.Success);
        Assert.Equal("3409", result.ErrorCode);
        Assert.Contains("Payment cannot be cancelled", result.Details);
    }

    [Fact]
    public async Task CancelPayment_LongReason_TruncatesGracefully()
    {
        // Arrange
        var longReason = new string('A', 300); // Exceeds 255 character limit
        var request = new PaymentCancelRequestDto
        {
            TeamSlug = "test-team",
            Token = "valid-test-token",
            PaymentId = "pay_123456789",
            Reason = longReason
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/v1/paymentcancel/cancel", request, _jsonOptions);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var content = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<PaymentCancelResponseDto>(content, _jsonOptions);

        Assert.NotNull(result);
        Assert.False(result.Success);
        Assert.Equal("3100", result.ErrorCode);
        Assert.Contains("Reason cannot exceed 255 characters", result.Details);
    }

    [Fact]
    public async Task CancelPayment_WithReceiptAndItems_ReturnsSuccessWithWarnings()
    {
        // Arrange
        var request = new PaymentCancelRequestDto
        {
            TeamSlug = "test-team",
            Token = "valid-test-token",
            PaymentId = "pay_123456789",
            Reason = "Order cancellation with receipt",
            Receipt = new CancelReceiptDto
            {
                Email = "customer@example.com",
                Phone = "+79001234567",
                Taxation = "osn",
                Receipt_FFD_105 = new
                {
                    inn = "1234567890",
                    receipt_type = "return"
                }
            },
            Items = new List<CancelItemDto>
            {
                new CancelItemDto
                {
                    ItemId = "book-001",
                    Quantity = 2,
                    Amount = 75000,
                    Reason = "Damaged item",
                    Tax = "vat20"
                },
                new CancelItemDto
                {
                    ItemId = "book-002",
                    Quantity = 1,
                    Amount = 75000,
                    Reason = "Wrong item",
                    Tax = "vat20"
                }
            },
            Force = true, // Force cancellation
            Data = new Dictionary<string, string>
            {
                ["orderType"] = "books",
                ["customerType"] = "individual"
            }
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/v1/paymentcancel/cancel", request, _jsonOptions);

        // Assert
        Assert.True(response.IsSuccessStatusCode);

        var content = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<PaymentCancelResponseDto>(content, _jsonOptions);

        Assert.NotNull(result);
        Assert.True(result.Success);
        Assert.Equal(request.PaymentId, result.PaymentId);
        Assert.Equal("CANCELLED", result.Status);
        Assert.Equal("FULL_CANCELLATION", result.CancellationType);
        Assert.NotNull(result.Details);
        Assert.Equal(request.Reason, result.Details.Reason);
        Assert.True(result.Details.WasForced); // Force flag should be preserved
        Assert.NotNull(result.Details.Data);
        Assert.Equal("books", result.Details.Data["orderType"]);
    }

    [Fact]
    public async Task CancelPayment_ComprehensiveFieldValidation_ReturnsAllFields()
    {
        // Arrange
        var request = new PaymentCancelRequestDto
        {
            TeamSlug = "test-team",
            Token = "valid-test-token",
            PaymentId = "pay_555666777", // This triggers FULL_REFUND
            Amount = 350000,
            Reason = "Comprehensive cancellation test",
            Receipt = new CancelReceiptDto
            {
                Email = "comprehensive@test.com",
                Phone = "+79998887766",
                Taxation = "osn"
            },
            Data = new Dictionary<string, string>
            {
                ["merchantId"] = "MERCHANT-123",
                ["externalRequestId"] = "comprehensive-test-id",
                ["customerKey"] = "CUSTOMER-789"
            }
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/v1/paymentcancel/cancel", request, _jsonOptions);

        // Assert
        Assert.True(response.IsSuccessStatusCode);

        var content = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<PaymentCancelResponseDto>(content, _jsonOptions);

        Assert.NotNull(result);
        Assert.True(result.Success);
        
        // Verify all response fields are populated
        Assert.NotNull(result.PaymentId);
        Assert.NotNull(result.OrderId);
        Assert.NotNull(result.Status);
        Assert.NotNull(result.CancellationType);
        Assert.True(result.OriginalAmount > 0);
        Assert.True(result.CancelledAmount > 0);
        Assert.Equal(0, result.RemainingAmount);
        Assert.NotNull(result.Currency);
        Assert.True(result.CancelledAt.HasValue);
        
        // Verify bank details
        Assert.NotNull(result.BankDetails);
        Assert.NotNull(result.BankDetails.BankTransactionId);
        Assert.NotNull(result.BankDetails.OriginalAuthorizationCode);
        Assert.NotNull(result.BankDetails.CancellationAuthorizationCode);
        Assert.NotNull(result.BankDetails.Rrn);
        Assert.Equal("00", result.BankDetails.ResponseCode);
        Assert.Equal("Refund Approved", result.BankDetails.ResponseMessage);
        
        // Verify refund details (for FULL_REFUND)
        Assert.NotNull(result.Refund);
        Assert.NotNull(result.Refund.RefundId);
        Assert.Equal("PROCESSING", result.Refund.RefundStatus);
        Assert.NotNull(result.Refund.CardInfo);
        
        // Verify details
        Assert.NotNull(result.Details);
        Assert.Equal(request.Reason, result.Details.Reason);
        Assert.False(result.Details.WasForced);
        Assert.True(result.Details.ProcessingDuration.HasValue);
        Assert.NotNull(result.Details.Data);
        Assert.NotNull(result.Details.Warnings);
        Assert.Contains("Refund processing may take 3-5 business days", result.Details.Warnings);
    }

    [Fact]
    public async Task CancelPayment_MultipleRequestsWithDifferentIdempotencyKeys_ReturnsUniqueResults()
    {
        // Arrange
        var request1 = new PaymentCancelRequestDto
        {
            TeamSlug = "test-team",
            Token = "valid-test-token",
            PaymentId = "pay_123456789",
            Reason = "First cancellation",
            Data = new Dictionary<string, string>
            {
                ["externalRequestId"] = "key-001"
            }
        };

        var request2 = new PaymentCancelRequestDto
        {
            TeamSlug = "test-team",
            Token = "valid-test-token",
            PaymentId = "pay_123456789",
            Reason = "Second cancellation",
            Data = new Dictionary<string, string>
            {
                ["externalRequestId"] = "key-002"
            }
        };

        // Act
        var response1 = await _client.PostAsJsonAsync("/api/v1/paymentcancel/cancel", request1, _jsonOptions);
        var response2 = await _client.PostAsJsonAsync("/api/v1/paymentcancel/cancel", request2, _jsonOptions);

        // Assert
        Assert.True(response1.IsSuccessStatusCode);
        Assert.True(response2.IsSuccessStatusCode);

        var content1 = await response1.Content.ReadAsStringAsync();
        var result1 = JsonSerializer.Deserialize<PaymentCancelResponseDto>(content1, _jsonOptions);

        var content2 = await response2.Content.ReadAsStringAsync();
        var result2 = JsonSerializer.Deserialize<PaymentCancelResponseDto>(content2, _jsonOptions);

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
/// Test utilities for PaymentCancel testing
/// </summary>
public static class PaymentCancelTestUtils
{
    public static PaymentCancelRequestDto CreateValidCancelRequest(string? paymentId = null, string? reason = null)
    {
        return new PaymentCancelRequestDto
        {
            TeamSlug = "test-team",
            Token = "valid-test-token",
            PaymentId = paymentId ?? "pay_123456789",
            Reason = reason ?? "Test payment cancellation",
            Data = new Dictionary<string, string>
            {
                ["testReason"] = "automated_test"
            }
        };
    }

    public static PaymentCancelRequestDto CreateValidCancelRequestWithReceipt(string? paymentId = null, string? reason = null)
    {
        var request = CreateValidCancelRequest(paymentId, reason);
        request.Receipt = new CancelReceiptDto
        {
            Email = "test@example.com",
            Phone = "+79001234567",
            Taxation = "osn"
        };
        return request;
    }

    public static PaymentCancelRequestDto CreateValidCancelRequestWithItems(string? paymentId = null, string? reason = null)
    {
        var request = CreateValidCancelRequestWithReceipt(paymentId, reason);
        request.Items = new List<CancelItemDto>
        {
            new CancelItemDto
            {
                ItemId = "item-001",
                Quantity = 1,
                Amount = 150000,
                Reason = "Test item cancellation",
                Tax = "vat20"
            }
        };
        return request;
    }
}