using FluentValidation;
using PaymentGateway.Core.DTOs.Common;

namespace PaymentGateway.Core.DTOs.Validation;

/// <summary>
/// Base FluentValidation validator for common request properties
/// </summary>
public class BaseRequestValidator : AbstractValidator<BaseRequestDto>
{
    public BaseRequestValidator()
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

        // Timestamp validation
        RuleFor(x => x.Timestamp)
            .Must(BeValidTimestamp)
            .WithErrorCode("TIMESTAMP_INVALID")
            .WithMessage("Timestamp must be within acceptable range");

        // CorrelationId validation (optional)
        RuleFor(x => x.CorrelationId)
            .MaximumLength(50)
            .WithErrorCode("CORRELATION_ID_TOO_LONG")
            .WithMessage("CorrelationId cannot exceed 50 characters")
            .When(x => !string.IsNullOrEmpty(x.CorrelationId));
    }

    private bool BeValidTimestamp(DateTime timestamp)
    {
        var now = DateTime.UtcNow;
        var diff = Math.Abs((now - timestamp).TotalMinutes);
        
        // Allow timestamps within 15 minutes of current time to account for clock skew
        return diff <= 15;
    }
}