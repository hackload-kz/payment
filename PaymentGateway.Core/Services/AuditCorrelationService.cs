// SPDX-License-Identifier: MIT
// Copyright (c) 2025 HackLoad Payment Gateway

using Microsoft.Extensions.Logging;
using PaymentGateway.Core.Entities;
using PaymentGateway.Core.Repositories;
using System.Text.Json;

namespace PaymentGateway.Core.Services;

/// <summary>
/// Audit Correlation Service for tracking and correlating audit events across service boundaries
/// </summary>
public interface IAuditCorrelationService
{
    Task<string> CreateCorrelationContextAsync(string operationType, string entityId, Dictionary<string, object>? metadata = null);
    Task<AuditCorrelationContext> GetCorrelationContextAsync(string correlationId);
    Task AddAuditEventAsync(string correlationId, string eventType, string serviceName, string details, Dictionary<string, object>? data = null);
    Task<List<AuditCorrelationEvent>> GetCorrelatedEventsAsync(string correlationId, CancellationToken cancellationToken = default);
    Task<List<AuditCorrelationEvent>> GetEventsByEntityAsync(string entityType, string entityId, CancellationToken cancellationToken = default);
    Task CompleteCorrelationAsync(string correlationId, bool success, string? summary = null);
}

public class AuditCorrelationService : IAuditCorrelationService
{
    private readonly ILogger<AuditCorrelationService> _logger;
    private readonly IComprehensiveAuditService _auditService;
    
    // In-memory correlation contexts (could be moved to Redis for scalability)
    private readonly Dictionary<string, AuditCorrelationContext> _correlationContexts = new();
    private readonly object _correlationLock = new object();
    
    // Metrics
    private static readonly System.Diagnostics.Metrics.Meter _meter = new("PaymentGateway.AuditCorrelation");
    private static readonly System.Diagnostics.Metrics.Counter<long> _correlationCounter = 
        _meter.CreateCounter<long>("audit_correlation_operations_total");
    private static readonly System.Diagnostics.Metrics.Histogram<double> _correlationDuration = 
        _meter.CreateHistogram<double>("audit_correlation_duration_seconds");
    private static readonly System.Diagnostics.Metrics.Gauge<int> _activeCorrelations = 
        _meter.CreateGauge<int>("active_audit_correlations_total");

    public AuditCorrelationService(
        ILogger<AuditCorrelationService> logger,
        IComprehensiveAuditService auditService)
    {
        _logger = logger;
        _auditService = auditService;
    }

    public async Task<string> CreateCorrelationContextAsync(string operationType, string entityId, Dictionary<string, object>? metadata = null)
    {
        var correlationId = Guid.NewGuid().ToString();
        var context = new AuditCorrelationContext
        {
            CorrelationId = correlationId,
            OperationType = operationType,
            EntityId = entityId,
            StartedAt = DateTime.UtcNow,
            Metadata = metadata ?? new Dictionary<string, object>(),
            Events = new List<AuditCorrelationEvent>()
        };

        lock (_correlationLock)
        {
            _correlationContexts[correlationId] = context;
        }

        _correlationCounter.Add(1, new KeyValuePair<string, object?>("operation", "create"),
            new KeyValuePair<string, object?>("type", operationType));
        _activeCorrelations.Record(_correlationContexts.Count);

        _logger.LogDebug("Created audit correlation context: {CorrelationId} for {OperationType} on {EntityId}",
            correlationId, operationType, entityId);

        return correlationId;
    }

    public async Task<AuditCorrelationContext> GetCorrelationContextAsync(string correlationId)
    {
        lock (_correlationLock)
        {
            if (_correlationContexts.TryGetValue(correlationId, out var context))
            {
                return context;
            }
        }

        throw new InvalidOperationException($"Correlation context not found: {correlationId}");
    }

    public async Task AddAuditEventAsync(string correlationId, string eventType, string serviceName, string details, Dictionary<string, object>? data = null)
    {
        try
        {
            var auditEvent = new AuditCorrelationEvent
            {
                EventId = Guid.NewGuid().ToString(),
                CorrelationId = correlationId,
                EventType = eventType,
                ServiceName = serviceName,
                Details = details,
                Data = data ?? new Dictionary<string, object>(),
                Timestamp = DateTime.UtcNow
            };

            // Add to correlation context
            lock (_correlationLock)
            {
                if (_correlationContexts.TryGetValue(correlationId, out var context))
                {
                    context.Events.Add(auditEvent);
                    context.LastEventAt = DateTime.UtcNow;
                }
            }

            // Persist audit event using ComprehensiveAuditService
            var auditContext = new AuditContext
            {
                UserId = serviceName, // Using service name as user for system operations
                CorrelationId = correlationId,
                RequestId = auditEvent.EventId
            };

            await _auditService.LogSystemEventAsync(
                Enum.Parse<AuditAction>(eventType, ignoreCase: true),
                "AuditCorrelation",
                details,
                auditContext);

            _correlationCounter.Add(1, new KeyValuePair<string, object?>("operation", "add_event"),
                new KeyValuePair<string, object?>("event_type", eventType),
                new KeyValuePair<string, object?>("service", serviceName));

            _logger.LogDebug("Added audit event to correlation {CorrelationId}: {EventType} from {ServiceName}",
                correlationId, eventType, serviceName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error adding audit event to correlation {CorrelationId}", correlationId);
            throw;
        }
    }

    public async Task<List<AuditCorrelationEvent>> GetCorrelatedEventsAsync(string correlationId, CancellationToken cancellationToken = default)
    {
        try
        {
            // Get from in-memory context first
            lock (_correlationLock)
            {
                if (_correlationContexts.TryGetValue(correlationId, out var context))
                {
                    return context.Events.OrderBy(e => e.Timestamp).ToList();
                }
            }

            // Fall back to audit service search using correlation ID
            var auditEntries = await _auditService.QueryAuditLogsAsync(new AuditQueryFilter
            {
                CorrelationId = correlationId,
                EntityType = "AuditCorrelation"
            }, cancellationToken);
            
            var events = new List<AuditCorrelationEvent>();

            foreach (var entry in auditEntries)
            {
                try
                {
                    events.Add(new AuditCorrelationEvent
                    {
                        EventId = entry.RequestId ?? Guid.NewGuid().ToString(),
                        CorrelationId = correlationId,
                        EventType = entry.Action.ToString(),
                        ServiceName = entry.UserId ?? "Unknown",
                        Details = entry.Details ?? "",
                        Data = new Dictionary<string, object>(),
                        Timestamp = entry.Timestamp
                    });
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to process audit entry for correlation {CorrelationId}", correlationId);
                }
            }

            return events.OrderBy(e => e.Timestamp).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting correlated events for {CorrelationId}", correlationId);
            throw;
        }
    }

    public async Task<List<AuditCorrelationEvent>> GetEventsByEntityAsync(string entityType, string entityId, CancellationToken cancellationToken = default)
    {
        try
        {
            // Query audit entries by entity ID and type
            var auditEntries = await _auditService.QueryAuditLogsAsync(new AuditQueryFilter
            {
                EntityType = entityType,
                EntityId = Guid.TryParse(entityId, out var entityGuid) ? entityGuid : null
            }, cancellationToken);
            
            var events = new List<AuditCorrelationEvent>();

            foreach (var entry in auditEntries)
            {
                try
                {
                    events.Add(new AuditCorrelationEvent
                    {
                        EventId = entry.RequestId ?? Guid.NewGuid().ToString(),
                        CorrelationId = entry.CorrelationId ?? "unknown",
                        EventType = entry.Action.ToString(),
                        ServiceName = entry.UserId ?? "Unknown",
                        Details = entry.Details ?? "",
                        Data = new Dictionary<string, object>(),
                        Timestamp = entry.Timestamp
                    });
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to process audit entry for entity {EntityType}:{EntityId}", entityType, entityId);
                }
            }

            return events.OrderBy(e => e.Timestamp).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting events by entity {EntityType}:{EntityId}", entityType, entityId);
            throw;
        }
    }

    public async Task CompleteCorrelationAsync(string correlationId, bool success, string? summary = null)
    {
        try
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            
            lock (_correlationLock)
            {
                if (_correlationContexts.TryGetValue(correlationId, out var context))
                {
                    context.CompletedAt = DateTime.UtcNow;
                    context.Success = success;
                    context.Summary = summary;
                    
                    stopwatch.Stop();
                    context.Duration = stopwatch.Elapsed;
                    
                    // Remove from active contexts after some time to prevent memory leaks
                    _ = Task.Delay(TimeSpan.FromMinutes(5)).ContinueWith(_ =>
                    {
                        lock (_correlationLock)
                        {
                            _correlationContexts.Remove(correlationId);
                        }
                    });
                }
            }

            // Add completion event
            await AddAuditEventAsync(correlationId, "CORRELATION_COMPLETED", "AuditCorrelationService", 
                $"Correlation completed: {(success ? "SUCCESS" : "FAILURE")}{(summary != null ? $" - {summary}" : "")}", 
                new Dictionary<string, object>
                {
                    ["Success"] = success,
                    ["Summary"] = summary ?? "",
                    ["Duration"] = stopwatch.ElapsedMilliseconds
                });

            _correlationCounter.Add(1, new KeyValuePair<string, object?>("operation", "complete"),
                new KeyValuePair<string, object?>("success", success.ToString()));
            _correlationDuration.Record(stopwatch.Elapsed.TotalSeconds);
            _activeCorrelations.Record(_correlationContexts.Count);

            _logger.LogDebug("Completed audit correlation {CorrelationId}: {Success}, Duration: {Duration}ms",
                correlationId, success ? "SUCCESS" : "FAILURE", stopwatch.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error completing correlation {CorrelationId}", correlationId);
            throw;
        }
    }
}

/// <summary>
/// Audit correlation context for tracking related events
/// </summary>
public class AuditCorrelationContext
{
    public string CorrelationId { get; set; } = string.Empty;
    public string OperationType { get; set; } = string.Empty;
    public string EntityId { get; set; } = string.Empty;
    public DateTime StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public DateTime? LastEventAt { get; set; }
    public bool Success { get; set; }
    public string? Summary { get; set; }
    public TimeSpan Duration { get; set; }
    public Dictionary<string, object> Metadata { get; set; } = new();
    public List<AuditCorrelationEvent> Events { get; set; } = new();
}

/// <summary>
/// Individual audit event within a correlation context
/// </summary>
public class AuditCorrelationEvent
{
    public string EventId { get; set; } = string.Empty;
    public string CorrelationId { get; set; } = string.Empty;
    public string EventType { get; set; } = string.Empty;
    public string ServiceName { get; set; } = string.Empty;
    public string Details { get; set; } = string.Empty;
    public Dictionary<string, object> Data { get; set; } = new();
    public DateTime Timestamp { get; set; }
}