using Payment.Gateway.Middleware;

namespace Payment.Gateway.Extensions;

public static class ApplicationBuilderExtensions
{
    public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder app)
    {
        return app.Use(async (context, next) =>
        {
            context.Response.Headers.Append("X-Content-Type-Options", "nosniff");
            context.Response.Headers.Append("X-Frame-Options", "DENY");
            context.Response.Headers.Append("X-XSS-Protection", "1; mode=block");
            context.Response.Headers.Append("Referrer-Policy", "strict-origin-when-cross-origin");
            context.Response.Headers.Append("Content-Security-Policy", 
                "default-src 'self'; script-src 'self' 'unsafe-inline'; style-src 'self' 'unsafe-inline'");

            await next();
        });
    }

    public static IApplicationBuilder UsePaymentGatewayMiddleware(this IApplicationBuilder app)
    {
        app.UseMiddleware<AuthenticationMiddleware>();
        app.UseMiddleware<GlobalExceptionMiddleware>();
        
        return app;
    }
}