using Microsoft.EntityFrameworkCore;
using Serilog;
using Prometheus;
using FluentValidation;
using Payment.Gateway.Infrastructure;
using Payment.Gateway.Extensions;

var builder = WebApplication.CreateBuilder(args);

// Configure Serilog
builder.Host.UseSerilog((context, configuration) =>
{
    configuration.ReadFrom.Configuration(context.Configuration);
});

// Add services to the container
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new() { Title = "Payment Gateway API", Version = "v1" });
    c.AddSecurityDefinition("Bearer", new()
    {
        Type = Microsoft.OpenApi.Models.SecuritySchemeType.Http,
        Scheme = "bearer",
        Description = "JWT Authorization header using Bearer scheme"
    });
});

// Add PostgreSQL
builder.Services.AddDbContext<PaymentDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

// Add FluentValidation
builder.Services.AddValidatorsFromAssemblyContaining<Program>();

// Add custom services
builder.Services.AddPaymentGatewayServices();

// Add CORS for development
builder.Services.AddCors(options =>
{
    options.AddPolicy("Development", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

// Add health checks
builder.Services.AddHealthChecks()
    .AddNpgSql(builder.Configuration.GetConnectionString("DefaultConnection")!)
    .ForwardToPrometheus();

var app = builder.Build();

// Configure the HTTP request pipeline
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
    app.UseCors("Development");
}

// Add security headers
if (builder.Configuration.GetValue<bool>("PaymentGateway:SecurityHeaders"))
{
    app.UseSecurityHeaders();
}

// Add Prometheus metrics
if (builder.Configuration.GetValue<bool>("PaymentGateway:EnablePrometheus"))
{
    app.UseMetricServer();
    app.UseHttpMetrics();
}

// Add Serilog request logging
app.UseSerilogRequestLogging();

app.UseHttpsRedirection();

// Add static files
app.UseStaticFiles();

// Add custom middleware
app.UsePaymentGatewayMiddleware();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHealthChecks("/health");

app.Run();
