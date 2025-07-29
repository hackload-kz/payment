using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PaymentGateway.Core.Configuration;
using PaymentGateway.Core.Services;
using Serilog;
using Serilog.Events;
using Serilog.Formatting.Compact;
using Serilog.Sinks.PostgreSQL;

namespace PaymentGateway.Infrastructure.Logging;

public static class SerilogConfigurationService
{
    public static ILogger CreateLogger(IConfiguration configuration, IServiceProvider serviceProvider)
    {
        var loggingConfig = configuration.GetSection(LoggingConfiguration.SectionName).Get<LoggingConfiguration>() 
                           ?? new LoggingConfiguration();

        var loggerConfig = new LoggerConfiguration()
            .ReadFrom.Configuration(configuration)
            .MinimumLevel.Is(ParseLogLevel(loggingConfig.Serilog.MinimumLevel));

        // Add minimum level overrides
        foreach (var levelOverride in loggingConfig.Serilog.MinimumLevelOverrides)
        {
            loggerConfig.MinimumLevel.Override(levelOverride.Key, ParseLogLevel(levelOverride.Value));
        }

        // Add enrichers
        loggerConfig
            .Enrich.FromLogContext()
            .Enrich.WithMachineName()
            .Enrich.WithEnvironmentName()
            .Enrich.WithThreadId()
            .Enrich.WithProcessId()
            .Enrich.WithProcessName();

        // Add custom enrichers if services are available
        try
        {
            var correlationIdService = serviceProvider.GetService<ICorrelationIdService>();
            if (correlationIdService != null)
            {
                loggerConfig.Enrich.With(new CorrelationIdEnricher(correlationIdService));
            }

            var maskingService = serviceProvider.GetService<ISensitiveDataMaskingService>();
            if (maskingService != null)
            {
                loggerConfig.Enrich.With(new SensitiveDataEnricher(maskingService));
            }

            var metricsService = serviceProvider.GetService<ILoggingMetricsService>();
            if (metricsService != null)
            {
                loggerConfig.Enrich.With(new MetricsEnricher(metricsService));
            }
        }
        catch
        {
            // Services not available during initial setup, continue without custom enrichers
        }

        // Configure sinks
        ConfigureSinks(loggerConfig, loggingConfig, configuration);

        return loggerConfig.CreateLogger();
    }

    private static void ConfigureSinks(LoggerConfiguration loggerConfig, LoggingConfiguration loggingConfig, IConfiguration configuration)
    {
        // Console sink
        if (loggingConfig.Serilog.EnableConsole)
        {
            if (loggingConfig.Serilog.EnableCompactJsonFormatting)
            {
                loggerConfig.WriteTo.Console(new CompactJsonFormatter());
            }
            else
            {
                loggerConfig.WriteTo.Console(
                    outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz}] [{Level:u3}] [{CorrelationId}] {Message:lj}{NewLine}{Exception}");
            }
        }

        // File sink
        if (loggingConfig.Serilog.EnableFile)
        {
            var logDirectory = Path.Combine(AppContext.BaseDirectory, loggingConfig.Serilog.LogDirectory);
            Directory.CreateDirectory(logDirectory);

            if (loggingConfig.Serilog.EnableCompactJsonFormatting)
            {
                loggerConfig.WriteTo.File(
                    new CompactJsonFormatter(),
                    Path.Combine(logDirectory, "payment-gateway-.json"),
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: loggingConfig.Retention.FileRetentionDays,
                    shared: true);
            }
            else
            {
                loggerConfig.WriteTo.File(
                    Path.Combine(logDirectory, "payment-gateway-.log"),
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: loggingConfig.Retention.FileRetentionDays,
                    outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz}] [{Level:u3}] [{CorrelationId}] {Message:lj}{NewLine}{Exception}",
                    shared: true);
            }
        }

        // Database sink
        if (loggingConfig.Serilog.EnableDatabase)
        {
            var connectionString = configuration.GetConnectionString("DefaultConnection");
            if (!string.IsNullOrEmpty(connectionString))
            {
                var columnOptions = GetPostgreSqlColumnOptions(loggingConfig);
                
                loggerConfig.WriteTo.PostgreSQL(
                    connectionString,
                    "logs",
                    columnOptions,
                    needAutoCreateTable: true,
                    respectCase: true);
            }
        }
    }

    private static IDictionary<string, ColumnWriterBase> GetPostgreSqlColumnOptions(LoggingConfiguration loggingConfig)
    {
        return new Dictionary<string, ColumnWriterBase>
        {
            { "timestamp", new TimestampColumnWriter() },
            { "level", new LevelColumnWriter() },
            { "message", new RenderedMessageColumnWriter() },
            { "message_template", new MessageTemplateColumnWriter() },
            { "exception", new ExceptionColumnWriter() },
            { "correlation_id", new SinglePropertyColumnWriter("CorrelationId", PropertyWriteMethod.ToString) },
            { "event_type", new SinglePropertyColumnWriter("EventType", PropertyWriteMethod.ToString) },
            { "operation", new SinglePropertyColumnWriter("Operation", PropertyWriteMethod.ToString) },
            { "entity_type", new SinglePropertyColumnWriter("EntityType", PropertyWriteMethod.ToString) },
            { "entity_id", new SinglePropertyColumnWriter("EntityId", PropertyWriteMethod.ToString) },
            { "user_id", new SinglePropertyColumnWriter("UserId", PropertyWriteMethod.ToString) },
            { "machine_name", new SinglePropertyColumnWriter("MachineName", PropertyWriteMethod.ToString) },
            { "environment_name", new SinglePropertyColumnWriter("EnvironmentName", PropertyWriteMethod.ToString) },
            { "properties", new PropertiesColumnWriter() }
        };
    }

    private static LogEventLevel ParseLogLevel(string level)
    {
        return level.ToLowerInvariant() switch
        {
            "verbose" => LogEventLevel.Verbose,
            "debug" => LogEventLevel.Debug,
            "information" => LogEventLevel.Information,
            "warning" => LogEventLevel.Warning,
            "error" => LogEventLevel.Error,
            "fatal" => LogEventLevel.Fatal,
            _ => LogEventLevel.Information
        };
    }
}