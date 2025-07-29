using PaymentGateway.Core.Enums;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace PaymentGateway.Core.Services;

public interface IErrorCategorizationService
{
    ErrorCategoryInfo GetErrorCategoryInfo(PaymentErrorCode errorCode);
    List<PaymentErrorCode> GetErrorsByCategory(ErrorCategory category);
    List<PaymentErrorCode> GetErrorsBySeverity(ErrorSeverity severity);
    bool ShouldAlertOnError(PaymentErrorCode errorCode);
    TimeSpan GetRetryDelay(PaymentErrorCode errorCode, int attemptNumber);
    string GetUserFriendlyMessage(PaymentErrorCode errorCode, string language = "ru");
}

public record ErrorCategoryInfo(
    ErrorCategory Category,
    ErrorSeverity Severity,
    bool IsRetryable,
    bool RequiresUserAction,
    bool RequiresSupportContact,
    bool ShouldAlert,
    TimeSpan BaseRetryDelay,
    string Description);

public class ErrorCategorizationOptions
{
    public Dictionary<string, int> AlertThresholds { get; set; } = new()
    {
        { "Critical", 1 },      // Alert on any critical error
        { "High", 5 },          // Alert if >5% high severity errors
        { "Medium", 10 },       // Alert if >10% medium severity errors
        { "Authentication", 10 } // Alert if >10% auth failures
    };

    public Dictionary<string, TimeSpan> RetryDelays { get; set; } = new()
    {
        { "TemporaryIssues", TimeSpan.FromSeconds(30) },
        { "ServiceUnavailable", TimeSpan.FromMinutes(1) },
        { "SystemError", TimeSpan.FromMinutes(5) }
    };

    public bool EnableDetailedCategorization { get; set; } = true;
}

public class ErrorCategorizationService : IErrorCategorizationService
{
    private readonly ILogger<ErrorCategorizationService> _logger;
    private readonly ErrorCategorizationOptions _options;

    private static readonly Dictionary<PaymentErrorCode, ErrorCategoryInfo> ErrorCategoryInfoMap = new()
    {
        // Critical System Errors
        {
            PaymentErrorCode.CriticalSystemError,
            new ErrorCategoryInfo(ErrorCategory.Critical, ErrorSeverity.Critical, false, false, true, true,
                TimeSpan.FromMinutes(5), "Critical payment system failure requiring immediate attention")
        },
        {
            PaymentErrorCode.InternalSystemError,
            new ErrorCategoryInfo(ErrorCategory.Critical, ErrorSeverity.Critical, false, false, true, true,
                TimeSpan.FromMinutes(5), "Critical internal system error")
        },
        {
            PaymentErrorCode.CriticalInternalSystemError,
            new ErrorCategoryInfo(ErrorCategory.Critical, ErrorSeverity.Critical, false, false, true, true,
                TimeSpan.FromMinutes(5), "Critical internal system processing error")
        },

        // Authentication Errors
        {
            PaymentErrorCode.InvalidToken,
            new ErrorCategoryInfo(ErrorCategory.Authentication, ErrorSeverity.High, false, true, false, true,
                TimeSpan.Zero, "Invalid authentication token - requires credential verification")
        },
        {
            PaymentErrorCode.TokenAuthenticationFailed,
            new ErrorCategoryInfo(ErrorCategory.Authentication, ErrorSeverity.High, false, true, false, true,
                TimeSpan.Zero, "Token authentication failed - verify signature generation")
        },
        {
            PaymentErrorCode.TerminalNotFound,
            new ErrorCategoryInfo(ErrorCategory.Authentication, ErrorSeverity.High, false, true, false, true,
                TimeSpan.Zero, "Terminal not found - verify TerminalKey")
        },
        {
            PaymentErrorCode.TerminalAccessDenied,
            new ErrorCategoryInfo(ErrorCategory.Authentication, ErrorSeverity.High, false, true, true, true,
                TimeSpan.Zero, "Terminal access denied - merchant inactive or blocked")
        },

        // Validation Errors
        {
            PaymentErrorCode.InvalidParameterFormat,
            new ErrorCategoryInfo(ErrorCategory.Validation, ErrorSeverity.Medium, false, true, false, false,
                TimeSpan.Zero, "Request parameters format validation failed")
        },
        {
            PaymentErrorCode.MissingRequiredParameters,
            new ErrorCategoryInfo(ErrorCategory.Validation, ErrorSeverity.Medium, false, true, false, false,
                TimeSpan.Zero, "Required request parameters missing")
        },
        {
            PaymentErrorCode.RequestValidationFailed,
            new ErrorCategoryInfo(ErrorCategory.Validation, ErrorSeverity.Medium, false, true, false, false,
                TimeSpan.Zero, "FluentValidation request validation failed")
        },

        // Bank Rejections
        {
            PaymentErrorCode.BankRejectedPayment,
            new ErrorCategoryInfo(ErrorCategory.BankRejection, ErrorSeverity.Medium, false, true, false, false,
                TimeSpan.Zero, "Bank declined transaction processing")
        },
        {
            PaymentErrorCode.ContactIssuingBank,
            new ErrorCategoryInfo(ErrorCategory.BankRejection, ErrorSeverity.Medium, false, true, false, false,
                TimeSpan.Zero, "Customer must contact issuing bank")
        },
        {
            PaymentErrorCode.MerchantRejectedCard,
            new ErrorCategoryInfo(ErrorCategory.BankRejection, ErrorSeverity.Medium, false, true, false, false,
                TimeSpan.Zero, "Merchant rejected card processing")
        },

        // Insufficient Funds
        {
            PaymentErrorCode.InsufficientCardFunds,
            new ErrorCategoryInfo(ErrorCategory.InsufficientFunds, ErrorSeverity.Medium, false, true, false, false,
                TimeSpan.Zero, "Insufficient funds on customer card")
        },
        {
            PaymentErrorCode.InsufficientCardFunds2,
            new ErrorCategoryInfo(ErrorCategory.InsufficientFunds, ErrorSeverity.Medium, false, true, false, false,
                TimeSpan.Zero, "Insufficient funds on bank account")
        },
        {
            PaymentErrorCode.InsufficientAccountFunds,
            new ErrorCategoryInfo(ErrorCategory.InsufficientFunds, ErrorSeverity.Medium, false, true, false, false,
                TimeSpan.Zero, "Account balance insufficient for transaction")
        },

        // Card Issues
        {
            PaymentErrorCode.CardExpired,
            new ErrorCategoryInfo(ErrorCategory.CardIssues, ErrorSeverity.Medium, false, true, false, false,
                TimeSpan.Zero, "Payment card has expired")
        },
        {
            PaymentErrorCode.InvalidCardNumber,
            new ErrorCategoryInfo(ErrorCategory.CardIssues, ErrorSeverity.Medium, false, true, false, false,
                TimeSpan.Zero, "Bank card number validation failed")
        },
        {
            PaymentErrorCode.InvalidCvv,
            new ErrorCategoryInfo(ErrorCategory.CardIssues, ErrorSeverity.Medium, false, true, false, false,
                TimeSpan.Zero, "CVV/CVC security code validation failed")
        },
        {
            PaymentErrorCode.CardFailedLuhnCheck,
            new ErrorCategoryInfo(ErrorCategory.CardIssues, ErrorSeverity.Medium, false, true, false, false,
                TimeSpan.Zero, "Card number failed Luhn algorithm validation")
        },

        // Temporary Issues (Retryable)
        {
            PaymentErrorCode.ServiceTemporarilyUnavailable,
            new ErrorCategoryInfo(ErrorCategory.TemporaryIssues, ErrorSeverity.Low, true, false, false, false,
                TimeSpan.FromSeconds(30), "Service temporarily unavailable - retry recommended")
        },
        {
            PaymentErrorCode.TemporaryProcessingIssue,
            new ErrorCategoryInfo(ErrorCategory.TemporaryIssues, ErrorSeverity.Low, true, false, false, false,
                TimeSpan.FromSeconds(30), "Temporary payment processing issue")
        },
        {
            PaymentErrorCode.ExternalServiceUnavailable,
            new ErrorCategoryInfo(ErrorCategory.TemporaryIssues, ErrorSeverity.Medium, true, false, false, true,
                TimeSpan.FromMinutes(1), "External service unavailable")
        },
        {
            PaymentErrorCode.RepeatOperationLater,
            new ErrorCategoryInfo(ErrorCategory.TemporaryIssues, ErrorSeverity.Low, true, false, false, false,
                TimeSpan.FromSeconds(30), "Operation should be retried later")
        },

        // Business Logic Errors
        {
            PaymentErrorCode.InvalidStateTransition,
            new ErrorCategoryInfo(ErrorCategory.BusinessLogic, ErrorSeverity.Medium, false, false, false, false,
                TimeSpan.Zero, "Payment state transition not allowed")
        },
        {
            PaymentErrorCode.DuplicateOrderId,
            new ErrorCategoryInfo(ErrorCategory.BusinessLogic, ErrorSeverity.Medium, false, true, false, false,
                TimeSpan.Zero, "Order ID already exists for terminal")
        },
        {
            PaymentErrorCode.InvalidPaymentStatusForOperation,
            new ErrorCategoryInfo(ErrorCategory.BusinessLogic, ErrorSeverity.Medium, false, false, false, false,
                TimeSpan.Zero, "Payment status invalid for requested operation")
        },

        // Configuration Issues
        {
            PaymentErrorCode.TerminalBlocked,
            new ErrorCategoryInfo(ErrorCategory.Configuration, ErrorSeverity.High, false, false, true, true,
                TimeSpan.Zero, "Terminal is blocked or inactive")
        },
        {
            PaymentErrorCode.PaymentMethodDisabled,
            new ErrorCategoryInfo(ErrorCategory.Configuration, ErrorSeverity.Medium, false, false, true, true,
                TimeSpan.Zero, "Payment method disabled for terminal")
        },
        {
            PaymentErrorCode.ChargeMethodBlocked,
            new ErrorCategoryInfo(ErrorCategory.Configuration, ErrorSeverity.Medium, false, false, true, true,
                TimeSpan.Zero, "Charge method blocked for terminal")
        }
    };

    public ErrorCategorizationService(
        ILogger<ErrorCategorizationService> logger,
        IOptions<ErrorCategorizationOptions> options)
    {
        _logger = logger;
        _options = options.Value;
    }

    public ErrorCategoryInfo GetErrorCategoryInfo(PaymentErrorCode errorCode)
    {
        if (ErrorCategoryInfoMap.TryGetValue(errorCode, out var categoryInfo))
        {
            return categoryInfo;
        }

        // Default categorization for unmapped errors
        var category = errorCode.GetCategory();
        var severity = errorCode.GetSeverity();
        var isRetryable = errorCode.IsRetryable();

        return new ErrorCategoryInfo(
            category,
            severity,
            isRetryable,
            errorCode.RequiresUserAction(),
            errorCode.RequiresSupportContact(),
            severity == ErrorSeverity.Critical,
            GetDefaultRetryDelay(category),
            $"Error code {(int)errorCode} - {category} category");
    }

    public List<PaymentErrorCode> GetErrorsByCategory(ErrorCategory category)
    {
        return ErrorCategoryInfoMap
            .Where(kvp => kvp.Value.Category == category)
            .Select(kvp => kvp.Key)
            .ToList();
    }

    public List<PaymentErrorCode> GetErrorsBySeverity(ErrorSeverity severity)
    {
        return ErrorCategoryInfoMap
            .Where(kvp => kvp.Value.Severity == severity)
            .Select(kvp => kvp.Key)
            .ToList();
    }

    public bool ShouldAlertOnError(PaymentErrorCode errorCode)
    {
        var categoryInfo = GetErrorCategoryInfo(errorCode);
        return categoryInfo.ShouldAlert;
    }

    public TimeSpan GetRetryDelay(PaymentErrorCode errorCode, int attemptNumber)
    {
        var categoryInfo = GetErrorCategoryInfo(errorCode);
        
        if (!categoryInfo.IsRetryable)
        {
            return TimeSpan.Zero;
        }

        // Exponential backoff with jitter
        var baseDelay = categoryInfo.BaseRetryDelay;
        var exponentialDelay = TimeSpan.FromMilliseconds(baseDelay.TotalMilliseconds * Math.Pow(2, attemptNumber - 1));
        
        // Add jitter (±25%)
        var jitterMs = Random.Shared.Next(
            (int)(exponentialDelay.TotalMilliseconds * 0.75),
            (int)(exponentialDelay.TotalMilliseconds * 1.25));

        var finalDelay = TimeSpan.FromMilliseconds(Math.Min(jitterMs, TimeSpan.FromMinutes(5).TotalMilliseconds));

        _logger.LogDebug("Calculated retry delay for error {ErrorCode}, attempt {Attempt}: {Delay}ms",
            errorCode, attemptNumber, finalDelay.TotalMilliseconds);

        return finalDelay;
    }

    public string GetUserFriendlyMessage(PaymentErrorCode errorCode, string language = "ru")
    {
        var userFriendlyMessages = new Dictionary<PaymentErrorCode, Dictionary<string, UserFriendlyMessage>>
        {
            {
                PaymentErrorCode.InsufficientCardFunds,
                new Dictionary<string, UserFriendlyMessage>
                {
                    ["ru"] = new("Недостаточно средств", "На вашей карте недостаточно средств для совершения покупки", "Попробуйте другую карту или пополните счет"),
                    ["en"] = new("Insufficient Funds", "Your card has insufficient funds for this purchase", "Try another card or add funds to your account")
                }
            },
            {
                PaymentErrorCode.CardExpired,
                new Dictionary<string, UserFriendlyMessage>
                {
                    ["ru"] = new("Карта просрочена", "Срок действия вашей карты истек", "Используйте актуальную карту"),
                    ["en"] = new("Card Expired", "Your card has expired", "Please use a valid card")
                }
            },
            {
                PaymentErrorCode.InvalidCvv,
                new Dictionary<string, UserFriendlyMessage>
                {
                    ["ru"] = new("Неверный код безопасности", "Проверьте правильность ввода CVV/CVC кода", "Введите трехзначный код с обратной стороны карты"),
                    ["en"] = new("Invalid Security Code", "Please check your CVV/CVC code", "Enter the 3-digit code from the back of your card")
                }
            },
            {
                PaymentErrorCode.ServiceTemporarilyUnavailable,
                new Dictionary<string, UserFriendlyMessage>
                {
                    ["ru"] = new("Сервис временно недоступен", "Платежная система временно недоступна", "Попробуйте повторить операцию через несколько минут"),
                    ["en"] = new("Service Temporarily Unavailable", "Payment service is temporarily unavailable", "Please try again in a few minutes")
                }
            }
        };

        if (userFriendlyMessages.TryGetValue(errorCode, out var languageMessages) &&
            languageMessages.TryGetValue(language.ToLowerInvariant(), out var message))
        {
            return $"{message.Title}: {message.Description}. {message.Action}";
        }

        // Fallback to basic error message
        var categoryInfo = GetErrorCategoryInfo(errorCode);
        return language.ToLowerInvariant() == "ru" 
            ? "Произошла ошибка при обработке платежа" 
            : "An error occurred while processing payment";
    }

    private TimeSpan GetDefaultRetryDelay(ErrorCategory category)
    {
        return category switch
        {
            ErrorCategory.TemporaryIssues => TimeSpan.FromSeconds(30),
            ErrorCategory.System => TimeSpan.FromMinutes(1),
            ErrorCategory.Configuration => TimeSpan.FromMinutes(5),
            _ => TimeSpan.Zero
        };
    }

    private record UserFriendlyMessage(string Title, string Description, string Action);
}