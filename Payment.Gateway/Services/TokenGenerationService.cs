using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Payment.Gateway.Services;

public class TokenGenerationService : ITokenGenerationService
{
    public string GenerateToken(IDictionary<string, object> parameters, string password)
    {
        // Step 1: Extract root-level parameters (exclude nested objects/arrays)
        var tokenParams = new Dictionary<string, string>();
        
        foreach (var kvp in parameters)
        {
            // Only include scalar values, exclude complex objects and arrays
            if (kvp.Value != null && !IsComplexType(kvp.Value))
            {
                tokenParams[kvp.Key] = ConvertToString(kvp.Value);
            }
        }
        
        // Step 2: Add merchant password
        tokenParams["Password"] = password;
        
        // Step 3: Sort parameters alphabetically by key
        var sortedKeys = tokenParams.Keys.OrderBy(k => k).ToArray();
        
        // Step 4: Concatenate values in sorted order
        var concatenated = string.Join("", sortedKeys.Select(key => tokenParams[key]));
        
        // Step 5: Generate SHA-256 hash with UTF-8 encoding
        using var sha256 = SHA256.Create();
        var hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(concatenated));
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }

    public bool ValidateToken(IDictionary<string, object> parameters, string token, string password)
    {
        var generatedToken = GenerateToken(parameters, password);
        return string.Equals(generatedToken, token, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsComplexType(object value)
    {
        return value is JsonElement jsonElement
            ? jsonElement.ValueKind == JsonValueKind.Object || jsonElement.ValueKind == JsonValueKind.Array
            : value.GetType().IsClass && value.GetType() != typeof(string);
    }

    private static string ConvertToString(object value)
    {
        return value switch
        {
            string str => str,
            bool boolean => boolean.ToString().ToLowerInvariant(),
            JsonElement jsonElement => jsonElement.ValueKind switch
            {
                JsonValueKind.String => jsonElement.GetString() ?? "",
                JsonValueKind.Number => jsonElement.ToString(),
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                JsonValueKind.Null => "",
                _ => jsonElement.ToString()
            },
            null => "",
            _ => value.ToString() ?? ""
        };
    }
}