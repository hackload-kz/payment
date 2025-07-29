using FluentValidation;
using PaymentGateway.Core.DTOs.PaymentInit;
using PaymentGateway.Core.DTOs.PaymentConfirm;
using PaymentGateway.Core.DTOs.PaymentCancel;

namespace PaymentGateway.Core.Validation.CrossField;

/// <summary>
/// Cross-field validation rules for payment operations
/// </summary>
public static class CrossFieldValidationRules
{
    /// <summary>
    /// Validates that payment amounts are consistent across all related fields
    /// </summary>
    public static IRuleBuilderOptions<T, T> MustHaveConsistentAmounts<T>(this IRuleBuilder<T, T> ruleBuilder)
        where T : PaymentInitRequestDto
    {
        return ruleBuilder.Must(request =>
        {
            // If items are provided, their total must match the payment amount
            if (request.Items != null && request.Items.Any())
            {
                var itemsTotal = request.Items.Sum(item => item.Amount);
                var tolerance = 0.01m; // Allow for small rounding differences
                
                if (Math.Abs(itemsTotal - request.Amount) > tolerance)
                    return false;
            }

            // Validate individual item calculations
            if (request.Items != null)
            {
                foreach (var item in request.Items)
                {
                    var calculatedAmount = item.Quantity * item.Price;
                    if (Math.Abs(calculatedAmount - item.Amount) > 0.01m)
                        return false;
                }
            }

            return true;
        })
        .WithErrorCode("AMOUNT_CONSISTENCY_VIOLATION")
        .WithMessage("Payment amounts are not consistent across all fields");
    }

    /// <summary>
    /// Validates that receipt information is consistent with payment data
    /// </summary>
    public static IRuleBuilderOptions<T, T> MustHaveConsistentReceiptData<T>(this IRuleBuilder<T, T> ruleBuilder)
        where T : PaymentInitRequestDto
    {
        return ruleBuilder.Must(request =>
        {
            if (request.Receipt == null)
                return true;

            // Receipt email should match customer email if both provided
            if (!string.IsNullOrEmpty(request.Email) && 
                !string.IsNullOrEmpty(request.Receipt.Email) &&
                !request.Email.Equals(request.Receipt.Email, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            // Receipt phone should match customer phone if both provided
            if (!string.IsNullOrEmpty(request.Phone) && 
                !string.IsNullOrEmpty(request.Receipt.Phone) &&
                !request.Phone.Equals(request.Receipt.Phone))
            {
                return false;
            }

            return true;
        })
        .WithErrorCode("RECEIPT_DATA_INCONSISTENT")
        .WithMessage("Receipt contact information must match customer contact information");
    }

    /// <summary>
    /// Validates that callback URLs are consistent and properly configured
    /// </summary>
    public static IRuleBuilderOptions<T, T> MustHaveConsistentCallbackConfiguration<T>(this IRuleBuilder<T, T> ruleBuilder)
        where T : PaymentInitRequestDto
    {
        return ruleBuilder.Must(request =>
        {
            var urls = new[] { request.SuccessURL, request.FailURL, request.NotificationURL }
                .Where(url => !string.IsNullOrEmpty(url))
                .ToList();

            if (!urls.Any())
                return true; // No URLs provided is valid

            // All URLs should use the same protocol (HTTP/HTTPS)
            var protocols = urls.Select(url => Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.Scheme : null)
                .Where(scheme => scheme != null)
                .Distinct()
                .ToList();

            if (protocols.Count > 1)
                return false; // Mixed protocols not allowed

            // All URLs should belong to the same domain for security
            var domains = urls.Select(url => Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.Host : null)
                .Where(host => host != null)
                .Distinct()
                .ToList();

            if (domains.Count > 1)
                return false; // Multiple domains not allowed

            return true;
        })
        .WithErrorCode("CALLBACK_URLS_INCONSISTENT")
        .WithMessage("Callback URLs must use consistent protocol and domain");
    }

    /// <summary>
    /// Validates currency and amount relationships
    /// </summary>
    public static IRuleBuilderOptions<T, T> MustHaveValidCurrencyAmountRelationship<T>(this IRuleBuilder<T, T> ruleBuilder)
        where T : PaymentInitRequestDto
    {
        return ruleBuilder.Must(request =>
        {
            // Define minimum amounts per currency (in minor units)
            var minimumAmounts = new Dictionary<string, decimal>
            {
                { "RUB", 1000 },  // 10.00 RUB
                { "USD", 100 },   // 1.00 USD
                { "EUR", 100 },   // 1.00 EUR
                { "KZT", 10000 }, // 100.00 KZT
                { "BYN", 300 }    // 3.00 BYN
            };

            // Define maximum amounts per currency (in minor units)
            var maximumAmounts = new Dictionary<string, decimal>
            {
                { "RUB", 50000000 },  // 500,000.00 RUB
                { "USD", 1000000 },   // 10,000.00 USD  
                { "EUR", 1000000 },   // 10,000.00 EUR
                { "KZT", 100000000 }, // 1,000,000.00 KZT
                { "BYN", 5000000 }    // 50,000.00 BYN
            };

            var currency = request.Currency?.ToUpperInvariant();
            if (string.IsNullOrEmpty(currency))
                return false;

            if (minimumAmounts.TryGetValue(currency, out var minAmount) && request.Amount < minAmount)
                return false;

            if (maximumAmounts.TryGetValue(currency, out var maxAmount) && request.Amount > maxAmount)
                return false;

            return true;
        })
        .WithErrorCode("CURRENCY_AMOUNT_RELATIONSHIP_INVALID")
        .WithMessage("Payment amount is not valid for the specified currency");
    }

    /// <summary>
    /// Validates payment type and related field consistency
    /// </summary>
    public static IRuleBuilderOptions<T, T> MustHaveConsistentPaymentTypeConfiguration<T>(this IRuleBuilder<T, T> ruleBuilder)
        where T : PaymentInitRequestDto
    {
        return ruleBuilder.Must(request =>
        {
            // Two-stage payments (PayType = "T") have different requirements
            if (request.PayType == "T")
            {
                // Two-stage payments should have longer expiry times
                if (request.PaymentExpiry < 60) // Less than 1 hour
                    return false;

                // Two-stage payments typically require notification URLs
                if (string.IsNullOrEmpty(request.NotificationURL))
                    return false;
            }

            // Single-stage payments (PayType = "O") validation
            if (request.PayType == "O")
            {
                // Single-stage payments can have shorter expiry times
                if (request.PaymentExpiry > 1440) // More than 24 hours
                    return false;
            }

            return true;
        })
        .WithErrorCode("PAYMENT_TYPE_CONFIGURATION_INCONSISTENT")
        .WithMessage("Payment configuration is not consistent with the specified payment type");
    }

    /// <summary>
    /// Validates customer data consistency across all fields
    /// </summary>
    public static IRuleBuilderOptions<T, T> MustHaveConsistentCustomerData<T>(this IRuleBuilder<T, T> ruleBuilder)
        where T : PaymentInitRequestDto
    {
        return ruleBuilder.Must(request =>
        {
            // If customer key is provided, other customer data should be consistent
            if (!string.IsNullOrEmpty(request.CustomerKey))
            {
                // Customer key suggests a registered customer, so contact info should be provided
                if (string.IsNullOrEmpty(request.Email) && string.IsNullOrEmpty(request.Phone))
                    return false;
            }

            // Email and phone validation consistency
            if (!string.IsNullOrEmpty(request.Email))
            {
                // Email domain should be reasonable (not obviously fake)
                var emailParts = request.Email.Split('@');
                if (emailParts.Length == 2)
                {
                    var domain = emailParts[1].ToLowerInvariant();
                    var suspiciousDomains = new[] { "test.com", "example.com", "fake.com", "temp.com" };
                    if (suspiciousDomains.Contains(domain))
                        return false;
                }
            }

            return true;
        })
        .WithErrorCode("CUSTOMER_DATA_INCONSISTENT")
        .WithMessage("Customer data fields are not consistent with each other");
    }

    /// <summary>
    /// Validates language and localization consistency
    /// </summary>
    public static IRuleBuilderOptions<T, T> MustHaveConsistentLocalizationData<T>(this IRuleBuilder<T, T> ruleBuilder)
        where T : PaymentInitRequestDto
    {
        return ruleBuilder.Must(request =>
        {
            // Phone number format should be consistent with language/locale
            if (!string.IsNullOrEmpty(request.Phone) && !string.IsNullOrEmpty(request.Language))
            {
                if (request.Language == "ru")
                {
                    // Russian phone numbers should start with +7 or 8
                    if (request.Phone.StartsWith("+7") || request.Phone.StartsWith("8"))
                        return true;
                    // Allow other formats for international customers
                }
            }

            // Description language should be appropriate for the specified language
            if (!string.IsNullOrEmpty(request.Description) && !string.IsNullOrEmpty(request.Language))
            {
                if (request.Language == "ru")
                {
                    // Check for Cyrillic characters (basic check)
                    var hasCyrillic = request.Description.Any(c => c >= 0x0400 && c <= 0x04FF);
                    var hasLatin = request.Description.Any(c => (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z'));
                    
                    // Allow mixed scripts but warn about inconsistency
                    if (hasLatin && !hasCyrillic && request.Description.Length > 10)
                        return false; // Likely English description with Russian language setting
                }
            }

            return true;
        })
        .WithErrorCode("LOCALIZATION_DATA_INCONSISTENT")
        .WithMessage("Language setting is not consistent with other localized data");
    }
}

/// <summary>
/// Cross-field validation extensions for payment confirmation
/// </summary>
public static class PaymentConfirmCrossFieldValidationRules
{
    /// <summary>
    /// Validates that confirmation data is consistent with original payment
    /// </summary>
    public static IRuleBuilderOptions<T, T> MustBeConsistentWithOriginalPayment<T>(this IRuleBuilder<T, T> ruleBuilder)
        where T : PaymentConfirmRequestDto
    {
        return ruleBuilder.Must(request =>
        {
            // This would typically fetch the original payment and validate consistency
            // For now, we'll do basic validation
            
            if (request.Amount.HasValue && request.Amount.Value <= 0)
                return false;

            // Receipt data consistency if provided
            if (request.Receipt != null)
            {
                if (request.Receipt.Receipt_FFD_105 == null && request.Receipt.Receipt_FFD_12 == null)
                    return false;
            }

            return true;
        })
        .WithErrorCode("CONFIRMATION_DATA_INCONSISTENT")
        .WithMessage("Confirmation data is not consistent with the original payment");
    }
}

/// <summary>
/// Cross-field validation extensions for payment cancellation
/// </summary>
public static class PaymentCancelCrossFieldValidationRules
{
    /// <summary>
    /// Validates that cancellation data is consistent and reasonable
    /// </summary>
    public static IRuleBuilderOptions<T, T> MustHaveValidCancellationData<T>(this IRuleBuilder<T, T> ruleBuilder)
        where T : PaymentCancelRequestDto
    {
        return ruleBuilder.Must(request =>
        {
            // Amount validation
            if (request.Amount.HasValue && request.Amount.Value <= 0)
                return false;

            // Reason validation for high-value refunds
            if (request.Amount.HasValue && request.Amount.Value > 100000 && string.IsNullOrEmpty(request.Reason))
                return false; // High-value refunds require reasons

            // Receipt consistency
            if (request.Receipt != null)
            {
                if (request.Receipt.Receipt_FFD_105 == null && request.Receipt.Receipt_FFD_12 == null)
                    return false;
            }

            return true;
        })
        .WithErrorCode("CANCELLATION_DATA_INVALID")
        .WithMessage("Cancellation data is not valid or consistent");
    }
}