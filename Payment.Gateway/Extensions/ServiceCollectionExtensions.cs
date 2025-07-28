using Payment.Gateway.Services;

namespace Payment.Gateway.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddPaymentGatewayServices(this IServiceCollection services)
    {
        // Register business services
        services.AddScoped<ITokenGenerationService, TokenGenerationService>();
        services.AddScoped<IMerchantService, MerchantService>();
        services.AddScoped<IPaymentService, PaymentService>();
        services.AddScoped<IPaymentStateMachine, PaymentStateMachine>();

        // Register repositories
        services.AddScoped<IPaymentRepository, PaymentRepository>();
        services.AddScoped<IMerchantRepository, MerchantRepository>();

        return services;
    }
}