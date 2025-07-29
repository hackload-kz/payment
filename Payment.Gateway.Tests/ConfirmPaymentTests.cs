using Microsoft.Extensions.Logging;
using Moq;
using Payment.Gateway.DTOs;
using Payment.Gateway.Models;
using Payment.Gateway.Services;
using Payment.Gateway.Validators;

namespace Payment.Gateway.Tests;

public class ConfirmPaymentTests
{
    private readonly Mock<IPaymentRepository> _mockPaymentRepository;
    private readonly Mock<IPaymentStateMachine> _mockStateMachine;
    private readonly Mock<ITokenGenerationService> _mockTokenService;
    private readonly Mock<IMerchantService> _mockMerchantService;
    private readonly Mock<ILogger<PaymentService>> _mockLogger;
    private readonly PaymentService _paymentService;
    private readonly ConfirmPaymentRequestValidator _validator;

    public ConfirmPaymentTests()
    {
        _mockPaymentRepository = new Mock<IPaymentRepository>();
        _mockStateMachine = new Mock<IPaymentStateMachine>();
        _mockTokenService = new Mock<ITokenGenerationService>();
        _mockMerchantService = new Mock<IMerchantService>();
        _mockLogger = new Mock<ILogger<PaymentService>>();
        
        _paymentService = new PaymentService(
            _mockPaymentRepository.Object,
            _mockStateMachine.Object,
            _mockTokenService.Object,
            _mockMerchantService.Object,
            _mockLogger.Object);

        _validator = new ConfirmPaymentRequestValidator();
    }

    [Fact]
    public async Task ConfirmPaymentAsync_WithValidRequest_ShouldReturnSuccess()
    {
        // Arrange
        var merchant = new Merchant
        {
            TerminalKey = "TEST_TERMINAL_1",
            Password = "test_password",
            IsActive = true
        };

        var payment = new PaymentEntity
        {
            PaymentId = "PAYMENT_123",
            OrderId = "ORDER_123",
            TerminalKey = "TEST_TERMINAL_1",
            Amount = 10000,
            CurrentStatus = PaymentStatus.AUTHORIZED
        };

        var request = new ConfirmPaymentRequest
        {
            TerminalKey = "TEST_TERMINAL_1",
            PaymentId = "PAYMENT_123",
            Token = "valid_token",
            Amount = 10000
        };

        _mockMerchantService.Setup(x => x.GetMerchantAsync("TEST_TERMINAL_1"))
            .ReturnsAsync(merchant);

        _mockTokenService.Setup(x => x.GenerateToken(It.IsAny<IDictionary<string, object>>(), "test_password"))
            .Returns("valid_token");

        _mockPaymentRepository.Setup(x => x.GetByIdAsync("PAYMENT_123"))
            .ReturnsAsync(payment);

        _mockStateMachine.Setup(x => x.TransitionAsync("PAYMENT_123", PaymentStatus.CONFIRMING, null, null))
            .ReturnsAsync(true);

        _mockStateMachine.Setup(x => x.TransitionAsync("PAYMENT_123", PaymentStatus.CONFIRMED, null, null))
            .ReturnsAsync(true);

        _mockPaymentRepository.Setup(x => x.UpdateAsync(It.IsAny<PaymentEntity>()))
            .ReturnsAsync((PaymentEntity p) => p);

        // Act
        var result = await _paymentService.ConfirmPaymentAsync(request);

        // Assert
        Assert.True(result.Success);
        Assert.Equal("CONFIRMED", result.Status);
        Assert.Equal("0", result.ErrorCode);
        Assert.Equal("TEST_TERMINAL_1", result.TerminalKey);
        Assert.Equal("ORDER_123", result.OrderId);
        Assert.Equal("PAYMENT_123", result.PaymentId);

        _mockStateMachine.Verify(x => x.TransitionAsync("PAYMENT_123", PaymentStatus.CONFIRMING, null, null), Times.Once);
        _mockStateMachine.Verify(x => x.TransitionAsync("PAYMENT_123", PaymentStatus.CONFIRMED, null, null), Times.Once);
        _mockPaymentRepository.Verify(x => x.UpdateAsync(It.IsAny<PaymentEntity>()), Times.Once);
    }

    [Fact]
    public async Task ConfirmPaymentAsync_WithPartialAmount_ShouldUpdateAmount()
    {
        // Arrange
        var merchant = new Merchant
        {
            TerminalKey = "TEST_TERMINAL_1",
            Password = "test_password",
            IsActive = true
        };

        var payment = new PaymentEntity
        {
            PaymentId = "PAYMENT_123",
            OrderId = "ORDER_123",
            TerminalKey = "TEST_TERMINAL_1",
            Amount = 10000,
            CurrentStatus = PaymentStatus.AUTHORIZED
        };

        var request = new ConfirmPaymentRequest
        {
            TerminalKey = "TEST_TERMINAL_1",
            PaymentId = "PAYMENT_123",
            Token = "valid_token",
            Amount = 7500 // Partial amount
        };

        _mockMerchantService.Setup(x => x.GetMerchantAsync("TEST_TERMINAL_1"))
            .ReturnsAsync(merchant);

        _mockTokenService.Setup(x => x.GenerateToken(It.IsAny<IDictionary<string, object>>(), "test_password"))
            .Returns("valid_token");

        _mockPaymentRepository.Setup(x => x.GetByIdAsync("PAYMENT_123"))
            .ReturnsAsync(payment);

        _mockStateMachine.Setup(x => x.TransitionAsync("PAYMENT_123", PaymentStatus.CONFIRMING, null, null))
            .ReturnsAsync(true);

        _mockStateMachine.Setup(x => x.TransitionAsync("PAYMENT_123", PaymentStatus.CONFIRMED, null, null))
            .ReturnsAsync(true);

        PaymentEntity? updatedPayment = null;
        _mockPaymentRepository.Setup(x => x.UpdateAsync(It.IsAny<PaymentEntity>()))
            .Callback<PaymentEntity>(p => updatedPayment = p)
            .ReturnsAsync((PaymentEntity p) => p);

        // Act
        var result = await _paymentService.ConfirmPaymentAsync(request);

        // Assert
        Assert.True(result.Success);
        Assert.Equal("CONFIRMED", result.Status);
        Assert.NotNull(updatedPayment);
        Assert.Equal(7500, updatedPayment.Amount); // Amount should be updated to partial amount
    }

    [Fact]
    public async Task ConfirmPaymentAsync_WithInvalidStatus_ShouldReturnError()
    {
        // Arrange
        var merchant = new Merchant
        {
            TerminalKey = "TEST_TERMINAL_1",
            Password = "test_password",
            IsActive = true
        };

        var payment = new PaymentEntity
        {
            PaymentId = "PAYMENT_123",
            OrderId = "ORDER_123",
            TerminalKey = "TEST_TERMINAL_1",
            Amount = 10000,
            CurrentStatus = PaymentStatus.CONFIRMED // Already confirmed
        };

        var request = new ConfirmPaymentRequest
        {
            TerminalKey = "TEST_TERMINAL_1",
            PaymentId = "PAYMENT_123",
            Token = "valid_token"
        };

        _mockMerchantService.Setup(x => x.GetMerchantAsync("TEST_TERMINAL_1"))
            .ReturnsAsync(merchant);

        _mockTokenService.Setup(x => x.GenerateToken(It.IsAny<IDictionary<string, object>>(), "test_password"))
            .Returns("valid_token");

        _mockPaymentRepository.Setup(x => x.GetByIdAsync("PAYMENT_123"))
            .ReturnsAsync(payment);

        // Act
        var result = await _paymentService.ConfirmPaymentAsync(request);

        // Assert
        Assert.False(result.Success);
        Assert.Equal("CONFIRMED", result.Status);
        Assert.Equal("1003", result.ErrorCode);
        Assert.Equal("Payment not in valid status for confirmation", result.Message);
        Assert.Contains("CONFIRMED", result.Details!);

        _mockStateMachine.Verify(x => x.TransitionAsync(It.IsAny<string>(), It.IsAny<PaymentStatus>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task ConfirmPaymentAsync_WithAmountExceeded_ShouldReturnError()
    {
        // Arrange
        var merchant = new Merchant
        {
            TerminalKey = "TEST_TERMINAL_1",
            Password = "test_password",
            IsActive = true
        };

        var payment = new PaymentEntity
        {
            PaymentId = "PAYMENT_123",
            OrderId = "ORDER_123",
            TerminalKey = "TEST_TERMINAL_1",
            Amount = 10000,
            CurrentStatus = PaymentStatus.AUTHORIZED
        };

        var request = new ConfirmPaymentRequest
        {
            TerminalKey = "TEST_TERMINAL_1",
            PaymentId = "PAYMENT_123",
            Token = "valid_token",
            Amount = 15000 // Exceeds authorized amount
        };

        _mockMerchantService.Setup(x => x.GetMerchantAsync("TEST_TERMINAL_1"))
            .ReturnsAsync(merchant);

        _mockTokenService.Setup(x => x.GenerateToken(It.IsAny<IDictionary<string, object>>(), "test_password"))
            .Returns("valid_token");

        _mockPaymentRepository.Setup(x => x.GetByIdAsync("PAYMENT_123"))
            .ReturnsAsync(payment);

        // Act
        var result = await _paymentService.ConfirmPaymentAsync(request);

        // Assert
        Assert.False(result.Success);
        Assert.Equal("AUTHORIZED", result.Status);
        Assert.Equal("1007", result.ErrorCode);
        Assert.Equal("Confirmation amount exceeds authorized amount", result.Message);
        Assert.Contains("15000", result.Details!);
        Assert.Contains("10000", result.Details!);

        _mockStateMachine.Verify(x => x.TransitionAsync(It.IsAny<string>(), It.IsAny<PaymentStatus>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task ConfirmPaymentAsync_WithPaymentNotFound_ShouldReturnError()
    {
        // Arrange
        var merchant = new Merchant
        {
            TerminalKey = "TEST_TERMINAL_1",
            Password = "test_password",
            IsActive = true
        };

        var request = new ConfirmPaymentRequest
        {
            TerminalKey = "TEST_TERMINAL_1",
            PaymentId = "NON_EXISTENT",
            Token = "valid_token"
        };

        _mockMerchantService.Setup(x => x.GetMerchantAsync("TEST_TERMINAL_1"))
            .ReturnsAsync(merchant);

        _mockTokenService.Setup(x => x.GenerateToken(It.IsAny<IDictionary<string, object>>(), "test_password"))
            .Returns("valid_token");

        _mockPaymentRepository.Setup(x => x.GetByIdAsync("NON_EXISTENT"))
            .ReturnsAsync((PaymentEntity?)null);

        // Act
        var result = await _paymentService.ConfirmPaymentAsync(request);

        // Assert
        Assert.False(result.Success);
        Assert.Equal("ERROR", result.Status);
        Assert.Equal("255", result.ErrorCode);
        Assert.Equal("Payment not found", result.Message);
    }

    [Fact]
    public async Task ConfirmPaymentAsync_WithWrongTerminal_ShouldReturnError()
    {
        // Arrange
        var merchant = new Merchant
        {
            TerminalKey = "TEST_TERMINAL_1",
            Password = "test_password",
            IsActive = true
        };

        var payment = new PaymentEntity
        {
            PaymentId = "PAYMENT_123",
            OrderId = "ORDER_123",
            TerminalKey = "DIFFERENT_TERMINAL", // Different terminal
            Amount = 10000,
            CurrentStatus = PaymentStatus.AUTHORIZED
        };

        var request = new ConfirmPaymentRequest
        {
            TerminalKey = "TEST_TERMINAL_1",
            PaymentId = "PAYMENT_123",
            Token = "valid_token"
        };

        _mockMerchantService.Setup(x => x.GetMerchantAsync("TEST_TERMINAL_1"))
            .ReturnsAsync(merchant);

        _mockTokenService.Setup(x => x.GenerateToken(It.IsAny<IDictionary<string, object>>(), "test_password"))
            .Returns("valid_token");

        _mockPaymentRepository.Setup(x => x.GetByIdAsync("PAYMENT_123"))
            .ReturnsAsync(payment);

        // Act
        var result = await _paymentService.ConfirmPaymentAsync(request);

        // Assert
        Assert.False(result.Success);
        Assert.Equal("AUTHORIZED", result.Status);
        Assert.Equal("255", result.ErrorCode);
        Assert.Equal("Payment not found", result.Message); // Security: don't reveal payment exists
    }

    // Validator Tests
    [Fact]
    public async Task Validator_WithValidRequest_ShouldReturnValid()
    {
        // Arrange
        var request = new ConfirmPaymentRequest
        {
            TerminalKey = "TEST_TERMINAL_1",
            PaymentId = "PAYMENT_123",
            Token = "valid_token"
        };

        // Act
        var result = await _validator.ValidateAsync(request);

        // Assert
        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Theory]
    [InlineData("")]
    [InlineData("123456789012345678901")] // >20 chars
    public async Task Validator_WithInvalidPaymentId_ShouldReturnInvalid(string paymentId)
    {
        // Arrange
        var request = new ConfirmPaymentRequest
        {
            TerminalKey = "TEST_TERMINAL_1",
            PaymentId = paymentId,
            Token = "valid_token"
        };

        // Act
        var result = await _validator.ValidateAsync(request);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == "PaymentId");
    }

    [Theory]
    [InlineData("192.168.1.1", true)]
    [InlineData("2001:db8::1", true)]
    [InlineData("invalid-ip", false)]
    [InlineData("", true)] // Empty is valid (optional)
    public async Task Validator_WithIPAddress_ShouldValidateCorrectly(string ip, bool shouldBeValid)
    {
        // Arrange
        var request = new ConfirmPaymentRequest
        {
            TerminalKey = "TEST_TERMINAL_1",
            PaymentId = "PAYMENT_123",
            Token = "valid_token",
            IP = string.IsNullOrEmpty(ip) ? null : ip
        };

        // Act
        var result = await _validator.ValidateAsync(request);

        // Assert
        Assert.Equal(shouldBeValid, result.IsValid);
        if (!shouldBeValid)
        {
            Assert.Contains(result.Errors, e => e.PropertyName == "IP");
        }
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(-100, false)]
    [InlineData(1000, true)]
    public async Task Validator_WithAmount_ShouldValidateCorrectly(long amount, bool shouldBeValid)
    {
        // Arrange
        var request = new ConfirmPaymentRequest
        {
            TerminalKey = "TEST_TERMINAL_1",
            PaymentId = "PAYMENT_123",
            Token = "valid_token",
            Amount = amount
        };

        // Act
        var result = await _validator.ValidateAsync(request);

        // Assert
        Assert.Equal(shouldBeValid, result.IsValid);
        if (!shouldBeValid)
        {
            Assert.Contains(result.Errors, e => e.PropertyName == "Amount");
        }
    }
}