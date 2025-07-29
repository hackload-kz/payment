using Prometheus;
using Serilog.Core;
using Serilog.Events;

namespace PaymentGateway.Infrastructure.Logging;

public interface ILoggingMetricsService
{
    void IncrementLogCount(LogEventLevel level, string sourceContext);
    void IncrementAuditLogCount(string eventType);
    void RecordLogProcessingTime(double milliseconds);
}

public class LoggingMetricsService : ILoggingMetricsService
{
    private static readonly Counter LogEventsTotal = Metrics
        .CreateCounter("payment_gateway_log_events_total", 
            "Total number of log events", 
            new[] { "level", "source_context" });

    private static readonly Counter AuditLogEventsTotal = Metrics
        .CreateCounter("payment_gateway_audit_log_events_total", 
            "Total number of audit log events", 
            new[] { "event_type" });

    private static readonly Histogram LogProcessingDuration = Metrics
        .CreateHistogram("payment_gateway_log_processing_duration_seconds", 
            "Time spent processing log events");

    public void IncrementLogCount(LogEventLevel level, string sourceContext)
    {
        LogEventsTotal.WithLabels(level.ToString(), sourceContext).Inc();
    }

    public void IncrementAuditLogCount(string eventType)
    {
        AuditLogEventsTotal.WithLabels(eventType).Inc();
    }

    public void RecordLogProcessingTime(double milliseconds)
    {
        LogProcessingDuration.Observe(milliseconds / 1000.0);
    }
}

public class MetricsEnricher : ILogEventEnricher
{
    private readonly ILoggingMetricsService _metricsService;

    public MetricsEnricher(ILoggingMetricsService metricsService)
    {
        _metricsService = metricsService;
    }

    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        var sourceContext = "Unknown";
        
        if (logEvent.Properties.TryGetValue("SourceContext", out var sourceContextProperty) &&
            sourceContextProperty is ScalarValue scalarValue &&
            scalarValue.Value is string sourceContextString)
        {
            sourceContext = sourceContextString;
        }

        _metricsService.IncrementLogCount(logEvent.Level, sourceContext);

        // Check if this is an audit log event
        if (logEvent.MessageTemplate.Text.StartsWith("AUDIT:", StringComparison.OrdinalIgnoreCase))
        {
            var eventType = "Unknown";
            if (logEvent.Properties.TryGetValue("EventType", out var eventTypeProperty) &&
                eventTypeProperty is ScalarValue eventTypeScalar &&
                eventTypeScalar.Value is string eventTypeString)
            {
                eventType = eventTypeString;
            }
            
            _metricsService.IncrementAuditLogCount(eventType);
        }
    }
}