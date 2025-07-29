using FluentValidation;
using PaymentGateway.Core.DTOs.PaymentCancel;

namespace PaymentGateway.Core.DTOs.Validation;

/// <summary>
/// FluentValidation validator for PaymentCancelRequestDto
/// </summary>
public class PaymentCancelRequestValidator : AbstractValidator<PaymentCancelRequestDto>
{
    public PaymentCancelRequestValidator()
    {
        // TeamSlug validation
        Include(new BaseRequestValidator());

        // PaymentId validation
        RuleFor(x => x.PaymentId)
            .NotEmpty()
            .WithErrorCode("PAYMENT_ID_REQUIRED")
            .WithMessage("PaymentId is required")
            .MaximumLength(20)
            .WithErrorCode("PAYMENT_ID_TOO_LONG")
            .WithMessage("PaymentId cannot exceed 20 characters")
            .Matches("^[0-9]+$")
            .WithErrorCode("PAYMENT_ID_INVALID_FORMAT")
            .WithMessage("PaymentId must contain only digits");

        // Amount validation (for full refund amount check)
        RuleFor(x => x.Amount)
            .GreaterThan(0)
            .WithErrorCode("AMOUNT_INVALID")
            .WithMessage("Amount must be greater than 0")
            .When(x => x.Amount.HasValue);

        // Reason validation (optional)
        RuleFor(x => x.Reason)
            .MaximumLength(500)
            .WithErrorCode("REASON_TOO_LONG")
            .WithMessage("Reason cannot exceed 500 characters")
            .When(x => !string.IsNullOrEmpty(x.Reason));

        // Receipt validation (optional)
        RuleFor(x => x.Receipt)
            .NotNull()
            .WithErrorCode("RECEIPT_REQUIRED")
            .WithMessage("Receipt is required for refunds")
            .When(x => x.Amount.HasValue);

        // Amount validation (optional)
        RuleFor(x => x.Amount)
            .GreaterThan(0)
            .WithErrorCode("AMOUNT_INVALID")
            .WithMessage("Amount must be greater than 0")
            .When(x => x.Amount.HasValue);
    }

}