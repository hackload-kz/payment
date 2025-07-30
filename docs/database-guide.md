# PaymentGateway Database Setup Guide

This guide covers database setup, testing, and management for the PaymentGateway service.

## Database Overview

The PaymentGateway uses **PostgreSQL** as its primary database with Entity Framework Core for data access and migrations.

### Database Architecture

- **Primary Database**: PostgreSQL 16+
- **ORM**: Entity Framework Core
- **Migration System**: Code-First with EF Migrations
- **Connection Pooling**: Built-in .NET connection pooling
- **Monitoring**: Integrated with Prometheus metrics

## Quick Database Setup

### Option 1: Docker Compose (Recommended)

The easiest way to set up the complete development environment:

```bash
# Start all services including PostgreSQL
docker-compose up -d

# Check database health
docker-compose ps
```

This starts:
- **PostgreSQL** on port 5432
- **Redis** on port 6379 (for caching)
- **Prometheus** on port 9090 (for metrics)
- **Grafana** on port 3000 (for dashboards)

### Option 2: Local PostgreSQL Installation

#### Ubuntu/Debian
```bash
# Install PostgreSQL
sudo apt update
sudo apt install postgresql postgresql-contrib

# Start service
sudo systemctl start postgresql
sudo systemctl enable postgresql

# Create database and user
sudo -u postgres psql
```

#### macOS with Homebrew
```bash
# Install PostgreSQL
brew install postgresql@16
brew services start postgresql@16

# Create database
psql postgres
```

#### Windows
```bash
# Download and install from https://www.postgresql.org/download/windows/
# Or use chocolatey
choco install postgresql

# Start service
net start postgresql-x64-16
```

## Database Configuration

### Connection Strings

#### Development
```bash
ConnectionStrings__DefaultConnection="Host=localhost;Database=PaymentGateway_Dev;Username=postgres;Password=postgres123!;Port=5432;Pooling=true;MinPoolSize=2;MaxPoolSize=20;Connection Idle Lifetime=300;"
```

#### Production
```bash
ConnectionStrings__DefaultConnection="Host=prod-db-server;Database=PaymentGateway;Username=paymentuser;Password=secure_password;Port=5432;Pooling=true;MinPoolSize=5;MaxPoolSize=100;Connection Idle Lifetime=300;SSL Mode=Require;"
```

#### Docker Environment
```bash
ConnectionStrings__DefaultConnection="Host=postgres;Database=PaymentGateway;Username=postgres;Password=postgres123!;Port=5432"
```

### Database Setup Commands

```bash
# Create database and user manually
sudo -u postgres psql

-- In PostgreSQL shell:
CREATE DATABASE PaymentGateway_Dev;
CREATE USER paymentuser WITH PASSWORD 'postgres123!';
GRANT ALL PRIVILEGES ON DATABASE PaymentGateway_Dev TO paymentuser;
\q
```

## Entity Framework Migrations

### Initial Setup

```bash
# Install EF Core tools globally
dotnet tool install --global dotnet-ef

# Verify installation
dotnet ef --version
```

### Running Migrations

```bash
# From project root directory
cd PaymentGateway.Infrastructure

# Apply migrations to database
dotnet ef database update --project ../PaymentGateway.Infrastructure --startup-project ../PaymentGateway.API

# Apply to specific environment
dotnet ef database update --project ../PaymentGateway.Infrastructure --startup-project ../PaymentGateway.API --configuration Development
```

### Creating New Migrations

```bash
# Create a new migration
dotnet ef migrations add MigrationName --project PaymentGateway.Infrastructure --startup-project PaymentGateway.API

# Review the generated migration files in Migrations/ folder before applying
```

### Migration Management

```bash
# List all migrations
dotnet ef migrations list --project PaymentGateway.Infrastructure --startup-project PaymentGateway.API

# Remove last migration (if not applied)
dotnet ef migrations remove --project PaymentGateway.Infrastructure --startup-project PaymentGateway.API

# Reset database (WARNING: Deletes all data)
dotnet ef database drop --force --project PaymentGateway.Infrastructure --startup-project PaymentGateway.API
dotnet ef database update --project PaymentGateway.Infrastructure --startup-project PaymentGateway.API
```

## Database Schema

### Core Tables

#### **payments**
Main payment transaction table:
```sql
CREATE TABLE payments (
    payment_id VARCHAR(20) PRIMARY KEY,
    order_id VARCHAR(36) NOT NULL,
    terminal_key VARCHAR(20) NOT NULL,
    amount BIGINT NOT NULL,
    current_status INTEGER NOT NULL DEFAULT 0,
    description VARCHAR(500),
    customer_key VARCHAR(36),
    notification_url TEXT,
    success_url TEXT,
    fail_url TEXT,
    created_date TIMESTAMP NOT NULL DEFAULT NOW(),
    updated_date TIMESTAMP NOT NULL DEFAULT NOW(),
    -- Additional fields...
);
```

#### **merchants**
Merchant/terminal configuration:
```sql
CREATE TABLE merchants (
    terminal_key VARCHAR(20) PRIMARY KEY,
    password VARCHAR(50) NOT NULL,
    is_active BOOLEAN NOT NULL DEFAULT true,
    created_date TIMESTAMP NOT NULL DEFAULT NOW(),
    last_login_date TIMESTAMP
);
```

#### **payment_status_history**
Payment status change tracking:
```sql
CREATE TABLE payment_status_history (
    id SERIAL PRIMARY KEY,
    payment_id VARCHAR(20) NOT NULL,
    status INTEGER NOT NULL,
    timestamp TIMESTAMP NOT NULL DEFAULT NOW(),
    error_code VARCHAR(10),
    message VARCHAR(500),
    FOREIGN KEY (payment_id) REFERENCES payments(payment_id)
);
```

#### **audit_logs** (EF Generated)
Comprehensive audit trail for all operations:
- Entity changes tracking
- User action logging
- System event recording
- Performance metrics

## Testing Database Setup

### Test Database Configuration

```bash
# Create separate test database
ConnectionStrings__TestConnection="Host=localhost;Database=PaymentGateway_Test;Username=postgres;Password=postgres123!;Port=5432"
```

### Database Testing Strategies

#### 1. In-Memory Database (Fast)
```csharp
// Use in unit tests for fast execution
services.AddDbContext<PaymentGatewayDbContext>(options =>
    options.UseInMemoryDatabase("TestDatabase"));
```

#### 2. Test Container (Realistic)
```csharp
// Use Testcontainers for integration tests
var postgres = new PostgreSqlBuilder()
    .WithDatabase("PaymentGateway_Test")
    .WithUsername("postgres")
    .WithPassword("postgres123!")
    .Build();
```

#### 3. Dedicated Test Database
```bash
# Create test database
createdb PaymentGateway_Test -U postgres

# Run migrations on test database
dotnet ef database update --connection "Host=localhost;Database=PaymentGateway_Test;Username=postgres;Password=postgres123!"
```

### Test Data Setup

#### Seed Data for Testing
```sql
-- Insert test merchants
INSERT INTO merchants (terminal_key, password, is_active) VALUES 
('TEST_TERMINAL_1', 'test_password_1', true),
('TEST_TERMINAL_2', 'test_password_2', true),
('DEMO_TERMINAL', 'demo_password', true);

-- Insert test payments
INSERT INTO payments (payment_id, order_id, terminal_key, amount, current_status) VALUES
('TEST_PAYMENT_1', 'ORDER_001', 'TEST_TERMINAL_1', 10000, 0),
('TEST_PAYMENT_2', 'ORDER_002', 'TEST_TERMINAL_1', 25000, 1),
('TEST_PAYMENT_3', 'ORDER_003', 'TEST_TERMINAL_2', 50000, 2);
```

## Database Performance

### Indexing Strategy
```sql
-- Performance indexes
CREATE INDEX idx_payments_order_id ON payments(order_id);
CREATE INDEX idx_payments_terminal_key ON payments(terminal_key);
CREATE INDEX idx_payments_status_created ON payments(current_status, created_date);
CREATE INDEX idx_payment_history_payment_timestamp ON payment_status_history(payment_id, timestamp);
```

### Connection Pool Configuration
```json
{
  "Database": {
    "PoolSize": 128,
    "CommandTimeout": 30,
    "EnableRetryOnFailure": true,
    "MaxRetryCount": 3,
    "MaxRetryDelay": "00:00:30"
  }
}
```

### Performance Monitoring
```bash
# Database metrics endpoint
curl https://localhost:7162/api/metrics/system

# Prometheus metrics
curl http://localhost:8081/metrics | grep postgres
```

## Database Backup and Recovery

### Backup Commands
```bash
# Create full backup
pg_dump -h localhost -U postgres -d PaymentGateway > backup_$(date +%Y%m%d_%H%M%S).sql

# Create compressed backup
pg_dump -h localhost -U postgres -d PaymentGateway | gzip > backup_$(date +%Y%m%d_%H%M%S).sql.gz

# Backup with Docker
docker exec payment-postgres pg_dump -U postgres PaymentGateway > backup.sql
```

### Restore Commands
```bash
# Restore from backup
psql -h localhost -U postgres -d PaymentGateway < backup.sql

# Restore compressed backup
gunzip -c backup.sql.gz | psql -h localhost -U postgres -d PaymentGateway

# Restore with Docker
docker exec -i payment-postgres psql -U postgres -d PaymentGateway < backup.sql
```

## Troubleshooting

### Common Database Issues

#### 1. Connection Issues
```bash
# Check if PostgreSQL is running
sudo systemctl status postgresql

# Test connection
pg_isready -h localhost -p 5432

# Check logs
sudo tail -f /var/log/postgresql/postgresql-16-main.log
```

#### 2. Migration Issues
```bash
# Check migration status
dotnet ef migrations list

# Force migration
dotnet ef database update --verbose

# Check EF logs
Logging__LogLevel__Microsoft.EntityFrameworkCore=Debug
```

#### 3. Performance Issues
```bash
# Monitor active connections
SELECT count(*) FROM pg_stat_activity WHERE datname = 'PaymentGateway';

# Check slow queries
SELECT query, mean_exec_time, calls 
FROM pg_stat_statements 
ORDER BY mean_exec_time DESC LIMIT 10;
```

### Database Health Checks

The application includes built-in health checks:

```bash
# Check database health
curl https://localhost:7162/health

# Detailed health information
curl https://localhost:7162/health?detailed=true
```

## Security Considerations

### Database Security Best Practices

1. **Use strong passwords** for database users
2. **Enable SSL connections** in production
3. **Restrict database access** to application servers only
4. **Regular security updates** for PostgreSQL
5. **Audit logging** for sensitive operations
6. **Data encryption** for sensitive fields

### Connection Security
```bash
# Production connection with SSL
ConnectionStrings__DefaultConnection="Host=prod-server;Database=PaymentGateway;Username=appuser;Password=secure_password;SSL Mode=Require;Trust Server Certificate=false;"
```

## Monitoring and Maintenance

### Regular Maintenance Tasks

```bash
# Analyze table statistics
ANALYZE;

# Vacuum to reclaim space
VACUUM;

# Reindex for performance
REINDEX DATABASE PaymentGateway;
```

### Automated Maintenance
```sql
-- Set up automated maintenance (run weekly)
CREATE EXTENSION IF NOT EXISTS pg_cron;
SELECT cron.schedule('vacuum-analyze', '0 2 * * 0', 'VACUUM ANALYZE;');
```