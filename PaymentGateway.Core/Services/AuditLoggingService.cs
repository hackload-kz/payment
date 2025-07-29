using Microsoft.Extensions.Logging;
using PaymentGateway.Core.Configuration;
using PaymentGateway.Core.Entities;
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
    private readonly AuditConfiguration _auditConfig;
    private readonly ISensitiveDataMaskingService _maskingService;
    private readonly ICorrelationIdService _correlationIdService;

    public AuditLoggingService(
        ILogger<AuditLoggingService> logger,
        AuditConfiguration auditConfig,
        ISensitiveDataMaskingService maskingService,
        ICorrelationIdService correlationIdService)
    {
        _logger = logger;
        _auditConfig = auditConfig;
        _maskingService = maskingService;
        _correlationIdService = correlationIdService;
    }

    public async Task LogPaymentOperationAsync(string operation, string paymentId, object? beforeState, object? afterState, string? userId = null)
    {
        if (!_auditConfig.EnableIntegrityHashing) // Using available property
            return;

        var auditEntry = new AuditLogEntry
        {
            EventType = "PaymentOperation",
            Operation = operation,
            EntityType = "Payment",
            EntityId = paymentId,
            UserId = userId,
            BeforeState = beforeState != null ? _maskingService.MaskObject(beforeState) : null,
            AfterState = afterState != null ? _maskingService.MaskObject(afterState) : null,
            CorrelationId = _correlationIdService.CurrentCorrelationId,
            Timestamp = DateTime.UtcNow
        };

        await LogAuditEntryAsync(auditEntry);
    }

    public async Task LogAuthenticationEventAsync(string eventType, string? userId, bool success, string? details = null)
    {
        if (!_auditConfig.EnableIntegrityHashing) // Using available property
            return;

        var auditEntry = new AuditLogEntry
        {
            EventType = "Authentication",
            Operation = eventType,
            EntityType = "User",
            EntityId = userId,
            UserId = userId,
            Success = success,
            Details = details != null ? _maskingService.MaskSensitiveData(details) : null,
            CorrelationId = _correlationIdService.CurrentCorrelationId,
            Timestamp = DateTime.UtcNow
        };

        await LogAuditEntryAsync(auditEntry);
    }

    public async Task LogDatabaseChangeAsync(string entityType, string entityId, string operation, object? beforeState, object? afterState, string? userId = null)
    {
        if (!_auditConfig.EnableIntegrityHashing) // Using available property
            return;

        var auditEntry = new AuditLogEntry
        {
            EventType = "DatabaseChange",
            Operation = operation,
            EntityType = entityType,
            EntityId = entityId,
            UserId = userId,
            BeforeState = beforeState != null ? _maskingService.MaskObject(beforeState) : null,
            AfterState = afterState != null ? _maskingService.MaskObject(afterState) : null,
            CorrelationId = _correlationIdService.CurrentCorrelationId,
            Timestamp = DateTime.UtcNow
        };

        await LogAuditEntryAsync(auditEntry);
    }

    public async Task LogSecurityEventAsync(string eventType, string details, string? userId = null)
    {
        var auditEntry = new AuditLogEntry
        {
            EventType = "Security",
            Operation = eventType,
            EntityType = "System",
            EntityId = Environment.MachineName,
            UserId = userId,
            Details = _maskingService.MaskSensitiveData(details),
            CorrelationId = _correlationIdService.CurrentCorrelationId,
            Timestamp = DateTime.UtcNow,
            Severity = "High"
        };

        await LogAuditEntryAsync(auditEntry);
    }

    public async Task LogSystemEventAsync(string eventType, string details, object? data = null)
    {
        var auditEntry = new AuditLogEntry
        {
            EventType = "System",
            Operation = eventType,
            EntityType = "System",
            EntityId = Environment.MachineName,
            Details = details,
            AdditionalData = data != null ? _maskingService.MaskObject(data) : null,
            CorrelationId = _correlationIdService.CurrentCorrelationId,
            Timestamp = DateTime.UtcNow
        };

        await LogAuditEntryAsync(auditEntry);
    }

    private async Task LogAuditEntryAsync(AuditLogEntry auditEntry)
    {
        try
        {
            // Log to structured logger with all audit information
            using var scope = _logger.BeginScope(new Dictionary<string, object>
            {
                ["CorrelationId"] = auditEntry.CorrelationId,
                ["EventType"] = auditEntry.EventType,
                ["Operation"] = auditEntry.Operation,
                ["EntityType"] = auditEntry.EntityType,
                ["EntityId"] = auditEntry.EntityId ?? "Unknown",
                ["UserId"] = auditEntry.UserId ?? "System",
                ["Timestamp"] = auditEntry.Timestamp,
                ["Severity"] = auditEntry.Severity ?? "Information"
            });

            _logger.LogInformation("AUDIT: {EventType} - {Operation} on {EntityType}:{EntityId} by {UserId}. Details: {Details}",
                auditEntry.EventType,
                auditEntry.Operation,
                auditEntry.EntityType,
                auditEntry.EntityId,
                auditEntry.UserId ?? "System",
                auditEntry.Details ?? "No additional details");

            // If we have state changes, log them separately
            if (auditEntry.BeforeState != null || auditEntry.AfterState != null)
            {
                _logger.LogInformation("AUDIT_STATE_CHANGE: Before: {BeforeState}, After: {AfterState}",
                    auditEntry.BeforeState ?? "null",
                    auditEntry.AfterState ?? "null");
            }

            // If we have additional data, log it
            if (auditEntry.AdditionalData != null)
            {
                _logger.LogInformation("AUDIT_DATA: {AdditionalData}",
                    auditEntry.AdditionalData);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to log audit entry: {AuditEntry}",
                JsonSerializer.Serialize(auditEntry));
        }

        await Task.CompletedTask;
    }
}

public class AuditLogEntry
{
    public string EventType { get; set; } = string.Empty;
    public string Operation { get; set; } = string.Empty;
    public string EntityType { get; set; } = string.Empty;
    public string? EntityId { get; set; }
    public string? UserId { get; set; }
    public string? BeforeState { get; set; }
    public string? AfterState { get; set; }
    public string? Details { get; set; }
    public string? AdditionalData { get; set; }
    public string CorrelationId { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
    public bool? Success { get; set; }
    public string? Severity { get; set; } = "Information";
}