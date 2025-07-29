using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using PaymentGateway.Core.DTOs;
using PaymentGateway.Core.Enums;
using PaymentGateway.Core.Services;
using System.Net;
using System.Text.Json;
using FluentValidation;

namespace PaymentGateway.API.Middleware;

public class GlobalExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<GlobalExceptionHandlingMiddleware> _logger;
    private readonly ICorrelationIdService _correlationIdService;

    public GlobalExceptionHandlingMiddleware(
        RequestDelegate next,
        ILogger<GlobalExceptionHandlingMiddleware> logger,
        ICorrelationIdService correlationIdService)
    {
        _next = next;
        _logger = logger;
        _correlationIdService = correlationIdService;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            await HandleExceptionAsync(context, ex);
        }
    }

    private async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        var correlationId = _correlationIdService.CurrentCorrelationId;
        
        _logger.LogError(exception, "Unhandled exception occurred. CorrelationId: {CorrelationId}", correlationId);

        var errorResponse = MapExceptionToErrorResponse(exception, correlationId);
        var httpStatusCode = GetHttpStatusCode(errorResponse.ErrorCode);

        context.Response.ContentType = "application/json";
        context.Response.StatusCode = (int)httpStatusCode;

        var jsonResponse = JsonSerializer.Serialize(errorResponse, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        });

        await context.Response.WriteAsync(jsonResponse);

        // Log metrics for error tracking
        LogErrorMetrics(errorResponse, exception);
    }

    private ErrorResponseDto MapExceptionToErrorResponse(Exception exception, string correlationId)
    {
        return exception switch
        {
            ValidationException validationEx => HandleValidationException(validationEx, correlationId),
            PaymentException paymentEx => HandlePaymentException(paymentEx, correlationId),
            UnauthorizedAccessException => HandleUnauthorizedException(correlationId),
            ArgumentException argumentEx => HandleArgumentException(argumentEx, correlationId),
            InvalidOperationException invalidOpEx => HandleInvalidOperationException(invalidOpEx, correlationId),
            TimeoutException timeoutEx => HandleTimeoutException(timeoutEx, correlationId),
            HttpRequestException httpEx => HandleHttpRequestException(httpEx, correlationId),
            _ => HandleGenericException(exception, correlationId)
        };
    }

    private ErrorResponseDto HandleValidationException(ValidationException validationException, string correlationId)
    {
        var validationErrors = validationException.Errors.Select(error => new ValidationErrorDto
        {
            Field = error.PropertyName,
            Message = error.ErrorMessage,
            Code = error.ErrorCode,
            AttemptedValue = error.AttemptedValue
        }).ToList();

        return ValidationErrorResponseDto.FromValidationErrors(validationErrors, correlationId);
    }

    private ErrorResponseDto HandlePaymentException(PaymentException paymentException, string correlationId)
    {
        return ErrorResponseDto.FromPaymentError(
            paymentException.ErrorCode,
            paymentException.Details,
            paymentException.Status,
            correlationId);
    }

    private ErrorResponseDto HandleUnauthorizedException(string correlationId)
    {
        return ErrorResponseDto.FromPaymentError(
            PaymentErrorCode.TokenAuthenticationFailed,
            "Authentication failed - verify credentials",
            "UNAUTHORIZED",
            correlationId);
    }

    private ErrorResponseDto HandleArgumentException(ArgumentException argumentException, string correlationId)
    {
        return ErrorResponseDto.FromPaymentError(
            PaymentErrorCode.InvalidParameterFormat,
            argumentException.Message,
            "BAD_REQUEST",
            correlationId);
    }

    private ErrorResponseDto HandleInvalidOperationException(InvalidOperationException invalidOpException, string correlationId)
    {
        // Check if it's a state transition error
        if (invalidOpException.Message.Contains("state") || invalidOpException.Message.Contains("status"))
        {
            return ErrorResponseDto.FromPaymentError(
                PaymentErrorCode.InvalidStateTransition,
                invalidOpException.Message,
                "INVALID_STATE",
                correlationId);
        }

        return ErrorResponseDto.FromPaymentError(
            PaymentErrorCode.InternalRequestProcessingError,
            invalidOpException.Message,
            "PROCESSING_ERROR",
            correlationId);
    }

    private ErrorResponseDto HandleTimeoutException(TimeoutException timeoutException, string correlationId)
    {
        return ErrorResponseDto.FromPaymentError(
            PaymentErrorCode.ServiceTemporarilyUnavailable,
            "Operation timed out - please try again",
            "TIMEOUT",
            correlationId);
    }

    private ErrorResponseDto HandleHttpRequestException(HttpRequestException httpException, string correlationId)
    {
        return ErrorResponseDto.FromPaymentError(
            PaymentErrorCode.ExternalServiceUnavailable,
            "External service unavailable",
            "SERVICE_UNAVAILABLE",
            correlationId);
    }

    private ErrorResponseDto HandleGenericException(Exception exception, string correlationId)
    {
        var isCritical = IsCriticalException(exception);
        var errorCode = isCritical ? PaymentErrorCode.CriticalInternalSystemError : PaymentErrorCode.InternalRequestProcessingError;

        return ErrorResponseDto.FromPaymentError(
            errorCode,
            "An unexpected error occurred",
            "INTERNAL_ERROR",
            correlationId);
    }

    private bool IsCriticalException(Exception exception)
    {
        // Define critical exception types
        var criticalExceptionTypes = new[]
        {
            typeof(OutOfMemoryException),
            typeof(StackOverflowException),
            typeof(AccessViolationException),
            typeof(AppDomainUnloadedException),
            typeof(BadImageFormatException),
            typeof(CannotUnloadAppDomainException),
            typeof(InvalidProgramException)
        };

        return criticalExceptionTypes.Contains(exception.GetType()) ||
               exception.Message.Contains("database", StringComparison.OrdinalIgnoreCase) ||
               exception.Message.Contains("connection", StringComparison.OrdinalIgnoreCase);
    }

    private HttpStatusCode GetHttpStatusCode(string errorCode)
    {
        if (!int.TryParse(errorCode, out var code))
            return HttpStatusCode.InternalServerError;

        var paymentErrorCode = (PaymentErrorCode)code;

        return paymentErrorCode switch
        {
            PaymentErrorCode.Success => HttpStatusCode.OK,
            
            // Authentication errors
            PaymentErrorCode.InvalidToken or
            PaymentErrorCode.TokenAuthenticationFailed or
            PaymentErrorCode.TerminalNotFound or
            PaymentErrorCode.TerminalAccessDenied => HttpStatusCode.Unauthorized,
            
            // Validation errors
            PaymentErrorCode.InvalidParameterFormat or
            PaymentErrorCode.MissingRequiredParameters or
            PaymentErrorCode.RequestValidationFailed or
            PaymentErrorCode.InvalidRequestData => HttpStatusCode.BadRequest,
            
            // Not found errors
            PaymentErrorCode.PaymentNotFound or
            PaymentErrorCode.OperationNotFound => HttpStatusCode.NotFound,
            
            // Conflict errors
            PaymentErrorCode.DuplicateOrderId or
            PaymentErrorCode.DuplicateOrderOperation or
            PaymentErrorCode.InvalidStateTransition => HttpStatusCode.Conflict,
            
            // Rate limiting
            PaymentErrorCode.AuthorizationAttemptLimitExceeded or
            PaymentErrorCode.CustomerPaymentLimitExceeded or
            PaymentErrorCode.DailyPaymentLimitExceeded => HttpStatusCode.TooManyRequests,
            
            // Payment processing errors (422 Unprocessable Entity)
            PaymentErrorCode.InsufficientCardFunds or
            PaymentErrorCode.CardExpired or
            PaymentErrorCode.InvalidCardNumber or
            PaymentErrorCode.InvalidCvv or
            PaymentErrorCode.BankRejectedPayment => HttpStatusCode.UnprocessableEntity,
            
            // Service unavailable
            PaymentErrorCode.ServiceTemporarilyUnavailable or
            PaymentErrorCode.ExternalServiceUnavailable or
            PaymentErrorCode.TemporaryProcessingIssue => HttpStatusCode.ServiceUnavailable,
            
            // Critical system errors
            PaymentErrorCode.CriticalSystemError or
            PaymentErrorCode.CriticalInternalSystemError or
            PaymentErrorCode.InternalSystemError => HttpStatusCode.InternalServerError,
            
            // Default to Internal Server Error
            _ => HttpStatusCode.InternalServerError
        };
    }

    private void LogErrorMetrics(ErrorResponseDto errorResponse, Exception exception)
    {
        try
        {
            // Log structured metrics for monitoring
            var properties = new Dictionary<string, object>
            {
                { "ErrorCode", errorResponse.ErrorCode },
                { "ErrorCategory", errorResponse.ErrorContext?.Category ?? "Unknown" },
                { "ErrorSeverity", errorResponse.ErrorContext?.Severity ?? "Unknown" },
                { "ExceptionType", exception.GetType().Name },
                { "CorrelationId", errorResponse.CorrelationId ?? "Unknown" }
            };

            _logger.LogError("Payment error occurred: {ErrorCode} - {Message} - Properties: {@Properties}",
                errorResponse.ErrorCode, errorResponse.Message, properties);
        }
        catch (Exception logException)
        {
            // Don't let logging errors affect the response
            _logger.LogError(logException, "Failed to log error metrics");
        }
    }
}

// Custom payment exception class for domain-specific errors
public class PaymentException : Exception
{
    public PaymentErrorCode ErrorCode { get; }
    public string? Details { get; }
    public string? Status { get; }

    public PaymentException(PaymentErrorCode errorCode, string? details = null, string? status = null)
        : base(GetDefaultMessage(errorCode))
    {
        ErrorCode = errorCode;
        Details = details;
        Status = status;
    }

    public PaymentException(PaymentErrorCode errorCode, string message, Exception innerException, string? details = null, string? status = null)
        : base(message, innerException)
    {
        ErrorCode = errorCode;
        Details = details;
        Status = status;
    }

    private static string GetDefaultMessage(PaymentErrorCode errorCode)
    {
        return errorCode switch
        {
            PaymentErrorCode.PaymentNotFound => "Payment not found",
            PaymentErrorCode.InvalidStateTransition => "Invalid payment state transition",
            PaymentErrorCode.DuplicateOrderId => "Order with this ID already exists",
            PaymentErrorCode.InsufficientCardFunds => "Insufficient funds on card",
            PaymentErrorCode.InvalidToken => "Invalid authentication token",
            _ => "Payment processing error occurred"
        };
    }
}

// Extension methods for easier exception throwing
public static class PaymentExceptionExtensions
{
    public static void ThrowPaymentException(this PaymentErrorCode errorCode, string? details = null, string? status = null)
    {
        throw new PaymentException(errorCode, details, status);
    }

    public static void ThrowPaymentExceptionIf(this PaymentErrorCode errorCode, bool condition, string? details = null, string? status = null)
    {
        if (condition)
        {
            throw new PaymentException(errorCode, details, status);
        }
    }
}