using PaymentGateway.Core.Services;
using Serilog.Core;
using Serilog.Events;

namespace PaymentGateway.Infrastructure.Logging;

public class CorrelationIdEnricher : ILogEventEnricher
{
    private readonly ICorrelationIdService _correlationIdService;

    public CorrelationIdEnricher(ICorrelationIdService correlationIdService)
    {
        _correlationIdService = correlationIdService;
    }

    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        var correlationId = _correlationIdService.CurrentCorrelationId;
        var property = propertyFactory.CreateProperty("CorrelationId", correlationId);
        logEvent.AddPropertyIfAbsent(property);
    }
}