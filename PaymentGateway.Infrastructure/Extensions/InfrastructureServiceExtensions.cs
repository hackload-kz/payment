using Microsoft.Extensions.DependencyInjection;
using PaymentGateway.Core.Repositories;
using PaymentGateway.Core.Services;
using PaymentGateway.Core.Interfaces;

namespace PaymentGateway.Infrastructure.Extensions;

public static class InfrastructureServiceExtensions
{
    public static IServiceCollection AddInfrastructureServices(this IServiceCollection services)
    {
        // Register memory cache for repositories
        services.AddMemoryCache();
        
        // Register repositories
        services.AddScoped<IPaymentRepository, PaymentRepository>();
        services.AddScoped<ITeamRepository, TeamRepository>();
        services.AddScoped<ICustomerRepository, CustomerRepository>();
        services.AddScoped<ITransactionRepository, TransactionRepository>();
        services.AddScoped<IAuditRepository, AuditRepository>();
        services.AddScoped<IPaymentStateTransitionRepository, PaymentStateTransitionRepository>();
        
        // Register Unit of Work
        services.AddScoped<IUnitOfWork, UnitOfWork>();

        // Register configuration options
        services.Configure<DistributedLockOptions>(options => { });

        // Register core services
        services.AddScoped<IAuthenticationService, AuthenticationService>();
        services.AddScoped<ISensitiveDataMaskingService, SensitiveDataMaskingService>();
        
        // Register state management services
        services.AddScoped<IPaymentLifecycleManagementService, PaymentLifecycleManagementService>();
        services.AddScoped<IPaymentStateMachine, PaymentStateMachine>();
        services.AddScoped<IPaymentStateTransitionValidationService, PaymentStateTransitionValidationService>();
        services.AddScoped<IPaymentStateManager, PaymentStateManager>();
        services.AddScoped<IPaymentStateTransitionEventService, PaymentStateTransitionEventService>();
        
        // Register core payment operation services
        services.AddScoped<IPaymentConfirmationService, PaymentConfirmationService>();
        services.AddScoped<IPaymentCancellationService, PaymentCancellationService>();
        services.AddScoped<IPaymentInitializationService, PaymentInitializationService>();
        services.AddScoped<IPaymentStatusCheckService, PaymentStatusCheckService>();
        services.AddScoped<IPaymentAuthenticationService, PaymentAuthenticationService>();
        
        // Register business services
        services.AddScoped<IBusinessRuleEngineService, BusinessRuleEngineService>();
        services.AddScoped<ICardPaymentProcessingService, CardPaymentProcessingService>();
        services.AddScoped<IConcurrentPaymentProcessingService, ConcurrentPaymentProcessingService>();
        services.AddScoped<IPaymentQueueService, PaymentQueueService>();
        services.AddScoped<IRateLimitingService, RateLimitingService>();
        
        // Register security and authentication services
        services.AddScoped<ISecureFormTokenService, SecureFormTokenService>();
        services.AddScoped<ISessionSecurityService, SessionSecurityService>();
        services.AddScoped<ITokenGenerationService, TokenGenerationService>();
        services.AddScoped<ITeamPasswordManagementService, TeamPasswordManagementService>();
        
        // Register auditing and logging services
        services.AddScoped<IAuditLoggingService, AuditLoggingService>();
        services.AddScoped<IComprehensiveAuditService, ComprehensiveAuditService>();
        // Register HTTP client for webhook services
        services.AddHttpClient<INotificationWebhookService, NotificationWebhookService>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(30);
            client.DefaultRequestHeaders.Add("User-Agent", "PaymentGateway-Webhook/1.0");
        });
        
        // Register metrics and monitoring services
        services.AddSingleton<IMetricsService, MetricsService>();
        services.AddSingleton<IDistributedLockService, InMemoryDistributedLockService>();
        services.AddSingleton<IPrometheusMetricsService, PrometheusMetricsService>();
        services.AddSingleton<IConcurrencyMetricsService, ConcurrencyMetricsService>();
        services.AddSingleton<IPaymentProcessingMetricsService, PaymentProcessingMetricsService>();
        services.AddSingleton<IPaymentStateMachineMetrics, PaymentStateMachineMetrics>();
        services.AddSingleton<ICorrelationIdService, CorrelationIdService>();
        services.AddSingleton<IDeadlockDetectionService, DeadlockDetectionService>();

        return services;
    }
}