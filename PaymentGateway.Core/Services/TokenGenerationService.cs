using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace PaymentGateway.Core.Services;

public interface ITokenGenerationService
{
    string GenerateToken(Dictionary<string, object> requestParameters, string password);
    bool ValidateToken(Dictionary<string, object> requestParameters, string password, string providedToken);
    Task<string> GenerateTokenAsync(Dictionary<string, object> requestParameters, string password);
    Task<bool> ValidateTokenAsync(Dictionary<string, object> requestParameters, string password, string providedToken);
    string ExtractConcatenatedString(Dictionary<string, object> requestParameters, string password);
}

public class TokenGenerationOptions
{
    public bool EnableTokenLogging { get; set; } = false;
    public bool EnableDebugMode { get; set; } = false;
    public List<string> ExcludedParameters { get; set; } = new() { "Token", "Receipt", "DICT" };
    public bool ValidateParameterTypes { get; set; } = true;
}

public class TokenGenerationService : ITokenGenerationService
{
    private readonly ILogger<TokenGenerationService> _logger;
    private readonly TokenGenerationOptions _options;

    // Cached UTF-8 encoding instance for performance
    private static readonly UTF8Encoding Utf8Encoding = new(false, true);

    public TokenGenerationService(
        ILogger<TokenGenerationService> logger,
        IOptions<TokenGenerationOptions> options)
    {
        _logger = logger;
        _options = options.Value;
    }

    public string GenerateToken(Dictionary<string, object> requestParameters, string password)
    {
        ArgumentNullException.ThrowIfNull(requestParameters);
        ArgumentException.ThrowIfNullOrEmpty(password);

        try
        {
            // Step 1: Extract root-level scalar parameters only
            var tokenParameters = ExtractRootLevelScalarParameters(requestParameters);

            // Step 2: Add password to parameters
            tokenParameters["Password"] = password;

            // Step 3: Sort parameters alphabetically by key
            var sortedKeys = tokenParameters.Keys.OrderBy(k => k, StringComparer.Ordinal).ToList();

            // Step 4: Concatenate values in sorted order
            var concatenatedString = string.Concat(sortedKeys.Select(key => tokenParameters[key]));

            // Step 5: Generate SHA-256 hash
            var token = ComputeSha256Hash(concatenatedString);

            if (_options.EnableDebugMode)
            {
                _logger.LogDebug("Token generation completed for {ParameterCount} parameters", 
                    tokenParameters.Count - 1); // Exclude password from count
            }

            return token;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate token for request parameters");
            throw new InvalidOperationException("Token generation failed", ex);
        }
    }

    public bool ValidateToken(Dictionary<string, object> requestParameters, string password, string providedToken)
    {
        ArgumentNullException.ThrowIfNull(requestParameters);
        ArgumentException.ThrowIfNullOrEmpty(password);
        ArgumentException.ThrowIfNullOrEmpty(providedToken);

        try
        {
            var expectedToken = GenerateToken(requestParameters, password);
            var isValid = string.Equals(expectedToken, providedToken, StringComparison.OrdinalIgnoreCase);

            if (!isValid)
            {
                _logger.LogWarning("Token validation failed. Expected token does not match provided token");
                
                if (_options.EnableDebugMode)
                {
                    var concatenatedString = ExtractConcatenatedString(requestParameters, password);
                    _logger.LogDebug("Concatenated string for token validation (password masked): {ConcatenatedString}", 
                        MaskPasswordInString(concatenatedString, password));
                }
            }

            return isValid;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to validate token");
            return false;
        }
    }

    public async Task<string> GenerateTokenAsync(Dictionary<string, object> requestParameters, string password)
    {
        return await Task.Run(() => GenerateToken(requestParameters, password));
    }

    public async Task<bool> ValidateTokenAsync(Dictionary<string, object> requestParameters, string password, string providedToken)
    {
        return await Task.Run(() => ValidateToken(requestParameters, password, providedToken));
    }

    public string ExtractConcatenatedString(Dictionary<string, object> requestParameters, string password)
    {
        ArgumentNullException.ThrowIfNull(requestParameters);
        ArgumentException.ThrowIfNullOrEmpty(password);

        var tokenParameters = ExtractRootLevelScalarParameters(requestParameters);
        tokenParameters["Password"] = password;
        var sortedKeys = tokenParameters.Keys.OrderBy(k => k, StringComparer.Ordinal).ToList();
        
        return string.Concat(sortedKeys.Select(key => tokenParameters[key]));
    }

    private Dictionary<string, string> ExtractRootLevelScalarParameters(Dictionary<string, object> requestParameters)
    {
        var tokenParameters = new Dictionary<string, string>();

        foreach (var kvp in requestParameters)
        {
            // Skip excluded parameters (Token, nested objects like Receipt, DICT)
            if (_options.ExcludedParameters.Contains(kvp.Key, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            // Only include scalar values (exclude objects, arrays, and collections)
            if (IsScalarValue(kvp.Value))
            {
                var stringValue = ConvertToTokenString(kvp.Value);
                tokenParameters[kvp.Key] = stringValue;

                if (_options.EnableDebugMode)
                {
                    _logger.LogDebug("Included parameter: {Key} = {Value} (Type: {Type})", 
                        kvp.Key, stringValue, kvp.Value?.GetType().Name ?? "null");
                }
            }
            else if (_options.EnableDebugMode)
            {
                _logger.LogDebug("Excluded non-scalar parameter: {Key} (Type: {Type})", 
                    kvp.Key, kvp.Value?.GetType().Name ?? "null");
            }
        }

        return tokenParameters;
    }

    private static bool IsScalarValue(object? value)
    {
        if (value == null) return true;

        var type = value.GetType();
        
        // Check for primitive types and strings
        if (type.IsPrimitive || type == typeof(string) || type == typeof(decimal))
        {
            return true;
        }

        // Check for nullable primitive types
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Nullable<>))
        {
            var underlyingType = Nullable.GetUnderlyingType(type);
            return underlyingType?.IsPrimitive == true || underlyingType == typeof(decimal);
        }

        // Handle DateTime and other common scalar types
        return type == typeof(DateTime) || 
               type == typeof(DateTimeOffset) || 
               type == typeof(TimeSpan) ||
               type == typeof(Guid);
    }

    private static string ConvertToTokenString(object? value)
    {
        return value switch
        {
            null => string.Empty,
            bool b => b ? "true" : "false",
            DateTime dt => dt.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"),
            DateTimeOffset dto => dto.ToString("yyyy-MM-ddTHH:mm:ss.fffzzz"),
            decimal d => d.ToString("F"),
            double db => db.ToString("F"),
            float f => f.ToString("F"),
            _ => value.ToString() ?? string.Empty
        };
    }

    private static string ComputeSha256Hash(string input)
    {
        var inputBytes = Utf8Encoding.GetBytes(input);
        var hashBytes = SHA256.HashData(inputBytes);
        
        // Convert to lowercase hexadecimal string
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }

    private string MaskPasswordInString(string concatenatedString, string password)
    {
        if (string.IsNullOrEmpty(concatenatedString) || string.IsNullOrEmpty(password))
        {
            return concatenatedString;
        }

        return concatenatedString.Replace(password, "***MASKED***");
    }
}

// Extension methods for easier token usage
public static class TokenGenerationExtensions
{
    public static async Task<string> GenerateTokenForRequestAsync(
        this ITokenGenerationService tokenService, 
        object request, 
        string password)
    {
        var parameters = ConvertObjectToDictionary(request);
        return await tokenService.GenerateTokenAsync(parameters, password);
    }

    public static async Task<bool> ValidateTokenForRequestAsync(
        this ITokenGenerationService tokenService, 
        object request, 
        string password, 
        string providedToken)
    {
        var parameters = ConvertObjectToDictionary(request);
        return await tokenService.ValidateTokenAsync(parameters, password, providedToken);
    }

    private static Dictionary<string, object> ConvertObjectToDictionary(object obj)
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        };

        var json = JsonSerializer.Serialize(obj, options);
        var dictionary = JsonSerializer.Deserialize<Dictionary<string, object>>(json, options);
        return dictionary ?? new Dictionary<string, object>();
    }
}

// Custom exception for token generation errors
public class TokenGenerationException : Exception
{
    public string? ParameterName { get; }
    public object? ParameterValue { get; }

    public TokenGenerationException(string message) : base(message) { }

    public TokenGenerationException(string message, Exception innerException) : base(message, innerException) { }

    public TokenGenerationException(string message, string parameterName, object? parameterValue) : base(message)
    {
        ParameterName = parameterName;
        ParameterValue = parameterValue;
    }
}