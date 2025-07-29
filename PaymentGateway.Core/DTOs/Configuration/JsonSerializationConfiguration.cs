using System.Text.Json;
using System.Text.Json.Serialization;

namespace PaymentGateway.Core.DTOs.Configuration;

/// <summary>
/// JSON serialization configuration for DTOs
/// </summary>
public static class JsonSerializationConfiguration
{
    /// <summary>
    /// Default JSON serializer options for API responses
    /// </summary>
    public static JsonSerializerOptions DefaultOptions => new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
        Converters =
        {
            new JsonStringEnumConverter(JsonNamingPolicy.CamelCase),
            new DateTimeConverter(),
            new TimeSpanConverter(),
            new DecimalConverter(),
            new DictionaryStringObjectConverter()
        }
    };

    /// <summary>
    /// JSON serializer options for API requests (more permissive)
    /// </summary>
    public static JsonSerializerOptions RequestOptions => new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.AllowNamedFloatingPointLiterals,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters =
        {
            new JsonStringEnumConverter(JsonNamingPolicy.CamelCase),
            new DateTimeConverter(),
            new TimeSpanConverter(),
            new DecimalConverter(),
            new DictionaryStringObjectConverter()
        }
    };

    /// <summary>
    /// JSON serializer options for production (optimized)
    /// </summary>
    public static JsonSerializerOptions ProductionOptions => new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = false,
        NumberHandling = JsonNumberHandling.Strict,
        Converters =
        {
            new JsonStringEnumConverter(JsonNamingPolicy.CamelCase),
            new DateTimeConverter(),
            new DecimalConverter()
        }
    };
}

/// <summary>
/// Custom DateTime converter for consistent formatting
/// </summary>
public class DateTimeConverter : JsonConverter<DateTime>
{
    private const string DateTimeFormat = "yyyy-MM-ddTHH:mm:ss.fffZ";

    public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var dateString = reader.GetString();
        if (string.IsNullOrEmpty(dateString))
            return default;

        // Try multiple formats for flexibility
        var formats = new[]
        {
            DateTimeFormat,
            "yyyy-MM-ddTHH:mm:ssZ",
            "yyyy-MM-ddTHH:mm:ss.ffZ",
            "yyyy-MM-ddTHH:mm:ss",
            "yyyy-MM-dd HH:mm:ss",
            "yyyy-MM-dd"
        };

        foreach (var format in formats)
        {
            if (DateTime.TryParseExact(dateString, format, null, System.Globalization.DateTimeStyles.AdjustToUniversal, out var date))
            {
                return date;
            }
        }

        // Fallback to standard parsing
        if (DateTime.TryParse(dateString, out var fallbackDate))
        {
            return fallbackDate.ToUniversalTime();
        }

        throw new JsonException($"Unable to parse '{dateString}' as DateTime");
    }

    public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToUniversalTime().ToString(DateTimeFormat));
    }
}

/// <summary>
/// Custom TimeSpan converter for consistent formatting
/// </summary>
public class TimeSpanConverter : JsonConverter<TimeSpan>
{
    public override TimeSpan Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var value = reader.GetString();
        if (string.IsNullOrEmpty(value))
            return default;

        if (TimeSpan.TryParse(value, out var timeSpan))
            return timeSpan;

        // Try parsing as total milliseconds
        if (double.TryParse(value, out var milliseconds))
            return TimeSpan.FromMilliseconds(milliseconds);

        throw new JsonException($"Unable to parse '{value}' as TimeSpan");
    }

    public override void Write(Utf8JsonWriter writer, TimeSpan value, JsonSerializerOptions options)
    {
        // Write as ISO 8601 duration format
        writer.WriteStringValue(value.ToString(@"c"));
    }
}

/// <summary>
/// Custom decimal converter for monetary values
/// </summary>
public class DecimalConverter : JsonConverter<decimal>
{
    public override decimal Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
        {
            var stringValue = reader.GetString();
            if (decimal.TryParse(stringValue, out var decimalValue))
                return decimalValue;
            throw new JsonException($"Unable to parse '{stringValue}' as decimal");
        }

        return reader.GetDecimal();
    }

    public override void Write(Utf8JsonWriter writer, decimal value, JsonSerializerOptions options)
    {
        // Always write decimals as numbers, not strings
        writer.WriteNumberValue(value);
    }
}

/// <summary>
/// Custom converter for Dictionary<string, object> to handle mixed types
/// </summary>
public class DictionaryStringObjectConverter : JsonConverter<Dictionary<string, object>>
{
    public override Dictionary<string, object> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException("Expected StartObject token");
        }

        var dictionary = new Dictionary<string, object>();

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject)
            {
                return dictionary;
            }

            if (reader.TokenType != JsonTokenType.PropertyName)
            {
                throw new JsonException("Expected PropertyName token");
            }

            var propertyName = reader.GetString()!;
            reader.Read();

            dictionary[propertyName] = ReadValue(ref reader);
        }

        throw new JsonException("Unexpected end of JSON input");
    }

    public override void Write(Utf8JsonWriter writer, Dictionary<string, object> value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();

        foreach (var kvp in value)
        {
            writer.WritePropertyName(kvp.Key);
            WriteValue(writer, kvp.Value, options);
        }

        writer.WriteEndObject();
    }

    private static object ReadValue(ref Utf8JsonReader reader)
    {
        return reader.TokenType switch
        {
            JsonTokenType.String => reader.GetString()!,
            JsonTokenType.Number => reader.TryGetInt64(out var longValue) ? longValue : reader.GetDouble(),
            JsonTokenType.True => true,
            JsonTokenType.False => false,
            JsonTokenType.Null => null!,
            JsonTokenType.StartObject => ReadDictionary(ref reader),
            JsonTokenType.StartArray => ReadArray(ref reader),
            _ => throw new JsonException($"Unexpected token type: {reader.TokenType}")
        };
    }

    private static Dictionary<string, object> ReadDictionary(ref Utf8JsonReader reader)
    {
        var dictionary = new Dictionary<string, object>();

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject)
            {
                return dictionary;
            }

            if (reader.TokenType != JsonTokenType.PropertyName)
            {
                throw new JsonException("Expected PropertyName token");
            }

            var propertyName = reader.GetString()!;
            reader.Read();
            dictionary[propertyName] = ReadValue(ref reader);
        }

        throw new JsonException("Unexpected end of JSON input");
    }

    private static List<object> ReadArray(ref Utf8JsonReader reader)
    {
        var array = new List<object>();

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndArray)
            {
                return array;
            }

            array.Add(ReadValue(ref reader));
        }

        throw new JsonException("Unexpected end of JSON input");
    }

    private static void WriteValue(Utf8JsonWriter writer, object value, JsonSerializerOptions options)
    {
        switch (value)
        {
            case null:
                writer.WriteNullValue();
                break;
            case string stringValue:
                writer.WriteStringValue(stringValue);
                break;
            case int intValue:
                writer.WriteNumberValue(intValue);
                break;
            case long longValue:
                writer.WriteNumberValue(longValue);
                break;
            case double doubleValue:
                writer.WriteNumberValue(doubleValue);
                break;
            case decimal decimalValue:
                writer.WriteNumberValue(decimalValue);
                break;
            case bool boolValue:
                writer.WriteBooleanValue(boolValue);
                break;
            case DateTime dateTimeValue:
                writer.WriteStringValue(dateTimeValue.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"));
                break;
            case Dictionary<string, object> dictValue:
                JsonSerializer.Serialize(writer, dictValue, options);
                break;
            case IEnumerable<object> arrayValue:
                JsonSerializer.Serialize(writer, arrayValue, options);
                break;
            default:
                JsonSerializer.Serialize(writer, value, value.GetType(), options);
                break;
        }
    }
}