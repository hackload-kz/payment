using PaymentGateway.API.Middleware;
using PaymentGateway.API.Filters;
using PaymentGateway.API.Configuration;
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


    public static IServiceCollection AddVersionedSwagger(this IServiceCollection services, IConfiguration configuration)
    {
        var swaggerOptions = configuration.GetSection(SwaggerOptions.SectionName).Get<SwaggerOptions>() ?? new SwaggerOptions();
        
        services.Configure<SwaggerOptions>(configuration.GetSection(SwaggerOptions.SectionName));
        
        services.AddSwaggerGen(options =>
        {
            options.SwaggerDoc("v1", new OpenApiInfo
            {
                Title = swaggerOptions.Title,
                Version = "v1",
                Description = swaggerOptions.Description,                
                License = new OpenApiLicense
                {
                    Name = "MIT License",
                    Url = new Uri("https://opensource.org/licenses/MIT")
                }
            });

            // Add security definitions for both Bearer tokens and API keys
            options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
            {
                Description = "JWT Authorization header using the Bearer scheme (for admin endpoints)",
                Name = "Authorization",
                In = ParameterLocation.Header,
                Type = SecuritySchemeType.ApiKey,
                Scheme = "Bearer",
                BearerFormat = "JWT"
            });

            options.AddSecurityDefinition("AdminToken", new OpenApiSecurityScheme
            {
                Description = "Admin token header (alternative to Bearer token)",
                Name = "X-Admin-Token",
                In = ParameterLocation.Header,
                Type = SecuritySchemeType.ApiKey
            });

            options.AddSecurityDefinition("PaymentAuth", new OpenApiSecurityScheme
            {
                Description = "Payment authentication using TeamSlug and SHA-256 Token in request body",
                Name = "Payment Authentication",
                Type = SecuritySchemeType.ApiKey,
                In = ParameterLocation.Query
            });

            // Add security requirements
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

            // Enable XML comments if available
            var xmlFile = $"{System.Reflection.Assembly.GetExecutingAssembly().GetName().Name}.xml";
            var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
            if (File.Exists(xmlPath))
            {
                options.IncludeXmlComments(xmlPath, includeControllerXmlComments: true);
            }

            // Configure basic options
            options.IgnoreObsoleteActions();
            options.IgnoreObsoleteProperties();
        });

        return services;
    }
}