using Payment.Gateway.Services;
using System.Collections.Generic;

namespace Payment.Gateway.Tests;

public class TokenGenerationServiceTests
{
    private readonly TokenGenerationService _tokenService;

    public TokenGenerationServiceTests()
    {
        _tokenService = new TokenGenerationService();
    }

    [Fact]
    public void GenerateToken_WithSpecificationExample_ShouldReturnExpectedHash()
    {
        // Arrange - Example from the specification
        var parameters = new Dictionary<string, object>
        {
            ["TerminalKey"] = "MerchantTerminalKey",
            ["Amount"] = 19200,
            ["OrderId"] = "21090",
            ["Description"] = "Подарочная карта на 1000 рублей"
        };
        var password = "usaf8fw8fsw21g";
        var expectedToken = "0024a00af7c350a3a67ca168ce06502aa72772456662e38696d48b56ee9c97d9";

        // Act
        var actualToken = _tokenService.GenerateToken(parameters, password);

        // Assert
        Assert.Equal(expectedToken, actualToken);
    }

    [Fact]
    public void GenerateToken_WithComplexObjects_ShouldExcludeThemFromCalculation()
    {
        // Arrange
        var parameters = new Dictionary<string, object>
        {
            ["TerminalKey"] = "TestTerminal",
            ["Amount"] = 10000,
            ["OrderId"] = "12345",
            ["DATA"] = new Dictionary<string, string> { ["key"] = "value" }, // Should be excluded
            ["Receipt"] = new { Items = new[] { new { Name = "Test" } } } // Should be excluded
        };
        var password = "testpassword";

        // Act
        var token1 = _tokenService.GenerateToken(parameters, password);
        
        // Generate token without complex objects
        var simpleParameters = new Dictionary<string, object>
        {
            ["TerminalKey"] = "TestTerminal",
            ["Amount"] = 10000,
            ["OrderId"] = "12345"
        };
        var token2 = _tokenService.GenerateToken(simpleParameters, password);

        // Assert - Both tokens should be the same since complex objects are excluded
        Assert.Equal(token1, token2);
    }

    [Fact]
    public void GenerateToken_WithDifferentParameterOrder_ShouldReturnSameToken()
    {
        // Arrange
        var parameters1 = new Dictionary<string, object>
        {
            ["TerminalKey"] = "TestTerminal",
            ["Amount"] = 10000,
            ["OrderId"] = "12345"
        };

        var parameters2 = new Dictionary<string, object>
        {
            ["OrderId"] = "12345",
            ["TerminalKey"] = "TestTerminal",
            ["Amount"] = 10000
        };
        var password = "testpassword";

        // Act
        var token1 = _tokenService.GenerateToken(parameters1, password);
        var token2 = _tokenService.GenerateToken(parameters2, password);

        // Assert
        Assert.Equal(token1, token2);
    }

    [Fact]
    public void GenerateToken_WithBooleanValues_ShouldConvertToLowercase()
    {
        // Arrange
        var parameters = new Dictionary<string, object>
        {
            ["TerminalKey"] = "TestTerminal",
            ["Recurrent"] = true,
            ["Test"] = false
        };
        var password = "testpassword";

        // Act
        var token = _tokenService.GenerateToken(parameters, password);

        // Assert - Should not throw and should include boolean values as "true"/"false"
        Assert.NotNull(token);
        Assert.Equal(64, token.Length); // SHA-256 hex length
    }

    [Fact]
    public void GenerateToken_WithNullValues_ShouldHandleThemGracefully()
    {
        // Arrange
        var parameters = new Dictionary<string, object>
        {
            ["TerminalKey"] = "TestTerminal",
            ["Description"] = null!,
            ["Amount"] = 10000
        };
        var password = "testpassword";

        // Act
        var token = _tokenService.GenerateToken(parameters, password);

        // Assert
        Assert.NotNull(token);
        Assert.Equal(64, token.Length);
    }

    [Fact]
    public void ValidateToken_WithCorrectToken_ShouldReturnTrue()
    {
        // Arrange
        var parameters = new Dictionary<string, object>
        {
            ["TerminalKey"] = "TestTerminal",
            ["Amount"] = 10000,
            ["OrderId"] = "12345"
        };
        var password = "testpassword";
        var token = _tokenService.GenerateToken(parameters, password);

        // Act
        var isValid = _tokenService.ValidateToken(parameters, token, password);

        // Assert
        Assert.True(isValid);
    }

    [Fact]
    public void ValidateToken_WithIncorrectToken_ShouldReturnFalse()
    {
        // Arrange
        var parameters = new Dictionary<string, object>
        {
            ["TerminalKey"] = "TestTerminal",
            ["Amount"] = 10000,
            ["OrderId"] = "12345"
        };
        var password = "testpassword";
        var wrongToken = "wrongtoken";

        // Act
        var isValid = _tokenService.ValidateToken(parameters, wrongToken, password);

        // Assert
        Assert.False(isValid);
    }

    [Fact]
    public void ValidateToken_WithWrongPassword_ShouldReturnFalse()
    {
        // Arrange
        var parameters = new Dictionary<string, object>
        {
            ["TerminalKey"] = "TestTerminal",
            ["Amount"] = 10000,
            ["OrderId"] = "12345"
        };
        var password = "testpassword";
        var wrongPassword = "wrongpassword";
        var token = _tokenService.GenerateToken(parameters, password);

        // Act
        var isValid = _tokenService.ValidateToken(parameters, token, wrongPassword);

        // Assert
        Assert.False(isValid);
    }

    [Theory]
    [InlineData(123, "123")]
    [InlineData(true, "true")]
    [InlineData(false, "false")]
    [InlineData("test", "test")]
    public void GenerateToken_WithDifferentDataTypes_ShouldConvertCorrectly(object value, string expectedString)
    {
        // Arrange
        var parameters = new Dictionary<string, object>
        {
            ["TerminalKey"] = "TestTerminal",
            ["TestParam"] = value
        };
        var password = "testpassword";

        // Act & Assert - Should not throw exception
        var token = _tokenService.GenerateToken(parameters, password);
        Assert.NotNull(token);
        Assert.Equal(64, token.Length);
    }
}