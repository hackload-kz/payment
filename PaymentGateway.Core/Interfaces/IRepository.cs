using PaymentGateway.Core.Entities;
using System.Linq.Expressions;

namespace PaymentGateway.Core.Interfaces;

public interface IRepository<TEntity> where TEntity : BaseEntity
{
    // Basic CRUD operations
    Task<TEntity?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<TEntity?> GetByIdAsync(Guid id, bool includeDeleted, CancellationToken cancellationToken = default);
    
    Task<IEnumerable<TEntity>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<IEnumerable<TEntity>> GetAllAsync(bool includeDeleted, CancellationToken cancellationToken = default);
    
    Task<IEnumerable<TEntity>> FindAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default);
    Task<IEnumerable<TEntity>> FindAsync(Expression<Func<TEntity, bool>> predicate, bool includeDeleted, CancellationToken cancellationToken = default);
    
    Task<TEntity?> FirstOrDefaultAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default);
    Task<TEntity?> FirstOrDefaultAsync(Expression<Func<TEntity, bool>> predicate, bool includeDeleted, CancellationToken cancellationToken = default);
    
    // Async CRUD operations
    Task AddAsync(TEntity entity, CancellationToken cancellationToken = default);
    Task AddRangeAsync(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default);
    
    void Update(TEntity entity);
    void UpdateRange(IEnumerable<TEntity> entities);
    
    void Remove(TEntity entity);
    void RemoveRange(IEnumerable<TEntity> entities);
    
    // Soft delete operations
    void SoftDelete(TEntity entity, string? deletedBy = null);
    void SoftDeleteRange(IEnumerable<TEntity> entities, string? deletedBy = null);
    
    void Restore(TEntity entity, string? restoredBy = null);
    void RestoreRange(IEnumerable<TEntity> entities, string? restoredBy = null);
    
    // Count operations
    Task<int> CountAsync(CancellationToken cancellationToken = default);
    Task<int> CountAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default);
    Task<int> CountAsync(Expression<Func<TEntity, bool>> predicate, bool includeDeleted, CancellationToken cancellationToken = default);
    
    // Existence checks
    Task<bool> ExistsAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default);
    Task<bool> ExistsAsync(Expression<Func<TEntity, bool>> predicate, bool includeDeleted, CancellationToken cancellationToken = default);
    
    // Paging support
    Task<(IEnumerable<TEntity> Items, int TotalCount)> GetPagedAsync(
        int pageNumber, 
        int pageSize, 
        Expression<Func<TEntity, bool>>? predicate = null,
        Expression<Func<TEntity, object>>? orderBy = null,
        bool ascending = true,
        bool includeDeleted = false,
        CancellationToken cancellationToken = default);
    
    // Advanced querying
    IQueryable<TEntity> Query();
    IQueryable<TEntity> Query(bool includeDeleted);
    IQueryable<TEntity> QueryWithIncludes(params Expression<Func<TEntity, object>>[] includes);
    IQueryable<TEntity> QueryWithIncludes(bool includeDeleted, params Expression<Func<TEntity, object>>[] includes);
    
    // Bulk operations
    Task<int> BulkInsertAsync(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default);
    Task<int> BulkUpdateAsync(IEnumerable<TEntity> entities, CancellationToken cancellationToken = default);
    Task<int> BulkDeleteAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default);
    Task<int> BulkSoftDeleteAsync(Expression<Func<TEntity, bool>> predicate, string? deletedBy = null, CancellationToken cancellationToken = default);
    
    // Performance operations
    Task<TEntity?> GetWithCacheAsync(Guid id, TimeSpan? cacheDuration = null, CancellationToken cancellationToken = default);
    Task InvalidateCacheAsync(Guid id, CancellationToken cancellationToken = default);
    Task InvalidateCacheAsync(Expression<Func<TEntity, bool>> predicate, CancellationToken cancellationToken = default);
}

public interface IRepository<TEntity, TKey> where TEntity : class
{
    Task<TEntity?> GetByIdAsync(TKey id, CancellationToken cancellationToken = default);
    Task<IEnumerable<TEntity>> GetAllAsync(CancellationToken cancellationToken = default);
    Task AddAsync(TEntity entity, CancellationToken cancellationToken = default);
    void Update(TEntity entity);
    void Remove(TEntity entity);
    Task<bool> ExistsAsync(TKey id, CancellationToken cancellationToken = default);
}