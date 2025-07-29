using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using PaymentGateway.Core.Services;

namespace PaymentGateway.Core.Extensions;

public static class ConcurrencyServiceExtensions
{
    public static IServiceCollection AddConcurrencyServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Configure options
        services.Configure<DistributedLockOptions>(
            configuration.GetSection("Concurrency:DistributedLock"));
        services.Configure<ConcurrentPaymentProcessingOptions>(
            configuration.GetSection("Concurrency:PaymentProcessing"));
        services.Configure<PaymentQueueOptions>(
            configuration.GetSection("Concurrency:PaymentQueue"));
        services.Configure<DeadlockDetectionOptions>(
            configuration.GetSection("Concurrency:DeadlockDetection"));
        services.Configure<RateLimitingOptions>(
            configuration.GetSection("Concurrency:RateLimit"));
        services.Configure<ConcurrencyMetricsOptions>(
            configuration.GetSection("Concurrency:Metrics"));

        // Register core services
        services.AddSingleton<IPaymentStateManager, PaymentStateManager>();
        services.AddSingleton<IDistributedLockService, InMemoryDistributedLockService>();
        services.AddScoped<IConcurrentPaymentProcessingService, ConcurrentPaymentProcessingService>();
        
        // Register queue services
        services.AddSingleton<IPaymentQueueService, PaymentQueueService>();
        services.AddHostedService<PaymentQueueBackgroundService>();
        
        // Register deadlock detection
        services.AddSingleton<IDeadlockDetectionService, DeadlockDetectionService>();
        services.AddHostedService<DeadlockDetectionBackgroundService>();
        
        // Register rate limiting
        services.AddSingleton<IRateLimitingService, RateLimitingService>();
        
        // Register metrics and monitoring
        services.AddSingleton<IConcurrencyMetricsService, ConcurrencyMetricsService>();
        services.AddHostedService<ConcurrencyMonitoringBackgroundService>();

        return services;
    }

    public static IServiceCollection AddConcurrencyDefaults(this IServiceCollection services)
    {
        // Add services with default configuration
        services.AddSingleton<IPaymentStateManager, PaymentStateManager>();
        services.AddSingleton<IDistributedLockService, InMemoryDistributedLockService>();
        services.AddScoped<IConcurrentPaymentProcessingService, ConcurrentPaymentProcessingService>();
        services.AddSingleton<IPaymentQueueService, PaymentQueueService>();
        services.AddHostedService<PaymentQueueBackgroundService>();
        services.AddSingleton<IDeadlockDetectionService, DeadlockDetectionService>();
        services.AddHostedService<DeadlockDetectionBackgroundService>();
        services.AddSingleton<IRateLimitingService, RateLimitingService>();
        services.AddSingleton<IConcurrencyMetricsService, ConcurrencyMetricsService>();
        services.AddHostedService<ConcurrencyMonitoringBackgroundService>();

        // Configure default options
        services.Configure<DistributedLockOptions>(options =>
        {
            options.DefaultTimeout = TimeSpan.FromSeconds(30);
            options.DefaultExpiry = TimeSpan.FromMinutes(5);
            options.MaxRetryAttempts = 3;
            options.RetryDelay = TimeSpan.FromMilliseconds(100);
        });

        services.Configure<ConcurrentPaymentProcessingOptions>(options =>
        {
            options.LockTimeout = TimeSpan.FromSeconds(30);
            options.ProcessingTimeout = TimeSpan.FromMinutes(2);
            options.MaxConcurrentOperations = 100;
        });

        services.Configure<PaymentQueueOptions>(options =>
        {
            options.MaxQueueSize = 10000;
            options.MaxConcurrentProcessing = 50;
            options.ProcessingTimeout = TimeSpan.FromMinutes(5);
            options.RetryAttempts = 3;
            options.RetryDelay = TimeSpan.FromSeconds(30);
        });

        services.Configure<DeadlockDetectionOptions>(options =>
        {
            options.DetectionInterval = TimeSpan.FromSeconds(30);
            options.MaxLockWaitTime = TimeSpan.FromMinutes(2);
            options.EnableAutomaticResolution = true;
            options.MaxDeadlockHistory = 100;
        });

        services.Configure<RateLimitingOptions>(options =>
        {
            options.Policies = new Dictionary<string, RateLimitPolicy>
            {
                { "DefaultAPI", RateLimitingService.DefaultApiPolicy },
                { "PaymentInit", RateLimitingService.PaymentInitPolicy },
                { "PaymentProcessing", RateLimitingService.PaymentProcessingPolicy }
            };
            options.EnableMetrics = true;
            options.CleanupInterval = TimeSpan.FromMinutes(5);
        });

        services.Configure<ConcurrencyMetricsOptions>(options =>
        {
            options.ReportingInterval = TimeSpan.FromMinutes(1);
            options.EnableDetailedMetrics = true;
            options.MetricsRetentionHours = 24;
        });

        return services;
    }
}