using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text.RegularExpressions;
using System.Text;
using System.Web;

namespace PaymentGateway.Core.Services;

/// <summary>
/// Service interface for comprehensive input validation and sanitization
/// </summary>
public interface IComprehensiveInputValidationService
{
    Task<InputValidationResult> ValidateAndSanitizePaymentDataAsync<T>(T paymentData, ValidationOptions? options = null, CancellationToken cancellationToken = default);
    Task<InputValidationResult> ValidateCardNumberAsync(string cardNumber, CancellationToken cancellationToken = default);
    Task<InputValidationResult> ValidateCvvAsync(string cvv, string cardNumber, CancellationToken cancellationToken = default);
    Task<InputValidationResult> ValidateExpiryDateAsync(string expiryDate, CancellationToken cancellationToken = default);
    Task<SanitizationResult> SanitizeInputStringAsync(string input, SanitizationOptions? options = null, CancellationToken cancellationToken = default);
    Task<InputValidationResult> ValidateEmailAsync(string email, CancellationToken cancellationToken = default);
    Task<InputValidationResult> ValidatePhoneNumberAsync(string phoneNumber, string? countryCode = null, CancellationToken cancellationToken = default);
    Task<InputValidationResult> ValidateAmountAsync(decimal amount, string currency, CancellationToken cancellationToken = default);
    Task<AntiInjectionResult> DetectAndPreventInjectionAttacksAsync(string input, CancellationToken cancellationToken = default);
    Task<XssProtectionResult> ProtectAgainstXssAsync(string input, CancellationToken cancellationToken = default);
    Task<List<ValidationIssue>> PerformComprehensiveSecurityValidationAsync(object data, CancellationToken cancellationToken = default);
}

/// <summary>
/// Comprehensive input validation and sanitization service implementation
/// </summary>
public class ComprehensiveInputValidationService : IComprehensiveInputValidationService
{
    private readonly ILogger<ComprehensiveInputValidationService> _logger;
    private readonly ISecurityAuditService _securityAuditService;
    private readonly InputValidationOptions _options;

    // Regex patterns for validation
    private static readonly Regex CardNumberRegex = new(@"^[0-9]{13,19}$", RegexOptions.Compiled);
    private static readonly Regex CvvRegex = new(@"^[0-9]{3,4}$", RegexOptions.Compiled);
    private static readonly Regex ExpiryDateRegex = new(@"^(0[1-9]|1[0-2])\/([0-9]{2}|[0-9]{4})$", RegexOptions.Compiled);
    private static readonly Regex EmailRegex = new(@"^[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}$", RegexOptions.Compiled);
    private static readonly Regex PhoneRegex = new(@"^\+?[1-9]\d{1,14}$", RegexOptions.Compiled);
    
    // Security patterns
    private static readonly Regex SqlInjectionRegex = new(@"(\b(ALTER|CREATE|DELETE|DROP|EXEC(UTE)?|INSERT( +INTO)?|MERGE|SELECT|UPDATE|UNION( +ALL)?)\b)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex XssRegex = new(@"<[^>]*>|javascript:|vbscript:|on\w+\s*=", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex CommandInjectionRegex = new(@"[;&|`$(){}\\<>]", RegexOptions.Compiled);

    public ComprehensiveInputValidationService(
        ILogger<ComprehensiveInputValidationService> logger,
        ISecurityAuditService securityAuditService,
        IOptions<InputValidationOptions> options)
    {
        _logger = logger;
        _securityAuditService = securityAuditService;
        _options = options.Value;
    }

    public async Task<InputValidationResult> ValidateAndSanitizePaymentDataAsync<T>(
        T paymentData, 
        ValidationOptions? options = null, 
        CancellationToken cancellationToken = default)
    {
        try
        {
            var validationOptions = options ?? new ValidationOptions();
            var startTime = DateTime.UtcNow;
            var issues = new List<ValidationIssue>();
            var sanitizedProperties = new Dictionary<string, object>();

            _logger.LogDebug("Validating and sanitizing payment data of type {DataType}", typeof(T).Name);

            var properties = typeof(T).GetProperties();
            
            foreach (var property in properties)
            {
                var value = property.GetValue(paymentData);
                if (value == null)
                {
                    sanitizedProperties[property.Name] = null!;
                    continue;
                }

                var stringValue = value.ToString() ?? "";
                var propertyName = property.Name.ToLowerInvariant();

                // Property-specific validation
                switch (propertyName)
                {
                    case "cardnumber" or "card_number":
                        var cardValidation = await ValidateCardNumberAsync(stringValue, cancellationToken);
                        if (!cardValidation.IsValid)
                        {
                            issues.AddRange(cardValidation.Issues);
                        }
                        sanitizedProperties[property.Name] = cardValidation.SanitizedValue ?? stringValue;
                        break;

                    case "cvv" or "cvc":
                        var cvvValidation = await ValidateCvvAsync(stringValue, "", cancellationToken);
                        if (!cvvValidation.IsValid)
                        {
                            issues.AddRange(cvvValidation.Issues);
                        }
                        sanitizedProperties[property.Name] = cvvValidation.SanitizedValue ?? stringValue;
                        break;

                    case "expirydate" or "expiry_date":
                        var expiryValidation = await ValidateExpiryDateAsync(stringValue, cancellationToken);
                        if (!expiryValidation.IsValid)
                        {
                            issues.AddRange(expiryValidation.Issues);
                        }
                        sanitizedProperties[property.Name] = expiryValidation.SanitizedValue ?? stringValue;
                        break;

                    case "email":
                        var emailValidation = await ValidateEmailAsync(stringValue, cancellationToken);
                        if (!emailValidation.IsValid)
                        {
                            issues.AddRange(emailValidation.Issues);
                        }
                        sanitizedProperties[property.Name] = emailValidation.SanitizedValue ?? stringValue;
                        break;

                    case "phone" or "phonenumber":
                        var phoneValidation = await ValidatePhoneNumberAsync(stringValue, null, cancellationToken);
                        if (!phoneValidation.IsValid)
                        {
                            issues.AddRange(phoneValidation.Issues);
                        }
                        sanitizedProperties[property.Name] = phoneValidation.SanitizedValue ?? stringValue;
                        break;

                    case "amount":
                        if (decimal.TryParse(stringValue, out var amount))
                        {
                            var amountValidation = await ValidateAmountAsync(amount, "USD", cancellationToken);
                            if (!amountValidation.IsValid)
                            {
                                issues.AddRange(amountValidation.Issues);
                            }
                        }
                        sanitizedProperties[property.Name] = value;
                        break;

                    default:
                        // General string sanitization
                        var sanitizationResult = await SanitizeInputStringAsync(stringValue, null, cancellationToken);
                        if (!sanitizationResult.IsClean)
                        {
                            issues.Add(new ValidationIssue
                            {
                                PropertyName = property.Name,
                                IssueType = ValidationIssueType.SecurityThreat,
                                Severity = ValidationSeverity.High,
                                Message = "Potentially malicious content detected",
                                OriginalValue = stringValue,
                                SuggestedValue = sanitizationResult.SanitizedValue
                            });
                        }
                        sanitizedProperties[property.Name] = sanitizationResult.SanitizedValue;
                        break;
                }
            }

            var validationTime = DateTime.UtcNow - startTime;
            var isValid = issues.Count == 0 || issues.All(i => i.Severity < ValidationSeverity.High);

            await _securityAuditService.LogSecurityEventAsync(new SecurityAuditEvent(
                Guid.NewGuid().ToString(),
                isValid ? SecurityEventType.AuthenticationSuccess : SecurityEventType.SecurityPolicyViolation,
                isValid ? SecurityEventSeverity.Low : SecurityEventSeverity.High,
                DateTime.UtcNow,
                null,
                null,
                null,
                null,
                $"Payment data validation for type {typeof(T).Name}: {(isValid ? "Valid" : "Invalid")}",
                new Dictionary<string, string>
                {
                    { "DataType", typeof(T).Name },
                    { "IssueCount", issues.Count.ToString() },
                    { "ValidationTime", validationTime.TotalMilliseconds.ToString() }
                },
                null,
                isValid,
                isValid ? null : $"{issues.Count} validation issues found"
            ));

            return new InputValidationResult
            {
                IsValid = isValid,
                Issues = issues,
                SanitizedData = sanitizedProperties,
                ValidationTime = validationTime
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating payment data of type {DataType}", typeof(T).Name);
            
            return new InputValidationResult
            {
                IsValid = false,
                Issues = new List<ValidationIssue>
                {
                    new ValidationIssue
                    {
                        IssueType = ValidationIssueType.SystemError,
                        Severity = ValidationSeverity.Critical,
                        Message = "Validation system error",
                        Details = ex.Message
                    }
                }
            };
        }
    }

    public async Task<InputValidationResult> ValidateCardNumberAsync(string cardNumber, CancellationToken cancellationToken = default)
    {
        var issues = new List<ValidationIssue>();
        var sanitizedCardNumber = SanitizeCardNumber(cardNumber);

        // Basic format validation
        if (string.IsNullOrWhiteSpace(sanitizedCardNumber))
        {
            issues.Add(new ValidationIssue
            {
                PropertyName = "CardNumber",
                IssueType = ValidationIssueType.Required,
                Severity = ValidationSeverity.High,
                Message = "Card number is required"
            });
        }
        else if (!CardNumberRegex.IsMatch(sanitizedCardNumber))
        {
            issues.Add(new ValidationIssue
            {
                PropertyName = "CardNumber",
                IssueType = ValidationIssueType.InvalidFormat,
                Severity = ValidationSeverity.High,
                Message = "Card number format is invalid"
            });
        }
        else if (!IsValidLuhnChecksum(sanitizedCardNumber))
        {
            issues.Add(new ValidationIssue
            {
                PropertyName = "CardNumber",
                IssueType = ValidationIssueType.InvalidChecksum,
                Severity = ValidationSeverity.High,
                Message = "Card number fails Luhn algorithm validation"
            });
        }

        // Card type validation
        var cardType = DetermineCardType(sanitizedCardNumber);
        if (cardType == PaymentCardType.Unknown)
        {
            issues.Add(new ValidationIssue
            {
                PropertyName = "CardNumber",
                IssueType = ValidationIssueType.UnsupportedPaymentCardType,
                Severity = ValidationSeverity.Medium,
                Message = "Card type is not supported or recognized"
            });
        }

        await LogValidationEventAsync("CardNumber", issues.Count == 0, $"Card type: {cardType}");

        return new InputValidationResult
        {
            IsValid = issues.Count == 0 || issues.All(i => i.Severity < ValidationSeverity.High),
            Issues = issues,
            SanitizedValue = sanitizedCardNumber
        };
    }

    public async Task<InputValidationResult> ValidateCvvAsync(string cvv, string cardNumber, CancellationToken cancellationToken = default)
    {
        var issues = new List<ValidationIssue>();
        var sanitizedCvv = SanitizeCvv(cvv);

        if (string.IsNullOrWhiteSpace(sanitizedCvv))
        {
            issues.Add(new ValidationIssue
            {
                PropertyName = "CVV",
                IssueType = ValidationIssueType.Required,
                Severity = ValidationSeverity.High,
                Message = "CVV is required"
            });
        }
        else if (!CvvRegex.IsMatch(sanitizedCvv))
        {
            issues.Add(new ValidationIssue
            {
                PropertyName = "CVV",
                IssueType = ValidationIssueType.InvalidFormat,
                Severity = ValidationSeverity.High,
                Message = "CVV format is invalid"
            });
        }
        else
        {
            // Validate CVV length based on card type
            var cardType = DetermineCardType(cardNumber);
            var expectedLength = cardType == PaymentCardType.AmericanExpress ? 4 : 3;
            
            if (sanitizedCvv.Length != expectedLength)
            {
                issues.Add(new ValidationIssue
                {
                    PropertyName = "CVV",
                    IssueType = ValidationIssueType.InvalidLength,
                    Severity = ValidationSeverity.High,
                    Message = $"CVV length should be {expectedLength} digits for {cardType} cards"
                });
            }
        }

        await LogValidationEventAsync("CVV", issues.Count == 0);

        return new InputValidationResult
        {
            IsValid = issues.Count == 0,
            Issues = issues,
            SanitizedValue = sanitizedCvv
        };
    }

    public async Task<InputValidationResult> ValidateExpiryDateAsync(string expiryDate, CancellationToken cancellationToken = default)
    {
        var issues = new List<ValidationIssue>();
        var sanitizedExpiry = SanitizeExpiryDate(expiryDate);

        if (string.IsNullOrWhiteSpace(sanitizedExpiry))
        {
            issues.Add(new ValidationIssue
            {
                PropertyName = "ExpiryDate",
                IssueType = ValidationIssueType.Required,
                Severity = ValidationSeverity.High,
                Message = "Expiry date is required"
            });
        }
        else if (!ExpiryDateRegex.IsMatch(sanitizedExpiry))
        {
            issues.Add(new ValidationIssue
            {
                PropertyName = "ExpiryDate",
                IssueType = ValidationIssueType.InvalidFormat,
                Severity = ValidationSeverity.High,
                Message = "Expiry date format is invalid (expected MM/YY or MM/YYYY)"
            });
        }
        else
        {
            // Validate expiry date is not in the past
            if (IsExpiryDateInPast(sanitizedExpiry))
            {
                issues.Add(new ValidationIssue
                {
                    PropertyName = "ExpiryDate",
                    IssueType = ValidationIssueType.Expired,
                    Severity = ValidationSeverity.High,
                    Message = "Card has expired"
                });
            }
        }

        await LogValidationEventAsync("ExpiryDate", issues.Count == 0);

        return new InputValidationResult
        {
            IsValid = issues.Count == 0,
            Issues = issues,
            SanitizedValue = sanitizedExpiry
        };
    }

    public async Task<SanitizationResult> SanitizeInputStringAsync(
        string input, 
        SanitizationOptions? options = null, 
        CancellationToken cancellationToken = default)
    {
        try
        {
            var sanitizationOptions = options ?? new SanitizationOptions();
            var originalInput = input;
            var sanitizedInput = input;
            var threats = new List<SecurityThreat>();

            if (string.IsNullOrEmpty(input))
            {
                return new SanitizationResult
                {
                    OriginalValue = originalInput,
                    SanitizedValue = "",
                    IsClean = true,
                    DetectedThreats = threats
                };
            }

            // HTML encode if requested
            if (sanitizationOptions.HtmlEncode)
            {
                sanitizedInput = HttpUtility.HtmlEncode(sanitizedInput);
            }

            // Remove or escape special characters
            if (sanitizationOptions.RemoveSpecialCharacters)
            {
                sanitizedInput = RemoveSpecialCharacters(sanitizedInput);
            }

            // Detect injection attempts
            var injectionResult = await DetectAndPreventInjectionAttacksAsync(sanitizedInput, cancellationToken);
            if (!injectionResult.IsSafe)
            {
                threats.AddRange(injectionResult.DetectedThreats);
                sanitizedInput = injectionResult.SanitizedValue;
            }

            // Detect XSS attempts
            var xssResult = await ProtectAgainstXssAsync(sanitizedInput, cancellationToken);
            if (!xssResult.IsSafe)
            {
                threats.AddRange(xssResult.DetectedThreats);
                sanitizedInput = xssResult.SanitizedValue;
            }

            // Limit string length
            if (sanitizationOptions.MaxLength > 0 && sanitizedInput.Length > sanitizationOptions.MaxLength)
            {
                sanitizedInput = sanitizedInput.Substring(0, sanitizationOptions.MaxLength);
                threats.Add(new SecurityThreat
                {
                    Type = ThreatType.ExcessiveLength,
                    Severity = ThreatSeverity.Low,
                    Description = "Input exceeded maximum allowed length"
                });
            }

            var isClean = threats.Count == 0 && string.Equals(originalInput, sanitizedInput, StringComparison.Ordinal);

            if (!isClean)
            {
                await _securityAuditService.LogSecurityEventAsync(new SecurityAuditEvent(
                    Guid.NewGuid().ToString(),
                    SecurityEventType.SuspiciousActivity,
                    SecurityEventSeverity.Medium,
                    DateTime.UtcNow,
                    null,
                    null,
                    null,
                    null,
                    $"Input sanitization required: {threats.Count} threats detected",
                    new Dictionary<string, string>
                    {
                        { "ThreatCount", threats.Count.ToString() },
                        { "InputLength", originalInput.Length.ToString() }
                    },
                    null,
                    false,
                    string.Join(", ", threats.Select(t => t.Type.ToString()))
                ));
            }

            return new SanitizationResult
            {
                OriginalValue = originalInput,
                SanitizedValue = sanitizedInput,
                IsClean = isClean,
                DetectedThreats = threats
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sanitizing input string");
            
            return new SanitizationResult
            {
                OriginalValue = input,
                SanitizedValue = input,
                IsClean = false,
                DetectedThreats = new List<SecurityThreat>
                {
                    new SecurityThreat
                    {
                        Type = ThreatType.SystemError,
                        Severity = ThreatSeverity.High,
                        Description = "Sanitization system error"
                    }
                }
            };
        }
    }

    public async Task<InputValidationResult> ValidateEmailAsync(string email, CancellationToken cancellationToken = default)
    {
        var issues = new List<ValidationIssue>();
        var sanitizedEmail = email?.Trim().ToLowerInvariant() ?? "";

        if (string.IsNullOrWhiteSpace(sanitizedEmail))
        {
            issues.Add(new ValidationIssue
            {
                PropertyName = "Email",
                IssueType = ValidationIssueType.Required,
                Severity = ValidationSeverity.High,
                Message = "Email address is required"
            });
        }
        else if (!EmailRegex.IsMatch(sanitizedEmail))
        {
            issues.Add(new ValidationIssue
            {
                PropertyName = "Email",
                IssueType = ValidationIssueType.InvalidFormat,
                Severity = ValidationSeverity.High,
                Message = "Email address format is invalid"
            });
        }
        else if (sanitizedEmail.Length > 254)
        {
            issues.Add(new ValidationIssue
            {
                PropertyName = "Email",
                IssueType = ValidationIssueType.InvalidLength,
                Severity = ValidationSeverity.Medium,
                Message = "Email address is too long"
            });
        }

        await LogValidationEventAsync("Email", issues.Count == 0);

        return new InputValidationResult
        {
            IsValid = issues.Count == 0,
            Issues = issues,
            SanitizedValue = sanitizedEmail
        };
    }

    public async Task<InputValidationResult> ValidatePhoneNumberAsync(string phoneNumber, string? countryCode = null, CancellationToken cancellationToken = default)
    {
        var issues = new List<ValidationIssue>();
        var sanitizedPhone = SanitizePhoneNumber(phoneNumber);

        if (string.IsNullOrWhiteSpace(sanitizedPhone))
        {
            issues.Add(new ValidationIssue
            {
                PropertyName = "PhoneNumber",
                IssueType = ValidationIssueType.Required,
                Severity = ValidationSeverity.Medium,
                Message = "Phone number is required"
            });
        }
        else if (!PhoneRegex.IsMatch(sanitizedPhone))
        {
            issues.Add(new ValidationIssue
            {
                PropertyName = "PhoneNumber",
                IssueType = ValidationIssueType.InvalidFormat,
                Severity = ValidationSeverity.Medium,
                Message = "Phone number format is invalid"
            });
        }

        await LogValidationEventAsync("PhoneNumber", issues.Count == 0);

        return new InputValidationResult
        {
            IsValid = issues.Count == 0,
            Issues = issues,
            SanitizedValue = sanitizedPhone
        };
    }

    public async Task<InputValidationResult> ValidateAmountAsync(decimal amount, string currency, CancellationToken cancellationToken = default)
    {
        var issues = new List<ValidationIssue>();

        if (amount <= 0)
        {
            issues.Add(new ValidationIssue
            {
                PropertyName = "Amount",
                IssueType = ValidationIssueType.InvalidRange,
                Severity = ValidationSeverity.High,
                Message = "Amount must be greater than zero"
            });
        }
        else if (amount > _options.MaxTransactionAmount)
        {
            issues.Add(new ValidationIssue
            {
                PropertyName = "Amount",
                IssueType = ValidationIssueType.InvalidRange,
                Severity = ValidationSeverity.High,
                Message = $"Amount exceeds maximum allowed value of {_options.MaxTransactionAmount}"
            });
        }

        if (string.IsNullOrWhiteSpace(currency))
        {
            issues.Add(new ValidationIssue
            {
                PropertyName = "Currency",
                IssueType = ValidationIssueType.Required,
                Severity = ValidationSeverity.High,
                Message = "Currency is required"
            });
        }
        else if (!_options.SupportedCurrencies.Contains(currency.ToUpperInvariant()))
        {
            issues.Add(new ValidationIssue
            {
                PropertyName = "Currency",
                IssueType = ValidationIssueType.UnsupportedValue,
                Severity = ValidationSeverity.High,
                Message = $"Currency {currency} is not supported"
            });
        }

        await LogValidationEventAsync("Amount", issues.Count == 0, $"Amount: {amount} {currency}");

        return new InputValidationResult
        {
            IsValid = issues.Count == 0,
            Issues = issues
        };
    }

    public async Task<AntiInjectionResult> DetectAndPreventInjectionAttacksAsync(string input, CancellationToken cancellationToken = default)
    {
        var threats = new List<SecurityThreat>();
        var sanitizedInput = input;

        if (string.IsNullOrEmpty(input))
        {
            return new AntiInjectionResult
            {
                OriginalValue = input,
                SanitizedValue = "",
                IsSafe = true,
                DetectedThreats = threats
            };
        }

        // SQL Injection detection
        if (SqlInjectionRegex.IsMatch(input))
        {
            threats.Add(new SecurityThreat
            {
                Type = ThreatType.SqlInjection,
                Severity = ThreatSeverity.Critical,
                Description = "Potential SQL injection attempt detected"
            });
            
            // Remove SQL keywords
            sanitizedInput = SqlInjectionRegex.Replace(sanitizedInput, "");
        }

        // Command injection detection
        if (CommandInjectionRegex.IsMatch(input))
        {
            threats.Add(new SecurityThreat
            {
                Type = ThreatType.CommandInjection,
                Severity = ThreatSeverity.High,
                Description = "Potential command injection attempt detected"
            });
            
            // Remove dangerous characters
            sanitizedInput = CommandInjectionRegex.Replace(sanitizedInput, "");
        }

        var isSafe = threats.Count == 0;

        if (!isSafe)
        {
            await _securityAuditService.LogSecurityEventAsync(new SecurityAuditEvent(
                Guid.NewGuid().ToString(),
                SecurityEventType.SuspiciousActivity,
                SecurityEventSeverity.Critical,
                DateTime.UtcNow,
                null,
                null,
                null,
                null,
                $"Injection attack detected: {string.Join(", ", threats.Select(t => t.Type))}",
                new Dictionary<string, string>
                {
                    { "ThreatCount", threats.Count.ToString() },
                    { "ThreatTypes", string.Join(", ", threats.Select(t => t.Type.ToString())) }
                },
                null,
                false,
                "Injection attack attempt"
            ));
        }

        return new AntiInjectionResult
        {
            OriginalValue = input,
            SanitizedValue = sanitizedInput,
            IsSafe = isSafe,
            DetectedThreats = threats
        };
    }

    public async Task<XssProtectionResult> ProtectAgainstXssAsync(string input, CancellationToken cancellationToken = default)
    {
        var threats = new List<SecurityThreat>();
        var sanitizedInput = input;

        if (string.IsNullOrEmpty(input))
        {
            return new XssProtectionResult
            {
                OriginalValue = input,
                SanitizedValue = "",
                IsSafe = true,
                DetectedThreats = threats
            };
        }

        // XSS detection
        if (XssRegex.IsMatch(input))
        {
            threats.Add(new SecurityThreat
            {
                Type = ThreatType.CrossSiteScripting,
                Severity = ThreatSeverity.High,
                Description = "Potential XSS attempt detected"
            });
            
            // HTML encode the input
            sanitizedInput = HttpUtility.HtmlEncode(input);
        }

        var isSafe = threats.Count == 0;

        if (!isSafe)
        {
            await _securityAuditService.LogSecurityEventAsync(new SecurityAuditEvent(
                Guid.NewGuid().ToString(),
                SecurityEventType.SuspiciousActivity,
                SecurityEventSeverity.High,
                DateTime.UtcNow,
                null,
                null,
                null,
                null,
                "XSS attack detected",
                new Dictionary<string, string>(),
                null,
                false,
                "XSS attack attempt"
            ));
        }

        return new XssProtectionResult
        {
            OriginalValue = input,
            SanitizedValue = sanitizedInput,
            IsSafe = isSafe,
            DetectedThreats = threats
        };
    }

    public async Task<List<ValidationIssue>> PerformComprehensiveSecurityValidationAsync(object data, CancellationToken cancellationToken = default)
    {
        var issues = new List<ValidationIssue>();

        try
        {
            var properties = data.GetType().GetProperties();
            
            foreach (var property in properties)
            {
                var value = property.GetValue(data);
                if (value == null) continue;

                var stringValue = value.ToString() ?? "";
                
                // Security validation checks
                var injectionResult = await DetectAndPreventInjectionAttacksAsync(stringValue, cancellationToken);
                if (!injectionResult.IsSafe)
                {
                    issues.Add(new ValidationIssue
                    {
                        PropertyName = property.Name,
                        IssueType = ValidationIssueType.SecurityThreat,
                        Severity = ValidationSeverity.Critical,
                        Message = "Security threat detected",
                        Details = string.Join(", ", injectionResult.DetectedThreats.Select(t => t.Description))
                    });
                }

                var xssResult = await ProtectAgainstXssAsync(stringValue, cancellationToken);
                if (!xssResult.IsSafe)
                {
                    issues.Add(new ValidationIssue
                    {
                        PropertyName = property.Name,
                        IssueType = ValidationIssueType.SecurityThreat,
                        Severity = ValidationSeverity.High,
                        Message = "XSS threat detected",
                        Details = string.Join(", ", xssResult.DetectedThreats.Select(t => t.Description))
                    });
                }
            }

            await _securityAuditService.LogSecurityEventAsync(new SecurityAuditEvent(
                Guid.NewGuid().ToString(),
                SecurityEventType.DataAccess,
                issues.Count > 0 ? SecurityEventSeverity.High : SecurityEventSeverity.Low,
                DateTime.UtcNow,
                null,
                null,
                null,
                null,
                $"Comprehensive security validation completed: {issues.Count} issues found",
                new Dictionary<string, string>
                {
                    { "DataType", data.GetType().Name },
                    { "IssueCount", issues.Count.ToString() }
                },
                null,
                issues.Count == 0,
                issues.Count > 0 ? $"{issues.Count} security issues detected" : null
            ));

            return issues;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error performing comprehensive security validation");
            
            issues.Add(new ValidationIssue
            {
                IssueType = ValidationIssueType.SystemError,
                Severity = ValidationSeverity.Critical,
                Message = "Security validation system error",
                Details = ex.Message
            });

            return issues;
        }
    }

    #region Private Helper Methods

    private string SanitizeCardNumber(string cardNumber)
    {
        if (string.IsNullOrEmpty(cardNumber)) return "";
        return new string(cardNumber.Where(char.IsDigit).ToArray());
    }

    private string SanitizeCvv(string cvv)
    {
        if (string.IsNullOrEmpty(cvv)) return "";
        return new string(cvv.Where(char.IsDigit).ToArray());
    }

    private string SanitizeExpiryDate(string expiryDate)
    {
        if (string.IsNullOrEmpty(expiryDate)) return "";
        return new string(expiryDate.Where(c => char.IsDigit(c) || c == '/').ToArray());
    }

    private string SanitizePhoneNumber(string phoneNumber)
    {
        if (string.IsNullOrEmpty(phoneNumber)) return "";
        return new string(phoneNumber.Where(c => char.IsDigit(c) || c == '+').ToArray());
    }

    private string RemoveSpecialCharacters(string input)
    {
        if (string.IsNullOrEmpty(input)) return "";
        return new string(input.Where(c => char.IsLetterOrDigit(c) || char.IsWhiteSpace(c) || c == '.' || c == '-' || c == '_').ToArray());
    }

    private bool IsValidLuhnChecksum(string cardNumber)
    {
        if (string.IsNullOrEmpty(cardNumber)) return false;

        var sum = 0;
        var alternate = false;

        for (int i = cardNumber.Length - 1; i >= 0; i--)
        {
            if (!char.IsDigit(cardNumber[i])) return false;
            
            var digit = cardNumber[i] - '0';
            
            if (alternate)
            {
                digit *= 2;
                if (digit > 9)
                    digit = (digit % 10) + 1;
            }
            
            sum += digit;
            alternate = !alternate;
        }

        return sum % 10 == 0;
    }

    private PaymentCardType DetermineCardType(string cardNumber)
    {
        if (string.IsNullOrEmpty(cardNumber)) return PaymentCardType.Unknown;

        return cardNumber[0] switch
        {
            '4' => PaymentCardType.Visa,
            '5' => PaymentCardType.MasterCard,
            '3' when cardNumber.Length >= 2 && (cardNumber[1] == '4' || cardNumber[1] == '7') => PaymentCardType.AmericanExpress,
            '6' => PaymentCardType.Discover,
            _ => PaymentCardType.Unknown
        };
    }

    private bool IsExpiryDateInPast(string expiryDate)
    {
        if (!ExpiryDateRegex.IsMatch(expiryDate)) return false;

        var parts = expiryDate.Split('/');
        if (!int.TryParse(parts[0], out var month) || !int.TryParse(parts[1], out var year))
            return false;

        // Handle 2-digit years
        if (year < 100)
            year += 2000;

        var expiry = new DateTime(year, month, DateTime.DaysInMonth(year, month));
        return expiry < DateTime.UtcNow;
    }

    private async Task LogValidationEventAsync(string fieldName, bool isValid, string? additionalInfo = null)
    {
        await _securityAuditService.LogSecurityEventAsync(new SecurityAuditEvent(
            Guid.NewGuid().ToString(),
            isValid ? SecurityEventType.AuthenticationSuccess : SecurityEventType.AuthenticationFailure,
            isValid ? SecurityEventSeverity.Low : SecurityEventSeverity.Medium,
            DateTime.UtcNow,
            null,
            null,
            null,
            null,
            $"Field validation for {fieldName}: {(isValid ? "Valid" : "Invalid")}",
            new Dictionary<string, string>
            {
                { "FieldName", fieldName },
                { "AdditionalInfo", additionalInfo ?? "" }
            },
            null,
            isValid,
            isValid ? null : $"{fieldName} validation failed"
        ));
    }

    #endregion
}

// Supporting classes and enums
public class InputValidationOptions
{
    public decimal MaxTransactionAmount { get; set; } = 1000000m;
    public List<string> SupportedCurrencies { get; set; } = new() { "USD", "EUR", "GBP", "RUB" };
    public bool EnableStrictValidation { get; set; } = true;
    public bool LogValidationEvents { get; set; } = true;
}

public class ValidationOptions
{
    public bool StrictMode { get; set; } = true;
    public bool SanitizeInput { get; set; } = true;
    public List<string> RequiredFields { get; set; } = new();
}

public class SanitizationOptions
{
    public bool HtmlEncode { get; set; } = true;
    public bool RemoveSpecialCharacters { get; set; } = false;
    public int MaxLength { get; set; } = 1000;
    public bool PreserveSpaces { get; set; } = true;
}

public class InputValidationResult
{
    public bool IsValid { get; set; }
    public List<ValidationIssue> Issues { get; set; } = new();
    public Dictionary<string, object> SanitizedData { get; set; } = new();
    public string? SanitizedValue { get; set; }
    public TimeSpan ValidationTime { get; set; }
}

public class SanitizationResult
{
    public string OriginalValue { get; set; } = string.Empty;
    public string SanitizedValue { get; set; } = string.Empty;
    public bool IsClean { get; set; }
    public List<SecurityThreat> DetectedThreats { get; set; } = new();
}

public class AntiInjectionResult
{
    public string OriginalValue { get; set; } = string.Empty;
    public string SanitizedValue { get; set; } = string.Empty;
    public bool IsSafe { get; set; }
    public List<SecurityThreat> DetectedThreats { get; set; } = new();
}

public class XssProtectionResult
{
    public string OriginalValue { get; set; } = string.Empty;
    public string SanitizedValue { get; set; } = string.Empty;
    public bool IsSafe { get; set; }
    public List<SecurityThreat> DetectedThreats { get; set; } = new();
}

public class ValidationIssue
{
    public string PropertyName { get; set; } = string.Empty;
    public ValidationIssueType IssueType { get; set; }
    public ValidationSeverity Severity { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? Details { get; set; }
    public string? OriginalValue { get; set; }
    public string? SuggestedValue { get; set; }
}

public class SecurityThreat
{
    public ThreatType Type { get; set; }
    public ThreatSeverity Severity { get; set; }
    public string Description { get; set; } = string.Empty;
    public string? Details { get; set; }
}

public enum ValidationIssueType
{
    Required,
    InvalidFormat,
    InvalidLength,
    InvalidRange,
    InvalidChecksum,
    UnsupportedPaymentCardType,
    UnsupportedValue,
    Expired,
    SecurityThreat,
    SystemError
}

public enum ValidationSeverity
{
    Low = 1,
    Medium = 2,
    High = 3,
    Critical = 4
}

public enum ThreatType
{
    SqlInjection,
    CrossSiteScripting,
    CommandInjection,
    ExcessiveLength,
    SystemError
}

public enum ThreatSeverity
{
    Low = 1,
    Medium = 2,
    High = 3,
    Critical = 4
}

public enum PaymentCardType
{
    Unknown,
    Visa,
    MasterCard,
    AmericanExpress,
    Discover
}