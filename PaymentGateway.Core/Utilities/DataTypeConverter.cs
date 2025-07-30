using System.Security.Cryptography;
using System.Text;

namespace PaymentGateway.Core.Utilities;

/// <summary>
/// Provides safe data type conversion utilities to replace unsafe GetHashCode() usage
/// </summary>
public static class DataTypeConverter
{
    /// <summary>
    /// Converts a Guid to a consistent long value using SHA256 hash
    /// This provides better distribution than GetHashCode() and is deterministic
    /// </summary>
    /// <param name="guid">The Guid to convert</param>
    /// <returns>A consistent long representation of the Guid</returns>
    public static long GuidToLong(Guid guid)
    {
        var bytes = guid.ToByteArray();
        using var sha256 = SHA256.Create();
        var hash = sha256.ComputeHash(bytes);
        
        // Take first 8 bytes and convert to long
        return BitConverter.ToInt64(hash, 0);
    }
    
    /// <summary>
    /// Converts a Guid to a consistent int value using SHA256 hash
    /// This provides better distribution than GetHashCode() and is deterministic
    /// </summary>
    /// <param name="guid">The Guid to convert</param>
    /// <returns>A consistent int representation of the Guid</returns>
    public static int GuidToInt(Guid guid)
    {
        var bytes = guid.ToByteArray();
        using var sha256 = SHA256.Create();
        var hash = sha256.ComputeHash(bytes);
        
        // Take first 4 bytes and convert to int
        return BitConverter.ToInt32(hash, 0);
    }
    
    /// <summary>
    /// Creates a deterministic long from a string using SHA256
    /// </summary>
    /// <param name="value">The string to convert</param>
    /// <returns>A consistent long representation of the string</returns>
    public static long StringToLong(string value)
    {
        if (string.IsNullOrEmpty(value))
            return 0;
            
        var bytes = Encoding.UTF8.GetBytes(value);
        using var sha256 = SHA256.Create();
        var hash = sha256.ComputeHash(bytes);
        
        return BitConverter.ToInt64(hash, 0);
    }
    
    /// <summary>
    /// Creates a deterministic int from a string using SHA256
    /// </summary>
    /// <param name="value">The string to convert</param>
    /// <returns>A consistent int representation of the string</returns>
    public static int StringToInt(string value)
    {
        if (string.IsNullOrEmpty(value))
            return 0;
            
        var bytes = Encoding.UTF8.GetBytes(value);
        using var sha256 = SHA256.Create();
        var hash = sha256.ComputeHash(bytes);
        
        return BitConverter.ToInt32(hash, 0);
    }
    
    /// <summary>
    /// Converts a long back to a Guid (this is lossy and should be avoided)
    /// Only use when absolutely necessary for legacy compatibility
    /// </summary>
    /// <param name="value">The long to convert</param>
    /// <returns>A Guid representation (NOT reversible)</returns>
    [Obsolete("This conversion is lossy. Use proper Guid primary keys instead.")]
    public static Guid LongToGuid(long value)
    {
        var bytes = BitConverter.GetBytes(value);
        var guidBytes = new byte[16];
        Array.Copy(bytes, guidBytes, Math.Min(bytes.Length, guidBytes.Length));
        return new Guid(guidBytes);
    }
    
    /// <summary>
    /// Generates a consistent Guid from a string using namespace UUID v5 approach
    /// This is more appropriate for string-to-Guid conversions
    /// </summary>
    /// <param name="name">The string to convert</param>
    /// <param name="namespaceGuid">The namespace Guid (optional)</param>
    /// <returns>A deterministic Guid</returns>
    public static Guid StringToGuid(string name, Guid? namespaceGuid = null)
    {
        if (string.IsNullOrEmpty(name))
            return Guid.Empty;
            
        var ns = namespaceGuid ?? new Guid("6ba7b810-9dad-11d1-80b4-00c04fd430c8"); // DNS namespace
        var nameBytes = Encoding.UTF8.GetBytes(name);
        var namespaceBytes = ns.ToByteArray();
        
        using var sha1 = SHA1.Create();
        var combinedBytes = new byte[namespaceBytes.Length + nameBytes.Length];
        Array.Copy(namespaceBytes, combinedBytes, namespaceBytes.Length);
        Array.Copy(nameBytes, 0, combinedBytes, namespaceBytes.Length, nameBytes.Length);
        
        var hash = sha1.ComputeHash(combinedBytes);
        
        // Set version (5) and variant bits according to RFC 4122
        hash[6] = (byte)((hash[6] & 0x0F) | 0x50); // Version 5
        hash[8] = (byte)((hash[8] & 0x3F) | 0x80); // Variant 10
        
        // Take first 16 bytes for the Guid
        var guidBytes = new byte[16];
        Array.Copy(hash, guidBytes, 16);
        
        return new Guid(guidBytes);
    }
}