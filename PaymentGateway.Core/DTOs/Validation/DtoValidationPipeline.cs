using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PaymentGateway.Core.DTOs.Common;
using System.ComponentModel.DataAnnotations;

namespace PaymentGateway.Core.DTOs.Validation;

/// <summary>
/// Validation pipeline for DTOs combining FluentValidation and DataAnnotations
/// </summary>
public interface IDtoValidationPipeline
{
    Task<ValidationResult<T>> ValidateAsync<T>(T dto, CancellationToken cancellationToken = default) where T : class;
    Task<ValidationResult<T>> ValidateAsync<T>(T dto, string validationGroup, CancellationToken cancellationToken = default) where T : class;
    ValidationResult<T> Validate<T>(T dto) where T : class;
    ValidationResult<T> Validate<T>(T dto, string validationGroup) where T : class;
}

/// <summary>
/// DTO validation pipeline implementation
/// </summary>
public class DtoValidationPipeline : IDtoValidationPipeline
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<DtoValidationPipeline> _logger;

    public DtoValidationPipeline(
        IServiceProvider serviceProvider,
        ILogger<DtoValidationPipeline> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public async Task<ValidationResult<T>> ValidateAsync<T>(T dto, CancellationToken cancellationToken = default) where T : class
    {
        return await ValidateAsync(dto, "default", cancellationToken);
    }

    public async Task<ValidationResult<T>> ValidateAsync<T>(T dto, string validationGroup, CancellationToken cancellationToken = default) where T : class
    {
        if (dto == null)
        {
            return ValidationResult<T>.Failure("DTO cannot be null", "NULL_DTO");
        }

        var validationResult = new ValidationResult<T>(dto);

        try
        {
            // Step 1: DataAnnotations validation
            await ValidateDataAnnotationsAsync(dto, validationResult, cancellationToken);

            // Step 2: FluentValidation (if validator exists)
            await ValidateWithFluentValidationAsync(dto, validationGroup, validationResult, cancellationToken);

            // Step 3: Custom business validation
            await ValidateBusinessRulesAsync(dto, validationResult, cancellationToken);

            _logger.LogDebug("Validation completed for {DtoType}. IsValid: {IsValid}, ErrorCount: {ErrorCount}",
                typeof(T).Name, validationResult.IsValid, validationResult.Errors.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during validation of {DtoType}", typeof(T).Name);
            validationResult.AddError("VALIDATION_ERROR", "An error occurred during validation", "general");
        }

        return validationResult;
    }

    public ValidationResult<T> Validate<T>(T dto) where T : class
    {
        return ValidateAsync(dto).GetAwaiter().GetResult();
    }

    public ValidationResult<T> Validate<T>(T dto, string validationGroup) where T : class
    {
        return ValidateAsync(dto, validationGroup).GetAwaiter().GetResult();
    }

    private async Task ValidateDataAnnotationsAsync<T>(T dto, ValidationResult<T> result, CancellationToken cancellationToken) where T : class
    {
        try
        {
            var validationContext = new ValidationContext(dto);
            var dataAnnotationResults = new List<System.ComponentModel.DataAnnotations.ValidationResult>();

            var isValid = Validator.TryValidateObject(dto, validationContext, dataAnnotationResults, true);

            if (!isValid)
            {
                foreach (var validationError in dataAnnotationResults)
                {
                    var fieldName = validationError.MemberNames.FirstOrDefault() ?? "general";
                    result.AddError("DATA_ANNOTATION_ERROR", validationError.ErrorMessage ?? "Validation error", fieldName);
                }
            }

            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during DataAnnotations validation");
            result.AddError("DATA_ANNOTATION_ERROR", "DataAnnotations validation failed", "general");
        }
    }

    private async Task ValidateWithFluentValidationAsync<T>(T dto, string validationGroup, ValidationResult<T> result, CancellationToken cancellationToken) where T : class
    {
        try
        {
            var validator = _serviceProvider.GetService<IValidator<T>>();
            if (validator != null)
            {
                var fluentResult = await validator.ValidateAsync(dto, cancellationToken);

                if (!fluentResult.IsValid)
                {
                    foreach (var error in fluentResult.Errors)
                    {
                        result.AddError(
                            error.ErrorCode ?? "FLUENT_VALIDATION_ERROR",
                            error.ErrorMessage,
                            error.PropertyName);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during FluentValidation");
            result.AddError("FLUENT_VALIDATION_ERROR", "FluentValidation failed", "general");
        }
    }

    private async Task ValidateBusinessRulesAsync<T>(T dto, ValidationResult<T> result, CancellationToken cancellationToken) where T : class
    {
        try
        {
            // Check if DTO implements custom validation interface
            if (dto is ICustomValidatable customValidatable)
            {
                var businessValidationResult = await customValidatable.ValidateAsync(cancellationToken);
                if (!businessValidationResult.IsValid)
                {
                    foreach (var error in businessValidationResult.Errors)
                    {
                        result.AddError(error.Code ?? "BUSINESS_RULE_ERROR", error.Message ?? "Business rule validation failed", error.Field ?? "general");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during business rules validation");
            result.AddError("BUSINESS_RULE_ERROR", "Business rules validation failed", "general");
        }
    }
}

/// <summary>
/// Validation result for DTOs
/// </summary>
public class ValidationResult<T> where T : class
{
    public ValidationResult(T dto)
    {
        Data = dto;
        Errors = new List<ValidationError>();
    }

    public T Data { get; }
    public List<ValidationError> Errors { get; }
    public bool IsValid => !Errors.Any();

    public void AddError(string code, string message, string field)
    {
        Errors.Add(new ValidationError
        {
            Code = code,
            Message = message,
            Field = field
        });
    }

    public static ValidationResult<T> Success(T dto)
    {
        return new ValidationResult<T>(dto);
    }

    public static ValidationResult<T> Failure(string message, string code, string field = "general")
    {
        var result = new ValidationResult<T>(default!);
        result.AddError(code, message, field);
        return result;
    }

    public BaseResponseDto ToErrorResponse()
    {
        return new BaseResponseDto
        {
            Success = false,
            ErrorCode = Errors.FirstOrDefault()?.Code ?? "VALIDATION_ERROR",
            Message = "Validation failed",
            Details = Errors.Select(e => new ErrorDetailDto
            {
                Field = e.Field,
                Code = e.Code,
                Message = e.Message
            }).ToList(),
            Timestamp = DateTime.UtcNow
        };
    }
}

/// <summary>
/// Validation error information
/// </summary>
public class ValidationError
{
    public string? Code { get; set; }
    public string? Message { get; set; }
    public string? Field { get; set; }
    public Dictionary<string, object>? Context { get; set; }
}

/// <summary>
/// Interface for DTOs that need custom validation logic
/// </summary>
public interface ICustomValidatable
{
    Task<CustomValidationResult> ValidateAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Custom validation result
/// </summary>
public class CustomValidationResult
{
    public CustomValidationResult()
    {
        Errors = new List<ValidationError>();
    }

    public List<ValidationError> Errors { get; }
    public bool IsValid => !Errors.Any();

    public void AddError(string code, string message, string field = "general")
    {
        Errors.Add(new ValidationError
        {
            Code = code,
            Message = message,
            Field = field
        });
    }

    public static CustomValidationResult Success()
    {
        return new CustomValidationResult();
    }

    public static CustomValidationResult Failure(string message, string code, string field = "general")
    {
        var result = new CustomValidationResult();
        result.AddError(code, message, field);
        return result;
    }
}

/// <summary>
/// Validation context for passing additional information
/// </summary>
public class ValidationContext<T> where T : class
{
    public ValidationContext(T dto, string group = "default")
    {
        Data = dto;
        Group = group;
        Properties = new Dictionary<string, object>();
    }

    public T Data { get; }
    public string Group { get; }
    public Dictionary<string, object> Properties { get; }

    public void SetProperty(string key, object value)
    {
        Properties[key] = value;
    }

    public TValue? GetProperty<TValue>(string key)
    {
        if (Properties.TryGetValue(key, out var value) && value is TValue typedValue)
        {
            return typedValue;
        }
        return default;
    }
}