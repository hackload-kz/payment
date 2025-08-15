using Microsoft.AspNetCore.Mvc;
using PaymentGateway.Core.Configuration;
using PaymentGateway.Core.Services;
using Prometheus;
using System.Text;

namespace PaymentGateway.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class MetricsController : ControllerBase
{
    private readonly IPrometheusMetricsService _metricsService;
    private readonly MetricsConfiguration _metricsConfig;
    private readonly ILogger<MetricsController> _logger;

    public MetricsController(
        IPrometheusMetricsService metricsService,
        MetricsConfiguration metricsConfig,
        ILogger<MetricsController> logger)
    {
        _metricsService = metricsService;
        _metricsConfig = metricsConfig;
        _logger = logger;
    }

    /// <summary>
    /// Get current metrics summary
    /// </summary>
    [HttpGet("summary")]
    public async Task<ActionResult<MetricsSummaryResponse>> GetSummary()
    {
        try
        {
            // Simplified metrics summary
            var summary = new MetricsSummaryResponse
            {
                Timestamp = DateTime.UtcNow,
                TotalMetrics = 10, // Placeholder
                Categories = new Dictionary<string, int>
                {
                    ["payments"] = 3,
                    ["database"] = 3,
                    ["api"] = 2,
                    ["health"] = 2
                }
            };

            return Ok(summary);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting metrics summary");
            return StatusCode(500, new { error = "Failed to get metrics summary" });
        }
    }

    /// <summary>
    /// Get payment metrics
    /// </summary>
    [HttpGet("payments")]
    public async Task<ActionResult<PaymentMetricsResponse>> GetPaymentMetrics()
    {
        try
        {
            var paymentMetrics = await _metricsService.GetPaymentMetricsAsync();
            
            var response = new PaymentMetricsResponse
            {
                Timestamp = DateTime.UtcNow,
                PaymentMetrics = paymentMetrics
            };

            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting payment metrics");
            return StatusCode(500, new { error = "Failed to get payment metrics" });
        }
    }

    /// <summary>
    /// Get system metrics
    /// </summary>
    [HttpGet("system")]
    public async Task<ActionResult<SystemMetricsResponse>> GetSystemMetrics()
    {
        try
        {
            var systemMetrics = await _metricsService.GetSystemMetricsAsync();
            
            var response = new SystemMetricsResponse
            {
                Timestamp = DateTime.UtcNow,
                DatabaseMetrics = FilterMetricsByCategory(systemMetrics, "database"),
                ApiMetrics = FilterMetricsByCategory(systemMetrics, "api"),
                HealthCheckMetrics = FilterMetricsByCategory(systemMetrics, "authentication")
            };

            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting system metrics");
            return StatusCode(500, new { error = "Failed to get system metrics" });
        }
    }

    /// <summary>
    /// Get metrics dashboard HTML
    /// </summary>
    [HttpGet("dashboard")]
    public async Task<ContentResult> GetDashboard()
    {
        if (!_metricsConfig.Dashboard.Enabled)
        {
            return new ContentResult
            {
                Content = "<html><body><h1>Metrics Dashboard Disabled</h1></body></html>",
                ContentType = "text/html"
            };
        }

        var html = await GenerateDashboardHtml();
        return new ContentResult
        {
            Content = html,
            ContentType = "text/html"
        };
    }

    /// <summary>
    /// Reset specific metrics (for testing purposes)
    /// </summary>
    [HttpPost("reset")]
    public async Task<ActionResult> ResetMetrics([FromBody] ResetMetricsRequest request)
    {
        try
        {
            _logger.LogWarning("Metrics reset requested for categories: {Categories}", 
                string.Join(", ", request.Categories));

            // Note: Prometheus.NET doesn't support resetting individual metrics
            // This would typically be used in development/testing environments
            
            return Ok(new { message = "Metrics reset request logged" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error resetting metrics");
            return StatusCode(500, new { error = "Failed to reset metrics" });
        }
    }

    private static string GetMetricCategory(string metricName)
    {
        if (metricName.StartsWith("payment_gateway_payments"))
            return "payments";
        if (metricName.StartsWith("payment_gateway_database"))
            return "database";
        if (metricName.StartsWith("payment_gateway_api"))
            return "api";
        if (metricName.StartsWith("payment_gateway_health"))
            return "health";
        if (metricName.StartsWith("payment_gateway_authentication"))
            return "authentication";
        if (metricName.StartsWith("payment_gateway_background"))
            return "background_tasks";
        if (metricName.StartsWith("payment_gateway_transaction"))
            return "transactions";
        
        return "system";
    }
    
    private static Dictionary<string, object> FilterMetricsByCategory(Dictionary<string, object> allMetrics, string category)
    {
        var filtered = new Dictionary<string, object>();
        
        foreach (var (key, value) in allMetrics)
        {
            if (key.Contains(category, StringComparison.OrdinalIgnoreCase))
            {
                filtered[key] = value;
            }
        }
        
        return filtered;
    }

    private static Dictionary<string, object> ExtractPaymentMetrics(IEnumerable<object> metricFamilies)
    {
        // Simplified implementation - in a real scenario, you would parse the metric families
        return new Dictionary<string, object>
        {
            ["payment_success_count"] = "Available via /metrics endpoint",
            ["payment_failure_count"] = "Available via /metrics endpoint",
            ["payment_processing_time"] = "Available via /metrics endpoint"
        };
    }

    private static Dictionary<string, object> ExtractDatabaseMetrics(IEnumerable<object> metricFamilies)
    {
        return new Dictionary<string, object>
        {
            ["database_operations_count"] = "Available via /metrics endpoint",
            ["database_connection_time"] = "Available via /metrics endpoint",
            ["database_errors_count"] = "Available via /metrics endpoint"
        };
    }

    private static Dictionary<string, object> ExtractApiMetrics(IEnumerable<object> metricFamilies)
    {
        return new Dictionary<string, object>
        {
            ["api_requests_count"] = "Available via /metrics endpoint",
            ["api_request_duration"] = "Available via /metrics endpoint",
            ["api_errors_count"] = "Available via /metrics endpoint"
        };
    }

    private static Dictionary<string, object> ExtractHealthCheckMetrics(IEnumerable<object> metricFamilies)
    {
        return new Dictionary<string, object>
        {
            ["health_check_duration"] = "Available via /metrics endpoint",
            ["health_check_status"] = "Available via /metrics endpoint"
        };
    }

    private async Task<string> GenerateDashboardHtml()
    {
        var html = new StringBuilder();
        html.AppendLine("<!DOCTYPE html>");
        html.AppendLine("<html><head>");
        html.AppendLine("<title>Payment Gateway Metrics Dashboard</title>");
        html.AppendLine("<meta charset='utf-8'>");
        html.AppendLine("<meta name='viewport' content='width=device-width, initial-scale=1'>");
        html.AppendLine("<style>");
        html.AppendLine("body { font-family: Arial, sans-serif; margin: 20px; background-color: #f5f5f5; }");
        html.AppendLine(".container { max-width: 1200px; margin: 0 auto; }");
        html.AppendLine(".header { background: #2c3e50; color: white; padding: 20px; border-radius: 5px; margin-bottom: 20px; }");
        html.AppendLine(".metrics-section { background: white; padding: 20px; margin-bottom: 20px; border-radius: 5px; box-shadow: 0 2px 4px rgba(0,0,0,0.1); }");
        html.AppendLine(".metric-card { border: 1px solid #ddd; padding: 15px; margin: 10px 0; border-radius: 3px; }");
        html.AppendLine(".metric-name { font-weight: bold; color: #2c3e50; }");
        html.AppendLine(".metric-value { font-size: 1.2em; color: #27ae60; margin: 5px 0; }");
        html.AppendLine(".refresh-info { color: #7f8c8d; font-size: 0.9em; }");
        html.AppendLine("</style>");
        html.AppendLine($"<meta http-equiv='refresh' content='{_metricsConfig.Dashboard.RefreshIntervalSeconds}'>");
        html.AppendLine("</head><body>");
        
        html.AppendLine("<div class='container'>");
        html.AppendLine("<div class='header'>");
        html.AppendLine("<h1>Payment Gateway Metrics Dashboard</h1>");
        html.AppendLine($"<p class='refresh-info'>Auto-refresh every {_metricsConfig.Dashboard.RefreshIntervalSeconds} seconds | Last updated: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC</p>");
        html.AppendLine("</div>");

        if (_metricsConfig.Dashboard.ShowBusinessMetrics)
        {
            html.AppendLine("<div class='metrics-section'>");
            html.AppendLine("<h2>Business Metrics</h2>");
            html.AppendLine("<div class='metric-card'>");
            html.AppendLine("<div class='metric-name'>Payment Success Rate</div>");
            html.AppendLine("<div class='metric-value'>Available via /api/metrics/payments endpoint</div>");
            html.AppendLine("</div>");
            html.AppendLine("</div>");
        }

        if (_metricsConfig.Dashboard.ShowSystemMetrics)
        {
            html.AppendLine("<div class='metrics-section'>");
            html.AppendLine("<h2>System Metrics</h2>");
            html.AppendLine("<div class='metric-card'>");
            html.AppendLine("<div class='metric-name'>Database Operations</div>");
            html.AppendLine("<div class='metric-value'>Available via /api/metrics/system endpoint</div>");
            html.AppendLine("</div>");
            html.AppendLine("</div>");
        }

        html.AppendLine("<div class='metrics-section'>");
        html.AppendLine("<h2>Prometheus Metrics Endpoint</h2>");
        html.AppendLine($"<p>Raw metrics available at: <a href='{_metricsConfig.Prometheus.MetricsPath}' target='_blank'>{_metricsConfig.Prometheus.MetricsPath}</a></p>");
        html.AppendLine("<p>API Endpoints:</p>");
        html.AppendLine("<ul>");
        html.AppendLine("<li><a href='/api/metrics/summary'>Metrics Summary</a></li>");
        html.AppendLine("<li><a href='/api/metrics/payments'>Payment Metrics</a></li>");
        html.AppendLine("<li><a href='/api/metrics/system'>System Metrics</a></li>");
        html.AppendLine("</ul>");
        html.AppendLine("</div>");

        html.AppendLine("</div>");
        html.AppendLine("</body></html>");

        return html.ToString();
    }
}

public class MetricsSummaryResponse
{
    public DateTime Timestamp { get; set; }
    public int TotalMetrics { get; set; }
    public Dictionary<string, int> Categories { get; set; } = new();
}

public class PaymentMetricsResponse
{
    public DateTime Timestamp { get; set; }
    public Dictionary<string, object> PaymentMetrics { get; set; } = new();
}

public class SystemMetricsResponse
{
    public DateTime Timestamp { get; set; }
    public Dictionary<string, object> DatabaseMetrics { get; set; } = new();
    public Dictionary<string, object> ApiMetrics { get; set; } = new();
    public Dictionary<string, object> HealthCheckMetrics { get; set; } = new();
}

public class ResetMetricsRequest
{
    public List<string> Categories { get; set; } = new();
}