using Microsoft.Extensions.DependencyInjection;
using PaymentGateway.Core.Validation.Simplified;

namespace PaymentGateway.Core.Validation.Extensions;

/// <summary>
/// Service collection extensions for simplified validation services
/// </summary>
public static class SimplifiedValidationServiceExtensions
{
    /// <summary>
    /// Add simplified validation services to the service collection
    /// </summary>
    public static IServiceCollection AddSimplifiedValidationServices(this IServiceCollection services)
    {
        // Register simplified validation services
        services.AddScoped<IValidationFramework, SimplifiedValidationFramework>();
        services.AddScoped<IValidationMessageService, SimplifiedValidationMessageService>();
        services.AddScoped<IValidationPerformanceService, SimplifiedValidationPerformanceService>();
        services.AddScoped<IValidationTestingService, SimplifiedValidationTestingService>();

        return services;
    }
}