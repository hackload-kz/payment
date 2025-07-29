using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace PaymentGateway.Core.Services;

public interface ISecureConfigurationService
{
    Task<string> GetSecureValueAsync(string key);
    Task<T?> GetSecureValueAsync<T>(string key) where T : class;
    Task SetSecureValueAsync(string key, string value);
    Task SetSecureValueAsync<T>(string key, T value) where T : class;
    Task<bool> HasSecureValueAsync(string key);
    Task RemoveSecureValueAsync(string key);
    Task<Dictionary<string, string>> GetAllSecureKeysAsync();
    Task<string> EncryptValueAsync(string value);
    Task<string> DecryptValueAsync(string encryptedValue);
}

public class SecureConfigurationService : ISecureConfigurationService
{
    private readonly ILogger<SecureConfigurationService> _logger;
    private readonly IConfiguration _configuration;
    private readonly byte[] _encryptionKey;
    private readonly Dictionary<string, string> _secureValues;
    private readonly object _lock = new();

    // Paths for secure configuration sources
    private readonly List<string> _secureConfigurationPaths;

    public SecureConfigurationService(
        ILogger<SecureConfigurationService> logger,
        IConfiguration configuration)
    {
        _logger = logger;
        _configuration = configuration;
        _secureValues = new Dictionary<string, string>();
        _secureConfigurationPaths = new List<string>
        {
            "/var/secrets/paymentgateway/",  // Kubernetes secrets
            "/run/secrets/",                 // Docker secrets
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData) + "/paymentgateway/secrets/"
        };

        // Initialize encryption key
        _encryptionKey = InitializeEncryptionKey();

        // Load secure configuration on startup
        _ = Task.Run(LoadSecureConfigurationAsync);
    }

    public async Task<string> GetSecureValueAsync(string key)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);

        try
        {
            // First check environment variables (highest priority)
            var envValue = Environment.GetEnvironmentVariable(key);
            if (!string.IsNullOrEmpty(envValue))
            {
                _logger.LogDebug("Retrieved secure value for key {Key} from environment variable", key);
                return envValue;
            }

            // Check in-memory secure values
            lock (_lock)
            {
                if (_secureValues.TryGetValue(key, out var secureValue))
                {
                    var decryptedValue = await DecryptValueAsync(secureValue);
                    _logger.LogDebug("Retrieved secure value for key {Key} from secure storage", key);
                    return decryptedValue;
                }
            }

            // Check configuration files (least secure, for development)
            var configValue = _configuration[key];
            if (!string.IsNullOrEmpty(configValue))
            {
                _logger.LogWarning("Retrieved value for key {Key} from configuration file - consider using secure storage", key);
                return configValue;
            }

            // Check secure configuration files
            var secureFileValue = await ReadFromSecureFileAsync(key);
            if (!string.IsNullOrEmpty(secureFileValue))
            {
                _logger.LogDebug("Retrieved secure value for key {Key} from secure file", key);
                return secureFileValue;
            }

            throw new KeyNotFoundException($"Secure configuration value not found for key: {key}");
        }
        catch (Exception ex) when (!(ex is KeyNotFoundException))
        {
            _logger.LogError(ex, "Error retrieving secure value for key {Key}", key);
            throw new InvalidOperationException($"Failed to retrieve secure value for key: {key}", ex);
        }
    }

    public async Task<T?> GetSecureValueAsync<T>(string key) where T : class
    {
        var jsonValue = await GetSecureValueAsync(key);
        
        try
        {
            return JsonSerializer.Deserialize<T>(jsonValue);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Error deserializing secure value for key {Key} to type {Type}", key, typeof(T).Name);
            throw new InvalidOperationException($"Failed to deserialize secure value for key {key} to type {typeof(T).Name}", ex);
        }
    }

    public async Task SetSecureValueAsync(string key, string value)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        ArgumentNullException.ThrowIfNull(value);

        try
        {
            var encryptedValue = await EncryptValueAsync(value);
            
            lock (_lock)
            {
                _secureValues[key] = encryptedValue;
            }

            // Also save to secure file for persistence
            await WriteToSecureFileAsync(key, encryptedValue);

            _logger.LogInformation("Secure value set for key {Key}", key);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error setting secure value for key {Key}", key);
            throw new InvalidOperationException($"Failed to set secure value for key: {key}", ex);
        }
    }

    public async Task SetSecureValueAsync<T>(string key, T value) where T : class
    {
        ArgumentNullException.ThrowIfNull(value);
        
        var jsonValue = JsonSerializer.Serialize(value);
        await SetSecureValueAsync(key, jsonValue);
    }

    public async Task<bool> HasSecureValueAsync(string key)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);

        try
        {
            await GetSecureValueAsync(key);
            return true;
        }
        catch (KeyNotFoundException)
        {
            return false;
        }
    }

    public async Task RemoveSecureValueAsync(string key)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);

        try
        {
            lock (_lock)
            {
                _secureValues.Remove(key);
            }

            // Also remove from secure file
            await RemoveFromSecureFileAsync(key);

            _logger.LogInformation("Secure value removed for key {Key}", key);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error removing secure value for key {Key}", key);
            throw new InvalidOperationException($"Failed to remove secure value for key: {key}", ex);
        }

        await Task.CompletedTask;
    }

    public async Task<Dictionary<string, string>> GetAllSecureKeysAsync()
    {
        lock (_lock)
        {
            // Return only keys, not values for security
            return _secureValues.Keys.ToDictionary(k => k, k => "***ENCRYPTED***");
        }
    }

    public async Task<string> EncryptValueAsync(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        try
        {
            using var aes = Aes.Create();
            aes.Key = _encryptionKey;
            aes.GenerateIV();

            var plainBytes = Encoding.UTF8.GetBytes(value);
            
            using var encryptor = aes.CreateEncryptor();
            using var msEncrypt = new MemoryStream();
            using (var csEncrypt = new CryptoStream(msEncrypt, encryptor, CryptoStreamMode.Write))
            {
                csEncrypt.Write(plainBytes, 0, plainBytes.Length);
            }

            var iv = aes.IV;
            var encryptedBytes = msEncrypt.ToArray();
            var combinedBytes = new byte[iv.Length + encryptedBytes.Length];
            
            Array.Copy(iv, 0, combinedBytes, 0, iv.Length);
            Array.Copy(encryptedBytes, 0, combinedBytes, iv.Length, encryptedBytes.Length);

            return await Task.FromResult(Convert.ToBase64String(combinedBytes));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error encrypting value");
            throw new InvalidOperationException("Failed to encrypt value", ex);
        }
    }

    public async Task<string> DecryptValueAsync(string encryptedValue)
    {
        ArgumentException.ThrowIfNullOrEmpty(encryptedValue);

        try
        {
            var combinedBytes = Convert.FromBase64String(encryptedValue);
            
            using var aes = Aes.Create();
            aes.Key = _encryptionKey;

            var iv = new byte[aes.IV.Length];
            var encryptedBytes = new byte[combinedBytes.Length - iv.Length];
            
            Array.Copy(combinedBytes, 0, iv, 0, iv.Length);
            Array.Copy(combinedBytes, iv.Length, encryptedBytes, 0, encryptedBytes.Length);
            
            aes.IV = iv;

            using var decryptor = aes.CreateDecryptor();
            using var msDecrypt = new MemoryStream(encryptedBytes);
            using var csDecrypt = new CryptoStream(msDecrypt, decryptor, CryptoStreamMode.Read);
            using var reader = new StreamReader(csDecrypt);
            
            return await reader.ReadToEndAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error decrypting value");
            throw new InvalidOperationException("Failed to decrypt value", ex);
        }
    }

    private byte[] InitializeEncryptionKey()
    {
        try
        {
            // Try to get encryption key from environment
            var keyFromEnv = Environment.GetEnvironmentVariable("PAYMENTGATEWAY_ENCRYPTION_KEY");
            if (!string.IsNullOrEmpty(keyFromEnv))
            {
                var keyBytes = Convert.FromBase64String(keyFromEnv);
                if (keyBytes.Length == 32) // 256-bit key
                {
                    _logger.LogInformation("Using encryption key from environment variable");
                    return keyBytes;
                }
            }

            // Try to get key from secure file
            var keyFromFile = ReadEncryptionKeyFromFile();
            if (keyFromFile != null)
            {
                _logger.LogInformation("Using encryption key from secure file");
                return keyFromFile;
            }

            // Generate new key and save it
            _logger.LogWarning("No encryption key found, generating new key. This should only happen on first run.");
            var newKey = GenerateNewEncryptionKey();
            SaveEncryptionKeyToFile(newKey);
            
            return newKey;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error initializing encryption key");
            throw new InvalidOperationException("Failed to initialize encryption key", ex);
        }
    }

    private byte[]? ReadEncryptionKeyFromFile()
    {
        foreach (var path in _secureConfigurationPaths)
        {
            var keyFilePath = Path.Combine(path, "encryption.key");
            
            try
            {
                if (File.Exists(keyFilePath))
                {
                    var keyBase64 = File.ReadAllText(keyFilePath);
                    return Convert.FromBase64String(keyBase64);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error reading encryption key from {KeyFilePath}", keyFilePath);
            }
        }

        return null;
    }

    private void SaveEncryptionKeyToFile(byte[] key)
    {
        foreach (var path in _secureConfigurationPaths)
        {
            try
            {
                Directory.CreateDirectory(path);
                var keyFilePath = Path.Combine(path, "encryption.key");
                var keyBase64 = Convert.ToBase64String(key);
                
                File.WriteAllText(keyFilePath, keyBase64);
                
                // Set file permissions (Unix/Linux only)
                if (Environment.OSVersion.Platform == PlatformID.Unix)
                {
                    File.SetUnixFileMode(keyFilePath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
                }
                
                _logger.LogInformation("Encryption key saved to {KeyFilePath}", keyFilePath);
                return;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not save encryption key to {Path}", path);
            }
        }

        _logger.LogWarning("Could not save encryption key to any secure location");
    }

    private static byte[] GenerateNewEncryptionKey()
    {
        using var rng = RandomNumberGenerator.Create();
        var key = new byte[32]; // 256-bit key
        rng.GetBytes(key);
        return key;
    }

    private async Task LoadSecureConfigurationAsync()
    {
        foreach (var path in _secureConfigurationPaths)
        {
            await LoadSecureConfigurationFromPathAsync(path);
        }
    }

    private async Task LoadSecureConfigurationFromPathAsync(string basePath)
    {
        try
        {
            if (!Directory.Exists(basePath))
                return;

            var configFiles = Directory.GetFiles(basePath, "*.secret", SearchOption.TopDirectoryOnly);
            
            foreach (var configFile in configFiles)
            {
                var key = Path.GetFileNameWithoutExtension(configFile);
                var encryptedValue = await File.ReadAllTextAsync(configFile);
                
                lock (_lock)
                {
                    _secureValues[key] = encryptedValue;
                }
                
                _logger.LogDebug("Loaded secure configuration for key {Key} from {ConfigFile}", key, configFile);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error loading secure configuration from path {BasePath}", basePath);
        }
    }

    private async Task<string?> ReadFromSecureFileAsync(string key)
    {
        foreach (var path in _secureConfigurationPaths)
        {
            var filePath = Path.Combine(path, $"{key}.secret");
            
            try
            {
                if (File.Exists(filePath))
                {
                    var encryptedValue = await File.ReadAllTextAsync(filePath);
                    return await DecryptValueAsync(encryptedValue);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error reading secure file {FilePath}", filePath);
            }
        }

        return null;
    }

    private async Task WriteToSecureFileAsync(string key, string encryptedValue)
    {
        foreach (var path in _secureConfigurationPaths)
        {
            try
            {
                Directory.CreateDirectory(path);
                var filePath = Path.Combine(path, $"{key}.secret");
                
                await File.WriteAllTextAsync(filePath, encryptedValue);
                
                // Set file permissions (Unix/Linux only)
                if (Environment.OSVersion.Platform == PlatformID.Unix)
                {
                    File.SetUnixFileMode(filePath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
                }
                
                _logger.LogDebug("Secure value written to {FilePath}", filePath);
                return;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not write secure file to {Path}", path);
            }
        }

        _logger.LogWarning("Could not write secure file for key {Key} to any location", key);
    }

    private async Task RemoveFromSecureFileAsync(string key)
    {
        foreach (var path in _secureConfigurationPaths)
        {
            var filePath = Path.Combine(path, $"{key}.secret");
            
            try
            {
                if (File.Exists(filePath))
                {
                    File.Delete(filePath);
                    _logger.LogDebug("Secure file deleted: {FilePath}", filePath);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error deleting secure file {FilePath}", filePath);
            }
        }

        await Task.CompletedTask;
    }
}

// Extension methods for easier service registration and configuration
public static class SecureConfigurationServiceExtensions
{
    public static IServiceCollection AddSecureConfiguration(
        this IServiceCollection services)
    {
        services.AddSingleton<ISecureConfigurationService, SecureConfigurationService>();
        return services;
    }

    public static async Task<string> GetSecureConnectionStringAsync(
        this ISecureConfigurationService secureConfig,
        IConfiguration configuration,
        string connectionStringName = "DefaultConnection")
    {
        try
        {
            // Try to get from secure configuration first
            var secureConnectionString = await secureConfig.GetSecureValueAsync($"ConnectionStrings__{connectionStringName}");
            return secureConnectionString;
        }
        catch (KeyNotFoundException)
        {
            // Fall back to regular configuration, but process environment variables
            var connectionString = configuration.GetConnectionString(connectionStringName);
            
            if (string.IsNullOrEmpty(connectionString))
            {
                throw new InvalidOperationException($"Connection string '{connectionStringName}' not found in configuration");
            }

            // Process environment variable placeholders
            return ProcessEnvironmentVariables(connectionString);
        }
    }

    private static string ProcessEnvironmentVariables(string connectionString)
    {
        var result = connectionString;
        
        // Replace ${ENV_VAR} patterns with actual environment variable values
        var pattern = @"\$\{([^}]+)\}";
        var matches = System.Text.RegularExpressions.Regex.Matches(result, pattern);
        
        foreach (System.Text.RegularExpressions.Match match in matches)
        {
            var envVarName = match.Groups[1].Value;
            var envVarValue = Environment.GetEnvironmentVariable(envVarName);
            
            if (!string.IsNullOrEmpty(envVarValue))
            {
                result = result.Replace(match.Value, envVarValue);
            }
            else
            {
                throw new InvalidOperationException($"Environment variable '{envVarName}' is not set");
            }
        }
        
        return result;
    }
}

// Secure configuration options
public class SecureConfigurationOptions
{
    public const string SectionName = "SecureConfiguration";
    
    public List<string> SecurePaths { get; set; } = new();
    public bool UseEnvironmentVariables { get; set; } = true;
    public bool EncryptionEnabled { get; set; } = true;
    public string EncryptionKeySource { get; set; } = "Environment"; // Environment, File, Generated
    public bool AuditSecureAccess { get; set; } = true;
}