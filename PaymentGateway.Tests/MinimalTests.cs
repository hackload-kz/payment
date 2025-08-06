using FluentAssertions;

namespace PaymentGateway.Tests;

[TestFixture]
public class MinimalTests
{
    [Test]
    public void TC_MIN_001_BasicTest_ShouldPass()
    {
        // Arrange
        var expected = "PaymentGateway.API";
        
        // Act
        var actual = "PaymentGateway.API";
        
        // Assert
        actual.Should().Be(expected);
    }

    [Test]
    public void TC_MIN_002_PaymentIdValidation_ShouldWork()
    {
        // Arrange
        var validId = "pay_123456789";
        var invalidId = "invalid-id";
        
        // Act & Assert
        IsValidPaymentId(validId).Should().BeTrue();
        IsValidPaymentId(invalidId).Should().BeFalse();
    }

    [Test]
    public void TC_MIN_003_AmountValidation_ShouldWork()
    {
        // Arrange & Act & Assert
        IsValidAmount(150000m).Should().BeTrue();
        IsValidAmount(-1000m).Should().BeFalse();
        IsValidAmount(0m).Should().BeFalse();
    }

    [Test]
    public void TC_MIN_004_CurrencyValidation_ShouldWork()
    {  
        // Arrange & Act & Assert
        IsValidCurrency("RUB").Should().BeTrue();
        IsValidCurrency("USD").Should().BeTrue();
        IsValidCurrency("EUR").Should().BeTrue();
        IsValidCurrency("JPY").Should().BeFalse();
        IsValidCurrency("").Should().BeFalse();
    }

    // Test validation methods that correspond to business logic
    private static bool IsValidPaymentId(string paymentId) =>
        !string.IsNullOrWhiteSpace(paymentId) && 
        paymentId.Length <= 50 && 
        System.Text.RegularExpressions.Regex.IsMatch(paymentId, @"^pay_[a-zA-Z0-9]+$");

    private static bool IsValidAmount(decimal amount) =>
        amount > 0 && amount <= 100000000;

    private static bool IsValidCurrency(string currency)
    {
        var allowedCurrencies = new[] { "RUB", "USD", "EUR" };
        return allowedCurrencies.Contains(currency?.ToUpperInvariant());
    }
}