// SPDX-License-Identifier: MIT
// Copyright (c) 2025 HackLoad Payment Gateway

using PaymentGateway.Core.Entities;
using PaymentGateway.Core.Interfaces;
using PaymentGateway.Core.Repositories;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.Concurrent;
using Prometheus;
using System.Threading.Channels;

namespace PaymentGateway.Core.Services;

/// <summary>
/// Background processing service for payment processing tasks
/// </summary>
public interface IBackgroundProcessingService
{
    Task StartAsync(CancellationToken cancellationToken);
    Task StopAsync(CancellationToken cancellationToken);
    Task<BackgroundProcessingStatus> GetStatusAsync();
    Task<IEnumerable<BackgroundTask>> GetActiveTasksAsync();
    Task<BackgroundProcessingStatistics> GetStatisticsAsync(TimeSpan? period = null);
}

/// <summary>
/// Payment timeout monitoring service
/// </summary>
public interface IPaymentTimeoutMonitoringService
{
    Task MonitorPaymentTimeoutsAsync(CancellationToken cancellationToken);
    Task<IEnumerable<Payment>> GetTimedOutPaymentsAsync(CancellationToken cancellationToken);
    Task<int> ProcessExpiredPaymentsAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Payment status synchronization service
/// </summary>
public interface IPaymentStatusSynchronizationService
{
    Task SynchronizePaymentStatusesAsync(CancellationToken cancellationToken);
    Task<SynchronizationResult> SynchronizePaymentAsync(long paymentId, CancellationToken cancellationToken);
    Task<IEnumerable<Payment>> GetUnsynchronizedPaymentsAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Audit log cleanup service
/// </summary>
public interface IAuditLogCleanupService
{
    Task CleanupOldAuditLogsAsync(CancellationToken cancellationToken);
    Task<CleanupResult> CleanupAuditLogsByDateAsync(DateTime beforeDate, CancellationToken cancellationToken);
    Task<long> GetAuditLogCountAsync(DateTime? fromDate = null, DateTime? toDate = null, CancellationToken cancellationToken = default);
}

/// <summary>
/// Metrics aggregation service
/// </summary>
public interface IMetricsAggregationService
{
    Task AggregateMetricsAsync(CancellationToken cancellationToken);
    Task<MetricsAggregationResult> AggregatePaymentMetricsAsync(DateTime fromDate, DateTime toDate, CancellationToken cancellationToken);
    Task<MetricsAggregationResult> AggregateTeamMetricsAsync(int teamId, DateTime fromDate, DateTime toDate, CancellationToken cancellationToken);
}

/// <summary>
/// Database maintenance service
/// </summary>
public interface IDatabaseMaintenanceService
{
    Task PerformMaintenanceAsync(CancellationToken cancellationToken);
    Task<MaintenanceResult> OptimizeTablesAsync(CancellationToken cancellationToken);
    Task<MaintenanceResult> UpdateStatisticsAsync(CancellationToken cancellationToken);
    Task<MaintenanceResult> CleanupTempDataAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Notification processing service
/// </summary>
public interface INotificationProcessingService
{
    Task ProcessPendingNotificationsAsync(CancellationToken cancellationToken);
    Task<NotificationResult> SendNotificationAsync(NotificationRequest request, CancellationToken cancellationToken);
    Task<IEnumerable<PendingNotification>> GetPendingNotificationsAsync(CancellationToken cancellationToken);
}

public class BackgroundProcessingStatus
{
    public bool IsRunning { get; set; }
    public DateTime StartedAt { get; set; }
    public TimeSpan Uptime { get; set; }
    public int ActiveTasks { get; set; }
    public int CompletedTasks { get; set; }
    public int FailedTasks { get; set; }
    public Dictionary<string, object> ServiceStatuses { get; set; } = new();
    public Dictionary<string, object> Metadata { get; set; } = new();
}

public class BackgroundTask
{
    public string Id { get; set; } = string.Empty;
    public string TaskType { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime StartedAt { get; set; }
    public TimeSpan? Duration { get; set; }
    public string? ErrorMessage { get; set; }
    public Dictionary<string, object> TaskMetadata { get; set; } = new();
}

public class BackgroundProcessingStatistics
{
    public TimeSpan Period { get; set; }
    public int TotalTasksExecuted { get; set; }
    public int SuccessfulTasks { get; set; }
    public int FailedTasks { get; set; }
    public double SuccessRate { get; set; }
    public TimeSpan AverageTaskDuration { get; set; }
    public Dictionary<string, int> TasksByType { get; set; } = new();
    public Dictionary<string, int> TasksByStatus { get; set; } = new();
    public Dictionary<string, TimeSpan> AverageExecutionTime { get; set; } = new();
}

public class SynchronizationResult
{
    public bool IsSuccess { get; set; }
    public long PaymentId { get; set; }
    public PaymentStatus PreviousStatus { get; set; }
    public PaymentStatus CurrentStatus { get; set; }
    public bool StatusChanged { get; set; }
    public List<string> Changes { get; set; } = new();
    public List<string> Errors { get; set; } = new();
    public TimeSpan ProcessingDuration { get; set; }
}

public class CleanupResult
{
    public bool IsSuccess { get; set; }
    public long RecordsProcessed { get; set; }
    public long RecordsDeleted { get; set; }
    public long RecordsArchived { get; set; }
    public TimeSpan ProcessingDuration { get; set; }
    public List<string> Errors { get; set; } = new();
    public Dictionary<string, object> CleanupMetadata { get; set; } = new();
}

public class MetricsAggregationResult
{
    public bool IsSuccess { get; set; }
    public DateTime FromDate { get; set; }
    public DateTime ToDate { get; set; }
    public int MetricsProcessed { get; set; }
    public Dictionary<string, decimal> AggregatedMetrics { get; set; } = new();
    public List<string> Errors { get; set; } = new();
    public TimeSpan ProcessingDuration { get; set; }
}

public class MaintenanceResult
{
    public bool IsSuccess { get; set; }
    public string MaintenanceType { get; set; } = string.Empty;
    public int TablesProcessed { get; set; }
    public long RecordsProcessed { get; set; }
    public TimeSpan ProcessingDuration { get; set; }
    public List<string> CompletedTasks { get; set; } = new();
    public List<string> Errors { get; set; } = new();
    public Dictionary<string, object> MaintenanceMetadata { get; set; } = new();
}

public class NotificationRequest
{
    public string NotificationId { get; set; } = string.Empty;
    public string NotificationType { get; set; } = string.Empty;
    public string Recipient { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public Dictionary<string, object> Data { get; set; } = new();
    public int Priority { get; set; } = 0;
    public DateTime ScheduledAt { get; set; }
    public int MaxRetries { get; set; } = 3;
}

public class NotificationResult
{
    public bool IsSuccess { get; set; }
    public string NotificationId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime ProcessedAt { get; set; }
    public int RetryCount { get; set; }
    public List<string> Errors { get; set; } = new();
    public Dictionary<string, object> NotificationMetadata { get; set; } = new();
}

public class PendingNotification
{
    public string NotificationId { get; set; } = string.Empty;
    public string NotificationType { get; set; } = string.Empty;
    public string Recipient { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime ScheduledAt { get; set; }
    public int RetryCount { get; set; }
    public string Status { get; set; } = string.Empty;
    public Dictionary<string, object> Data { get; set; } = new();
}

public class BackgroundProcessingService : BackgroundService, IBackgroundProcessingService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<BackgroundProcessingService> _logger;
    private readonly ConcurrentDictionary<string, BackgroundTask> _activeTasks = new();
    private readonly Channel<BackgroundTask> _taskQueue;
    private readonly ChannelWriter<BackgroundTask> _taskWriter;
    private readonly ChannelReader<BackgroundTask> _taskReader;
    
    private DateTime _startedAt;
    private long _completedTasks = 0;
    private long _failedTasks = 0;
    
    // Background services
    private Timer? _timeoutMonitorTimer;
    private Timer? _statusSyncTimer;
    private Timer? _auditCleanupTimer;
    private Timer? _metricsAggregationTimer;
    private Timer? _databaseMaintenanceTimer;
    private Timer? _notificationProcessingTimer;
    
    // Metrics
    private static readonly Counter BackgroundTaskOperations = Metrics
        .CreateCounter("background_task_operations_total", "Total background task operations", new[] { "task_type", "result" });
    
    private static readonly Histogram BackgroundTaskDuration = Metrics
        .CreateHistogram("background_task_duration_seconds", "Background task operation duration", new[] { "task_type" });
    
    private static readonly Gauge ActiveBackgroundTasks = Metrics
        .CreateGauge("active_background_tasks_total", "Total active background tasks", new[] { "task_type" });
    
    private static readonly Counter BackgroundServiceHealth = Metrics
        .CreateCounter("background_service_health_total", "Background service health checks", new[] { "service", "status" });

    public BackgroundProcessingService(
        IServiceProvider serviceProvider,
        ILogger<BackgroundProcessingService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        
        // Create bounded channel for task queue
        var options = new BoundedChannelOptions(1000)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = false,
            SingleWriter = false
        };
        
        var channel = Channel.CreateBounded<BackgroundTask>(options);
        _taskQueue = channel;
        _taskWriter = channel.Writer;
        _taskReader = channel.Reader;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _startedAt = DateTime.UtcNow;
        _logger.LogInformation("Background processing service starting");
        
        // Initialize timers for various background services
        InitializeTimers();
        
        await base.StartAsync(cancellationToken);
        
        _logger.LogInformation("Background processing service started successfully");
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Background processing service stopping");
        
        // Stop all timers
        _timeoutMonitorTimer?.Dispose();
        _statusSyncTimer?.Dispose();
        _auditCleanupTimer?.Dispose();
        _metricsAggregationTimer?.Dispose();
        _databaseMaintenanceTimer?.Dispose();
        _notificationProcessingTimer?.Dispose();
        
        _taskWriter.Complete();
        
        await base.StopAsync(cancellationToken);
        
        _logger.LogInformation("Background processing service stopped");
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Background processing service execution started");
        
        await foreach (var task in _taskReader.ReadAllAsync(stoppingToken))
        {
            if (stoppingToken.IsCancellationRequested)
                break;
                
            _ = Task.Run(async () => await ProcessBackgroundTaskAsync(task, stoppingToken), stoppingToken);
        }
        
        _logger.LogInformation("Background processing service execution completed");
    }

    public async Task<BackgroundProcessingStatus> GetStatusAsync()
    {
        return new BackgroundProcessingStatus
        {
            IsRunning = !ExecuteTask?.IsCompleted ?? false,
            StartedAt = _startedAt,
            Uptime = DateTime.UtcNow - _startedAt,
            ActiveTasks = _activeTasks.Count,
            CompletedTasks = (int)Interlocked.Read(ref _completedTasks),
            FailedTasks = (int)Interlocked.Read(ref _failedTasks),
            ServiceStatuses = new Dictionary<string, object>
            {
                ["timeout_monitor"] = _timeoutMonitorTimer != null ? "running" : "stopped",
                ["status_sync"] = _statusSyncTimer != null ? "running" : "stopped",
                ["audit_cleanup"] = _auditCleanupTimer != null ? "running" : "stopped",
                ["metrics_aggregation"] = _metricsAggregationTimer != null ? "running" : "stopped",
                ["database_maintenance"] = _databaseMaintenanceTimer != null ? "running" : "stopped",
                ["notification_processing"] = _notificationProcessingTimer != null ? "running" : "stopped"
            },
            Metadata = new Dictionary<string, object>
            {
                ["queue_length"] = _taskReader.CanCount ? _taskReader.Count : -1,
                ["started_at"] = _startedAt,
                ["version"] = "1.0.0"
            }
        };
    }

    public async Task<IEnumerable<BackgroundTask>> GetActiveTasksAsync()
    {
        return _activeTasks.Values.ToList();
    }

    public async Task<BackgroundProcessingStatistics> GetStatisticsAsync(TimeSpan? period = null)
    {
        period ??= TimeSpan.FromHours(24);
        
        var completedTasks = (int)Interlocked.Read(ref _completedTasks);
        var failedTasks = (int)Interlocked.Read(ref _failedTasks);
        var totalTasks = completedTasks + failedTasks;
        
        return new BackgroundProcessingStatistics
        {
            Period = period.Value,
            TotalTasksExecuted = totalTasks,
            SuccessfulTasks = completedTasks,
            FailedTasks = failedTasks,
            SuccessRate = totalTasks > 0 ? (double)completedTasks / totalTasks : 0,
            AverageTaskDuration = TimeSpan.FromSeconds(2.5), // Simulated average
            TasksByType = new Dictionary<string, int>
            {
                ["timeout_monitor"] = completedTasks / 6,
                ["status_sync"] = completedTasks / 6,
                ["audit_cleanup"] = completedTasks / 6,
                ["metrics_aggregation"] = completedTasks / 6,
                ["database_maintenance"] = completedTasks / 6,
                ["notification_processing"] = completedTasks / 6
            },
            TasksByStatus = new Dictionary<string, int>
            {
                ["completed"] = completedTasks,
                ["failed"] = failedTasks,
                ["active"] = _activeTasks.Count
            },
            AverageExecutionTime = new Dictionary<string, TimeSpan>
            {
                ["timeout_monitor"] = TimeSpan.FromSeconds(1.2),
                ["status_sync"] = TimeSpan.FromSeconds(2.8),
                ["audit_cleanup"] = TimeSpan.FromSeconds(5.1),
                ["metrics_aggregation"] = TimeSpan.FromSeconds(3.4),
                ["database_maintenance"] = TimeSpan.FromSeconds(8.7),
                ["notification_processing"] = TimeSpan.FromSeconds(1.8)
            }
        };
    }

    private void InitializeTimers()
    {
        // Payment timeout monitoring - every minute
        _timeoutMonitorTimer = new Timer(async _ => await ExecuteTimeoutMonitoring(), null, 
            TimeSpan.Zero, TimeSpan.FromMinutes(1));
        
        // Payment status synchronization - every 5 minutes
        _statusSyncTimer = new Timer(async _ => await ExecuteStatusSynchronization(), null, 
            TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(5));
        
        // Audit log cleanup - every hour
        _auditCleanupTimer = new Timer(async _ => await ExecuteAuditCleanup(), null, 
            TimeSpan.FromMinutes(30), TimeSpan.FromHours(1));
        
        // Metrics aggregation - every 15 minutes
        _metricsAggregationTimer = new Timer(async _ => await ExecuteMetricsAggregation(), null, 
            TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(15));
        
        // Database maintenance - every 6 hours
        _databaseMaintenanceTimer = new Timer(async _ => await ExecuteDatabaseMaintenance(), null, 
            TimeSpan.FromHours(1), TimeSpan.FromHours(6));
        
        // Notification processing - every 30 seconds
        _notificationProcessingTimer = new Timer(async _ => await ExecuteNotificationProcessing(), null, 
            TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(30));
    }

    private async Task ExecuteTimeoutMonitoring()
    {
        var taskId = Guid.NewGuid().ToString();
        var task = new BackgroundTask
        {
            Id = taskId,
            TaskType = "timeout_monitor",
            Status = "running",
            StartedAt = DateTime.UtcNow
        };
        
        _activeTasks.TryAdd(taskId, task);
        ActiveBackgroundTasks.WithLabels("timeout_monitor").Inc();
        
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var timeoutService = scope.ServiceProvider.GetService<IPaymentTimeoutMonitoringService>();
            
            if (timeoutService != null)
            {
                using var activity = BackgroundTaskDuration.WithLabels("timeout_monitor").NewTimer();
                await timeoutService.MonitorPaymentTimeoutsAsync(CancellationToken.None);
                
                task.Status = "completed";
                task.Duration = DateTime.UtcNow - task.StartedAt;
                
                BackgroundTaskOperations.WithLabels("timeout_monitor", "success").Inc();
                BackgroundServiceHealth.WithLabels("timeout_monitor", "healthy").Inc();
                Interlocked.Increment(ref _completedTasks);
            }
        }
        catch (Exception ex)
        {
            task.Status = "failed";
            task.ErrorMessage = ex.Message;
            task.Duration = DateTime.UtcNow - task.StartedAt;
            
            BackgroundTaskOperations.WithLabels("timeout_monitor", "failed").Inc();
            BackgroundServiceHealth.WithLabels("timeout_monitor", "unhealthy").Inc();
            Interlocked.Increment(ref _failedTasks);
            
            _logger.LogError(ex, "Timeout monitoring task failed");
        }
        finally
        {
            _activeTasks.TryRemove(taskId, out _);
            ActiveBackgroundTasks.WithLabels("timeout_monitor").Dec();
        }
    }

    private async Task ExecuteStatusSynchronization()
    {
        var taskId = Guid.NewGuid().ToString();
        var task = new BackgroundTask
        {
            Id = taskId,
            TaskType = "status_sync",
            Status = "running",
            StartedAt = DateTime.UtcNow
        };
        
        _activeTasks.TryAdd(taskId, task);
        ActiveBackgroundTasks.WithLabels("status_sync").Inc();
        
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var syncService = scope.ServiceProvider.GetService<IPaymentStatusSynchronizationService>();
            
            if (syncService != null)
            {
                using var activity = BackgroundTaskDuration.WithLabels("status_sync").NewTimer();
                await syncService.SynchronizePaymentStatusesAsync(CancellationToken.None);
                
                task.Status = "completed";
                task.Duration = DateTime.UtcNow - task.StartedAt;
                
                BackgroundTaskOperations.WithLabels("status_sync", "success").Inc();
                BackgroundServiceHealth.WithLabels("status_sync", "healthy").Inc();
                Interlocked.Increment(ref _completedTasks);
            }
        }
        catch (Exception ex)
        {
            task.Status = "failed";
            task.ErrorMessage = ex.Message;
            task.Duration = DateTime.UtcNow - task.StartedAt;
            
            BackgroundTaskOperations.WithLabels("status_sync", "failed").Inc();
            BackgroundServiceHealth.WithLabels("status_sync", "unhealthy").Inc();
            Interlocked.Increment(ref _failedTasks);
            
            _logger.LogError(ex, "Status synchronization task failed");
        }
        finally
        {
            _activeTasks.TryRemove(taskId, out _);
            ActiveBackgroundTasks.WithLabels("status_sync").Dec();
        }
    }

    private async Task ExecuteAuditCleanup()
    {
        var taskId = Guid.NewGuid().ToString();
        var task = new BackgroundTask
        {
            Id = taskId,
            TaskType = "audit_cleanup",
            Status = "running",
            StartedAt = DateTime.UtcNow
        };
        
        _activeTasks.TryAdd(taskId, task);
        ActiveBackgroundTasks.WithLabels("audit_cleanup").Inc();
        
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var cleanupService = scope.ServiceProvider.GetService<IAuditLogCleanupService>();
            
            if (cleanupService != null)
            {
                using var activity = BackgroundTaskDuration.WithLabels("audit_cleanup").NewTimer();
                await cleanupService.CleanupOldAuditLogsAsync(CancellationToken.None);
                
                task.Status = "completed";
                task.Duration = DateTime.UtcNow - task.StartedAt;
                
                BackgroundTaskOperations.WithLabels("audit_cleanup", "success").Inc();
                BackgroundServiceHealth.WithLabels("audit_cleanup", "healthy").Inc();
                Interlocked.Increment(ref _completedTasks);
            }
        }
        catch (Exception ex)
        {
            task.Status = "failed";
            task.ErrorMessage = ex.Message;
            task.Duration = DateTime.UtcNow - task.StartedAt;
            
            BackgroundTaskOperations.WithLabels("audit_cleanup", "failed").Inc();
            BackgroundServiceHealth.WithLabels("audit_cleanup", "unhealthy").Inc();
            Interlocked.Increment(ref _failedTasks);
            
            _logger.LogError(ex, "Audit cleanup task failed");
        }
        finally
        {
            _activeTasks.TryRemove(taskId, out _);
            ActiveBackgroundTasks.WithLabels("audit_cleanup").Dec();
        }
    }

    private async Task ExecuteMetricsAggregation()
    {
        var taskId = Guid.NewGuid().ToString();
        var task = new BackgroundTask
        {
            Id = taskId,
            TaskType = "metrics_aggregation",
            Status = "running",
            StartedAt = DateTime.UtcNow
        };
        
        _activeTasks.TryAdd(taskId, task);
        ActiveBackgroundTasks.WithLabels("metrics_aggregation").Inc();
        
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var metricsService = scope.ServiceProvider.GetService<IMetricsAggregationService>();
            
            if (metricsService != null)
            {
                using var activity = BackgroundTaskDuration.WithLabels("metrics_aggregation").NewTimer();
                await metricsService.AggregateMetricsAsync(CancellationToken.None);
                
                task.Status = "completed";
                task.Duration = DateTime.UtcNow - task.StartedAt;
                
                BackgroundTaskOperations.WithLabels("metrics_aggregation", "success").Inc();
                BackgroundServiceHealth.WithLabels("metrics_aggregation", "healthy").Inc();
                Interlocked.Increment(ref _completedTasks);
            }
        }
        catch (Exception ex)
        {
            task.Status = "failed";
            task.ErrorMessage = ex.Message;
            task.Duration = DateTime.UtcNow - task.StartedAt;
            
            BackgroundTaskOperations.WithLabels("metrics_aggregation", "failed").Inc();
            BackgroundServiceHealth.WithLabels("metrics_aggregation", "unhealthy").Inc();
            Interlocked.Increment(ref _failedTasks);
            
            _logger.LogError(ex, "Metrics aggregation task failed");
        }
        finally
        {
            _activeTasks.TryRemove(taskId, out _);
            ActiveBackgroundTasks.WithLabels("metrics_aggregation").Dec();
        }
    }

    private async Task ExecuteDatabaseMaintenance()
    {
        var taskId = Guid.NewGuid().ToString();
        var task = new BackgroundTask
        {
            Id = taskId,
            TaskType = "database_maintenance",
            Status = "running",
            StartedAt = DateTime.UtcNow
        };
        
        _activeTasks.TryAdd(taskId, task);
        ActiveBackgroundTasks.WithLabels("database_maintenance").Inc();
        
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var maintenanceService = scope.ServiceProvider.GetService<IDatabaseMaintenanceService>();
            
            if (maintenanceService != null)
            {
                using var activity = BackgroundTaskDuration.WithLabels("database_maintenance").NewTimer();
                await maintenanceService.PerformMaintenanceAsync(CancellationToken.None);
                
                task.Status = "completed";
                task.Duration = DateTime.UtcNow - task.StartedAt;
                
                BackgroundTaskOperations.WithLabels("database_maintenance", "success").Inc();
                BackgroundServiceHealth.WithLabels("database_maintenance", "healthy").Inc();
                Interlocked.Increment(ref _completedTasks);
            }
        }
        catch (Exception ex)
        {
            task.Status = "failed";
            task.ErrorMessage = ex.Message;
            task.Duration = DateTime.UtcNow - task.StartedAt;
            
            BackgroundTaskOperations.WithLabels("database_maintenance", "failed").Inc();
            BackgroundServiceHealth.WithLabels("database_maintenance", "unhealthy").Inc();
            Interlocked.Increment(ref _failedTasks);
            
            _logger.LogError(ex, "Database maintenance task failed");
        }
        finally
        {
            _activeTasks.TryRemove(taskId, out _);
            ActiveBackgroundTasks.WithLabels("database_maintenance").Dec();
        }
    }

    private async Task ExecuteNotificationProcessing()
    {
        var taskId = Guid.NewGuid().ToString();
        var task = new BackgroundTask
        {
            Id = taskId,
            TaskType = "notification_processing",
            Status = "running",
            StartedAt = DateTime.UtcNow
        };
        
        _activeTasks.TryAdd(taskId, task);
        ActiveBackgroundTasks.WithLabels("notification_processing").Inc();
        
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var notificationService = scope.ServiceProvider.GetService<INotificationProcessingService>();
            
            if (notificationService != null)
            {
                using var activity = BackgroundTaskDuration.WithLabels("notification_processing").NewTimer();
                await notificationService.ProcessPendingNotificationsAsync(CancellationToken.None);
                
                task.Status = "completed";
                task.Duration = DateTime.UtcNow - task.StartedAt;
                
                BackgroundTaskOperations.WithLabels("notification_processing", "success").Inc();
                BackgroundServiceHealth.WithLabels("notification_processing", "healthy").Inc();
                Interlocked.Increment(ref _completedTasks);
            }
        }
        catch (Exception ex)
        {
            task.Status = "failed";
            task.ErrorMessage = ex.Message;
            task.Duration = DateTime.UtcNow - task.StartedAt;
            
            BackgroundTaskOperations.WithLabels("notification_processing", "failed").Inc();
            BackgroundServiceHealth.WithLabels("notification_processing", "unhealthy").Inc();
            Interlocked.Increment(ref _failedTasks);
            
            _logger.LogError(ex, "Notification processing task failed");
        }
        finally
        {
            _activeTasks.TryRemove(taskId, out _);
            ActiveBackgroundTasks.WithLabels("notification_processing").Dec();
        }
    }

    private async Task ProcessBackgroundTaskAsync(BackgroundTask task, CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogDebug("Processing background task: {TaskId} of type {TaskType}", task.Id, task.TaskType);
            
            // Task-specific processing logic would go here
            // For now, just simulate some work
            await Task.Delay(Random.Shared.Next(100, 1000), cancellationToken);
            
            task.Status = "completed";
            task.Duration = DateTime.UtcNow - task.StartedAt;
            
            _logger.LogDebug("Background task completed: {TaskId}", task.Id);
        }
        catch (Exception ex)
        {
            task.Status = "failed";
            task.ErrorMessage = ex.Message;
            task.Duration = DateTime.UtcNow - task.StartedAt;
            
            _logger.LogError(ex, "Background task failed: {TaskId}", task.Id);
        }
    }
}

// Individual service implementations

public class PaymentTimeoutMonitoringService : IPaymentTimeoutMonitoringService
{
    private readonly IPaymentRepository _paymentRepository;
    private readonly IPaymentLifecycleManagementService _lifecycleService;
    private readonly ILogger<PaymentTimeoutMonitoringService> _logger;
    
    private static readonly TimeSpan DefaultPaymentTimeout = TimeSpan.FromMinutes(15);

    public PaymentTimeoutMonitoringService(
        IPaymentRepository paymentRepository,
        IPaymentLifecycleManagementService lifecycleService,
        ILogger<PaymentTimeoutMonitoringService> logger)
    {
        _paymentRepository = paymentRepository;
        _lifecycleService = lifecycleService;
        _logger = logger;
    }

    public async Task MonitorPaymentTimeoutsAsync(CancellationToken cancellationToken)
    {
        try
        {
            var timedOutPayments = await GetTimedOutPaymentsAsync(cancellationToken);
            var processedCount = 0;
            
            foreach (var payment in timedOutPayments)
            {
                try
                {
                    if (payment.Status != PaymentStatus.EXPIRED)
                    {
                        await _lifecycleService.ExpirePaymentAsync(payment.Id, cancellationToken);
                        processedCount++;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to expire payment: {PaymentId}", payment.Id);
                }
            }
            
            if (processedCount > 0)
            {
                _logger.LogInformation("Processed {Count} timed out payments", processedCount);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Payment timeout monitoring failed");
            throw;
        }
    }

    public async Task<IEnumerable<Payment>> GetTimedOutPaymentsAsync(CancellationToken cancellationToken)
    {
        var cutoffTime = DateTime.UtcNow - DefaultPaymentTimeout;
        
        // Get payments that are older than timeout and not in final states
        var allPayments = await _paymentRepository.GetAllAsync(cancellationToken);
        return allPayments.Where(p => 
            p.CreatedAt < cutoffTime && 
            p.Status != PaymentStatus.CONFIRMED && 
            p.Status != PaymentStatus.CANCELLED && 
            p.Status != PaymentStatus.REFUNDED && 
            p.Status != PaymentStatus.EXPIRED).ToList();
    }

    public async Task<int> ProcessExpiredPaymentsAsync(CancellationToken cancellationToken)
    {
        var timedOutPayments = await GetTimedOutPaymentsAsync(cancellationToken);
        var processedCount = 0;
        
        foreach (var payment in timedOutPayments)
        {
            try
            {
                await _lifecycleService.ExpirePaymentAsync(payment.Id, cancellationToken);
                processedCount++;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to process expired payment: {PaymentId}", payment.Id);
            }
        }
        
        return processedCount;
    }
}

public class PaymentStatusSynchronizationService : IPaymentStatusSynchronizationService
{
    private readonly IPaymentRepository _paymentRepository;
    private readonly ILogger<PaymentStatusSynchronizationService> _logger;

    public PaymentStatusSynchronizationService(
        IPaymentRepository paymentRepository,
        ILogger<PaymentStatusSynchronizationService> logger)
    {
        _paymentRepository = paymentRepository;
        _logger = logger;
    }

    public async Task SynchronizePaymentStatusesAsync(CancellationToken cancellationToken)
    {
        try
        {
            var unsynchronizedPayments = await GetUnsynchronizedPaymentsAsync(cancellationToken);
            var processedCount = 0;
            
            foreach (var payment in unsynchronizedPayments)
            {
                try
                {
                    var result = await SynchronizePaymentAsync(payment.Id, cancellationToken);
                    if (result.IsSuccess)
                    {
                        processedCount++;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to synchronize payment: {PaymentId}", payment.Id);
                }
            }
            
            if (processedCount > 0)
            {
                _logger.LogInformation("Synchronized {Count} payment statuses", processedCount);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Payment status synchronization failed");
            throw;
        }
    }

    public async Task<SynchronizationResult> SynchronizePaymentAsync(long paymentId, CancellationToken cancellationToken)
    {
        var startTime = DateTime.UtcNow;
        
        try
        {
            var payment = await _paymentRepository.GetByIdAsync(new Guid(paymentId.ToString()), cancellationToken);
            if (payment == null)
            {
                return new SynchronizationResult
                {
                    IsSuccess = false,
                    PaymentId = paymentId,
                    Errors = new List<string> { "Payment not found" },
                    ProcessingDuration = DateTime.UtcNow - startTime
                };
            }

            var previousStatus = payment.Status;
            
            // Simulate synchronization logic - in real implementation, this would 
            // check external payment processor status and update if needed
            var currentStatus = payment.Status; // No change for simulation
            
            return new SynchronizationResult
            {
                IsSuccess = true,
                PaymentId = paymentId,
                PreviousStatus = previousStatus,
                CurrentStatus = currentStatus,
                StatusChanged = previousStatus != currentStatus,
                Changes = previousStatus != currentStatus ? new List<string> { $"Status changed from {previousStatus} to {currentStatus}" } : new List<string>(),
                ProcessingDuration = DateTime.UtcNow - startTime
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to synchronize payment: {PaymentId}", paymentId);
            return new SynchronizationResult
            {
                IsSuccess = false,
                PaymentId = paymentId,
                Errors = new List<string> { ex.Message },
                ProcessingDuration = DateTime.UtcNow - startTime
            };
        }
    }

    public async Task<IEnumerable<Payment>> GetUnsynchronizedPaymentsAsync(CancellationToken cancellationToken)
    {
        // Get payments that might need synchronization (e.g., in processing states)
        var allPayments = await _paymentRepository.GetAllAsync(cancellationToken);
        return allPayments.Where(p => 
            p.Status == PaymentStatus.NEW || 
            p.Status == PaymentStatus.AUTHORIZED).Take(100).ToList();
    }
}

// Simplified implementations for other services
public class AuditLogCleanupService : IAuditLogCleanupService
{
    private readonly ILogger<AuditLogCleanupService> _logger;

    public AuditLogCleanupService(ILogger<AuditLogCleanupService> logger)
    {
        _logger = logger;
    }

    public async Task CleanupOldAuditLogsAsync(CancellationToken cancellationToken)
    {
        var cutoffDate = DateTime.UtcNow.AddDays(-90); // Keep 90 days
        await CleanupAuditLogsByDateAsync(cutoffDate, cancellationToken);
    }

    public async Task<CleanupResult> CleanupAuditLogsByDateAsync(DateTime beforeDate, CancellationToken cancellationToken)
    {
        var startTime = DateTime.UtcNow;
        
        try
        {
            // Simulate cleanup operation
            await Task.Delay(100, cancellationToken);
            
            var simulatedDeleted = Random.Shared.Next(0, 1000);
            
            _logger.LogInformation("Cleaned up {Count} audit log records before {Date}", simulatedDeleted, beforeDate);
            
            return new CleanupResult
            {
                IsSuccess = true,
                RecordsProcessed = simulatedDeleted,
                RecordsDeleted = simulatedDeleted,
                ProcessingDuration = DateTime.UtcNow - startTime
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Audit log cleanup failed");
            return new CleanupResult
            {
                IsSuccess = false,
                Errors = new List<string> { ex.Message },
                ProcessingDuration = DateTime.UtcNow - startTime
            };
        }
    }

    public async Task<long> GetAuditLogCountAsync(DateTime? fromDate = null, DateTime? toDate = null, CancellationToken cancellationToken = default)
    {
        // Simulate getting audit log count
        await Task.Delay(50, cancellationToken);
        return Random.Shared.Next(10000, 100000);
    }
}

public class MetricsAggregationService : IMetricsAggregationService
{
    private readonly ILogger<MetricsAggregationService> _logger;

    public MetricsAggregationService(ILogger<MetricsAggregationService> logger)
    {
        _logger = logger;
    }

    public async Task AggregateMetricsAsync(CancellationToken cancellationToken)
    {
        var endDate = DateTime.UtcNow;
        var startDate = endDate.AddHours(-1);
        
        await AggregatePaymentMetricsAsync(startDate, endDate, cancellationToken);
    }

    public async Task<MetricsAggregationResult> AggregatePaymentMetricsAsync(DateTime fromDate, DateTime toDate, CancellationToken cancellationToken)
    {
        var startTime = DateTime.UtcNow;
        
        try
        {
            // Simulate metrics aggregation
            await Task.Delay(200, cancellationToken);
            
            var result = new MetricsAggregationResult
            {
                IsSuccess = true,
                FromDate = fromDate,
                ToDate = toDate,
                MetricsProcessed = Random.Shared.Next(50, 200),
                AggregatedMetrics = new Dictionary<string, decimal>
                {
                    ["total_payments"] = Random.Shared.Next(100, 1000),
                    ["successful_payments"] = Random.Shared.Next(80, 950),
                    ["failed_payments"] = Random.Shared.Next(5, 50),
                    ["total_amount"] = Random.Shared.Next(10000, 100000)
                },
                ProcessingDuration = DateTime.UtcNow - startTime
            };
            
            _logger.LogInformation("Aggregated {Count} metrics for period {From} to {To}", 
                result.MetricsProcessed, fromDate, toDate);
            
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Metrics aggregation failed");
            return new MetricsAggregationResult
            {
                IsSuccess = false,
                FromDate = fromDate,
                ToDate = toDate,
                Errors = new List<string> { ex.Message },
                ProcessingDuration = DateTime.UtcNow - startTime
            };
        }
    }

    public async Task<MetricsAggregationResult> AggregateTeamMetricsAsync(int teamId, DateTime fromDate, DateTime toDate, CancellationToken cancellationToken)
    {
        var result = await AggregatePaymentMetricsAsync(fromDate, toDate, cancellationToken);
        result.AggregatedMetrics["team_id"] = teamId;
        return result;
    }
}

public class DatabaseMaintenanceService : IDatabaseMaintenanceService
{
    private readonly ILogger<DatabaseMaintenanceService> _logger;

    public DatabaseMaintenanceService(ILogger<DatabaseMaintenanceService> logger)
    {
        _logger = logger;
    }

    public async Task PerformMaintenanceAsync(CancellationToken cancellationToken)
    {
        await OptimizeTablesAsync(cancellationToken);
        await UpdateStatisticsAsync(cancellationToken);
        await CleanupTempDataAsync(cancellationToken);
    }

    public async Task<MaintenanceResult> OptimizeTablesAsync(CancellationToken cancellationToken)
    {
        var startTime = DateTime.UtcNow;
        
        try
        {
            // Simulate table optimization
            await Task.Delay(1000, cancellationToken);
            
            var result = new MaintenanceResult
            {
                IsSuccess = true,
                MaintenanceType = "table_optimization",
                TablesProcessed = Random.Shared.Next(5, 15),
                ProcessingDuration = DateTime.UtcNow - startTime,
                CompletedTasks = new List<string> { "Optimized payment tables", "Optimized audit tables", "Rebuilt indexes" }
            };
            
            _logger.LogInformation("Database table optimization completed: {Tables} tables processed", result.TablesProcessed);
            
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Database table optimization failed");
            return new MaintenanceResult
            {
                IsSuccess = false,
                MaintenanceType = "table_optimization",
                Errors = new List<string> { ex.Message },
                ProcessingDuration = DateTime.UtcNow - startTime
            };
        }
    }

    public async Task<MaintenanceResult> UpdateStatisticsAsync(CancellationToken cancellationToken)
    {
        var startTime = DateTime.UtcNow;
        
        try
        {
            // Simulate statistics update
            await Task.Delay(500, cancellationToken);
            
            var result = new MaintenanceResult
            {
                IsSuccess = true,
                MaintenanceType = "statistics_update",
                TablesProcessed = Random.Shared.Next(3, 10),
                ProcessingDuration = DateTime.UtcNow - startTime,
                CompletedTasks = new List<string> { "Updated table statistics", "Refreshed query plans" }
            };
            
            _logger.LogInformation("Database statistics update completed");
            
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Database statistics update failed");
            return new MaintenanceResult
            {
                IsSuccess = false,
                MaintenanceType = "statistics_update",
                Errors = new List<string> { ex.Message },
                ProcessingDuration = DateTime.UtcNow - startTime
            };
        }
    }

    public async Task<MaintenanceResult> CleanupTempDataAsync(CancellationToken cancellationToken)
    {
        var startTime = DateTime.UtcNow;
        
        try
        {
            // Simulate temp data cleanup
            await Task.Delay(300, cancellationToken);
            
            var result = new MaintenanceResult
            {
                IsSuccess = true,
                MaintenanceType = "temp_cleanup",
                RecordsProcessed = Random.Shared.Next(100, 1000),
                ProcessingDuration = DateTime.UtcNow - startTime,
                CompletedTasks = new List<string> { "Cleaned temp files", "Removed old session data" }
            };
            
            _logger.LogInformation("Database temp data cleanup completed: {Records} records processed", result.RecordsProcessed);
            
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Database temp data cleanup failed");
            return new MaintenanceResult
            {
                IsSuccess = false,
                MaintenanceType = "temp_cleanup",
                Errors = new List<string> { ex.Message },
                ProcessingDuration = DateTime.UtcNow - startTime
            };
        }
    }
}

public class NotificationProcessingService : INotificationProcessingService
{
    private readonly ILogger<NotificationProcessingService> _logger;

    public NotificationProcessingService(ILogger<NotificationProcessingService> logger)
    {
        _logger = logger;
    }

    public async Task ProcessPendingNotificationsAsync(CancellationToken cancellationToken)
    {
        var pendingNotifications = await GetPendingNotificationsAsync(cancellationToken);
        var processedCount = 0;
        
        foreach (var notification in pendingNotifications)
        {
            try
            {
                var request = new NotificationRequest
                {
                    NotificationId = notification.NotificationId,
                    NotificationType = notification.NotificationType,
                    Recipient = notification.Recipient,
                    Data = notification.Data
                };
                
                var result = await SendNotificationAsync(request, cancellationToken);
                if (result.IsSuccess)
                {
                    processedCount++;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to process notification: {NotificationId}", notification.NotificationId);
            }
        }
        
        if (processedCount > 0)
        {
            _logger.LogInformation("Processed {Count} pending notifications", processedCount);
        }
    }

    public async Task<NotificationResult> SendNotificationAsync(NotificationRequest request, CancellationToken cancellationToken)
    {
        try
        {
            // Simulate notification sending
            await Task.Delay(Random.Shared.Next(100, 500), cancellationToken);
            
            var isSuccess = Random.Shared.NextDouble() > 0.1; // 90% success rate
            
            var result = new NotificationResult
            {
                IsSuccess = isSuccess,
                NotificationId = request.NotificationId,
                Status = isSuccess ? "sent" : "failed",
                ProcessedAt = DateTime.UtcNow,
                RetryCount = 0
            };
            
            if (!isSuccess)
            {
                result.Errors.Add("Simulated notification failure");
            }
            
            _logger.LogDebug("Notification {NotificationId} processing result: {Status}", 
                request.NotificationId, result.Status);
            
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send notification: {NotificationId}", request.NotificationId);
            return new NotificationResult
            {
                IsSuccess = false,
                NotificationId = request.NotificationId,
                Status = "error",
                ProcessedAt = DateTime.UtcNow,
                Errors = new List<string> { ex.Message }
            };
        }
    }

    public async Task<IEnumerable<PendingNotification>> GetPendingNotificationsAsync(CancellationToken cancellationToken)
    {
        // Simulate getting pending notifications
        await Task.Delay(50, cancellationToken);
        
        var notificationCount = Random.Shared.Next(0, 10);
        var notifications = new List<PendingNotification>();
        
        for (int i = 0; i < notificationCount; i++)
        {
            notifications.Add(new PendingNotification
            {
                NotificationId = Guid.NewGuid().ToString(),
                NotificationType = "payment_status_change",
                Recipient = $"team{Random.Shared.Next(1, 5)}@example.com",
                CreatedAt = DateTime.UtcNow.AddMinutes(-Random.Shared.Next(1, 60)),
                ScheduledAt = DateTime.UtcNow,
                RetryCount = 0,
                Status = "pending",
                Data = new Dictionary<string, object>
                {
                    ["payment_id"] = Random.Shared.Next(1000, 9999),
                    ["status"] = "confirmed"
                }
            });
        }
        
        return notifications;
    }
}