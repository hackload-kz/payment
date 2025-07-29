using PaymentGateway.Core.Entities;
using PaymentGateway.Core.Interfaces;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace PaymentGateway.Core.Services;

public interface IEntityAuditService
{
    Task<AuditEntry> CreateAuditEntryAsync<T>(T entity, AuditAction action, string? userId = null, string? details = null) where T : BaseEntity;
    Task<List<AuditEntry>> GetEntityAuditHistoryAsync(Guid entityId, CancellationToken cancellationToken = default);
    Task<List<AuditEntry>> GetUserAuditHistoryAsync(string userId, CancellationToken cancellationToken = default);
}

public class EntityAuditService : IEntityAuditService
{
    private readonly ILogger<EntityAuditService> _logger;
    private readonly List<AuditEntry> _auditEntries; // In-memory storage for demo

    public EntityAuditService(ILogger<EntityAuditService> logger)
    {
        _logger = logger;
        _auditEntries = new List<AuditEntry>();
    }

    public async Task<AuditEntry> CreateAuditEntryAsync<T>(T entity, AuditAction action, string? userId = null, string? details = null) 
        where T : BaseEntity
    {
        var auditEntry = new AuditEntry
        {
            EntityId = entity.Id,
            EntityType = typeof(T).Name,
            Action = action,
            UserId = userId,
            Timestamp = DateTime.UtcNow,
            Details = details,
            EntitySnapshotAfter = JsonSerializer.Serialize(entity, new JsonSerializerOptions 
            { 
                WriteIndented = false,
                DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
            })
        };

        _auditEntries.Add(auditEntry);
        
        _logger.LogInformation("Audit entry created: {Action} on {EntityType} {EntityId} by user {UserId}", 
            action, typeof(T).Name, entity.Id, userId ?? "System");

        return await Task.FromResult(auditEntry);
    }

    public async Task<List<AuditEntry>> GetEntityAuditHistoryAsync(Guid entityId, CancellationToken cancellationToken = default)
    {
        var history = _auditEntries
            .Where(ae => ae.EntityId == entityId)
            .OrderByDescending(ae => ae.Timestamp)
            .ToList();

        return await Task.FromResult(history);
    }

    public async Task<List<AuditEntry>> GetUserAuditHistoryAsync(string userId, CancellationToken cancellationToken = default)
    {
        var history = _auditEntries
            .Where(ae => ae.UserId == userId)
            .OrderByDescending(ae => ae.Timestamp)
            .ToList();

        return await Task.FromResult(history);
    }
}

// Removed - using AuditEntry from PaymentGateway.Core.Entities instead