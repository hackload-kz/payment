using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using PaymentGateway.Core.DTOs.Validation;
using PaymentGateway.Core.DTOs.PaymentInit;
using PaymentGateway.Core.DTOs.PaymentConfirm;
using PaymentGateway.Core.DTOs.PaymentCancel;
using PaymentGateway.Core.DTOs.PaymentCheck;
using PaymentGateway.Core.Validation.Async;
using PaymentGateway.Core.Validation.Localization;

namespace PaymentGateway.Core.Validation.Extensions;

/// <summary>
/// Service collection extensions for validation services
/// </summary>
public static class ValidationServiceExtensions
{
    /// <summary>
    /// Add validation services to the service collection
    /// </summary>
    public static IServiceCollection AddValidationServices(this IServiceCollection services)
    {
        // Register FluentValidation validators
        services.AddScoped<IValidator<PaymentInitRequestDto>, PaymentInitRequestValidator>();
        services.AddScoped<IValidator<PaymentConfirmRequestDto>, PaymentConfirmRequestValidator>();
        services.AddScoped<IValidator<PaymentCancelRequestDto>, PaymentCancelRequestValidator>();
        services.AddScoped<IValidator<PaymentCheckRequestDto>, PaymentCheckRequestValidator>();
        services.AddScoped<IValidator<OrderItemDto>, OrderItemValidator>();
        services.AddScoped<IValidator<ReceiptDto>, ReceiptValidator>();

        // Register validation services
        services.AddScoped<ISimplifiedAsyncValidationService, SimplifiedAsyncValidationService>();
        services.AddScoped<IValidationMessageLocalizer, ValidationMessageLocalizer>();

        return services;
    }

    /// <summary>
    /// Add FluentValidation to the service collection with custom configuration
    /// </summary>
    public static IServiceCollection AddFluentValidationServices(this IServiceCollection services)
    {
        services.AddValidatorsFromAssemblyContaining<PaymentInitRequestValidator>();
        
        return services;
    }
}