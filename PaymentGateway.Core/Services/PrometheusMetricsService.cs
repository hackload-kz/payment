using Prometheus;

namespace PaymentGateway.Core.Services;

public interface IPrometheusMetricsService
{
    // Payment processing metrics
    void IncrementPaymentSuccess(string teamSlug, string paymentMethod);
    void IncrementPaymentFailure(string teamSlug, string paymentMethod, string errorCode);
    void RecordPaymentProcessingTime(double milliseconds, string teamSlug, string paymentMethod);
    void IncrementConcurrentPayments();
    void DecrementConcurrentPayments();
    
    // Database metrics
    void RecordDatabaseConnectionTime(double milliseconds, string operation);
    void IncrementDatabaseErrors(string operation, string errorType);
    void IncrementDatabaseOperations(string operation);
    
    // API metrics
    void RecordApiRequestDuration(double milliseconds, string endpoint, string method, int statusCode);
    void IncrementApiRequests(string endpoint, string method, int statusCode);
    void IncrementApiErrors(string endpoint, string method, int statusCode, string errorCode);
    
    // Authentication metrics
    void IncrementAuthenticationAttempts(string teamSlug, bool success);
    void IncrementAuthenticationFailures(string teamSlug, string reason);
    
    // Business metrics
    void RecordTransactionAmount(decimal amount, string currency, string teamSlug);
    void IncrementTransactionCount(string status, string teamSlug, string paymentMethod);
    
    // System metrics
    void IncrementBackgroundTaskExecutions(string taskName, bool success);
    void RecordBackgroundTaskDuration(string taskName, double milliseconds);
    
    // Health check metrics
    void RecordHealthCheckDuration(string healthCheckName, double milliseconds, bool healthy);
    void SetHealthCheckStatus(string healthCheckName, bool healthy);
}

public class PrometheusMetricsService : IPrometheusMetricsService
{
    // Payment processing metrics
    private static readonly Counter PaymentSuccessTotal = Metrics
        .CreateCounter("payment_gateway_payments_success_total", 
            "Total number of successful payments", 
            new[] { "team_slug", "payment_method" });

    private static readonly Counter PaymentFailureTotal = Metrics
        .CreateCounter("payment_gateway_payments_failure_total", 
            "Total number of failed payments", 
            new[] { "team_slug", "payment_method", "error_code" });

    private static readonly Histogram PaymentProcessingDuration = Metrics
        .CreateHistogram("payment_gateway_payment_processing_duration_seconds", 
            "Time spent processing payments",
            new[] { "team_slug", "payment_method" });

    private static readonly Gauge ConcurrentPayments = Metrics
        .CreateGauge("payment_gateway_concurrent_payments", 
            "Current number of payments being processed");

    // Database metrics
    private static readonly Histogram DatabaseConnectionDuration = Metrics
        .CreateHistogram("payment_gateway_database_connection_duration_seconds", 
            "Time spent on database connections",
            new[] { "operation" });

    private static readonly Counter DatabaseErrorsTotal = Metrics
        .CreateCounter("payment_gateway_database_errors_total", 
            "Total number of database errors", 
            new[] { "operation", "error_type" });

    private static readonly Counter DatabaseOperationsTotal = Metrics
        .CreateCounter("payment_gateway_database_operations_total", 
            "Total number of database operations", 
            new[] { "operation" });

    // API metrics
    private static readonly Histogram ApiRequestDuration = Metrics
        .CreateHistogram("payment_gateway_api_request_duration_seconds", 
            "Duration of API requests",
            new[] { "endpoint", "method", "status_code" });

    private static readonly Counter ApiRequestsTotal = Metrics
        .CreateCounter("payment_gateway_api_requests_total", 
            "Total number of API requests", 
            new[] { "endpoint", "method", "status_code" });

    private static readonly Counter ApiErrorsTotal = Metrics
        .CreateCounter("payment_gateway_api_errors_total", 
            "Total number of API errors", 
            new[] { "endpoint", "method", "status_code", "error_code" });

    // Authentication metrics
    private static readonly Counter AuthenticationAttemptsTotal = Metrics
        .CreateCounter("payment_gateway_authentication_attempts_total", 
            "Total number of authentication attempts", 
            new[] { "team_slug", "success" });

    private static readonly Counter AuthenticationFailuresTotal = Metrics
        .CreateCounter("payment_gateway_authentication_failures_total", 
            "Total number of authentication failures", 
            new[] { "team_slug", "reason" });

    // Business metrics
    private static readonly Histogram TransactionAmounts = Metrics
        .CreateHistogram("payment_gateway_transaction_amounts", 
            "Distribution of transaction amounts",
            new[] { "currency", "team_slug" },
            new HistogramConfiguration
            {
                Buckets = new[] { 1.0, 5.0, 10.0, 25.0, 50.0, 100.0, 250.0, 500.0, 1000.0, 2500.0, 5000.0, 10000.0 }
            });

    private static readonly Counter TransactionCountTotal = Metrics
        .CreateCounter("payment_gateway_transactions_total", 
            "Total number of transactions", 
            new[] { "status", "team_slug", "payment_method" });

    // System metrics
    private static readonly Counter BackgroundTaskExecutionsTotal = Metrics
        .CreateCounter("payment_gateway_background_tasks_total", 
            "Total number of background task executions", 
            new[] { "task_name", "success" });

    private static readonly Histogram BackgroundTaskDuration = Metrics
        .CreateHistogram("payment_gateway_background_task_duration_seconds", 
            "Duration of background task executions",
            new[] { "task_name" });

    // Health check metrics
    private static readonly Histogram HealthCheckDuration = Metrics
        .CreateHistogram("payment_gateway_health_check_duration_seconds", 
            "Duration of health checks",
            new[] { "health_check_name", "healthy" });

    private static readonly Gauge HealthCheckStatus = Metrics
        .CreateGauge("payment_gateway_health_check_status", 
            "Status of health checks (1 = healthy, 0 = unhealthy)",
            new[] { "health_check_name" });

    // Payment processing metrics implementation
    public void IncrementPaymentSuccess(string teamSlug, string paymentMethod)
    {
        PaymentSuccessTotal.WithLabels(teamSlug, paymentMethod).Inc();
    }

    public void IncrementPaymentFailure(string teamSlug, string paymentMethod, string errorCode)
    {
        PaymentFailureTotal.WithLabels(teamSlug, paymentMethod, errorCode).Inc();
    }

    public void RecordPaymentProcessingTime(double milliseconds, string teamSlug, string paymentMethod)
    {
        PaymentProcessingDuration.WithLabels(teamSlug, paymentMethod).Observe(milliseconds / 1000.0);
    }

    public void IncrementConcurrentPayments()
    {
        ConcurrentPayments.Inc();
    }

    public void DecrementConcurrentPayments()
    {
        ConcurrentPayments.Dec();
    }

    // Database metrics implementation
    public void RecordDatabaseConnectionTime(double milliseconds, string operation)
    {
        DatabaseConnectionDuration.WithLabels(operation).Observe(milliseconds / 1000.0);
    }

    public void IncrementDatabaseErrors(string operation, string errorType)
    {
        DatabaseErrorsTotal.WithLabels(operation, errorType).Inc();
    }

    public void IncrementDatabaseOperations(string operation)
    {
        DatabaseOperationsTotal.WithLabels(operation).Inc();
    }

    // API metrics implementation
    public void RecordApiRequestDuration(double milliseconds, string endpoint, string method, int statusCode)
    {
        ApiRequestDuration.WithLabels(endpoint, method, statusCode.ToString()).Observe(milliseconds / 1000.0);
    }

    public void IncrementApiRequests(string endpoint, string method, int statusCode)
    {
        ApiRequestsTotal.WithLabels(endpoint, method, statusCode.ToString()).Inc();
    }

    public void IncrementApiErrors(string endpoint, string method, int statusCode, string errorCode)
    {
        ApiErrorsTotal.WithLabels(endpoint, method, statusCode.ToString(), errorCode).Inc();
    }

    // Authentication metrics implementation
    public void IncrementAuthenticationAttempts(string teamSlug, bool success)
    {
        AuthenticationAttemptsTotal.WithLabels(teamSlug, success.ToString().ToLower()).Inc();
    }

    public void IncrementAuthenticationFailures(string teamSlug, string reason)
    {
        AuthenticationFailuresTotal.WithLabels(teamSlug, reason).Inc();
    }

    // Business metrics implementation
    public void RecordTransactionAmount(decimal amount, string currency, string teamSlug)
    {
        TransactionAmounts.WithLabels(currency, teamSlug).Observe((double)amount);
    }

    public void IncrementTransactionCount(string status, string teamSlug, string paymentMethod)
    {
        TransactionCountTotal.WithLabels(status, teamSlug, paymentMethod).Inc();
    }

    // System metrics implementation
    public void IncrementBackgroundTaskExecutions(string taskName, bool success)
    {
        BackgroundTaskExecutionsTotal.WithLabels(taskName, success.ToString().ToLower()).Inc();
    }

    public void RecordBackgroundTaskDuration(string taskName, double milliseconds)
    {
        BackgroundTaskDuration.WithLabels(taskName).Observe(milliseconds / 1000.0);
    }

    // Health check metrics implementation
    public void RecordHealthCheckDuration(string healthCheckName, double milliseconds, bool healthy)
    {
        HealthCheckDuration.WithLabels(healthCheckName, healthy.ToString().ToLower()).Observe(milliseconds / 1000.0);
    }

    public void SetHealthCheckStatus(string healthCheckName, bool healthy)
    {
        HealthCheckStatus.WithLabels(healthCheckName).Set(healthy ? 1 : 0);
    }
}