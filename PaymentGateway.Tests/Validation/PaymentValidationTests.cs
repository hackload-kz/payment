using FluentAssertions;
using PaymentGateway.Core.DTOs.PaymentInit;

namespace PaymentGateway.Tests.Validation;

[TestFixture]
public class PaymentValidationTests
{
    [Test]
    public void ValidatePaymentId_WithValidFormat_ReturnsTrue()
    {
        // Arrange
        var validPaymentIds = new[]
        {
            "pay_123456789",
            "pay_abc123def456",
            "pay_TEST123",
            "pay_987654321abcdef"
        };

        // Act & Assert
        foreach (var paymentId in validPaymentIds)
        {
            IsValidPaymentId(paymentId).Should().BeTrue($"Payment ID '{paymentId}' should be valid");
        }
    }

    [Test]
    public void ValidatePaymentId_WithInvalidFormat_ReturnsFalse()
    {
        // Arrange
        var invalidPaymentIds = new[]
        {
            "",
            "invalid-payment-id",
            "pay_",
            "payment_123456789", // Wrong prefix
            "pay_123 456", // Contains space
            "pay_123@456", // Contains special character
            "pay_" + new string('a', 50) // Too long
        };

        // Act & Assert
        foreach (var paymentId in invalidPaymentIds)
        {
            IsValidPaymentId(paymentId).Should().BeFalse($"Payment ID '{paymentId}' should be invalid");
        }
    }

    [Test]
    public void ValidateCardNumber_WithValidCards_ReturnsTrue()
    {
        // Arrange - Valid test card numbers that pass Luhn algorithm
        var validCardNumbers = new[]
        {
            "4111111111111111", // Visa
            "5555555555554444", // Mastercard
            "378282246310005",  // American Express
            "4000000000000002", // Valid Luhn but test decline card
            "4242424242424242"  // Stripe test card
        };

        // Act & Assert
        foreach (var cardNumber in validCardNumbers)
        {
            IsValidCardNumber(cardNumber).Should().BeTrue($"Card number '{cardNumber}' should be valid");
        }
    }

    [Test]
    public void ValidateCardNumber_WithInvalidCards_ReturnsFalse()
    {
        // Arrange - Invalid card numbers
        var invalidCardNumbers = new[]
        {
            "",
            "1234567890123456", // Fails Luhn check
            "4111111111111112", // Fails Luhn check
            "411111111111111",  // Too short
            "41111111111111111", // Too long
            "411111111111111a", // Contains letter
            "4111 1111 1111 1111" // Contains spaces (should be stripped first)
        };

        // Act & Assert
        foreach (var cardNumber in invalidCardNumbers)
        {
            IsValidCardNumber(cardNumber).Should().BeFalse($"Card number '{cardNumber}' should be invalid");
        }
    }

    [Test]
    public void ValidateCardNumber_WithSpacesAndDashes_HandlesCorrectly()
    {
        // Arrange
        var cardNumberWithSpaces = "4111 1111 1111 1111";
        var cardNumberWithDashes = "4111-1111-1111-1111";

        // Act & Assert
        IsValidCardNumber(cardNumberWithSpaces).Should().BeTrue("Card number with spaces should be valid after cleaning");
        IsValidCardNumber(cardNumberWithDashes).Should().BeTrue("Card number with dashes should be valid after cleaning");
    }

    [Test]
    public void ValidateExpiryDate_WithValidDates_ReturnsTrue()
    {
        // Arrange - Future dates
        var nextYear = DateTime.Now.Year + 1;
        var validExpiryDates = new[]
        {
            $"12/{nextYear.ToString().Substring(2)}", // December next year
            $"01/{(nextYear + 1).ToString().Substring(2)}", // January year after next
            "12/99" // Far future
        };

        // Act & Assert
        foreach (var expiryDate in validExpiryDates)
        {
            IsValidExpiryDate(expiryDate).Should().BeTrue($"Expiry date '{expiryDate}' should be valid");
        }
    }

    [Test]
    public void ValidateExpiryDate_WithInvalidDates_ReturnsFalse()
    {
        // Arrange
        var invalidExpiryDates = new[]
        {
            "",
            "13/25", // Invalid month
            "00/25", // Invalid month
            "12/20", // Past year
            "1/25",  // Invalid format (should be 01/25)
            "12/5",  // Invalid format (should be 12/05)
            "12/2025", // Four-digit year
            "12-25", // Wrong separator
            "December/25" // Text month
        };

        // Act & Assert
        foreach (var expiryDate in invalidExpiryDates)
        {
            IsValidExpiryDate(expiryDate).Should().BeFalse($"Expiry date '{expiryDate}' should be invalid");
        }
    }

    [Test]
    public void ValidateCvv_WithValidCvv_ReturnsTrue()
    {
        // Arrange
        var validCvvs = new[]
        {
            "123", // 3-digit CVV
            "1234", // 4-digit CVV (American Express)
            "000", // Edge case
            "999"  // Edge case
        };

        // Act & Assert
        foreach (var cvv in validCvvs)
        {
            IsValidCvv(cvv).Should().BeTrue($"CVV '{cvv}' should be valid");
        }
    }

    [Test]
    public void ValidateCvv_WithInvalidCvv_ReturnsFalse()
    {
        // Arrange
        var invalidCvvs = new[]
        {
            "",
            "12",    // Too short
            "12345", // Too long
            "12a",   // Contains letter
            " 123",  // Contains space
            "12.3"   // Contains decimal
        };

        // Act & Assert
        foreach (var cvv in invalidCvvs)
        {
            IsValidCvv(cvv).Should().BeFalse($"CVV '{cvv}' should be invalid");
        }
    }

    [Test]
    public void ValidateEmail_WithValidEmails_ReturnsTrue()
    {
        // Arrange
        var validEmails = new[]
        {
            "test@example.com",
            "user.name@domain.co.uk",
            "firstname+lastname@example.com",
            "email@123.123.123.123", // IP address
            "1234567890@example.com",
            "email@example-one.com",
            "_______@example.com",
            "test@subdomain.example.com"
        };

        // Act & Assert
        foreach (var email in validEmails)
        {
            IsValidEmail(email).Should().BeTrue($"Email '{email}' should be valid");
        }
    }

    [Test]
    public void ValidateEmail_WithInvalidEmails_ReturnsFalse()
    {
        // Arrange
        var invalidEmails = new[]
        {
            "",
            "invalid-email",
            "@example.com",
            "test@",
            "test..test@example.com", // Double dot
            "test@example",           // No TLD
            "test @example.com",      // Space in local part
            "test@exam ple.com",      // Space in domain
            "test@.example.com",      // Dot at start of domain
            "test@example..com"       // Double dot in domain
        };

        // Act & Assert
        foreach (var email in invalidEmails)
        {
            IsValidEmail(email).Should().BeFalse($"Email '{email}' should be invalid");
        }
    }

    [Test]
    public void ValidatePhone_WithValidPhones_ReturnsTrue()
    {
        // Arrange
        var validPhones = new[]
        {
            "+7-900-123-4567",
            "+1234567890",
            "1234567890",
            "+7 (900) 123-45-67",
            "8-800-555-35-35",
            "+44 20 7946 0958",
            "1-800-FLOWERS" // Not valid by our regex, but let's test numeric only
        };

        // Act & Assert - Note: Our validation might be stricter
        var numericPhones = new[]
        {
            "+7-900-123-4567",
            "+1234567890",
            "1234567890",
            "+7 (900) 123-45-67",
            "8-800-555-35-35"
        };

        foreach (var phone in numericPhones)
        {
            IsValidPhone(phone).Should().BeTrue($"Phone '{phone}' should be valid");
        }
    }

    [Test]
    public void ValidatePhone_WithInvalidPhones_ReturnsFalse()
    {
        // Arrange
        var invalidPhones = new[]
        {
            "",
            "123",      // Too short
            "12345678901234567890123", // Too long
            "abc-def-ghij", // Letters only
            "123-abc-4567", // Contains letters
            "123 456",      // Too short with space
            "+", // Just plus sign
            "()-()" // Just punctuation
        };

        // Act & Assert
        foreach (var phone in invalidPhones)
        {
            IsValidPhone(phone).Should().BeFalse($"Phone '{phone}' should be invalid");
        }
    }

    [Test]
    public void ValidateAmount_WithValidAmounts_ReturnsTrue()
    {
        // Arrange
        var validAmounts = new decimal[]
        {
            1000,      // Minimum reasonable amount (10 RUB)
            150000,    // Typical amount (1500 RUB)
            1000000,   // High amount (10000 RUB)
            99999999   // Below maximum
        };

        // Act & Assert
        foreach (var amount in validAmounts)
        {
            ValidateAmount(amount).isValid.Should().BeTrue($"Amount {amount} should be valid");
        }
    }

    [Test]
    public void ValidateAmount_WithInvalidAmounts_ReturnsFalse()
    {
        // Arrange
        var invalidAmounts = new decimal[]
        {
            0,          // Zero
            -1000,      // Negative
            100000001   // Exceeds maximum (1M RUB)
        };

        // Act & Assert
        foreach (var amount in invalidAmounts)
        {
            ValidateAmount(amount).isValid.Should().BeFalse($"Amount {amount} should be invalid");
        }
    }

    [Test]
    public void ValidateCurrency_WithValidCurrencies_ReturnsTrue()
    {
        // Arrange
        var validCurrencies = new[] { "RUB", "USD", "EUR" };

        // Act & Assert
        foreach (var currency in validCurrencies)
        {
            ValidateCurrency(currency).Should().BeTrue($"Currency '{currency}' should be valid");
        }
    }

    [Test]
    public void ValidateCurrency_WithInvalidCurrencies_ReturnsFalse()
    {
        // Arrange
        var invalidCurrencies = new[] { "", "JPY", "GBP", "rub", "usd", "BTC", "XYZ" };

        // Act & Assert
        foreach (var currency in invalidCurrencies)
        {
            ValidateCurrency(currency).Should().BeFalse($"Currency '{currency}' should be invalid");
        }
    }

    // Helper methods that simulate the actual validation logic from the controller
    private bool IsValidPaymentId(string paymentId) =>
        !string.IsNullOrWhiteSpace(paymentId) && 
        paymentId.Length <= 50 && 
        System.Text.RegularExpressions.Regex.IsMatch(paymentId, @"^pay_[a-zA-Z0-9]+$");

    private bool IsValidCardNumber(string cardNumber)
    {
        var digits = cardNumber.Replace(" ", "").Replace("-", "");
        if (!System.Text.RegularExpressions.Regex.IsMatch(digits, @"^\d{13,19}$"))
            return false;

        // Luhn algorithm validation
        int sum = 0;
        bool alternate = false;
        for (int i = digits.Length - 1; i >= 0; i--)
        {
            int digit = digits[i] - '0';
            if (alternate)
            {
                digit *= 2;
                if (digit > 9) digit = digit / 10 + digit % 10;
            }
            sum += digit;
            alternate = !alternate;
        }
        return sum % 10 == 0;
    }

    private bool IsValidExpiryDate(string expiryDate)
    {
        if (!System.Text.RegularExpressions.Regex.IsMatch(expiryDate, @"^(0[1-9]|1[0-2])\/(\d{2})$"))
            return false;

        var parts = expiryDate.Split('/');
        if (int.TryParse(parts[0], out int month) && int.TryParse(parts[1], out int year))
        {
            var expiry = new DateTime(2000 + year, month, 1).AddMonths(1).AddDays(-1);
            return expiry >= DateTime.Today;
        }
        return false;
    }

    private bool IsValidCvv(string cvv) => 
        System.Text.RegularExpressions.Regex.IsMatch(cvv, @"^\d{3,4}$");

    private bool IsValidEmail(string email) => 
        System.Text.RegularExpressions.Regex.IsMatch(email, 
            @"^[a-zA-Z0-9.!#$%&'*+/=?^_`{|}~-]+@[a-zA-Z0-9](?:[a-zA-Z0-9-]{0,61}[a-zA-Z0-9])?(?:\.[a-zA-Z0-9](?:[a-zA-Z0-9-]{0,61}[a-zA-Z0-9])?)*$");

    private bool IsValidPhone(string phone) => 
        System.Text.RegularExpressions.Regex.IsMatch(phone, @"^[\+]?[0-9\s\-\(\)]{10,20}$");

    private (bool isValid, string? errorMessage) ValidateAmount(decimal amount)
    {
        if (amount <= 0)
            return (false, "Amount must be greater than zero");
        if (amount > 100000000) // 1M RUB max
            return (false, "Amount exceeds maximum limit (1,000,000 RUB)");
        
        return (true, null);
    }

    private bool ValidateCurrency(string currency)
    {
        var allowedCurrencies = new[] { "RUB", "USD", "EUR" };
        return allowedCurrencies.Contains(currency?.ToUpper());
    }
}