using Microsoft.Extensions.Logging;
using Moq;
using Payment.Gateway.DTOs;
using Payment.Gateway.Models;
using Payment.Gateway.Services;

namespace Payment.Gateway.Tests;

public class PaymentServiceTests
{
    private readonly Mock<IPaymentRepository> _mockPaymentRepository;
    private readonly Mock<IPaymentStateMachine> _mockStateMachine;
    private readonly Mock<ITokenGenerationService> _mockTokenService;
    private readonly Mock<IMerchantService> _mockMerchantService;
    private readonly Mock<ILogger<PaymentService>> _mockLogger;
    private readonly PaymentService _paymentService;

    public PaymentServiceTests()
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
    }

    [Fact]
    public async Task InitializePaymentAsync_WithValidRequest_ShouldReturnSuccess()
    {
        // Arrange
        var merchant = new Merchant
        {
            TerminalKey = "TEST_TERMINAL_1",
            Password = "test_password",
            IsActive = true
        };

        var request = new InitPaymentRequest
        {
            TerminalKey = "TEST_TERMINAL_1",
            Amount = 10000,
            OrderId = "ORDER_123",
            Token = "valid_token",
            Description = "Test payment"
        };

        _mockMerchantService.Setup(x => x.GetMerchantAsync("TEST_TERMINAL_1"))
            .ReturnsAsync(merchant);

        _mockTokenService.Setup(x => x.GenerateToken(It.IsAny<IDictionary<string, object>>(), "test_password"))
            .Returns("valid_token");

        _mockPaymentRepository.Setup(x => x.GetByOrderIdAsync("ORDER_123", "TEST_TERMINAL_1"))
            .ReturnsAsync(Array.Empty<PaymentEntity>());

        _mockPaymentRepository.Setup(x => x.CreateAsync(It.IsAny<PaymentEntity>()))
            .ReturnsAsync((PaymentEntity p) => p);

        _mockStateMachine.Setup(x => x.TransitionAsync(It.IsAny<string>(), PaymentStatus.NEW, null, null))
            .ReturnsAsync(true);

        // Act
        var result = await _paymentService.InitializePaymentAsync(request);

        // Assert
        Assert.True(result.Success);
        Assert.Equal("NEW", result.Status);
        Assert.Equal("0", result.ErrorCode);
        Assert.Equal("TEST_TERMINAL_1", result.TerminalKey);
        Assert.Equal(10000, result.Amount);
        Assert.Equal("ORDER_123", result.OrderId);
        Assert.NotEmpty(result.PaymentId);
        Assert.NotNull(result.PaymentURL);

        _mockPaymentRepository.Verify(x => x.CreateAsync(It.IsAny<PaymentEntity>()), Times.Once);
        _mockStateMachine.Verify(x => x.TransitionAsync(It.IsAny<string>(), PaymentStatus.NEW, null, null), Times.Once);
    }

    [Fact]
    public async Task InitializePaymentAsync_WithInvalidMerchant_ShouldReturnError()
    {
        // Arrange
        var request = new InitPaymentRequest
        {
            TerminalKey = "INVALID_TERMINAL",
            Amount = 10000,
            OrderId = "ORDER_123",
            Token = "some_token"
        };

        _mockMerchantService.Setup(x => x.GetMerchantAsync("INVALID_TERMINAL"))
            .ReturnsAsync((Merchant?)null);

        // Act
        var result = await _paymentService.InitializePaymentAsync(request);

        // Assert
        Assert.False(result.Success);
        Assert.Equal("ERROR", result.Status);
        Assert.Equal("202", result.ErrorCode);
        Assert.Equal("Terminal not found or inactive", result.Message);
        Assert.Empty(result.PaymentId);

        _mockPaymentRepository.Verify(x => x.CreateAsync(It.IsAny<PaymentEntity>()), Times.Never);
    }

    [Fact]
    public async Task InitializePaymentAsync_WithInactiveTerminal_ShouldReturnError()
    {
        // Arrange
        var merchant = new Merchant
        {
            TerminalKey = "TEST_TERMINAL_1",
            Password = "test_password",
            IsActive = false // Inactive terminal
        };

        var request = new InitPaymentRequest
        {
            TerminalKey = "TEST_TERMINAL_1",
            Amount = 10000,
            OrderId = "ORDER_123",
            Token = "some_token"
        };

        _mockMerchantService.Setup(x => x.GetMerchantAsync("TEST_TERMINAL_1"))
            .ReturnsAsync(merchant);

        // Act
        var result = await _paymentService.InitializePaymentAsync(request);

        // Assert
        Assert.False(result.Success);
        Assert.Equal("ERROR", result.Status);
        Assert.Equal("202", result.ErrorCode);
        Assert.Equal("Terminal not found or inactive", result.Message);
        Assert.Empty(result.PaymentId);
    }

    [Fact]
    public async Task InitializePaymentAsync_WithInvalidToken_ShouldReturnError()
    {
        // Arrange
        var merchant = new Merchant
        {
            TerminalKey = "TEST_TERMINAL_1",
            Password = "test_password",
            IsActive = true
        };

        var request = new InitPaymentRequest
        {
            TerminalKey = "TEST_TERMINAL_1",
            Amount = 10000,
            OrderId = "ORDER_123",
            Token = "invalid_token"
        };

        _mockMerchantService.Setup(x => x.GetMerchantAsync("TEST_TERMINAL_1"))
            .ReturnsAsync(merchant);

        _mockTokenService.Setup(x => x.GenerateToken(It.IsAny<IDictionary<string, object>>(), "test_password"))
            .Returns("valid_token"); // Different from request token

        // Act
        var result = await _paymentService.InitializePaymentAsync(request);

        // Assert
        Assert.False(result.Success);
        Assert.Equal("ERROR", result.Status);
        Assert.Equal("204", result.ErrorCode);
        Assert.Equal("Invalid token signature", result.Message);
        Assert.Empty(result.PaymentId);

        _mockPaymentRepository.Verify(x => x.CreateAsync(It.IsAny<PaymentEntity>()), Times.Never);
    }

    [Fact]
    public async Task InitializePaymentAsync_WithDuplicateOrderId_ShouldReturnError()
    {
        // Arrange
        var merchant = new Merchant
        {
            TerminalKey = "TEST_TERMINAL_1",
            Password = "test_password",
            IsActive = true
        };

        var existingPayment = new PaymentEntity
        {
            PaymentId = "EXISTING_PAYMENT",
            OrderId = "ORDER_123",
            TerminalKey = "TEST_TERMINAL_1",
            Amount = 5000,
            CurrentStatus = PaymentStatus.NEW
        };

        var request = new InitPaymentRequest
        {
            TerminalKey = "TEST_TERMINAL_1",
            Amount = 10000,
            OrderId = "ORDER_123", // Duplicate OrderId
            Token = "valid_token"
        };

        _mockMerchantService.Setup(x => x.GetMerchantAsync("TEST_TERMINAL_1"))
            .ReturnsAsync(merchant);

        _mockTokenService.Setup(x => x.GenerateToken(It.IsAny<IDictionary<string, object>>(), "test_password"))
            .Returns("valid_token");

        _mockPaymentRepository.Setup(x => x.GetByOrderIdAsync("ORDER_123", "TEST_TERMINAL_1"))
            .ReturnsAsync(new[] { existingPayment });

        // Act
        var result = await _paymentService.InitializePaymentAsync(request);

        // Assert
        Assert.False(result.Success);
        Assert.Equal("ERROR", result.Status);
        Assert.Equal("335", result.ErrorCode);
        Assert.Equal("Order with this OrderId already exists", result.Message);
        Assert.Empty(result.PaymentId);

        _mockPaymentRepository.Verify(x => x.CreateAsync(It.IsAny<PaymentEntity>()), Times.Never);
    }

    [Fact]
    public async Task InitializePaymentAsync_WithAllOptionalParameters_ShouldSetAllProperties()
    {
        // Arrange
        var merchant = new Merchant
        {
            TerminalKey = "TEST_TERMINAL_1",
            Password = "test_password",
            IsActive = true
        };

        var request = new InitPaymentRequest
        {
            TerminalKey = "TEST_TERMINAL_1",
            Amount = 10000,
            OrderId = "ORDER_123",
            Token = "valid_token",
            PayType = "T", // Two-stage payment
            Description = "Test payment with all options",
            CustomerKey = "CUSTOMER_123",
            Recurrent = "Y",
            Language = "en",
            NotificationURL = "https://merchant.com/notify",
            SuccessURL = "https://merchant.com/success",
            FailURL = "https://merchant.com/fail",
            RedirectDueDate = DateTime.UtcNow.AddHours(2),
            DATA = new Dictionary<string, string> { { "Phone", "79001234567" } },
            Descriptor = "TEST MERCHANT"
        };

        _mockMerchantService.Setup(x => x.GetMerchantAsync("TEST_TERMINAL_1"))
            .ReturnsAsync(merchant);

        _mockTokenService.Setup(x => x.GenerateToken(It.IsAny<IDictionary<string, object>>(), "test_password"))
            .Returns("valid_token");

        _mockPaymentRepository.Setup(x => x.GetByOrderIdAsync("ORDER_123", "TEST_TERMINAL_1"))
            .ReturnsAsync(Array.Empty<PaymentEntity>());

        PaymentEntity? capturedPayment = null;
        _mockPaymentRepository.Setup(x => x.CreateAsync(It.IsAny<PaymentEntity>()))
            .Callback<PaymentEntity>(p => capturedPayment = p)
            .ReturnsAsync((PaymentEntity p) => p);

        _mockStateMachine.Setup(x => x.TransitionAsync(It.IsAny<string>(), PaymentStatus.NEW, null, null))
            .ReturnsAsync(true);

        // Act
        var result = await _paymentService.InitializePaymentAsync(request);

        // Assert
        Assert.True(result.Success);
        Assert.NotNull(capturedPayment);
        Assert.Equal("T", capturedPayment.PayType);
        Assert.Equal("Test payment with all options", capturedPayment.Description);
        Assert.Equal("CUSTOMER_123", capturedPayment.CustomerKey);
        Assert.True(capturedPayment.Recurrent);
        Assert.Equal("en", capturedPayment.Language);
        Assert.Equal("https://merchant.com/notify", capturedPayment.NotificationURL);
        Assert.Equal("https://merchant.com/success", capturedPayment.SuccessURL);
        Assert.Equal("https://merchant.com/fail", capturedPayment.FailURL);
        Assert.NotNull(capturedPayment.DataJson);
        Assert.Contains("Phone", capturedPayment.DataJson);
    }

    [Fact]
    public async Task InitializePaymentAsync_ShouldGenerateUniquePaymentId()
    {
        // Arrange
        var merchant = new Merchant
        {
            TerminalKey = "TEST_TERMINAL_1",
            Password = "test_password",
            IsActive = true
        };

        var request = new InitPaymentRequest
        {
            TerminalKey = "TEST_TERMINAL_1",
            Amount = 10000,
            OrderId = "ORDER_123",
            Token = "valid_token"
        };

        _mockMerchantService.Setup(x => x.GetMerchantAsync("TEST_TERMINAL_1"))
            .ReturnsAsync(merchant);

        _mockTokenService.Setup(x => x.GenerateToken(It.IsAny<IDictionary<string, object>>(), "test_password"))
            .Returns("valid_token");

        _mockPaymentRepository.Setup(x => x.GetByOrderIdAsync("ORDER_123", "TEST_TERMINAL_1"))
            .ReturnsAsync(Array.Empty<PaymentEntity>());

        _mockPaymentRepository.Setup(x => x.CreateAsync(It.IsAny<PaymentEntity>()))
            .ReturnsAsync((PaymentEntity p) => p);

        _mockStateMachine.Setup(x => x.TransitionAsync(It.IsAny<string>(), PaymentStatus.NEW, null, null))
            .ReturnsAsync(true);

        // Act
        var result1 = await _paymentService.InitializePaymentAsync(request);
        var result2 = await _paymentService.InitializePaymentAsync(request);

        // Assert
        Assert.True(result1.Success);
        Assert.True(result2.Success);
        Assert.NotEqual(result1.PaymentId, result2.PaymentId);
        Assert.Equal(20, result1.PaymentId.Length); // Should be 20 characters
        Assert.Equal(20, result2.PaymentId.Length);
    }

    [Fact]
    public async Task InitializePaymentAsync_WithRepositoryException_ShouldReturnError()
    {
        // Arrange
        var merchant = new Merchant
        {
            TerminalKey = "TEST_TERMINAL_1",
            Password = "test_password",
            IsActive = true
        };

        var request = new InitPaymentRequest
        {
            TerminalKey = "TEST_TERMINAL_1",
            Amount = 10000,
            OrderId = "ORDER_123",
            Token = "valid_token"
        };

        _mockMerchantService.Setup(x => x.GetMerchantAsync("TEST_TERMINAL_1"))
            .ReturnsAsync(merchant);

        _mockTokenService.Setup(x => x.GenerateToken(It.IsAny<IDictionary<string, object>>(), "test_password"))
            .Returns("valid_token");

        _mockPaymentRepository.Setup(x => x.GetByOrderIdAsync("ORDER_123", "TEST_TERMINAL_1"))
            .ReturnsAsync(Array.Empty<PaymentEntity>());

        _mockPaymentRepository.Setup(x => x.CreateAsync(It.IsAny<PaymentEntity>()))
            .ThrowsAsync(new Exception("Database connection failed"));

        // Act
        var result = await _paymentService.InitializePaymentAsync(request);

        // Assert
        Assert.False(result.Success);
        Assert.Equal("ERROR", result.Status);
        Assert.Equal("999", result.ErrorCode);
        Assert.Equal("Internal server error", result.Message);
        Assert.Equal("Database connection failed", result.Details);
        Assert.Empty(result.PaymentId);
    }
}