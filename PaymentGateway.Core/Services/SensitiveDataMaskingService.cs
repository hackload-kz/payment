using System.Text.Json;
using System.Text.RegularExpressions;
using PaymentGateway.Core.Configuration;

namespace PaymentGateway.Core.Services;

public interface ISensitiveDataMaskingService
{
    string MaskSensitiveData(string data);
    string MaskObject(object obj);
    string MaskJsonString(string jsonString);
    Dictionary<string, object> MaskDictionary(Dictionary<string, object> dictionary);
}

public class SensitiveDataMaskingService : ISensitiveDataMaskingService
{
    private readonly AuditConfiguration _auditConfig;
    private readonly Dictionary<string, Regex> _maskingPatterns;

    public SensitiveDataMaskingService(AuditConfiguration auditConfig)
    {
        _auditConfig = auditConfig;
        _maskingPatterns = InitializeMaskingPatterns();
    }

    public string MaskSensitiveData(string data)
    {
        if (!_auditConfig.EnableSensitiveDataEncryption || string.IsNullOrEmpty(data))
            return data;

        var maskedData = data;
        
        // Apply all masking patterns
        foreach (var pattern in _maskingPatterns)
        {
            maskedData = pattern.Value.Replace(maskedData, match =>
            {
                var fieldValue = match.Groups[1].Value;
                return match.Value.Replace(fieldValue, MaskValue(fieldValue));
            });
        }

        return maskedData;
    }

    public string MaskObject(object obj)
    {
        if (obj == null) return string.Empty;

        try
        {
            var jsonString = JsonSerializer.Serialize(obj, new JsonSerializerOptions
            {
                WriteIndented = false,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });

            return MaskJsonString(jsonString);
        }
        catch
        {
            return obj.ToString() ?? string.Empty;
        }
    }

    public string MaskJsonString(string jsonString)
    {
        if (!_auditConfig.EnableSensitiveDataEncryption || string.IsNullOrEmpty(jsonString))
            return jsonString;

        try
        {
            var document = JsonDocument.Parse(jsonString);
            var maskedDict = ProcessJsonElement(document.RootElement);
            return JsonSerializer.Serialize(maskedDict, new JsonSerializerOptions
            {
                WriteIndented = false,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });
        }
        catch
        {
            return MaskSensitiveData(jsonString);
        }
    }

    public Dictionary<string, object> MaskDictionary(Dictionary<string, object> dictionary)
    {
        if (!_auditConfig.EnableSensitiveDataEncryption || dictionary == null)
            return dictionary;

        var maskedDict = new Dictionary<string, object>();

        foreach (var kvp in dictionary)
        {
            var key = kvp.Key;
            var value = kvp.Value;

            if (IsSensitiveField(key))
            {
                maskedDict[key] = MaskValue(value?.ToString() ?? string.Empty);
            }
            else if (value is Dictionary<string, object> nestedDict)
            {
                maskedDict[key] = MaskDictionary(nestedDict);
            }
            else
            {
                maskedDict[key] = value;
            }
        }

        return maskedDict;
    }

    private Dictionary<string, Regex> InitializeMaskingPatterns()
    {
        var patterns = new Dictionary<string, Regex>();

        var sensitiveFields = new[] { "CardNumber", "CVV", "Password", "Token", "TerminalKey" };
        foreach (var field in sensitiveFields)
        {
            // Pattern to match JSON field: "field": "value"
            var jsonPattern = $@"""{field}"":\s*""([^""]*)""|'{field}':\s*'([^']*)'";
            patterns[$"json_{field}"] = new Regex(jsonPattern, RegexOptions.IgnoreCase | RegexOptions.Compiled);

            // Pattern to match query string: field=value
            var queryPattern = $@"{field}=([^&\s]*)";
            patterns[$"query_{field}"] = new Regex(queryPattern, RegexOptions.IgnoreCase | RegexOptions.Compiled);

            // Pattern to match XML: <field>value</field>
            var xmlPattern = $@"<{field}[^>]*>([^<]*)</{field}>";
            patterns[$"xml_{field}"] = new Regex(xmlPattern, RegexOptions.IgnoreCase | RegexOptions.Compiled);
        }

        // Special patterns for card numbers
        patterns["card_number"] = new Regex(@"\b\d{4}[\s-]?\d{4}[\s-]?\d{4}[\s-]?\d{4}\b", RegexOptions.Compiled);
        patterns["card_number_compact"] = new Regex(@"\b\d{13,19}\b", RegexOptions.Compiled);

        return patterns;
    }

    private object ProcessJsonElement(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Object => ProcessJsonObject(element),
            JsonValueKind.Array => ProcessJsonArray(element),
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => element.GetRawText(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            _ => element.GetRawText()
        };
    }

    private Dictionary<string, object> ProcessJsonObject(JsonElement element)
    {
        var result = new Dictionary<string, object>();
        
        foreach (var property in element.EnumerateObject())
        {
            var key = property.Name;
            var value = ProcessJsonElement(property.Value);

            if (IsSensitiveField(key) && value is string stringValue)
            {
                result[key] = MaskValue(stringValue);
            }
            else
            {
                result[key] = value;
            }
        }

        return result;
    }

    private List<object> ProcessJsonArray(JsonElement element)
    {
        var result = new List<object>();
        
        foreach (var item in element.EnumerateArray())
        {
            result.Add(ProcessJsonElement(item));
        }

        return result;
    }

    private bool IsSensitiveField(string fieldName)
    {
        var sensitiveFields = new[] { "CardNumber", "CVV", "Password", "Token", "TerminalKey" };
        return sensitiveFields.Any(sf => 
            string.Equals(sf, fieldName, StringComparison.OrdinalIgnoreCase));
    }

    private string MaskValue(string value)
    {
        if (string.IsNullOrEmpty(value))
            return value;

        if (value.Length <= 4)
            return new string('*', value.Length);

        // For longer values, show first 2 and last 2 characters
        return $"{value[..2]}{new string('*', Math.Max(1, value.Length - 4))}{value[^2..]}";
    }
}