// SPDX-License-Identifier: MIT
// Copyright (c) 2025 HackLoad Payment Gateway

using PaymentGateway.Core.Entities;
using PaymentGateway.Core.Interfaces;
using PaymentGateway.Core.Repositories;
using PaymentGateway.Core.Enums;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.Concurrent;
using System.Threading.Channels;
using Prometheus;

namespace PaymentGateway.Core.Services;

/// <summary>
/// High-performance concurrent payment processing engine with advanced locking and queue management
/// </summary>
public interface IConcurrentPaymentProcessingEngineService
{
    Task<ProcessingResult> ProcessPaymentAsync(Guid paymentId, ProcessingOptions options = null, CancellationToken cancellationToken = default);
    Task<ProcessingResult> ProcessPaymentBatchAsync(IEnumerable<Guid> paymentIds, ProcessingOptions options = null, CancellationToken cancellationToken = default);
    Task QueuePaymentAsync(Guid paymentId, ProcessingPriority priority = ProcessingPriority.Normal, CancellationToken cancellationToken = default);
    Task<ProcessingStatus> GetProcessingStatusAsync(Guid paymentId, CancellationToken cancellationToken = default);
    Task<IEnumerable<ProcessingStatus>> GetActiveProcessingAsync(CancellationToken cancellationToken = default);
    Task<bool> CancelProcessingAsync(Guid paymentId, CancellationToken cancellationToken = default);
    Task StartProcessingEngineAsync(CancellationToken cancellationToken = default);
    Task StopProcessingEngineAsync(CancellationToken cancellationToken = default);
}

public class ProcessingOptions
{
    public TimeSpan? Timeout { get; set; } = TimeSpan.FromMinutes(5);
    public int MaxRetries { get; set; } = 3;
    public TimeSpan RetryDelay { get; set; } = TimeSpan.FromSeconds(1);
    public ProcessingPriority Priority { get; set; } = ProcessingPriority.Normal;
    public bool AllowConcurrentTeamProcessing { get; set; } = true;
    public Dictionary<string, object> Metadata { get; set; } = new();
}

public enum ProcessingPriority
{
    Low = 0,
    Normal = 1,
    High = 2,
    Critical = 3
}

public class ProcessingResult
{
    public Guid PaymentId { get; set; }
    public bool IsSuccess { get; set; }
    public PaymentStatus? ResultStatus { get; set; }
    public List<string> Errors { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
    public TimeSpan ProcessingDuration { get; set; }
    public int RetryCount { get; set; }
    public Dictionary<string, object> Metadata { get; set; } = new();

    public static ProcessingResult Success(Guid paymentId, PaymentStatus status, TimeSpan duration) =>
        new() { PaymentId = paymentId, IsSuccess = true, ResultStatus = status, ProcessingDuration = duration };

    public static ProcessingResult Failure(Guid paymentId, TimeSpan duration, params string[] errors) =>
        new() { PaymentId = paymentId, IsSuccess = false, ProcessingDuration = duration, Errors = errors.ToList() };
}

public class ProcessingStatus
{
    public Guid PaymentId { get; set; }
    public ProcessingState State { get; set; }
    public DateTime StartedAt { get; set; }
    public TimeSpan? Duration { get; set; }
    public ProcessingPriority Priority { get; set; }
    public int RetryCount { get; set; }
    public string LastError { get; set; }
    public Dictionary<string, object> Metadata { get; set; } = new();
}

public enum ProcessingState
{
    Queued,
    Processing,
    Completed,
    Failed,
    Cancelled
}

public class ConcurrentPaymentProcessingEngineService : IConcurrentPaymentProcessingEngineService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IDistributedLockService _distributedLockService;
    private readonly ILogger<ConcurrentPaymentProcessingEngineService> _logger;
    
    // Processing infrastructure
    private readonly Channel<PaymentProcessingItem> _processingQueue;
    private readonly ChannelWriter<PaymentProcessingItem> _queueWriter;
    private readonly ChannelReader<PaymentProcessingItem> _queueReader;
    
    // Processing state tracking
    private readonly ConcurrentDictionary<Guid, ProcessingStatus> _activeProcessing = new();
    private readonly ConcurrentDictionary<int, SemaphoreSlim> _teamSemaphores = new();
    private readonly SemaphoreSlim _globalSemaphore;
    
    // Processing workers
    private readonly List<Task> _processingWorkers = new();
    private readonly CancellationTokenSource _engineCancellation = new();
    
    // Configuration
    private readonly int _maxConcurrentProcessing = Environment.ProcessorCount * 2;
    private readonly int _maxConcurrentPerTeam = 5;
    private readonly int _processingWorkersCount = Environment.ProcessorCount;
    
    // Metrics
    private static readonly Counter ProcessingOperations = Metrics
        .CreateCounter("payment_processing_operations_total", "Total payment processing operations", new[] { "result", "priority" });
    
    private static readonly Histogram ProcessingDuration = Metrics
        .CreateHistogram("payment_processing_duration_seconds", "Payment processing duration", new[] { "priority" });
    
    private static readonly Gauge ActiveProcessingGauge = Metrics
        .CreateGauge("active_payment_processing_total", "Total active payment processing operations");
    
    private static readonly Gauge QueueLengthGauge = Metrics
        .CreateGauge("payment_processing_queue_length", "Payment processing queue length");

    private class PaymentProcessingItem
    {
        public Guid PaymentId { get; set; }
        public ProcessingPriority Priority { get; set; }
        public ProcessingOptions Options { get; set; }
        public DateTime QueuedAt { get; set; } = DateTime.UtcNow;
        public TaskCompletionSource<ProcessingResult> CompletionSource { get; set; }
    }

    public ConcurrentPaymentProcessingEngineService(
        IServiceProvider serviceProvider,
        IDistributedLockService distributedLockService,
        ILogger<ConcurrentPaymentProcessingEngineService> logger)
    {
        _serviceProvider = serviceProvider;
        _distributedLockService = distributedLockService;
        _logger = logger;
        
        // Initialize processing queue with bounded capacity
        var options = new BoundedChannelOptions(10000)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = false,
            SingleWriter = false
        };
        
        _processingQueue = Channel.CreateBounded<PaymentProcessingItem>(options);
        _queueWriter = _processingQueue.Writer;
        _queueReader = _processingQueue.Reader;
        
        // Initialize global processing semaphore
        _globalSemaphore = new SemaphoreSlim(_maxConcurrentProcessing, _maxConcurrentProcessing);
    }

    public async Task<ProcessingResult> ProcessPaymentAsync(Guid paymentId, ProcessingOptions options = null, CancellationToken cancellationToken = default)
    {
        using var activity = ProcessingDuration.WithLabels(options?.Priority.ToString() ?? "Normal").NewTimer();
        var lockKey = $"payment:processing:{paymentId}";
        
        try
        {
            options ??= new ProcessingOptions();
            
            // Check if payment is already being processed
            if (_activeProcessing.ContainsKey(paymentId))
            {
                _logger.LogWarning("Payment is already being processed: {PaymentId}", paymentId);
                return ProcessingResult.Failure(paymentId, TimeSpan.Zero, "Payment is already being processed");
            }

            // Acquire global processing semaphore
            await _globalSemaphore.WaitAsync(cancellationToken);
            try
            {
                // Acquire distributed lock for this payment
                await using var lockHandle = await _distributedLockService.AcquireLockAsync(lockKey, options.Timeout ?? TimeSpan.FromMinutes(5), cancellationToken);
                if (lockHandle == null)
                {
                    ProcessingOperations.WithLabels("lock_failed", options.Priority.ToString()).Inc();
                    return ProcessingResult.Failure(paymentId, TimeSpan.Zero, "Failed to acquire processing lock");
                }

                // Create processing status
                var processingStatus = new ProcessingStatus
                {
                    PaymentId = paymentId,
                    State = ProcessingState.Processing,
                    StartedAt = DateTime.UtcNow,
                    Priority = options.Priority
                };
                
                _activeProcessing.TryAdd(paymentId, processingStatus);
                ActiveProcessingGauge.Inc();

                // Get payment details
                using var scope = _serviceProvider.CreateScope();
                var paymentRepository = scope.ServiceProvider.GetRequiredService<IPaymentRepository>();
                var lifecycleService = scope.ServiceProvider.GetRequiredService<IPaymentLifecycleManagementService>();
                var validationService = scope.ServiceProvider.GetRequiredService<IPaymentStateTransitionValidationService>();

                var payment = await paymentRepository.GetByIdAsync(paymentId, cancellationToken);
                if (payment == null)
                {
                    ProcessingOperations.WithLabels("not_found", options.Priority.ToString()).Inc();
                    return ProcessingResult.Failure(paymentId, TimeSpan.Zero, "Payment not found");
                }

                // Acquire team-specific semaphore
                var teamSemaphore = _teamSemaphores.GetOrAdd(payment.TeamId, _ => new SemaphoreSlim(_maxConcurrentPerTeam, _maxConcurrentPerTeam));
                
                if (!options.AllowConcurrentTeamProcessing)
                {
                    await teamSemaphore.WaitAsync(cancellationToken);
                }
                else if (!await teamSemaphore.WaitAsync(100, cancellationToken))
                {
                    ProcessingOperations.WithLabels("team_limit_exceeded", options.Priority.ToString()).Inc();
                    return ProcessingResult.Failure(paymentId, TimeSpan.Zero, "Team concurrent processing limit exceeded");
                }

                try
                {
                    // Process payment with retries
                    var result = await ProcessPaymentWithRetriesAsync(payment, lifecycleService, validationService, options, cancellationToken);
                    
                    // Update processing status
                    processingStatus.State = result.IsSuccess ? ProcessingState.Completed : ProcessingState.Failed;
                    processingStatus.Duration = TimeSpan.Zero;
                    
                    if (result.IsSuccess)
                    {
                        ProcessingOperations.WithLabels("success", options.Priority.ToString()).Inc();
                    }
                    else
                    {
                        ProcessingOperations.WithLabels("failed", options.Priority.ToString()).Inc();
                        processingStatus.LastError = string.Join(", ", result.Errors);
                    }

                    return result;
                }
                finally
                {
                    teamSemaphore.Release();
                }
            }
            finally
            {
                _globalSemaphore.Release();
                _activeProcessing.TryRemove(paymentId, out _);
                ActiveProcessingGauge.Dec();
            }
        }
        catch (OperationCanceledException)
        {
            ProcessingOperations.WithLabels("cancelled", options?.Priority.ToString() ?? "Normal").Inc();
            return ProcessingResult.Failure(paymentId, TimeSpan.Zero, "Processing was cancelled");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Payment processing failed: {PaymentId}", paymentId);
            ProcessingOperations.WithLabels("error", options?.Priority.ToString() ?? "Normal").Inc();
            return ProcessingResult.Failure(paymentId, TimeSpan.Zero, "Internal processing error");
        }
    }

    public async Task<ProcessingResult> ProcessPaymentBatchAsync(IEnumerable<Guid> paymentIds, ProcessingOptions options = null, CancellationToken cancellationToken = default)
    {
        options ??= new ProcessingOptions();
        var results = new List<ProcessingResult>();
        
        try
        {
            var tasks = paymentIds.Select(async paymentId =>
            {
                try
                {
                    return await ProcessPaymentAsync(paymentId, options, cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Batch processing failed for payment: {PaymentId}", paymentId);
                    return ProcessingResult.Failure(paymentId, TimeSpan.Zero, "Batch processing error");
                }
            });

            var batchResults = await Task.WhenAll(tasks);
            results.AddRange(batchResults);

            var successCount = results.Count(r => r.IsSuccess);
            var totalCount = results.Count;
            
            _logger.LogInformation("Batch processing completed: {SuccessCount}/{TotalCount} successful", successCount, totalCount);
            
            // Return a summary result
            var overallSuccess = successCount == totalCount;
            var totalDuration = TimeSpan.FromMilliseconds(results.Sum(r => r.ProcessingDuration.TotalMilliseconds));
            
            if (overallSuccess)
            {
                return ProcessingResult.Success(Guid.Empty, PaymentStatus.PROCESSING, totalDuration);
            }
            else
            {
                var errors = results.Where(r => !r.IsSuccess).SelectMany(r => r.Errors).ToArray();
                return ProcessingResult.Failure(Guid.Empty, totalDuration, errors);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Batch processing failed");
            return ProcessingResult.Failure(Guid.Empty, TimeSpan.Zero, "Batch processing error");
        }
    }

    public async Task QueuePaymentAsync(Guid paymentId, ProcessingPriority priority = ProcessingPriority.Normal, CancellationToken cancellationToken = default)
    {
        try
        {
            var item = new PaymentProcessingItem
            {
                PaymentId = paymentId,
                Priority = priority,
                Options = new ProcessingOptions { Priority = priority },
                CompletionSource = new TaskCompletionSource<ProcessingResult>()
            };

            await _queueWriter.WriteAsync(item, cancellationToken);
            QueueLengthGauge.Inc();
            
            _logger.LogDebug("Payment queued for processing: {PaymentId}, Priority: {Priority}", paymentId, priority);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to queue payment: {PaymentId}", paymentId);
            throw;
        }
    }

    public async Task<ProcessingStatus> GetProcessingStatusAsync(Guid paymentId, CancellationToken cancellationToken = default)
    {
        return await Task.FromResult(_activeProcessing.GetValueOrDefault(paymentId));
    }

    public async Task<IEnumerable<ProcessingStatus>> GetActiveProcessingAsync(CancellationToken cancellationToken = default)
    {
        return await Task.FromResult(_activeProcessing.Values.ToList());
    }

    public async Task<bool> CancelProcessingAsync(Guid paymentId, CancellationToken cancellationToken = default)
    {
        try
        {
            if (_activeProcessing.TryGetValue(paymentId, out var status))
            {
                status.State = ProcessingState.Cancelled;
                _logger.LogInformation("Processing cancelled for payment: {PaymentId}", paymentId);
                return true;
            }
            
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to cancel processing for payment: {PaymentId}", paymentId);
            return false;
        }
    }

    public async Task StartProcessingEngineAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Starting payment processing engine with {WorkerCount} workers", _processingWorkersCount);
            
            // Start processing workers
            for (int i = 0; i < _processingWorkersCount; i++)
            {
                var workerId = i;
                var worker = ProcessingWorkerAsync(workerId, _engineCancellation.Token);
                _processingWorkers.Add(worker);
            }
            
            _logger.LogInformation("Payment processing engine started successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start payment processing engine");
            throw;
        }
    }

    public async Task StopProcessingEngineAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Stopping payment processing engine");
            
            // Signal cancellation
            _engineCancellation.Cancel();
            
            // Complete the queue writer
            _queueWriter.Complete();
            
            // Wait for all workers to complete
            await Task.WhenAll(_processingWorkers);
            
            _logger.LogInformation("Payment processing engine stopped successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to stop payment processing engine");
            throw;
        }
    }

    private async Task ProcessingWorkerAsync(int workerId, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Processing worker {WorkerId} started", workerId);
        
        try
        {
            await foreach (var item in _queueReader.ReadAllAsync(cancellationToken))
            {
                QueueLengthGauge.Dec();
                
                try
                {
                    var result = await ProcessPaymentAsync(item.PaymentId, item.Options, cancellationToken);
                    item.CompletionSource.SetResult(result);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Worker {WorkerId} failed to process payment: {PaymentId}", workerId, item.PaymentId);
                    var errorResult = ProcessingResult.Failure(item.PaymentId, TimeSpan.Zero, "Worker processing error");
                    item.CompletionSource.SetResult(errorResult);
                }
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Processing worker {WorkerId} cancelled", workerId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Processing worker {WorkerId} failed", workerId);
        }
        finally
        {
            _logger.LogInformation("Processing worker {WorkerId} stopped", workerId);
        }
    }

    private async Task<ProcessingResult> ProcessPaymentWithRetriesAsync(
        Payment payment, 
        IPaymentLifecycleManagementService lifecycleService,
        IPaymentStateTransitionValidationService validationService,
        ProcessingOptions options, 
        CancellationToken cancellationToken)
    {
        var startTime = DateTime.UtcNow;
        var retryCount = 0;
        
        while (retryCount <= options.MaxRetries)
        {
            try
            {
                // Validate payment can be processed
                var canProcess = await validationService.CanProcessPaymentAsync(payment.Id, cancellationToken);
                if (!canProcess)
                {
                    return ProcessingResult.Failure(payment.Id, DateTime.UtcNow - startTime, "Payment cannot be processed");
                }

                // Process payment based on current status
                Payment result = payment.Status switch
                {
                    PaymentStatus.NEW => await lifecycleService.ProcessPaymentAsync(payment.Id, cancellationToken),
                    PaymentStatus.PROCESSING => await lifecycleService.AuthorizePaymentAsync(payment.Id, cancellationToken),
                    PaymentStatus.AUTHORIZED => await lifecycleService.ConfirmPaymentAsync(payment.Id, cancellationToken),
                    _ => throw new InvalidOperationException($"Cannot process payment in {payment.Status} state")
                };

                return ProcessingResult.Success(payment.Id, result.Status, DateTime.UtcNow - startTime);
            }
            catch (Exception ex) when (retryCount < options.MaxRetries && IsRetryableException(ex))
            {
                retryCount++;
                _logger.LogWarning(ex, "Payment processing attempt {RetryCount} failed for payment {PaymentId}, retrying in {Delay}", 
                    retryCount, payment.PaymentId, options.RetryDelay);
                
                await Task.Delay(options.RetryDelay * retryCount, cancellationToken); // Exponential backoff
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Payment processing failed permanently for payment {PaymentId}", payment.PaymentId);
                var result = ProcessingResult.Failure(payment.Id, DateTime.UtcNow - startTime, ex.Message);
                result.RetryCount = retryCount;
                return result;
            }
        }

        var failureResult = ProcessingResult.Failure(payment.Id, DateTime.UtcNow - startTime, "Max retries exceeded");
        failureResult.RetryCount = retryCount;
        return failureResult;
    }

    private static bool IsRetryableException(Exception ex)
    {
        // Define which exceptions are retryable
        return ex is TimeoutException ||
               ex is InvalidOperationException && ex.Message.Contains("lock") ||
               (ex.InnerException != null && IsRetryableException(ex.InnerException));
    }

    public void Dispose()
    {
        _engineCancellation?.Cancel();
        _engineCancellation?.Dispose();
        _globalSemaphore?.Dispose();
        
        foreach (var semaphore in _teamSemaphores.Values)
        {
            semaphore?.Dispose();
        }
    }
}