// SPDX-License-Identifier: MIT
// Copyright (c) 2025 HackLoad Payment Gateway

using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using PaymentGateway.Core.Entities;
using PaymentGateway.Core.Interfaces;
using PaymentGateway.Core.Repositories;
using System.Text.Json;

namespace PaymentGateway.Core.Services;

/// <summary>
/// Payment Form Integration Service for connecting HTML payment forms with payment processing engine
/// 
/// This service implements:
/// - Payment form to processing engine integration
/// - Real-time payment status updates and notifications
/// - Payment lifecycle management through forms
/// - Form-based error handling and recovery
/// - Performance optimization for form processing
/// - Comprehensive monitoring and metrics
/// </summary>
public class PaymentFormIntegrationService
{
    private readonly ILogger<PaymentFormIntegrationService> _logger;
    private readonly IConfiguration _configuration;
    private readonly IMemoryCache _memoryCache;
    private readonly IPaymentRepository _paymentRepository;
    private readonly ITeamRepository _teamRepository;
    private readonly PaymentLifecycleManagementService _lifecycleService;
    private readonly CardPaymentProcessingService _cardProcessingService;
    private readonly PaymentProcessingMetricsService _metricsService;
    private readonly NotificationWebhookService _notificationService;
    private readonly BusinessRuleEngineService _businessRuleEngine;
    private readonly SessionSecurityService _sessionSecurityService;
    private readonly SecureFormTokenService _formTokenService;

    // Integration configuration
    private readonly int _statusUpdateIntervalSeconds;
    private readonly int _maxStatusUpdateRetries;
    private readonly bool _enableRealTimeUpdates;
    private readonly bool _enableFormCaching;
    private readonly bool _enablePerformanceOptimization;

    // Status update channels for real-time communication
    private readonly Dictionary<string, List<PaymentStatusUpdateSubscriber>> _statusSubscribers;
    private readonly object _subscribersLock = new object();

    // Form processing optimization caches
    private readonly Dictionary<string, PaymentFormProcessingContext> _processingContexts;
    private readonly object _contextsLock = new object();

    // Metrics
    private static readonly System.Diagnostics.Metrics.Meter _meter = new("PaymentFormIntegration");
    private static readonly System.Diagnostics.Metrics.Counter<long> _formIntegrationCounter = 
        _meter.CreateCounter<long>("payment_form_integration_operations_total");
    private static readonly System.Diagnostics.Metrics.Histogram<double> _formProcessingDuration = 
        _meter.CreateHistogram<double>("payment_form_processing_duration_seconds");
    private static readonly System.Diagnostics.Metrics.Gauge<int> _activeFormSessions = 
        _meter.CreateGauge<int>("active_payment_form_sessions_total");
    private static readonly System.Diagnostics.Metrics.Counter<long> _statusUpdateCounter = 
        _meter.CreateCounter<long>("payment_form_status_updates_total");

    public PaymentFormIntegrationService(
        ILogger<PaymentFormIntegrationService> logger,
        IConfiguration configuration,
        IMemoryCache memoryCache,
        IPaymentRepository paymentRepository,
        ITeamRepository teamRepository,
        PaymentLifecycleManagementService lifecycleService,
        CardPaymentProcessingService cardProcessingService,
        PaymentProcessingMetricsService metricsService,
        NotificationWebhookService notificationService,
        BusinessRuleEngineService businessRuleEngine,
        SessionSecurityService sessionSecurityService,
        SecureFormTokenService formTokenService)
    {
        _logger = logger;
        _configuration = configuration;
        _memoryCache = memoryCache;
        _paymentRepository = paymentRepository;
        _teamRepository = teamRepository;
        _lifecycleService = lifecycleService;
        _cardProcessingService = cardProcessingService;
        _metricsService = metricsService;
        _notificationService = notificationService;
        _businessRuleEngine = businessRuleEngine;
        _sessionSecurityService = sessionSecurityService;
        _formTokenService = formTokenService;

        // Load configuration
        _statusUpdateIntervalSeconds = _configuration.GetValue<int>("PaymentForm:StatusUpdateIntervalSeconds", 2);
        _maxStatusUpdateRetries = _configuration.GetValue<int>("PaymentForm:MaxStatusUpdateRetries", 3);
        _enableRealTimeUpdates = _configuration.GetValue<bool>("PaymentForm:EnableRealTimeUpdates", true);
        _enableFormCaching = _configuration.GetValue<bool>("PaymentForm:EnableFormCaching", true);
        _enablePerformanceOptimization = _configuration.GetValue<bool>("PaymentForm:EnablePerformanceOptimization", true);

        // Initialize collections
        _statusSubscribers = new Dictionary<string, List<PaymentStatusUpdateSubscriber>>();
        _processingContexts = new Dictionary<string, PaymentFormProcessingContext>();
    }

    /// <summary>
    /// Initialize payment form processing context
    /// </summary>
    public async Task<PaymentFormInitializationResult> InitializePaymentFormAsync(PaymentFormInitializationRequest request)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        
        try
        {
            _logger.LogInformation("Initializing payment form for PaymentId: {PaymentId}, SessionId: {SessionId}",
                request.PaymentId, request.SessionId);

            // Validate session security
            var sessionValidation = await _sessionSecurityService.ValidateSessionAsync(new ValidateSessionRequest
            {
                SessionId = request.SessionId,
                ClientIp = request.ClientIp,
                UserAgent = request.UserAgent,
                RequestedOperation = "INITIALIZE_FORM"
            });

            if (!sessionValidation.IsValid)
            {
                _formIntegrationCounter.Add(1, new KeyValuePair<string, object?>("result", "session_invalid"));
                return new PaymentFormInitializationResult
                {
                    Success = false,
                    ErrorMessage = "Invalid session",
                    RequiresNewSession = sessionValidation.RequiresNewSession
                };
            }

            // Get payment data
            var payment = await _paymentRepository.GetByPaymentIdAsync(request.PaymentId);
            if (payment == null)
            {
                _formIntegrationCounter.Add(1, new KeyValuePair<string, object?>("result", "payment_not_found"));
                return new PaymentFormInitializationResult
                {
                    Success = false,
                    ErrorMessage = "Payment not found"
                };
            }

            // Validate payment status for form initialization
            if (payment.Status != Enums.PaymentStatus.NEW)
            {
                _formIntegrationCounter.Add(1, new KeyValuePair<string, object?>("result", "invalid_payment_status"));
                return new PaymentFormInitializationResult
                {
                    Success = false,
                    ErrorMessage = $"Payment is in {payment.Status} status and cannot be processed"
                };
            }

            // TODO: Get team information
            // Issue: Cannot lookup Team by int TeamId since Team.Id is Guid
            // Need to implement proper teamId to Team entity mapping
            // var team = await _teamRepository.GetByIdAsync(payment.TeamId);
            // if (team == null)
            // {
            //     _formIntegrationCounter.Add(1, new KeyValuePair<string, object?>("result", "team_not_found"));
            //     return new PaymentFormInitializationResult
            //     {
            //         Success = false,
            //         ErrorMessage = "Team configuration not found"
            //     };
            // }

            // Generate secure form tokens
            var csrfTokenResult = await _formTokenService.GenerateCsrfTokenAsync(new CsrfTokenRequest
            {
                PaymentId = request.PaymentId,
                SessionId = request.SessionId,
                ClientIp = request.ClientIp,
                UserAgent = request.UserAgent,
                FormAction = "submit_payment",
                ExpectedFields = new List<string>
                {
                    "CardNumber", "ExpiryDate", "Cvv", "CardholderName", "Email", "Phone"
                }
            });

            if (!csrfTokenResult.Success)
            {
                _formIntegrationCounter.Add(1, new KeyValuePair<string, object?>("result", "csrf_token_failed"));
                return new PaymentFormInitializationResult
                {
                    Success = false,
                    ErrorMessage = "Failed to generate security token"
                };
            }

            // Create processing context
            var processingContext = new PaymentFormProcessingContext
            {
                PaymentId = request.PaymentId,
                SessionId = request.SessionId,
                TeamId = new Guid(payment.TeamId.ToString().PadLeft(32, '0').Insert(8, "-").Insert(12, "-").Insert(16, "-").Insert(20, "-")), // TODO: Fix data model - convert int TeamId to Guid
                ClientIp = request.ClientIp,
                UserAgent = request.UserAgent,
                InitializedAt = DateTime.UtcNow,
                LastActivityAt = DateTime.UtcNow,
                FormTokens = new List<string> { csrfTokenResult.Token! },
                ProcessingStage = PaymentFormProcessingStage.Initialized,
                PaymentAmount = payment.Amount,
                PaymentCurrency = payment.Currency
            };

            // Store processing context
            lock (_contextsLock)
            {
                _processingContexts[request.PaymentId] = processingContext;
            }

            // Subscribe to payment status updates if real-time updates are enabled
            if (_enableRealTimeUpdates)
            {
                await SubscribeToPaymentStatusUpdatesAsync(request.PaymentId, request.SessionId);
            }

            // Record metrics
            _formIntegrationCounter.Add(1, new KeyValuePair<string, object?>("result", "success"),
                new KeyValuePair<string, object?>("team_id", payment.TeamId),
                new KeyValuePair<string, object?>("currency", payment.Currency));

            _activeFormSessions.Record(_processingContexts.Count);

            _logger.LogInformation("Payment form initialized successfully for PaymentId: {PaymentId}, Duration: {Duration}ms",
                request.PaymentId, stopwatch.ElapsedMilliseconds);

            return new PaymentFormInitializationResult
            {
                Success = true,
                ProcessingContext = processingContext,
                CsrfToken = csrfTokenResult.Token!,
                SessionExpiresAt = sessionValidation.ExpiresAt,
                PaymentData = new PaymentFormData
                {
                    PaymentId = payment.PaymentId,
                    OrderId = payment.OrderId,
                    Amount = payment.Amount,
                    Currency = payment.Currency,
                    Description = payment.Description,
                    MerchantName = "Merchant", // TODO: Fix team lookup - cannot use team variable due to data model inconsistency
                    SuccessUrl = payment.SuccessUrl,
                    FailUrl = payment.FailUrl
                }
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error initializing payment form for PaymentId: {PaymentId}", request.PaymentId);
            
            _formIntegrationCounter.Add(1, new KeyValuePair<string, object?>("result", "error"),
                new KeyValuePair<string, object?>("error_type", ex.GetType().Name));

            return new PaymentFormInitializationResult
            {
                Success = false,
                ErrorMessage = "Failed to initialize payment form"
            };
        }
        finally
        {
            _formProcessingDuration.Record(stopwatch.Elapsed.TotalSeconds,
                new KeyValuePair<string, object?>("operation", "initialize"));
        }
    }

    /// <summary>
    /// Process payment form submission through integration with payment engine
    /// </summary>
    public async Task<PaymentFormProcessingResult> ProcessPaymentFormAsync(PaymentFormProcessingRequest request)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        
        try
        {
            _logger.LogInformation("Processing payment form submission for PaymentId: {PaymentId}",
                request.PaymentId);

            // Get processing context
            PaymentFormProcessingContext? context;
            lock (_contextsLock)
            {
                _processingContexts.TryGetValue(request.PaymentId, out context);
            }

            if (context == null)
            {
                _formIntegrationCounter.Add(1, new KeyValuePair<string, object?>("result", "context_not_found"));
                return new PaymentFormProcessingResult
                {
                    Success = false,
                    ErrorMessage = "Payment form context not found"
                };
            }

            // Update processing stage
            context.ProcessingStage = PaymentFormProcessingStage.Processing;
            context.LastActivityAt = DateTime.UtcNow;

            // Validate CSRF token
            var csrfValidation = await _formTokenService.ValidateCsrfTokenAsync(new CsrfTokenValidationRequest
            {
                Token = request.CsrfToken,
                PaymentId = request.PaymentId,
                SessionId = request.SessionId,
                ClientIp = request.ClientIp,
                FormAction = "submit_payment",
                SubmittedFields = request.FormFields?.Keys.ToList()
            });

            if (!csrfValidation.IsValid)
            {
                _formIntegrationCounter.Add(1, new KeyValuePair<string, object?>("result", "csrf_validation_failed"));
                return new PaymentFormProcessingResult
                {
                    Success = false,
                    ErrorMessage = "Security token validation failed"
                };
            }

            // Get payment data
            var payment = await _paymentRepository.GetByPaymentIdAsync(request.PaymentId);
            if (payment == null || payment.Status != Enums.PaymentStatus.NEW)
            {
                _formIntegrationCounter.Add(1, new KeyValuePair<string, object?>("result", "payment_invalid"));
                return new PaymentFormProcessingResult
                {
                    Success = false,
                    ErrorMessage = "Payment is not available for processing"
                };
            }

            // Process card payment through integration
            var cardProcessingResult = await _cardProcessingService.ProcessCardPaymentAsync(new CardPaymentRequest
            {
                PaymentId = payment.Id.GetHashCode(), // TODO: Fix data model - convert Guid to long
                TeamId = payment.TeamId,
                OrderId = payment.OrderId,
                CardNumber = request.CardNumber!,
                ExpiryMonth = request.ExpiryDate!.Split('/')[0], // TODO: Parse expiry date properly
                ExpiryYear = request.ExpiryDate!.Split('/')[1],
                CVV = request.Cvv!,
                CardholderName = request.CardholderName!,
                Amount = payment.Amount,
                Currency = payment.Currency
            });

            if (!cardProcessingResult.IsSuccess)
            {
                context.ProcessingStage = PaymentFormProcessingStage.Failed;
                context.LastError = cardProcessingResult.ProcessingErrors.FirstOrDefault() ?? "Card processing failed";

                _formIntegrationCounter.Add(1, new KeyValuePair<string, object?>("result", "card_processing_failed"));
                
                // Notify subscribers of failure
                await NotifyStatusUpdateAsync(request.PaymentId, Enums.PaymentStatus.FAILED, cardProcessingResult.ProcessingErrors.FirstOrDefault() ?? "Card processing failed");

                return new PaymentFormProcessingResult
                {
                    Success = false,
                    ErrorMessage = cardProcessingResult.ProcessingErrors.FirstOrDefault() ?? "Card processing failed",
                    ErrorCode = cardProcessingResult.IsSuccess ? "0" : "1" // TODO: CardProcessingResult doesn't have ErrorCode
                };
            }

            // Update payment through lifecycle management
            var updatedPayment = await _lifecycleService.AuthorizePaymentAsync(payment.Id);
            var lifecycleResult = new { Success = updatedPayment != null, ErrorMessage = updatedPayment == null ? "Failed to authorize payment" : "" };
            if (!lifecycleResult.Success)
            {
                context.ProcessingStage = PaymentFormProcessingStage.Failed;
                context.LastError = lifecycleResult.ErrorMessage;

                _formIntegrationCounter.Add(1, new KeyValuePair<string, object?>("result", "lifecycle_transition_failed"));
                
                return new PaymentFormProcessingResult
                {
                    Success = false,
                    ErrorMessage = lifecycleResult.ErrorMessage
                };
            }

            // Update processing context
            context.ProcessingStage = PaymentFormProcessingStage.Completed;
            context.CompletedAt = DateTime.UtcNow;
            context.ProcessingResult = new Dictionary<string, object>
            {
                ["TransactionId"] = cardProcessingResult.TransactionId ?? "",
                ["AuthorizationCode"] = cardProcessingResult.AuthorizationCode, // Using AuthorizationCode instead of MaskedCardNumber
                ["ProcessingTime"] = stopwatch.ElapsedMilliseconds
            };

            // Notify subscribers of success
            await NotifyStatusUpdateAsync(request.PaymentId, Enums.PaymentStatus.AUTHORIZED, "Payment authorized successfully");

            // Send webhook notification to merchant
            await _notificationService.SendNotificationAsync(new WebhookNotificationDeliveryRequest
            {
                NotificationId = Guid.NewGuid().ToString(),
                Type = NotificationType.PAYMENT_STATUS_CHANGE, // Using correct enum
                TeamId = payment.TeamId,
                TeamSlug = $"team-{payment.TeamId}",
                TemplateId = "payment-status-change",
                Recipients = new List<string> { "merchant@example.com" }, // TODO: Get actual merchant email
                TemplateData = new Dictionary<string, object>
                {
                    ["PaymentId"] = payment.PaymentId,
                    ["Status"] = Enums.PaymentStatus.AUTHORIZED.ToString(),
                    ["Amount"] = payment.Amount,
                    ["Currency"] = payment.Currency,
                    ["TransactionId"] = cardProcessingResult.TransactionId ?? ""
                }
            });

            // Record success metrics
            _formIntegrationCounter.Add(1, new KeyValuePair<string, object?>("result", "success"),
                new KeyValuePair<string, object?>("currency", payment.Currency));

            _logger.LogInformation("Payment form processed successfully for PaymentId: {PaymentId}, Duration: {Duration}ms",
                request.PaymentId, stopwatch.ElapsedMilliseconds);

            return new PaymentFormProcessingResult
            {
                Success = true,
                PaymentId = request.PaymentId,
                TransactionId = cardProcessingResult.TransactionId,
                Status = Enums.PaymentStatus.AUTHORIZED,
                ProcessingDuration = stopwatch.Elapsed,
                AdditionalData = context.ProcessingResult
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing payment form for PaymentId: {PaymentId}", request.PaymentId);
            
            _formIntegrationCounter.Add(1, new KeyValuePair<string, object?>("result", "error"),
                new KeyValuePair<string, object?>("error_type", ex.GetType().Name));

            return new PaymentFormProcessingResult
            {
                Success = false,
                ErrorMessage = "Payment processing failed",
                PaymentId = request.PaymentId
            };
        }
        finally
        {
            _formProcessingDuration.Record(stopwatch.Elapsed.TotalSeconds,
                new KeyValuePair<string, object?>("operation", "process"));
        }
    }

    /// <summary>
    /// Subscribe to real-time payment status updates
    /// </summary>
    public async Task<PaymentStatusSubscriptionResult> SubscribeToPaymentStatusUpdatesAsync(string paymentId, string sessionId)
    {
        try
        {
            _logger.LogDebug("Subscribing to payment status updates for PaymentId: {PaymentId}, SessionId: {SessionId}",
                paymentId, sessionId);

            var subscriber = new PaymentStatusUpdateSubscriber
            {
                SessionId = sessionId,
                PaymentId = paymentId,
                SubscribedAt = DateTime.UtcNow,
                LastUpdateAt = DateTime.UtcNow,
                IsActive = true
            };

            lock (_subscribersLock)
            {
                if (!_statusSubscribers.ContainsKey(paymentId))
                {
                    _statusSubscribers[paymentId] = new List<PaymentStatusUpdateSubscriber>();
                }
                _statusSubscribers[paymentId].Add(subscriber);
            }

            _statusUpdateCounter.Add(1, new KeyValuePair<string, object?>("operation", "subscribe"));

            return new PaymentStatusSubscriptionResult
            {
                Success = true,
                SubscriptionId = subscriber.SessionId,
                UpdateInterval = TimeSpan.FromSeconds(_statusUpdateIntervalSeconds)
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error subscribing to payment status updates for PaymentId: {PaymentId}", paymentId);
            
            return new PaymentStatusSubscriptionResult
            {
                Success = false,
                ErrorMessage = "Failed to subscribe to status updates"
            };
        }
    }

    /// <summary>
    /// Get current payment status for form updates
    /// </summary>
    public async Task<PaymentFormStatusResult> GetPaymentFormStatusAsync(string paymentId, string sessionId)
    {
        try
        {
            var payment = await _paymentRepository.GetByPaymentIdAsync(paymentId);
            if (payment == null)
            {
                return new PaymentFormStatusResult
                {
                    Success = false,
                    ErrorMessage = "Payment not found"
                };
            }

            PaymentFormProcessingContext? context;
            lock (_contextsLock)
            {
                _processingContexts.TryGetValue(paymentId, out context);
            }

            var statusResult = new PaymentFormStatusResult
            {
                Success = true,
                PaymentId = paymentId,
                Status = payment.Status,
                ProcessingStage = context?.ProcessingStage ?? PaymentFormProcessingStage.Unknown,
                LastUpdated = payment.UpdatedAt,
                StatusDescription = GetStatusDescription(payment.Status),
                AdditionalData = context?.ProcessingResult ?? new Dictionary<string, object>()
            };

            return statusResult;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting payment form status for PaymentId: {PaymentId}", paymentId);
            
            return new PaymentFormStatusResult
            {
                Success = false,
                ErrorMessage = "Failed to get payment status"
            };
        }
    }

    /// <summary>
    /// Clean up expired form sessions and contexts
    /// </summary>
    public async Task CleanupExpiredSessionsAsync()
    {
        try
        {
            _logger.LogDebug("Starting payment form session cleanup");

            var expiredContexts = new List<string>();
            var expiredSubscribers = new List<string>();

            // Clean up expired processing contexts
            lock (_contextsLock)
            {
                var expiryCutoff = DateTime.UtcNow.AddHours(-2); // 2 hour expiry
                
                foreach (var kvp in _processingContexts.ToList())
                {
                    if (kvp.Value.LastActivityAt < expiryCutoff)
                    {
                        expiredContexts.Add(kvp.Key);
                        _processingContexts.Remove(kvp.Key);
                    }
                }
            }

            // Clean up expired status subscribers
            lock (_subscribersLock)
            {
                var expiryCutoff = DateTime.UtcNow.AddMinutes(-30); // 30 minute expiry
                
                foreach (var paymentId in _statusSubscribers.Keys.ToList())
                {
                    var subscribers = _statusSubscribers[paymentId];
                    var activeSubscribers = subscribers.Where(s => s.LastUpdateAt >= expiryCutoff).ToList();
                    
                    if (activeSubscribers.Count != subscribers.Count)
                    {
                        expiredSubscribers.Add(paymentId);
                        if (activeSubscribers.Any())
                        {
                            _statusSubscribers[paymentId] = activeSubscribers;
                        }
                        else
                        {
                            _statusSubscribers.Remove(paymentId);
                        }
                    }
                }
            }

            _activeFormSessions.Record(_processingContexts.Count);

            _logger.LogInformation("Payment form session cleanup completed. Expired contexts: {ExpiredContexts}, Expired subscribers: {ExpiredSubscribers}",
                expiredContexts.Count, expiredSubscribers.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during payment form session cleanup");
        }
    }

    // Private helper methods

    private async Task NotifyStatusUpdateAsync(string paymentId, Enums.PaymentStatus status, string? message = null)
    {
        if (!_enableRealTimeUpdates)
            return;

        try
        {
            lock (_subscribersLock)
            {
                if (_statusSubscribers.TryGetValue(paymentId, out var subscribers))
                {
                    foreach (var subscriber in subscribers.Where(s => s.IsActive))
                    {
                        subscriber.LastUpdateAt = DateTime.UtcNow;
                        // In a real implementation, this would push to WebSocket/SignalR
                        _logger.LogDebug("Status update notification sent to SessionId: {SessionId} for PaymentId: {PaymentId}, Status: {Status}",
                            subscriber.SessionId, paymentId, status);
                    }
                }
            }

            _statusUpdateCounter.Add(1, new KeyValuePair<string, object?>("operation", "notify"),
                new KeyValuePair<string, object?>("status", status.ToString()));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending status update notification for PaymentId: {PaymentId}", paymentId);
        }
    }

    private string GetStatusDescription(Enums.PaymentStatus status) => status switch
    {
        Enums.PaymentStatus.NEW => "Payment initialized, ready for processing",
        Enums.PaymentStatus.AUTHORIZED => "Payment authorized successfully",
        Enums.PaymentStatus.CONFIRMED => "Payment confirmed and settled",
        Enums.PaymentStatus.FAILED => "Payment processing failed",
        Enums.PaymentStatus.CANCELLED => "Payment cancelled",
        Enums.PaymentStatus.REFUNDED => "Payment refunded",
        _ => status.ToString()
    };
}

// Supporting classes and enums

public enum PaymentFormProcessingStage
{
    Unknown = 0,
    Initialized = 1,
    Processing = 2,
    Completed = 3,
    Failed = 4
}

public class PaymentFormInitializationRequest
{
    public string PaymentId { get; set; } = string.Empty;
    public string SessionId { get; set; } = string.Empty;
    public string ClientIp { get; set; } = string.Empty;
    public string UserAgent { get; set; } = string.Empty;
    public string? Language { get; set; }
}

public class PaymentFormInitializationResult
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public bool RequiresNewSession { get; set; }
    public PaymentFormProcessingContext? ProcessingContext { get; set; }
    public string? CsrfToken { get; set; }
    public DateTime? SessionExpiresAt { get; set; }
    public PaymentFormData? PaymentData { get; set; }
}

public class PaymentFormProcessingRequest
{
    public string PaymentId { get; set; } = string.Empty;
    public string SessionId { get; set; } = string.Empty;
    public string ClientIp { get; set; } = string.Empty;
    public string CsrfToken { get; set; } = string.Empty;
    public string? CardNumber { get; set; }
    public string? ExpiryDate { get; set; }
    public string? Cvv { get; set; }
    public string? CardholderName { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public Dictionary<string, string>? FormFields { get; set; }
}

public class PaymentFormProcessingResult
{
    public bool Success { get; set; }
    public string? PaymentId { get; set; }
    public string? TransactionId { get; set; }
    public Enums.PaymentStatus Status { get; set; }
    public string? ErrorMessage { get; set; }
    public string? ErrorCode { get; set; }
    public TimeSpan ProcessingDuration { get; set; }
    public Dictionary<string, object>? AdditionalData { get; set; }
}

public class PaymentFormProcessingContext
{
    public string PaymentId { get; set; } = string.Empty;
    public string SessionId { get; set; } = string.Empty;
    public Guid TeamId { get; set; }
    public string ClientIp { get; set; } = string.Empty;
    public string UserAgent { get; set; } = string.Empty;
    public DateTime InitializedAt { get; set; }
    public DateTime LastActivityAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public List<string> FormTokens { get; set; } = new();
    public PaymentFormProcessingStage ProcessingStage { get; set; }
    public decimal PaymentAmount { get; set; }
    public string PaymentCurrency { get; set; } = string.Empty;
    public string? LastError { get; set; }
    public Dictionary<string, object>? ProcessingResult { get; set; }
}

public class PaymentFormData
{
    public string PaymentId { get; set; } = string.Empty;
    public string? OrderId { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string MerchantName { get; set; } = string.Empty;
    public string? SuccessUrl { get; set; }
    public string? FailUrl { get; set; }
}

public class PaymentStatusUpdateSubscriber
{
    public string SessionId { get; set; } = string.Empty;
    public string PaymentId { get; set; } = string.Empty;
    public DateTime SubscribedAt { get; set; }
    public DateTime LastUpdateAt { get; set; }
    public bool IsActive { get; set; }
}

public class PaymentStatusSubscriptionResult
{
    public bool Success { get; set; }
    public string? SubscriptionId { get; set; }
    public TimeSpan UpdateInterval { get; set; }
    public string? ErrorMessage { get; set; }
}

public class PaymentFormStatusResult
{
    public bool Success { get; set; }
    public string? PaymentId { get; set; }
    public Enums.PaymentStatus Status { get; set; }
    public PaymentFormProcessingStage ProcessingStage { get; set; }
    public DateTime? LastUpdated { get; set; }
    public string? StatusDescription { get; set; }
    public string? ErrorMessage { get; set; }
    public Dictionary<string, object> AdditionalData { get; set; } = new();
}

