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
    builder.Services.AddControllers()
        .AddJsonOptions(options =>
        {
            // Configure case-insensitive property matching
            options.JsonSerializerOptions.PropertyNameCaseInsensitive = true;
            options.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
        });

    // Add logging services
    builder.Services.AddLoggingServices(builder.Configuration);

    // Add Prometheus metrics
    builder.Services.AddPrometheusMetrics(builder.Configuration);

    // Add database configuration with metrics
    builder.Services.AddDatabase(builder.Configuration, builder.Environment);

    // Add infrastructure services (repositories and data access)
    builder.Services.AddInfrastructureServices();

    // Add core payment services
    builder.Services.AddPaymentServices(builder.Configuration);

    // Add health checks
    builder.Services.AddHealthChecks();

    // Add middleware services
    builder.Services.AddRequestResponseLogging();
    builder.Services.AddGlobalExceptionHandling();
    builder.Services.AddRequestValidation();
    builder.Services.AddAdminAuthentication(builder.Configuration);
    builder.Services.AddPaymentAuthentication();
    builder.Services.AddPaymentGatewaySecurityHeaders();

    // Add CORS configuration
    builder.Services.AddPaymentGatewayCors(builder.Configuration, builder.Environment);

    // Add API versioning
    builder.Services.AddPaymentGatewayApiVersioning();
    builder.Services.AddVersionedSwagger(builder.Configuration);

    // Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
    builder.Services.AddOpenApi();

    var app = builder.Build();

    // Log application startup
    Log.Information("Starting Payment Gateway application");

    // Migrate database on startup
    await app.MigrateDatabase(); 

    // Configure the HTTP request pipeline.
    var swaggerOptions = app.Configuration.GetSection("Swagger").Get<PaymentGateway.API.Configuration.SwaggerOptions>() ?? new();
    
    if (swaggerOptions.Enabled)
    {
        app.MapOpenApi();
        
        // Configure Swagger UI
        app.UseSwaggerUI(options =>
        {
            options.SwaggerEndpoint("/openapi/v1.json", $"{swaggerOptions.Title} v1");
            options.RoutePrefix = swaggerOptions.RoutePrefix;
            options.DocumentTitle = swaggerOptions.Title;
            options.DisplayOperationId();
            options.DisplayRequestDuration();
            
            // Security settings for production
            if (swaggerOptions.RequireHttps && !app.Environment.IsDevelopment())
            {
                options.ConfigObject.AdditionalItems["onComplete"] = "function() { if (window.location.protocol !== 'https:') { window.location.replace('https:' + window.location.href.substring(window.location.protocol.length)); } }";
            }
            
            // Disable try-it-out functionality if configured
            if (!swaggerOptions.EnableTryItOut)
            {
                options.ConfigObject.AdditionalItems["supportedSubmitMethods"] = "[]";
            }
            
            // Add custom CSS for production environments
            if (!app.Environment.IsDevelopment())
            {
                options.InjectStylesheet("/css/swagger-custom.css");
            }
        });
        
        Log.Information("Swagger UI enabled at /{RoutePrefix}", swaggerOptions.RoutePrefix);
    }
    else
    {
        Log.Information("Swagger UI is disabled via configuration");
    }

    app.UseHttpsRedirection();
    
    // Enable static files serving (CSS, JS, images, etc.)
    app.UseStaticFiles();
    
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
    
    // Add admin authentication (before other auth)
    app.UseAdminAuthentication();
    
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
    
    // Map Prometheus metrics endpoint
    app.MapMetrics();

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
