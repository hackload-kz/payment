using PaymentGateway.API.Middleware;
using PaymentGateway.API.Filters;
using Microsoft.AspNetCore.Mvc;
using Microsoft.OpenApi.Models;

namespace PaymentGateway.API.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddGlobalExceptionHandling(this IServiceCollection services)
    {
        // Register any dependencies for global exception handling
        return services;
    }

    public static IServiceCollection AddRequestResponseLogging(this IServiceCollection services)
    {
        // Register any dependencies for request/response logging
        return services;
    }

    public static IServiceCollection AddRequestValidation(this IServiceCollection services)
    {
        // Register any dependencies for request validation
        return services;
    }

    public static IServiceCollection AddAdminAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<AdminAuthenticationOptions>(
            configuration.GetSection(AdminAuthenticationOptions.SectionName));
        
        return services;
    }

    public static IServiceCollection AddPaymentAuthentication(this IServiceCollection services)
    {
        // Register PaymentAuthenticationFilter as a service for ServiceFilter usage
        services.AddScoped<PaymentAuthenticationFilter>();
        
        return services;
    }


    public static IServiceCollection AddVersionedSwagger(this IServiceCollection services)
    {
        services.AddSwaggerGen(options =>
        {
            options.SwaggerDoc("v1", new OpenApiInfo
            {
                Title = "Payment Gateway API",
                Version = "v1",
                Description = "Payment Gateway API for processing payments"
            });

            // Add security definition
            options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
            {
                Description = "JWT Authorization header using the Bearer scheme",
                Name = "Authorization",
                In = ParameterLocation.Header,
                Type = SecuritySchemeType.ApiKey,
                Scheme = "Bearer"
            });

            options.AddSecurityRequirement(new OpenApiSecurityRequirement
            {
                {
                    new OpenApiSecurityScheme
                    {
                        Reference = new OpenApiReference
                        {
                            Type = ReferenceType.SecurityScheme,
                            Id = "Bearer"
                        }
                    },
                    Array.Empty<string>()
                }
            });
        });

        return services;
    }
}