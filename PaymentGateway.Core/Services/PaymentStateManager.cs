using System.Collections.Concurrent;
using PaymentGateway.Core.Entities;
using PaymentGateway.Core.Enums;
using Microsoft.Extensions.Logging;

namespace PaymentGateway.Core.Services;

public interface IPaymentStateManager
{
    Task<bool> TryTransitionStateAsync(string paymentId, PaymentStatus fromStatus, PaymentStatus toStatus, CancellationToken cancellationToken = default);
    Task<PaymentStatus> GetPaymentStatusAsync(string paymentId, CancellationToken cancellationToken = default);
    Task<bool> IsValidTransitionAsync(PaymentStatus fromStatus, PaymentStatus toStatus);
    Task ReleaseLockAsync(string paymentId);
}

public class PaymentStateManager : IPaymentStateManager
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _paymentLocks;
    private readonly ILogger<PaymentStateManager> _logger;
    private readonly ConcurrentDictionary<string, PaymentStatus> _paymentStatusCache;
    
    private static readonly Dictionary<PaymentStatus, List<PaymentStatus>> ValidTransitions = new()
    {
        { PaymentStatus.INIT, new List<PaymentStatus> { PaymentStatus.NEW, PaymentStatus.CANCELLED, PaymentStatus.EXPIRED } },
        { PaymentStatus.NEW, new List<PaymentStatus> { PaymentStatus.FORM_SHOWED, PaymentStatus.CANCELLED, PaymentStatus.EXPIRED } },
        { PaymentStatus.FORM_SHOWED, new List<PaymentStatus> { PaymentStatus.AUTHORIZED, PaymentStatus.REJECTED, PaymentStatus.CANCELLED, PaymentStatus.EXPIRED } },
        { PaymentStatus.AUTHORIZED, new List<PaymentStatus> { PaymentStatus.CONFIRMED, PaymentStatus.CANCELLED, PaymentStatus.EXPIRED } },
        { PaymentStatus.CONFIRMED, new List<PaymentStatus> { PaymentStatus.REFUNDED, PaymentStatus.PARTIAL_REFUNDED } },
        { PaymentStatus.CANCELLED, new List<PaymentStatus>() },
        { PaymentStatus.REJECTED, new List<PaymentStatus>() },
        { PaymentStatus.REFUNDED, new List<PaymentStatus>() },
        { PaymentStatus.PARTIAL_REFUNDED, new List<PaymentStatus> { PaymentStatus.REFUNDED } },
        { PaymentStatus.EXPIRED, new List<PaymentStatus>() }
    };

    public PaymentStateManager(ILogger<PaymentStateManager> logger)
    {
        _paymentLocks = new ConcurrentDictionary<string, SemaphoreSlim>();
        _paymentStatusCache = new ConcurrentDictionary<string, PaymentStatus>();
        _logger = logger;
    }

    public async Task<bool> TryTransitionStateAsync(string paymentId, PaymentStatus fromStatus, PaymentStatus toStatus, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(paymentId);

        var lockObject = _paymentLocks.GetOrAdd(paymentId, _ => new SemaphoreSlim(1, 1));
        
        try
        {
            await lockObject.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);
            
            _logger.LogDebug("Attempting state transition for payment {PaymentId} from {FromStatus} to {ToStatus}", 
                paymentId, fromStatus, toStatus);

            if (!await IsValidTransitionAsync(fromStatus, toStatus))
            {
                _logger.LogWarning("Invalid state transition for payment {PaymentId} from {FromStatus} to {ToStatus}", 
                    paymentId, fromStatus, toStatus);
                return false;
            }

            var currentStatus = _paymentStatusCache.GetValueOrDefault(paymentId, PaymentStatus.INIT);
            if (currentStatus != fromStatus)
            {
                _logger.LogWarning("Payment {PaymentId} state mismatch. Expected {ExpectedStatus}, but current is {CurrentStatus}", 
                    paymentId, fromStatus, currentStatus);
                return false;
            }

            _paymentStatusCache.AddOrUpdate(paymentId, toStatus, (_, _) => toStatus);
            
            _logger.LogInformation("Payment {PaymentId} state transitioned from {FromStatus} to {ToStatus}", 
                paymentId, fromStatus, toStatus);
            
            return true;
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("State transition timeout for payment {PaymentId}", paymentId);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during state transition for payment {PaymentId}", paymentId);
            return false;
        }
        finally
        {
            lockObject.Release();
        }
    }

    public async Task<PaymentStatus> GetPaymentStatusAsync(string paymentId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(paymentId);
        
        return await Task.FromResult(_paymentStatusCache.GetValueOrDefault(paymentId, PaymentStatus.INIT));
    }

    public async Task<bool> IsValidTransitionAsync(PaymentStatus fromStatus, PaymentStatus toStatus)
    {
        return await Task.FromResult(ValidTransitions.ContainsKey(fromStatus) && 
                                   ValidTransitions[fromStatus].Contains(toStatus));
    }

    public async Task ReleaseLockAsync(string paymentId)
    {
        ArgumentNullException.ThrowIfNull(paymentId);
        
        if (_paymentLocks.TryRemove(paymentId, out var lockObject))
        {
            lockObject.Dispose();
            _logger.LogDebug("Released lock for payment {PaymentId}", paymentId);
        }
        
        await Task.CompletedTask;
    }
}