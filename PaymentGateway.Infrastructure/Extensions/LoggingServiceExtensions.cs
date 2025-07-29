using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PaymentGateway.Core.Configuration;
using PaymentGateway.Core.Services;
using PaymentGateway.Infrastructure.Logging;
using PaymentGateway.Infrastructure.Services;
using Serilog;

namespace PaymentGateway.Infrastructure.Extensions;

public static class LoggingServiceExtensions
{
    public static IServiceCollection AddLoggingServices(this IServiceCollection services, IConfiguration configuration)
    {
        // Configure logging configuration models
        var loggingConfig = configuration.GetSection(LoggingConfiguration.SectionName).Get<LoggingConfiguration>()
                           ?? new LoggingConfiguration();

        services.AddSingleton(loggingConfig);
        services.AddSingleton(loggingConfig.Serilog);
        services.AddSingleton(loggingConfig.Audit);
        services.AddSingleton(loggingConfig.Retention);
        
        // Configure audit configuration
        var auditConfig = configuration.GetSection("Audit").Get<AuditConfiguration>() ?? new AuditConfiguration();
        services.AddSingleton(auditConfig);

        // Register core logging services
        services.AddSingleton<ICorrelationIdService, CorrelationIdService>();
        services.AddSingleton<ISensitiveDataMaskingService, SensitiveDataMaskingService>();
        services.AddScoped<IAuditLoggingService, AuditLoggingService>();
        services.AddSingleton<ILoggingMetricsService, LoggingMetricsService>();

        // Register log retention services (singleton for background service compatibility)
        services.AddSingleton<ILogRetentionService, LogRetentionService>();
        services.AddHostedService<LogRetentionBackgroundService>();

        return services;
    }

    public static IHostBuilder UseSerilog(this IHostBuilder hostBuilder)
    {
        return hostBuilder.UseSerilog((context, services, configuration) =>
        {
            // Create Serilog logger with all configured sinks and enrichers
            var logger = SerilogConfigurationService.CreateLogger(context.Configuration, services);
            configuration.WriteTo.Logger(logger);
        });
    }

    public static IServiceCollection AddSerilogLogger(this IServiceCollection services, IConfiguration configuration, IServiceProvider serviceProvider)
    {
        // Create and configure Serilog logger
        var logger = SerilogConfigurationService.CreateLogger(configuration, serviceProvider);
        
        // Replace the default logger with Serilog
        services.AddSingleton<Serilog.ILogger>(logger);
        services.AddLogging(loggingBuilder =>
        {
            loggingBuilder.ClearProviders();
            loggingBuilder.AddSerilog(logger, dispose: true);
        });

        return services;
    }

}