using PaymentGateway.Core.Services;
using PaymentGateway.API.Middleware;
using Serilog;

namespace PaymentGateway.API.Extensions;

public static class ApplicationBuilderExtensions
{
    public static IApplicationBuilder UseCorrelationId(this IApplicationBuilder app)
    {
        return app.UseMiddleware<CorrelationIdMiddleware>();
    }

    public static IApplicationBuilder UseGlobalExceptionHandling(this IApplicationBuilder app)
    {
        return app.UseMiddleware<GlobalExceptionHandlingMiddleware>();
    }




    public static IApplicationBuilder UseAdminAuthentication(this IApplicationBuilder app)
    {
        return app.UseMiddleware<AdminAuthenticationMiddleware>();
    }

    public static IApplicationBuilder UseRequestLogging(this IApplicationBuilder app)
    {
        return app.UseSerilogRequestLogging(options =>
        {
            options.MessageTemplate = "HTTP {RequestMethod} {RequestPath} responded {StatusCode} in {Elapsed:0.0000} ms. CorrelationId: {CorrelationId}";
            options.EnrichDiagnosticContext = (diagnosticContext, httpContext) =>
            {
                var correlationIdService = httpContext.RequestServices.GetService<ICorrelationIdService>();
                if (correlationIdService != null)
                {
                    diagnosticContext.Set("CorrelationId", correlationIdService.CurrentCorrelationId);
                }

                diagnosticContext.Set("UserAgent", httpContext.Request.Headers.TryGetValue("User-Agent", out var userAgent) ? userAgent.ToString() : "Unknown");
                diagnosticContext.Set("ClientIP", httpContext.Connection.RemoteIpAddress?.ToString() ?? "Unknown");
                diagnosticContext.Set("RequestId", httpContext.TraceIdentifier);
            };
        });
    }
}