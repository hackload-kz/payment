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

    // Add database configuration
    builder.Services.AddDatabase(builder.Configuration, builder.Environment);

    // Add health checks
    builder.Services.AddHealthChecks();

    // Add Prometheus metrics
    builder.Services.AddSingleton(Metrics.DefaultRegistry);

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
    
    // Add Serilog request logging (after correlation ID)
    app.UseRequestLogging();

    // Add routing
    app.UseRouting();

    // Map controllers
    app.MapControllers();

    // Map health checks
    app.MapHealthChecks("/health");
    
    // Map Prometheus metrics
    app.UseMetricServer();

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
