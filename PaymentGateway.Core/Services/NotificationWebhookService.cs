// SPDX-License-Identifier: MIT
// Copyright (c) 2025 HackLoad Payment Gateway

using PaymentGateway.Core.Entities;
using PaymentGateway.Core.Interfaces;
using PaymentGateway.Core.Repositories;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Prometheus;

namespace PaymentGateway.Core.Services;

/// <summary>
/// Notification and webhook service with delivery system, retry logic, template management, and security
/// </summary>
public interface INotificationWebhookService
{
    Task<NotificationDeliveryResult> SendWebhookAsync(WebhookNotificationRequest request, CancellationToken cancellationToken = default);
    Task<NotificationDeliveryResult> SendNotificationAsync(WebhookNotificationDeliveryRequest request, CancellationToken cancellationToken = default);
    Task<IEnumerable<NotificationDeliveryAttempt>> GetDeliveryAttemptsAsync(string notificationId, CancellationToken cancellationToken = default);
    Task<NotificationDeliveryStatistics> GetDeliveryStatisticsAsync(Guid? teamId = null, TimeSpan? period = null, CancellationToken cancellationToken = default);
    Task<NotificationTemplate> CreateNotificationTemplateAsync(NotificationTemplate template, CancellationToken cancellationToken = default);
    Task<NotificationTemplate> UpdateNotificationTemplateAsync(NotificationTemplate template, CancellationToken cancellationToken = default);
    Task<IEnumerable<NotificationTemplate>> GetNotificationTemplatesAsync(Guid teamId, NotificationType type, CancellationToken cancellationToken = default);
    Task<bool> ValidateWebhookSignatureAsync(string payload, string signature, string secret, CancellationToken cancellationToken = default);
    Task<string> GenerateWebhookSignatureAsync(string payload, string secret, CancellationToken cancellationToken = default);
    Task<NotificationRateLimitStatus> CheckRateLimitAsync(Guid teamId, NotificationType type, CancellationToken cancellationToken = default);
}

public class WebhookNotificationRequest
{
    public Guid TeamId { get; set; }
    public string TeamSlug { get; set; } = string.Empty;
    public string WebhookUrl { get; set; } = string.Empty;
    public string EventType { get; set; } = string.Empty;
    public Dictionary<string, object> Payload { get; set; } = new();
    public string NotificationId { get; set; } = string.Empty;
    public int Priority { get; set; } = 5; // 1 (highest) to 10 (lowest)
    public TimeSpan? Timeout { get; set; }
    public Dictionary<string, string> Headers { get; set; } = new();
    public Dictionary<string, object> Metadata { get; set; } = new();
}

public class WebhookNotificationDeliveryRequest
{
    public Guid TeamId { get; set; }
    public string TeamSlug { get; set; } = string.Empty;
    public NotificationType Type { get; set; }
    public string TemplateId { get; set; } = string.Empty;
    public Dictionary<string, object> TemplateData { get; set; } = new();
    public List<string> Recipients { get; set; } = new();
    public string NotificationId { get; set; } = string.Empty;
    public int Priority { get; set; } = 5;
    public Dictionary<string, object> Metadata { get; set; } = new();
}

public enum NotificationType
{
    PAYMENT_STATUS_CHANGE = 1,
    PAYMENT_SUCCESS = 2,
    PAYMENT_FAILURE = 3,
    PAYMENT_TIMEOUT = 4,
    PAYMENT_REFUND = 5,
    PAYMENT_CHARGEBACK = 6,
    FRAUD_ALERT = 7,
    SYSTEM_ALERT = 8,
    MAINTENANCE_NOTICE = 9,
    ACCOUNT_UPDATE = 10
}

public class NotificationDeliveryResult
{
    public string NotificationId { get; set; } = string.Empty;
    public bool IsSuccess { get; set; }
    public string Status { get; set; } = "PENDING";
    public string DeliveryMethod { get; set; } = string.Empty;
    public DateTime DeliveredAt { get; set; }
    public TimeSpan DeliveryDuration { get; set; }
    public int AttemptCount { get; set; }
    public DateTime NextRetryAt { get; set; }
    public List<string> Errors { get; set; } = new();
    public Dictionary<string, object> DeliveryMetadata { get; set; } = new();
}

public class NotificationDeliveryAttempt : BaseEntity
{
    public string NotificationId { get; set; } = string.Empty;
    public Guid TeamId { get; set; }
    public NotificationType Type { get; set; }
    public string DeliveryMethod { get; set; } = string.Empty;
    public string Endpoint { get; set; } = string.Empty;
    public string Payload { get; set; } = string.Empty;
    public string Status { get; set; } = "PENDING";
    public int AttemptNumber { get; set; }
    public DateTime AttemptedAt { get; set; }
    public TimeSpan Duration { get; set; }
    public int? ResponseCode { get; set; }
    public string ResponseBody { get; set; } = string.Empty;
    public string ErrorMessage { get; set; } = string.Empty;
    public DateTime? NextRetryAt { get; set; }
    public Dictionary<string, object> AttemptMetadata { get; set; } = new();
}

public class NotificationTemplate : BaseEntity
{
    public Guid TeamId { get; set; }
    public NotificationType Type { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public string BodyTemplate { get; set; } = string.Empty;
    public string Format { get; set; } = "JSON"; // JSON, XML, FORM
    public Dictionary<string, object> DefaultData { get; set; } = new();
    public List<string> RequiredFields { get; set; } = new();
    public bool IsActive { get; set; } = true;
    public string Language { get; set; } = "en";
    public Dictionary<string, object> TemplateMetadata { get; set; } = new();
    
    // Navigation
    public Team Team { get; set; } = null!;
}

public class NotificationDeliveryStatistics
{
    public TimeSpan Period { get; set; }
    public int TotalNotifications { get; set; }
    public int SuccessfulDeliveries { get; set; }
    public int FailedDeliveries { get; set; }
    public int PendingDeliveries { get; set; }
    public double DeliverySuccessRate { get; set; }
    public TimeSpan AverageDeliveryTime { get; set; }
    public Dictionary<NotificationType, int> NotificationsByType { get; set; } = new();
    public Dictionary<string, int> NotificationsByTeam { get; set; } = new();
    public Dictionary<string, int> DeliveryMethodStats { get; set; } = new();
    public Dictionary<string, int> ErrorsByType { get; set; } = new();
    public Dictionary<int, int> ResponseCodeStats { get; set; } = new();
}

public class NotificationRateLimitStatus
{
    public bool IsAllowed { get; set; }
    public int RemainingCount { get; set; }
    public TimeSpan ResetTime { get; set; }
    public int LimitPerWindow { get; set; }
    public TimeSpan WindowDuration { get; set; }
    public string RateLimitType { get; set; } = string.Empty;
}

public class NotificationWebhookService : BackgroundService, INotificationWebhookService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IPaymentRepository _paymentRepository;
    private readonly ITeamRepository _teamRepository;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<NotificationWebhookService> _logger;
    
    // Notification processing channels
    private readonly Channel<NotificationDeliveryTask> _notificationChannel;
    private readonly ChannelWriter<NotificationDeliveryTask> _notificationWriter;
    private readonly ChannelReader<NotificationDeliveryTask> _notificationReader;
    
    // Rate limiting storage
    private readonly ConcurrentDictionary<string, NotificationRateLimit> _rateLimits = new();
    
    // Template storage (in production, this would be database)
    private readonly ConcurrentDictionary<string, NotificationTemplate> _templates = new();
    
    // Retry tracking
    private readonly ConcurrentDictionary<string, NotificationRetryState> _retryStates = new();
    
    // Metrics
    private static readonly Counter NotificationDeliveryOperations = Metrics
        .CreateCounter("notification_delivery_operations_total", "Total notification delivery operations", new[] { "team_id", "type", "method", "result" });
    
    private static readonly Histogram NotificationDeliveryDuration = Metrics
        .CreateHistogram("notification_delivery_duration_seconds", "Notification delivery operation duration", new[] { "type", "method" });
    
    private static readonly Counter NotificationRetryOperations = Metrics
        .CreateCounter("notification_retry_operations_total", "Total notification retry operations", new[] { "team_id", "type", "attempt" });
    
    private static readonly Gauge PendingNotifications = Metrics
        .CreateGauge("pending_notifications_total", "Total pending notifications", new[] { "team_id", "type", "priority" });
    
    private static readonly Counter NotificationSignatureOperations = Metrics
        .CreateCounter("notification_signature_operations_total", "Total notification signature operations", new[] { "operation", "result" });

    // Configuration
    private readonly int _maxConcurrentNotifications = Environment.ProcessorCount * 2;
    private readonly TimeSpan _defaultTimeout = TimeSpan.FromSeconds(30);
    private readonly int _maxRetryAttempts = 5;
    private readonly TimeSpan _baseRetryDelay = TimeSpan.FromSeconds(1);
    
    // Retry policies by notification type
    private static readonly Dictionary<NotificationType, RetryPolicy> RetryPolicies = new()
    {
        [NotificationType.PAYMENT_STATUS_CHANGE] = new RetryPolicy { MaxAttempts = 5, BaseDelay = TimeSpan.FromSeconds(2) },
        [NotificationType.PAYMENT_SUCCESS] = new RetryPolicy { MaxAttempts = 3, BaseDelay = TimeSpan.FromSeconds(1) },
        [NotificationType.PAYMENT_FAILURE] = new RetryPolicy { MaxAttempts = 5, BaseDelay = TimeSpan.FromSeconds(1) },
        [NotificationType.FRAUD_ALERT] = new RetryPolicy { MaxAttempts = 10, BaseDelay = TimeSpan.FromSeconds(1) },
        [NotificationType.SYSTEM_ALERT] = new RetryPolicy { MaxAttempts = 8, BaseDelay = TimeSpan.FromSeconds(5) }
    };

    // Rate limit policies by notification type and team
    private static readonly Dictionary<NotificationType, RateLimitPolicy> RateLimitPolicies = new()
    {
        [NotificationType.PAYMENT_STATUS_CHANGE] = new RateLimitPolicy { MaxPerMinute = 1000, MaxPerHour = 10000 },
        [NotificationType.PAYMENT_SUCCESS] = new RateLimitPolicy { MaxPerMinute = 500, MaxPerHour = 5000 },
        [NotificationType.PAYMENT_FAILURE] = new RateLimitPolicy { MaxPerMinute = 200, MaxPerHour = 2000 },
        [NotificationType.FRAUD_ALERT] = new RateLimitPolicy { MaxPerMinute = 100, MaxPerHour = 500 },
        [NotificationType.SYSTEM_ALERT] = new RateLimitPolicy { MaxPerMinute = 50, MaxPerHour = 200 }
    };

    public NotificationWebhookService(
        IServiceProvider serviceProvider,
        IPaymentRepository paymentRepository,
        ITeamRepository teamRepository,
        IHttpClientFactory httpClientFactory,
        ILogger<NotificationWebhookService> logger)
    {
        _serviceProvider = serviceProvider;
        _paymentRepository = paymentRepository;
        _teamRepository = teamRepository;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        
        // Configure bounded channel for notification processing
        var channelOptions = new BoundedChannelOptions(10000)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = false,
            SingleWriter = false
        };
        
        _notificationChannel = Channel.CreateBounded<NotificationDeliveryTask>(channelOptions);
        _notificationWriter = _notificationChannel.Writer;
        _notificationReader = _notificationChannel.Reader;
        
        InitializeDefaultTemplates();
    }

    public async Task<NotificationDeliveryResult> SendWebhookAsync(WebhookNotificationRequest request, CancellationToken cancellationToken = default)
    {
        using var activity = NotificationDeliveryDuration.WithLabels(request.EventType, "webhook").NewTimer();
        
        try
        {
            // Validate request
            if (string.IsNullOrEmpty(request.WebhookUrl) || string.IsNullOrEmpty(request.EventType))
            {
                NotificationDeliveryOperations.WithLabels(request.TeamId.ToString(), request.EventType, "webhook", "validation_failed").Inc();
                return new NotificationDeliveryResult
                {
                    NotificationId = request.NotificationId,
                    IsSuccess = false,
                    Status = "VALIDATION_FAILED",
                    Errors = new List<string> { "WebhookUrl and EventType are required" }
                };
            }

            // Check rate limits
            var rateLimitKey = $"webhook:{request.TeamId}:{request.EventType}";
            var rateLimit = await CheckWebhookRateLimitAsync(rateLimitKey, request.EventType, cancellationToken);
            if (!rateLimit.IsAllowed)
            {
                NotificationDeliveryOperations.WithLabels(request.TeamId.ToString(), request.EventType, "webhook", "rate_limited").Inc();
                return new NotificationDeliveryResult
                {
                    NotificationId = request.NotificationId,
                    IsSuccess = false,
                    Status = "RATE_LIMITED",
                    Errors = new List<string> { $"Rate limit exceeded. Try again in {rateLimit.ResetTime.TotalSeconds} seconds" }
                };
            }

            // Create delivery task
            var deliveryTask = new NotificationDeliveryTask
            {
                NotificationId = request.NotificationId,
                TeamId = request.TeamId,
                Type = NotificationType.PAYMENT_STATUS_CHANGE, // Default for webhooks
                Method = "webhook",
                Endpoint = request.WebhookUrl,
                Payload = JsonSerializer.Serialize(request.Payload),
                Priority = request.Priority,
                Headers = request.Headers,
                Timeout = request.Timeout ?? _defaultTimeout,
                Metadata = request.Metadata,
                CreatedAt = DateTime.UtcNow
            };

            // Queue for processing
            await _notificationWriter.WriteAsync(deliveryTask, cancellationToken);
            
            PendingNotifications.WithLabels(request.TeamId.ToString(), "PAYMENT_STATUS_CHANGE", request.Priority.ToString()).Inc();
            
            _logger.LogInformation("Webhook notification queued: {NotificationId}, Team: {TeamId}, URL: {WebhookUrl}", 
                request.NotificationId, request.TeamId, request.WebhookUrl);

            return new NotificationDeliveryResult
            {
                NotificationId = request.NotificationId,
                IsSuccess = true,
                Status = "QUEUED",
                DeliveryMethod = "webhook",
                AttemptCount = 0
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send webhook notification: {NotificationId}", request.NotificationId);
            NotificationDeliveryOperations.WithLabels(request.TeamId.ToString(), request.EventType, "webhook", "error").Inc();
            
            return new NotificationDeliveryResult
            {
                NotificationId = request.NotificationId,
                IsSuccess = false,
                Status = "ERROR",
                Errors = new List<string> { "Internal notification error" }
            };
        }
    }

    public async Task<NotificationDeliveryResult> SendNotificationAsync(WebhookNotificationDeliveryRequest request, CancellationToken cancellationToken = default)
    {
        using var activity = NotificationDeliveryDuration.WithLabels(request.Type.ToString(), "notification").NewTimer();
        
        try
        {
            // Check rate limits
            var rateLimitStatus = await CheckRateLimitAsync(request.TeamId, request.Type, cancellationToken);
            if (!rateLimitStatus.IsAllowed)
            {
                NotificationDeliveryOperations.WithLabels(request.TeamId.ToString(), request.Type.ToString(), "notification", "rate_limited").Inc();
                return new NotificationDeliveryResult
                {
                    NotificationId = request.NotificationId,
                    IsSuccess = false,
                    Status = "RATE_LIMITED",
                    Errors = new List<string> { $"Rate limit exceeded. Remaining: {rateLimitStatus.RemainingCount}" }
                };
            }

            // Get template
            var template = await GetNotificationTemplateAsync(request.TeamId, request.Type, request.TemplateId, cancellationToken);
            if (template == null)
            {
                NotificationDeliveryOperations.WithLabels(request.TeamId.ToString(), request.Type.ToString(), "notification", "template_not_found").Inc();
                return new NotificationDeliveryResult
                {
                    NotificationId = request.NotificationId,
                    IsSuccess = false,
                    Status = "TEMPLATE_NOT_FOUND",
                    Errors = new List<string> { $"Template not found: {request.TemplateId}" }
                };
            }

            // Render template
            var renderedPayload = await RenderTemplateAsync(template, request.TemplateData, cancellationToken);
            
            // Create delivery task
            var deliveryTask = new NotificationDeliveryTask
            {
                NotificationId = request.NotificationId,
                TeamId = request.TeamId,
                Type = request.Type,
                Method = "notification",
                Endpoint = string.Join(",", request.Recipients),
                Payload = renderedPayload,
                Priority = request.Priority,
                Metadata = request.Metadata,
                CreatedAt = DateTime.UtcNow
            };

            // Queue for processing
            await _notificationWriter.WriteAsync(deliveryTask, cancellationToken);
            
            PendingNotifications.WithLabels(request.TeamId.ToString(), request.Type.ToString(), request.Priority.ToString()).Inc();
            
            _logger.LogInformation("Notification queued: {NotificationId}, Team: {TeamId}, Type: {Type}", 
                request.NotificationId, request.TeamId, request.Type);

            return new NotificationDeliveryResult
            {
                NotificationId = request.NotificationId,
                IsSuccess = true,
                Status = "QUEUED",
                DeliveryMethod = "notification",
                AttemptCount = 0
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send notification: {NotificationId}", request.NotificationId);
            NotificationDeliveryOperations.WithLabels(request.TeamId.ToString(), request.Type.ToString(), "notification", "error").Inc();
            
            return new NotificationDeliveryResult
            {
                NotificationId = request.NotificationId,
                IsSuccess = false,
                Status = "ERROR",
                Errors = new List<string> { "Internal notification error" }
            };
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Notification webhook service started");
        
        // Start multiple worker tasks for concurrent processing
        var tasks = new List<Task>();
        for (int i = 0; i < _maxConcurrentNotifications; i++)
        {
            tasks.Add(ProcessNotificationsAsync(stoppingToken));
        }
        
        // Start retry processor
        tasks.Add(ProcessRetriesAsync(stoppingToken));
        
        // Start rate limit cleanup
        tasks.Add(CleanupRateLimitsAsync(stoppingToken));
        
        await Task.WhenAll(tasks);
    }

    private async Task ProcessNotificationsAsync(CancellationToken cancellationToken)
    {
        await foreach (var deliveryTask in _notificationReader.ReadAllAsync(cancellationToken))
        {
            try
            {
                await ProcessNotificationDeliveryAsync(deliveryTask, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing notification delivery: {NotificationId}", deliveryTask.NotificationId);
            }
        }
    }

    private async Task ProcessNotificationDeliveryAsync(NotificationDeliveryTask task, CancellationToken cancellationToken)
    {
        var startTime = DateTime.UtcNow;
        var attempt = new NotificationDeliveryAttempt
        {
            NotificationId = task.NotificationId,
            TeamId = task.TeamId,
            Type = task.Type,
            DeliveryMethod = task.Method,
            Endpoint = task.Endpoint,
            Payload = task.Payload,
            AttemptNumber = task.AttemptCount + 1,
            AttemptedAt = startTime,
            Status = "PROCESSING"
        };

        try
        {
            bool success = false;
            string errorMessage = string.Empty;
            int? responseCode = null;
            string responseBody = string.Empty;

            if (task.Method == "webhook")
            {
                var result = await DeliverWebhookAsync(task, cancellationToken);
                success = result.Success;
                errorMessage = result.ErrorMessage;
                responseCode = result.ResponseCode;
                responseBody = result.ResponseBody;
            }
            else
            {
                var result = await DeliverNotificationAsync(task, cancellationToken);
                success = result.IsSuccess;
                errorMessage = result.Errors.FirstOrDefault() ?? string.Empty;
            }

            attempt.Duration = DateTime.UtcNow - startTime;
            attempt.Status = success ? "SUCCESS" : "FAILED";
            attempt.ResponseCode = responseCode;
            attempt.ResponseBody = responseBody;
            attempt.ErrorMessage = errorMessage;

            if (success)
            {
                NotificationDeliveryOperations.WithLabels(task.TeamId.ToString(), task.Type.ToString(), task.Method, "success").Inc();
                PendingNotifications.WithLabels(task.TeamId.ToString(), task.Type.ToString(), task.Priority.ToString()).Dec();
                
                _logger.LogInformation("Notification delivered successfully: {NotificationId}, Duration: {Duration}ms", 
                    task.NotificationId, attempt.Duration.TotalMilliseconds);
            }
            else
            {
                // Handle retry logic
                await HandleNotificationRetryAsync(task, attempt, cancellationToken);
            }

            // Log attempt (in production, save to database)
            _logger.LogDebug("Notification delivery attempt logged: {NotificationId}, Attempt: {AttemptNumber}, Success: {Success}", 
                task.NotificationId, attempt.AttemptNumber, success);
        }
        catch (Exception ex)
        {
            attempt.Duration = DateTime.UtcNow - startTime;
            attempt.Status = "ERROR";
            attempt.ErrorMessage = ex.Message;
            
            _logger.LogError(ex, "Notification delivery attempt failed: {NotificationId}", task.NotificationId);
            await HandleNotificationRetryAsync(task, attempt, cancellationToken);
        }
    }

    private async Task<WebhookDeliveryResult> DeliverWebhookAsync(NotificationDeliveryTask task, CancellationToken cancellationToken)
    {
        try
        {
            using var httpClient = _httpClientFactory.CreateClient("webhooks");
            httpClient.Timeout = task.Timeout;

            // Create HTTP request
            var request = new HttpRequestMessage(HttpMethod.Post, task.Endpoint)
            {
                Content = new StringContent(task.Payload, Encoding.UTF8, "application/json")
            };

            // Add custom headers
            foreach (var header in task.Headers)
            {
                request.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }

            // Add signature if team has webhook secret
            var team = await _teamRepository.GetByIdAsync(task.TeamId, cancellationToken);
            if (team != null && !string.IsNullOrEmpty(team.WebhookSecret))
            {
                var signature = await GenerateWebhookSignatureAsync(task.Payload, team.WebhookSecret, cancellationToken);
                request.Headers.TryAddWithoutValidation("X-Webhook-Signature", signature);
            }

            // Add standard headers
            request.Headers.TryAddWithoutValidation("X-Notification-Id", task.NotificationId);
            request.Headers.TryAddWithoutValidation("X-Event-Type", task.Type.ToString());
            request.Headers.TryAddWithoutValidation("X-Team-Id", task.TeamId.ToString());
            request.Headers.TryAddWithoutValidation("User-Agent", "PaymentGateway-Webhook/1.0");

            // Send request
            var response = await httpClient.SendAsync(request, cancellationToken);
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

            return new WebhookDeliveryResult
            {
                Success = response.IsSuccessStatusCode,
                ResponseCode = (int)response.StatusCode,
                ResponseBody = responseBody,
                ErrorMessage = response.IsSuccessStatusCode ? string.Empty : $"HTTP {response.StatusCode}: {response.ReasonPhrase}"
            };
        }
        catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException)
        {
            return new WebhookDeliveryResult
            {
                Success = false,
                ErrorMessage = "Webhook delivery timeout"
            };
        }
        catch (HttpRequestException ex)
        {
            return new WebhookDeliveryResult
            {
                Success = false,
                ErrorMessage = $"HTTP error: {ex.Message}"
            };
        }
        catch (Exception ex)
        {
            return new WebhookDeliveryResult
            {
                Success = false,
                ErrorMessage = $"Delivery error: {ex.Message}"
            };
        }
    }

    private async Task<NotificationDeliveryResult> DeliverNotificationAsync(NotificationDeliveryTask task, CancellationToken cancellationToken)
    {
        // Simulated notification delivery (email, SMS, etc.)
        await Task.Delay(Random.Shared.Next(100, 500), cancellationToken);
        
        // Simulate delivery success/failure
        var success = Random.Shared.NextDouble() > 0.1; // 90% success rate
        
        return new NotificationDeliveryResult
        {
            IsSuccess = success,
            Status = success ? "DELIVERED" : "FAILED",
            Errors = success ? new List<string>() : new List<string> { "Simulated delivery failure" }
        };
    }

    private async Task HandleNotificationRetryAsync(NotificationDeliveryTask task, NotificationDeliveryAttempt attempt, CancellationToken cancellationToken)
    {
        var retryPolicy = RetryPolicies.GetValueOrDefault(task.Type, new RetryPolicy { MaxAttempts = _maxRetryAttempts, BaseDelay = _baseRetryDelay });
        
        if (task.AttemptCount < retryPolicy.MaxAttempts)
        {
            // Calculate next retry time with exponential backoff
            var delay = TimeSpan.FromTicks(retryPolicy.BaseDelay.Ticks * (long)Math.Pow(2, task.AttemptCount));
            var jitter = TimeSpan.FromMilliseconds(Random.Shared.Next(0, 1000));
            var nextRetryAt = DateTime.UtcNow.Add(delay).Add(jitter);

            // Update retry state
            _retryStates.AddOrUpdate(task.NotificationId, 
                new NotificationRetryState { NextRetryAt = nextRetryAt, AttemptCount = task.AttemptCount + 1 },
                (key, existing) => new NotificationRetryState { NextRetryAt = nextRetryAt, AttemptCount = task.AttemptCount + 1 });

            NotificationRetryOperations.WithLabels(task.TeamId.ToString(), task.Type.ToString(), (task.AttemptCount + 1).ToString()).Inc();
            
            _logger.LogWarning("Notification delivery failed, scheduled for retry: {NotificationId}, Attempt: {AttemptCount}, NextRetry: {NextRetryAt}", 
                task.NotificationId, task.AttemptCount + 1, nextRetryAt);
        }
        else
        {
            // Max retries exceeded
            NotificationDeliveryOperations.WithLabels(task.TeamId.ToString(), task.Type.ToString(), task.Method, "max_retries_exceeded").Inc();
            PendingNotifications.WithLabels(task.TeamId.ToString(), task.Type.ToString(), task.Priority.ToString()).Dec();
            
            _logger.LogError("Notification delivery failed permanently: {NotificationId}, MaxAttempts: {MaxAttempts}", 
                task.NotificationId, retryPolicy.MaxAttempts);
        }
    }

    private async Task ProcessRetriesAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var now = DateTime.UtcNow;
                var retryTasks = new List<Task>();

                foreach (var kvp in _retryStates.ToList())
                {
                    if (kvp.Value.NextRetryAt <= now)
                    {
                        if (_retryStates.TryRemove(kvp.Key, out var retryState))
                        {
                            // Create retry task (would need to reconstruct original task)
                            _logger.LogDebug("Processing retry for notification: {NotificationId}", kvp.Key);
                        }
                    }
                }

                await Task.Delay(TimeSpan.FromSeconds(10), cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in retry processor");
                await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);
            }
        }
    }

    private async Task CleanupRateLimitsAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var now = DateTime.UtcNow;
                var keysToRemove = new List<string>();

                foreach (var kvp in _rateLimits.ToList())
                {
                    if (kvp.Value.WindowStart.Add(kvp.Value.WindowDuration) < now)
                    {
                        keysToRemove.Add(kvp.Key);
                    }
                }

                foreach (var key in keysToRemove)
                {
                    _rateLimits.TryRemove(key, out _);
                }

                await Task.Delay(TimeSpan.FromMinutes(1), cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in rate limit cleanup");
                await Task.Delay(TimeSpan.FromMinutes(5), cancellationToken);
            }
        }
    }

    public async Task<string> GenerateWebhookSignatureAsync(string payload, string secret, CancellationToken cancellationToken = default)
    {
        try
        {
            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
            var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
            var signature = "sha256=" + Convert.ToHexString(hash).ToLower();
            
            NotificationSignatureOperations.WithLabels("generate", "success").Inc();
            return signature;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate webhook signature");
            NotificationSignatureOperations.WithLabels("generate", "error").Inc();
            throw;
        }
    }

    public async Task<bool> ValidateWebhookSignatureAsync(string payload, string signature, string secret, CancellationToken cancellationToken = default)
    {
        try
        {
            var expectedSignature = await GenerateWebhookSignatureAsync(payload, secret, cancellationToken);
            var isValid = string.Equals(signature, expectedSignature, StringComparison.OrdinalIgnoreCase);
            
            NotificationSignatureOperations.WithLabels("validate", isValid ? "success" : "failed").Inc();
            return isValid;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to validate webhook signature");
            NotificationSignatureOperations.WithLabels("validate", "error").Inc();
            return false;
        }
    }

    public async Task<NotificationRateLimitStatus> CheckRateLimitAsync(Guid teamId, NotificationType type, CancellationToken cancellationToken = default)
    {
        var policy = RateLimitPolicies.GetValueOrDefault(type, new RateLimitPolicy { MaxPerMinute = 100, MaxPerHour = 1000 });
        var key = $"{teamId}:{type}:minute";
        var now = DateTime.UtcNow;
        var windowStart = new DateTime(now.Year, now.Month, now.Day, now.Hour, now.Minute, 0);

        var rateLimit = _rateLimits.AddOrUpdate(key, 
            new NotificationRateLimit { WindowStart = windowStart, Count = 1, WindowDuration = TimeSpan.FromMinutes(1) },
            (k, existing) => 
            {
                if (existing.WindowStart == windowStart)
                {
                    existing.Count++;
                    return existing;
                }
                else
                {
                    return new NotificationRateLimit { WindowStart = windowStart, Count = 1, WindowDuration = TimeSpan.FromMinutes(1) };
                }
            });

        var isAllowed = rateLimit.Count <= policy.MaxPerMinute;
        var resetTime = windowStart.Add(TimeSpan.FromMinutes(1)) - now;
        var remaining = Math.Max(0, policy.MaxPerMinute - rateLimit.Count);

        return new NotificationRateLimitStatus
        {
            IsAllowed = isAllowed,
            RemainingCount = remaining,
            ResetTime = resetTime,
            LimitPerWindow = policy.MaxPerMinute,
            WindowDuration = TimeSpan.FromMinutes(1),
            RateLimitType = "per_minute"
        };
    }

    #region Template Management

    public async Task<NotificationTemplate> CreateNotificationTemplateAsync(NotificationTemplate template, CancellationToken cancellationToken = default)
    {
        template.Id = Guid.NewGuid();
        template.CreatedAt = DateTime.UtcNow;
        template.UpdatedAt = DateTime.UtcNow;
        
        var key = $"{template.TeamId}:{template.Type}:{template.Id}";
        _templates.TryAdd(key, template);
        
        _logger.LogInformation("Notification template created: {TemplateId}, Team: {TeamId}, Type: {Type}", 
            template.Id, template.TeamId, template.Type);
        
        return template;
    }

    public async Task<NotificationTemplate> UpdateNotificationTemplateAsync(NotificationTemplate template, CancellationToken cancellationToken = default)
    {
        template.UpdatedAt = DateTime.UtcNow;
        
        var key = $"{template.TeamId}:{template.Type}:{template.Id}";
        _templates.AddOrUpdate(key, template, (k, existing) => template);
        
        _logger.LogInformation("Notification template updated: {TemplateId}, Team: {TeamId}, Type: {Type}", 
            template.Id, template.TeamId, template.Type);
        
        return template;
    }

    public async Task<IEnumerable<NotificationTemplate>> GetNotificationTemplatesAsync(Guid teamId, NotificationType type, CancellationToken cancellationToken = default)
    {
        return _templates.Values
            .Where(t => t.TeamId == teamId && t.Type == type && t.IsActive)
            .OrderBy(t => t.Name)
            .ToList();
    }

    private async Task<NotificationTemplate?> GetNotificationTemplateAsync(Guid teamId, NotificationType type, string templateId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(templateId))
        {
            // Return default template for type
            return _templates.Values.FirstOrDefault(t => t.TeamId == teamId && t.Type == type && t.IsActive);
        }
        
        var key = $"{teamId}:{type}:{templateId}";
        return _templates.GetValueOrDefault(key);
    }

    private async Task<string> RenderTemplateAsync(NotificationTemplate template, Dictionary<string, object> data, CancellationToken cancellationToken)
    {
        // Simple template rendering (in production, use proper templating engine)
        var rendered = template.BodyTemplate;
        
        // Merge default data with provided data
        var allData = new Dictionary<string, object>(template.DefaultData);
        foreach (var kvp in data)
        {
            allData[kvp.Key] = kvp.Value;
        }
        
        // Replace placeholders
        foreach (var kvp in allData)
        {
            rendered = rendered.Replace($"{{{kvp.Key}}}", kvp.Value?.ToString() ?? "");
        }
        
        return rendered;
    }

    #endregion

    #region Helper Methods

    private async Task<NotificationRateLimitStatus> CheckWebhookRateLimitAsync(string key, string eventType, CancellationToken cancellationToken)
    {
        // Simplified rate limiting for webhooks
        return new NotificationRateLimitStatus
        {
            IsAllowed = true,
            RemainingCount = 1000,
            ResetTime = TimeSpan.FromMinutes(1),
            LimitPerWindow = 1000,
            WindowDuration = TimeSpan.FromMinutes(1),
            RateLimitType = "webhook"
        };
    }

    private void InitializeDefaultTemplates()
    {
        // Initialize default templates for common notification types
        var defaultTemplates = new[]
        {
            new NotificationTemplate
            {
                Id = Guid.Parse("11111111-1111-1111-1111-111111111111"), // Fixed Guid for default template
                TeamId = Guid.Empty, // Global template
                Type = NotificationType.PAYMENT_SUCCESS,
                Name = "Payment Success Default",
                Subject = "Payment Successful",
                BodyTemplate = "Payment {payment_id} for amount {amount} {currency} has been processed successfully.",
                Format = "JSON",
                RequiredFields = new List<string> { "payment_id", "amount", "currency" },
                Language = "en"
            },
            new NotificationTemplate
            {
                Id = Guid.Parse("22222222-2222-2222-2222-222222222222"), // Fixed Guid for default template
                TeamId = Guid.Empty,
                Type = NotificationType.PAYMENT_FAILURE,
                Name = "Payment Failure Default",
                Subject = "Payment Failed",
                BodyTemplate = "Payment {payment_id} for amount {amount} {currency} has failed. Reason: {error_message}",
                Format = "JSON",
                RequiredFields = new List<string> { "payment_id", "amount", "currency", "error_message" },
                Language = "en"
            }
        };

        foreach (var template in defaultTemplates)
        {
            var key = $"{template.TeamId}:{template.Type}:{template.Id}";
            _templates.TryAdd(key, template);
        }
    }

    #endregion

    public async Task<IEnumerable<NotificationDeliveryAttempt>> GetDeliveryAttemptsAsync(string notificationId, CancellationToken cancellationToken = default)
    {
        // In production, this would query the database
        return new List<NotificationDeliveryAttempt>();
    }

    public async Task<NotificationDeliveryStatistics> GetDeliveryStatisticsAsync(Guid? teamId = null, TimeSpan? period = null, CancellationToken cancellationToken = default)
    {
        period ??= TimeSpan.FromDays(7);
        
        // Simulated statistics (in production, query from database)
        return new NotificationDeliveryStatistics
        {
            Period = period.Value,
            TotalNotifications = 15000,
            SuccessfulDeliveries = 14250,
            FailedDeliveries = 600,
            PendingDeliveries = 150,
            DeliverySuccessRate = 0.95,
            AverageDeliveryTime = TimeSpan.FromSeconds(1.2),
            NotificationsByType = new Dictionary<NotificationType, int>
            {
                [NotificationType.PAYMENT_STATUS_CHANGE] = 8000,
                [NotificationType.PAYMENT_SUCCESS] = 4000,
                [NotificationType.PAYMENT_FAILURE] = 2000,
                [NotificationType.FRAUD_ALERT] = 500,
                [NotificationType.SYSTEM_ALERT] = 500
            },
            NotificationsByTeam = new Dictionary<string, int>
            {
                ["1"] = 4000,
                ["2"] = 3500,
                ["3"] = 3000,
                ["4"] = 2500,
                ["5"] = 2000
            },
            DeliveryMethodStats = new Dictionary<string, int>
            {
                ["webhook"] = 12000,
                ["email"] = 2000,
                ["sms"] = 1000
            },
            ErrorsByType = new Dictionary<string, int>
            {
                ["timeout"] = 300,
                ["http_error"] = 200,
                ["rate_limited"] = 100
            },
            ResponseCodeStats = new Dictionary<int, int>
            {
                [200] = 14250,
                [404] = 200,
                [500] = 300,
                [429] = 100,
                [0] = 150 // Timeouts
            }
        };
    }


    #region Helper Classes

    private class NotificationDeliveryTask
    {
        public string NotificationId { get; set; } = string.Empty;
        public Guid TeamId { get; set; }
        public NotificationType Type { get; set; }
        public string Method { get; set; } = string.Empty;
        public string Endpoint { get; set; } = string.Empty;
        public string Payload { get; set; } = string.Empty;
        public int Priority { get; set; }
        public int AttemptCount { get; set; }
        public Dictionary<string, string> Headers { get; set; } = new();
        public TimeSpan Timeout { get; set; }
        public Dictionary<string, object> Metadata { get; set; } = new();
        public DateTime CreatedAt { get; set; }
    }

    private class WebhookDeliveryResult
    {
        public bool Success { get; set; }
        public int? ResponseCode { get; set; }
        public string ResponseBody { get; set; } = string.Empty;
        public string ErrorMessage { get; set; } = string.Empty;
    }

    private class NotificationRetryState
    {
        public DateTime NextRetryAt { get; set; }
        public int AttemptCount { get; set; }
    }
    
    private class NotificationRateLimit
    {
        public DateTime WindowStart { get; set; }
        public int Count { get; set; }
        public TimeSpan WindowDuration { get; set; }
    }

    private class RetryPolicy
    {
        public int MaxAttempts { get; set; }
        public TimeSpan BaseDelay { get; set; }
    }

    private class RateLimitPolicy
    {
        public int MaxPerMinute { get; set; }
        public int MaxPerHour { get; set; }
    }

    #endregion
}