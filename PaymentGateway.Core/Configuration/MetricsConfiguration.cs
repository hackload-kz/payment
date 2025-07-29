namespace PaymentGateway.Core.Configuration;

public class MetricsConfiguration
{
    public const string SectionName = "Metrics";

    public PrometheusConfiguration Prometheus { get; set; } = new();
    public DashboardConfiguration Dashboard { get; set; } = new();
    public BusinessMetricsConfiguration Business { get; set; } = new();
}

public class PrometheusConfiguration
{
    public bool Enabled { get; set; } = true;
    public string MetricsPath { get; set; } = "/metrics";
    public int Port { get; set; } = 8081;
    public string Host { get; set; } = "*";
    public bool EnableDebugMetrics { get; set; } = false;
    public int ScrapeIntervalSeconds { get; set; } = 15;
    public Dictionary<string, string> Labels { get; set; } = new();
}

public class DashboardConfiguration
{
    public bool Enabled { get; set; } = true;
    public string DashboardPath { get; set; } = "/metrics-dashboard";
    public bool ShowDetailedMetrics { get; set; } = true;
    public bool ShowBusinessMetrics { get; set; } = true;
    public bool ShowSystemMetrics { get; set; } = true;
    public int RefreshIntervalSeconds { get; set; } = 30;
}

public class BusinessMetricsConfiguration
{
    public bool EnableTransactionMetrics { get; set; } = true;
    public bool EnableRevenueMetrics { get; set; } = true;
    public bool EnableCustomerMetrics { get; set; } = true;
    public bool EnablePaymentMethodMetrics { get; set; } = true;
    public bool EnableTeamMetrics { get; set; } = true;
    public List<string> ExcludedTeams { get; set; } = new();
    public Dictionary<string, decimal> CurrencyConversionRates { get; set; } = new()
    {
        { "USD", 1.0m },
        { "EUR", 0.85m },
        { "RUB", 90.0m }
    };
}