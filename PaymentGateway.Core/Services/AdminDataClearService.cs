using Microsoft.Extensions.Logging;
using PaymentGateway.Core.Repositories;
using PaymentGateway.Core.Interfaces;

namespace PaymentGateway.Core.Services;

public interface IAdminDataClearService
{
    Task<AdminDataClearResult> ClearDatabaseAsync(CancellationToken cancellationToken = default);
    Task<AdminDataClearResult> ClearTeamDataAsync(string teamSlug, CancellationToken cancellationToken = default);
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
    
    // Team-specific information
    public string? TeamSlug { get; set; }
    public bool IsTeamSpecific { get; set; }
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
            Success = false,
            IsTeamSpecific = false
        };

        try
        {
            _logger.LogWarning("Starting admin database clear operation at {Timestamp}", startTime);

            // Use UnitOfWork ExecuteInTransactionAsync which handles execution strategy properly
            await _unitOfWork.ExecuteInTransactionAsync(async () =>
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

                result.Success = true;
                result.OperationDuration = DateTime.UtcNow - startTime;

                _logger.LogWarning("Admin database clear operation completed successfully. Deleted: {PaymentCount} payments, {TransactionCount} transactions, {OrderCount} unique orders in {Duration}ms",
                    result.DeletedPayments, result.DeletedTransactions, result.DeletedOrders, result.OperationDuration.TotalMilliseconds);
            }, cancellationToken);
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

    public async Task<AdminDataClearResult> ClearTeamDataAsync(string teamSlug, CancellationToken cancellationToken = default)
    {
        var startTime = DateTime.UtcNow;
        var result = new AdminDataClearResult
        {
            ClearTimestamp = startTime,
            Success = false,
            TeamSlug = teamSlug,
            IsTeamSpecific = true
        };

        try
        {
            _logger.LogWarning("Starting admin team-specific database clear operation for team {TeamSlug} at {Timestamp}", teamSlug, startTime);

            // First, verify the team exists
            var team = await _unitOfWork.Teams.FindAsync(t => t.TeamSlug == teamSlug, cancellationToken);
            var teamEntity = team.FirstOrDefault();
            
            if (teamEntity == null)
            {
                result.ErrorMessage = $"Team with slug '{teamSlug}' not found";
                _logger.LogWarning("Team not found for slug: {TeamSlug}", teamSlug);
                return result;
            }

            // Use UnitOfWork ExecuteInTransactionAsync which handles execution strategy properly
            await _unitOfWork.ExecuteInTransactionAsync(async () =>
            {
                // Get counts before deletion for statistics - filter by TeamSlug for payments and TeamId for transactions
                var payments = await _unitOfWork.Payments.FindAsync(p => p.TeamSlug == teamSlug && !p.IsDeleted, cancellationToken);
                var paymentList = payments.ToList();
                result.DeletedPayments = paymentList.Count;

                // Count orders (unique OrderId values for this team)
                result.DeletedOrders = paymentList
                    .Where(p => !string.IsNullOrEmpty(p.OrderId))
                    .Select(p => p.OrderId)
                    .Distinct()
                    .Count();

                // Get payment IDs to find related transactions
                var paymentIds = paymentList.Select(p => p.Id).ToList();
                var transactions = await _unitOfWork.Transactions.FindAsync(t => paymentIds.Contains(t.PaymentId) && !t.IsDeleted, cancellationToken);
                result.DeletedTransactions = transactions.Count();

                _logger.LogInformation("About to delete for team {TeamSlug}: {PaymentCount} payments, {TransactionCount} transactions, {OrderCount} unique orders",
                    teamSlug, result.DeletedPayments, result.DeletedTransactions, result.DeletedOrders);

                // Delete transactions first (they reference payments)
                if (paymentIds.Any())
                {
                    await _unitOfWork.Transactions.BulkDeleteAsync(t => paymentIds.Contains(t.PaymentId) && !t.IsDeleted, cancellationToken);
                }

                // Delete payments for this team
                await _unitOfWork.Payments.BulkDeleteAsync(p => p.TeamSlug == teamSlug && !p.IsDeleted, cancellationToken);

                await _unitOfWork.SaveChangesAsync(cancellationToken);

                result.Success = true;
                result.OperationDuration = DateTime.UtcNow - startTime;

                _logger.LogWarning("Admin team-specific database clear operation completed successfully for team {TeamSlug}. Deleted: {PaymentCount} payments, {TransactionCount} transactions, {OrderCount} unique orders in {Duration}ms",
                    teamSlug, result.DeletedPayments, result.DeletedTransactions, result.DeletedOrders, result.OperationDuration.TotalMilliseconds);
            }, cancellationToken);
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.ErrorMessage = ex.Message;
            result.OperationDuration = DateTime.UtcNow - startTime;

            _logger.LogError(ex, "Admin team-specific database clear operation failed for team {TeamSlug} after {Duration}ms",
                teamSlug, result.OperationDuration.TotalMilliseconds);
        }

        return result;
    }
}