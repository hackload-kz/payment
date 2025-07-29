using System.Collections.Concurrent;
using System.Threading.Channels;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace PaymentGateway.Core.Services;

public enum PaymentQueueOperation
{
    Initialize,
    Authorize,
    Confirm,
    Cancel,
    Expire
}

public record PaymentQueueItem(
    string PaymentId,
    PaymentQueueOperation Operation,
    object? Data,
    DateTime EnqueuedAt,
    int Priority = 0);

public interface IPaymentQueueService
{
    Task EnqueueAsync(PaymentQueueItem item, CancellationToken cancellationToken = default);
    Task<(bool Success, PaymentQueueItem? Item)> TryDequeueAsync(CancellationToken cancellationToken = default);
    int GetQueueLength();
    int GetProcessingCount();
}

public class PaymentQueueOptions
{
    public int MaxQueueSize { get; set; } = 10000;
    public int MaxConcurrentProcessing { get; set; } = 50;
    public TimeSpan ProcessingTimeout { get; set; } = TimeSpan.FromMinutes(5);
    public int RetryAttempts { get; set; } = 3;
    public TimeSpan RetryDelay { get; set; } = TimeSpan.FromSeconds(30);
}

public class PaymentQueueService : IPaymentQueueService
{
    private readonly Channel<PaymentQueueItem> _queue;
    private readonly ChannelWriter<PaymentQueueItem> _writer;
    private readonly ChannelReader<PaymentQueueItem> _reader;
    private readonly ILogger<PaymentQueueService> _logger;
    private readonly PaymentQueueOptions _options;
    private readonly ConcurrentDictionary<string, PaymentQueueItem> _processing;

    public PaymentQueueService(ILogger<PaymentQueueService> logger, IOptions<PaymentQueueOptions> options)
    {
        _logger = logger;
        _options = options.Value;
        _processing = new ConcurrentDictionary<string, PaymentQueueItem>();

        var queueOptions = new BoundedChannelOptions(_options.MaxQueueSize)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = false,
            SingleWriter = false
        };

        _queue = Channel.CreateBounded<PaymentQueueItem>(queueOptions);
        _writer = _queue.Writer;
        _reader = _queue.Reader;
    }

    public async Task EnqueueAsync(PaymentQueueItem item, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(item);

        try
        {
            await _writer.WriteAsync(item, cancellationToken);
            _logger.LogDebug("Enqueued payment operation {Operation} for payment {PaymentId}", 
                item.Operation, item.PaymentId);
        }
        catch (InvalidOperationException)
        {
            _logger.LogError("Queue is closed, cannot enqueue payment operation {Operation} for payment {PaymentId}", 
                item.Operation, item.PaymentId);
            throw;
        }
    }

    public async Task<(bool Success, PaymentQueueItem? Item)> TryDequeueAsync(CancellationToken cancellationToken = default)
    {
        if (_processing.Count >= _options.MaxConcurrentProcessing)
        {
            return (false, null);
        }

        try
        {
            if (_reader.TryRead(out var item))
            {
                _processing.TryAdd(item.PaymentId, item);
                _logger.LogDebug("Dequeued payment operation {Operation} for payment {PaymentId}", 
                    item.Operation, item.PaymentId);
                return (true, item);
            }

            item = await _reader.ReadAsync(cancellationToken);
            _processing.TryAdd(item.PaymentId, item);
            _logger.LogDebug("Dequeued payment operation {Operation} for payment {PaymentId}", 
                item.Operation, item.PaymentId);
            return (true, item);
        }
        catch (OperationCanceledException)
        {
            return (false, null);
        }
        catch (InvalidOperationException)
        {
            _logger.LogWarning("Queue reader has been completed");
            return (false, null);
        }
    }

    public int GetQueueLength()
    {
        return _reader.Count;
    }

    public int GetProcessingCount()
    {
        return _processing.Count;
    }

    public void CompleteProcessing(string paymentId)
    {
        _processing.TryRemove(paymentId, out _);
    }

    public void CompleteQueue()
    {
        _writer.Complete();
    }
}

public class PaymentQueueBackgroundService : BackgroundService
{
    private readonly IPaymentQueueService _queueService;
    private readonly IConcurrentPaymentProcessingService _paymentProcessingService;
    private readonly ILogger<PaymentQueueBackgroundService> _logger;
    private readonly PaymentQueueOptions _options;

    public PaymentQueueBackgroundService(
        IPaymentQueueService queueService,
        IConcurrentPaymentProcessingService paymentProcessingService,
        ILogger<PaymentQueueBackgroundService> logger,
        IOptions<PaymentQueueOptions> options)
    {
        _queueService = queueService;
        _paymentProcessingService = paymentProcessingService;
        _logger = logger;
        _options = options.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Payment queue background service started");

        var processingTasks = new List<Task>();

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var dequeueResult = await _queueService.TryDequeueAsync(stoppingToken);
                if (dequeueResult.Success && dequeueResult.Item != null)
                {
                    var processingTask = ProcessPaymentItemAsync(dequeueResult.Item, stoppingToken);
                    processingTasks.Add(processingTask);

                    processingTasks.RemoveAll(t => t.IsCompleted);

                    if (processingTasks.Count >= _options.MaxConcurrentProcessing)
                    {
                        await Task.WhenAny(processingTasks);
                        processingTasks.RemoveAll(t => t.IsCompleted);
                    }
                }
                else
                {
                    await Task.Delay(100, stoppingToken);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in payment queue background service");
                await Task.Delay(1000, stoppingToken);
            }
        }

        await Task.WhenAll(processingTasks);
        _logger.LogInformation("Payment queue background service stopped");
    }

    private async Task ProcessPaymentItemAsync(PaymentQueueItem item, CancellationToken cancellationToken)
    {
        var attempt = 0;
        
        while (attempt < _options.RetryAttempts)
        {
            try
            {
                _logger.LogDebug("Processing payment operation {Operation} for payment {PaymentId} (attempt {Attempt})", 
                    item.Operation, item.PaymentId, attempt + 1);

                var result = item.Operation switch
                {
                    PaymentQueueOperation.Initialize => await ProcessInitializeAsync(item, cancellationToken),
                    PaymentQueueOperation.Authorize => await ProcessAuthorizeAsync(item, cancellationToken),
                    PaymentQueueOperation.Confirm => await ProcessConfirmAsync(item, cancellationToken),
                    PaymentQueueOperation.Cancel => await ProcessCancelAsync(item, cancellationToken),
                    PaymentQueueOperation.Expire => await ProcessExpireAsync(item, cancellationToken),
                    _ => throw new ArgumentException($"Unknown operation: {item.Operation}")
                };

                if (result)
                {
                    _logger.LogInformation("Successfully processed payment operation {Operation} for payment {PaymentId}", 
                        item.Operation, item.PaymentId);
                    break;
                }

                attempt++;
                if (attempt < _options.RetryAttempts)
                {
                    _logger.LogWarning("Payment operation {Operation} for payment {PaymentId} failed, retrying in {Delay}", 
                        item.Operation, item.PaymentId, _options.RetryDelay);
                    await Task.Delay(_options.RetryDelay, cancellationToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing payment operation {Operation} for payment {PaymentId} (attempt {Attempt})", 
                    item.Operation, item.PaymentId, attempt + 1);
                
                attempt++;
                if (attempt < _options.RetryAttempts)
                {
                    await Task.Delay(_options.RetryDelay, cancellationToken);
                }
            }
        }

        if (attempt >= _options.RetryAttempts)
        {
            _logger.LogError("Failed to process payment operation {Operation} for payment {PaymentId} after {Attempts} attempts", 
                item.Operation, item.PaymentId, _options.RetryAttempts);
        }

        if (_queueService is PaymentQueueService concreteQueue)
        {
            concreteQueue.CompleteProcessing(item.PaymentId);
        }
    }

    private async Task<bool> ProcessInitializeAsync(PaymentQueueItem item, CancellationToken cancellationToken)
    {
        if (item.Data is not InitializePaymentRequest request)
        {
            _logger.LogError("Invalid data type for Initialize operation");
            return false;
        }

        var result = await _paymentProcessingService.InitializePaymentAsync(request, cancellationToken);
        return result.IsSuccess;
    }

    private async Task<bool> ProcessAuthorizeAsync(PaymentQueueItem item, CancellationToken cancellationToken)
    {
        if (item.Data is not AuthorizePaymentRequest request)
        {
            _logger.LogError("Invalid data type for Authorize operation");
            return false;
        }

        var result = await _paymentProcessingService.AuthorizePaymentAsync(item.PaymentId, request, cancellationToken);
        return result.IsSuccess;
    }

    private async Task<bool> ProcessConfirmAsync(PaymentQueueItem item, CancellationToken cancellationToken)
    {
        var result = await _paymentProcessingService.ConfirmPaymentAsync(item.PaymentId, cancellationToken);
        return result.IsSuccess;
    }

    private async Task<bool> ProcessCancelAsync(PaymentQueueItem item, CancellationToken cancellationToken)
    {
        var result = await _paymentProcessingService.CancelPaymentAsync(item.PaymentId, cancellationToken);
        return result.IsSuccess;
    }

    private async Task<bool> ProcessExpireAsync(PaymentQueueItem item, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Processing payment expiration for payment {PaymentId}", item.PaymentId);
        return await Task.FromResult(true);
    }
}