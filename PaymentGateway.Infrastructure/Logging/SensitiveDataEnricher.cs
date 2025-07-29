using PaymentGateway.Core.Services;
using Serilog.Core;
using Serilog.Events;

namespace PaymentGateway.Infrastructure.Logging;

public class SensitiveDataEnricher : ILogEventEnricher
{
    private readonly ISensitiveDataMaskingService _maskingService;

    public SensitiveDataEnricher(ISensitiveDataMaskingService maskingService)
    {
        _maskingService = maskingService;
    }

    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        // Check if the log event contains sensitive data and mask it
        if (logEvent.MessageTemplate.Text.Contains("CardNumber", StringComparison.OrdinalIgnoreCase) ||
            logEvent.MessageTemplate.Text.Contains("CVV", StringComparison.OrdinalIgnoreCase) ||
            logEvent.MessageTemplate.Text.Contains("Password", StringComparison.OrdinalIgnoreCase))
        {
            var maskedProperty = propertyFactory.CreateProperty("SensitiveDataMasked", true);
            logEvent.AddPropertyIfAbsent(maskedProperty);
        }
    }
}