using FluentValidation;
using PaymentGateway.Core.DTOs.PaymentInit;

namespace PaymentGateway.Core.DTOs.Validation;

/// <summary>
/// FluentValidation validator for PaymentInitRequestDto
/// </summary>
public class PaymentInitRequestValidator : AbstractValidator<PaymentInitRequestDto>
{
    public PaymentInitRequestValidator()
    {
        // TeamSlug validation
        RuleFor(x => x.TeamSlug)
            .NotEmpty()
            .WithErrorCode("TEAM_SLUG_REQUIRED")
            .WithMessage("TeamSlug is required")
            .MaximumLength(50)
            .WithErrorCode("TEAM_SLUG_TOO_LONG")
            .WithMessage("TeamSlug cannot exceed 50 characters")
            .Matches("^[a-zA-Z0-9_-]+$")
            .WithErrorCode("TEAM_SLUG_INVALID_FORMAT")
            .WithMessage("TeamSlug can only contain letters, numbers, hyphens, and underscores");

        // Token validation
        RuleFor(x => x.Token)
            .NotEmpty()
            .WithErrorCode("TOKEN_REQUIRED")
            .WithMessage("Token is required")
            .MaximumLength(256)
            .WithErrorCode("TOKEN_TOO_LONG")
            .WithMessage("Token cannot exceed 256 characters");

        // Amount validation
        RuleFor(x => x.Amount)
            .GreaterThanOrEqualTo(1000)
            .WithErrorCode("AMOUNT_TOO_SMALL")
            .WithMessage("Amount must be at least 1000 kopecks (10 RUB)")
            .LessThanOrEqualTo(50000000)
            .WithErrorCode("AMOUNT_TOO_LARGE")
            .WithMessage("Amount cannot exceed 50000000 kopecks (500000 RUB)");

        // OrderId validation
        RuleFor(x => x.OrderId)
            .NotEmpty()
            .WithErrorCode("ORDER_ID_REQUIRED")
            .WithMessage("OrderId is required")
            .MaximumLength(36)
            .WithErrorCode("ORDER_ID_TOO_LONG")
            .WithMessage("OrderId cannot exceed 36 characters")
            .Matches("^[a-zA-Z0-9_-]+$")
            .WithErrorCode("ORDER_ID_INVALID_FORMAT")
            .WithMessage("OrderId can only contain letters, numbers, hyphens, and underscores");

        // Currency validation
        RuleFor(x => x.Currency)
            .NotEmpty()
            .WithErrorCode("CURRENCY_REQUIRED")
            .WithMessage("Currency is required")
            .Length(3)
            .WithErrorCode("CURRENCY_INVALID_LENGTH")
            .WithMessage("Currency must be exactly 3 characters")
            .Must(BeValidCurrency)
            .WithErrorCode("CURRENCY_NOT_SUPPORTED")
            .WithMessage("Currency is not supported");

        // PayType validation (optional)
        RuleFor(x => x.PayType)
            .Must(x => x == null || x == "O" || x == "T")
            .WithErrorCode("PAY_TYPE_INVALID")
            .WithMessage("PayType must be 'O' (single-stage) or 'T' (two-stage)");

        // Description validation (optional)
        RuleFor(x => x.Description)
            .MaximumLength(140)
            .WithErrorCode("DESCRIPTION_TOO_LONG")
            .WithMessage("Description cannot exceed 140 characters")
            .When(x => !string.IsNullOrEmpty(x.Description));

        // CustomerKey validation (optional)
        RuleFor(x => x.CustomerKey)
            .MaximumLength(36)
            .WithErrorCode("CUSTOMER_KEY_TOO_LONG")
            .WithMessage("CustomerKey cannot exceed 36 characters")
            .When(x => !string.IsNullOrEmpty(x.CustomerKey));

        // Email validation (optional)
        RuleFor(x => x.Email)
            .EmailAddress()
            .WithErrorCode("EMAIL_INVALID_FORMAT")
            .WithMessage("Email format is invalid")
            .MaximumLength(254)
            .WithErrorCode("EMAIL_TOO_LONG")
            .WithMessage("Email cannot exceed 254 characters")
            .When(x => !string.IsNullOrEmpty(x.Email));

        // Phone validation (optional)
        RuleFor(x => x.Phone)
            .Matches(@"^\+?[1-9]\d{6,19}$")
            .WithErrorCode("PHONE_INVALID_FORMAT")
            .WithMessage("Phone must be 7-20 digits with optional leading +")
            .When(x => !string.IsNullOrEmpty(x.Phone));

        // Language validation
        RuleFor(x => x.Language)
            .NotEmpty()
            .WithErrorCode("LANGUAGE_REQUIRED")
            .WithMessage("Language is required")
            .Must(x => x == "ru" || x == "en")
            .WithErrorCode("LANGUAGE_NOT_SUPPORTED")
            .WithMessage("Language must be 'ru' or 'en'");

        // URL validations (optional)
        RuleFor(x => x.SuccessURL)
            .Must(BeValidUrl)
            .WithErrorCode("SUCCESS_URL_INVALID")
            .WithMessage("SuccessURL must be a valid URL")
            .MaximumLength(2048)
            .WithErrorCode("SUCCESS_URL_TOO_LONG")
            .WithMessage("SuccessURL cannot exceed 2048 characters")
            .When(x => !string.IsNullOrEmpty(x.SuccessURL));

        RuleFor(x => x.FailURL)
            .Must(BeValidUrl)
            .WithErrorCode("FAIL_URL_INVALID")
            .WithMessage("FailURL must be a valid URL")
            .MaximumLength(2048)
            .WithErrorCode("FAIL_URL_TOO_LONG")
            .WithMessage("FailURL cannot exceed 2048 characters")
            .When(x => !string.IsNullOrEmpty(x.FailURL));

        RuleFor(x => x.NotificationURL)
            .Must(BeValidUrl)
            .WithErrorCode("NOTIFICATION_URL_INVALID")
            .WithMessage("NotificationURL must be a valid URL")
            .MaximumLength(2048)
            .WithErrorCode("NOTIFICATION_URL_TOO_LONG")
            .WithMessage("NotificationURL cannot exceed 2048 characters")
            .When(x => !string.IsNullOrEmpty(x.NotificationURL));

        // PaymentExpiry validation
        RuleFor(x => x.PaymentExpiry)
            .GreaterThan(0)
            .WithErrorCode("PAYMENT_EXPIRY_INVALID")
            .WithMessage("PaymentExpiry must be greater than 0")
            .LessThanOrEqualTo(43200)
            .WithErrorCode("PAYMENT_EXPIRY_TOO_LONG")
            .WithMessage("PaymentExpiry cannot exceed 43200 minutes (30 days)");

        // Items validation (optional)
        RuleForEach(x => x.Items)
            .SetValidator(new OrderItemValidator())
            .When(x => x.Items != null && x.Items.Any());

        // Custom business rules
        RuleFor(x => x)
            .Must(HaveValidItemsTotal)
            .WithErrorCode("ITEMS_TOTAL_MISMATCH")
            .WithMessage("Sum of item amounts must equal payment amount")
            .When(x => x.Items != null && x.Items.Any());

        RuleFor(x => x.Data)
            .Must(HaveValidDataDictionary)
            .WithErrorCode("DATA_INVALID")
            .WithMessage("Data dictionary contains invalid entries")
            .When(x => x.Data != null);
    }

    private bool BeValidCurrency(string currency)
    {
        if (string.IsNullOrEmpty(currency))
            return false;

        var supportedCurrencies = new[] { "KZT", "USD", "EUR", "BYN", "RUB" };
        return supportedCurrencies.Contains(currency.ToUpperInvariant());
    }

    private bool BeValidUrl(string? url)
    {
        if (string.IsNullOrEmpty(url))
            return true;

        return Uri.TryCreate(url, UriKind.Absolute, out var uri) && 
               (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
    }

    private bool HaveValidItemsTotal(PaymentInitRequestDto dto)
    {
        if (dto.Items == null || !dto.Items.Any())
            return true;

        var totalItemsAmount = dto.Items.Sum(item => item.Amount);
        return Math.Abs(totalItemsAmount - dto.Amount) < 0.01m; // Allow for small rounding differences
    }

    private bool HaveValidDataDictionary(Dictionary<string, string>? data)
    {
        if (data == null)
            return true;

        // Check for valid key-value pairs
        foreach (var kvp in data)
        {
            if (string.IsNullOrEmpty(kvp.Key) || kvp.Key.Length > 50)
                return false;
            if (kvp.Value != null && kvp.Value.Length > 1000)
                return false;
        }

        return true;
    }
}

/// <summary>
/// FluentValidation validator for OrderItemDto
/// </summary>
public class OrderItemValidator : AbstractValidator<OrderItemDto>
{
    public OrderItemValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty()
            .WithErrorCode("ITEM_NAME_REQUIRED")
            .WithMessage("Item name is required")
            .MaximumLength(100)
            .WithErrorCode("ITEM_NAME_TOO_LONG")
            .WithMessage("Item name cannot exceed 100 characters");

        RuleFor(x => x.Quantity)
            .GreaterThan(0)
            .WithErrorCode("ITEM_QUANTITY_INVALID")
            .WithMessage("Item quantity must be greater than 0")
            .LessThanOrEqualTo(99999.999m)
            .WithErrorCode("ITEM_QUANTITY_TOO_LARGE")
            .WithMessage("Item quantity cannot exceed 99999.999");

        RuleFor(x => x.Price)
            .GreaterThan(0)
            .WithErrorCode("ITEM_PRICE_INVALID")
            .WithMessage("Item price must be greater than 0");

        RuleFor(x => x.Amount)
            .GreaterThan(0)
            .WithErrorCode("ITEM_AMOUNT_INVALID")
            .WithMessage("Item amount must be greater than 0");

        RuleFor(x => x.Category)
            .MaximumLength(50)
            .WithErrorCode("ITEM_CATEGORY_TOO_LONG")
            .WithMessage("Item category cannot exceed 50 characters")
            .When(x => !string.IsNullOrEmpty(x.Category));

        // Business rule: Amount should equal Quantity * Price
        RuleFor(x => x)
            .Must(x => Math.Abs(x.Amount - (x.Quantity * x.Price)) < 0.01m)
            .WithErrorCode("ITEM_AMOUNT_CALCULATION_MISMATCH")
            .WithMessage("Item amount must equal quantity * price");
    }
}