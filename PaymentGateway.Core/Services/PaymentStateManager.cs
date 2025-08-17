using System.Collections.Concurrent;
using PaymentGateway.Core.Entities;
using PaymentGateway.Core.Enums;
using Microsoft.Extensions.Logging;
using PaymentGateway.Core.Repositories;

namespace PaymentGateway.Core.Services;

public interface IPaymentStateManager
{
    Task<bool> TryTransitionStateAsync(string paymentId, PaymentStatus fromStatus, PaymentStatus toStatus, string teamSlug, CancellationToken cancellationToken = default);
    Task<bool> TryTransitionStateAsync(string paymentId, PaymentStatus fromStatus, PaymentStatus toStatus, CancellationToken cancellationToken = default);
    Task<PaymentStatus> GetPaymentStatusAsync(string paymentId, CancellationToken cancellationToken = default);
    Task<bool> IsValidTransitionAsync(PaymentStatus fromStatus, PaymentStatus toStatus);
    Task ReleaseLockAsync(string paymentId);
    Task SynchronizePaymentStateAsync(string paymentId, PaymentStatus currentStatus, CancellationToken cancellationToken = default);
}

public class PaymentStateManager : IPaymentStateManager
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _paymentLocks;
    private readonly ILogger<PaymentStateManager> _logger;
    private readonly ConcurrentDictionary<string, PaymentStatus> _paymentStatusCache;
    private readonly INotificationWebhookService _webhookService;
    private readonly IPaymentRepository _paymentRepository;
    
    private static readonly Dictionary<PaymentStatus, List<PaymentStatus>> ValidTransitions = new()
    {
        { PaymentStatus.INIT, new List<PaymentStatus> { PaymentStatus.NEW, PaymentStatus.CANCELLED, PaymentStatus.EXPIRED } },
        { PaymentStatus.NEW, new List<PaymentStatus> { PaymentStatus.FORM_SHOWED, PaymentStatus.AUTHORIZED, PaymentStatus.CANCELLED, PaymentStatus.EXPIRED } },
        { PaymentStatus.FORM_SHOWED, new List<PaymentStatus> { PaymentStatus.AUTHORIZED, PaymentStatus.REJECTED, PaymentStatus.CANCELLED, PaymentStatus.EXPIRED } },
        { PaymentStatus.AUTHORIZED, new List<PaymentStatus> { PaymentStatus.CONFIRMED, PaymentStatus.CANCELLED, PaymentStatus.EXPIRED } },
        { PaymentStatus.CONFIRMED, new List<PaymentStatus> { PaymentStatus.REFUNDED, PaymentStatus.PARTIAL_REFUNDED } },
        { PaymentStatus.CANCELLED, new List<PaymentStatus>() },
        { PaymentStatus.REJECTED, new List<PaymentStatus>() },
        { PaymentStatus.REFUNDED, new List<PaymentStatus>() },
        { PaymentStatus.PARTIAL_REFUNDED, new List<PaymentStatus> { PaymentStatus.REFUNDED } },
        { PaymentStatus.EXPIRED, new List<PaymentStatus>() }
    };

    public PaymentStateManager(
        ILogger<PaymentStateManager> logger,
        INotificationWebhookService webhookService,
        IPaymentRepository paymentRepository)
    {
        _paymentLocks = new ConcurrentDictionary<string, SemaphoreSlim>();
        _paymentStatusCache = new ConcurrentDictionary<string, PaymentStatus>();
        _logger = logger;
        _webhookService = webhookService;
        _paymentRepository = paymentRepository;
    }

    public async Task<bool> TryTransitionStateAsync(string paymentId, PaymentStatus fromStatus, PaymentStatus toStatus, string teamSlug, CancellationToken cancellationToken = default)
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

            // Get the actual current status from database or cache
            var currentStatus = await GetPaymentStatusAsync(paymentId, cancellationToken);
            if (currentStatus != fromStatus)
            {
                _logger.LogWarning("Payment {PaymentId} state mismatch. Expected {ExpectedStatus}, but current is {CurrentStatus}", 
                    paymentId, fromStatus, currentStatus);
                return false;
            }

            // Update database first to ensure consistency
            try
            {
                var payment = await _paymentRepository.GetByPaymentIdAsync(paymentId, cancellationToken);
                if (payment != null)
                {
                    payment.Status = toStatus;
                    payment.UpdatedAt = DateTime.UtcNow;
                    await _paymentRepository.UpdateAsync(payment, cancellationToken);
                    _logger.LogDebug("Payment {PaymentId} status updated in database to {Status}", paymentId, toStatus);
                }
                else
                {
                    _logger.LogWarning("Payment {PaymentId} not found in database during state transition", paymentId);
                    return false;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to update payment {PaymentId} status in database during transition to {ToStatus}", paymentId, toStatus);
                return false; // Fail the transition if database update fails
            }
            
            // Update in-memory cache only after successful database update
            _paymentStatusCache.AddOrUpdate(paymentId, toStatus, (_, _) => toStatus);
            
            _logger.LogInformation("Payment {PaymentId} state transitioned from {FromStatus} to {ToStatus}", 
                paymentId, fromStatus, toStatus);
            
            // Send webhook notification for status changes
            await SendWebhookNotificationAsync(paymentId, toStatus, teamSlug, cancellationToken);
            
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

    public async Task<bool> TryTransitionStateAsync(string paymentId, PaymentStatus fromStatus, PaymentStatus toStatus, CancellationToken cancellationToken = default)
    {
        // For backward compatibility, try to send webhook without teamSlug (webhook service will skip if no NotificationUrl)
        return await TryTransitionStateAsync(paymentId, fromStatus, toStatus, "", cancellationToken);
    }

    public async Task<PaymentStatus> GetPaymentStatusAsync(string paymentId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(paymentId);
        
        // Always check database first to ensure we have the most current status
        // This prevents cache inconsistency issues
        try
        {
            var payment = await _paymentRepository.GetByPaymentIdAsync(paymentId, cancellationToken);
            if (payment != null)
            {
                // Update cache with current database status
                _paymentStatusCache.AddOrUpdate(paymentId, payment.Status, (_, _) => payment.Status);
                _logger.LogDebug("Loaded payment status from database: {PaymentId} = {Status}", paymentId, payment.Status);
                return payment.Status;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading payment status from database for {PaymentId}", paymentId);
            
            // Fall back to cache if database is unavailable
            if (_paymentStatusCache.TryGetValue(paymentId, out var cachedStatus))
            {
                _logger.LogWarning("Using cached payment status for {PaymentId} due to database error: {Status}", paymentId, cachedStatus);
                return cachedStatus;
            }
        }
        
        // Default to INIT if payment not found
        _logger.LogWarning("Payment {PaymentId} not found in database or cache, defaulting to INIT status", paymentId);
        return PaymentStatus.INIT;
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

    public async Task SynchronizePaymentStateAsync(string paymentId, PaymentStatus currentStatus, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(paymentId);
        
        try
        {
            // Update database first to ensure consistency
            var payment = await _paymentRepository.GetByPaymentIdAsync(paymentId, cancellationToken);
            if (payment != null)
            {
                payment.Status = currentStatus;
                payment.UpdatedAt = DateTime.UtcNow;
                await _paymentRepository.UpdateAsync(payment, cancellationToken);
                _logger.LogDebug("Updated payment {PaymentId} status in database to {Status}", paymentId, currentStatus);
            }
            else
            {
                _logger.LogWarning("Payment {PaymentId} not found in database during synchronization", paymentId);
            }
            
            // Update cache only after successful database update
            _paymentStatusCache.AddOrUpdate(paymentId, currentStatus, (_, _) => currentStatus);
            _logger.LogDebug("Synchronized payment state cache: {PaymentId} = {Status}", paymentId, currentStatus);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to synchronize payment {PaymentId} status to {Status}", paymentId, currentStatus);
            // Don't update cache if database update failed to maintain consistency
            throw;
        }
    }

    private async Task SendWebhookNotificationAsync(string paymentId, PaymentStatus status, string teamSlug, CancellationToken cancellationToken)
    {
        try
        {
            var additionalData = new Dictionary<string, object>
            {
                ["transition_timestamp"] = DateTime.UtcNow,
                ["status_name"] = status.ToString()
            };

            // Send specific webhook notifications based on status
            switch (status)
            {
                case PaymentStatus.CONFIRMED:
                    await _webhookService.SendPaymentCompletedNotificationAsync(paymentId, teamSlug);
                    break;
                case PaymentStatus.REJECTED:
                case PaymentStatus.CANCELLED:
                case PaymentStatus.EXPIRED:
                    await _webhookService.SendPaymentFailedNotificationAsync(paymentId, status.ToString(), teamSlug);
                    break;
                default:
                    await _webhookService.SendPaymentNotificationAsync(paymentId, status.ToString(), teamSlug, additionalData);
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send webhook notification for payment {PaymentId} status {Status}", paymentId, status);
            // Don't throw - webhook failures shouldn't break payment processing
        }
    }
}