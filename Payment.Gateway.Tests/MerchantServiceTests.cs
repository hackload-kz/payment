using Microsoft.Extensions.Logging;
using Moq;
using Payment.Gateway.Models;
using Payment.Gateway.Services;

namespace Payment.Gateway.Tests;

public class MerchantServiceTests
{
    private readonly Mock<IMerchantRepository> _mockMerchantRepository;
    private readonly Mock<ILogger<MerchantService>> _mockLogger;
    private readonly MerchantService _merchantService;

    public MerchantServiceTests()
    {
        _mockMerchantRepository = new Mock<IMerchantRepository>();
        _mockLogger = new Mock<ILogger<MerchantService>>();
        _merchantService = new MerchantService(_mockMerchantRepository.Object, _mockLogger.Object);
    }

    [Fact]
    public async Task GetMerchantAsync_WithValidTerminalKey_ShouldReturnMerchant()
    {
        // Arrange
        var terminalKey = "TEST_TERMINAL_1";
        var expectedMerchant = new Merchant
        {
            TerminalKey = terminalKey,
            Password = "test_password_1",
            IsActive = true
        };

        _mockMerchantRepository
            .Setup(x => x.GetByTerminalKeyAsync(terminalKey))
            .ReturnsAsync(expectedMerchant);

        _mockMerchantRepository
            .Setup(x => x.UpdateAsync(It.IsAny<Merchant>()))
            .ReturnsAsync((Merchant m) => m);

        // Act
        var result = await _merchantService.GetMerchantAsync(terminalKey);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(terminalKey, result.TerminalKey);
        Assert.Equal("test_password_1", result.Password);
        Assert.True(result.IsActive);

        // Verify that UpdateAsync was called to update LastLoginDate
        _mockMerchantRepository.Verify(x => x.UpdateAsync(It.IsAny<Merchant>()), Times.Once);
    }

    [Fact]
    public async Task GetMerchantAsync_WithInvalidTerminalKey_ShouldReturnNull()
    {
        // Arrange
        var terminalKey = "INVALID_TERMINAL";

        _mockMerchantRepository
            .Setup(x => x.GetByTerminalKeyAsync(terminalKey))
            .ReturnsAsync((Merchant?)null);

        // Act
        var result = await _merchantService.GetMerchantAsync(terminalKey);

        // Assert
        Assert.Null(result);

        // Verify that UpdateAsync was not called
        _mockMerchantRepository.Verify(x => x.UpdateAsync(It.IsAny<Merchant>()), Times.Never);
    }

    [Fact]
    public async Task ValidateCredentialsAsync_WithValidCredentials_ShouldReturnTrue()
    {
        // Arrange
        var terminalKey = "TEST_TERMINAL_1";
        var password = "test_password_1";

        _mockMerchantRepository
            .Setup(x => x.ValidateCredentialsAsync(terminalKey, password))
            .ReturnsAsync(true);

        // Act
        var result = await _merchantService.ValidateCredentialsAsync(terminalKey, password);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task ValidateCredentialsAsync_WithInvalidCredentials_ShouldReturnFalse()
    {
        // Arrange
        var terminalKey = "TEST_TERMINAL_1";
        var password = "wrong_password";

        _mockMerchantRepository
            .Setup(x => x.ValidateCredentialsAsync(terminalKey, password))
            .ReturnsAsync(false);

        // Act
        var result = await _merchantService.ValidateCredentialsAsync(terminalKey, password);

        // Assert
        Assert.False(result);
    }

    [Theory]
    [InlineData("", "password")]
    [InlineData("terminal", "")]
    [InlineData("", "")]
    [InlineData(null, "password")]
    [InlineData("terminal", null)]
    public async Task ValidateCredentialsAsync_WithEmptyOrNullCredentials_ShouldReturnFalse(string? terminalKey, string? password)
    {
        // Act
        var result = await _merchantService.ValidateCredentialsAsync(terminalKey!, password!);

        // Assert
        Assert.False(result);

        // Verify that repository method was not called
        _mockMerchantRepository.Verify(x => x.ValidateCredentialsAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }
}