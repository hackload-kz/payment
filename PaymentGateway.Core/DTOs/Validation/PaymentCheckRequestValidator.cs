using FluentValidation;
using PaymentGateway.Core.DTOs.PaymentCheck;

namespace PaymentGateway.Core.DTOs.Validation;

/// <summary>
/// FluentValidation validator for PaymentCheckRequestDto
/// </summary>
public class PaymentCheckRequestValidator : AbstractValidator<PaymentCheckRequestDto>
{
    public PaymentCheckRequestValidator()
    {
        // TeamSlug validation
        Include(new BaseRequestValidator());

        // PaymentId validation (optional - either PaymentId or OrderId must be provided)
        RuleFor(x => x.PaymentId)
            .MaximumLength(20)
            .WithErrorCode("PAYMENT_ID_TOO_LONG")
            .WithMessage("PaymentId cannot exceed 20 characters")
            .Matches("^[0-9]+$")
            .WithErrorCode("PAYMENT_ID_INVALID_FORMAT")
            .WithMessage("PaymentId must contain only digits")
            .When(x => !string.IsNullOrEmpty(x.PaymentId));

        // OrderId validation (optional - either PaymentId or OrderId must be provided)
        RuleFor(x => x.OrderId)
            .MaximumLength(36)
            .WithErrorCode("ORDER_ID_TOO_LONG")
            .WithMessage("OrderId cannot exceed 36 characters")
            .Matches("^[a-zA-Z0-9_-]+$")
            .WithErrorCode("ORDER_ID_INVALID_FORMAT")
            .WithMessage("OrderId can only contain letters, numbers, hyphens, and underscores")
            .When(x => !string.IsNullOrEmpty(x.OrderId));

        // Custom rule: Either PaymentId or OrderId must be provided
        RuleFor(x => x)
            .Must(x => !string.IsNullOrEmpty(x.PaymentId) || !string.IsNullOrEmpty(x.OrderId))
            .WithErrorCode("PAYMENT_OR_ORDER_ID_REQUIRED")
            .WithMessage("Either PaymentId or OrderId must be provided");

        // Language validation
        RuleFor(x => x.Language)
            .NotEmpty()
            .WithErrorCode("LANGUAGE_REQUIRED")
            .WithMessage("Language is required")
            .Must(language => language == "ru" || language == "en")
            .WithErrorCode("LANGUAGE_INVALID")
            .WithMessage("Language must be 'ru' or 'en'");
    }

}