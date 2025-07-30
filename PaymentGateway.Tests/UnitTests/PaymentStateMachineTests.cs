// SPDX-License-Identifier: MIT
// Copyright (c) 2025 HackLoad Payment Gateway

using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using PaymentGateway.Core.Entities;
using PaymentGateway.Core.Enums;
using PaymentGateway.Core.Interfaces;
using PaymentGateway.Core.Repositories;
using PaymentGateway.Core.Services;
using PaymentGateway.Tests.TestHelpers;

namespace PaymentGateway.Tests.UnitTests;

/// <summary>
/// Unit tests for PaymentStateMachine
/// </summary>
public class PaymentStateMachineTests : BaseTest
{
    private readonly Mock<IPaymentStateTransitionRepository> _mockTransitionRepository;
    private readonly PaymentStateMachine _stateMachine;

    public PaymentStateMachineTests()
    {
        _mockTransitionRepository = AddMockRepository<IPaymentStateTransitionRepository>();
        
        _stateMachine = new PaymentStateMachine(
            GetService<ILogger<PaymentStateMachine>>(),
            _mockTransitionRepository.Object
        );
    }

    [Theory]
    [InlineData(PaymentStatus.NEW, PaymentStatus.AUTHORIZED, true)]
    [InlineData(PaymentStatus.AUTHORIZED, PaymentStatus.CONFIRMED, true)]
    [InlineData(PaymentStatus.NEW, PaymentStatus.CONFIRMED, false)]
    [InlineData(PaymentStatus.CONFIRMED, PaymentStatus.AUTHORIZED, false)]
    [InlineData(PaymentStatus.NEW, PaymentStatus.CANCELLED, true)]
    [InlineData(PaymentStatus.AUTHORIZED, PaymentStatus.CANCELLED, true)]
    [InlineData(PaymentStatus.CONFIRMED, PaymentStatus.REFUNDED, true)]
    [InlineData(PaymentStatus.CANCELLED, PaymentStatus.NEW, false)]
    [InlineData(PaymentStatus.REFUNDED, PaymentStatus.CONFIRMED, false)]
    public async Task IsValidTransition_ShouldReturnCorrectResult(
        PaymentStatus fromStatus, 
        PaymentStatus toStatus, 
        bool expectedResult)
    {
        // Act
        var result = await _stateMachine.IsValidTransitionAsync(fromStatus, toStatus);

        // Assert
        result.Should().Be(expectedResult);
    }

    [Fact]
    public async Task GetValidTransitions_ForNewStatus_ShouldReturnCorrectTransitions()
    {
        // Act
        var validTransitions = await _stateMachine.GetValidTransitionsAsync(PaymentStatus.NEW);

        // Assert
        validTransitions.Should().Contain(new[]
        {
            PaymentStatus.AUTHORIZED,
            PaymentStatus.CANCELLED,
            PaymentStatus.FAILED
        });
        validTransitions.Should().NotContain(new[]
        {
            PaymentStatus.CONFIRMED,
            PaymentStatus.REFUNDED
        });
    }

    [Fact]
    public async Task GetValidTransitions_ForAuthorizedStatus_ShouldReturnCorrectTransitions()
    {
        // Act
        var validTransitions = await _stateMachine.GetValidTransitionsAsync(PaymentStatus.AUTHORIZED);

        // Assert
        validTransitions.Should().Contain(new[]
        {
            PaymentStatus.CONFIRMED,
            PaymentStatus.CANCELLED,
            PaymentStatus.FAILED
        });
        validTransitions.Should().NotContain(new[]
        {
            PaymentStatus.NEW,
            PaymentStatus.REFUNDED
        });
    }

    [Fact]
    public async Task GetValidTransitions_ForConfirmedStatus_ShouldReturnCorrectTransitions()
    {
        // Act
        var validTransitions = await _stateMachine.GetValidTransitionsAsync(PaymentStatus.CONFIRMED);

        // Assert
        validTransitions.Should().Contain(PaymentStatus.REFUNDED);
        validTransitions.Should().NotContain(new[]
        {
            PaymentStatus.NEW,
            PaymentStatus.AUTHORIZED,
            PaymentStatus.CANCELLED,
            PaymentStatus.FAILED
        });
    }

    [Fact]
    public async Task GetValidTransitions_ForFinalStatus_ShouldReturnEmptyList()
    {
        // Arrange
        var finalStatuses = new[] { PaymentStatus.CANCELLED, PaymentStatus.REFUNDED, PaymentStatus.FAILED };

        foreach (var status in finalStatuses)
        {
            // Act
            var validTransitions = await _stateMachine.GetValidTransitionsAsync(status);

            // Assert
            validTransitions.Should().BeEmpty($"Final status {status} should not have valid transitions");
        }
    }

    [Fact]
    public async Task ExecuteTransition_WithValidTransition_ShouldSucceed()
    {
        // Arrange
        var payment = TestDataBuilder.CreatePayment(PaymentStatus.NEW);
        var fromStatus = PaymentStatus.NEW;
        var toStatus = PaymentStatus.AUTHORIZED;
        var reason = "Payment authorized successfully";

        _mockTransitionRepository
            .Setup(r => r.CreateTransitionAsync(It.IsAny<PaymentStateTransition>()))
            .ReturnsAsync(true);

        // Act
        var result = await _stateMachine.ExecuteTransitionAsync(
            payment.Id, fromStatus, toStatus, reason);

        // Assert
        result.Should().BeTrue();
        
        _mockTransitionRepository.Verify(
            r => r.CreateTransitionAsync(It.Is<PaymentStateTransition>(t =>
                t.PaymentId == payment.Id &&
                t.FromStatus == fromStatus &&
                t.ToStatus == toStatus &&
                t.Reason == reason)),
            Times.Once);
    }

    [Fact]
    public async Task ExecuteTransition_WithInvalidTransition_ShouldFail()
    {
        // Arrange
        var payment = TestDataBuilder.CreatePayment(PaymentStatus.NEW);
        var fromStatus = PaymentStatus.NEW;
        var toStatus = PaymentStatus.CONFIRMED; // Invalid transition
        var reason = "Invalid transition attempt";

        // Act
        var result = await _stateMachine.ExecuteTransitionAsync(
            payment.Id, fromStatus, toStatus, reason);

        // Assert
        result.Should().BeFalse();
        
        _mockTransitionRepository.Verify(
            r => r.CreateTransitionAsync(It.IsAny<PaymentStateTransition>()),
            Times.Never);
    }

    [Fact]
    public async Task GetTransitionHistory_ShouldReturnTransitionsFromRepository()
    {
        // Arrange
        var paymentId = Guid.NewGuid();
        var expectedTransitions = new List<PaymentStateTransition>
        {
            new()
            {
                Id = Guid.NewGuid(),
                PaymentId = paymentId,
                FromStatus = PaymentStatus.NEW,
                ToStatus = PaymentStatus.AUTHORIZED,
                Reason = "Authorized",
                TransitionedAt = DateTime.UtcNow.AddMinutes(-10)
            },
            new()
            {
                Id = Guid.NewGuid(),
                PaymentId = paymentId,
                FromStatus = PaymentStatus.AUTHORIZED,
                ToStatus = PaymentStatus.CONFIRMED,
                Reason = "Confirmed",
                TransitionedAt = DateTime.UtcNow.AddMinutes(-5)
            }
        };

        _mockTransitionRepository
            .Setup(r => r.GetTransitionHistoryAsync(paymentId))
            .ReturnsAsync(expectedTransitions);

        // Act
        var result = await _stateMachine.GetTransitionHistoryAsync(paymentId);

        // Assert
        result.Should().BeEquivalentTo(expectedTransitions);
        
        _mockTransitionRepository.Verify(
            r => r.GetTransitionHistoryAsync(paymentId),
            Times.Once);
    }

    [Fact]
    public async Task ValidateBusinessRules_ForPaymentAmount_ShouldReturnCorrectResult()
    {
        // Arrange
        var payment = TestDataBuilder.CreatePayment(amount: 50000m); // Valid amount
        var toStatus = PaymentStatus.AUTHORIZED;

        // Act
        var result = await _stateMachine.ValidateBusinessRulesAsync(payment, toStatus);

        // Assert
        result.IsValid.Should().BeTrue();
        result.Violations.Should().BeEmpty();
    }

    [Fact]
    public async Task ValidateBusinessRules_ForInvalidAmount_ShouldReturnViolations()
    {
        // Arrange
        var payment = TestDataBuilder.CreatePayment(amount: 5m); // Below minimum
        var toStatus = PaymentStatus.AUTHORIZED;

        // Act
        var result = await _stateMachine.ValidateBusinessRulesAsync(payment, toStatus);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Violations.Should().NotBeEmpty();
        result.Violations.Should().Contain(v => v.Contains("amount"));
    }

    [Fact]
    public async Task ValidateBusinessRules_ForExpiredPayment_ShouldReturnViolations()
    {
        // Arrange
        var payment = TestDataBuilder.CreatePayment();
        payment.ExpiresAt = DateTime.UtcNow.AddMinutes(-10); // Expired
        var toStatus = PaymentStatus.AUTHORIZED;

        // Act
        var result = await _stateMachine.ValidateBusinessRulesAsync(payment, toStatus);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Violations.Should().NotBeEmpty();
        result.Violations.Should().Contain(v => v.Contains("expired"));
    }

    [Theory]
    [InlineData(PaymentStatus.NEW, true)]
    [InlineData(PaymentStatus.AUTHORIZED, true)]
    [InlineData(PaymentStatus.CONFIRMED, false)]
    [InlineData(PaymentStatus.CANCELLED, false)]
    [InlineData(PaymentStatus.REFUNDED, false)]
    [InlineData(PaymentStatus.FAILED, false)]
    public async Task CanTransitionFromStatus_ShouldReturnCorrectResult(
        PaymentStatus status, 
        bool expectedCanTransition)
    {
        // Act
        var result = await _stateMachine.CanTransitionFromStatusAsync(status);

        // Assert
        result.Should().Be(expectedCanTransition);
    }

    [Fact]
    public async Task GetTransitionMatrix_ShouldReturnCompleteMatrix()
    {
        // Act
        var matrix = await _stateMachine.GetTransitionMatrixAsync();

        // Assert
        matrix.Should().NotBeEmpty();
        matrix.Should().ContainKey(PaymentStatus.NEW);
        matrix.Should().ContainKey(PaymentStatus.AUTHORIZED);
        matrix.Should().ContainKey(PaymentStatus.CONFIRMED);
        
        matrix[PaymentStatus.NEW].Should().Contain(PaymentStatus.AUTHORIZED);
        matrix[PaymentStatus.AUTHORIZED].Should().Contain(PaymentStatus.CONFIRMED);
        matrix[PaymentStatus.CONFIRMED].Should().Contain(PaymentStatus.REFUNDED);
    }

    [Fact]
    public async Task ConcurrentTransitionAttempts_ShouldHandleCorrectly()
    {
        // Arrange
        var payment = TestDataBuilder.CreatePayment(PaymentStatus.NEW);
        var fromStatus = PaymentStatus.NEW;
        var toStatus = PaymentStatus.AUTHORIZED;
        var reason = "Concurrent transition test";

        _mockTransitionRepository
            .Setup(r => r.CreateTransitionAsync(It.IsAny<PaymentStateTransition>()))
            .ReturnsAsync(true);

        // Act
        var tasks = Enumerable.Range(0, 5).Select(async i =>
            await _stateMachine.ExecuteTransitionAsync(
                payment.Id, fromStatus, toStatus, $"{reason} - {i}")
        );

        var results = await Task.WhenAll(tasks);

        // Assert
        results.Should().OnlyContain(r => r == true);
        
        // Should have attempted to create transitions
        _mockTransitionRepository.Verify(
            r => r.CreateTransitionAsync(It.IsAny<PaymentStateTransition>()),
            Times.Exactly(5));
    }

    [Fact]
    public async Task ExecuteTransition_WithRepositoryFailure_ShouldReturnFalse()
    {
        // Arrange
        var payment = TestDataBuilder.CreatePayment(PaymentStatus.NEW);
        var fromStatus = PaymentStatus.NEW;
        var toStatus = PaymentStatus.AUTHORIZED;
        var reason = "Repository failure test";

        _mockTransitionRepository
            .Setup(r => r.CreateTransitionAsync(It.IsAny<PaymentStateTransition>()))
            .ThrowsAsync(new InvalidOperationException("Database error"));

        // Act
        var result = await _stateMachine.ExecuteTransitionAsync(
            payment.Id, fromStatus, toStatus, reason);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task ValidateTransitionPath_WithValidPath_ShouldReturnTrue()
    {
        // Arrange
        var path = new[]
        {
            PaymentStatus.NEW,
            PaymentStatus.AUTHORIZED,
            PaymentStatus.CONFIRMED
        };

        // Act
        var result = await _stateMachine.ValidateTransitionPathAsync(path);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public async Task ValidateTransitionPath_WithInvalidPath_ShouldReturnFalse()
    {
        // Arrange
        var path = new[]
        {
            PaymentStatus.NEW,
            PaymentStatus.CONFIRMED, // Invalid: skipping AUTHORIZED
            PaymentStatus.REFUNDED
        };

        // Act
        var result = await _stateMachine.ValidateTransitionPathAsync(path);

        // Assert
        result.Should().BeFalse();
    }
}