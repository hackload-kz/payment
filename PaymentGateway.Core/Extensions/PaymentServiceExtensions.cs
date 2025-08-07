using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PaymentGateway.Core.Configuration;
using PaymentGateway.Core.Services;

namespace PaymentGateway.Core.Extensions;

/// <summary>
/// Extension methods for registering payment services in dependency injection container
/// </summary>
public static class PaymentServiceExtensions
{
    /// <summary>
    /// Registers all core payment services required for payment processing
    /// </summary>
    /// <param name="services">The service collection to add services to</param>
    /// <param name="configuration">The configuration instance</param>
    /// <returns>The service collection for chaining</returns>
    public static IServiceCollection AddPaymentServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Register configuration options
        services.Configure<ApiOptions>(configuration.GetSection(ApiOptions.SectionName));
        
        // Register core payment processing services
        services.AddScoped<IBusinessRuleEngineService, BusinessRuleEngineService>();
        services.AddScoped<ICardPaymentProcessingService, CardPaymentProcessingService>();
        services.AddScoped<IComprehensiveAuditService, ComprehensiveAuditService>();
        
        // Register payment authentication and security services
        services.AddScoped<IPaymentAuthenticationService, PaymentAuthenticationService>();
        
        // Register payment lifecycle services
        services.AddScoped<IPaymentInitializationService, PaymentInitializationService>();
        services.AddScoped<IPaymentConfirmationService, PaymentConfirmationService>();
        services.AddScoped<IPaymentCancellationService, PaymentCancellationService>();
        services.AddScoped<IPaymentStatusCheckService, PaymentStatusCheckService>();

        // Register team management services
        services.AddScoped<ITeamRegistrationService, TeamRegistrationService>();

        return services;
    }

    /// <summary>
    /// Registers payment services with default configuration
    /// </summary>
    /// <param name="services">The service collection to add services to</param>
    /// <returns>The service collection for chaining</returns>
    public static IServiceCollection AddPaymentServicesDefaults(this IServiceCollection services)
    {
        // Register with scoped lifetime for request-scoped behavior
        services.AddScoped<IBusinessRuleEngineService, BusinessRuleEngineService>();
        services.AddScoped<ICardPaymentProcessingService, CardPaymentProcessingService>();
        services.AddScoped<IComprehensiveAuditService, ComprehensiveAuditService>();
        services.AddScoped<IPaymentAuthenticationService, PaymentAuthenticationService>();
        services.AddScoped<IPaymentInitializationService, PaymentInitializationService>();
        services.AddScoped<IPaymentConfirmationService, PaymentConfirmationService>();
        services.AddScoped<IPaymentCancellationService, PaymentCancellationService>();
        services.AddScoped<IPaymentStatusCheckService, PaymentStatusCheckService>();
        services.AddScoped<ITeamRegistrationService, TeamRegistrationService>();

        return services;
    }
}