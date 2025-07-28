using FluentValidation;
using Payment.Gateway.DTOs;

namespace Payment.Gateway.Validators;

public class InitPaymentRequestValidator : AbstractValidator<InitPaymentRequest>
{
    public InitPaymentRequestValidator()
    {
        // Core Payment Parameters
        RuleFor(x => x.TerminalKey)
            .NotEmpty()
            .MaximumLength(20)
            .WithMessage("TerminalKey is required and must be ≤20 characters");

        RuleFor(x => x.Amount)
            .GreaterThanOrEqualTo(1000)
            .WithMessage("Amount must be at least 1000 kopecks (10 RUB)")
            .Must(amount => amount.ToString().Length <= 10)
            .WithMessage("Amount must be ≤10 characters");

        RuleFor(x => x.OrderId)
            .NotEmpty()
            .MaximumLength(36)
            .WithMessage("OrderId is required and must be ≤36 characters");

        RuleFor(x => x.Token)
            .NotEmpty()
            .WithMessage("Token is required");

        // Optional Payment Configuration
        RuleFor(x => x.PayType)
            .Must(payType => payType == null || payType == "O" || payType == "T")
            .WithMessage("PayType must be 'O' (single-stage) or 'T' (two-stage)")
            .MaximumLength(1);

        RuleFor(x => x.Description)
            .MaximumLength(140)
            .WithMessage("Description must be ≤140 characters");

        // Customer Management
        RuleFor(x => x.CustomerKey)
            .MaximumLength(36)
            .WithMessage("CustomerKey must be ≤36 characters");

        RuleFor(x => x.Recurrent)
            .Must(recurrent => recurrent == null || recurrent == "Y" || recurrent == "N")
            .WithMessage("Recurrent must be 'Y' or 'N'")
            .MaximumLength(1);

        // CustomerKey is required if Recurrent = Y
        RuleFor(x => x.CustomerKey)
            .NotEmpty()
            .When(x => x.Recurrent == "Y")
            .WithMessage("CustomerKey is required when Recurrent = 'Y'");

        // Localization
        RuleFor(x => x.Language)
            .Must(lang => lang == null || lang == "ru" || lang == "en")
            .WithMessage("Language must be 'ru' or 'en'")
            .MaximumLength(2);

        // URL Configuration
        RuleFor(x => x.NotificationURL)
            .Must(BeValidUrl)
            .When(x => !string.IsNullOrEmpty(x.NotificationURL))
            .WithMessage("NotificationURL must be a valid URI");

        RuleFor(x => x.SuccessURL)
            .Must(BeValidUrl)
            .When(x => !string.IsNullOrEmpty(x.SuccessURL))
            .WithMessage("SuccessURL must be a valid URI");

        RuleFor(x => x.FailURL)
            .Must(BeValidUrl)
            .When(x => !string.IsNullOrEmpty(x.FailURL))
            .WithMessage("FailURL must be a valid URI");

        // Session Management
        RuleFor(x => x.RedirectDueDate)
            .Must(BeValidRedirectDueDate)
            .When(x => x.RedirectDueDate.HasValue)
            .WithMessage("RedirectDueDate must be between 1 minute and 90 days from now");

        // DATA object validation
        RuleFor(x => x.DATA)
            .Must(data => data == null || data.Count <= 20)
            .WithMessage("DATA object must contain at most 20 key-value pairs");

        RuleForEach(x => x.DATA)
            .Must(kvp => kvp.Key.Length <= 20)
            .WithMessage("DATA keys must be ≤20 characters")
            .Must(kvp => kvp.Value.Length <= 100)
            .WithMessage("DATA values must be ≤100 characters")
            .When(x => x.DATA != null);

        // Special DATA parameters validation
        RuleFor(x => x.DATA)
            .Must(ValidatePhoneParameter)
            .When(x => x.DATA != null && x.DATA.ContainsKey("Phone"))
            .WithMessage("Phone parameter must be 7-20 digits, optional leading +");

        RuleFor(x => x.DATA)
            .Must(ValidateAccountParameter)
            .When(x => x.DATA != null && x.DATA.ContainsKey("account"))
            .WithMessage("Account parameter must be ≤30 characters");
    }

    private static bool BeValidUrl(string? url)
    {
        if (string.IsNullOrEmpty(url)) return true;
        return Uri.TryCreate(url, UriKind.Absolute, out _);
    }

    private static bool BeValidRedirectDueDate(DateTime? redirectDueDate)
    {
        if (!redirectDueDate.HasValue) return true;
        
        var now = DateTime.UtcNow;
        var minDate = now.AddMinutes(1);
        var maxDate = now.AddDays(90);
        
        return redirectDueDate >= minDate && redirectDueDate <= maxDate;
    }

    private static bool ValidatePhoneParameter(Dictionary<string, string>? data)
    {
        if (data == null || !data.TryGetValue("Phone", out var phone)) return true;
        
        // Remove optional leading +
        var phoneDigits = phone.StartsWith("+") ? phone[1..] : phone;
        
        // Check if all characters are digits and length is 7-20
        return phoneDigits.All(char.IsDigit) && phoneDigits.Length >= 7 && phoneDigits.Length <= 20;
    }

    private static bool ValidateAccountParameter(Dictionary<string, string>? data)
    {
        if (data == null || !data.TryGetValue("account", out var account)) return true;
        
        return account.Length <= 30;
    }
}