using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using PaymentGateway.Core.Configuration;
using PaymentGateway.Core.Services;
using PaymentGateway.Infrastructure.Interceptors;
using PaymentGateway.Infrastructure.Services;

namespace PaymentGateway.Infrastructure.Extensions;

public static class PrometheusServiceExtensions
{
    public static IServiceCollection AddPrometheusMetrics(this IServiceCollection services, IConfiguration configuration)
    {
        // Configure metrics configuration
        var metricsConfig = configuration.GetSection(MetricsConfiguration.SectionName).Get<MetricsConfiguration>()
                           ?? new MetricsConfiguration();

        services.AddSingleton(metricsConfig);
        services.AddSingleton(metricsConfig.Prometheus);
        services.AddSingleton(metricsConfig.Dashboard);
        services.AddSingleton(metricsConfig.Business);

        // Register core metrics services
        services.AddSingleton<IPrometheusMetricsService, PrometheusMetricsService>();
        services.AddScoped<IHealthCheckMetricsService, HealthCheckMetricsService>();

        // Register database metrics interceptor
        services.AddScoped<DatabaseMetricsInterceptor>();

        // Note: HealthCheckMetricsBackgroundService will be registered in the API project

        return services;
    }

    public static IServiceCollection AddDatabaseMetricsInterceptor(this IServiceCollection services)
    {
        // This will be called from the database configuration extension
        services.AddScoped<DatabaseMetricsInterceptor>();
        return services;
    }

    // Note: Health check configuration will be done in the API project
}