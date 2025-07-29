using FluentValidation;
using PaymentGateway.Core.DTOs.PaymentInit;

namespace PaymentGateway.Core.DTOs.Validation;

/// <summary>
/// FluentValidation validator for ReceiptDto
/// </summary>
public class ReceiptValidator : AbstractValidator<ReceiptDto>
{
    public ReceiptValidator()
    {
        // Taxation validation (optional)
        RuleFor(x => x.Taxation)
            .Must(BeValidTaxationType)
            .WithErrorCode("TAXATION_INVALID")
            .WithMessage("Taxation must be a valid taxation system type")
            .When(x => !string.IsNullOrEmpty(x.Taxation));

        // Email validation (optional)
        RuleFor(x => x.Email)
            .EmailAddress()
            .WithErrorCode("RECEIPT_EMAIL_INVALID_FORMAT")
            .WithMessage("Receipt email format is invalid")
            .MaximumLength(254)
            .WithErrorCode("RECEIPT_EMAIL_TOO_LONG")
            .WithMessage("Receipt email cannot exceed 254 characters")
            .When(x => !string.IsNullOrEmpty(x.Email));

        // Phone validation (optional)
        RuleFor(x => x.Phone)
            .Matches(@"^\+?[1-9]\d{6,19}$")
            .WithErrorCode("RECEIPT_PHONE_INVALID_FORMAT")
            .WithMessage("Receipt phone must be 7-20 digits with optional leading +")
            .When(x => !string.IsNullOrEmpty(x.Phone));

        // Custom rule: Either email or phone must be provided
        RuleFor(x => x)
            .Must(x => !string.IsNullOrEmpty(x.Email) || !string.IsNullOrEmpty(x.Phone))
            .WithErrorCode("RECEIPT_CONTACT_REQUIRED")
            .WithMessage("Either receipt email or phone must be provided");

        // Receipt data validation
        RuleFor(x => x)
            .Must(HaveValidReceiptData)
            .WithErrorCode("RECEIPT_DATA_REQUIRED")
            .WithMessage("At least one receipt format (FFD 1.05 or FFD 1.2) must be provided");
    }

    private bool BeValidTaxationType(string? taxation)
    {
        if (string.IsNullOrEmpty(taxation))
            return true;

        var validTaxationTypes = new[]
        {
            "osn", "usn_income", "usn_income_outcome", "envd", "esn", "patent"
        };

        return validTaxationTypes.Contains(taxation.ToLowerInvariant());
    }

    private bool HaveValidReceiptData(ReceiptDto receipt)
    {
        return receipt.Receipt_FFD_105 != null || receipt.Receipt_FFD_12 != null;
    }
}