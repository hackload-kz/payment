using FluentAssertions;

namespace PaymentGateway.Tests;

[TestFixture]
public class BasicTests
{
    [Test]
    public void TC_BASIC_001_BasicTest_ShouldPass()
    {
        // Arrange
        var expected = "test";
        
        // Act
        var actual = "test";
        
        // Assert
        actual.Should().Be(expected);
    }

    [Test]
    public void TC_BASIC_002_PaymentIdValidation_WithValidId_ShouldReturnTrue()
    {
        // Arrange
        var validPaymentId = "pay_123456789";
        
        // Act
        var isValid = IsValidPaymentId(validPaymentId);
        
        // Assert
        isValid.Should().BeTrue();
    }

    [Test]
    public void TC_BASIC_003_PaymentIdValidation_WithInvalidId_ShouldReturnFalse()
    {
        // Arrange
        var invalidPaymentId = "invalid-payment-id";
        
        // Act
        var isValid = IsValidPaymentId(invalidPaymentId);
        
        // Assert
        isValid.Should().BeFalse();
    }

    [Test]
    public void TC_BASIC_004_AmountValidation_WithValidAmount_ShouldReturnTrue()
    {
        // Arrange
        var validAmount = 150000m;
        
        // Act
        var isValid = IsValidAmount(validAmount);
        
        // Assert
        isValid.Should().BeTrue();
    }

    [Test]
    public void TC_BASIC_005_AmountValidation_WithNegativeAmount_ShouldReturnFalse()
    {
        // Arrange
        var invalidAmount = -1000m;
        
        // Act
        var isValid = IsValidAmount(invalidAmount);
        
        // Assert
        isValid.Should().BeFalse();
    }

    [Test]
    public void TC_BASIC_006_CurrencyValidation_WithValidCurrency_ShouldReturnTrue()
    {
        // Arrange
        var validCurrencies = new[] { "KZT", "USD", "EUR", "BYN", "RUB" };
        
        // Act & Assert
        foreach (var currency in validCurrencies)
        {
            IsValidCurrency(currency).Should().BeTrue($"Currency {currency} should be valid");
        }
    }

    [Test]
    public void TC_BASIC_007_CurrencyValidation_WithInvalidCurrency_ShouldReturnFalse()
    {
        // Arrange
        var invalidCurrencies = new[] { "JPY", "GBP", "BTC", "" };
        
        // Act & Assert
        foreach (var currency in invalidCurrencies)
        {
            IsValidCurrency(currency).Should().BeFalse($"Currency {currency} should be invalid");
        }
    }

    // Helper methods for validation
    private static bool IsValidPaymentId(string paymentId) =>
        !string.IsNullOrWhiteSpace(paymentId) && 
        paymentId.Length <= 50 && 
        System.Text.RegularExpressions.Regex.IsMatch(paymentId, @"^pay_[a-zA-Z0-9]+$");

    private static bool IsValidAmount(decimal amount) =>
        amount > 0 && amount <= 100000000;

    private static bool IsValidCurrency(string currency)
    {
        var allowedCurrencies = new[] { "KZT", "USD", "EUR", "BYN", "RUB" };
        return allowedCurrencies.Contains(currency?.ToUpper());
    }
}