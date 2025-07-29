using PaymentGateway.Core.Services;

namespace PaymentGateway.API.Middleware;

public class CorrelationIdMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<CorrelationIdMiddleware> _logger;
    private const string CorrelationIdHeaderName = "X-Correlation-ID";

    public CorrelationIdMiddleware(RequestDelegate next, ILogger<CorrelationIdMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context, ICorrelationIdService correlationIdService)
    {
        string correlationId;

        // Check if correlation ID is provided in the request headers
        if (context.Request.Headers.TryGetValue(CorrelationIdHeaderName, out var correlationIdFromHeader) 
            && !string.IsNullOrWhiteSpace(correlationIdFromHeader.FirstOrDefault()))
        {
            correlationId = correlationIdFromHeader.First()!;
        }
        else
        {
            // Generate a new correlation ID if not provided
            correlationId = correlationIdService.GenerateCorrelationId();
        }

        // Set the correlation ID in the service
        correlationIdService.SetCorrelationId(correlationId);

        // Add correlation ID to response headers
        context.Response.Headers.TryAdd(CorrelationIdHeaderName, correlationId);

        // Add correlation ID to the logging scope
        using var logScope = _logger.BeginScope(new Dictionary<string, object>
        {
            ["CorrelationId"] = correlationId
        });

        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception occurred during request processing. CorrelationId: {CorrelationId}",
                correlationId);
            throw;
        }
    }
}