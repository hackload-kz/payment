using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using PaymentGateway.Core.Data;
using PaymentGateway.Core.Entities;
using PaymentGateway.Core.Interfaces;
using System.Linq.Expressions;

namespace PaymentGateway.Core.Repositories;

public class Repository<TEntity> : IRepository<TEntity> where TEntity : BaseEntity
{
    protected readonly PaymentGatewayDbContext _context;
    protected readonly DbSet<TEntity> _dbSet;
    protected readonly ILogger<Repository<TEntity>> _logger;
    protected readonly IMemoryCache _cache;
    private readonly string _cacheKeyPrefix;
    
    public Repository(
        PaymentGatewayDbContext context, 
        ILogger<Repository<TEntity>> logger,
        IMemoryCache cache)
    {
        _context = context;
        _dbSet = context.Set<TEntity>();
        _logger = logger;
        _cache = cache;
        _cacheKeyPrefix = typeof(TEntity).Name;
    }

    #region Basic CRUD Operations

    public virtual async Task<TEntity?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await GetByIdAsync(id, includeDeleted: false, cancellationToken);
    }

    public virtual async Task<TEntity?> GetByIdAsync(Guid id, bool includeDeleted, CancellationToken cancellationToken = default)
    {
        try
        {
            var query = includeDeleted ? _dbSet.IgnoreQueryFilters() : _dbSet.AsQueryable();
            var entity = await query.FirstOrDefaultAsync(e => e.Id == id, cancellationToken);
            
            if (entity != null && !includeDeleted && entity.IsDeleted)
                return null;
                
            return entity;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting {EntityType} by ID {Id}", typeof(TEntity).Name, id);
            throw;
        }
    }

    public virtual async Task<IEnumerable<TEntity>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return await GetAllAsync(includeDeleted: false, cancellationToken);
    }

    public virtual async Task<IEnumerable<TEntity>> GetAllAsync(bool includeDeleted, CancellationToken cancellationToken = default)
    {
        try
        {
            var query = includeDeleted ? _dbSet.IgnoreQueryFilters() : _dbSet.AsQueryable();
            return await query.ToListAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting all {EntityType}", typeof(TEntity).Name);
            throw;
        }
    }

    public virtual async Task<IEnumerable<TEntity>> FindAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default)
    {
        return await FindAsync(predicate, includeDeleted: false, cancellationToken);
    }

    public virtual async Task<IEnumerable<TEntity>> FindAsync(Expression<Func<TEntity, bool>> predicate, bool includeDeleted, CancellationToken cancellationToken = default)
    {
        try
        {
            var query = includeDeleted ? _dbSet.IgnoreQueryFilters() : _dbSet.AsQueryable();
            return await query.Where(predicate).ToListAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error finding {EntityType} with predicate", typeof(TEntity).Name);
            throw;
        }
    }

    public virtual async Task<TEntity?> FirstOrDefaultAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default)
    {
        return await FirstOrDefaultAsync(predicate, includeDeleted: false, cancellationToken);
    }

    public virtual async Task<TEntity?> FirstOrDefaultAsync(Expression<Func<TEntity, bool>> predicate, bool includeDeleted, CancellationToken cancellationToken = default)
    {
        try
        {
            var query = includeDeleted ? _dbSet.IgnoreQueryFilters() : _dbSet.AsQueryable();
            return await query.FirstOrDefaultAsync(predicate, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting first {EntityType} with predicate", typeof(TEntity).Name);
            throw;
        }
    }

    #endregion

    #region Async CRUD Operations

    public virtual async Task AddAsync(TEntity entity, CancellationToken cancellationToken = default)
    {
        try
        {
            entity.MarkAsCreated();
            await _dbSet.AddAsync(entity, cancellationToken);
            _logger.LogDebug("Added {EntityType} with ID {Id}", typeof(TEntity).Name, entity.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error adding {EntityType}", typeof(TEntity).Name);
            throw;
        }
    }

    public virtual async Task AddRangeAsync(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default)
    {
        try
        {
            var entityList = entities.ToList();
            foreach (var entity in entityList)
            {
                entity.MarkAsCreated();
            }
            
            await _dbSet.AddRangeAsync(entityList, cancellationToken);
            _logger.LogDebug("Added {Count} {EntityType} entities", entityList.Count, typeof(TEntity).Name);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error adding range of {EntityType}", typeof(TEntity).Name);
            throw;
        }
    }

    public virtual void Update(TEntity entity)
    {
        try
        {
            entity.MarkAsUpdated();
            _dbSet.Update(entity);
            _logger.LogDebug("Updated {EntityType} with ID {Id}", typeof(TEntity).Name, entity.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating {EntityType} with ID {Id}", typeof(TEntity).Name, entity.Id);
            throw;
        }
    }

    public virtual void UpdateRange(IEnumerable<TEntity> entities)
    {
        try
        {
            var entityList = entities.ToList();
            foreach (var entity in entityList)
            {
                entity.MarkAsUpdated();
            }
            
            _dbSet.UpdateRange(entityList);
            _logger.LogDebug("Updated {Count} {EntityType} entities", entityList.Count, typeof(TEntity).Name);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating range of {EntityType}", typeof(TEntity).Name);
            throw;
        }
    }

    public virtual void Remove(TEntity entity)
    {
        try
        {
            _dbSet.Remove(entity);
            _logger.LogDebug("Removed {EntityType} with ID {Id}", typeof(TEntity).Name, entity.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error removing {EntityType} with ID {Id}", typeof(TEntity).Name, entity.Id);
            throw;
        }
    }

    public virtual void RemoveRange(IEnumerable<TEntity> entities)
    {
        try
        {
            var entityList = entities.ToList();
            _dbSet.RemoveRange(entityList);
            _logger.LogDebug("Removed {Count} {EntityType} entities", entityList.Count, typeof(TEntity).Name);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error removing range of {EntityType}", typeof(TEntity).Name);
            throw;
        }
    }

    #endregion

    #region Soft Delete Operations

    public virtual void SoftDelete(TEntity entity, string? deletedBy = null)
    {
        try
        {
            entity.MarkAsDeleted(deletedBy);
            _dbSet.Update(entity);
            _logger.LogDebug("Soft deleted {EntityType} with ID {Id}", typeof(TEntity).Name, entity.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error soft deleting {EntityType} with ID {Id}", typeof(TEntity).Name, entity.Id);
            throw;
        }
    }

    public virtual void SoftDeleteRange(IEnumerable<TEntity> entities, string? deletedBy = null)
    {
        try
        {
            var entityList = entities.ToList();
            foreach (var entity in entityList)
            {
                entity.MarkAsDeleted(deletedBy);
            }
            
            _dbSet.UpdateRange(entityList);
            _logger.LogDebug("Soft deleted {Count} {EntityType} entities", entityList.Count, typeof(TEntity).Name);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error soft deleting range of {EntityType}", typeof(TEntity).Name);
            throw;
        }
    }

    public virtual void Restore(TEntity entity, string? restoredBy = null)
    {
        try
        {
            entity.MarkAsRestored(restoredBy);
            _dbSet.Update(entity);
            _logger.LogDebug("Restored {EntityType} with ID {Id}", typeof(TEntity).Name, entity.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error restoring {EntityType} with ID {Id}", typeof(TEntity).Name, entity.Id);
            throw;
        }
    }

    public virtual void RestoreRange(IEnumerable<TEntity> entities, string? restoredBy = null)
    {
        try
        {
            var entityList = entities.ToList();
            foreach (var entity in entityList)
            {
                entity.MarkAsRestored(restoredBy);
            }
            
            _dbSet.UpdateRange(entityList);
            _logger.LogDebug("Restored {Count} {EntityType} entities", entityList.Count, typeof(TEntity).Name);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error restoring range of {EntityType}", typeof(TEntity).Name);
            throw;
        }
    }

    #endregion

    #region Count Operations

    public virtual async Task<int> CountAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await _dbSet.CountAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error counting {EntityType}", typeof(TEntity).Name);
            throw;
        }
    }

    public virtual async Task<int> CountAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default)
    {
        return await CountAsync(predicate, includeDeleted: false, cancellationToken);
    }

    public virtual async Task<int> CountAsync(Expression<Func<TEntity, bool>> predicate, bool includeDeleted, CancellationToken cancellationToken = default)
    {
        try
        {
            var query = includeDeleted ? _dbSet.IgnoreQueryFilters() : _dbSet.AsQueryable();
            return await query.CountAsync(predicate, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error counting {EntityType} with predicate", typeof(TEntity).Name);
            throw;
        }
    }

    #endregion

    #region Existence Checks

    public virtual async Task<bool> ExistsAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default)
    {
        return await ExistsAsync(predicate, includeDeleted: false, cancellationToken);
    }

    public virtual async Task<bool> ExistsAsync(Expression<Func<TEntity, bool>> predicate, bool includeDeleted, CancellationToken cancellationToken = default)
    {
        try
        {
            var query = includeDeleted ? _dbSet.IgnoreQueryFilters() : _dbSet.AsQueryable();
            return await query.AnyAsync(predicate, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking existence of {EntityType} with predicate", typeof(TEntity).Name);
            throw;
        }
    }

    #endregion

    #region Paging Support

    public virtual async Task<(IEnumerable<TEntity> Items, int TotalCount)> GetPagedAsync(
        int pageNumber, 
        int pageSize, 
        Expression<Func<TEntity, bool>>? predicate = null,
        Expression<Func<TEntity, object>>? orderBy = null,
        bool ascending = true,
        bool includeDeleted = false,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var query = includeDeleted ? _dbSet.IgnoreQueryFilters() : _dbSet.AsQueryable();
            
            if (predicate != null)
                query = query.Where(predicate);
            
            var totalCount = await query.CountAsync(cancellationToken);
            
            if (orderBy != null)
            {
                query = ascending ? query.OrderBy(orderBy) : query.OrderByDescending(orderBy);
            }
            else
            {
                query = query.OrderByDescending(e => e.CreatedAt);
            }
            
            var items = await query
                .Skip((pageNumber - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync(cancellationToken);
            
            return (items, totalCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting paged {EntityType} data", typeof(TEntity).Name);
            throw;
        }
    }

    #endregion

    #region Advanced Querying

    public virtual IQueryable<TEntity> Query()
    {
        return _dbSet.AsQueryable();
    }

    public virtual IQueryable<TEntity> Query(bool includeDeleted)
    {
        return includeDeleted ? _dbSet.IgnoreQueryFilters() : _dbSet.AsQueryable();
    }

    public virtual IQueryable<TEntity> QueryWithIncludes(params Expression<Func<TEntity, object>>[] includes)
    {
        return QueryWithIncludes(includeDeleted: false, includes);
    }

    public virtual IQueryable<TEntity> QueryWithIncludes(bool includeDeleted, params Expression<Func<TEntity, object>>[] includes)
    {
        var query = includeDeleted ? _dbSet.IgnoreQueryFilters() : _dbSet.AsQueryable();
        
        return includes.Aggregate(query, (current, include) => current.Include(include));
    }

    #endregion

    #region Bulk Operations

    public virtual async Task<int> BulkInsertAsync(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default)
    {
        try
        {
            var entityList = entities.ToList();
            foreach (var entity in entityList)
            {
                entity.MarkAsCreated();
            }
            
            await _dbSet.AddRangeAsync(entityList, cancellationToken);
            var result = await _context.SaveChangesAsync(cancellationToken);
            
            _logger.LogInformation("Bulk inserted {Count} {EntityType} entities", entityList.Count, typeof(TEntity).Name);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error bulk inserting {EntityType}", typeof(TEntity).Name);
            throw;
        }
    }

    public virtual async Task<int> BulkUpdateAsync(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default)
    {
        try
        {
            var entityList = entities.ToList();
            foreach (var entity in entityList)
            {
                entity.MarkAsUpdated();
            }
            
            _dbSet.UpdateRange(entityList);
            var result = await _context.SaveChangesAsync(cancellationToken);
            
            _logger.LogInformation("Bulk updated {Count} {EntityType} entities", entityList.Count, typeof(TEntity).Name);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error bulk updating {EntityType}", typeof(TEntity).Name);
            throw;
        }
    }

    public virtual async Task<int> BulkDeleteAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default)
    {
        try
        {
            var entities = await _dbSet.Where(predicate).ToListAsync(cancellationToken);
            _dbSet.RemoveRange(entities);
            var result = await _context.SaveChangesAsync(cancellationToken);
            
            _logger.LogInformation("Bulk deleted {Count} {EntityType} entities", entities.Count, typeof(TEntity).Name);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error bulk deleting {EntityType}", typeof(TEntity).Name);
            throw;
        }
    }

    public virtual async Task<int> BulkSoftDeleteAsync(Expression<Func<TEntity, bool>> predicate, string? deletedBy = null, CancellationToken cancellationToken = default)
    {
        try
        {
            var entities = await _dbSet.Where(predicate).ToListAsync(cancellationToken);
            
            foreach (var entity in entities)
            {
                entity.MarkAsDeleted(deletedBy);
            }
            
            _dbSet.UpdateRange(entities);
            var result = await _context.SaveChangesAsync(cancellationToken);
            
            _logger.LogInformation("Bulk soft deleted {Count} {EntityType} entities", entities.Count, typeof(TEntity).Name);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error bulk soft deleting {EntityType}", typeof(TEntity).Name);
            throw;
        }
    }

    #endregion

    #region Performance Operations with Caching

    public virtual async Task<TEntity?> GetWithCacheAsync(Guid id, TimeSpan? cacheDuration = null, CancellationToken cancellationToken = default)
    {
        var cacheKey = $"{_cacheKeyPrefix}_{id}";
        
        if (_cache.TryGetValue(cacheKey, out TEntity? cachedEntity))
        {
            _logger.LogDebug("Cache hit for {EntityType} with ID {Id}", typeof(TEntity).Name, id);
            return cachedEntity;
        }
        
        var entity = await GetByIdAsync(id, cancellationToken);
        
        if (entity != null)
        {
            var duration = cacheDuration ?? TimeSpan.FromMinutes(15);
            _cache.Set(cacheKey, entity, duration);
            _logger.LogDebug("Cached {EntityType} with ID {Id} for {Duration}", typeof(TEntity).Name, id, duration);
        }
        
        return entity;
    }

    public virtual async Task InvalidateCacheAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var cacheKey = $"{_cacheKeyPrefix}_{id}";
        _cache.Remove(cacheKey);
        _logger.LogDebug("Invalidated cache for {EntityType} with ID {Id}", typeof(TEntity).Name, id);
        await Task.CompletedTask;
    }

    public virtual async Task InvalidateCacheAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default)
    {
        // For bulk cache invalidation, we'd need to implement a more sophisticated caching strategy
        // For now, we'll log the invalidation request
        _logger.LogDebug("Bulk cache invalidation requested for {EntityType}", typeof(TEntity).Name);
        await Task.CompletedTask;
    }

    #endregion
}