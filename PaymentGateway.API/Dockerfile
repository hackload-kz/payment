# Use the official .NET 9.0 runtime as a parent image
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
WORKDIR /app
EXPOSE 8080
EXPOSE 8081

# Use the .NET 9.0 SDK for building
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src

# Copy csproj files and restore dependencies
COPY ["PaymentGateway.API/PaymentGateway.API.csproj", "PaymentGateway.API/"]
COPY ["PaymentGateway.Core/PaymentGateway.Core.csproj", "PaymentGateway.Core/"]
COPY ["PaymentGateway.Infrastructure/PaymentGateway.Infrastructure.csproj", "PaymentGateway.Infrastructure/"]

# Restore dependencies
RUN dotnet restore "./PaymentGateway.API/PaymentGateway.API.csproj"

# Copy the source code
COPY . .
WORKDIR "/src/PaymentGateway.API"

# Build the application
RUN dotnet build "./PaymentGateway.API.csproj" -c $BUILD_CONFIGURATION -o /app/build

# Publish the application
FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./PaymentGateway.API.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

# Final stage/image
FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .

# Create a non-root user for security
RUN adduser --disabled-password --gecos '' appuser && chown -R appuser /app
USER appuser

ENTRYPOINT ["dotnet", "PaymentGateway.API.dll"]