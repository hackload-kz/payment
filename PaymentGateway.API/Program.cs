using PaymentGateway.API.Extensions;
using PaymentGateway.API.Configuration;
using PaymentGateway.API.Middleware;
using PaymentGateway.Infrastructure.Data;
using PaymentGateway.Infrastructure.Extensions;
using PaymentGateway.Core.Extensions;
using Prometheus;
using Serilog;

// Configure Serilog early for startup logging
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    // Configure Serilog
    builder.Host.UseSerilog();

    // Add services to the container.
    builder.Services.AddControllers();

    // Add logging services
    builder.Services.AddLoggingServices(builder.Configuration);

    // Add Prometheus metrics
    builder.Services.AddPrometheusMetrics(builder.Configuration);

    // Add database configuration with metrics
    builder.Services.AddDatabase(builder.Configuration, builder.Environment);

    // Add core payment services
    builder.Services.AddPaymentServices(builder.Configuration);

    // Add health checks
    builder.Services.AddHealthChecks();

    // Add middleware services
    builder.Services.AddRequestResponseLogging();
    builder.Services.AddGlobalExceptionHandling();
    builder.Services.AddRequestValidation();
    builder.Services.AddPaymentGatewaySecurityHeaders();

    // Add CORS configuration
    builder.Services.AddPaymentGatewayCors(builder.Configuration, builder.Environment);

    // Add API versioning
    builder.Services.AddPaymentGatewayApiVersioning();
    builder.Services.AddVersionedSwagger();

    // Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
    builder.Services.AddOpenApi();

    var app = builder.Build();

    // Log application startup
    Log.Information("Starting Payment Gateway application");

    // Migrate database on startup
    await app.MigrateDatabase();

    // Configure the HTTP request pipeline.
    if (app.Environment.IsDevelopment())
    {
        app.MapOpenApi();
    }

    app.UseHttpsRedirection();
    
    // Add security headers (early in pipeline)
    app.UseSecurityHeaders();
    
    // Add global exception handling (early for all errors)
    app.UseGlobalExceptionHandling();
    
    // Add correlation ID middleware (must be early in pipeline)
    app.UseCorrelationId();
    
    // Add metrics middleware (after correlation ID)
    app.UseMiddleware<PaymentGateway.API.Middleware.MetricsMiddleware>();
    
    // Add request validation (before authentication)
    app.UseRequestValidation();
    
    // Add authentication and rate limiting
    app.UseAuthenticationRateLimit();
    app.UsePaymentAuthentication();
    
    // Add CORS (after authentication)
    app.UsePaymentGatewayCors(app.Environment);
    
    // Add request/response logging (after authentication for security)
    app.UseRequestResponseLogging();
    
    // Add Serilog request logging (after custom logging)
    app.UseRequestLogging();

    // Add routing
    app.UseRouting();

    // Map controllers
    app.MapControllers();

    // Map health checks
    app.MapHealthChecks("/health");

    Log.Information("Payment Gateway application started successfully");
    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Payment Gateway application terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}

// Make Program class public for integration tests
public partial class Program { }
