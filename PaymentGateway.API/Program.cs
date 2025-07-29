using PaymentGateway.API.Extensions;
using PaymentGateway.Infrastructure.Data;
using PaymentGateway.Infrastructure.Extensions;
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

    // Add health checks
    builder.Services.AddHealthChecks();

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
    
    // Add correlation ID middleware (must be early in pipeline)
    app.UseCorrelationId();
    
    // Add metrics middleware (after correlation ID)
    app.UseMiddleware<PaymentGateway.API.Middleware.MetricsMiddleware>();
    
    // Add Serilog request logging (after metrics)
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
