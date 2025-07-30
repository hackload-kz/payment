// SPDX-License-Identifier: MIT
// Copyright (c) 2025 HackLoad Payment Gateway

using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using PaymentGateway.Core.DTOs;
using PaymentGateway.Core.Entities;
using PaymentGateway.Core.Enums;
using PaymentGateway.Core.Interfaces;
using PaymentGateway.Core.Repositories;
using PaymentGateway.Core.Services;
using PaymentGateway.Tests.TestHelpers;

namespace PaymentGateway.Tests.UnitTests;

/// <summary>
/// Unit tests for PaymentInitializationService
/// </summary>
public class PaymentInitializationServiceTests : BaseTest
{
    private readonly Mock<IPaymentRepository> _mockPaymentRepository;
    private readonly Mock<ITeamRepository> _mockTeamRepository;
    private readonly Mock<BusinessRuleEngineService> _mockBusinessRuleEngine;
    private readonly PaymentInitializationService _paymentInitializationService;

    public PaymentInitializationServiceTests()
    {
        _mockPaymentRepository = AddMockRepository<IPaymentRepository>();
        _mockTeamRepository = AddMockRepository<ITeamRepository>();
        _mockBusinessRuleEngine = AddMockService<BusinessRuleEngineService>();

        _paymentInitializationService = new PaymentInitializationService(
            GetService<ILogger<PaymentInitializationService>>(),
            MockConfiguration.Object,
            GetService<IMemoryCache>(),
            _mockPaymentRepository.Object,
            _mockTeamRepository.Object,
            _mockBusinessRuleEngine.Object
        );
    }

    [Fact]
    public async Task InitializePaymentAsync_WithValidRequest_ShouldReturnSuccess()
    {
        // Arrange
        var team = TestDataBuilder.CreateTeam("test_team", "Test Team");
        var request = TestDataBuilder.CreatePaymentInitRequest(
            teamSlug: team.TeamSlug,
            amount: 1000m,
            orderId: "ORDER_123");

        _mockTeamRepository
            .Setup(r => r.GetByTeamSlugAsync(team.TeamSlug))
            .ReturnsAsync(team);

        _mockPaymentRepository
            .Setup(r => r.GetByOrderIdAsync(request.OrderId, team.Id))
            .ReturnsAsync((Payment?)null); // No duplicate order

        _mockBusinessRuleEngine
            .Setup(e => e.EvaluateRulesAsync(
                It.IsAny<BusinessRuleType>(),
                It.IsAny<PaymentRuleContext>()))
            .ReturnsAsync(new BusinessRuleValidationResult { IsValid = true });

        _mockPaymentRepository
            .Setup(r => r.CreateAsync(It.IsAny<Payment>()))
            .ReturnsAsync((Payment p) => p);

        // Act
        var result = await _paymentInitializationService.InitializePaymentAsync(request);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeTrue();
        result.PaymentId.Should().NotBeNullOrEmpty();
        result.PaymentUrl.Should().NotBeNullOrEmpty();
        result.Status.Should().Be(PaymentStatus.NEW);
        result.ErrorMessage.Should().BeNull();

        _mockPaymentRepository.Verify(
            r => r.CreateAsync(It.Is<Payment>(p =>
                p.TeamId == team.Id &&
                p.Amount == request.Amount &&
                p.OrderId == request.OrderId &&
                p.Currency == request.Currency &&
                p.Status == PaymentStatus.NEW)),
            Times.Once);
    }

    [Fact]
    public async Task InitializePaymentAsync_WithNonexistentTeam_ShouldReturnError()
    {
        // Arrange
        var request = TestDataBuilder.CreatePaymentInitRequest(teamSlug: "nonexistent_team");

        _mockTeamRepository
            .Setup(r => r.GetByTeamSlugAsync(request.TeamSlug))
            .ReturnsAsync((Team?)null);

        // Act
        var result = await _paymentInitializationService.InitializePaymentAsync(request);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Team not found");
        result.PaymentId.Should().BeNull();
        result.PaymentUrl.Should().BeNull();

        _mockPaymentRepository.Verify(
            r => r.CreateAsync(It.IsAny<Payment>()),
            Times.Never);
    }

    [Fact]
    public async Task InitializePaymentAsync_WithInactiveTeam_ShouldReturnError()
    {
        // Arrange
        var team = TestDataBuilder.CreateTeam("test_team", "Test Team", isActive: false);
        var request = TestDataBuilder.CreatePaymentInitRequest(teamSlug: team.TeamSlug);

        _mockTeamRepository
            .Setup(r => r.GetByTeamSlugAsync(team.TeamSlug))
            .ReturnsAsync(team);

        // Act
        var result = await _paymentInitializationService.InitializePaymentAsync(request);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Team is not active");

        _mockPaymentRepository.Verify(
            r => r.CreateAsync(It.IsAny<Payment>()),
            Times.Never);
    }

    [Fact]
    public async Task InitializePaymentAsync_WithDuplicateOrderId_ShouldReturnError()
    {
        // Arrange
        var team = TestDataBuilder.CreateTeam("test_team", "Test Team");
        var request = TestDataBuilder.CreatePaymentInitRequest(
            teamSlug: team.TeamSlug,
            orderId: "ORDER_123");

        var existingPayment = TestDataBuilder.CreatePayment(
            orderId: request.OrderId,
            teamId: team.Id);

        _mockTeamRepository
            .Setup(r => r.GetByTeamSlugAsync(team.TeamSlug))
            .ReturnsAsync(team);

        _mockPaymentRepository
            .Setup(r => r.GetByOrderIdAsync(request.OrderId, team.Id))
            .ReturnsAsync(existingPayment);

        // Act
        var result = await _paymentInitializationService.InitializePaymentAsync(request);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("OrderId already exists");

        _mockPaymentRepository.Verify(
            r => r.CreateAsync(It.IsAny<Payment>()),
            Times.Never);
    }

    [Fact]
    public async Task InitializePaymentAsync_WithBusinessRuleViolation_ShouldReturnError()
    {
        // Arrange
        var team = TestDataBuilder.CreateTeam("test_team", "Test Team");
        var request = TestDataBuilder.CreatePaymentInitRequest(
            teamSlug: team.TeamSlug,
            amount: 2000000m); // Amount exceeding limits

        _mockTeamRepository
            .Setup(r => r.GetByTeamSlugAsync(team.TeamSlug))
            .ReturnsAsync(team);

        _mockPaymentRepository
            .Setup(r => r.GetByOrderIdAsync(request.OrderId, team.Id))
            .ReturnsAsync((Payment?)null);

        _mockBusinessRuleEngine
            .Setup(e => e.EvaluateRulesAsync(
                It.IsAny<BusinessRuleType>(),
                It.IsAny<PaymentRuleContext>()))
            .ReturnsAsync(new BusinessRuleValidationResult
            {
                IsValid = false,
                Violations = new List<BusinessRuleViolation>
                {
                    new() { Message = "Amount exceeds daily limit", Severity = ViolationSeverity.Error }
                }
            });

        // Act
        var result = await _paymentInitializationService.InitializePaymentAsync(request);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Amount exceeds daily limit");

        _mockPaymentRepository.Verify(
            r => r.CreateAsync(It.IsAny<Payment>()),
            Times.Never);
    }

    [Theory]
    [InlineData(5.0)] // Below minimum
    [InlineData(1500000.0)] // Above maximum
    public async Task InitializePaymentAsync_WithInvalidAmount_ShouldReturnError(decimal amount)
    {
        // Arrange
        var team = TestDataBuilder.CreateTeam("test_team", "Test Team");
        var request = TestDataBuilder.CreatePaymentInitRequest(
            teamSlug: team.TeamSlug,
            amount: amount);

        _mockTeamRepository
            .Setup(r => r.GetByTeamSlugAsync(team.TeamSlug))
            .ReturnsAsync(team);

        // Act
        var result = await _paymentInitializationService.InitializePaymentAsync(request);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("amount");

        _mockPaymentRepository.Verify(
            r => r.CreateAsync(It.IsAny<Payment>()),
            Times.Never);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("   ")]
    public async Task InitializePaymentAsync_WithInvalidOrderId_ShouldReturnError(string? orderId)
    {
        // Arrange
        var request = TestDataBuilder.CreatePaymentInitRequest(orderId: orderId!);

        // Act
        var result = await _paymentInitializationService.InitializePaymentAsync(request);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("OrderId");

        _mockPaymentRepository.Verify(
            r => r.CreateAsync(It.IsAny<Payment>()),
            Times.Never);
    }

    [Theory]
    [InlineData("CHF")] // Unsupported currency
    [InlineData("JPY")] // Unsupported currency
    [InlineData("")] // Empty currency
    [InlineData(null)] // Null currency
    public async Task InitializePaymentAsync_WithInvalidCurrency_ShouldReturnError(string? currency)
    {
        // Arrange
        var request = TestDataBuilder.CreatePaymentInitRequest(currency: currency!);

        // Act
        var result = await _paymentInitializationService.InitializePaymentAsync(request);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("currency");

        _mockPaymentRepository.Verify(
            r => r.CreateAsync(It.IsAny<Payment>()),
            Times.Never);
    }

    [Fact]
    public async Task GeneratePaymentUrl_ShouldReturnValidUrl()
    {
        // Arrange
        var paymentId = "PAY_12345";

        // Act
        var paymentUrl = await _paymentInitializationService.GeneratePaymentUrlAsync(paymentId);

        // Assert
        paymentUrl.Should().NotBeNullOrEmpty();
        paymentUrl.Should().Contain(paymentId);
        paymentUrl.Should().StartWith("http");
    }

    [Fact]
    public async Task ValidatePaymentRequest_WithValidRequest_ShouldReturnSuccess()
    {
        // Arrange
        var request = TestDataBuilder.CreatePaymentInitRequest();

        // Act
        var result = await _paymentInitializationService.ValidatePaymentRequestAsync(request);

        // Assert
        result.Should().NotBeNull();
        result.IsValid.Should().BeTrue();
        result.ValidationErrors.Should().BeEmpty();
    }

    [Fact]
    public async Task ValidatePaymentRequest_WithInvalidRequest_ShouldReturnErrors()
    {
        // Arrange
        var request = new PaymentInitRequestDto
        {
            TeamSlug = "", // Invalid
            Amount = -100, // Invalid
            OrderId = "", // Invalid
            Currency = "INVALID", // Invalid
            Description = null // Invalid
        };

        // Act
        var result = await _paymentInitializationService.ValidatePaymentRequestAsync(request);

        // Assert
        result.Should().NotBeNull();
        result.IsValid.Should().BeFalse();
        result.ValidationErrors.Should().NotBeEmpty();
        result.ValidationErrors.Should().HaveCountGreaterOrEqualTo(4);
    }

    [Fact]
    public async Task InitializePaymentAsync_ConcurrentRequests_ShouldHandleCorrectly()
    {
        // Arrange
        var team = TestDataBuilder.CreateTeam("test_team", "Test Team");
        
        _mockTeamRepository
            .Setup(r => r.GetByTeamSlugAsync(team.TeamSlug))
            .ReturnsAsync(team);

        _mockPaymentRepository
            .Setup(r => r.GetByOrderIdAsync(It.IsAny<string>(), team.Id))
            .ReturnsAsync((Payment?)null);

        _mockBusinessRuleEngine
            .Setup(e => e.EvaluateRulesAsync(
                It.IsAny<BusinessRuleType>(),
                It.IsAny<PaymentRuleContext>()))
            .ReturnsAsync(new BusinessRuleValidationResult { IsValid = true });

        _mockPaymentRepository
            .Setup(r => r.CreateAsync(It.IsAny<Payment>()))
            .ReturnsAsync((Payment p) => p);

        // Act
        var tasks = Enumerable.Range(0, 10).Select(async i =>
        {
            var request = TestDataBuilder.CreatePaymentInitRequest(
                teamSlug: team.TeamSlug,
                orderId: $"ORDER_{i}");
            return await _paymentInitializationService.InitializePaymentAsync(request);
        });

        var results = await Task.WhenAll(tasks);

        // Assert
        results.Should().OnlyContain(r => r.Success == true);
        results.Select(r => r.PaymentId).Should().OnlyHaveUniqueItems();

        _mockPaymentRepository.Verify(
            r => r.CreateAsync(It.IsAny<Payment>()),
            Times.Exactly(10));
    }

    [Fact]
    public async Task GetPaymentStatisticsAsync_ShouldReturnStatistics()
    {
        // Arrange
        var teamSlug = "test_team";

        // Act
        var stats = await _paymentInitializationService.GetPaymentStatisticsAsync(teamSlug);

        // Assert
        stats.Should().NotBeNull();
        stats.TeamSlug.Should().Be(teamSlug);
        stats.TotalInitializations.Should().BeGreaterOrEqualTo(0);
        stats.SuccessfulInitializations.Should().BeGreaterOrEqualTo(0);
        stats.FailedInitializations.Should().BeGreaterOrEqualTo(0);
    }

    [Fact]
    public async Task InitializePaymentAsync_WithRepositoryException_ShouldReturnError()
    {
        // Arrange
        var team = TestDataBuilder.CreateTeam("test_team", "Test Team");
        var request = TestDataBuilder.CreatePaymentInitRequest(teamSlug: team.TeamSlug);

        _mockTeamRepository
            .Setup(r => r.GetByTeamSlugAsync(team.TeamSlug))
            .ReturnsAsync(team);

        _mockPaymentRepository
            .Setup(r => r.GetByOrderIdAsync(request.OrderId, team.Id))
            .ReturnsAsync((Payment?)null);

        _mockBusinessRuleEngine
            .Setup(e => e.EvaluateRulesAsync(
                It.IsAny<BusinessRuleType>(),
                It.IsAny<PaymentRuleContext>()))
            .ReturnsAsync(new BusinessRuleValidationResult { IsValid = true });

        _mockPaymentRepository
            .Setup(r => r.CreateAsync(It.IsAny<Payment>()))
            .ThrowsAsync(new InvalidOperationException("Database error"));

        // Act
        var result = await _paymentInitializationService.InitializePaymentAsync(request);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("error");
    }

    [Fact]
    public async Task InitializePaymentAsync_WithValidReceiptItems_ShouldSucceed()
    {
        // Arrange
        var team = TestDataBuilder.CreateTeam("test_team", "Test Team");
        var request = TestDataBuilder.CreatePaymentInitRequest(teamSlug: team.TeamSlug);
        request.Receipt = new ReceiptDto
        {
            Items = new List<OrderItemDto>
            {
                new() { Name = "Test Item 1", Quantity = 1, Amount = 500m },
                new() { Name = "Test Item 2", Quantity = 2, Amount = 500m }
            }
        };

        _mockTeamRepository
            .Setup(r => r.GetByTeamSlugAsync(team.TeamSlug))
            .ReturnsAsync(team);

        _mockPaymentRepository
            .Setup(r => r.GetByOrderIdAsync(request.OrderId, team.Id))
            .ReturnsAsync((Payment?)null);

        _mockBusinessRuleEngine
            .Setup(e => e.EvaluateRulesAsync(
                It.IsAny<BusinessRuleType>(),
                It.IsAny<PaymentRuleContext>()))
            .ReturnsAsync(new BusinessRuleValidationResult { IsValid = true });

        _mockPaymentRepository
            .Setup(r => r.CreateAsync(It.IsAny<Payment>()))
            .ReturnsAsync((Payment p) => p);

        // Act
        var result = await _paymentInitializationService.InitializePaymentAsync(request);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeTrue();
        result.PaymentId.Should().NotBeNullOrEmpty();
    }
}