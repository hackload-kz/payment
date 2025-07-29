using PaymentGateway.Core.Entities;
using PaymentGateway.Core.Repositories;

namespace PaymentGateway.Core.Interfaces;

public interface IUnitOfWork : IDisposable
{
    // Repository access
    IPaymentRepository Payments { get; }
    ITeamRepository Teams { get; }
    ICustomerRepository Customers { get; }
    ITransactionRepository Transactions { get; }
    IRepository<PaymentMethodInfo> PaymentMethods { get; }
    
    // Transaction management
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
    Task<int> SaveChangesAsync(string userId, CancellationToken cancellationToken = default);
    
    // Transaction control
    Task BeginTransactionAsync(CancellationToken cancellationToken = default);
    Task CommitTransactionAsync(CancellationToken cancellationToken = default);
    Task RollbackTransactionAsync(CancellationToken cancellationToken = default);
    
    // Bulk operations
    Task<int> BulkSaveChangesAsync(CancellationToken cancellationToken = default);
    
    // State management
    void DetachAllEntities();
    Task ReloadEntityAsync<TEntity>(TEntity entity, CancellationToken cancellationToken = default) where TEntity : BaseEntity;
    
    // Query execution
    Task<TResult> ExecuteInTransactionAsync<TResult>(Func<Task<TResult>> operation, CancellationToken cancellationToken = default);
    Task ExecuteInTransactionAsync(Func<Task> operation, CancellationToken cancellationToken = default);
    
    // Performance monitoring
    Task<TimeSpan> MeasureExecutionTimeAsync(Func<Task> operation);
    Task<(TResult Result, TimeSpan ExecutionTime)> MeasureExecutionTimeAsync<TResult>(Func<Task<TResult>> operation);
}