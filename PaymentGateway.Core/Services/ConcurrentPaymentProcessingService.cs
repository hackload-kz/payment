using PaymentGateway.Core.Entities;
using PaymentGateway.Core.Enums;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace PaymentGateway.Core.Services;

public interface IConcurrentPaymentProcessingService
{
    Task<PaymentProcessingResult> InitializePaymentAsync(InitializePaymentRequest request, CancellationToken cancellationToken = default);
    Task<PaymentProcessingResult> AuthorizePaymentAsync(string paymentId, AuthorizePaymentRequest request, CancellationToken cancellationToken = default);
    Task<PaymentProcessingResult> ConfirmPaymentAsync(string paymentId, CancellationToken cancellationToken = default);
    Task<PaymentProcessingResult> CancelPaymentAsync(string paymentId, CancellationToken cancellationToken = default);
    Task<Payment?> GetPaymentAsync(string paymentId, CancellationToken cancellationToken = default);
}

public record InitializePaymentRequest(
    string TeamSlug,
    string OrderId,
    decimal Amount,
    string Currency,
    string? Description,
    string? CustomerEmail);

public record AuthorizePaymentRequest(
    string CardNumber,
    string ExpiryDate,
    string CVV,
    string? CardHolderName);

public record PaymentProcessingResult(
    bool IsSuccess,
    string? PaymentId,
    PaymentStatus? Status,
    string? ErrorCode,
    string? ErrorMessage);

public class ConcurrentPaymentProcessingOptions
{
    public TimeSpan LockTimeout { get; set; } = TimeSpan.FromSeconds(30);
    public TimeSpan ProcessingTimeout { get; set; } = TimeSpan.FromMinutes(2);
    public int MaxConcurrentOperations { get; set; } = 100;
}

public class ConcurrentPaymentProcessingService : IConcurrentPaymentProcessingService
{
    private readonly IPaymentStateManager _stateManager;
    private readonly IDistributedLockService _lockService;
    private readonly ILogger<ConcurrentPaymentProcessingService> _logger;
    private readonly ConcurrentPaymentProcessingOptions _options;
    private readonly SemaphoreSlim _concurrencySemaphore;

    public ConcurrentPaymentProcessingService(
        IPaymentStateManager stateManager,
        IDistributedLockService lockService,
        ILogger<ConcurrentPaymentProcessingService> logger,
        IOptions<ConcurrentPaymentProcessingOptions> options)
    {
        _stateManager = stateManager;
        _lockService = lockService;
        _logger = logger;
        _options = options.Value;
        _concurrencySemaphore = new SemaphoreSlim(_options.MaxConcurrentOperations, _options.MaxConcurrentOperations);
    }

    public async Task<PaymentProcessingResult> InitializePaymentAsync(InitializePaymentRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!await _concurrencySemaphore.WaitAsync(_options.ProcessingTimeout, cancellationToken))
        {
            _logger.LogWarning("Payment initialization rejected due to concurrency limit");
            return new PaymentProcessingResult(false, null, null, "SYSTEM_OVERLOAD", "System is currently overloaded");
        }

        try
        {
            var paymentId = Guid.NewGuid().ToString();
            using var lockHandle = await _lockService.AcquireLockAsync($"payment:{paymentId}", _options.LockTimeout, cancellationToken);
            
            if (lockHandle == null)
            {
                _logger.LogWarning("Failed to acquire lock for payment initialization {PaymentId}", paymentId);
                return new PaymentProcessingResult(false, null, null, "LOCK_TIMEOUT", "Failed to acquire processing lock");
            }

            _logger.LogInformation("Initializing payment {PaymentId} for team {TeamSlug} with amount {Amount} {Currency}", 
                paymentId, request.TeamSlug, request.Amount, request.Currency);

            if (!await _stateManager.TryTransitionStateAsync(paymentId, PaymentStatus.INIT, PaymentStatus.NEW, request.TeamSlug, cancellationToken))
            {
                _logger.LogError("Failed to transition payment {PaymentId} from INIT to NEW", paymentId);
                return new PaymentProcessingResult(false, paymentId, PaymentStatus.INIT, "STATE_TRANSITION_ERROR", "Failed to initialize payment state");
            }

            await Task.Delay(100, cancellationToken);

            _logger.LogInformation("Payment {PaymentId} initialized successfully", paymentId);
            return new PaymentProcessingResult(true, paymentId, PaymentStatus.NEW, null, null);
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Payment initialization was cancelled");
            return new PaymentProcessingResult(false, null, null, "OPERATION_CANCELLED", "Operation was cancelled");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during payment initialization");
            return new PaymentProcessingResult(false, null, null, "INTERNAL_ERROR", "Internal processing error");
        }
        finally
        {
            _concurrencySemaphore.Release();
        }
    }

    public async Task<PaymentProcessingResult> AuthorizePaymentAsync(string paymentId, AuthorizePaymentRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(paymentId);
        ArgumentNullException.ThrowIfNull(request);

        if (!await _concurrencySemaphore.WaitAsync(_options.ProcessingTimeout, cancellationToken))
        {
            _logger.LogWarning("Payment authorization rejected due to concurrency limit for {PaymentId}", paymentId);
            return new PaymentProcessingResult(false, paymentId, null, "SYSTEM_OVERLOAD", "System is currently overloaded");
        }

        try
        {
            using var lockHandle = await _lockService.AcquireLockAsync($"payment:{paymentId}", _options.LockTimeout, cancellationToken);
            
            if (lockHandle == null)
            {
                _logger.LogWarning("Failed to acquire lock for payment authorization {PaymentId}", paymentId);
                return new PaymentProcessingResult(false, paymentId, null, "LOCK_TIMEOUT", "Failed to acquire processing lock");
            }

            var currentStatus = await _stateManager.GetPaymentStatusAsync(paymentId, cancellationToken);
            
            if (!await _stateManager.TryTransitionStateAsync(paymentId, currentStatus, PaymentStatus.FORM_SHOWED, cancellationToken))
            {
                _logger.LogWarning("Invalid state transition for payment authorization {PaymentId} from {CurrentStatus}", paymentId, currentStatus);
                return new PaymentProcessingResult(false, paymentId, currentStatus, "INVALID_STATE", "Payment is not in a valid state for authorization");
            }

            _logger.LogInformation("Processing authorization for payment {PaymentId}", paymentId);

            await SimulateCardProcessingAsync(request, cancellationToken);

            if (!await _stateManager.TryTransitionStateAsync(paymentId, PaymentStatus.FORM_SHOWED, PaymentStatus.AUTHORIZED, cancellationToken))
            {
                _logger.LogError("Failed to transition payment {PaymentId} to AUTHORIZED state", paymentId);
                return new PaymentProcessingResult(false, paymentId, PaymentStatus.FORM_SHOWED, "STATE_TRANSITION_ERROR", "Failed to authorize payment");
            }

            _logger.LogInformation("Payment {PaymentId} authorized successfully", paymentId);
            return new PaymentProcessingResult(true, paymentId, PaymentStatus.AUTHORIZED, null, null);
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Payment authorization was cancelled for {PaymentId}", paymentId);
            return new PaymentProcessingResult(false, paymentId, null, "OPERATION_CANCELLED", "Operation was cancelled");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during payment authorization for {PaymentId}", paymentId);
            return new PaymentProcessingResult(false, paymentId, null, "INTERNAL_ERROR", "Internal processing error");
        }
        finally
        {
            _concurrencySemaphore.Release();
        }
    }

    public async Task<PaymentProcessingResult> ConfirmPaymentAsync(string paymentId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(paymentId);

        if (!await _concurrencySemaphore.WaitAsync(_options.ProcessingTimeout, cancellationToken))
        {
            _logger.LogWarning("Payment confirmation rejected due to concurrency limit for {PaymentId}", paymentId);
            return new PaymentProcessingResult(false, paymentId, null, "SYSTEM_OVERLOAD", "System is currently overloaded");
        }

        try
        {
            using var lockHandle = await _lockService.AcquireLockAsync($"payment:{paymentId}", _options.LockTimeout, cancellationToken);
            
            if (lockHandle == null)
            {
                _logger.LogWarning("Failed to acquire lock for payment confirmation {PaymentId}", paymentId);
                return new PaymentProcessingResult(false, paymentId, null, "LOCK_TIMEOUT", "Failed to acquire processing lock");
            }

            var currentStatus = await _stateManager.GetPaymentStatusAsync(paymentId, cancellationToken);
            
            if (currentStatus != PaymentStatus.AUTHORIZED)
            {
                _logger.LogWarning("Payment {PaymentId} is not in AUTHORIZED state for confirmation. Current state: {CurrentStatus}", paymentId, currentStatus);
                return new PaymentProcessingResult(false, paymentId, currentStatus, "INVALID_STATE", "Payment must be authorized before confirmation");
            }

            _logger.LogInformation("Confirming payment {PaymentId}", paymentId);

            await SimulatePaymentConfirmationAsync(cancellationToken);

            if (!await _stateManager.TryTransitionStateAsync(paymentId, PaymentStatus.AUTHORIZED, PaymentStatus.CONFIRMED, cancellationToken))
            {
                _logger.LogError("Failed to transition payment {PaymentId} to CONFIRMED state", paymentId);
                return new PaymentProcessingResult(false, paymentId, PaymentStatus.AUTHORIZED, "STATE_TRANSITION_ERROR", "Failed to confirm payment");
            }

            _logger.LogInformation("Payment {PaymentId} confirmed successfully", paymentId);
            return new PaymentProcessingResult(true, paymentId, PaymentStatus.CONFIRMED, null, null);
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Payment confirmation was cancelled for {PaymentId}", paymentId);
            return new PaymentProcessingResult(false, paymentId, null, "OPERATION_CANCELLED", "Operation was cancelled");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during payment confirmation for {PaymentId}", paymentId);
            return new PaymentProcessingResult(false, paymentId, null, "INTERNAL_ERROR", "Internal processing error");
        }
        finally
        {
            _concurrencySemaphore.Release();
        }
    }

    public async Task<PaymentProcessingResult> CancelPaymentAsync(string paymentId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(paymentId);

        if (!await _concurrencySemaphore.WaitAsync(_options.ProcessingTimeout, cancellationToken))
        {
            _logger.LogWarning("Payment cancellation rejected due to concurrency limit for {PaymentId}", paymentId);
            return new PaymentProcessingResult(false, paymentId, null, "SYSTEM_OVERLOAD", "System is currently overloaded");
        }

        try
        {
            using var lockHandle = await _lockService.AcquireLockAsync($"payment:{paymentId}", _options.LockTimeout, cancellationToken);
            
            if (lockHandle == null)
            {
                _logger.LogWarning("Failed to acquire lock for payment cancellation {PaymentId}", paymentId);
                return new PaymentProcessingResult(false, paymentId, null, "LOCK_TIMEOUT", "Failed to acquire processing lock");
            }

            var currentStatus = await _stateManager.GetPaymentStatusAsync(paymentId, cancellationToken);
            
            if (!await _stateManager.IsValidTransitionAsync(currentStatus, PaymentStatus.CANCELLED))
            {
                _logger.LogWarning("Payment {PaymentId} cannot be cancelled from current state {CurrentStatus}", paymentId, currentStatus);
                return new PaymentProcessingResult(false, paymentId, currentStatus, "INVALID_STATE", "Payment cannot be cancelled in current state");
            }

            _logger.LogInformation("Cancelling payment {PaymentId}", paymentId);

            if (!await _stateManager.TryTransitionStateAsync(paymentId, currentStatus, PaymentStatus.CANCELLED, cancellationToken))
            {
                _logger.LogError("Failed to transition payment {PaymentId} to CANCELLED state", paymentId);
                return new PaymentProcessingResult(false, paymentId, currentStatus, "STATE_TRANSITION_ERROR", "Failed to cancel payment");
            }

            _logger.LogInformation("Payment {PaymentId} cancelled successfully", paymentId);
            return new PaymentProcessingResult(true, paymentId, PaymentStatus.CANCELLED, null, null);
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Payment cancellation was cancelled for {PaymentId}", paymentId);
            return new PaymentProcessingResult(false, paymentId, null, "OPERATION_CANCELLED", "Operation was cancelled");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during payment cancellation for {PaymentId}", paymentId);
            return new PaymentProcessingResult(false, paymentId, null, "INTERNAL_ERROR", "Internal processing error");
        }
        finally
        {
            _concurrencySemaphore.Release();
        }
    }

    public async Task<Payment?> GetPaymentAsync(string paymentId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(paymentId);

        try
        {
            var status = await _stateManager.GetPaymentStatusAsync(paymentId, cancellationToken);
            
            return new Payment
            {
                PaymentId = paymentId,
                Status = status
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving payment {PaymentId}", paymentId);
            return null;
        }
    }

    private async Task SimulateCardProcessingAsync(AuthorizePaymentRequest request, CancellationToken cancellationToken)
    {
        await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken);
    }

    private async Task SimulatePaymentConfirmationAsync(CancellationToken cancellationToken)
    {
        await Task.Delay(TimeSpan.FromMilliseconds(300), cancellationToken);
    }

    public void Dispose()
    {
        _concurrencySemaphore?.Dispose();
        GC.SuppressFinalize(this);
    }
}