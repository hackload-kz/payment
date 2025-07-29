// SPDX-License-Identifier: MIT
// Copyright (c) 2025 HackLoad Payment Gateway

namespace PaymentGateway.API.Configuration;

/// <summary>
/// CORS configuration for payment gateway web clients
/// 
/// Provides secure CORS configuration for payment gateway APIs with support for:
/// - Development and production environments
/// - Configurable allowed origins, methods, and headers
/// - Credentials support for authenticated requests
/// - Security-focused default settings
/// - Environment-specific policy management
/// </summary>
public static class CorsConfiguration
{
    public const string PaymentGatewayPolicy = "PaymentGatewayPolicy";
    public const string DevelopmentPolicy = "DevelopmentPolicy";

    /// <summary>
    /// Configure CORS services and policies
    /// </summary>
    public static IServiceCollection AddPaymentGatewayCors(this IServiceCollection services, IConfiguration configuration, IWebHostEnvironment environment)
    {
        var corsOptions = configuration.GetSection("Cors").Get<CorsOptions>() ?? new CorsOptions();

        services.AddCors(options =>
        {
            // Production/Staging policy - strict CORS settings
            options.AddPolicy(PaymentGatewayPolicy, policy =>
            {
                // Configure allowed origins
                if (corsOptions.AllowedOrigins != null && corsOptions.AllowedOrigins.Any())
                {
                    policy.WithOrigins(corsOptions.AllowedOrigins);
                }
                else
                {
                    // Default secure origins for production
                    policy.WithOrigins(
                        "https://payment.hackload.com",
                        "https://checkout.hackload.com",
                        "https://api.hackload.com"
                    );
                }

                // Configure allowed methods - only essential payment methods
                var allowedMethods = corsOptions.AllowedMethods ?? new[]
                {
                    "GET", "POST", "OPTIONS"
                };
                policy.WithMethods(allowedMethods);

                // Configure allowed headers
                var allowedHeaders = corsOptions.AllowedHeaders ?? new[]
                {
                    "Accept",
                    "Content-Type",
                    "Authorization",
                    "X-Requested-With",
                    "X-API-Key",
                    "X-Correlation-ID",
                    "X-Request-ID"
                };
                policy.WithHeaders(allowedHeaders);

                // Configure exposed headers for client access
                var exposedHeaders = corsOptions.ExposedHeaders ?? new[]
                {
                    "X-Correlation-ID",
                    "X-Request-ID",
                    "X-Rate-Limit-Remaining",
                    "X-Rate-Limit-Reset"
                };
                policy.WithExposedHeaders(exposedHeaders);

                // Allow credentials for authenticated requests
                if (corsOptions.AllowCredentials)
                {
                    policy.AllowCredentials();
                }

                // Cache preflight requests for performance
                policy.SetPreflightMaxAge(TimeSpan.FromMinutes(corsOptions.PreflightMaxAgeMinutes));
            });

            // Development policy - more permissive for local development
            if (environment.IsDevelopment())
            {
                options.AddPolicy(DevelopmentPolicy, policy =>
                {
                    policy.AllowAnyOrigin()
                          .AllowAnyMethod()
                          .AllowAnyHeader();
                });
            }
        });

        return services;
    }

    /// <summary>
    /// Apply CORS middleware with environment-specific policies
    /// </summary>
    public static IApplicationBuilder UsePaymentGatewayCors(this IApplicationBuilder app, IWebHostEnvironment environment)
    {
        if (environment.IsDevelopment())
        {
            app.UseCors(DevelopmentPolicy);
        }
        else
        {
            app.UseCors(PaymentGatewayPolicy);
        }

        return app;
    }
}

/// <summary>
/// CORS configuration options
/// </summary>
public class CorsOptions
{
    /// <summary>
    /// Allowed origins for CORS requests
    /// </summary>
    public string[]? AllowedOrigins { get; set; }

    /// <summary>
    /// Allowed HTTP methods
    /// </summary>
    public string[]? AllowedMethods { get; set; }

    /// <summary>
    /// Allowed request headers
    /// </summary>
    public string[]? AllowedHeaders { get; set; }

    /// <summary>
    /// Headers exposed to the client
    /// </summary>
    public string[]? ExposedHeaders { get; set; }

    /// <summary>
    /// Whether to allow credentials in CORS requests
    /// </summary>
    public bool AllowCredentials { get; set; } = true;

    /// <summary>
    /// Preflight cache duration in minutes
    /// </summary>
    public int PreflightMaxAgeMinutes { get; set; } = 10;

    /// <summary>
    /// Environment-specific configurations
    /// </summary>
    public Dictionary<string, EnvironmentCorsSettings>? Environments { get; set; }
}

/// <summary>
/// Environment-specific CORS settings
/// </summary>
public class EnvironmentCorsSettings
{
    public string[]? AllowedOrigins { get; set; }
    public bool AllowCredentials { get; set; } = true;
    public int PreflightMaxAgeMinutes { get; set; } = 10;
}