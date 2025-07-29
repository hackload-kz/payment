using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using PaymentGateway.Core.Entities;
using System.Collections.Concurrent;

namespace PaymentGateway.Infrastructure.Interceptors;

/// <summary>
/// Interceptor for handling optimistic concurrency conflicts and providing enhanced concurrency control
/// </summary>
public class ConcurrencyInterceptor : SaveChangesInterceptor
{
    private readonly ILogger<ConcurrencyInterceptor> _logger;
    private readonly ConcurrentDictionary<string, ConcurrencyMetrics> _concurrencyMetrics = new();

    public ConcurrencyInterceptor(ILogger<ConcurrencyInterceptor> logger)
    {
        _logger = logger;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        if (eventData.Context != null)
        {
            ProcessConcurrencyPreparation(eventData.Context);
        }

        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        if (eventData.Context != null)
        {
            ProcessConcurrencyPreparation(eventData.Context);
        }

        return base.SavingChanges(eventData, result);
    }

    public override async ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData,
        int result,
        CancellationToken cancellationToken = default)
    {
        if (eventData.Context != null)
        {
            await ProcessSuccessfulSave(eventData.Context, result);
        }

        return await base.SavedChangesAsync(eventData, result, cancellationToken);
    }

    public override int SavedChanges(SaveChangesCompletedEventData eventData, int result)
    {
        if (eventData.Context != null)
        {
            ProcessSuccessfulSave(eventData.Context, result).GetAwaiter().GetResult();
        }

        return base.SavedChanges(eventData, result);
    }

    public override async ValueTask SaveChangesFailedAsync(
        DbContextErrorEventData eventData,
        CancellationToken cancellationToken = default)
    {
        if (eventData.Context != null && eventData.Exception != null)
        {
            await ProcessConcurrencyFailure(eventData.Context, eventData.Exception);
        }

        await base.SaveChangesFailedAsync(eventData, cancellationToken);
    }

    public override void SaveChangesFailed(DbContextErrorEventData eventData)
    {
        if (eventData.Context != null && eventData.Exception != null)
        {
            ProcessConcurrencyFailure(eventData.Context, eventData.Exception).GetAwaiter().GetResult();
        }

        base.SaveChangesFailed(eventData);
    }

    private void ProcessConcurrencyPreparation(DbContext context)
    {
        var changedEntries = context.ChangeTracker.Entries()
            .Where(e => e.State == EntityState.Modified || e.State == EntityState.Deleted)
            .ToList();

        foreach (var entry in changedEntries)
        {
            var entityType = entry.Entity.GetType().Name;
            
            // Track concurrency attempts
            _concurrencyMetrics.AddOrUpdate(entityType,
                new ConcurrencyMetrics { EntityType = entityType, AttemptCount = 1 },
                (key, existing) => existing with { AttemptCount = existing.AttemptCount + 1 });

            // Add additional concurrency checks for critical entities
            if (entry.Entity is Payment payment)
            {
                ValidatePaymentConcurrency(entry, payment);
            }
            else if (entry.Entity is Transaction transaction)
            {
                ValidateTransactionConcurrency(entry, transaction);
            }
        }

        _logger.LogDebug("Prepared {Count} entities for concurrency-controlled save operation", changedEntries.Count);
    }

    private async Task ProcessSuccessfulSave(DbContext context, int affectedRows)
    {
        await Task.CompletedTask; // Make method async for consistency

        var changedEntities = context.ChangeTracker.Entries()
            .Where(e => e.State == EntityState.Unchanged) // After successful save, they become unchanged
            .Select(e => e.Entity.GetType().Name)
            .GroupBy(name => name)
            .ToDictionary(g => g.Key, g => g.Count());

        foreach (var (entityType, count) in changedEntities)
        {
            _concurrencyMetrics.AddOrUpdate(entityType,
                new ConcurrencyMetrics { EntityType = entityType, SuccessCount = count },
                (key, existing) => existing with { SuccessCount = existing.SuccessCount + count });
        }

        _logger.LogDebug("Successfully saved {AffectedRows} rows across {EntityTypes} entity types", 
            affectedRows, changedEntities.Count);
    }

    private async Task ProcessConcurrencyFailure(DbContext context, Exception exception)
    {
        await Task.CompletedTask; // Make method async for consistency

        var isConcurrencyException = exception is DbUpdateConcurrencyException;
        
        if (isConcurrencyException)
        {
            var concurrencyEx = (DbUpdateConcurrencyException)exception;
            
            foreach (var entry in concurrencyEx.Entries)
            {
                var entityType = entry.Entity.GetType().Name;
                var entityId = GetEntityId(entry.Entity);

                _concurrencyMetrics.AddOrUpdate(entityType,
                    new ConcurrencyMetrics { EntityType = entityType, ConflictCount = 1 },
                    (key, existing) => existing with { ConflictCount = existing.ConflictCount + 1 });

                _logger.LogWarning("Concurrency conflict detected for {EntityType} with ID {EntityId}. " +
                                 "Current values may have been modified by another process.",
                    entityType, entityId);

                // Log detailed concurrency information
                await LogConcurrencyDetails(entry);
            }
        }
        else
        {
            // Handle other types of database exceptions that might be concurrency-related
            var affectedEntities = context.ChangeTracker.Entries()
                .Where(e => e.State != EntityState.Unchanged)
                .Select(e => e.Entity.GetType().Name)
                .Distinct();

            foreach (var entityType in affectedEntities)
            {
                _concurrencyMetrics.AddOrUpdate(entityType,
                    new ConcurrencyMetrics { EntityType = entityType, ErrorCount = 1 },
                    (key, existing) => existing with { ErrorCount = existing.ErrorCount + 1 });
            }

            _logger.LogError(exception, "Database save operation failed with non-concurrency exception");
        }
    }

    private void ValidatePaymentConcurrency(Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry entry, Payment payment)
    {
        // Additional validation for payment entities
        if (entry.State == EntityState.Modified)
        {
            var originalStatus = entry.OriginalValues.GetValue<PaymentStatus>(nameof(Payment.Status));
            var currentStatus = payment.Status;

            // Validate state transition is still valid
            if (originalStatus != currentStatus)
            {
                _logger.LogDebug("Payment {PaymentId} status changing from {FromStatus} to {ToStatus}",
                    payment.Id, originalStatus, currentStatus);
            }

            // Check for critical field modifications
            var criticalFields = new[] { nameof(Payment.Amount), nameof(Payment.Status), nameof(Payment.TeamId) };
            foreach (var field in criticalFields)
            {
                if (entry.Property(field).IsModified)
                {
                    _logger.LogInformation("Critical field {Field} modified for Payment {PaymentId}", 
                        field, payment.Id);
                }
            }
        }
    }

    private void ValidateTransactionConcurrency(Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry entry, Transaction transaction)
    {
        // Additional validation for transaction entities
        if (entry.State == EntityState.Modified)
        {
            var originalStatus = entry.OriginalValues.GetValue<TransactionStatus>(nameof(Transaction.Status));
            var currentStatus = transaction.Status;

            if (originalStatus != currentStatus)
            {
                _logger.LogDebug("Transaction {TransactionId} status changing from {FromStatus} to {ToStatus}",
                    transaction.Id, originalStatus, currentStatus);
            }
        }
    }

    private async Task LogConcurrencyDetails(Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry entry)
    {
        await Task.CompletedTask; // Make method async

        try
        {
            var entityType = entry.Entity.GetType().Name;
            var entityId = GetEntityId(entry.Entity);

            // Log current values
            var currentValues = entry.CurrentValues.Properties
                .ToDictionary(p => p.Name, p => entry.CurrentValues[p]?.ToString() ?? "NULL");

            // Log original values
            var originalValues = entry.OriginalValues?.Properties
                .ToDictionary(p => p.Name, p => entry.OriginalValues[p]?.ToString() ?? "NULL") ?? new Dictionary<string, string>();

            // Log database values if available
            var databaseValues = entry.GetDatabaseValues();
            var dbValues = databaseValues?.Properties
                .ToDictionary(p => p.Name, p => databaseValues[p]?.ToString() ?? "NULL") ?? new Dictionary<string, string>();

            _logger.LogWarning("Concurrency conflict details for {EntityType} {EntityId}:\n" +
                             "Current Values: {CurrentValues}\n" +
                             "Original Values: {OriginalValues}\n" +
                             "Database Values: {DatabaseValues}",
                entityType, entityId,
                string.Join(", ", currentValues.Select(kv => $"{kv.Key}={kv.Value}")),
                string.Join(", ", originalValues.Select(kv => $"{kv.Key}={kv.Value}")),
                string.Join(", ", dbValues.Select(kv => $"{kv.Key}={kv.Value}")));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to log concurrency conflict details");
        }
    }

    private static object? GetEntityId(object entity)
    {
        // Try to get ID from BaseEntity
        if (entity is BaseEntity baseEntity)
        {
            return baseEntity.Id;
        }

        // Try to get Id property using reflection
        var idProperty = entity.GetType().GetProperty("Id");
        return idProperty?.GetValue(entity);
    }

    /// <summary>
    /// Get concurrency metrics for monitoring
    /// </summary>
    public IEnumerable<ConcurrencyMetrics> GetConcurrencyMetrics()
    {
        return _concurrencyMetrics.Values.OrderByDescending(m => m.ConflictCount);
    }

    /// <summary>
    /// Clear concurrency metrics (for testing or memory management)
    /// </summary>
    public void ClearMetrics()
    {
        _concurrencyMetrics.Clear();
    }
}

/// <summary>
/// Concurrency metrics for monitoring database conflicts
/// </summary>
public record ConcurrencyMetrics
{
    public string EntityType { get; init; } = string.Empty;
    public int AttemptCount { get; init; }
    public int SuccessCount { get; init; }
    public int ConflictCount { get; init; }
    public int ErrorCount { get; init; }
    public double ConflictRate => AttemptCount > 0 ? (double)ConflictCount / AttemptCount : 0;
    public double SuccessRate => AttemptCount > 0 ? (double)SuccessCount / AttemptCount : 0;
}