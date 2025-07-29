using PaymentGateway.Core.Enums;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Globalization;
using System.Text.Json;

namespace PaymentGateway.Core.Services;

public interface IErrorLocalizationService
{
    string GetLocalizedMessage(PaymentErrorCode errorCode, string language = "ru");
    string GetLocalizedDetails(PaymentErrorCode errorCode, string language = "ru");
    LocalizedErrorInfo GetLocalizedErrorInfo(PaymentErrorCode errorCode, string language = "ru");
    List<string> GetSupportedLanguages();
    Task LoadLocalizationAsync(string language);
}

public record LocalizedErrorInfo(
    string Message,
    string Details,
    string UserFriendlyTitle,
    string UserFriendlyMessage,
    string RecommendedAction,
    string SupportContact);

public class ErrorLocalizationOptions
{
    public string DefaultLanguage { get; set; } = "ru";
    public List<string> SupportedLanguages { get; set; } = new() { "ru", "en" };
    public bool EnableDynamicLoading { get; set; } = false;
    public string LocalizationFilePath { get; set; } = "Resources/ErrorLocalizations";
}

public class ErrorLocalizationService : IErrorLocalizationService
{
    private readonly ILogger<ErrorLocalizationService> _logger;
    private readonly ErrorLocalizationOptions _options;
    private readonly Dictionary<string, Dictionary<PaymentErrorCode, LocalizedErrorInfo>> _localizations;

    public ErrorLocalizationService(
        ILogger<ErrorLocalizationService> logger,
        IOptions<ErrorLocalizationOptions> options)
    {
        _logger = logger;
        _options = options.Value;
        _localizations = new Dictionary<string, Dictionary<PaymentErrorCode, LocalizedErrorInfo>>();
        
        InitializeLocalizations();
    }

    public string GetLocalizedMessage(PaymentErrorCode errorCode, string language = "ru")
    {
        var errorInfo = GetLocalizedErrorInfo(errorCode, language);
        return errorInfo.Message;
    }

    public string GetLocalizedDetails(PaymentErrorCode errorCode, string language = "ru")
    {
        var errorInfo = GetLocalizedErrorInfo(errorCode, language);
        return errorInfo.Details;
    }

    public LocalizedErrorInfo GetLocalizedErrorInfo(PaymentErrorCode errorCode, string language = "ru")
    {
        var normalizedLanguage = NormalizeLanguage(language);
        
        if (_localizations.TryGetValue(normalizedLanguage, out var languageLocalizations) &&
            languageLocalizations.TryGetValue(errorCode, out var localizedInfo))
        {
            return localizedInfo;
        }

        // Fallback to default language
        if (normalizedLanguage != _options.DefaultLanguage &&
            _localizations.TryGetValue(_options.DefaultLanguage, out var defaultLocalizations) &&
            defaultLocalizations.TryGetValue(errorCode, out var defaultInfo))
        {
            _logger.LogDebug("Using default language fallback for error {ErrorCode} in language {Language}", 
                errorCode, language);
            return defaultInfo;
        }

        // Final fallback
        return CreateFallbackErrorInfo(errorCode, normalizedLanguage);
    }

    public List<string> GetSupportedLanguages()
    {
        return _options.SupportedLanguages.ToList();
    }

    public async Task LoadLocalizationAsync(string language)
    {
        if (!_options.EnableDynamicLoading)
        {
            _logger.LogWarning("Dynamic loading is disabled");
            return;
        }

        try
        {
            var filePath = Path.Combine(_options.LocalizationFilePath, $"{language}.json");
            if (!File.Exists(filePath))
            {
                _logger.LogWarning("Localization file not found: {FilePath}", filePath);
                return;
            }

            var jsonContent = await File.ReadAllTextAsync(filePath);
            var localizationData = JsonSerializer.Deserialize<Dictionary<string, LocalizedErrorInfo>>(jsonContent);

            if (localizationData == null)
            {
                _logger.LogError("Failed to deserialize localization data for language {Language}", language);
                return;
            }

            var errorLocalizations = new Dictionary<PaymentErrorCode, LocalizedErrorInfo>();
            foreach (var kvp in localizationData)
            {
                if (Enum.TryParse<PaymentErrorCode>(kvp.Key, out var errorCode))
                {
                    errorLocalizations[errorCode] = kvp.Value;
                }
            }

            _localizations[NormalizeLanguage(language)] = errorLocalizations;
            _logger.LogInformation("Loaded {Count} localizations for language {Language}", 
                errorLocalizations.Count, language);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load localization for language {Language}", language);
        }
    }

    private void InitializeLocalizations()
    {
        InitializeRussianLocalizations();
        InitializeEnglishLocalizations();
    }

    private void InitializeRussianLocalizations()
    {
        var russianLocalizations = new Dictionary<PaymentErrorCode, LocalizedErrorInfo>
        {
            [PaymentErrorCode.Success] = new(
                "Операция выполнена успешно",
                "Платеж обработан без ошибок",
                "Успешно",
                "Ваш платеж выполнен успешно",
                "Операция завершена",
                ""
            ),
            [PaymentErrorCode.InvalidParameterFormat] = new(
                "Переданные параметры не соответствуют требуемому формату",
                "Проверьте формат переданных данных в запросе",
                "Ошибка в данных",
                "Некоторые данные указаны неверно",
                "Проверьте правильность заполнения полей и повторите попытку",
                "Обратитесь в техническую поддержку, если ошибка повторяется"
            ),
            [PaymentErrorCode.MissingRequiredParameters] = new(
                "Отсутствуют обязательные параметры в запросе",
                "Не переданы все необходимые для обработки данные",
                "Не хватает данных",
                "Не указаны обязательные поля",
                "Заполните все обязательные поля и повторите попытку",
                "Свяжитесь с поддержкой для получения помощи"
            ),
            [PaymentErrorCode.CriticalSystemError] = new(
                "Критическая ошибка платежной системы",
                "Произошел сбой в работе системы обработки платежей",
                "Системная ошибка",
                "Временные технические неполадки",
                "Попробуйте повторить операцию через несколько минут",
                "Обратитесь в службу поддержки"
            ),
            [PaymentErrorCode.InvalidStateTransition] = new(
                "Статус операции не может быть изменен",
                "Текущее состояние платежа не позволяет выполнить операцию",
                "Недопустимая операция",
                "Операция недоступна для данного платежа",
                "Проверьте статус платежа",
                "Обратитесь в поддержку для уточнения статуса"
            ),
            [PaymentErrorCode.InsufficientCardFunds] = new(
                "На карте недостаточно денежных средств",
                "Баланс карты меньше суммы операции",
                "Недостаточно средств",
                "На вашей карте недостаточно средств",
                "Пополните счет или используйте другую карту",
                "Обратитесь в банк для проверки баланса"
            ),
            [PaymentErrorCode.CardExpired] = new(
                "Карта просрочена",
                "Срок действия банковской карты истек",
                "Карта просрочена",
                "Срок действия вашей карты истек",
                "Используйте актуальную карту",
                "Обратитесь в банк для получения новой карты"
            ),
            [PaymentErrorCode.InvalidToken] = new(
                "Ошибка аутентификации. Проверьте TerminalKey и SecretKey",
                "Неверные данные для авторизации в системе",
                "Ошибка авторизации",
                "Проблема с авторизацией платежа",
                "Обратитесь к администратору системы",
                "Свяжитесь с технической поддержкой"
            ),
            [PaymentErrorCode.PaymentNotFound] = new(
                "Платежная операция не найдена",
                "Запрашиваемый платеж отсутствует в системе",
                "Платеж не найден",
                "Информация о платеже недоступна",
                "Проверьте правильность номера платежа",
                "Обратитесь в поддержку с номером операции"
            ),
            [PaymentErrorCode.ServiceTemporarilyUnavailable] = new(
                "Сервис временно недоступен, попробуйте позднее",
                "Платежная система находится на техническом обслуживании",
                "Временные неполадки",
                "Сервис временно недоступен",
                "Попробуйте повторить операцию через несколько минут",
                "Если проблема не решается, обратитесь в поддержку"
            ),
            [PaymentErrorCode.DuplicateOrderId] = new(
                "Операция с указанным идентификатором заказа уже выполняется",
                "Заказ с таким номером уже обрабатывается",
                "Дублирование заказа",
                "Этот заказ уже обрабатывается",
                "Дождитесь завершения текущей операции",
                "Обратитесь в поддержку при повторении ошибки"
            )
        };

        _localizations["ru"] = russianLocalizations;
    }

    private void InitializeEnglishLocalizations()
    {
        var englishLocalizations = new Dictionary<PaymentErrorCode, LocalizedErrorInfo>
        {
            [PaymentErrorCode.Success] = new(
                "Operation completed successfully",
                "Payment processed without errors",
                "Success",
                "Your payment was completed successfully",
                "Operation completed",
                ""
            ),
            [PaymentErrorCode.InvalidParameterFormat] = new(
                "Parameters do not match required format",
                "Check the format of data in the request",
                "Data Error",
                "Some data is incorrect",
                "Check field formatting and try again",
                "Contact technical support if error persists"
            ),
            [PaymentErrorCode.MissingRequiredParameters] = new(
                "Required parameters are missing in request",
                "Not all necessary data for processing was provided",
                "Missing Data",
                "Required fields are not specified",
                "Fill in all required fields and try again",
                "Contact support for assistance"
            ),
            [PaymentErrorCode.CriticalSystemError] = new(
                "Critical payment system error",
                "Payment processing system failure occurred",
                "System Error",
                "Temporary technical issues",
                "Try repeating the operation in a few minutes",
                "Contact support service"
            ),
            [PaymentErrorCode.InvalidStateTransition] = new(
                "Operation status cannot be changed",
                "Current payment state does not allow this operation",
                "Invalid Operation",
                "Operation not available for this payment",
                "Check payment status",
                "Contact support to clarify status"
            ),
            [PaymentErrorCode.InsufficientCardFunds] = new(
                "Insufficient funds on card",
                "Card balance is less than operation amount",
                "Insufficient Funds",
                "Your card has insufficient funds",
                "Add funds or use another card",
                "Contact your bank to check balance"
            ),
            [PaymentErrorCode.CardExpired] = new(
                "Card expired",
                "Bank card expiration date has passed",
                "Card Expired",
                "Your card has expired",
                "Use a valid card",
                "Contact your bank for a new card"
            ),
            [PaymentErrorCode.InvalidToken] = new(
                "Authentication error. Check TerminalKey and SecretKey",
                "Invalid system authorization data",
                "Authorization Error",
                "Payment authorization issue",
                "Contact system administrator",
                "Contact technical support"
            ),
            [PaymentErrorCode.PaymentNotFound] = new(
                "Payment operation not found",
                "Requested payment is not in the system",
                "Payment Not Found",
                "Payment information unavailable",
                "Check payment number correctness",
                "Contact support with operation number"
            ),
            [PaymentErrorCode.ServiceTemporarilyUnavailable] = new(
                "Service temporarily unavailable, try later",
                "Payment system is under maintenance",
                "Temporary Issues",
                "Service temporarily unavailable",
                "Try repeating the operation in a few minutes",
                "Contact support if problem persists"
            ),
            [PaymentErrorCode.DuplicateOrderId] = new(
                "Operation with this order ID already executing",
                "Order with this number is already being processed",
                "Duplicate Order",
                "This order is already being processed",
                "Wait for current operation to complete",
                "Contact support if error repeats"
            )
        };

        _localizations["en"] = englishLocalizations;
    }

    private string NormalizeLanguage(string language)
    {
        var normalized = language?.ToLowerInvariant() ?? _options.DefaultLanguage;
        
        // Handle culture codes like "ru-RU" -> "ru"
        if (normalized.Contains('-'))
        {
            normalized = normalized.Split('-')[0];
        }

        return _options.SupportedLanguages.Contains(normalized) ? normalized : _options.DefaultLanguage;
    }

    private LocalizedErrorInfo CreateFallbackErrorInfo(PaymentErrorCode errorCode, string language)
    {
        var isRussian = language == "ru";
        
        return new LocalizedErrorInfo(
            isRussian ? "Произошла ошибка при обработке запроса" : "An error occurred while processing request",
            isRussian ? $"Код ошибки: {(int)errorCode}" : $"Error code: {(int)errorCode}",
            isRussian ? "Ошибка" : "Error",
            isRussian ? "При обработке запроса произошла ошибка" : "An error occurred while processing the request",
            isRussian ? "Повторите попытку или обратитесь в поддержку" : "Try again or contact support",
            isRussian ? "Обратитесь в службу поддержки" : "Contact support service"
        );
    }
}

// Extension methods for easier localization usage
public static class ErrorLocalizationExtensions
{
    public static string ToLocalizedMessage(this PaymentErrorCode errorCode, IErrorLocalizationService localizationService, string language = "ru")
    {
        return localizationService.GetLocalizedMessage(errorCode, language);
    }

    public static LocalizedErrorInfo ToLocalizedErrorInfo(this PaymentErrorCode errorCode, IErrorLocalizationService localizationService, string language = "ru")
    {
        return localizationService.GetLocalizedErrorInfo(errorCode, language);
    }

    public static string DetectLanguageFromCulture()
    {
        var culture = CultureInfo.CurrentCulture;
        return culture.TwoLetterISOLanguageName.ToLowerInvariant() switch
        {
            "ru" => "ru",
            "en" => "en",
            _ => "en"
        };
    }

    public static string DetectLanguageFromRequest(HttpContext? httpContext)
    {
        if (httpContext?.Request.Headers.AcceptLanguage.Count > 0)
        {
            var acceptLanguage = httpContext.Request.Headers.AcceptLanguage.ToString();
            if (acceptLanguage.StartsWith("ru", StringComparison.OrdinalIgnoreCase))
                return "ru";
            if (acceptLanguage.StartsWith("en", StringComparison.OrdinalIgnoreCase))
                return "en";
        }

        return DetectLanguageFromCulture();
    }
}