using System.Text.RegularExpressions;

namespace PaymentGateway.Core.Services;

public interface ISensitiveDataMaskingService
{
    string MaskSensitiveData(string input);
    bool ContainsSensitiveData(string input);
    Dictionary<string, object> MaskSensitiveProperties(Dictionary<string, object> properties);
}

public class SensitiveDataMaskingService : ISensitiveDataMaskingService
{
    private readonly HashSet<string> _sensitiveFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "CardNumber",
        "CVV", 
        "Password",
        "Token",
        "TerminalKey",
        "TeamSlug",
        "PIN",
        "SecurityCode"
    };

    private readonly Dictionary<string, Regex> _sensitivePatterns = new()
    {
        { "CardNumber", new Regex(@"\d{4}[\s-]?\d{4}[\s-]?\d{4}[\s-]?\d{4}", RegexOptions.Compiled) },
        { "CVV", new Regex(@"\b\d{3,4}\b", RegexOptions.Compiled) },
        { "Email", new Regex(@"\b[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Z|a-z]{2,}\b", RegexOptions.Compiled) }
    };

    public string MaskSensitiveData(string input)
    {
        if (string.IsNullOrEmpty(input))
            return input;

        var result = input;

        foreach (var pattern in _sensitivePatterns)
        {
            result = pattern.Value.Replace(result, match =>
            {
                return pattern.Key switch
                {
                    "CardNumber" => MaskCardNumber(match.Value),
                    "CVV" => "***",
                    "Email" => MaskEmail(match.Value),
                    _ => "***"
                };
            });
        }

        return result;
    }

    public bool ContainsSensitiveData(string input)
    {
        if (string.IsNullOrEmpty(input))
            return false;

        return _sensitiveFields.Any(field => 
            input.Contains(field, StringComparison.OrdinalIgnoreCase)) ||
            _sensitivePatterns.Values.Any(pattern => pattern.IsMatch(input));
    }

    public Dictionary<string, object> MaskSensitiveProperties(Dictionary<string, object> properties)
    {
        if (properties == null)
            return properties;

        var maskedProperties = new Dictionary<string, object>();

        foreach (var kvp in properties)
        {
            if (_sensitiveFields.Contains(kvp.Key))
            {
                maskedProperties[kvp.Key] = MaskValue(kvp.Value);
            }
            else
            {
                maskedProperties[kvp.Key] = kvp.Value;
            }
        }

        return maskedProperties;
    }

    private string MaskCardNumber(string cardNumber)
    {
        if (string.IsNullOrEmpty(cardNumber))
            return cardNumber;

        var digits = cardNumber.Replace(" ", "").Replace("-", "");
        if (digits.Length < 8)
            return "****";

        return $"{digits[..4]}****{digits[^4..]}";
    }

    private string MaskEmail(string email)
    {
        if (string.IsNullOrEmpty(email))
            return email;

        var parts = email.Split('@');
        if (parts.Length != 2)
            return "***@***.***";

        var localPart = parts[0];
        var domainPart = parts[1];

        var maskedLocal = localPart.Length > 2 ? 
            $"{localPart[0]}***{localPart[^1]}" : 
            "***";

        return $"{maskedLocal}@{domainPart}";
    }

    private object MaskValue(object value)
    {
        return value switch
        {
            string str => MaskSensitiveData(str),
            _ => "***"
        };
    }
}