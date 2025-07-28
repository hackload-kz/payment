using Payment.Gateway.DTOs;
using Payment.Gateway.Validators;

namespace Payment.Gateway.Tests;

public class InitPaymentRequestValidatorTests
{
    private readonly InitPaymentRequestValidator _validator;

    public InitPaymentRequestValidatorTests()
    {
        _validator = new InitPaymentRequestValidator();
    }

    [Fact]
    public async Task Validate_WithValidRequest_ShouldReturnValid()
    {
        // Arrange
        var request = new InitPaymentRequest
        {
            TerminalKey = "TEST_TERMINAL_1",
            Amount = 10000,
            OrderId = "ORDER_123",
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
    [InlineData("12345678901234567890X")] // >20 chars
    public async Task Validate_WithInvalidTerminalKey_ShouldReturnInvalid(string terminalKey)
    {
        // Arrange
        var request = new InitPaymentRequest
        {
            TerminalKey = terminalKey,
            Amount = 10000,
            OrderId = "ORDER_123",
            Token = "valid_token"
        };

        // Act
        var result = await _validator.ValidateAsync(request);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorMessage.Contains("Terminal Key") || e.ErrorMessage.Contains("TerminalKey"));
    }

    [Theory]
    [InlineData(999, "Amount must be at least 1000 kopecks")]
    [InlineData(0, "Amount must be at least 1000 kopecks")]
    [InlineData(-1000, "Amount must be at least 1000 kopecks")]
    public async Task Validate_WithInvalidAmount_ShouldReturnInvalid(long amount, string expectedErrorSubstring)
    {
        // Arrange
        var request = new InitPaymentRequest
        {
            TerminalKey = "TEST_TERMINAL_1",
            Amount = amount,
            OrderId = "ORDER_123",
            Token = "valid_token"
        };

        // Act
        var result = await _validator.ValidateAsync(request);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorMessage.Contains(expectedErrorSubstring));
    }

    [Theory]
    [InlineData("")]
    [InlineData("1234567890123456789012345678901234567")] // >36 chars
    public async Task Validate_WithInvalidOrderId_ShouldReturnInvalid(string orderId)
    {
        // Arrange
        var request = new InitPaymentRequest
        {
            TerminalKey = "TEST_TERMINAL_1",
            Amount = 10000,
            OrderId = orderId,
            Token = "valid_token"
        };

        // Act
        var result = await _validator.ValidateAsync(request);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorMessage.Contains("Order Id") || e.ErrorMessage.Contains("OrderId"));
    }

    [Fact]
    public async Task Validate_WithEmptyToken_ShouldReturnInvalid()
    {
        // Arrange
        var request = new InitPaymentRequest
        {
            TerminalKey = "TEST_TERMINAL_1",
            Amount = 10000,
            OrderId = "ORDER_123",
            Token = ""
        };

        // Act
        var result = await _validator.ValidateAsync(request);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorMessage.Contains("Token is required"));
    }

    [Theory]
    [InlineData("O", true)]
    [InlineData("T", true)]
    [InlineData("X", false)]
    [InlineData("OT", false)] // >1 char
    public async Task Validate_WithPayType_ShouldValidateCorrectly(string payType, bool shouldBeValid)
    {
        // Arrange
        var request = new InitPaymentRequest
        {
            TerminalKey = "TEST_TERMINAL_1",
            Amount = 10000,
            OrderId = "ORDER_123",
            Token = "valid_token",
            PayType = payType
        };

        // Act
        var result = await _validator.ValidateAsync(request);

        // Assert
        Assert.Equal(shouldBeValid, result.IsValid);
        if (!shouldBeValid)
        {
            Assert.Contains(result.Errors, e => e.ErrorMessage.Contains("PayType"));
        }
    }

    [Fact]
    public async Task Validate_WithLongDescription_ShouldReturnInvalid()
    {
        // Arrange
        var request = new InitPaymentRequest
        {
            TerminalKey = "TEST_TERMINAL_1",
            Amount = 10000,
            OrderId = "ORDER_123",
            Token = "valid_token",
            Description = new string('A', 141) // >140 chars
        };

        // Act
        var result = await _validator.ValidateAsync(request);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorMessage.Contains("Description"));
    }

    [Theory]
    [InlineData("Y", "CUSTOMER_123", true)]
    [InlineData("Y", "", false)] // CustomerKey required when Recurrent = Y
    [InlineData("Y", null, false)]
    [InlineData("N", "", true)] // CustomerKey not required when Recurrent = N
    [InlineData("N", null, true)]
    [InlineData(null, "", true)] // CustomerKey not required when Recurrent is null
    public async Task Validate_WithRecurrentAndCustomerKey_ShouldValidateCorrectly(string? recurrent, string? customerKey, bool shouldBeValid)
    {
        // Arrange
        var request = new InitPaymentRequest
        {
            TerminalKey = "TEST_TERMINAL_1",
            Amount = 10000,
            OrderId = "ORDER_123",
            Token = "valid_token",
            Recurrent = recurrent,
            CustomerKey = customerKey
        };

        // Act
        var result = await _validator.ValidateAsync(request);

        // Assert
        Assert.Equal(shouldBeValid, result.IsValid);
        if (!shouldBeValid)
        {
            Assert.Contains(result.Errors, e => e.ErrorMessage.Contains("CustomerKey is required when Recurrent"));
        }
    }

    [Theory]
    [InlineData("ru", true)]
    [InlineData("en", true)]
    [InlineData("fr", false)]
    [InlineData("rus", false)] // >2 chars
    public async Task Validate_WithLanguage_ShouldValidateCorrectly(string language, bool shouldBeValid)
    {
        // Arrange
        var request = new InitPaymentRequest
        {
            TerminalKey = "TEST_TERMINAL_1",
            Amount = 10000,
            OrderId = "ORDER_123",
            Token = "valid_token",
            Language = language
        };

        // Act
        var result = await _validator.ValidateAsync(request);

        // Assert
        Assert.Equal(shouldBeValid, result.IsValid);
        if (!shouldBeValid)
        {
            Assert.Contains(result.Errors, e => e.ErrorMessage.Contains("Language"));
        }
    }

    [Theory]
    [InlineData("https://merchant.com/notify", true)]
    [InlineData("http://merchant.com/notify", true)]
    [InlineData("invalid-url", false)]
    [InlineData("", true)] // Empty is valid (optional field)
    public async Task Validate_WithNotificationURL_ShouldValidateCorrectly(string notificationUrl, bool shouldBeValid)
    {
        // Arrange
        var request = new InitPaymentRequest
        {
            TerminalKey = "TEST_TERMINAL_1",
            Amount = 10000,
            OrderId = "ORDER_123",
            Token = "valid_token",
            NotificationURL = string.IsNullOrEmpty(notificationUrl) ? null : notificationUrl
        };

        // Act
        var result = await _validator.ValidateAsync(request);

        // Assert
        Assert.Equal(shouldBeValid, result.IsValid);
        if (!shouldBeValid)
        {
            Assert.Contains(result.Errors, e => e.ErrorMessage.Contains("NotificationURL"));
        }
    }

    [Fact]
    public async Task Validate_WithRedirectDueDateInPast_ShouldReturnInvalid()
    {
        // Arrange
        var request = new InitPaymentRequest
        {
            TerminalKey = "TEST_TERMINAL_1",
            Amount = 10000,
            OrderId = "ORDER_123",
            Token = "valid_token",
            RedirectDueDate = DateTime.UtcNow.AddMinutes(-1) // In the past
        };

        // Act
        var result = await _validator.ValidateAsync(request);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorMessage.Contains("RedirectDueDate"));
    }

    [Fact]
    public async Task Validate_WithRedirectDueDateTooFar_ShouldReturnInvalid()
    {
        // Arrange
        var request = new InitPaymentRequest
        {
            TerminalKey = "TEST_TERMINAL_1",
            Amount = 10000,
            OrderId = "ORDER_123",
            Token = "valid_token",
            RedirectDueDate = DateTime.UtcNow.AddDays(91) // >90 days
        };

        // Act
        var result = await _validator.ValidateAsync(request);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorMessage.Contains("RedirectDueDate"));
    }

    [Fact]
    public async Task Validate_WithTooManyDATAParameters_ShouldReturnInvalid()
    {
        // Arrange
        var data = new Dictionary<string, string>();
        for (int i = 1; i <= 21; i++) // >20 parameters
        {
            data[$"key{i}"] = $"value{i}";
        }

        var request = new InitPaymentRequest
        {
            TerminalKey = "TEST_TERMINAL_1",
            Amount = 10000,
            OrderId = "ORDER_123",
            Token = "valid_token",
            DATA = data
        };

        // Act
        var result = await _validator.ValidateAsync(request);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorMessage.Contains("DATA object must contain at most 20"));
    }

    [Theory]
    [InlineData("79001234567", true)] // Valid phone
    [InlineData("+79001234567", true)] // Valid phone with +
    [InlineData("1234567", true)] // Minimum length
    [InlineData("12345678901234567890", true)] // Maximum length
    [InlineData("123456", false)] // Too short
    [InlineData("123456789012345678901", false)] // Too long
    [InlineData("79001234567a", false)] // Contains non-digit
    public async Task Validate_WithPhoneInDATA_ShouldValidateCorrectly(string phone, bool shouldBeValid)
    {
        // Arrange
        var request = new InitPaymentRequest
        {
            TerminalKey = "TEST_TERMINAL_1",
            Amount = 10000,
            OrderId = "ORDER_123",
            Token = "valid_token",
            DATA = new Dictionary<string, string> { { "Phone", phone } }
        };

        // Act
        var result = await _validator.ValidateAsync(request);

        // Assert
        Assert.Equal(shouldBeValid, result.IsValid);
        if (!shouldBeValid)
        {
            Assert.Contains(result.Errors, e => e.ErrorMessage.Contains("Phone"));
        }
    }

    [Theory]
    [InlineData("wallet123", true)] // Valid account
    [InlineData("123456789012345678901234567890", true)] // Maximum length (30)
    [InlineData("1234567890123456789012345678901", false)] // Too long (31)
    public async Task Validate_WithAccountInDATA_ShouldValidateCorrectly(string account, bool shouldBeValid)
    {
        // Arrange
        var request = new InitPaymentRequest
        {
            TerminalKey = "TEST_TERMINAL_1",
            Amount = 10000,
            OrderId = "ORDER_123",
            Token = "valid_token",
            DATA = new Dictionary<string, string> { { "account", account } }
        };

        // Act
        var result = await _validator.ValidateAsync(request);

        // Assert
        Assert.Equal(shouldBeValid, result.IsValid);
        if (!shouldBeValid)
        {
            Assert.Contains(result.Errors, e => e.ErrorMessage.Contains("Account"));
        }
    }
}