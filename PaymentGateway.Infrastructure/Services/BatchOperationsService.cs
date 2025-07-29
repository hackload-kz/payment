using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PaymentGateway.Core.Data;
using PaymentGateway.Core.Entities;
using System.Data;
using System.Text;
using Npgsql;

namespace PaymentGateway.Infrastructure.Services;

/// <summary>
/// Service for performing bulk database operations efficiently
/// </summary>
public interface IBatchOperationsService
{
    Task<int> BulkInsertAsync<T>(IEnumerable<T> entities, CancellationToken cancellationToken = default) where T : BaseEntity;
    Task<int> BulkUpdateAsync<T>(IEnumerable<T> entities, CancellationToken cancellationToken = default) where T : BaseEntity;
    Task<int> BulkDeleteAsync<T>(IEnumerable<Guid> ids, CancellationToken cancellationToken = default) where T : BaseEntity;
    Task<int> BulkUpsertAsync<T>(IEnumerable<T> entities, CancellationToken cancellationToken = default) where T : BaseEntity;
    Task<BatchOperationResult> ExecuteBatchOperationAsync<T>(BatchOperation<T> operation, CancellationToken cancellationToken = default) where T : BaseEntity;
}

public class BatchOperationsService : IBatchOperationsService
{
    private readonly PaymentGatewayDbContext _context;
    private readonly ILogger<BatchOperationsService> _logger;
    private const int DefaultBatchSize = 1000;

    public BatchOperationsService(PaymentGatewayDbContext context, ILogger<BatchOperationsService> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<int> BulkInsertAsync<T>(IEnumerable<T> entities, CancellationToken cancellationToken = default) where T : BaseEntity
    {
        var entityList = entities.ToList();
        if (!entityList.Any())
        {
            return 0;
        }

        _logger.LogInformation("Starting bulk insert of {Count} {EntityType} entities", entityList.Count, typeof(T).Name);

        var totalInserted = 0;
        var batches = entityList.Chunk(DefaultBatchSize);

        foreach (var batch in batches)
        {
            try
            {
                // Set audit fields
                var now = DateTime.UtcNow;
                foreach (var entity in batch)
                {
                    entity.CreatedAt = now;
                    entity.UpdatedAt = now;
                    entity.Id = entity.Id == Guid.Empty ? Guid.NewGuid() : entity.Id;
                }

                // Use EF Core's bulk insert capabilities
                _context.Set<T>().AddRange(batch);
                var inserted = await _context.SaveChangesAsync(cancellationToken);
                totalInserted += inserted;

                _logger.LogDebug("Inserted batch of {BatchSize} {EntityType} entities", batch.Length, typeof(T).Name);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to insert batch of {EntityType} entities", typeof(T).Name);
                throw;
            }
        }

        _logger.LogInformation("Successfully bulk inserted {TotalInserted} {EntityType} entities", totalInserted, typeof(T).Name);
        return totalInserted;
    }

    public async Task<int> BulkUpdateAsync<T>(IEnumerable<T> entities, CancellationToken cancellationToken = default) where T : BaseEntity
    {
        var entityList = entities.ToList();
        if (!entityList.Any())
        {
            return 0;
        }

        _logger.LogInformation("Starting bulk update of {Count} {EntityType} entities", entityList.Count, typeof(T).Name);

        var totalUpdated = 0;
        var batches = entityList.Chunk(DefaultBatchSize);

        foreach (var batch in batches)
        {
            try
            {
                // Load existing entities for update
                var ids = batch.Select(e => e.Id).ToList();
                var existingEntities = await _context.Set<T>()
                    .Where(e => ids.Contains(e.Id))
                    .ToDictionaryAsync(e => e.Id, cancellationToken);

                var now = DateTime.UtcNow;
                var updatedInBatch = 0;

                foreach (var entity in batch)
                {
                    if (existingEntities.TryGetValue(entity.Id, out var existingEntity))
                    {
                        // Update audit fields
                        entity.UpdatedAt = now;
                        entity.CreatedAt = existingEntity.CreatedAt; // Preserve original creation time

                        // Update the entity
                        _context.Entry(existingEntity).CurrentValues.SetValues(entity);
                        updatedInBatch++;
                    }
                }

                var updated = await _context.SaveChangesAsync(cancellationToken);
                totalUpdated += updated;

                _logger.LogDebug("Updated batch of {UpdatedInBatch} {EntityType} entities", updatedInBatch, typeof(T).Name);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to update batch of {EntityType} entities", typeof(T).Name);
                throw;
            }
        }

        _logger.LogInformation("Successfully bulk updated {TotalUpdated} {EntityType} entities", totalUpdated, typeof(T).Name);
        return totalUpdated;
    }

    public async Task<int> BulkDeleteAsync<T>(IEnumerable<Guid> ids, CancellationToken cancellationToken = default) where T : BaseEntity
    {
        var idList = ids.ToList();
        if (!idList.Any())
        {
            return 0;
        }

        _logger.LogInformation("Starting bulk delete of {Count} {EntityType} entities", idList.Count, typeof(T).Name);

        var totalDeleted = 0;
        var batches = idList.Chunk(DefaultBatchSize);

        foreach (var batch in batches)
        {
            try
            {
                // For soft delete, update IsDeleted flag
                var entities = await _context.Set<T>()
                    .Where(e => batch.Contains(e.Id))
                    .ToListAsync(cancellationToken);

                var now = DateTime.UtcNow;
                foreach (var entity in entities)
                {
                    entity.IsDeleted = true;
                    entity.DeletedAt = now;
                    entity.UpdatedAt = now;
                }

                var deleted = await _context.SaveChangesAsync(cancellationToken);
                totalDeleted += deleted;

                _logger.LogDebug("Soft deleted batch of {BatchSize} {EntityType} entities", entities.Count, typeof(T).Name);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to delete batch of {EntityType} entities", typeof(T).Name);
                throw;
            }
        }

        _logger.LogInformation("Successfully bulk deleted {TotalDeleted} {EntityType} entities", totalDeleted, typeof(T).Name);
        return totalDeleted;
    }

    public async Task<int> BulkUpsertAsync<T>(IEnumerable<T> entities, CancellationToken cancellationToken = default) where T : BaseEntity
    {
        var entityList = entities.ToList();
        if (!entityList.Any())
        {
            return 0;
        }

        _logger.LogInformation("Starting bulk upsert of {Count} {EntityType} entities", entityList.Count, typeof(T).Name);

        var totalProcessed = 0;
        var batches = entityList.Chunk(DefaultBatchSize);

        foreach (var batch in batches)
        {
            try
            {
                var ids = batch.Select(e => e.Id).ToList();
                var existingEntities = await _context.Set<T>()
                    .Where(e => ids.Contains(e.Id))
                    .ToDictionaryAsync(e => e.Id, cancellationToken);

                var now = DateTime.UtcNow;
                var toInsert = new List<T>();
                var toUpdate = new List<T>();

                foreach (var entity in batch)
                {
                    if (existingEntities.ContainsKey(entity.Id))
                    {
                        // Update existing
                        entity.UpdatedAt = now;
                        entity.CreatedAt = existingEntities[entity.Id].CreatedAt;
                        toUpdate.Add(entity);
                    }
                    else
                    {
                        // Insert new
                        entity.CreatedAt = now;
                        entity.UpdatedAt = now;
                        entity.Id = entity.Id == Guid.Empty ? Guid.NewGuid() : entity.Id;
                        toInsert.Add(entity);
                    }
                }

                // Perform updates
                foreach (var entity in toUpdate)
                {
                    var existingEntity = existingEntities[entity.Id];
                    _context.Entry(existingEntity).CurrentValues.SetValues(entity);
                }

                // Perform inserts
                if (toInsert.Any())
                {
                    _context.Set<T>().AddRange(toInsert);
                }

                var processed = await _context.SaveChangesAsync(cancellationToken);
                totalProcessed += processed;

                _logger.LogDebug("Upserted batch: {InsertCount} inserts, {UpdateCount} updates", 
                    toInsert.Count, toUpdate.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to upsert batch of {EntityType} entities", typeof(T).Name);
                throw;
            }
        }

        _logger.LogInformation("Successfully bulk upserted {TotalProcessed} {EntityType} entities", totalProcessed, typeof(T).Name);
        return totalProcessed;
    }

    public async Task<BatchOperationResult> ExecuteBatchOperationAsync<T>(BatchOperation<T> operation, CancellationToken cancellationToken = default) where T : BaseEntity
    {
        var startTime = DateTime.UtcNow;
        var result = new BatchOperationResult
        {
            OperationType = operation.OperationType,
            EntityType = typeof(T).Name,
            StartTime = startTime
        };

        try
        {
            _logger.LogInformation("Starting batch operation {OperationType} for {EntityType} with {Count} entities",
                operation.OperationType, typeof(T).Name, operation.Entities.Count());

            var affectedRows = operation.OperationType switch
            {
                BatchOperationType.Insert => await BulkInsertAsync(operation.Entities, cancellationToken),
                BatchOperationType.Update => await BulkUpdateAsync(operation.Entities, cancellationToken),
                BatchOperationType.Delete => await BulkDeleteAsync(operation.Entities.Select(e => e.Id), cancellationToken),
                BatchOperationType.Upsert => await BulkUpsertAsync(operation.Entities, cancellationToken),
                _ => throw new ArgumentException($"Unsupported batch operation type: {operation.OperationType}")
            };

            result.AffectedRows = affectedRows;
            result.IsSuccess = true;
            result.EndTime = DateTime.UtcNow;

            _logger.LogInformation("Completed batch operation {OperationType} for {EntityType}. " +
                                 "Affected rows: {AffectedRows}, Duration: {Duration}ms",
                operation.OperationType, typeof(T).Name, affectedRows, result.Duration.TotalMilliseconds);
        }
        catch (Exception ex)
        {
            result.IsSuccess = false;
            result.ErrorMessage = ex.Message;
            result.EndTime = DateTime.UtcNow;

            _logger.LogError(ex, "Batch operation {OperationType} for {EntityType} failed after {Duration}ms",
                operation.OperationType, typeof(T).Name, result.Duration.TotalMilliseconds);

            throw;
        }

        return result;
    }
}

/// <summary>
/// Represents a batch operation to be performed
/// </summary>
public class BatchOperation<T> where T : BaseEntity
{
    public BatchOperationType OperationType { get; set; }
    public IEnumerable<T> Entities { get; set; } = Enumerable.Empty<T>();
    public int BatchSize { get; set; } = 1000;
    public Dictionary<string, object> Options { get; set; } = new();
}

/// <summary>
/// Result of a batch operation
/// </summary>
public class BatchOperationResult
{
    public BatchOperationType OperationType { get; set; }
    public string EntityType { get; set; } = string.Empty;
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public TimeSpan Duration => EndTime - StartTime;
    public int AffectedRows { get; set; }
    public bool IsSuccess { get; set; }
    public string? ErrorMessage { get; set; }
}

/// <summary>
/// Types of batch operations
/// </summary>
public enum BatchOperationType
{
    Insert,
    Update,
    Delete,
    Upsert
}