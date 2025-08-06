using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace PaymentGateway.Core.Services;

public interface IAuditLoggingService
{
    Task LogPaymentOperationAsync(string operation, string paymentId, object? beforeState, object? afterState, string? userId = null);
    Task LogAuthenticationEventAsync(string eventType, string? userId, bool success, string? details = null);
    Task LogDatabaseChangeAsync(string entityType, string entityId, string operation, object? beforeState, object? afterState, string? userId = null);
    Task LogSecurityEventAsync(string eventType, string details, string? userId = null);
    Task LogSystemEventAsync(string eventType, string details, object? data = null);
}

public class AuditLoggingService : IAuditLoggingService
{
    private readonly ILogger<AuditLoggingService> _logger;

    public AuditLoggingService(ILogger<AuditLoggingService> logger)
    {
        _logger = logger;
    }

    public async Task LogPaymentOperationAsync(string operation, string paymentId, object? beforeState, object? afterState, string? userId = null)
    {
        _logger.LogInformation("Payment operation {Operation} for payment {PaymentId} by user {UserId}", 
            operation, paymentId, userId ?? "anonymous");
        await Task.CompletedTask;
    }

    public async Task LogAuthenticationEventAsync(string eventType, string? userId, bool success, string? details = null)
    {
        _logger.LogInformation("Authentication event {EventType} for user {UserId}: {Success} - {Details}", 
            eventType, userId ?? "anonymous", success ? "Success" : "Failed", details ?? "");
        await Task.CompletedTask;
    }

    public async Task LogDatabaseChangeAsync(string entityType, string entityId, string operation, object? beforeState, object? afterState, string? userId = null)
    {
        _logger.LogInformation("Database change {Operation} on {EntityType} {EntityId} by user {UserId}", 
            operation, entityType, entityId, userId ?? "system");
        await Task.CompletedTask;
    }

    public async Task LogSecurityEventAsync(string eventType, string details, string? userId = null)
    {
        _logger.LogWarning("Security event {EventType} for user {UserId}: {Details}", 
            eventType, userId ?? "anonymous", details);
        await Task.CompletedTask;
    }

    public async Task LogSystemEventAsync(string eventType, string details, object? data = null)
    {
        _logger.LogInformation("System event {EventType}: {Details}", eventType, details);
        await Task.CompletedTask;
    }
}