# Build stage
FROM mcr.microsoft.com/dotnet/sdk:9.0-alpine AS build
WORKDIR /src

# Copy project files
COPY PaymentGateway.sln ./
COPY PaymentGateway.API/PaymentGateway.API.csproj PaymentGateway.API/
COPY PaymentGateway.Core/PaymentGateway.Core.csproj PaymentGateway.Core/
COPY PaymentGateway.Infrastructure/PaymentGateway.Infrastructure.csproj PaymentGateway.Infrastructure/
COPY PaymentGateway.Tests/PaymentGateway.Tests.csproj PaymentGateway.Tests/

# Restore dependencies
RUN dotnet restore

# Copy source code
COPY . .

# Build application
RUN dotnet build PaymentGateway.sln -c Release --no-restore

# Test stage (optional - can be skipped in production builds)
FROM build AS test
WORKDIR /src/PaymentGateway.Tests
RUN dotnet test ../PaymentGateway.sln --no-build -c Release --logger trx --results-directory /testresults

# Publish stage
FROM build AS publish
WORKDIR /src/PaymentGateway.API
RUN dotnet publish -c Release --no-build -o /app/publish

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:9.0-alpine AS runtime
WORKDIR /app

# Create non-root user for security
RUN addgroup -g 1001 -S appgroup && \
    adduser -S appuser -u 1001 -G appgroup

# Install required packages for health checks and PostgreSQL
RUN apk add --no-cache curl postgresql-client

# Copy published application
COPY --from=publish /app/publish .

# Set ownership to non-root user
RUN chown -R appuser:appgroup /app

# Switch to non-root user
USER appuser

# Configure ASP.NET Core
ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production

# Expose ports
EXPOSE 8080 8081

# Health check
HEALTHCHECK --interval=30s --timeout=3s --start-period=5s --retries=3 \
    CMD curl -f http://localhost:8080/health || exit 1

# Start application
ENTRYPOINT ["dotnet", "PaymentGateway.API.dll"]