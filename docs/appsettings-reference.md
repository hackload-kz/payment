# PaymentGateway.API Configuration Reference

Complete developer reference for all configuration options in `appsettings.json` and environment-specific overrides.

## Configuration File Hierarchy

The application uses standard .NET configuration hierarchy:

1. **`appsettings.json`** - Base configuration (production defaults)
2. **`appsettings.Development.json`** - Development overrides  
3. **`appsettings.Staging.json`** - Staging environment overrides
4. **`appsettings.Production.json`** - Production environment overrides
5. **Environment variables** - Runtime overrides (highest priority)

## Core Configuration Sections

### 1. Connection Strings

Controls database connectivity and connection pooling.

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Host=localhost;Database=PaymentGateway;Username=postgres;Password=postgres123!;Port=5432;Pooling=true;MinPoolSize=5;MaxPoolSize=100;Connection Idle Lifetime=300;"
  }
}
```

**Parameters:**
- **Host**: Database server hostname/IP
- **Database**: Database name
- **Username/Password**: Database credentials
- **Port**: Database port (default: 5432 for PostgreSQL)
- **Pooling**: Enable connection pooling (recommended: `true`)
- **MinPoolSize**: Minimum connections in pool
- **MaxPoolSize**: Maximum connections in pool
- **Connection Idle Lifetime**: Connection timeout in seconds

**Environment Differences:**
- **Development**: `PaymentGateway_Dev` database, smaller pool (2-20)
- **Production**: Uses environment variables `${DB_HOST}`, larger pool (20-200)

**Impact on Service:**
- **Higher pool sizes** = Better concurrent request handling
- **Lower pool sizes** = Less resource usage
- **Connection timeouts** = Automatic cleanup of idle connections

### 2. Logging Configuration

Controls application logging behavior across multiple systems.

#### 2.1 Standard .NET Logging

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning",
      "Microsoft.EntityFrameworkCore.Database.Command": "Information",
      "Microsoft.EntityFrameworkCore.Infrastructure": "Warning"
    }
  }
}
```

**Log Levels (in order of verbosity):**
- **Trace**: Most verbose, typically for debugging
- **Debug**: Detailed information for debugging
- **Information**: General informational messages
- **Warning**: Warning messages but not errors
- **Error**: Error messages
- **Critical**: Critical failures that may cause application termination

**Impact on Service:**
- **Debug/Trace levels** = High disk usage, detailed debugging info
- **Warning/Error levels** = Lower disk usage, production-suitable
- **EF Database.Command** = Logs all SQL queries when set to Debug/Information

#### 2.2 Serilog Configuration

```json
{
  "Logging": {
    "Serilog": {
      "MinimumLevel": "Information",
      "EnableConsole": true,
      "EnableFile": true,
      "EnableDatabase": true,
      "LogDirectory": "logs",
      "EnableCompactJsonFormatting": true,
      "EnableStructuredLogging": true,
      "EnableMetrics": true,
      "MinimumLevelOverrides": {
        "Microsoft": "Warning",
        "Microsoft.AspNetCore": "Warning",
        "Microsoft.EntityFrameworkCore": "Information",
        "System.Net.Http.HttpClient": "Warning"
      }
    }
  }
}
```

**Parameters:**
- **MinimumLevel**: Base logging level for Serilog
- **EnableConsole**: Log to console output (`true` for dev, `false` for prod)
- **EnableFile**: Log to files in LogDirectory
- **EnableDatabase**: Log to database audit tables
- **LogDirectory**: Directory for log files (`logs` for dev, `/var/log/paymentgateway` for prod)
- **EnableCompactJsonFormatting**: Compact JSON format for structured logs
- **EnableStructuredLogging**: Enable structured logging with properties
- **EnableMetrics**: Include performance metrics in logs

**Environment Differences:**
- **Development**: Console logging enabled, less structured formatting
- **Production**: Console disabled, compact JSON formatting, database logging

**Impact on Service:**
- **Console logging** = Visible in terminal/container logs
- **File logging** = Persistent logs on disk
- **Database logging** = Queryable audit trail
- **Structured logging** = Better log parsing and analysis

#### 2.3 Audit Configuration

```json
{
  "Logging": {
    "Audit": {
      "EnableAuditLogging": true,
      "AuditTableName": "audit_logs",
      "LogPaymentOperations": true,
      "LogAuthenticationEvents": true,
      "LogDatabaseChanges": true,
      "EnableSensitiveDataMasking": true,
      "SensitiveFields": [
        "CardNumber",
        "CVV", 
        "Password",
        "Token",
        "TerminalKey",
        "TeamSlug"
      ]
    }
  }
}
```

**Parameters:**
- **EnableAuditLogging**: Master switch for audit system
- **AuditTableName**: Database table for audit records
- **LogPaymentOperations**: Audit payment init/confirm/cancel operations
- **LogAuthenticationEvents**: Audit login attempts and token validation
- **LogDatabaseChanges**: Audit all entity changes via EF
- **EnableSensitiveDataMasking**: Mask sensitive data in audit logs
- **SensitiveFields**: List of field names to mask

**Environment Differences:**
- **Development**: Sensitive data masking disabled for debugging
- **Production**: Sensitive data masking enabled for compliance

**Impact on Service:**
- **Audit logging** = Comprehensive compliance trail
- **Sensitive data masking** = GDPR/PCI DSS compliance
- **Payment operations logging** = Transaction audit trail
- **Authentication logging** = Security event tracking

#### 2.4 Retention Configuration

```json
{
  "Logging": {
    "Retention": {
      "FileRetentionDays": 30,
      "DatabaseRetentionDays": 90,
      "AuditRetentionDays": 365,
      "EnableAutoCleanup": true,
      "CleanupSchedule": "0 2 * * *"
    }
  }
}
```

**Parameters:**
- **FileRetentionDays**: Days to keep log files (7 dev, 30 default, 90 prod)
- **DatabaseRetentionDays**: Days to keep database logs (30 dev, 90 default, 365 prod)
- **AuditRetentionDays**: Days to keep audit records (90 dev, 365 default, 2555 prod)
- **EnableAutoCleanup**: Automatic cleanup of old logs
- **CleanupSchedule**: Cron expression for cleanup (daily at 2 AM)

**Environment Differences:**
- **Development**: Shorter retention (7-90 days), cleanup disabled
- **Production**: Longer retention (90-2555 days), cleanup enabled

**Impact on Service:**
- **Shorter retention** = Less disk usage, faster queries
- **Longer retention** = Better compliance, more disk usage
- **Auto cleanup** = Prevents disk space issues

### 3. Metrics Configuration

Controls monitoring and metrics collection systems.

#### 3.1 Prometheus Metrics

```json
{
  "Metrics": {
    "Prometheus": {
      "Enabled": true,
      "MetricsPath": "/metrics",
      "Port": 8081,
      "Host": "*",
      "EnableDebugMetrics": false,
      "ScrapeIntervalSeconds": 15,
      "Labels": {
        "service": "payment_gateway",
        "environment": "production"
      }
    }
  }
}
```

**Parameters:**
- **Enabled**: Enable Prometheus metrics endpoint
- **MetricsPath**: HTTP path for metrics (`/metrics`)
- **Port**: Port for metrics server (8081)
- **Host**: Bind address (`*` for all interfaces)
- **EnableDebugMetrics**: Include debug-level metrics
- **ScrapeIntervalSeconds**: Expected scrape interval for optimization
- **Labels**: Default labels added to all metrics

**Environment Differences:**
- **Development**: Debug metrics enabled, faster scrape (5s)
- **Production**: Debug metrics disabled, slower scrape (30s)

**Impact on Service:**
- **Debug metrics** = More detailed performance data, higher overhead
- **Faster scrape intervals** = More real-time monitoring, higher load
- **Metrics enabled** = Essential for production monitoring

#### 3.2 Metrics Dashboard

```json
{
  "Metrics": {
    "Dashboard": {
      "Enabled": true,
      "DashboardPath": "/metrics-dashboard", 
      "ShowDetailedMetrics": true,
      "ShowBusinessMetrics": true,
      "ShowSystemMetrics": true,
      "RefreshIntervalSeconds": 30
    }
  }
}
```

**Parameters:**
- **Enabled**: Enable built-in metrics dashboard
- **DashboardPath**: HTTP path for dashboard
- **ShowDetailedMetrics**: Include detailed performance metrics
- **ShowBusinessMetrics**: Include payment/business metrics
- **ShowSystemMetrics**: Include system resource metrics
- **RefreshIntervalSeconds**: Dashboard auto-refresh interval

**Environment Differences:**
- **Development**: All metrics shown, fast refresh (10s)
- **Production**: Dashboard disabled for security

**Impact on Service:**
- **Dashboard enabled** = Easy metrics visualization, potential security exposure
- **Detailed metrics** = Better debugging, higher memory usage
- **Fast refresh** = Real-time monitoring, higher CPU usage

#### 3.3 Business Metrics

```json
{
  "Metrics": {
    "Business": {
      "EnableTransactionMetrics": true,
      "EnableRevenueMetrics": true,
      "EnableCustomerMetrics": true,
      "EnablePaymentMethodMetrics": true,
      "EnableTeamMetrics": true,
      "ExcludedTeams": [],
      "CurrencyConversionRates": {
        "USD": 1.0,
        "EUR": 0.85,
        "RUB": 90.0
      }
    }
  }
}
```

**Parameters:**
- **EnableTransactionMetrics**: Track payment transaction metrics
- **EnableRevenueMetrics**: Track revenue and amounts
- **EnableCustomerMetrics**: Track customer-related metrics
- **EnablePaymentMethodMetrics**: Track payment method usage
- **EnableTeamMetrics**: Track team/merchant metrics
- **ExcludedTeams**: Teams to exclude from metrics
- **CurrencyConversionRates**: Exchange rates for multi-currency reporting

**Impact on Service:**
- **Business metrics** = Essential for business intelligence
- **Currency conversion** = Unified reporting across currencies
- **Team exclusion** = Privacy/compliance for specific merchants

### 4. Database Configuration

Controls Entity Framework behavior and database interaction.

```json
{
  "Database": {
    "EnableRetryOnFailure": true,
    "MaxRetryCount": 3,
    "MaxRetryDelay": "00:00:30",
    "CommandTimeout": 30,
    "PoolSize": 128,
    "EnableSensitiveDataLogging": false,
    "EnableDetailedErrors": false
  }
}
```

**Parameters:**
- **EnableRetryOnFailure**: Automatic retry on transient failures
- **MaxRetryCount**: Maximum retry attempts (2 dev, 3 default, 5 prod)
- **MaxRetryDelay**: Maximum delay between retries (10s dev, 30s default, 60s prod)
- **CommandTimeout**: SQL command timeout in seconds (60s dev, 30s default, 60s prod)
- **PoolSize**: Connection pool size (20 dev, 128 default, 200 prod)
- **EnableSensitiveDataLogging**: Log parameter values (dev only)
- **EnableDetailedErrors**: Detailed EF error messages (dev only)

**Environment Differences:**
- **Development**: Sensitive data logging enabled, detailed errors
- **Production**: Sensitive data logging disabled, no detailed errors

**Impact on Service:**
- **Retry on failure** = Better resilience to network issues
- **Higher retry counts** = Better reliability, potentially slower failure detection
- **Larger pool sizes** = Better concurrent performance, more resource usage
- **Sensitive data logging** = Security risk in production, useful for debugging

### 5. Health Checks Configuration

Controls application health monitoring.

```json
{
  "HealthChecks": {
    "Database": {
      "Timeout": "00:00:05",
      "FailureStatus": "Degraded"
    }
  }
}
```

**Parameters:**
- **Database.Timeout**: Database health check timeout (5s default, 10s prod)
- **Database.FailureStatus**: Status when database check fails (`Degraded` default, `Unhealthy` prod)

**Environment Differences:**
- **Development**: `Degraded` status allows continued operation
- **Production**: `Unhealthy` status triggers alerting/restart

**Impact on Service:**
- **Shorter timeouts** = Faster health check responses, may miss slow queries
- **Degraded vs Unhealthy** = Different alerting/restart behaviors

### 6. Security Configuration (Production Only)

Advanced security settings available in production environment.

```json
{
  "Security": {
    "Authentication": {
      "TokenExpirationMinutes": 30,
      "RefreshTokenExpirationDays": 7,
      "EnableRefreshTokens": true,
      "MaxFailedAttempts": 3,
      "LockoutDurationMinutes": 30
    },
    "Https": {
      "RequireHttps": true,
      "EnableHsts": true,
      "HstsMaxAgeDays": 365,
      "HstsIncludeSubdomains": true,
      "HstsPreload": true
    },
    "RateLimit": {
      "IpRateLimit": {
        "MaxRequests": 500,
        "WindowMinutes": 1
      },
      "AuthenticationRateLimit": {
        "MaxRequests": 5,
        "WindowMinutes": 1
      }
    }
  }
}
```

**Authentication Parameters:**
- **TokenExpirationMinutes**: JWT token lifetime
- **RefreshTokenExpirationDays**: Refresh token lifetime
- **EnableRefreshTokens**: Allow token refresh
- **MaxFailedAttempts**: Failed auth attempts before lockout
- **LockoutDurationMinutes**: Account lockout duration

**HTTPS Parameters:**
- **RequireHttps**: Force HTTPS redirects
- **EnableHsts**: HTTP Strict Transport Security
- **HstsMaxAgeDays**: HSTS header max-age
- **HstsIncludeSubdomains**: HSTS applies to subdomains
- **HstsPreload**: Enable HSTS preload

**Rate Limiting Parameters:**
- **IpRateLimit**: General IP-based rate limiting
- **AuthenticationRateLimit**: Stricter limits for auth endpoints

**Impact on Service:**
- **Token expiration** = Security vs user experience balance
- **HSTS** = Better security, potential accessibility issues
- **Rate limiting** = DDoS protection, may affect legitimate traffic

### 7. Feature Flags (Production Only)

Controls advanced features and behaviors.

```json
{
  "FeatureFlags": {
    "EnableAdvancedMetrics": true,
    "EnableSecurityAudit": true,
    "EnableTokenExpiration": true,
    "EnableRateLimit": true,
    "EnableHttpsEnforcement": true,
    "EnableConfigurationValidation": true,
    "EnableConfigurationHotReload": false
  }
}
```

**Parameters:**
- **EnableAdvancedMetrics**: Advanced performance metrics collection
- **EnableSecurityAudit**: Enhanced security event logging
- **EnableTokenExpiration**: Token-based authentication expiration
- **EnableRateLimit**: IP and authentication rate limiting
- **EnableHttpsEnforcement**: Force HTTPS for all requests
- **EnableConfigurationValidation**: Startup configuration validation
- **EnableConfigurationHotReload**: Runtime configuration updates

**Impact on Service:**
- **Advanced metrics** = Better monitoring, higher resource usage
- **Security audit** = Comprehensive security logging
- **Configuration validation** = Prevents startup with invalid config
- **Hot reload** = Runtime config changes without restart

### 8. Allowed Hosts

```json
{
  "AllowedHosts": "*"
}
```

**Parameters:**
- **AllowedHosts**: Comma-separated list of allowed host headers
- **`*`**: Allow all hosts (development/testing)
- **Specific domains**: Production security (e.g., `"api.payment.com,*.payment.com"`)

**Impact on Service:**
- **`*`** = No host header protection, suitable for development
- **Specific hosts** = Protection against host header attacks

## Environment-Specific Behaviors

### Development Environment
- **Verbose logging** (Debug level)
- **Sensitive data logging** enabled
- **Console logging** enabled
- **Smaller connection pools** (2-20)
- **Shorter log retention** (7-90 days)
- **Debug metrics** enabled
- **Dashboard** enabled with fast refresh
- **No security hardening**

### Production Environment
- **Minimal logging** (Warning/Error levels)
- **Sensitive data masking** enabled
- **No console logging**
- **Larger connection pools** (20-200)
- **Longer log retention** (90-2555 days)
- **Dashboard disabled**
- **Security hardening** enabled
- **Rate limiting** active
- **HTTPS enforcement**

## Configuration Best Practices

### 1. Security
- Never store secrets in configuration files
- Use environment variables for sensitive data
- Enable sensitive data masking in production
- Configure appropriate log retention periods

### 2. Performance
- Size connection pools based on expected load
- Use appropriate log levels for environment
- Configure retry policies for resilience
- Monitor metrics collection overhead

### 3. Monitoring
- Enable Prometheus metrics in all environments
- Configure appropriate health check timeouts
- Set up proper log aggregation
- Use structured logging for better analysis

### 4. Compliance
- Enable audit logging for payment operations
- Configure appropriate data retention periods
- Mask sensitive data in logs
- Implement proper authentication controls

## Configuration Validation

The application validates configuration at startup when `EnableConfigurationValidation` is enabled. Common validation errors:

- **Missing required connection strings**
- **Invalid log retention periods**
- **Misconfigured metric endpoints**
- **Invalid cron expressions**
- **Missing required environment variables**

## Troubleshooting Configuration Issues

### Database Connection Issues
1. Check connection string format
2. Verify PostgreSQL service status
3. Validate credentials and permissions
4. Check network connectivity and firewall rules

### Logging Issues
1. Verify log directory permissions
2. Check disk space availability
3. Validate log level configurations
4. Ensure database logging tables exist

### Metrics Issues
1. Check port availability (8081)
2. Verify Prometheus configuration
3. Test metrics endpoint accessibility
4. Monitor metrics collection performance

### Performance Issues
1. Review connection pool sizing
2. Check retry policy configuration
3. Monitor database query performance
4. Validate timeout settings

This configuration reference provides developers with comprehensive understanding of how each setting affects the PaymentGateway service behavior across different environments.