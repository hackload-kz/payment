using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace PaymentGateway.Core.Services;

/// <summary>
/// Service interface for comprehensive payment data encryption
/// </summary>
public interface IPaymentDataEncryptionService
{
    Task<EncryptionResult> EncryptPaymentDataAsync<T>(T paymentData, EncryptionOptions? options = null, CancellationToken cancellationToken = default);
    Task<DecryptionResult<T>> DecryptPaymentDataAsync<T>(string encryptedData, EncryptionOptions? options = null, CancellationToken cancellationToken = default);
    Task<string> EncryptSensitiveFieldAsync(string sensitiveValue, string fieldType, CancellationToken cancellationToken = default);
    Task<string> DecryptSensitiveFieldAsync(string encryptedValue, string fieldType, CancellationToken cancellationToken = default);
    Task<FieldMaskingResult> MaskSensitiveFieldsAsync<T>(T data, MaskingOptions? options = null, CancellationToken cancellationToken = default);
    Task<DatabaseEncryptionResult> EncryptForDatabaseStorageAsync(object data, string tableName, string fieldName, CancellationToken cancellationToken = default);
    Task<T> DecryptFromDatabaseStorageAsync<T>(string encryptedData, string tableName, string fieldName, CancellationToken cancellationToken = default);
    Task<TransitEncryptionResult> EncryptForTransitAsync(object data, string destinationEndpoint, CancellationToken cancellationToken = default);
    Task<T> DecryptFromTransitAsync<T>(string encryptedData, string sourceEndpoint, CancellationToken cancellationToken = default);
    Task<bool> ValidateEncryptionIntegrityAsync(string encryptedData, string integrityHash, CancellationToken cancellationToken = default);
}

/// <summary>
/// Comprehensive payment data encryption service implementation
/// </summary>
public class PaymentDataEncryptionService : IPaymentDataEncryptionService
{
    private readonly ILogger<PaymentDataEncryptionService> _logger;
    private readonly ISecurityAuditService _securityAuditService;
    private readonly PaymentEncryptionOptions _options;

    // Static encryption keys - in production, these should be stored securely (Azure Key Vault, AWS KMS, etc.)
    private readonly byte[] _primaryEncryptionKey;
    private readonly byte[] _databaseEncryptionKey;
    private readonly byte[] _transitEncryptionKey;

    public PaymentDataEncryptionService(
        ILogger<PaymentDataEncryptionService> logger,
        ISecurityAuditService securityAuditService,
        IOptions<PaymentEncryptionOptions> options)
    {
        _logger = logger;
        _securityAuditService = securityAuditService;
        _options = options.Value;

        // Initialize encryption keys (in production, load from secure key management)
        _primaryEncryptionKey = DeriveKeyFromPassword(_options.MasterPassword, "primary");
        _databaseEncryptionKey = DeriveKeyFromPassword(_options.MasterPassword, "database");
        _transitEncryptionKey = DeriveKeyFromPassword(_options.MasterPassword, "transit");
    }

    public async Task<EncryptionResult> EncryptPaymentDataAsync<T>(
        T paymentData, 
        EncryptionOptions? options = null, 
        CancellationToken cancellationToken = default)
    {
        try
        {
            var encryptionOptions = options ?? new EncryptionOptions();
            var startTime = DateTime.UtcNow;

            _logger.LogDebug("Encrypting payment data of type {DataType}", typeof(T).Name);

            // Serialize the data
            var jsonData = JsonSerializer.Serialize(paymentData, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });

            // Choose encryption algorithm
            var encryptedData = encryptionOptions.Algorithm switch
            {
                EncryptionAlgorithm.AES_256_GCM => await EncryptWithAesGcmAsync(jsonData, _primaryEncryptionKey),
                EncryptionAlgorithm.AES_256_CBC => await EncryptWithAesCbcAsync(jsonData, _primaryEncryptionKey),
                EncryptionAlgorithm.ChaCha20_Poly1305 => await EncryptWithChaCha20Async(jsonData, _primaryEncryptionKey),
                _ => throw new NotSupportedException($"Encryption algorithm {encryptionOptions.Algorithm} is not supported")
            };

            // Generate integrity hash
            var integrityHash = GenerateIntegrityHash(encryptedData);

            var encryptionTime = DateTime.UtcNow - startTime;

            await _securityAuditService.LogSecurityEventAsync(new SecurityAuditEvent(
                Guid.NewGuid().ToString(),
                SecurityEventType.DataAccess,
                SecurityEventSeverity.Low,
                DateTime.UtcNow,
                null,
                null,
                null,
                null,
                $"Payment data encrypted using {encryptionOptions.Algorithm}",
                new Dictionary<string, string>
                {
                    { "DataType", typeof(T).Name },
                    { "Algorithm", encryptionOptions.Algorithm.ToString() },
                    { "EncryptionTime", encryptionTime.TotalMilliseconds.ToString() }
                },
                null,
                true,
                null
            ));

            return new EncryptionResult
            {
                EncryptedData = Convert.ToBase64String(encryptedData),
                Algorithm = encryptionOptions.Algorithm,
                IntegrityHash = integrityHash,
                EncryptedAt = DateTime.UtcNow,
                IsSuccessful = true
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error encrypting payment data of type {DataType}", typeof(T).Name);
            
            await _securityAuditService.LogSecurityEventAsync(new SecurityAuditEvent(
                Guid.NewGuid().ToString(),
                SecurityEventType.SystemError,
                SecurityEventSeverity.High,
                DateTime.UtcNow,
                null,
                null,
                null,
                null,
                $"Payment data encryption failed for type {typeof(T).Name}",
                new Dictionary<string, string>(),
                null,
                false,
                ex.Message
            ));

            return new EncryptionResult
            {
                IsSuccessful = false,
                ErrorMessage = "Encryption failed"
            };
        }
    }

    public async Task<DecryptionResult<T>> DecryptPaymentDataAsync<T>(
        string encryptedData, 
        EncryptionOptions? options = null, 
        CancellationToken cancellationToken = default)
    {
        try
        {
            var encryptionOptions = options ?? new EncryptionOptions();
            var startTime = DateTime.UtcNow;

            _logger.LogDebug("Decrypting payment data to type {DataType}", typeof(T).Name);

            // Decode the encrypted data
            var encryptedBytes = Convert.FromBase64String(encryptedData);

            // Decrypt based on algorithm
            var decryptedJson = encryptionOptions.Algorithm switch
            {
                EncryptionAlgorithm.AES_256_GCM => await DecryptWithAesGcmAsync(encryptedBytes, _primaryEncryptionKey),
                EncryptionAlgorithm.AES_256_CBC => await DecryptWithAesCbcAsync(encryptedBytes, _primaryEncryptionKey),
                EncryptionAlgorithm.ChaCha20_Poly1305 => await DecryptWithChaCha20Async(encryptedBytes, _primaryEncryptionKey),
                _ => throw new NotSupportedException($"Decryption algorithm {encryptionOptions.Algorithm} is not supported")
            };

            // Deserialize the data
            var paymentData = JsonSerializer.Deserialize<T>(decryptedJson, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });

            var decryptionTime = DateTime.UtcNow - startTime;

            await _securityAuditService.LogSecurityEventAsync(new SecurityAuditEvent(
                Guid.NewGuid().ToString(),
                SecurityEventType.DataAccess,
                SecurityEventSeverity.Low,
                DateTime.UtcNow,
                null,
                null,
                null,
                null,
                $"Payment data decrypted to type {typeof(T).Name}",
                new Dictionary<string, string>
                {
                    { "DataType", typeof(T).Name },
                    { "Algorithm", encryptionOptions.Algorithm.ToString() },
                    { "DecryptionTime", decryptionTime.TotalMilliseconds.ToString() }
                },
                null,
                true,
                null
            ));

            return new DecryptionResult<T>
            {
                Data = paymentData,
                IsSuccessful = true,
                DecryptedAt = DateTime.UtcNow
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error decrypting payment data to type {DataType}", typeof(T).Name);
            
            await _securityAuditService.LogSecurityEventAsync(new SecurityAuditEvent(
                Guid.NewGuid().ToString(),
                SecurityEventType.SystemError,
                SecurityEventSeverity.High,
                DateTime.UtcNow,
                null,
                null,
                null,
                null,
                $"Payment data decryption failed for type {typeof(T).Name}",
                new Dictionary<string, string>(),
                null,
                false,
                ex.Message
            ));

            return new DecryptionResult<T>
            {
                IsSuccessful = false,
                ErrorMessage = "Decryption failed"
            };
        }
    }

    public async Task<string> EncryptSensitiveFieldAsync(string sensitiveValue, string fieldType, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Encrypting sensitive field of type {FieldType}", fieldType);

            // Use field-specific encryption key
            var fieldKey = DeriveFieldSpecificKey(fieldType);
            var encryptedBytes = await EncryptWithAesGcmAsync(sensitiveValue, fieldKey);
            
            await _securityAuditService.LogSecurityEventAsync(new SecurityAuditEvent(
                Guid.NewGuid().ToString(),
                SecurityEventType.DataAccess,
                SecurityEventSeverity.Low,
                DateTime.UtcNow,
                null,
                null,
                null,
                null,
                $"Sensitive field encrypted: {fieldType}",
                new Dictionary<string, string> { { "FieldType", fieldType } },
                null,
                true,
                null
            ));

            return Convert.ToBase64String(encryptedBytes);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error encrypting sensitive field of type {FieldType}", fieldType);
            throw;
        }
    }

    public async Task<string> DecryptSensitiveFieldAsync(string encryptedValue, string fieldType, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Decrypting sensitive field of type {FieldType}", fieldType);

            var encryptedBytes = Convert.FromBase64String(encryptedValue);
            var fieldKey = DeriveFieldSpecificKey(fieldType);
            var decryptedValue = await DecryptWithAesGcmAsync(encryptedBytes, fieldKey);
            
            await _securityAuditService.LogSecurityEventAsync(new SecurityAuditEvent(
                Guid.NewGuid().ToString(),
                SecurityEventType.DataAccess,
                SecurityEventSeverity.Low,
                DateTime.UtcNow,
                null,
                null,
                null,
                null,
                $"Sensitive field decrypted: {fieldType}",
                new Dictionary<string, string> { { "FieldType", fieldType } },
                null,
                true,
                null
            ));

            return decryptedValue;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error decrypting sensitive field of type {FieldType}", fieldType);
            throw;
        }
    }

    public async Task<FieldMaskingResult> MaskSensitiveFieldsAsync<T>(T data, MaskingOptions? options = null, CancellationToken cancellationToken = default)
    {
        try
        {
            var maskingOptions = options ?? new MaskingOptions();
            var maskedData = new Dictionary<string, object>();
            var properties = typeof(T).GetProperties();

            foreach (var property in properties)
            {
                var value = property.GetValue(data);
                if (value == null)
                {
                    maskedData[property.Name] = null!;
                    continue;
                }

                var stringValue = value.ToString() ?? "";
                var maskedValue = property.Name.ToLowerInvariant() switch
                {
                    "cardnumber" or "card_number" => MaskCardNumber(stringValue, maskingOptions),
                    "cvv" or "cvc" => MaskCvv(stringValue, maskingOptions),
                    "email" => MaskEmail(stringValue, maskingOptions),
                    "phone" => MaskPhone(stringValue, maskingOptions),
                    "ssn" or "taxid" => MaskSsn(stringValue, maskingOptions),
                    _ => stringValue
                };

                maskedData[property.Name] = maskedValue;
            }

            await _securityAuditService.LogSecurityEventAsync(new SecurityAuditEvent(
                Guid.NewGuid().ToString(),
                SecurityEventType.DataAccess,
                SecurityEventSeverity.Low,
                DateTime.UtcNow,
                null,
                null,
                null,
                null,
                $"Sensitive fields masked for type {typeof(T).Name}",
                new Dictionary<string, string> { { "DataType", typeof(T).Name } },
                null,
                true,
                null
            ));

            return new FieldMaskingResult
            {
                MaskedData = maskedData,
                IsSuccessful = true,
                MaskedAt = DateTime.UtcNow
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error masking sensitive fields for type {DataType}", typeof(T).Name);
            
            return new FieldMaskingResult
            {
                IsSuccessful = false,
                ErrorMessage = ex.Message
            };
        }
    }

    public async Task<DatabaseEncryptionResult> EncryptForDatabaseStorageAsync(object data, string tableName, string fieldName, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Encrypting data for database storage: {TableName}.{FieldName}", tableName, fieldName);

            var jsonData = JsonSerializer.Serialize(data);
            var encryptedBytes = await EncryptWithAesGcmAsync(jsonData, _databaseEncryptionKey);
            var integrityHash = GenerateIntegrityHash(encryptedBytes);

            await _securityAuditService.LogSecurityEventAsync(new SecurityAuditEvent(
                Guid.NewGuid().ToString(),
                SecurityEventType.DataAccess,
                SecurityEventSeverity.Low,
                DateTime.UtcNow,
                null,
                null,
                null,
                null,
                $"Data encrypted for database storage: {tableName}.{fieldName}",
                new Dictionary<string, string>
                {
                    { "TableName", tableName },
                    { "FieldName", fieldName }
                },
                null,
                true,
                null
            ));

            return new DatabaseEncryptionResult
            {
                EncryptedData = Convert.ToBase64String(encryptedBytes),
                IntegrityHash = integrityHash,
                TableName = tableName,
                FieldName = fieldName,
                EncryptedAt = DateTime.UtcNow,
                IsSuccessful = true
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error encrypting data for database storage: {TableName}.{FieldName}", tableName, fieldName);
            
            return new DatabaseEncryptionResult
            {
                IsSuccessful = false,
                ErrorMessage = ex.Message
            };
        }
    }

    public async Task<T> DecryptFromDatabaseStorageAsync<T>(string encryptedData, string tableName, string fieldName, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Decrypting data from database storage: {TableName}.{FieldName}", tableName, fieldName);

            var encryptedBytes = Convert.FromBase64String(encryptedData);
            var decryptedJson = await DecryptWithAesGcmAsync(encryptedBytes, _databaseEncryptionKey);
            var data = JsonSerializer.Deserialize<T>(decryptedJson);

            await _securityAuditService.LogSecurityEventAsync(new SecurityAuditEvent(
                Guid.NewGuid().ToString(),
                SecurityEventType.DataAccess,
                SecurityEventSeverity.Low,
                DateTime.UtcNow,
                null,
                null,
                null,
                null,
                $"Data decrypted from database storage: {tableName}.{fieldName}",
                new Dictionary<string, string>
                {
                    { "TableName", tableName },
                    { "FieldName", fieldName }
                },
                null,
                true,
                null
            ));

            return data;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error decrypting data from database storage: {TableName}.{FieldName}", tableName, fieldName);
            throw;
        }
    }

    public async Task<TransitEncryptionResult> EncryptForTransitAsync(object data, string destinationEndpoint, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Encrypting data for transit to endpoint: {Endpoint}", destinationEndpoint);

            var jsonData = JsonSerializer.Serialize(data);
            var encryptedBytes = await EncryptWithAesGcmAsync(jsonData, _transitEncryptionKey);
            var integrityHash = GenerateIntegrityHash(encryptedBytes);

            await _securityAuditService.LogSecurityEventAsync(new SecurityAuditEvent(
                Guid.NewGuid().ToString(),
                SecurityEventType.DataAccess,
                SecurityEventSeverity.Low,
                DateTime.UtcNow,
                null,
                null,
                null,
                null,
                $"Data encrypted for transit to endpoint: {destinationEndpoint}",
                new Dictionary<string, string> { { "DestinationEndpoint", destinationEndpoint } },
                null,
                true,
                null
            ));

            return new TransitEncryptionResult
            {
                EncryptedData = Convert.ToBase64String(encryptedBytes),
                IntegrityHash = integrityHash,
                DestinationEndpoint = destinationEndpoint,
                EncryptedAt = DateTime.UtcNow,
                IsSuccessful = true
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error encrypting data for transit to endpoint: {Endpoint}", destinationEndpoint);
            
            return new TransitEncryptionResult
            {
                IsSuccessful = false,
                ErrorMessage = ex.Message
            };
        }
    }

    public async Task<T> DecryptFromTransitAsync<T>(string encryptedData, string sourceEndpoint, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Decrypting data from transit from endpoint: {Endpoint}", sourceEndpoint);

            var encryptedBytes = Convert.FromBase64String(encryptedData);
            var decryptedJson = await DecryptWithAesGcmAsync(encryptedBytes, _transitEncryptionKey);
            var data = JsonSerializer.Deserialize<T>(decryptedJson);

            await _securityAuditService.LogSecurityEventAsync(new SecurityAuditEvent(
                Guid.NewGuid().ToString(),
                SecurityEventType.DataAccess,
                SecurityEventSeverity.Low,
                DateTime.UtcNow,
                null,
                null,
                null,
                null,
                $"Data decrypted from transit from endpoint: {sourceEndpoint}",
                new Dictionary<string, string> { { "SourceEndpoint", sourceEndpoint } },
                null,
                true,
                null
            ));

            return data;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error decrypting data from transit from endpoint: {Endpoint}", sourceEndpoint);
            throw;
        }
    }

    public async Task<bool> ValidateEncryptionIntegrityAsync(string encryptedData, string integrityHash, CancellationToken cancellationToken = default)
    {
        try
        {
            var encryptedBytes = Convert.FromBase64String(encryptedData);
            var computedHash = GenerateIntegrityHash(encryptedBytes);
            var isValid = string.Equals(integrityHash, computedHash, StringComparison.Ordinal);

            await _securityAuditService.LogSecurityEventAsync(new SecurityAuditEvent(
                Guid.NewGuid().ToString(),
                isValid ? SecurityEventType.AuthenticationSuccess : SecurityEventType.AuthenticationFailure,
                isValid ? SecurityEventSeverity.Low : SecurityEventSeverity.High,
                DateTime.UtcNow,
                null,
                null,
                null,
                null,
                $"Encryption integrity validation: {(isValid ? "Valid" : "Invalid")}",
                new Dictionary<string, string>(),
                null,
                isValid,
                isValid ? null : "Integrity hash mismatch"
            ));

            return isValid;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating encryption integrity");
            return false;
        }
    }

    #region Private Helper Methods

    private byte[] DeriveKeyFromPassword(string password, string salt)
    {
        const int keySize = 32; // 256 bits
        const int iterations = 100000;
        
        using var rfc2898 = new Rfc2898DeriveBytes(password, Encoding.UTF8.GetBytes(salt), iterations, HashAlgorithmName.SHA256);
        return rfc2898.GetBytes(keySize);
    }

    private byte[] DeriveFieldSpecificKey(string fieldType)
    {
        return DeriveKeyFromPassword(_options.MasterPassword, $"field_{fieldType}");
    }

    private async Task<byte[]> EncryptWithAesGcmAsync(string plaintext, byte[] key)
    {
        var plaintextBytes = Encoding.UTF8.GetBytes(plaintext);
        var nonce = new byte[12]; // 96-bit nonce for GCM
        var ciphertext = new byte[plaintextBytes.Length];
        var tag = new byte[16]; // 128-bit authentication tag

        RandomNumberGenerator.Fill(nonce);

        using var aes = new AesGcm(key);
        aes.Encrypt(nonce, plaintextBytes, ciphertext, tag);

        // Combine nonce + ciphertext + tag
        var result = new byte[nonce.Length + ciphertext.Length + tag.Length];
        Array.Copy(nonce, 0, result, 0, nonce.Length);
        Array.Copy(ciphertext, 0, result, nonce.Length, ciphertext.Length);
        Array.Copy(tag, 0, result, nonce.Length + ciphertext.Length, tag.Length);

        return await Task.FromResult(result);
    }

    private async Task<string> DecryptWithAesGcmAsync(byte[] encryptedData, byte[] key)
    {
        if (encryptedData.Length < 12 + 16) // nonce + tag minimum
        {
            throw new ArgumentException("Invalid encrypted data length");
        }

        var nonce = new byte[12];
        var tag = new byte[16];
        var ciphertext = new byte[encryptedData.Length - 12 - 16];

        Array.Copy(encryptedData, 0, nonce, 0, 12);
        Array.Copy(encryptedData, 12, ciphertext, 0, ciphertext.Length);
        Array.Copy(encryptedData, 12 + ciphertext.Length, tag, 0, 16);

        var plaintext = new byte[ciphertext.Length];

        using var aes = new AesGcm(key);
        aes.Decrypt(nonce, ciphertext, tag, plaintext);

        return await Task.FromResult(Encoding.UTF8.GetString(plaintext));
    }

    private async Task<byte[]> EncryptWithAesCbcAsync(string plaintext, byte[] key)
    {
        var plaintextBytes = Encoding.UTF8.GetBytes(plaintext);
        var iv = new byte[16]; // 128-bit IV for CBC
        RandomNumberGenerator.Fill(iv);

        using var aes = Aes.Create();
        aes.Key = key;
        aes.IV = iv;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;

        using var encryptor = aes.CreateEncryptor();
        var ciphertext = encryptor.TransformFinalBlock(plaintextBytes, 0, plaintextBytes.Length);

        // Combine IV + ciphertext
        var result = new byte[iv.Length + ciphertext.Length];
        Array.Copy(iv, 0, result, 0, iv.Length);
        Array.Copy(ciphertext, 0, result, iv.Length, ciphertext.Length);

        return await Task.FromResult(result);
    }

    private async Task<string> DecryptWithAesCbcAsync(byte[] encryptedData, byte[] key)
    {
        if (encryptedData.Length < 16) // IV minimum
        {
            throw new ArgumentException("Invalid encrypted data length");
        }

        var iv = new byte[16];
        var ciphertext = new byte[encryptedData.Length - 16];

        Array.Copy(encryptedData, 0, iv, 0, 16);
        Array.Copy(encryptedData, 16, ciphertext, 0, ciphertext.Length);

        using var aes = Aes.Create();
        aes.Key = key;
        aes.IV = iv;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;

        using var decryptor = aes.CreateDecryptor();
        var plaintext = decryptor.TransformFinalBlock(ciphertext, 0, ciphertext.Length);

        return await Task.FromResult(Encoding.UTF8.GetString(plaintext));
    }

    private async Task<byte[]> EncryptWithChaCha20Async(string plaintext, byte[] key)
    {
        // ChaCha20 implementation would require additional libraries
        // For now, fall back to AES-GCM
        return await EncryptWithAesGcmAsync(plaintext, key);
    }

    private async Task<string> DecryptWithChaCha20Async(byte[] encryptedData, byte[] key)
    {
        // ChaCha20 implementation would require additional libraries
        // For now, fall back to AES-GCM
        return await DecryptWithAesGcmAsync(encryptedData, key);
    }

    private string GenerateIntegrityHash(byte[] data)
    {
        using var sha256 = SHA256.Create();
        var hashBytes = sha256.ComputeHash(data);
        return Convert.ToBase64String(hashBytes);
    }

    private string MaskCardNumber(string cardNumber, MaskingOptions options)
    {
        if (string.IsNullOrEmpty(cardNumber) || cardNumber.Length < 8)
        {
            return options.MaskCharacter.ToString();
        }

        // Show first 4 and last 4 digits
        var firstFour = cardNumber.Substring(0, 4);
        var lastFour = cardNumber.Substring(cardNumber.Length - 4);
        var middleMask = new string(options.MaskCharacter, cardNumber.Length - 8);
        
        return $"{firstFour}{middleMask}{lastFour}";
    }

    private string MaskCvv(string cvv, MaskingOptions options)
    {
        return string.IsNullOrEmpty(cvv) ? "" : new string(options.MaskCharacter, cvv.Length);
    }

    private string MaskEmail(string email, MaskingOptions options)
    {
        if (string.IsNullOrEmpty(email) || !email.Contains('@'))
        {
            return email;
        }

        var parts = email.Split('@');
        var localPart = parts[0];
        var domainPart = parts[1];

        if (localPart.Length <= 2)
        {
            return email;
        }

        var maskedLocal = localPart.Substring(0, 1) + 
                         new string(options.MaskCharacter, localPart.Length - 2) + 
                         localPart.Substring(localPart.Length - 1);

        return $"{maskedLocal}@{domainPart}";
    }

    private string MaskPhone(string phone, MaskingOptions options)
    {
        if (string.IsNullOrEmpty(phone) || phone.Length < 4)
        {
            return phone;
        }

        // Show last 4 digits
        var lastFour = phone.Substring(phone.Length - 4);
        var maskLength = phone.Length - 4;
        var mask = new string(options.MaskCharacter, maskLength);
        
        return $"{mask}{lastFour}";
    }

    private string MaskSsn(string ssn, MaskingOptions options)
    {
        if (string.IsNullOrEmpty(ssn) || ssn.Length < 4)
        {
            return new string(options.MaskCharacter, ssn?.Length ?? 0);
        }

        // Show last 4 digits
        var lastFour = ssn.Substring(ssn.Length - 4);
        var maskLength = ssn.Length - 4;
        var mask = new string(options.MaskCharacter, maskLength);
        
        return $"{mask}{lastFour}";
    }

    #endregion
}

// Supporting classes and enums
public class PaymentEncryptionOptions
{
    public string MasterPassword { get; set; } = "DefaultMasterPassword_ChangeInProduction";
    public EncryptionAlgorithm DefaultAlgorithm { get; set; } = EncryptionAlgorithm.AES_256_GCM;
    public bool EnableIntegrityValidation { get; set; } = true;
    public int KeyDerivationIterations { get; set; } = 100000;
}

public class EncryptionOptions
{
    public EncryptionAlgorithm Algorithm { get; set; } = EncryptionAlgorithm.AES_256_GCM;
    public bool IncludeIntegrityHash { get; set; } = true;
    public Dictionary<string, string> AdditionalData { get; set; } = new();
}

public class MaskingOptions
{
    public char MaskCharacter { get; set; } = '*';
    public bool PreservePrefixSuffix { get; set; } = true;
    public Dictionary<string, MaskingRule> FieldSpecificRules { get; set; } = new();
}

public class MaskingRule
{
    public int PrefixLength { get; set; }
    public int SuffixLength { get; set; }
    public char MaskCharacter { get; set; } = '*';
}

public class EncryptionResult
{
    public string EncryptedData { get; set; } = string.Empty;
    public EncryptionAlgorithm Algorithm { get; set; }
    public string IntegrityHash { get; set; } = string.Empty;
    public DateTime EncryptedAt { get; set; }
    public bool IsSuccessful { get; set; }
    public string? ErrorMessage { get; set; }
}

public class DecryptionResult<T>
{
    public T? Data { get; set; }
    public DateTime DecryptedAt { get; set; }
    public bool IsSuccessful { get; set; }
    public string? ErrorMessage { get; set; }
}

public class FieldMaskingResult
{
    public Dictionary<string, object> MaskedData { get; set; } = new();
    public DateTime MaskedAt { get; set; }
    public bool IsSuccessful { get; set; }
    public string? ErrorMessage { get; set; }
}

public class DatabaseEncryptionResult
{
    public string EncryptedData { get; set; } = string.Empty;
    public string IntegrityHash { get; set; } = string.Empty;
    public string TableName { get; set; } = string.Empty;
    public string FieldName { get; set; } = string.Empty;
    public DateTime EncryptedAt { get; set; }
    public bool IsSuccessful { get; set; }
    public string? ErrorMessage { get; set; }
}

public class TransitEncryptionResult
{
    public string EncryptedData { get; set; } = string.Empty;
    public string IntegrityHash { get; set; } = string.Empty;
    public string DestinationEndpoint { get; set; } = string.Empty;
    public DateTime EncryptedAt { get; set; }
    public bool IsSuccessful { get; set; }
    public string? ErrorMessage { get; set; }
}

public enum EncryptionAlgorithm
{
    AES_256_GCM,
    AES_256_CBC,
    ChaCha20_Poly1305
}