using Microsoft.Extensions.Logging;
using Moq;
using Payment.Gateway.Models;
using Payment.Gateway.Services;

namespace Payment.Gateway.Tests;

public class PaymentStateMachineTests
{
    private readonly Mock<IPaymentRepository> _mockPaymentRepository;
    private readonly Mock<ILogger<PaymentStateMachine>> _mockLogger;
    private readonly PaymentStateMachine _paymentStateMachine;

    public PaymentStateMachineTests()
    {
        _mockPaymentRepository = new Mock<IPaymentRepository>();
        _mockLogger = new Mock<ILogger<PaymentStateMachine>>();
        _paymentStateMachine = new PaymentStateMachine(_mockPaymentRepository.Object, _mockLogger.Object);
    }

    [Theory]
    [InlineData(PaymentStatus.INIT, PaymentStatus.NEW, true)]
    [InlineData(PaymentStatus.NEW, PaymentStatus.FORM_SHOWED, true)]
    [InlineData(PaymentStatus.NEW, PaymentStatus.CANCELLED, true)]
    [InlineData(PaymentStatus.NEW, PaymentStatus.DEADLINE_EXPIRED, true)]
    [InlineData(PaymentStatus.FORM_SHOWED, PaymentStatus.ONECHOOSEVISION, true)]
    [InlineData(PaymentStatus.AUTHORIZING, PaymentStatus.THREE_DS_CHECKING, true)]
    [InlineData(PaymentStatus.AUTHORIZING, PaymentStatus.AUTHORIZED, true)]
    [InlineData(PaymentStatus.AUTHORIZED, PaymentStatus.CONFIRMING, true)]
    [InlineData(PaymentStatus.AUTHORIZED, PaymentStatus.REVERSING, true)]
    [InlineData(PaymentStatus.CONFIRMED, PaymentStatus.REFUNDING, true)]
    [InlineData(PaymentStatus.INIT, PaymentStatus.AUTHORIZED, false)] // Invalid transition
    [InlineData(PaymentStatus.CANCELLED, PaymentStatus.NEW, false)] // Terminal state
    [InlineData(PaymentStatus.REJECTED, PaymentStatus.AUTHORIZED, false)] // Terminal state
    public void CanTransition_ShouldReturnCorrectResult(PaymentStatus from, PaymentStatus to, bool expected)
    {
        // Act
        var result = _paymentStateMachine.CanTransition(from, to);

        // Assert
        Assert.Equal(expected, result);
    }

    [Fact]
    public void GetValidNextStates_FromINIT_ShouldReturnCorrectStates()
    {
        // Act
        var validStates = _paymentStateMachine.GetValidNextStates(PaymentStatus.INIT);

        // Assert
        Assert.Single(validStates);
        Assert.Contains(PaymentStatus.NEW, validStates);
    }

    [Fact]
    public void GetValidNextStates_FromNEW_ShouldReturnCorrectStates()
    {
        // Act
        var validStates = _paymentStateMachine.GetValidNextStates(PaymentStatus.NEW);

        // Assert
        Assert.Equal(3, validStates.Length);
        Assert.Contains(PaymentStatus.CANCELLED, validStates);
        Assert.Contains(PaymentStatus.DEADLINE_EXPIRED, validStates);
        Assert.Contains(PaymentStatus.FORM_SHOWED, validStates);
    }

    [Fact]
    public void GetValidNextStates_FromAUTHORIZED_ShouldReturnCorrectStates()
    {
        // Act
        var validStates = _paymentStateMachine.GetValidNextStates(PaymentStatus.AUTHORIZED);

        // Assert
        Assert.Equal(2, validStates.Length);
        Assert.Contains(PaymentStatus.CONFIRMING, validStates);
        Assert.Contains(PaymentStatus.REVERSING, validStates);
    }

    [Fact]
    public void GetValidNextStates_FromTerminalState_ShouldReturnEmptyArray()
    {
        // Act
        var validStates = _paymentStateMachine.GetValidNextStates(PaymentStatus.CANCELLED);

        // Assert
        Assert.Empty(validStates);
    }

    [Fact]
    public async Task TransitionAsync_WithValidTransition_ShouldReturnTrue()
    {
        // Arrange
        var paymentId = "TEST_PAYMENT_1";
        var payment = new PaymentEntity
        {
            PaymentId = paymentId,
            CurrentStatus = PaymentStatus.INIT,
            OrderId = "ORDER_1",
            TerminalKey = "TERMINAL_1",
            Amount = 10000,
            AttemptCount = 0
        };

        _mockPaymentRepository.Setup(x => x.GetByIdAsync(paymentId))
            .ReturnsAsync(payment);
        _mockPaymentRepository.Setup(x => x.UpdateAsync(It.IsAny<PaymentEntity>()))
            .ReturnsAsync((PaymentEntity p) => p);
        _mockPaymentRepository.Setup(x => x.AddStatusHistoryAsync(It.IsAny<string>(), It.IsAny<PaymentStatus>(), It.IsAny<string>(), It.IsAny<string>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _paymentStateMachine.TransitionAsync(paymentId, PaymentStatus.NEW);

        // Assert
        Assert.True(result);
        Assert.Equal(PaymentStatus.NEW, payment.CurrentStatus);
        _mockPaymentRepository.Verify(x => x.UpdateAsync(It.IsAny<PaymentEntity>()), Times.Once);
        _mockPaymentRepository.Verify(x => x.AddStatusHistoryAsync(paymentId, PaymentStatus.NEW, null, null), Times.Once);
    }

    [Fact]
    public async Task TransitionAsync_WithInvalidTransition_ShouldReturnFalse()
    {
        // Arrange
        var paymentId = "TEST_PAYMENT_1";
        var payment = new PaymentEntity
        {
            PaymentId = paymentId,
            CurrentStatus = PaymentStatus.INIT,
            OrderId = "ORDER_1",
            TerminalKey = "TERMINAL_1",
            Amount = 10000
        };

        _mockPaymentRepository.Setup(x => x.GetByIdAsync(paymentId))
            .ReturnsAsync(payment);

        // Act
        var result = await _paymentStateMachine.TransitionAsync(paymentId, PaymentStatus.AUTHORIZED); // Invalid from INIT

        // Assert
        Assert.False(result);
        _mockPaymentRepository.Verify(x => x.UpdateAsync(It.IsAny<PaymentEntity>()), Times.Never);
        _mockPaymentRepository.Verify(x => x.AddStatusHistoryAsync(It.IsAny<string>(), It.IsAny<PaymentStatus>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task TransitionAsync_WithNonExistentPayment_ShouldReturnFalse()
    {
        // Arrange
        var paymentId = "NON_EXISTENT_PAYMENT";

        _mockPaymentRepository.Setup(x => x.GetByIdAsync(paymentId))
            .ReturnsAsync((PaymentEntity?)null);

        // Act
        var result = await _paymentStateMachine.TransitionAsync(paymentId, PaymentStatus.NEW);

        // Assert
        Assert.False(result);
        _mockPaymentRepository.Verify(x => x.UpdateAsync(It.IsAny<PaymentEntity>()), Times.Never);
    }

    [Fact]
    public async Task TransitionAsync_ToAUTHORIZING_ShouldIncrementAttemptCount()
    {
        // Arrange
        var paymentId = "TEST_PAYMENT_1";
        var payment = new PaymentEntity
        {
            PaymentId = paymentId,
            CurrentStatus = PaymentStatus.FINISHAUTHORIZE,
            OrderId = "ORDER_1",
            TerminalKey = "TERMINAL_1",
            Amount = 10000,
            AttemptCount = 0
        };

        _mockPaymentRepository.Setup(x => x.GetByIdAsync(paymentId))
            .ReturnsAsync(payment);
        _mockPaymentRepository.Setup(x => x.UpdateAsync(It.IsAny<PaymentEntity>()))
            .ReturnsAsync((PaymentEntity p) => p);
        _mockPaymentRepository.Setup(x => x.AddStatusHistoryAsync(It.IsAny<string>(), It.IsAny<PaymentStatus>(), It.IsAny<string>(), It.IsAny<string>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _paymentStateMachine.TransitionAsync(paymentId, PaymentStatus.AUTHORIZING);

        // Assert
        Assert.True(result);
        Assert.Equal(1, payment.AttemptCount);
        Assert.Equal(PaymentStatus.AUTHORIZING, payment.CurrentStatus);
    }

    [Fact]
    public async Task TransitionAsync_ToNEW_ShouldSetExpirationDate()
    {
        // Arrange
        var paymentId = "TEST_PAYMENT_1";
        var payment = new PaymentEntity
        {
            PaymentId = paymentId,
            CurrentStatus = PaymentStatus.INIT,
            OrderId = "ORDER_1",
            TerminalKey = "TERMINAL_1",
            Amount = 10000,
            ExpirationDate = null
        };

        _mockPaymentRepository.Setup(x => x.GetByIdAsync(paymentId))
            .ReturnsAsync(payment);
        _mockPaymentRepository.Setup(x => x.UpdateAsync(It.IsAny<PaymentEntity>()))
            .ReturnsAsync((PaymentEntity p) => p);
        _mockPaymentRepository.Setup(x => x.AddStatusHistoryAsync(It.IsAny<string>(), It.IsAny<PaymentStatus>(), It.IsAny<string>(), It.IsAny<string>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _paymentStateMachine.TransitionAsync(paymentId, PaymentStatus.NEW);

        // Assert
        Assert.True(result);
        Assert.NotNull(payment.ExpirationDate);
        Assert.True(payment.ExpirationDate > DateTime.UtcNow.AddMinutes(25)); // Should be about 30 minutes from now
    }

    [Fact]
    public async Task TransitionAsync_WithErrorCodeAndMessage_ShouldSetProperties()
    {
        // Arrange
        var paymentId = "TEST_PAYMENT_1";
        var errorCode = "AUTH001";
        var message = "Authorization failed";
        var payment = new PaymentEntity
        {
            PaymentId = paymentId,
            CurrentStatus = PaymentStatus.AUTHORIZING,
            OrderId = "ORDER_1",
            TerminalKey = "TERMINAL_1",
            Amount = 10000
        };

        _mockPaymentRepository.Setup(x => x.GetByIdAsync(paymentId))
            .ReturnsAsync(payment);
        _mockPaymentRepository.Setup(x => x.UpdateAsync(It.IsAny<PaymentEntity>()))
            .ReturnsAsync((PaymentEntity p) => p);
        _mockPaymentRepository.Setup(x => x.AddStatusHistoryAsync(It.IsAny<string>(), It.IsAny<PaymentStatus>(), It.IsAny<string>(), It.IsAny<string>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _paymentStateMachine.TransitionAsync(paymentId, PaymentStatus.AUTH_FAIL, errorCode, message);

        // Assert
        Assert.True(result);
        Assert.Equal(PaymentStatus.AUTH_FAIL, payment.CurrentStatus);
        Assert.Equal(errorCode, payment.ErrorCode);
        Assert.Equal(message, payment.Message);
        _mockPaymentRepository.Verify(x => x.AddStatusHistoryAsync(paymentId, PaymentStatus.AUTH_FAIL, errorCode, message), Times.Once);
    }

    [Theory]
    [InlineData(PaymentStatus.THREE_DS_CHECKING)]
    [InlineData(PaymentStatus.SUBMITPASSIVIZATION)]
    [InlineData(PaymentStatus.SUBMITPASSIVIZATION2)]
    [InlineData(PaymentStatus.THREE_DS_CHECKED)]
    public void CanTransition_3DSStates_ShouldHaveValidTransitions(PaymentStatus status)
    {
        // Act & Assert - These should all have valid next states
        var validStates = _paymentStateMachine.GetValidNextStates(status);
        Assert.NotEmpty(validStates);
    }

    [Theory]
    [InlineData(PaymentStatus.CANCELLED)]
    [InlineData(PaymentStatus.DEADLINE_EXPIRED)]
    [InlineData(PaymentStatus.REJECTED)]
    [InlineData(PaymentStatus.REVERSED)]
    [InlineData(PaymentStatus.PARTIAL_REVERSED)]
    [InlineData(PaymentStatus.REFUNDED)]
    [InlineData(PaymentStatus.PARTIAL_REFUNDED)]
    public void GetValidNextStates_TerminalStates_ShouldReturnEmpty(PaymentStatus terminalStatus)
    {
        // Act
        var validStates = _paymentStateMachine.GetValidNextStates(terminalStatus);

        // Assert
        Assert.Empty(validStates);
    }

    [Fact]
    public async Task TransitionAsync_RepositoryException_ShouldReturnFalse()
    {
        // Arrange
        var paymentId = "TEST_PAYMENT_1";
        var payment = new PaymentEntity
        {
            PaymentId = paymentId,
            CurrentStatus = PaymentStatus.INIT,
            OrderId = "ORDER_1",
            TerminalKey = "TERMINAL_1",
            Amount = 10000
        };

        _mockPaymentRepository.Setup(x => x.GetByIdAsync(paymentId))
            .ReturnsAsync(payment);
        _mockPaymentRepository.Setup(x => x.UpdateAsync(It.IsAny<PaymentEntity>()))
            .ThrowsAsync(new Exception("Database error"));

        // Act
        var result = await _paymentStateMachine.TransitionAsync(paymentId, PaymentStatus.NEW);

        // Assert
        Assert.False(result);
    }
}