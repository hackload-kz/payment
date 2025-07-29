using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PaymentGateway.Core.Services;
using System.Diagnostics;

namespace PaymentGateway.Infrastructure.Services;

public interface IHealthCheckMetricsService
{
    Task RecordHealthCheckAsync(string name, HealthCheckResult result, TimeSpan duration);
    Task RecordOverallHealthAsync(HealthStatus status, TimeSpan duration);
}

public class HealthCheckMetricsService : IHealthCheckMetricsService
{
    private readonly IPrometheusMetricsService _metricsService;
    private readonly ILogger<HealthCheckMetricsService> _logger;

    public HealthCheckMetricsService(
        IPrometheusMetricsService metricsService,
        ILogger<HealthCheckMetricsService> logger)
    {
        _metricsService = metricsService;
        _logger = logger;
    }

    public async Task RecordHealthCheckAsync(string name, HealthCheckResult result, TimeSpan duration)
    {
        var isHealthy = result.Status == HealthStatus.Healthy;
        var durationMs = duration.TotalMilliseconds;

        _metricsService.RecordHealthCheckDuration(name, durationMs, isHealthy);
        _metricsService.SetHealthCheckStatus(name, isHealthy);

        if (result.Status != HealthStatus.Healthy)
        {
            _logger.LogWarning("Health check {HealthCheckName} failed with status {Status}. Duration: {Duration}ms. Description: {Description}",
                name, result.Status, durationMs, result.Description);
        }

        await Task.CompletedTask;
    }

    public async Task RecordOverallHealthAsync(HealthStatus status, TimeSpan duration)
    {
        var isHealthy = status == HealthStatus.Healthy;
        var durationMs = duration.TotalMilliseconds;

        _metricsService.RecordHealthCheckDuration("overall", durationMs, isHealthy);
        _metricsService.SetHealthCheckStatus("overall", isHealthy);

        _logger.LogInformation("Overall health check completed with status {Status}. Duration: {Duration}ms",
            status, durationMs);

        await Task.CompletedTask;
    }
}

// Note: HealthCheckMetricsBackgroundService will be implemented in the API project 
// where IHealthCheckService is available