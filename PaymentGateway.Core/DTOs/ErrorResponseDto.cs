using PaymentGateway.Core.Enums;
using System.Text.Json.Serialization;

namespace PaymentGateway.Core.DTOs;

public class ErrorResponseDto
{
    [JsonPropertyName("Success")]
    public bool Success { get; set; } = false;

    [JsonPropertyName("ErrorCode")]
    public string ErrorCode { get; set; } = string.Empty;

    [JsonPropertyName("Message")]
    public string Message { get; set; } = string.Empty;

    [JsonPropertyName("Details")]
    public string? Details { get; set; }

    [JsonPropertyName("Status")]
    public string? Status { get; set; }

    [JsonPropertyName("ErrorContext")]
    public ErrorContextDto? ErrorContext { get; set; }

    [JsonPropertyName("CorrelationId")]
    public string? CorrelationId { get; set; }

    [JsonPropertyName("Timestamp")]
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    public static ErrorResponseDto FromPaymentError(
        PaymentErrorCode errorCode, 
        string? details = null, 
        string? status = null,
        string? correlationId = null)
    {
        return new ErrorResponseDto
        {
            Success = false,
            ErrorCode = ((int)errorCode).ToString(),
            Message = GetLocalizedMessage(errorCode, "ru"),
            Details = details,
            Status = status,
            ErrorContext = new ErrorContextDto
            {
                Category = errorCode.GetCategory().ToString(),
                Severity = errorCode.GetSeverity().ToString(),
                RetryPossible = errorCode.IsRetryable(),
                UserActionRequired = errorCode.RequiresUserAction(),
                SupportContact = errorCode.RequiresSupportContact()
            },
            CorrelationId = correlationId,
            Timestamp = DateTime.UtcNow
        };
    }

    public static ErrorResponseDto CreateSuccess(object? data = null, string? correlationId = null)
    {
        return new ErrorResponseDto
        {
            Success = true,
            ErrorCode = "2000",
            Message = "Operation completed successfully",
            Status = "SUCCESS",
            CorrelationId = correlationId,
            Timestamp = DateTime.UtcNow
        };
    }

    private static string GetLocalizedMessage(PaymentErrorCode errorCode, string language)
    {
        // This will be enhanced with proper localization service later
        return language.ToLowerInvariant() switch
        {
            "ru" => GetRussianMessage(errorCode),
            "en" => GetEnglishMessage(errorCode),
            _ => GetEnglishMessage(errorCode)
        };
    }

    private static string GetRussianMessage(PaymentErrorCode errorCode)
    {
        return errorCode switch
        {
            PaymentErrorCode.Success => "Операция выполнена успешно",
            PaymentErrorCode.InvalidParameterFormat => "Переданные параметры не соответствуют требуемому формату",
            PaymentErrorCode.MissingRequiredParameters => "Отсутствуют обязательные параметры в запросе",
            PaymentErrorCode.CriticalSystemError => "Критическая ошибка платежной системы",
            PaymentErrorCode.InvalidStateTransition => "Статус операции не может быть изменен",
            PaymentErrorCode.TechnicalSupportRequired => "Требуется обращение в службу технической поддержки",
            PaymentErrorCode.CardBindingFailed => "Не удалось привязать карту к профилю клиента",
            PaymentErrorCode.InvalidCustomerStatus => "Текущий статус клиента блокирует выполнение операции",
            PaymentErrorCode.InvalidTransactionStatus => "Статус транзакции не позволяет выполнить запрашиваемое действие",
            PaymentErrorCode.RedirectUrlMissing => "Не предоставлен URL для перенаправления пользователя",
            PaymentErrorCode.ChargeMethodBlocked => "Операция списания заблокирована для данного терминала",
            PaymentErrorCode.PaymentExecutionImpossible => "Обработка платежа в данный момент невозможна",
            PaymentErrorCode.InvalidRedirectExpiration => "Указан некорректный срок действия ссылки перенаправления",
            PaymentErrorCode.MobilePaymentUnavailable => "Мобильные платежи находятся на техническом обслуживании",
            PaymentErrorCode.WebMoneyUnavailable => "Оплата через WebMoney недоступна для данного терминала",
            PaymentErrorCode.InvalidPaymentData => "Переданные данные платежа не прошли валидацию",
            PaymentErrorCode.DuplicateOrderId => "Операция с указанным идентификатором заказа уже выполняется",
            PaymentErrorCode.ServiceTemporarilyUnavailable => "Сервис временно недоступен, попробуйте позднее",
            PaymentErrorCode.InsufficientCardFunds => "На карте недостаточно денежных средств",
            PaymentErrorCode.InvalidToken => "Ошибка аутентификации. Проверьте TerminalKey и SecretKey",
            PaymentErrorCode.TerminalNotFound => "Недействительные учетные данные. Проверьте TerminalKey",
            PaymentErrorCode.TerminalBlocked => "Терминал заблокирован или неактивен",
            PaymentErrorCode.CardExpired => "Карта просрочена",
            PaymentErrorCode.InvalidCardNumber => "Некорректный номер банковской карты",
            PaymentErrorCode.InvalidCvv => "Код безопасности CVV/CVC не соответствует карте",
            PaymentErrorCode.ContactIssuingBank => "Обратитесь в банк-эмитент для решения вопроса",
            PaymentErrorCode.BankRejectedPayment => "Банк-эмитент отклонил проведение транзакции",
            PaymentErrorCode.ForeignCardNotAccepted => "Карты зарубежных банков не принимаются",
            PaymentErrorCode.ForeignCardPaymentBlocked => "Оплата картами иностранных банков заблокирована",
            PaymentErrorCode.PaymentNotFound => "Платежная операция не найдена",
            PaymentErrorCode.RequestValidationFailed => "Ошибка валидации запроса",
            PaymentErrorCode.DuplicateOrderOperation => "Операция с данным OrderId уже существует",
            PaymentErrorCode.InternalRequestProcessingError => "Внутренняя ошибка обработки запроса",
            PaymentErrorCode.InvalidPaymentStatusForOperation => "Недопустимый статус платежа для подтверждения",
            PaymentErrorCode.OperationAmountLimitExceeded2 => "Сумма подтверждения превышает авторизованную сумму",
            _ => "Произошла ошибка при обработке запроса"
        };
    }

    private static string GetEnglishMessage(PaymentErrorCode errorCode)
    {
        return errorCode switch
        {
            PaymentErrorCode.Success => "Operation completed successfully",
            PaymentErrorCode.InvalidParameterFormat => "Parameters do not match required format",
            PaymentErrorCode.MissingRequiredParameters => "Required parameters are missing in request",
            PaymentErrorCode.CriticalSystemError => "Critical payment system error",
            PaymentErrorCode.InvalidStateTransition => "Operation status cannot be changed",
            PaymentErrorCode.TechnicalSupportRequired => "Technical support contact required",
            PaymentErrorCode.CardBindingFailed => "Failed to bind card to customer profile",
            PaymentErrorCode.InvalidCustomerStatus => "Current customer status blocks operation execution",
            PaymentErrorCode.InvalidTransactionStatus => "Transaction status does not allow requested action",
            PaymentErrorCode.RedirectUrlMissing => "Redirect URL not provided",
            PaymentErrorCode.ChargeMethodBlocked => "Charge operation blocked for this terminal",
            PaymentErrorCode.PaymentExecutionImpossible => "Payment processing currently impossible",
            PaymentErrorCode.InvalidRedirectExpiration => "Invalid redirect link expiration specified",
            PaymentErrorCode.MobilePaymentUnavailable => "Mobile payments under maintenance",
            PaymentErrorCode.WebMoneyUnavailable => "WebMoney payment unavailable for this terminal",
            PaymentErrorCode.InvalidPaymentData => "Payment data validation failed",
            PaymentErrorCode.DuplicateOrderId => "Operation with this order ID already executing",
            PaymentErrorCode.ServiceTemporarilyUnavailable => "Service temporarily unavailable, try later",
            PaymentErrorCode.InsufficientCardFunds => "Insufficient funds on card",
            PaymentErrorCode.InvalidToken => "Authentication error. Check TerminalKey and SecretKey",
            PaymentErrorCode.TerminalNotFound => "Invalid credentials. Check TerminalKey",
            PaymentErrorCode.TerminalBlocked => "Terminal blocked or inactive",
            PaymentErrorCode.CardExpired => "Card expired",
            PaymentErrorCode.InvalidCardNumber => "Invalid bank card number",
            PaymentErrorCode.InvalidCvv => "CVV/CVC security code does not match card",
            PaymentErrorCode.ContactIssuingBank => "Contact issuing bank to resolve issue",
            PaymentErrorCode.BankRejectedPayment => "Issuing bank declined transaction",
            PaymentErrorCode.ForeignCardNotAccepted => "Foreign bank cards not accepted",
            PaymentErrorCode.ForeignCardPaymentBlocked => "Foreign bank card payments blocked",
            PaymentErrorCode.PaymentNotFound => "Payment operation not found",
            PaymentErrorCode.RequestValidationFailed => "Request validation failed",
            PaymentErrorCode.DuplicateOrderOperation => "Operation with this OrderId already exists",
            PaymentErrorCode.InternalRequestProcessingError => "Internal request processing error",
            PaymentErrorCode.InvalidPaymentStatusForOperation => "Invalid payment status for confirmation",
            PaymentErrorCode.OperationAmountLimitExceeded2 => "Confirmation amount exceeds authorized amount",
            _ => "An error occurred while processing request"
        };
    }
}

public class ErrorContextDto
{
    [JsonPropertyName("category")]
    public string Category { get; set; } = string.Empty;

    [JsonPropertyName("severity")]
    public string Severity { get; set; } = string.Empty;

    [JsonPropertyName("retry_possible")]
    public bool RetryPossible { get; set; }

    [JsonPropertyName("user_action_required")]
    public bool UserActionRequired { get; set; }

    [JsonPropertyName("support_contact")]
    public bool SupportContact { get; set; }
}

public class ValidationErrorResponseDto : ErrorResponseDto
{
    [JsonPropertyName("ValidationErrors")]
    public List<ValidationErrorDto> ValidationErrors { get; set; } = new();

    public static ValidationErrorResponseDto FromValidationErrors(
        List<ValidationErrorDto> validationErrors,
        string? correlationId = null)
    {
        return new ValidationErrorResponseDto
        {
            Success = false,
            ErrorCode = "9005",
            Message = "Request validation failed",
            Details = "Check required fields and formats",
            Status = "VALIDATION_ERROR",
            ValidationErrors = validationErrors,
            ErrorContext = new ErrorContextDto
            {
                Category = ErrorCategory.Validation.ToString(),
                Severity = ErrorSeverity.Medium.ToString(),
                RetryPossible = false,
                UserActionRequired = true,
                SupportContact = false
            },
            CorrelationId = correlationId,
            Timestamp = DateTime.UtcNow
        };
    }
}

public class ValidationErrorDto
{
    [JsonPropertyName("field")]
    public string Field { get; set; } = string.Empty;

    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;

    [JsonPropertyName("code")]
    public string? Code { get; set; }

    [JsonPropertyName("attempted_value")]
    public object? AttemptedValue { get; set; }
}

public class PaymentResponseDto<T> where T : class
{
    [JsonPropertyName("Success")]
    public bool Success { get; set; }

    [JsonPropertyName("Data")]
    public T? Data { get; set; }

    [JsonPropertyName("Error")]
    public ErrorResponseDto? Error { get; set; }

    [JsonPropertyName("CorrelationId")]
    public string? CorrelationId { get; set; }

    [JsonPropertyName("Timestamp")]
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    public static PaymentResponseDto<T> SuccessResult(T data, string? correlationId = null)
    {
        return new PaymentResponseDto<T>
        {
            Success = true,
            Data = data,
            CorrelationId = correlationId,
            Timestamp = DateTime.UtcNow
        };
    }

    public static PaymentResponseDto<T> ErrorResult(PaymentErrorCode errorCode, string? details = null, string? correlationId = null)
    {
        return new PaymentResponseDto<T>
        {
            Success = false,
            Error = ErrorResponseDto.FromPaymentError(errorCode, details, correlationId: correlationId),
            CorrelationId = correlationId,
            Timestamp = DateTime.UtcNow
        };
    }

    public static PaymentResponseDto<T> ErrorResult(ErrorResponseDto error, string? correlationId = null)
    {
        return new PaymentResponseDto<T>
        {
            Success = false,
            Error = error,
            CorrelationId = correlationId,
            Timestamp = DateTime.UtcNow
        };
    }
}