using FluentValidation;
using Payment.Gateway.DTOs;

namespace Payment.Gateway.Validators;

public class ConfirmPaymentRequestValidator : AbstractValidator<ConfirmPaymentRequest>
{
    public ConfirmPaymentRequestValidator()
    {
        // Required Parameters
        RuleFor(x => x.TerminalKey)
            .NotEmpty()
            .MaximumLength(20)
            .WithMessage("TerminalKey is required and must be ≤20 characters");

        RuleFor(x => x.PaymentId)
            .NotEmpty()
            .MaximumLength(20)
            .WithMessage("PaymentId is required and must be ≤20 characters");

        RuleFor(x => x.Token)
            .NotEmpty()
            .WithMessage("Token is required");

        // Optional Parameters
        RuleFor(x => x.Amount)
            .GreaterThan(0)
            .When(x => x.Amount.HasValue)
            .WithMessage("Amount must be greater than 0 when provided");

        RuleFor(x => x.Route)
            .Must(route => route == null || route == "TCB" || route == "BNPL")
            .WithMessage("Route must be 'TCB' or 'BNPL'");

        RuleFor(x => x.Source)
            .Must(source => source == null || source == "installment" || source == "BNPL")
            .WithMessage("Source must be 'installment' or 'BNPL'");

        // IP validation (basic format check)
        RuleFor(x => x.IP)
            .Must(BeValidIPAddress)
            .When(x => !string.IsNullOrEmpty(x.IP))
            .WithMessage("IP must be a valid IP address");
    }

    private static bool BeValidIPAddress(string? ip)
    {
        if (string.IsNullOrEmpty(ip)) return true;
        
        // Basic IP validation - could be IPv4 or IPv6
        return System.Net.IPAddress.TryParse(ip, out _);
    }
}