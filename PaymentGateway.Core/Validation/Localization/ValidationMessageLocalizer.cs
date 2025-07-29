using Microsoft.Extensions.Localization;
using System.Globalization;
using System.Resources;

namespace PaymentGateway.Core.Validation.Localization;

/// <summary>
/// Service for localizing validation error messages
/// </summary>
public interface IValidationMessageLocalizer
{
    string GetLocalizedMessage(string errorCode, string language = "ru", params object[] args);
    string GetLocalizedMessage(string errorCode, CultureInfo culture, params object[] args);
    Dictionary<string, string> GetAllMessages(string language = "ru");
    bool IsLanguageSupported(string language);
}

/// <summary>
/// Implementation of validation message localizer
/// </summary>
public class ValidationMessageLocalizer : IValidationMessageLocalizer
{
    private readonly Dictionary<string, Dictionary<string, string>> _messages;
    private readonly string[] _supportedLanguages = { "ru", "en" };

    public ValidationMessageLocalizer()
    {
        _messages = InitializeMessages();
    }

    public string GetLocalizedMessage(string errorCode, string language = "ru", params object[] args)
    {
        if (!IsLanguageSupported(language))
            language = "ru"; // Default to Russian

        if (_messages.TryGetValue(language, out var languageMessages) &&
            languageMessages.TryGetValue(errorCode, out var message))
        {
            try
            {
                return args.Length > 0 ? string.Format(message, args) : message;
            }
            catch (FormatException)
            {
                return message; // Return unformatted message if formatting fails
            }
        }

        // Fallback to error code if message not found
        return $"Validation error: {errorCode}";
    }

    public string GetLocalizedMessage(string errorCode, CultureInfo culture, params object[] args)
    {
        var language = culture.TwoLetterISOLanguageName.ToLowerInvariant();
        return GetLocalizedMessage(errorCode, language, args);
    }

    public Dictionary<string, string> GetAllMessages(string language = "ru")
    {
        if (!IsLanguageSupported(language))
            language = "ru";

        return _messages.TryGetValue(language, out var messages) 
            ? new Dictionary<string, string>(messages) 
            : new Dictionary<string, string>();
    }

    public bool IsLanguageSupported(string language)
    {
        return _supportedLanguages.Contains(language?.ToLowerInvariant());
    }

    private Dictionary<string, Dictionary<string, string>> InitializeMessages()
    {
        var messages = new Dictionary<string, Dictionary<string, string>>();

        // Russian messages
        messages["ru"] = new Dictionary<string, string>
        {
            // Team and authentication errors
            ["TEAM_SLUG_REQUIRED"] = "Поле TeamSlug обязательно для заполнения",
            ["TEAM_SLUG_TOO_LONG"] = "TeamSlug не может превышать 50 символов",
            ["TEAM_SLUG_INVALID_FORMAT"] = "TeamSlug может содержать только буквы, цифры, дефисы и подчеркивания",
            ["TEAM_NOT_FOUND"] = "Команда не найдена или неактивна",
            ["TOKEN_REQUIRED"] = "Поле Token обязательно для заполнения",
            ["TOKEN_TOO_LONG"] = "Token не может превышать 256 символов",
            ["TOKEN_INVALID"] = "Недействительный токен аутентификации",

            // Amount validation errors
            ["AMOUNT_TOO_SMALL"] = "Сумма должна быть не менее 1000 копеек (10 руб.)",
            ["AMOUNT_TOO_LARGE"] = "Сумма не может превышать 50000000 копеек (500000 руб.)",
            ["AMOUNT_INVALID"] = "Сумма должна быть больше нуля",
            ["AMOUNT_CONSISTENCY_VIOLATION"] = "Суммы платежа не согласованы между всеми полями",
            ["TRANSACTION_LIMIT_EXCEEDED"] = "Сумма платежа превышает лимит одной транзакции",
            ["DAILY_LIMIT_EXCEEDED"] = "Сумма платежа превышает дневной лимит",
            ["DAILY_TRANSACTION_COUNT_EXCEEDED"] = "Превышен лимит количества транзакций в день",

            // Order and payment identification errors
            ["ORDER_ID_REQUIRED"] = "Поле OrderId обязательно для заполнения",
            ["ORDER_ID_TOO_LONG"] = "OrderId не может превышать 36 символов",
            ["ORDER_ID_INVALID_FORMAT"] = "OrderId может содержать только буквы, цифры, дефисы и подчеркивания",
            ["ORDER_ID_ALREADY_EXISTS"] = "OrderId уже существует для данной команды",
            ["ORDER_ID_DUPLICATE"] = "Дублирующий OrderId для данной команды",
            ["PAYMENT_ID_REQUIRED"] = "Поле PaymentId обязательно для заполнения",
            ["PAYMENT_ID_TOO_LONG"] = "PaymentId не может превышать 20 символов",
            ["PAYMENT_ID_INVALID_FORMAT"] = "PaymentId должен содержать только цифры",
            ["PAYMENT_NOT_FOUND"] = "Платеж не найден",
            ["PAYMENT_NOT_FOUND_BY_ORDER_ID"] = "Платеж с данным OrderId не найден",
            ["PAYMENT_OR_ORDER_ID_REQUIRED"] = "Необходимо указать PaymentId или OrderId",

            // Currency validation errors
            ["CURRENCY_REQUIRED"] = "Поле Currency обязательно для заполнения",
            ["CURRENCY_INVALID_LENGTH"] = "Валюта должна содержать ровно 3 символа",
            ["CURRENCY_NOT_SUPPORTED"] = "Валюта не поддерживается",
            ["CURRENCY_NOT_SUPPORTED_FOR_TEAM"] = "Валюта не поддерживается для данной команды",
            ["CURRENCY_AMOUNT_RELATIONSHIP_INVALID"] = "Сумма платежа некорректна для указанной валюты",

            // Payment type and configuration errors
            ["PAY_TYPE_INVALID"] = "PayType должен быть 'O' (одностадийный) или 'T' (двухстадийный)",
            ["PAYMENT_TYPE_CONFIGURATION_INCONSISTENT"] = "Конфигурация платежа не соответствует указанному типу платежа",
            ["PAYMENT_EXPIRY_INVALID"] = "PaymentExpiry должен быть больше нуля",
            ["PAYMENT_EXPIRY_TOO_LONG"] = "PaymentExpiry не может превышать 43200 минут (30 дней)",
            ["EXPIRY_TIME_NOT_ALLOWED"] = "Время истечения платежа не входит в разрешенный диапазон для данной команды",

            // Customer validation errors
            ["CUSTOMER_KEY_TOO_LONG"] = "CustomerKey не может превышать 36 символов",
            ["CUSTOMER_NOT_VALID_FOR_TEAM"] = "Клиент недействителен для данной команды",
            ["CUSTOMER_NOT_VALID"] = "Клиент недействителен для данной команды",
            ["CUSTOMER_DATA_INCONSISTENT"] = "Поля данных клиента не согласованы между собой",

            // Contact information errors
            ["EMAIL_INVALID_FORMAT"] = "Неверный формат email",
            ["EMAIL_TOO_LONG"] = "Email не может превышать 254 символа",
            ["PHONE_INVALID_FORMAT"] = "Телефон должен содержать 7-20 цифр с необязательным ведущим +",
            ["RECEIPT_EMAIL_INVALID_FORMAT"] = "Неверный формат email чека",
            ["RECEIPT_EMAIL_TOO_LONG"] = "Email чека не может превышать 254 символа",
            ["RECEIPT_PHONE_INVALID_FORMAT"] = "Телефон чека должен содержать 7-20 цифр с необязательным ведущим +",
            ["RECEIPT_CONTACT_REQUIRED"] = "Необходимо указать email или телефон для чека",

            // Description and text field errors
            ["DESCRIPTION_TOO_LONG"] = "Описание не может превышать 140 символов",
            ["REASON_TOO_LONG"] = "Причина не может превышать 500 символов",

            // Language and localization errors
            ["LANGUAGE_REQUIRED"] = "Поле Language обязательно для заполнения",
            ["LANGUAGE_NOT_SUPPORTED"] = "Язык должен быть 'ru' или 'en'",
            ["LOCALIZATION_DATA_INCONSISTENT"] = "Настройка языка не согласована с другими локализованными данными",

            // URL validation errors
            ["SUCCESS_URL_INVALID"] = "SuccessURL должен быть действительным URL",
            ["SUCCESS_URL_TOO_LONG"] = "SuccessURL не может превышать 2048 символов",
            ["FAIL_URL_INVALID"] = "FailURL должен быть действительным URL",
            ["FAIL_URL_TOO_LONG"] = "FailURL не может превышать 2048 символов",
            ["NOTIFICATION_URL_INVALID"] = "NotificationURL должен быть действительным URL",
            ["NOTIFICATION_URL_TOO_LONG"] = "NotificationURL не может превышать 2048 символов",
            ["CALLBACK_URLS_INCONSISTENT"] = "URL обратного вызова должны использовать согласованный протокол и домен",

            // Item validation errors
            ["ITEM_NAME_REQUIRED"] = "Название товара обязательно для заполнения",
            ["ITEM_NAME_TOO_LONG"] = "Название товара не может превышать 100 символов",
            ["ITEM_QUANTITY_INVALID"] = "Количество товара должно быть больше нуля",
            ["ITEM_QUANTITY_TOO_LARGE"] = "Количество товара не может превышать 99999.999",
            ["ITEM_PRICE_INVALID"] = "Цена товара должна быть больше нуля",
            ["ITEM_AMOUNT_INVALID"] = "Сумма товара должна быть больше нуля",
            ["ITEM_CATEGORY_TOO_LONG"] = "Категория товара не может превышать 50 символов",
            ["ITEM_AMOUNT_CALCULATION_MISMATCH"] = "Сумма товара должна равняться количество * цена",
            ["ITEMS_TOTAL_MISMATCH"] = "Сумма всех товаров должна равняться сумме платежа",
            ["ITEMS_CONFIGURATION_INVALID"] = "Конфигурация товаров недействительна для данного типа платежа",

            // Receipt validation errors
            ["RECEIPT_DATA_REQUIRED"] = "Необходимо указать хотя бы один формат чека (FFD 1.05 или FFD 1.2)",
            ["RECEIPT_DATA_INCONSISTENT"] = "Контактная информация чека должна совпадать с контактной информацией клиента",
            ["RECEIPT_REQUIRED_FOR_TEAM"] = "Чек обязателен для данной команды",
            ["TAXATION_INVALID"] = "Taxation должен быть действительным типом системы налогообложения",

            // Data validation errors
            ["DATA_INVALID"] = "Словарь данных содержит недействительные записи",
            ["ADDITIONAL_DATA_INVALID"] = "Словарь дополнительных данных содержит недействительные записи",
            ["CUSTOM_DATA_INVALID"] = "Словарь пользовательских данных содержит недействительные записи",

            // Payment state validation errors
            ["PAYMENT_NOT_AUTHORIZABLE"] = "Платеж не находится в состоянии, позволяющем подтверждение",
            ["PAYMENT_NOT_CONFIRMABLE"] = "Платеж не находится в состоянии, позволяющем подтверждение",
            ["PAYMENT_NOT_CANCELLABLE"] = "Платеж не находится в состоянии, позволяющем отмену",
            ["PAYMENT_TEAM_MISMATCH"] = "Платеж не принадлежит данной команде",
            ["PAYMENT_EXPIRED"] = "Срок действия платежа истек",

            // Confirmation and cancellation errors
            ["CONFIRMATION_AMOUNT_INVALID"] = "Сумма подтверждения недействительна для данного платежа",
            ["CONFIRMATION_AMOUNT_EXCEEDS_PAYMENT"] = "Сумма подтверждения не может превышать сумму платежа",
            ["CONFIRMATION_TIME_EXPIRED"] = "Время подтверждения платежа истекло",
            ["CONFIRMATION_DATA_INCONSISTENT"] = "Данные подтверждения не согласованы с исходным платежом",
            ["REFUND_AMOUNT_INVALID"] = "Сумма возврата недействительна для данного платежа",
            ["REFUND_AMOUNT_EXCEEDS_PAYMENT"] = "Сумма возврата не может превышать сумму платежа",
            ["REFUND_TIME_EXPIRED"] = "Время возврата платежа истекло",
            ["REFUND_TIME_LIMIT_EXCEEDED"] = "Превышен лимит времени для возврата",
            ["REFUND_REASON_REQUIRED"] = "Причина возврата обязательна для данного типа платежа",
            ["PARTIAL_REFUNDS_NOT_ALLOWED"] = "Частичные возвраты не разрешены для данной команды",
            ["CANCELLATION_DATA_INVALID"] = "Данные отмены недействительны или не согласованы",

            // System and service errors
            ["VALIDATION_SERVICE_ERROR"] = "Сервис валидации временно недоступен",
            ["TIMESTAMP_INVALID"] = "Timestamp должен быть в допустимом диапазоне",
            ["REQUEST_ID_TOO_LONG"] = "RequestId не может превышать 50 символов",
            ["REQUEST_ID_INVALID_FORMAT"] = "RequestId может содержать только буквы, цифры, дефисы и подчеркивания",
            ["INCLUDE_HISTORY_INVALID"] = "IncludeHistory должен быть действительным булевым значением"
        };

        // English messages
        messages["en"] = new Dictionary<string, string>
        {
            // Team and authentication errors
            ["TEAM_SLUG_REQUIRED"] = "TeamSlug field is required",
            ["TEAM_SLUG_TOO_LONG"] = "TeamSlug cannot exceed 50 characters",
            ["TEAM_SLUG_INVALID_FORMAT"] = "TeamSlug can only contain letters, numbers, hyphens, and underscores",
            ["TEAM_NOT_FOUND"] = "Team does not exist or is not active",
            ["TOKEN_REQUIRED"] = "Token field is required",
            ["TOKEN_TOO_LONG"] = "Token cannot exceed 256 characters",
            ["TOKEN_INVALID"] = "Invalid authentication token",

            // Amount validation errors
            ["AMOUNT_TOO_SMALL"] = "Amount must be at least 1000 kopecks (10 RUB)",
            ["AMOUNT_TOO_LARGE"] = "Amount cannot exceed 50000000 kopecks (500000 RUB)",
            ["AMOUNT_INVALID"] = "Amount must be greater than zero",
            ["AMOUNT_CONSISTENCY_VIOLATION"] = "Payment amounts are not consistent across all fields",
            ["TRANSACTION_LIMIT_EXCEEDED"] = "Payment amount exceeds single transaction limit",
            ["DAILY_LIMIT_EXCEEDED"] = "Payment amount exceeds daily limit", 
            ["DAILY_TRANSACTION_COUNT_EXCEEDED"] = "Daily transaction count limit exceeded",

            // Order and payment identification errors
            ["ORDER_ID_REQUIRED"] = "OrderId field is required",
            ["ORDER_ID_TOO_LONG"] = "OrderId cannot exceed 36 characters",
            ["ORDER_ID_INVALID_FORMAT"] = "OrderId can only contain letters, numbers, hyphens, and underscores",
            ["ORDER_ID_ALREADY_EXISTS"] = "OrderId already exists for this team",
            ["ORDER_ID_DUPLICATE"] = "Duplicate OrderId for this team",
            ["PAYMENT_ID_REQUIRED"] = "PaymentId field is required",
            ["PAYMENT_ID_TOO_LONG"] = "PaymentId cannot exceed 20 characters",
            ["PAYMENT_ID_INVALID_FORMAT"] = "PaymentId must contain only digits",
            ["PAYMENT_NOT_FOUND"] = "Payment not found",
            ["PAYMENT_NOT_FOUND_BY_ORDER_ID"] = "Payment with this OrderId not found",
            ["PAYMENT_OR_ORDER_ID_REQUIRED"] = "Either PaymentId or OrderId must be provided",

            // Currency validation errors
            ["CURRENCY_REQUIRED"] = "Currency field is required",
            ["CURRENCY_INVALID_LENGTH"] = "Currency must be exactly 3 characters",
            ["CURRENCY_NOT_SUPPORTED"] = "Currency is not supported",
            ["CURRENCY_NOT_SUPPORTED_FOR_TEAM"] = "Currency is not supported for this team",
            ["CURRENCY_AMOUNT_RELATIONSHIP_INVALID"] = "Payment amount is not valid for the specified currency",

            // Payment type and configuration errors
            ["PAY_TYPE_INVALID"] = "PayType must be 'O' (single-stage) or 'T' (two-stage)",
            ["PAYMENT_TYPE_CONFIGURATION_INCONSISTENT"] = "Payment configuration is not consistent with the specified payment type",
            ["PAYMENT_EXPIRY_INVALID"] = "PaymentExpiry must be greater than zero",
            ["PAYMENT_EXPIRY_TOO_LONG"] = "PaymentExpiry cannot exceed 43200 minutes (30 days)",
            ["EXPIRY_TIME_NOT_ALLOWED"] = "Payment expiry time is not within allowed range for this team",

            // Customer validation errors
            ["CUSTOMER_KEY_TOO_LONG"] = "CustomerKey cannot exceed 36 characters",
            ["CUSTOMER_NOT_VALID_FOR_TEAM"] = "Customer is not valid for this team",
            ["CUSTOMER_NOT_VALID"] = "Customer is not valid for this team",
            ["CUSTOMER_DATA_INCONSISTENT"] = "Customer data fields are not consistent with each other",

            // Contact information errors
            ["EMAIL_INVALID_FORMAT"] = "Invalid email format",
            ["EMAIL_TOO_LONG"] = "Email cannot exceed 254 characters",
            ["PHONE_INVALID_FORMAT"] = "Phone must be 7-20 digits with optional leading +",
            ["RECEIPT_EMAIL_INVALID_FORMAT"] = "Invalid receipt email format",
            ["RECEIPT_EMAIL_TOO_LONG"] = "Receipt email cannot exceed 254 characters",
            ["RECEIPT_PHONE_INVALID_FORMAT"] = "Receipt phone must be 7-20 digits with optional leading +",
            ["RECEIPT_CONTACT_REQUIRED"] = "Either receipt email or phone must be provided",

            // Description and text field errors
            ["DESCRIPTION_TOO_LONG"] = "Description cannot exceed 140 characters",
            ["REASON_TOO_LONG"] = "Reason cannot exceed 500 characters",

            // Language and localization errors
            ["LANGUAGE_REQUIRED"] = "Language field is required",
            ["LANGUAGE_NOT_SUPPORTED"] = "Language must be 'ru' or 'en'",
            ["LOCALIZATION_DATA_INCONSISTENT"] = "Language setting is not consistent with other localized data",

            // URL validation errors
            ["SUCCESS_URL_INVALID"] = "SuccessURL must be a valid URL",
            ["SUCCESS_URL_TOO_LONG"] = "SuccessURL cannot exceed 2048 characters",  
            ["FAIL_URL_INVALID"] = "FailURL must be a valid URL",
            ["FAIL_URL_TOO_LONG"] = "FailURL cannot exceed 2048 characters",
            ["NOTIFICATION_URL_INVALID"] = "NotificationURL must be a valid URL",
            ["NOTIFICATION_URL_TOO_LONG"] = "NotificationURL cannot exceed 2048 characters",
            ["CALLBACK_URLS_INCONSISTENT"] = "Callback URLs must use consistent protocol and domain",

            // Item validation errors
            ["ITEM_NAME_REQUIRED"] = "Item name is required",
            ["ITEM_NAME_TOO_LONG"] = "Item name cannot exceed 100 characters",
            ["ITEM_QUANTITY_INVALID"] = "Item quantity must be greater than zero",
            ["ITEM_QUANTITY_TOO_LARGE"] = "Item quantity cannot exceed 99999.999",
            ["ITEM_PRICE_INVALID"] = "Item price must be greater than zero",
            ["ITEM_AMOUNT_INVALID"] = "Item amount must be greater than zero",
            ["ITEM_CATEGORY_TOO_LONG"] = "Item category cannot exceed 50 characters",
            ["ITEM_AMOUNT_CALCULATION_MISMATCH"] = "Item amount must equal quantity * price",
            ["ITEMS_TOTAL_MISMATCH"] = "Sum of item amounts must equal payment amount",
            ["ITEMS_CONFIGURATION_INVALID"] = "Items configuration is not valid for this payment type",

            // Receipt validation errors
            ["RECEIPT_DATA_REQUIRED"] = "At least one receipt format (FFD 1.05 or FFD 1.2) must be provided",
            ["RECEIPT_DATA_INCONSISTENT"] = "Receipt contact information must match customer contact information",
            ["RECEIPT_REQUIRED_FOR_TEAM"] = "Receipt is required for this team",
            ["TAXATION_INVALID"] = "Taxation must be a valid taxation system type",

            // Data validation errors
            ["DATA_INVALID"] = "Data dictionary contains invalid entries",
            ["ADDITIONAL_DATA_INVALID"] = "AdditionalData dictionary contains invalid entries",
            ["CUSTOM_DATA_INVALID"] = "CustomData dictionary contains invalid entries",

            // Payment state validation errors
            ["PAYMENT_NOT_AUTHORIZABLE"] = "Payment is not in a state that allows confirmation",
            ["PAYMENT_NOT_CONFIRMABLE"] = "Payment is not in a confirmable state",
            ["PAYMENT_NOT_CANCELLABLE"] = "Payment is not in a cancellable state",
            ["PAYMENT_TEAM_MISMATCH"] = "Payment does not belong to this team",
            ["PAYMENT_EXPIRED"] = "Payment has expired",

            // Confirmation and cancellation errors
            ["CONFIRMATION_AMOUNT_INVALID"] = "Confirmation amount is not valid for this payment",
            ["CONFIRMATION_AMOUNT_EXCEEDS_PAYMENT"] = "Confirmation amount cannot exceed payment amount",
            ["CONFIRMATION_TIME_EXPIRED"] = "Payment confirmation time has expired",
            ["CONFIRMATION_DATA_INCONSISTENT"] = "Confirmation data is not consistent with the original payment",
            ["REFUND_AMOUNT_INVALID"] = "Refund amount is not valid for this payment",
            ["REFUND_AMOUNT_EXCEEDS_PAYMENT"] = "Refund amount cannot exceed payment amount",
            ["REFUND_TIME_EXPIRED"] = "Payment refund time has expired",
            ["REFUND_TIME_LIMIT_EXCEEDED"] = "Refund time limit has been exceeded",
            ["REFUND_REASON_REQUIRED"] = "Refund reason is required for this payment type",
            ["PARTIAL_REFUNDS_NOT_ALLOWED"] = "Partial refunds are not allowed for this team",
            ["CANCELLATION_DATA_INVALID"] = "Cancellation data is not valid or consistent",

            // System and service errors
            ["VALIDATION_SERVICE_ERROR"] = "Validation service temporarily unavailable",
            ["TIMESTAMP_INVALID"] = "Timestamp must be within acceptable range",
            ["REQUEST_ID_TOO_LONG"] = "RequestId cannot exceed 50 characters",
            ["REQUEST_ID_INVALID_FORMAT"] = "RequestId can only contain letters, numbers, hyphens, and underscores",
            ["INCLUDE_HISTORY_INVALID"] = "IncludeHistory must be a valid boolean value"
        };

        return messages;
    }
}