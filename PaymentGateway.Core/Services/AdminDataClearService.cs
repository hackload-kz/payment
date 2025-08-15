using Microsoft.Extensions.Logging;
using PaymentGateway.Core.Repositories;
using PaymentGateway.Core.Interfaces;

namespace PaymentGateway.Core.Services;

public interface IAdminDataClearService
{
    Task<AdminDataClearResult> ClearDatabaseAsync(CancellationToken cancellationToken = default);
}

public class AdminDataClearResult
{
    public int DeletedPayments { get; set; }
    public int DeletedTransactions { get; set; }
    public int DeletedOrders { get; set; } // This will be part of payments (OrderId field)
    public DateTime ClearTimestamp { get; set; }
    public TimeSpan OperationDuration { get; set; }
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
}

public class AdminDataClearService : IAdminDataClearService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<AdminDataClearService> _logger;

    public AdminDataClearService(
        IUnitOfWork unitOfWork,
        ILogger<AdminDataClearService> logger)
    {
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<AdminDataClearResult> ClearDatabaseAsync(CancellationToken cancellationToken = default)
    {
        var startTime = DateTime.UtcNow;
        var result = new AdminDataClearResult
        {
            ClearTimestamp = startTime,
            Success = false
        };

        try
        {
            _logger.LogWarning("Starting admin database clear operation at {Timestamp}", startTime);

            await _unitOfWork.BeginTransactionAsync(cancellationToken);

            try
            {
                // Get counts before deletion for statistics
                var payments = await _unitOfWork.Payments.GetAllAsync(cancellationToken);
                var paymentList = payments.ToList();
                result.DeletedPayments = paymentList.Count;

                // Count orders (unique OrderId values across all payments)
                result.DeletedOrders = paymentList
                    .Where(p => !string.IsNullOrEmpty(p.OrderId))
                    .Select(p => p.OrderId)
                    .Distinct()
                    .Count();

                var transactions = await _unitOfWork.Transactions.GetAllAsync(cancellationToken);
                result.DeletedTransactions = transactions.Count();

                _logger.LogInformation("About to delete: {PaymentCount} payments, {TransactionCount} transactions, {OrderCount} unique orders",
                    result.DeletedPayments, result.DeletedTransactions, result.DeletedOrders);

                // Delete transactions first (they reference payments)
                await _unitOfWork.Transactions.BulkDeleteAsync(t => !t.IsDeleted, cancellationToken);

                // Delete payments (contains orders via OrderId field)
                await _unitOfWork.Payments.BulkDeleteAsync(p => !p.IsDeleted, cancellationToken);

                await _unitOfWork.SaveChangesAsync(cancellationToken);
                await _unitOfWork.CommitTransactionAsync(cancellationToken);

                result.Success = true;
                result.OperationDuration = DateTime.UtcNow - startTime;

                _logger.LogWarning("Admin database clear operation completed successfully. Deleted: {PaymentCount} payments, {TransactionCount} transactions, {OrderCount} unique orders in {Duration}ms",
                    result.DeletedPayments, result.DeletedTransactions, result.DeletedOrders, result.OperationDuration.TotalMilliseconds);
            }
            catch (Exception ex)
            {
                await _unitOfWork.RollbackTransactionAsync(cancellationToken);
                throw;
            }
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.ErrorMessage = ex.Message;
            result.OperationDuration = DateTime.UtcNow - startTime;

            _logger.LogError(ex, "Admin database clear operation failed after {Duration}ms",
                result.OperationDuration.TotalMilliseconds);
        }

        return result;
    }
}