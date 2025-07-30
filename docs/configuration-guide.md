# PaymentGateway Configuration Guide

This guide covers all configuration options and environment variables for the PaymentGateway service.

## Configuration Files

The application uses standard .NET configuration hierarchy:

- `appsettings.json` - Base configuration
- `appsettings.Development.json` - Development overrides
- `appsettings.Staging.json` - Staging overrides
- `appsettings.Production.json` - Production overrides
- Environment variables - Runtime overrides

## Environment Variables

### Core Database Configuration

```bash
# Database connection string
ConnectionStrings__DefaultConnection="Host=localhost;Database=PaymentGateway;Username=postgres;Password=postgres123!;Port=5432;Pooling=true;MinPoolSize=5;MaxPoolSize=100;Connection Idle Lifetime=300;"

# Individual database settings (ONLY required for Production/Staging environments)
# These are used for environment variable substitution in appsettings.Production.json
# NOT needed for Development - Development uses direct connection strings
DB_HOST=localhost
DB_PORT=5432
DB_NAME=PaymentGateway
DB_USER=postgres
DB_PASSWORD=postgres123!
```

### Logging Configuration

```bash
# General logging level (Debug, Information, Warning, Error, Critical)
Logging__LogLevel__Default=Information
Logging__LogLevel__PaymentGateway=Debug

# Serilog specific settings
Logging__Serilog__MinimumLevel=Information
Logging__Serilog__EnableConsole=true
Logging__Serilog__EnableFile=true
Logging__Serilog__EnableDatabase=true
Logging__Serilog__LogDirectory=logs

# Enable metrics collection in logs
Logging__Serilog__EnableMetrics=true
```

### Audit Configuration

```bash
# Enable comprehensive audit logging
Logging__Audit__EnableAuditLogging=true
Logging__Audit__LogPaymentOperations=true
Logging__Audit__LogAuthenticationEvents=true
Logging__Audit__LogDatabaseChanges=true

# Data protection
Logging__Audit__EnableSensitiveDataMasking=true

# Audit table name
Logging__Audit__AuditTableName=audit_logs
```

### Metrics and Monitoring

```bash
# Prometheus metrics
Metrics__Prometheus__Enabled=true
Metrics__Prometheus__MetricsPath=/metrics
Metrics__Prometheus__Port=8081
Metrics__Prometheus__EnableDebugMetrics=false
Metrics__Prometheus__ScrapeIntervalSeconds=15

# Metrics dashboard
Metrics__Dashboard__Enabled=true
Metrics__Dashboard__DashboardPath=/metrics-dashboard
Metrics__Dashboard__ShowDetailedMetrics=true
Metrics__Dashboard__RefreshIntervalSeconds=30

# Business metrics
Metrics__Business__EnableTransactionMetrics=true
Metrics__Business__EnableRevenueMetrics=true
Metrics__Business__EnableCustomerMetrics=true
Metrics__Business__EnableTeamMetrics=true
```

### Database Performance

```bash
# Connection pooling and retry settings
Database__EnableRetryOnFailure=true
Database__MaxRetryCount=3
Database__MaxRetryDelay=00:00:30
Database__CommandTimeout=30
Database__PoolSize=128

# Development only - enables sensitive data logging
Database__EnableSensitiveDataLogging=false
Database__EnableDetailedErrors=false
```

### Data Retention

```bash
# Log retention periods (in days)
Logging__Retention__FileRetentionDays=30
Logging__Retention__DatabaseRetentionDays=90
Logging__Retention__AuditRetentionDays=365

# Automatic cleanup
Logging__Retention__EnableAutoCleanup=true
Logging__Retention__CleanupSchedule="0 2 * * *"
```

### Security and CORS

```bash
# Allowed hosts
AllowedHosts="localhost;*.example.com"

# CORS settings (configured programmatically)
CORS_ORIGINS="http://localhost:3000,https://localhost:3001"
CORS_ALLOW_CREDENTIALS=true
```

## Environment-Specific Configurations

### Development Environment

```bash
# Development-specific overrides
ASPNETCORE_ENVIRONMENT=Development
ASPNETCORE_URLS="https://localhost:7162;http://localhost:5162"

# Enhanced debugging
Logging__LogLevel__Default=Debug
Logging__LogLevel__Microsoft.EntityFrameworkCore.Database.Command=Debug
Database__EnableSensitiveDataLogging=true
Database__EnableDetailedErrors=true

# Reduced retention for development
Logging__Retention__FileRetentionDays=7
Logging__Retention__DatabaseRetentionDays=30
Logging__Retention__EnableAutoCleanup=false

# More frequent metrics collection
Metrics__Prometheus__ScrapeIntervalSeconds=5
Metrics__Dashboard__RefreshIntervalSeconds=10
```

### Staging Environment

```bash
ASPNETCORE_ENVIRONMENT=Staging
ASPNETCORE_URLS="https://localhost:7162"

# Production-like settings with some debugging
Logging__LogLevel__Default=Information
Logging__LogLevel__PaymentGateway=Debug
Database__EnableSensitiveDataLogging=false
Database__EnableDetailedErrors=false
```

### Production Environment

```bash
ASPNETCORE_ENVIRONMENT=Production
ASPNETCORE_URLS="https://0.0.0.0:443"

# Minimal logging for performance
Logging__LogLevel__Default=Warning
Logging__LogLevel__PaymentGateway=Information
Logging__Serilog__MinimumLevel=Information

# Enhanced security
Logging__Audit__EnableSensitiveDataMasking=true
Database__EnableSensitiveDataLogging=false
Database__EnableDetailedErrors=false

# Optimized performance
Database__PoolSize=128
Database__CommandTimeout=30
```

## Docker Configuration

When running with Docker Compose, use the `.env` file:

```bash
# .env file for docker-compose

# PostgreSQL container initialization (used by postgres image)
POSTGRES_DB=PaymentGateway
POSTGRES_USER=paymentuser
POSTGRES_PASSWORD=secure_password_here

# Application database connection (used by .NET app)
# Note: DB_HOST=postgres matches the service name in docker-compose.yml
ConnectionStrings__DefaultConnection="Host=postgres;Database=${POSTGRES_DB};Username=${POSTGRES_USER};Password=${POSTGRES_PASSWORD};Port=5432"

# Alternative: Set individual connection components (if needed)
# DB_HOST=postgres
# DB_PORT=5432
# DB_NAME=${POSTGRES_DB}
# DB_USER=${POSTGRES_USER}
# DB_PASSWORD=${POSTGRES_PASSWORD}

# Application settings
ASPNETCORE_ENVIRONMENT=Development
ASPNETCORE_URLS=http://+:80;https://+:443
```

## Configuration Validation

The application validates configuration at startup. Check the logs for any configuration errors:

```bash
# Check configuration validation
dotnet run --project PaymentGateway.API --configuration Development
```

## Sensitive Data Configuration

### Sensitive Fields

The following fields are automatically masked in audit logs:

- `CardNumber`
- `CVV`
- `Password`
- `Token`
- `TerminalKey`
- `TeamSlug`

### Adding Custom Sensitive Fields

```json
{
  "Logging": {
    "Audit": {
      "SensitiveFields": [
        "CardNumber",
        "CVV",
        "Password",
        "Token",
        "TerminalKey",
        "TeamSlug",
        "CustomSensitiveField"
      ]
    }
  }
}
```

## Currency Configuration

Configure exchange rates for multi-currency support:

```json
{
  "Metrics": {
    "Business": {
      "CurrencyConversionRates": {
        "USD": 1.0,
        "EUR": 0.85,
        "RUB": 90.0,
        "GBP": 0.73
      }
    }
  }
}
```

## Health Check Configuration

```bash
# Health check timeout
HealthChecks__Database__Timeout=00:00:05
HealthChecks__Database__FailureStatus=Degraded
```

## Best Practices

1. **Never store secrets in configuration files** - Use environment variables or Azure Key Vault
2. **Use different databases for different environments**
3. **Enable audit logging in production**
4. **Set appropriate retention periods** based on compliance requirements
5. **Use connection pooling** for better performance
6. **Monitor configuration changes** through audit logs

## Troubleshooting Configuration

### Common Issues

1. **Database connection fails**:
   - Verify connection string format
   - Check PostgreSQL service status
   - Validate credentials

2. **Logs not appearing**:
   - Check `LogDirectory` permissions
   - Verify `EnableFile` is true
   - Check minimum log level settings

3. **Metrics not collected**:
   - Ensure `Metrics__Prometheus__Enabled=true`
   - Check port availability
   - Verify Prometheus configuration

### Configuration Debug Commands

```bash
# View current configuration
dotnet run --project PaymentGateway.API -- --list-config

# Test database connection
dotnet run --project PaymentGateway.API -- --test-db

# Validate configuration
dotnet run --project PaymentGateway.API -- --validate-config
```