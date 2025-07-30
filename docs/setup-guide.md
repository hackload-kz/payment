# PaymentGateway Local Setup Guide

This guide will help you set up and run the PaymentGateway service locally for development and testing.

## Project Overview

The PaymentGateway is a comprehensive payment processing system with the following components:

- **PaymentGateway.API** - Main web API with controllers and middleware
- **PaymentGateway.Core** - Business logic, entities, and services
- **PaymentGateway.Infrastructure** - Data access, logging, and external services
- **PaymentGateway.Tests** - Unit and integration tests

## Prerequisites

- **.NET 8.0 SDK** or later
- **PostgreSQL 12+** (for database)
- **Docker & Docker Compose** (optional, for containerized setup)
- **Git** (for version control)

## Quick Start

### 1. Clone and Build

```bash
# Clone the repository
git clone <repository-url>
cd hackload-payment

# Restore dependencies
dotnet restore PaymentGateway.sln

# Build the solution
dotnet build PaymentGateway.sln
```

### 2. Database Setup

#### Option A: Using Docker (Recommended)
```bash
# Start PostgreSQL with Docker Compose
docker-compose up -d

# This will start:
# - PostgreSQL on port 5432
# - Prometheus on port 9090 
# - Grafana on port 3000
```

#### Option B: Local PostgreSQL Installation
```bash
# Install PostgreSQL (Ubuntu/Debian)
sudo apt-get install postgresql postgresql-contrib

# Or on macOS with Homebrew
brew install postgresql

# Start PostgreSQL service
sudo systemctl start postgresql  # Linux
brew services start postgresql   # macOS

# Create database and user
sudo -u postgres psql
CREATE DATABASE PaymentGateway;
CREATE USER paymentuser WITH PASSWORD 'postgres123!';
GRANT ALL PRIVILEGES ON DATABASE PaymentGateway TO paymentuser;
\q
```

### 3. Run Database Migrations

```bash
# Install Entity Framework CLI tools (if not already installed)
dotnet tool install --global dotnet-ef

# Navigate to Infrastructure project
cd PaymentGateway.Infrastructure

# Run migrations
dotnet ef database update --project ../PaymentGateway.Infrastructure --startup-project ../PaymentGateway.API
```

### 4. Start the Application

```bash
# From the root directory
dotnet run --project PaymentGateway.API

# Or specify configuration
dotnet run --project PaymentGateway.API --configuration Development
```

The API will be available at:
- **HTTPS**: https://localhost:7162
- **HTTP**: http://localhost:5162
- **Swagger UI**: https://localhost:7162/swagger
- **Health Check**: https://localhost:7162/health
- **Metrics**: https://localhost:7162/metrics

## Development Setup

### Hot Reload Development
```bash
# Run with hot reload for development
dotnet watch run --project PaymentGateway.API
```

### Running Tests
```bash
# Run all tests
dotnet test PaymentGateway.sln

# Run specific test project
dotnet test PaymentGateway.Tests

# Run with coverage
dotnet test --collect:"XPlat Code Coverage"
```

### Code Quality Tools
```bash
# Format code
dotnet format PaymentGateway.sln

# Analyze code
dotnet analyze PaymentGateway.sln
```

## Verification

After setup, verify everything is working:

1. **API Health Check**:
   ```bash
   curl https://localhost:7162/health
   ```

2. **Database Connection**:
   ```bash
   curl https://localhost:7162/api/metrics/system
   ```

3. **Swagger Documentation**:
   Open https://localhost:7162/swagger in your browser

## Troubleshooting

### Common Issues

1. **Port Already in Use**:
   - Check `PaymentGateway.API/Properties/launchSettings.json`
   - Modify ports as needed

2. **Database Connection Issues**:
   - Verify PostgreSQL is running: `systemctl status postgresql`
   - Check connection string in `appsettings.Development.json`
   - Ensure database and user exist

3. **SSL Certificate Issues**:
   ```bash
   # Trust development certificates
   dotnet dev-certs https --trust
   ```

4. **Migration Issues**:
   ```bash
   # Reset migrations (WARNING: This will delete data)
   dotnet ef database drop --force
   dotnet ef database update
   ```

### Logs and Debugging

- **Application logs**: Check `logs/` directory
- **Enable detailed logging**: Set `Logging:LogLevel:Default` to `Debug` in appsettings
- **Database queries**: Set `Microsoft.EntityFrameworkCore.Database.Command` to `Information`

## Next Steps

- Review [Configuration Guide](configuration-guide.md) for environment variables
- See [Database Guide](database-guide.md) for database testing setup
- Check [API Reference](api-reference.md) for endpoint documentation