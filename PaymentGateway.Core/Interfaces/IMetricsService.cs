namespace PaymentGateway.Core.Interfaces;

public interface IMetricsService
{
    Task RecordCounterAsync(string metricName, double value = 1, Dictionary<string, string>? labels = null);
    Task RecordGaugeAsync(string metricName, double value, Dictionary<string, string>? labels = null);
    Task RecordHistogramAsync(string metricName, double value, Dictionary<string, string>? labels = null);
}

public class PrometheusMetricsAdapter : IMetricsService
{
    public async Task RecordCounterAsync(string metricName, double value = 1, Dictionary<string, string>? labels = null)
    {
        // Implementation would record counter metrics to Prometheus
        await Task.CompletedTask;
    }

    public async Task RecordGaugeAsync(string metricName, double value, Dictionary<string, string>? labels = null)
    {
        // Implementation would record gauge metrics to Prometheus
        await Task.CompletedTask;
    }

    public async Task RecordHistogramAsync(string metricName, double value, Dictionary<string, string>? labels = null)
    {
        // Implementation would record histogram metrics to Prometheus
        await Task.CompletedTask;
    }
}