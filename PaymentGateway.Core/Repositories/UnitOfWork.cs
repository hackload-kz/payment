using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PaymentGateway.Core.Data;
using PaymentGateway.Core.Entities;
using PaymentGateway.Core.Interfaces;
using PaymentGateway.Core.Services;
using System.Diagnostics;

namespace PaymentGateway.Core.Repositories;

public class UnitOfWork : IUnitOfWork
{
    private readonly PaymentGatewayDbContext _context;
    private readonly ILogger<UnitOfWork> _logger;
    private readonly IMemoryCache _cache;
    private IDbContextTransaction? _transaction;
    private bool _disposed = false;
    
    // Repository instances
    private IPaymentRepository? _payments;
    private ITeamRepository? _teams;
    private ICustomerRepository? _customers;
    private ITransactionRepository? _transactions;
    private IRepository<PaymentMethodInfo>? _paymentMethods;
    private IPaymentStateTransitionRepository? _stateTransitions;
    
    public UnitOfWork(
        PaymentGatewayDbContext context,
        ILogger<UnitOfWork> logger,
        IMemoryCache cache)
    {
        _context = context;
        _logger = logger;
        _cache = cache;
    }
    
    // Repository properties with lazy initialization
    public IPaymentRepository Payments
    {
        get
        {
            _payments ??= new PaymentRepository(_context, 
                NullLogger<PaymentRepository>.Instance, 
                _cache);
            return _payments;
        }
    }
    
    public ITeamRepository Teams
    {
        get
        {
            _teams ??= new TeamRepository(_context, 
                NullLogger<TeamRepository>.Instance, 
                _cache);
            return _teams;
        }
    }
    
    public ICustomerRepository Customers
    {
        get
        {
            _customers ??= new CustomerRepository(_context, 
                NullLogger<CustomerRepository>.Instance, 
                _cache);
            return _customers;
        }
    }
    
    public ITransactionRepository Transactions
    {
        get
        {
            _transactions ??= new TransactionRepository(_context, 
                NullLogger<TransactionRepository>.Instance, 
                _cache);
            return _transactions;
        }
    }
    
    public IRepository<PaymentMethodInfo> PaymentMethods
    {
        get
        {
            _paymentMethods ??= new Repository<PaymentMethodInfo>(_context, 
                NullLogger<Repository<PaymentMethodInfo>>.Instance, 
                _cache);
            return _paymentMethods;
        }
    }
    
    public IPaymentStateTransitionRepository StateTransitions
    {
        get
        {
            _stateTransitions ??= new PaymentStateTransitionRepository(_context, 
                NullLogger<PaymentStateTransitionRepository>.Instance, 
                _cache);
            return _stateTransitions;
        }
    }
    
    // Transaction management
    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        return await SaveChangesAsync(userId: null, cancellationToken);
    }
    
    public async Task<int> SaveChangesAsync(string? userId, CancellationToken cancellationToken = default)
    {
        try
        {
            var stopwatch = Stopwatch.StartNew();
            
            // Set user context for audit fields if provided
            if (!string.IsNullOrEmpty(userId))
            {
                SetUserContextForAuditFields(userId);
            }
            
            var result = await _context.SaveChangesAsync(cancellationToken);
            
            stopwatch.Stop();
            _logger.LogDebug("SaveChanges completed in {ElapsedMs}ms, affected {RecordCount} records", 
                stopwatch.ElapsedMilliseconds, result);
            
            return result;
        }
        catch (DbUpdateConcurrencyException ex)
        {
            _logger.LogError(ex, "Concurrency conflict occurred during SaveChanges");
            throw new InvalidOperationException("The record was modified by another user. Please refresh and try again.", ex);
        }
        catch (DbUpdateException ex)
        {
            _logger.LogError(ex, "Database update error occurred during SaveChanges");
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error occurred during SaveChanges");
            throw;
        }
    }
    
    // Transaction control
    public async Task BeginTransactionAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            if (_transaction != null)
            {
                throw new InvalidOperationException("Transaction is already active");
            }
            
            _transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
            _logger.LogDebug("Database transaction started");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error starting database transaction");
            throw;
        }
    }
    
    public async Task CommitTransactionAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            if (_transaction == null)
            {
                throw new InvalidOperationException("No active transaction to commit");
            }
            
            await _transaction.CommitAsync(cancellationToken);
            _logger.LogDebug("Database transaction committed successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error committing database transaction");
            await RollbackTransactionAsync(cancellationToken);
            throw;
        }
        finally
        {
            await DisposeTransactionAsync();
        }
    }
    
    public async Task RollbackTransactionAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            if (_transaction != null)
            {
                await _transaction.RollbackAsync(cancellationToken);
                _logger.LogDebug("Database transaction rolled back");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error rolling back database transaction");
            throw;
        }
        finally
        {
            await DisposeTransactionAsync();
        }
    }
    
    // Bulk operations
    public async Task<int> BulkSaveChangesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var stopwatch = Stopwatch.StartNew();
            
            // Disable change tracking for better performance
            _context.ChangeTracker.AutoDetectChangesEnabled = false;
            
            var result = await _context.SaveChangesAsync(cancellationToken);
            
            // Re-enable change tracking
            _context.ChangeTracker.AutoDetectChangesEnabled = true;
            
            stopwatch.Stop();
            _logger.LogInformation("BulkSaveChanges completed in {ElapsedMs}ms, affected {RecordCount} records", 
                stopwatch.ElapsedMilliseconds, result);
            
            return result;
        }
        catch (Exception ex)
        {
            // Re-enable change tracking in case of error
            _context.ChangeTracker.AutoDetectChangesEnabled = true;
            _logger.LogError(ex, "Error during BulkSaveChanges");
            throw;
        }
    }
    
    // State management
    public void DetachAllEntities()
    {
        try
        {
            var entries = _context.ChangeTracker.Entries().ToList();
            foreach (var entry in entries)
            {
                entry.State = EntityState.Detached;
            }
            
            _logger.LogDebug("Detached {EntryCount} entities from change tracker", entries.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error detaching entities");
            throw;
        }
    }
    
    public async Task ReloadEntityAsync<TEntity>(TEntity entity, CancellationToken cancellationToken = default) where TEntity : BaseEntity
    {
        try
        {
            var entry = _context.Entry(entity);
            if (entry.State != EntityState.Detached)
            {
                await entry.ReloadAsync(cancellationToken);
                _logger.LogDebug("Reloaded entity {EntityType} with ID {Id}", typeof(TEntity).Name, entity.Id);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reloading entity {EntityType} with ID {Id}", typeof(TEntity).Name, entity.Id);
            throw;
        }
    }
    
    // Query execution with transaction management
    public async Task<TResult> ExecuteInTransactionAsync<TResult>(Func<Task<TResult>> operation, CancellationToken cancellationToken = default)
    {
        var wasTransactionStartedHere = _transaction == null;
        
        try
        {
            if (wasTransactionStartedHere)
            {
                await BeginTransactionAsync(cancellationToken);
            }
            
            var result = await operation();
            
            if (wasTransactionStartedHere)
            {
                await CommitTransactionAsync(cancellationToken);
            }
            
            return result;
        }
        catch (Exception ex)
        {
            if (wasTransactionStartedHere)
            {
                await RollbackTransactionAsync(cancellationToken);
            }
            
            _logger.LogError(ex, "Error executing operation in transaction");
            throw;
        }
    }
    
    public async Task ExecuteInTransactionAsync(Func<Task> operation, CancellationToken cancellationToken = default)
    {
        await ExecuteInTransactionAsync(async () =>
        {
            await operation();
            return Task.CompletedTask;
        }, cancellationToken);
    }
    
    // Performance monitoring
    public async Task<TimeSpan> MeasureExecutionTimeAsync(Func<Task> operation)
    {
        var stopwatch = Stopwatch.StartNew();
        
        try
        {
            await operation();
        }
        finally
        {
            stopwatch.Stop();
        }
        
        return stopwatch.Elapsed;
    }
    
    public async Task<(TResult Result, TimeSpan ExecutionTime)> MeasureExecutionTimeAsync<TResult>(Func<Task<TResult>> operation)
    {
        var stopwatch = Stopwatch.StartNew();
        
        try
        {
            var result = await operation();
            return (result, stopwatch.Elapsed);
        }
        finally
        {
            stopwatch.Stop();
        }
    }
    
    // Private helper methods
    private void SetUserContextForAuditFields(string userId)
    {
        var entries = _context.ChangeTracker.Entries<BaseEntity>()
            .Where(e => e.State == EntityState.Added || e.State == EntityState.Modified);
        
        foreach (var entry in entries)
        {
            if (entry.State == EntityState.Added)
            {
                entry.Entity.CreatedBy = userId;
                entry.Entity.CreatedAt = DateTime.UtcNow;
            }
            
            if (entry.State == EntityState.Modified)
            {
                entry.Entity.UpdatedBy = userId;
                entry.Entity.UpdatedAt = DateTime.UtcNow;
            }
        }
    }
    
    private async Task DisposeTransactionAsync()
    {
        if (_transaction != null)
        {
            await _transaction.DisposeAsync();
            _transaction = null;
        }
    }
    
    // IDisposable implementation
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }
    
    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed && disposing)
        {
            _transaction?.Dispose();
            _context.Dispose();
            _disposed = true;
            
            _logger.LogDebug("UnitOfWork disposed");
        }
    }
}