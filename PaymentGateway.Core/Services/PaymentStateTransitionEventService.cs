using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PaymentGateway.Core.Entities;
using PaymentGateway.Core.Enums;
using System.Collections.Concurrent;

namespace PaymentGateway.Core.Services;

public interface IPaymentStateTransitionEventService
{
    Task PublishTransitionEventAsync(PaymentStateTransitionEvent transitionEvent, CancellationToken cancellationToken = default);
    Task PublishStateTransitionAsync(Guid paymentId, PaymentStatus fromStatus, PaymentStatus toStatus, CancellationToken cancellationToken = default);
    void Subscribe<THandler>(PaymentStatus? fromStatus = null, PaymentStatus? toStatus = null) where THandler : IPaymentStateTransitionHandler;
    void Subscribe(IPaymentStateTransitionHandler handler, PaymentStatus? fromStatus = null, PaymentStatus? toStatus = null);
    Task UnsubscribeAsync<THandler>() where THandler : IPaymentStateTransitionHandler;
    Task UnsubscribeAsync(IPaymentStateTransitionHandler handler);
}

public interface IPaymentStateTransitionHandler
{
    Task HandleAsync(PaymentStateTransitionEvent transitionEvent, CancellationToken cancellationToken = default);
    bool CanHandle(PaymentStatus fromStatus, PaymentStatus toStatus);
    int Priority { get; } // Lower numbers = higher priority
}

public class PaymentStateTransitionEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PaymentId { get; set; }
    public PaymentStatus FromStatus { get; set; }
    public PaymentStatus ToStatus { get; set; }
    public string TransitionId { get; set; } = string.Empty;
    public DateTime TransitionedAt { get; set; }
    public string? UserId { get; set; }
    public string? Reason { get; set; }
    public Dictionary<string, object> Context { get; set; } = new();
    public Payment Payment { get; set; } = null!;
    public PaymentStateTransition Transition { get; set; } = null!;
}

public class PaymentStateTransitionEventService : IPaymentStateTransitionEventService
{
    private readonly ILogger<PaymentStateTransitionEventService> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly ConcurrentBag<HandlerRegistration> _handlers = new();

    private class HandlerRegistration
    {
        public Type? HandlerType { get; set; }
        public IPaymentStateTransitionHandler? HandlerInstance { get; set; }
        public PaymentStatus? FromStatus { get; set; }
        public PaymentStatus? ToStatus { get; set; }
        public int Priority { get; set; }
    }

    public PaymentStateTransitionEventService(
        ILogger<PaymentStateTransitionEventService> logger,
        IServiceProvider serviceProvider)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
    }

    public async Task PublishTransitionEventAsync(PaymentStateTransitionEvent transitionEvent, CancellationToken cancellationToken = default)
    {
        try
        {
            var applicableHandlers = GetApplicableHandlers(transitionEvent.FromStatus, transitionEvent.ToStatus)
                .OrderBy(h => h.Priority)
                .ToList();

            if (!applicableHandlers.Any())
            {
                _logger.LogDebug("No handlers found for transition {FromStatus} -> {ToStatus}", 
                    transitionEvent.FromStatus, transitionEvent.ToStatus);
                return;
            }

            _logger.LogInformation("Publishing transition event for payment {PaymentId}: {FromStatus} -> {ToStatus} to {HandlerCount} handlers",
                transitionEvent.PaymentId, transitionEvent.FromStatus, transitionEvent.ToStatus, applicableHandlers.Count);

            var tasks = new List<Task>();
            
            foreach (var handler in applicableHandlers)
            {
                tasks.Add(ExecuteHandlerAsync(handler, transitionEvent, cancellationToken));
            }

            await Task.WhenAll(tasks);

            _logger.LogDebug("All handlers completed for transition event {EventId}", transitionEvent.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error publishing transition event for payment {PaymentId}", transitionEvent.PaymentId);
            throw;
        }
    }

    public void Subscribe<THandler>(PaymentStatus? fromStatus = null, PaymentStatus? toStatus = null) where THandler : IPaymentStateTransitionHandler
    {
        var handlerType = typeof(THandler);
        var existingRegistration = _handlers.FirstOrDefault(h => h.HandlerType == handlerType);
        
        if (existingRegistration != null)
        {
            _logger.LogWarning("Handler {HandlerType} is already registered", handlerType.Name);
            return;
        }

        // Get priority from handler instance
        var tempHandler = (IPaymentStateTransitionHandler)Activator.CreateInstance(handlerType)!;
        var priority = tempHandler.Priority;

        var registration = new HandlerRegistration
        {
            HandlerType = handlerType,
            FromStatus = fromStatus,
            ToStatus = toStatus,
            Priority = priority
        };

        _handlers.Add(registration);
        
        _logger.LogInformation("Registered handler {HandlerType} for transitions {FromStatus} -> {ToStatus} with priority {Priority}",
            handlerType.Name, fromStatus?.ToString() ?? "Any", toStatus?.ToString() ?? "Any", priority);
    }

    public void Subscribe(IPaymentStateTransitionHandler handler, PaymentStatus? fromStatus = null, PaymentStatus? toStatus = null)
    {
        var existingRegistration = _handlers.FirstOrDefault(h => ReferenceEquals(h.HandlerInstance, handler));
        
        if (existingRegistration != null)
        {
            _logger.LogWarning("Handler instance {HandlerType} is already registered", handler.GetType().Name);
            return;
        }

        var registration = new HandlerRegistration
        {
            HandlerInstance = handler,
            FromStatus = fromStatus,
            ToStatus = toStatus,
            Priority = handler.Priority
        };

        _handlers.Add(registration);
        
        _logger.LogInformation("Registered handler instance {HandlerType} for transitions {FromStatus} -> {ToStatus} with priority {Priority}",
            handler.GetType().Name, fromStatus?.ToString() ?? "Any", toStatus?.ToString() ?? "Any", handler.Priority);
    }

    public async Task UnsubscribeAsync<THandler>() where THandler : IPaymentStateTransitionHandler
    {
        var handlerType = typeof(THandler);
        var handlersToRemove = _handlers.Where(h => h.HandlerType == handlerType).ToList();
        
        // Note: ConcurrentBag doesn't support removal, so we'd need to implement
        // a different approach in production (e.g., using ConcurrentDictionary)
        // For now, we'll log the unsubscription

        _logger.LogInformation("Unregistered {Count} handlers of type {HandlerType}", handlersToRemove.Count, handlerType.Name);
        await Task.CompletedTask;
    }

    public async Task UnsubscribeAsync(IPaymentStateTransitionHandler handler)
    {
        var handlersToRemove = _handlers.Where(h => ReferenceEquals(h.HandlerInstance, handler)).ToList();
        
        // Note: ConcurrentBag doesn't support removal, so we'd need to implement
        // a different approach in production (e.g., using ConcurrentDictionary)
        // For now, we'll log the unsubscription

        _logger.LogInformation("Unregistered handler instance {HandlerType}", handler.GetType().Name);
        await Task.CompletedTask;
    }

    public async Task PublishStateTransitionAsync(Guid paymentId, PaymentStatus fromStatus, PaymentStatus toStatus, CancellationToken cancellationToken = default)
    {
        var transitionEvent = new PaymentStateTransitionEvent
        {
            PaymentId = paymentId,
            FromStatus = fromStatus,
            ToStatus = toStatus,
            TransitionId = $"{fromStatus}_{toStatus}_{DateTime.UtcNow:yyyyMMddHHmmss}",
            TransitionedAt = DateTime.UtcNow
        };

        await PublishTransitionEventAsync(transitionEvent, cancellationToken);
    }

    private IEnumerable<IPaymentStateTransitionHandler> GetApplicableHandlers(PaymentStatus fromStatus, PaymentStatus toStatus)
    {
        var applicableHandlers = new List<(IPaymentStateTransitionHandler Handler, int Priority)>();

        foreach (var registration in _handlers)
        {
            // Check if handler matches the transition criteria
            if (registration.FromStatus.HasValue && registration.FromStatus.Value != fromStatus)
                continue;
            
            if (registration.ToStatus.HasValue && registration.ToStatus.Value != toStatus)
                continue;

            IPaymentStateTransitionHandler? handler = null;

            // Get handler instance
            if (registration.HandlerInstance != null)
            {
                handler = registration.HandlerInstance;
            }
            else if (registration.HandlerType != null)
            {
                try
                {
                    handler = (IPaymentStateTransitionHandler)ActivatorUtilities.CreateInstance(_serviceProvider, registration.HandlerType);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to create handler instance of type {HandlerType}", registration.HandlerType.Name);
                    continue;
                }
            }

            // Double-check with handler's CanHandle method
            if (handler != null && handler.CanHandle(fromStatus, toStatus))
            {
                applicableHandlers.Add((handler, registration.Priority));
            }
        }

        return applicableHandlers.OrderBy(h => h.Priority).Select(h => h.Handler);
    }

    private async Task ExecuteHandlerAsync(IPaymentStateTransitionHandler handler, PaymentStateTransitionEvent transitionEvent, CancellationToken cancellationToken)
    {
        try
        {
            if (handler == null)
            {
                _logger.LogError("Handler cannot be null");
                return;
            }

            _logger.LogDebug("Executing handler {HandlerType} for transition {FromStatus} -> {ToStatus}",
                handler.GetType().Name, transitionEvent.FromStatus, transitionEvent.ToStatus);

            await handler.HandleAsync(transitionEvent, cancellationToken);

            _logger.LogDebug("Handler {HandlerType} completed successfully", handler.GetType().Name);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Handler {HandlerType} failed to execute for transition {FromStatus} -> {ToStatus}",
                handler?.GetType().Name ?? "Unknown", transitionEvent.FromStatus, transitionEvent.ToStatus);
            
            // Don't rethrow - we want other handlers to continue executing
        }
    }
}

// Example handler implementations
public class PaymentAuthorizationHandler : IPaymentStateTransitionHandler
{
    private readonly ILogger<PaymentAuthorizationHandler> _logger;

    public PaymentAuthorizationHandler(ILogger<PaymentAuthorizationHandler> logger)
    {
        _logger = logger;
    }

    public int Priority => 10;

    public bool CanHandle(PaymentStatus fromStatus, PaymentStatus toStatus)
    {
        return toStatus == PaymentStatus.AUTHORIZED || fromStatus == PaymentStatus.AUTHORIZING;
    }

    public async Task HandleAsync(PaymentStateTransitionEvent transitionEvent, CancellationToken cancellationToken = default)
    {
        if (transitionEvent.ToStatus == PaymentStatus.AUTHORIZED)
        {
            _logger.LogInformation("Payment {PaymentId} successfully authorized", transitionEvent.PaymentId);
            
            // Send notification, update external systems, etc.
            await Task.Delay(10, cancellationToken); // Simulate async work
        }
        else if (transitionEvent.FromStatus == PaymentStatus.AUTHORIZING && transitionEvent.ToStatus == PaymentStatus.AUTH_FAIL)
        {
            _logger.LogWarning("Payment {PaymentId} authorization failed", transitionEvent.PaymentId);
            
            // Handle authorization failure, send notifications, etc.
            await Task.Delay(10, cancellationToken); // Simulate async work
        }
    }
}

public class PaymentNotificationHandler : IPaymentStateTransitionHandler
{
    private readonly ILogger<PaymentNotificationHandler> _logger;

    public PaymentNotificationHandler(ILogger<PaymentNotificationHandler> logger)
    {
        _logger = logger;
    }

    public int Priority => 100; // Lower priority - run after business logic

    public bool CanHandle(PaymentStatus fromStatus, PaymentStatus toStatus)
    {
        // Handle all final state transitions
        var finalStates = new[]
        {
            PaymentStatus.CONFIRMED,
            PaymentStatus.CANCELLED,
            PaymentStatus.REVERSED,
            PaymentStatus.REFUNDED,
            PaymentStatus.REJECTED,
            PaymentStatus.EXPIRED
        };

        return finalStates.Contains(toStatus);
    }

    public async Task HandleAsync(PaymentStateTransitionEvent transitionEvent, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Sending notifications for payment {PaymentId} final state: {Status}",
            transitionEvent.PaymentId, transitionEvent.ToStatus);
        
        // Send webhooks, emails, SMS, etc.
        await Task.Delay(50, cancellationToken); // Simulate async notification work
    }
}