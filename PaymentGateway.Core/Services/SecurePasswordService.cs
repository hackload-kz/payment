using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Security.Cryptography;
using System.Text;
using System.Collections.Concurrent;

namespace PaymentGateway.Core.Services;

public interface ISecurePasswordService
{
    string HashPassword(string password);
    bool VerifyPassword(string password, string hashedPassword);
    Task<string> EncryptSensitiveDataAsync(string data, CancellationToken cancellationToken = default);
    Task<string> DecryptSensitiveDataAsync(string encryptedData, CancellationToken cancellationToken = default);
    Task<PasswordStrengthResult> ValidatePasswordStrengthAsync(string password);
    Task<bool> IsPasswordCompromisedAsync(string password, CancellationToken cancellationToken = default);
    string GenerateSecurePassword(int length = 16);
    Task<PasswordRotationResult> RotatePasswordAsync(string teamSlug, string oldPassword, string newPassword);
}

public record PasswordStrengthResult(
    bool IsStrong,
    int Score, // 0-100
    List<string> Weaknesses,
    List<string> Suggestions);

public record PasswordRotationResult(
    bool IsSuccessful,
    string? NewHashedPassword,
    DateTime? RotationDate,
    string? ErrorMessage);

public class SecurePasswordOptions
{
    public int MinPasswordLength { get; set; } = 12;
    public int MaxPasswordLength { get; set; } = 128;
    public bool RequireUppercase { get; set; } = true;
    public bool RequireLowercase { get; set; } = true;
    public bool RequireDigits { get; set; } = true;
    public bool RequireSpecialCharacters { get; set; } = true;
    public int MinUniqueCharacters { get; set; } = 8;
    public List<string> ProhibitedPasswords { get; set; } = new()
    {
        "password", "123456", "qwerty", "admin", "root", "test"
    };
    public bool EnablePasswordRotation { get; set; } = true;
    public TimeSpan PasswordRotationInterval { get; set; } = TimeSpan.FromDays(90);
    public int PasswordHistorySize { get; set; } = 5;
    public string EncryptionKey { get; set; } = "payment-gateway-encryption-key-2024"; // Should be from secure config
}

public class SecurePasswordService : ISecurePasswordService
{
    private readonly ILogger<SecurePasswordService> _logger;
    private readonly SecurePasswordOptions _options;
    
    // In-memory storage for password history (in production, use database)
    private readonly ConcurrentDictionary<string, List<PasswordHistoryEntry>> _passwordHistory;
    private readonly ConcurrentDictionary<string, DateTime> _lastPasswordRotation;

    // Characters for password generation
    private const string LowercaseChars = "abcdefghijklmnopqrstuvwxyz";
    private const string UppercaseChars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
    private const string DigitChars = "0123456789";
    private const string SpecialChars = "!@#$%^&*()_+-=[]{}\\|;:,.<>?";

    public SecurePasswordService(
        ILogger<SecurePasswordService> logger,
        IOptions<SecurePasswordOptions> options)
    {
        _logger = logger;
        _options = options.Value;
        _passwordHistory = new ConcurrentDictionary<string, List<PasswordHistoryEntry>>();
        _lastPasswordRotation = new ConcurrentDictionary<string, DateTime>();
    }

    public string HashPassword(string password)
    {
        ArgumentException.ThrowIfNullOrEmpty(password);

        try
        {
            // Generate a random salt
            var salt = new byte[16];
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(salt);
            }

            // Hash the password using PBKDF2 with SHA-256
            using var pbkdf2 = new Rfc2898DeriveBytes(password, salt, 100000, HashAlgorithmName.SHA256);
            var hash = pbkdf2.GetBytes(32);

            // Combine salt and hash
            var combined = new byte[48];
            Array.Copy(salt, 0, combined, 0, 16);
            Array.Copy(hash, 0, combined, 16, 32);

            // Return base64 encoded result
            return Convert.ToBase64String(combined);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to hash password");
            throw new InvalidOperationException("Password hashing failed", ex);
        }
    }

    public bool VerifyPassword(string password, string hashedPassword)
    {
        ArgumentException.ThrowIfNullOrEmpty(password);
        ArgumentException.ThrowIfNullOrEmpty(hashedPassword);

        try
        {
            // Decode the stored hash
            var combined = Convert.FromBase64String(hashedPassword);
            if (combined.Length != 48)
            {
                return false;
            }

            // Extract salt and hash
            var salt = new byte[16];
            var storedHash = new byte[32];
            Array.Copy(combined, 0, salt, 0, 16);
            Array.Copy(combined, 16, storedHash, 0, 32);

            // Hash the provided password with the stored salt
            using var pbkdf2 = new Rfc2898DeriveBytes(password, salt, 100000, HashAlgorithmName.SHA256);
            var testHash = pbkdf2.GetBytes(32);

            // Compare hashes using constant-time comparison
            return CryptographicOperations.FixedTimeEquals(storedHash, testHash);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to verify password");
            return false;
        }
    }

    public async Task<string> EncryptSensitiveDataAsync(string data, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(data);

        try
        {
            using var aes = Aes.Create();
            aes.Key = DeriveKeyFromString(_options.EncryptionKey);
            aes.GenerateIV();

            using var encryptor = aes.CreateEncryptor();
            var dataBytes = Encoding.UTF8.GetBytes(data);
            var encryptedBytes = await Task.Run(() => encryptor.TransformFinalBlock(dataBytes, 0, dataBytes.Length), cancellationToken);

            // Combine IV and encrypted data
            var result = new byte[aes.IV.Length + encryptedBytes.Length];
            Array.Copy(aes.IV, 0, result, 0, aes.IV.Length);
            Array.Copy(encryptedBytes, 0, result, aes.IV.Length, encryptedBytes.Length);

            return Convert.ToBase64String(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to encrypt sensitive data");
            throw new InvalidOperationException("Data encryption failed", ex);
        }
    }

    public async Task<string> DecryptSensitiveDataAsync(string encryptedData, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(encryptedData);

        try
        {
            var combinedBytes = Convert.FromBase64String(encryptedData);
            
            using var aes = Aes.Create();
            aes.Key = DeriveKeyFromString(_options.EncryptionKey);

            // Extract IV and encrypted data
            var iv = new byte[16];
            var encrypted = new byte[combinedBytes.Length - 16];
            Array.Copy(combinedBytes, 0, iv, 0, 16);
            Array.Copy(combinedBytes, 16, encrypted, 0, encrypted.Length);

            aes.IV = iv;

            using var decryptor = aes.CreateDecryptor();
            var decryptedBytes = await Task.Run(() => decryptor.TransformFinalBlock(encrypted, 0, encrypted.Length), cancellationToken);

            return Encoding.UTF8.GetString(decryptedBytes);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to decrypt sensitive data");
            throw new InvalidOperationException("Data decryption failed", ex);
        }
    }

    public async Task<PasswordStrengthResult> ValidatePasswordStrengthAsync(string password)
    {
        var weaknesses = new List<string>();
        var suggestions = new List<string>();
        var score = 0;

        if (string.IsNullOrEmpty(password))
        {
            return new PasswordStrengthResult(false, 0, 
                new List<string> { "Password is required" },
                new List<string> { "Please provide a password" });
        }

        // Length check
        if (password.Length < _options.MinPasswordLength)
        {
            weaknesses.Add($"Password is too short (minimum {_options.MinPasswordLength} characters)");
            suggestions.Add($"Use at least {_options.MinPasswordLength} characters");
        }
        else if (password.Length >= _options.MinPasswordLength)
        {
            score += 20;
        }

        if (password.Length > _options.MaxPasswordLength)
        {
            weaknesses.Add($"Password is too long (maximum {_options.MaxPasswordLength} characters)");
        }

        // Character diversity checks
        var hasLowercase = password.Any(char.IsLower);
        var hasUppercase = password.Any(char.IsUpper);
        var hasDigit = password.Any(char.IsDigit);
        var hasSpecial = password.Any(c => !char.IsLetterOrDigit(c));

        if (_options.RequireLowercase && !hasLowercase)
        {
            weaknesses.Add("Password must contain lowercase letters");
            suggestions.Add("Add lowercase letters (a-z)");
        }
        else if (hasLowercase)
        {
            score += 15;
        }

        if (_options.RequireUppercase && !hasUppercase)
        {
            weaknesses.Add("Password must contain uppercase letters");
            suggestions.Add("Add uppercase letters (A-Z)");
        }
        else if (hasUppercase)
        {
            score += 15;
        }

        if (_options.RequireDigits && !hasDigit)
        {
            weaknesses.Add("Password must contain digits");
            suggestions.Add("Add numbers (0-9)");
        }
        else if (hasDigit)
        {
            score += 15;
        }

        if (_options.RequireSpecialCharacters && !hasSpecial)
        {
            weaknesses.Add("Password must contain special characters");
            suggestions.Add("Add special characters (!@#$%^&*)");
        }
        else if (hasSpecial)
        {
            score += 15;
        }

        // Unique character count
        var uniqueChars = password.Distinct().Count();
        if (uniqueChars < _options.MinUniqueCharacters)
        {
            weaknesses.Add($"Password must have at least {_options.MinUniqueCharacters} unique characters");
            suggestions.Add("Use more varied characters");
        }
        else
        {
            score += 10;
        }

        // Common password check
        var lowerPassword = password.ToLowerInvariant();
        if (_options.ProhibitedPasswords.Any(prohibited => lowerPassword.Contains(prohibited.ToLowerInvariant())))
        {
            weaknesses.Add("Password contains common words or patterns");
            suggestions.Add("Avoid common words and patterns");
            score -= 20;
        }

        // Pattern detection
        if (HasRepeatingPatterns(password))
        {
            weaknesses.Add("Password contains repeating patterns");
            suggestions.Add("Avoid repeating sequences");
            score -= 10;
        }

        if (HasSequentialPatterns(password))
        {
            weaknesses.Add("Password contains sequential patterns");
            suggestions.Add("Avoid sequential characters (abc, 123)");
            score -= 10;
        }

        // Complexity bonus
        if (password.Length > 16 && uniqueChars > 12)
        {
            score += 10; // Bonus for very complex passwords
        }

        // Ensure score is within 0-100 range
        score = Math.Max(0, Math.Min(100, score));
        var isStrong = score >= 70 && weaknesses.Count == 0;

        if (isStrong)
        {
            suggestions.Clear();
            suggestions.Add("Password meets all security requirements");
        }

        return await Task.FromResult(new PasswordStrengthResult(isStrong, score, weaknesses, suggestions));
    }

    public async Task<bool> IsPasswordCompromisedAsync(string password, CancellationToken cancellationToken = default)
    {
        // In a real implementation, this would check against known compromised password databases
        // like HaveIBeenPwned API or local breach databases
        
        // For this implementation, we'll check against a simple list of known bad passwords
        var knownBadPasswords = new[]
        {
            "password", "123456", "123456789", "qwerty", "password123",
            "1234567", "12345678", "12345", "iloveyou", "111111",
            "123123", "abc123", "qwerty123", "1q2w3e4r", "admin",
            "qwertyuiop", "654321", "555666", "lovely", "7777777",
            "welcome", "888888", "princess", "dragon", "password1",
            "123qwe", "sunshine", "master", "1234567890", "shadow"
        };

        var isCompromised = knownBadPasswords.Contains(password.ToLowerInvariant());
        
        return await Task.FromResult(isCompromised);
    }

    public string GenerateSecurePassword(int length = 16)
    {
        if (length < _options.MinPasswordLength)
            length = _options.MinPasswordLength;

        if (length > _options.MaxPasswordLength)
            length = _options.MaxPasswordLength;

        var allChars = new StringBuilder();
        var password = new StringBuilder();

        // Add required character types first
        if (_options.RequireLowercase)
        {
            allChars.Append(LowercaseChars);
            password.Append(GetRandomChar(LowercaseChars));
        }

        if (_options.RequireUppercase)
        {
            allChars.Append(UppercaseChars);
            password.Append(GetRandomChar(UppercaseChars));
        }

        if (_options.RequireDigits)
        {
            allChars.Append(DigitChars);
            password.Append(GetRandomChar(DigitChars));
        }

        if (_options.RequireSpecialCharacters)
        {
            allChars.Append(SpecialChars);
            password.Append(GetRandomChar(SpecialChars));
        }

        // Fill remaining length with random characters from all allowed characters
        var allCharsString = allChars.ToString();
        while (password.Length < length)
        {
            password.Append(GetRandomChar(allCharsString));
        }

        // Shuffle the password characters
        return ShuffleString(password.ToString());
    }

    public async Task<PasswordRotationResult> RotatePasswordAsync(string teamSlug, string oldPassword, string newPassword)
    {
        try
        {
            // Validate new password strength
            var strengthResult = await ValidatePasswordStrengthAsync(newPassword);
            if (!strengthResult.IsStrong)
            {
                return new PasswordRotationResult(false, null, null, 
                    $"New password does not meet strength requirements: {string.Join(", ", strengthResult.Weaknesses)}");
            }

            // Check if password was recently used
            if (await IsPasswordRecentlyUsedAsync(teamSlug, newPassword))
            {
                return new PasswordRotationResult(false, null, null, 
                    "New password was recently used. Please choose a different password.");
            }

            // Check if password is compromised
            if (await IsPasswordCompromisedAsync(newPassword))
            {
                return new PasswordRotationResult(false, null, null, 
                    "New password appears in known breached password lists. Please choose a different password.");
            }

            // Hash the new password
            var hashedPassword = HashPassword(newPassword);

            // Record in password history
            await RecordPasswordInHistoryAsync(teamSlug, hashedPassword);

            // Update rotation date
            _lastPasswordRotation[teamSlug] = DateTime.UtcNow;

            _logger.LogInformation("Password rotated successfully for team {TeamSlug}", teamSlug);

            return new PasswordRotationResult(true, hashedPassword, DateTime.UtcNow, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to rotate password for team {TeamSlug}", teamSlug);
            return new PasswordRotationResult(false, null, null, "Password rotation failed due to internal error");
        }
    }

    private static byte[] DeriveKeyFromString(string keyString)
    {
        using var sha256 = SHA256.Create();
        return sha256.ComputeHash(Encoding.UTF8.GetBytes(keyString));
    }

    private static char GetRandomChar(string chars)
    {
        using var rng = RandomNumberGenerator.Create();
        var randomBytes = new byte[4];
        rng.GetBytes(randomBytes);
        var random = BitConverter.ToUInt32(randomBytes, 0);
        return chars[(int)(random % chars.Length)];
    }

    private static string ShuffleString(string input)
    {
        var array = input.ToCharArray();
        using var rng = RandomNumberGenerator.Create();
        
        for (int i = array.Length - 1; i > 0; i--)
        {
            var randomBytes = new byte[4];
            rng.GetBytes(randomBytes);
            var j = (int)(BitConverter.ToUInt32(randomBytes, 0) % (i + 1));
            (array[i], array[j]) = (array[j], array[i]);
        }
        
        return new string(array);
    }

    private static bool HasRepeatingPatterns(string password)
    {
        // Check for repeating sequences of 3+ characters
        for (int i = 0; i <= password.Length - 6; i++)
        {
            var pattern = password.Substring(i, 3);
            if (password.Substring(i + 3, Math.Min(3, password.Length - i - 3)) == pattern)
            {
                return true;
            }
        }
        return false;
    }

    private static bool HasSequentialPatterns(string password)
    {
        // Check for sequential characters (abc, 123, etc.)
        for (int i = 0; i <= password.Length - 3; i++)
        {
            var char1 = password[i];
            var char2 = password[i + 1];
            var char3 = password[i + 2];

            if (char2 == char1 + 1 && char3 == char2 + 1)
            {
                return true;
            }
        }
        return false;
    }

    private async Task<bool> IsPasswordRecentlyUsedAsync(string teamSlug, string password)
    {
        if (!_passwordHistory.TryGetValue(teamSlug, out var history))
            return false;

        foreach (var entry in history.TakeLast(_options.PasswordHistorySize))
        {
            if (VerifyPassword(password, entry.HashedPassword))
            {
                return true;
            }
        }

        return await Task.FromResult(false);
    }

    private async Task RecordPasswordInHistoryAsync(string teamSlug, string hashedPassword)
    {
        var entry = new PasswordHistoryEntry(hashedPassword, DateTime.UtcNow);
        
        _passwordHistory.AddOrUpdate(teamSlug,
            new List<PasswordHistoryEntry> { entry },
            (_, existing) =>
            {
                existing.Add(entry);
                // Keep only the last N passwords
                return existing.TakeLast(_options.PasswordHistorySize).ToList();
            });

        await Task.CompletedTask;
    }

    private record PasswordHistoryEntry(string HashedPassword, DateTime CreatedAt);
}