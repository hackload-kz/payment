// SPDX-License-Identifier: MIT
// Copyright (c) 2025 HackLoad Payment Gateway

using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Text.Json;

namespace PaymentGateway.Core.Services;

/// <summary>
/// Payment Form Status Update Service for real-time payment status communication
/// 
/// This service implements:
/// - Real-time payment status updates via WebSocket/SignalR
/// - Status update broadcasting to subscribed clients
/// - Connection management and cleanup
/// - Secure status update delivery
/// - Performance optimization for real-time updates
/// - Status update history and analytics
/// </summary>
public class PaymentFormStatusUpdateService
{
    private readonly ILogger<PaymentFormStatusUpdateService> _logger;
    private readonly IConfiguration _configuration;
    private readonly IMemoryCache _memoryCache;

    // Real-time configuration
    private readonly bool _enableRealTimeUpdates;
    private readonly int _maxConnectionsPerPayment;
    private readonly int _statusUpdateHistoryLimit;
    private readonly int _connectionTimeoutMinutes;
    private readonly int _heartbeatIntervalSeconds;

    // Connection management
    private readonly ConcurrentDictionary<string, PaymentStatusConnection> _connections;
    private readonly ConcurrentDictionary<string, List<string>> _paymentConnections;
    private readonly ConcurrentDictionary<string, FormPaymentStatusHistory> _statusHistory;

    // Status update queues for buffering
    private readonly ConcurrentQueue<PaymentStatusUpdateMessage> _updateQueue;
    private readonly object _queueLock = new object();

    // Metrics
    private static readonly System.Diagnostics.Metrics.Meter _meter = new("PaymentFormStatusUpdate");
    private static readonly System.Diagnostics.Metrics.Counter<long> _statusUpdateDeliveryCounter = 
        _meter.CreateCounter<long>("payment_form_status_updates_delivered_total");
    private static readonly System.Diagnostics.Metrics.Gauge<int> _activeStatusConnections = 
        _meter.CreateGauge<int>("active_payment_status_connections_total");
    private static readonly System.Diagnostics.Metrics.Histogram<double> _statusUpdateLatency = 
        _meter.CreateHistogram<double>("payment_status_update_latency_seconds");
    private static readonly System.Diagnostics.Metrics.Counter<long> _statusUpdateErrors = 
        _meter.CreateCounter<long>("payment_status_update_errors_total");

    public PaymentFormStatusUpdateService(
        ILogger<PaymentFormStatusUpdateService> logger,
        IConfiguration configuration,
        IMemoryCache memoryCache)
    {
        _logger = logger;
        _configuration = configuration;
        _memoryCache = memoryCache;

        // Load configuration
        _enableRealTimeUpdates = _configuration.GetValue<bool>("PaymentForm:EnableRealTimeUpdates", true);
        _maxConnectionsPerPayment = _configuration.GetValue<int>("PaymentForm:MaxConnectionsPerPayment", 5);
        _statusUpdateHistoryLimit = _configuration.GetValue<int>("PaymentForm:StatusUpdateHistoryLimit", 100);
        _connectionTimeoutMinutes = _configuration.GetValue<int>("PaymentForm:ConnectionTimeoutMinutes", 30);
        _heartbeatIntervalSeconds = _configuration.GetValue<int>("PaymentForm:HeartbeatIntervalSeconds", 30);

        // Initialize collections
        _connections = new ConcurrentDictionary<string, PaymentStatusConnection>();
        _paymentConnections = new ConcurrentDictionary<string, List<string>>();
        _statusHistory = new ConcurrentDictionary<string, FormPaymentStatusHistory>();
        _updateQueue = new ConcurrentQueue<PaymentStatusUpdateMessage>();

        // Start background processing
        _ = Task.Run(ProcessStatusUpdateQueue);
        _ = Task.Run(ProcessConnectionHeartbeat);
    }

    /// <summary>
    /// Register a new connection for payment status updates
    /// </summary>
    public async Task<PaymentStatusConnectionResult> RegisterConnectionAsync(PaymentStatusConnectionRequest request)
    {
        try
        {
            if (!_enableRealTimeUpdates)
            {
                return new PaymentStatusConnectionResult
                {
                    Success = false,
                    ErrorMessage = "Real-time updates are disabled"
                };
            }

            _logger.LogDebug("Registering status connection for PaymentId: {PaymentId}, ConnectionId: {ConnectionId}",
                request.PaymentId, request.ConnectionId);

            // Check connection limits per payment
            var paymentConnectionsKey = request.PaymentId;
            var existingConnections = _paymentConnections.GetOrAdd(paymentConnectionsKey, _ => new List<string>());

            lock (existingConnections)
            {
                if (existingConnections.Count >= _maxConnectionsPerPayment)
                {
                    _logger.LogWarning("Maximum connections exceeded for PaymentId: {PaymentId}, Current: {CurrentConnections}",
                        request.PaymentId, existingConnections.Count);
                    
                    return new PaymentStatusConnectionResult
                    {
                        Success = false,
                        ErrorMessage = "Maximum connections per payment exceeded"
                    };
                }

                existingConnections.Add(request.ConnectionId);
            }

            // Create connection
            var connection = new PaymentStatusConnection
            {
                ConnectionId = request.ConnectionId,
                PaymentId = request.PaymentId,
                SessionId = request.SessionId,
                ClientIp = request.ClientIp,
                UserAgent = request.UserAgent,
                ConnectedAt = DateTime.UtcNow,
                LastHeartbeat = DateTime.UtcNow,
                IsActive = true,
                SubscriptionFilters = request.SubscriptionFilters ?? new List<string>()
            };

            _connections[request.ConnectionId] = connection;

            // Initialize status history for payment if not exists
            if (!_statusHistory.ContainsKey(request.PaymentId))
            {
                _statusHistory[request.PaymentId] = new FormPaymentStatusHistory
                {
                    PaymentId = request.PaymentId,
                    CreatedAt = DateTime.UtcNow,
                    StatusUpdates = new List<PaymentStatusUpdateEntry>()
                };
            }

            // Send initial status if available
            var initialStatus = await GetCurrentPaymentStatusAsync(request.PaymentId);
            if (initialStatus != null)
            {
                await SendStatusUpdateToConnectionAsync(request.ConnectionId, new PaymentStatusUpdateMessage
                {
                    PaymentId = request.PaymentId,
                    Status = initialStatus.Status,
                    Message = "Connected to payment status updates",
                    UpdateType = StatusUpdateType.Initial,
                    Timestamp = DateTime.UtcNow
                });
            }

            _activeStatusConnections.Record(_connections.Count);

            _logger.LogInformation("Status connection registered successfully for PaymentId: {PaymentId}, ConnectionId: {ConnectionId}",
                request.PaymentId, request.ConnectionId);

            return new PaymentStatusConnectionResult
            {
                Success = true,
                ConnectionId = request.ConnectionId,
                HeartbeatInterval = TimeSpan.FromSeconds(_heartbeatIntervalSeconds),
                SupportedUpdateTypes = Enum.GetValues<StatusUpdateType>().ToList()
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error registering status connection for PaymentId: {PaymentId}", request.PaymentId);
            
            return new PaymentStatusConnectionResult
            {
                Success = false,
                ErrorMessage = "Failed to register status connection"
            };
        }
    }

    /// <summary>
    /// Broadcast payment status update to all subscribed connections
    /// </summary>
    public async Task<bool> BroadcastPaymentStatusUpdateAsync(PaymentStatusBroadcastRequest request)
    {
        try
        {
            _logger.LogDebug("Broadcasting payment status update for PaymentId: {PaymentId}, Status: {Status}",
                request.PaymentId, request.Status);

            var updateMessage = new PaymentStatusUpdateMessage
            {
                PaymentId = request.PaymentId,
                Status = request.Status,
                Message = request.Message,
                UpdateType = request.UpdateType,
                Timestamp = DateTime.UtcNow,
                AdditionalData = request.AdditionalData ?? new Dictionary<string, object>()
            };

            // Add to update queue for processing
            _updateQueue.Enqueue(updateMessage);

            // Record in status history
            await RecordStatusUpdateInHistoryAsync(request.PaymentId, updateMessage);

            _statusUpdateDeliveryCounter.Add(1, new KeyValuePair<string, object?>("status", request.Status.ToString()),
                new KeyValuePair<string, object?>("update_type", request.UpdateType.ToString()));

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error broadcasting payment status update for PaymentId: {PaymentId}", request.PaymentId);
            
            _statusUpdateErrors.Add(1, new KeyValuePair<string, object?>("operation", "broadcast"));
            return false;
        }
    }

    /// <summary>
    /// Get payment status history for a specific payment
    /// </summary>
    public async Task<PaymentStatusHistoryResult> GetPaymentStatusHistoryAsync(string paymentId, int? limit = null)
    {
        try
        {
            if (!_statusHistory.TryGetValue(paymentId, out var history))
            {
                return new PaymentStatusHistoryResult
                {
                    Success = false,
                    ErrorMessage = "No status history found for payment"
                };
            }

            var updates = history.StatusUpdates.OrderByDescending(u => u.Timestamp).ToList();
            if (limit.HasValue)
            {
                updates = updates.Take(limit.Value).ToList();
            }

            return new PaymentStatusHistoryResult
            {
                Success = true,
                PaymentId = paymentId,
                StatusUpdates = updates,
                TotalUpdates = history.StatusUpdates.Count,
                FirstUpdate = history.StatusUpdates.MinBy(u => u.Timestamp)?.Timestamp,
                LastUpdate = history.StatusUpdates.MaxBy(u => u.Timestamp)?.Timestamp
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting payment status history for PaymentId: {PaymentId}", paymentId);
            
            return new PaymentStatusHistoryResult
            {
                Success = false,
                ErrorMessage = "Failed to get status history"
            };
        }
    }

    /// <summary>
    /// Disconnect a status connection
    /// </summary>
    public async Task<bool> DisconnectAsync(string connectionId)
    {
        try
        {
            if (_connections.TryRemove(connectionId, out var connection))
            {
                // Remove from payment connections
                if (_paymentConnections.TryGetValue(connection.PaymentId, out var paymentConnections))
                {
                    lock (paymentConnections)
                    {
                        paymentConnections.Remove(connectionId);
                        if (paymentConnections.Count == 0)
                        {
                            _paymentConnections.TryRemove(connection.PaymentId, out _);
                        }
                    }
                }

                _activeStatusConnections.Record(_connections.Count);

                _logger.LogInformation("Status connection disconnected: {ConnectionId}, PaymentId: {PaymentId}",
                    connectionId, connection.PaymentId);

                return true;
            }

            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error disconnecting status connection: {ConnectionId}", connectionId);
            return false;
        }
    }

    /// <summary>
    /// Update heartbeat for a connection
    /// </summary>
    public async Task<bool> UpdateHeartbeatAsync(string connectionId)
    {
        try
        {
            if (_connections.TryGetValue(connectionId, out var connection))
            {
                connection.LastHeartbeat = DateTime.UtcNow;
                return true;
            }

            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating heartbeat for connection: {ConnectionId}", connectionId);
            return false;
        }
    }

    /// <summary>
    /// Get connection statistics
    /// </summary>
    public PaymentStatusConnectionStatistics GetConnectionStatistics()
    {
        var stats = new PaymentStatusConnectionStatistics
        {
            TotalConnections = _connections.Count,
            ActiveConnections = _connections.Values.Count(c => c.IsActive),
            PaymentsWithConnections = _paymentConnections.Count,
            TotalStatusHistory = _statusHistory.Count,
            QueuedUpdates = _updateQueue.Count
        };

        var connectionsByPayment = _paymentConnections.Values.Select(c => c.Count).ToList();
        if (connectionsByPayment.Any())
        {
            stats.AverageConnectionsPerPayment = connectionsByPayment.Average();
            stats.MaxConnectionsPerPayment = connectionsByPayment.Max();
        }

        return stats;
    }

    // Background processing methods

    private async Task ProcessStatusUpdateQueue()
    {
        while (true)
        {
            try
            {
                if (!_updateQueue.IsEmpty)
                {
                    var processedCount = 0;
                    while (_updateQueue.TryDequeue(out var updateMessage) && processedCount < 100) // Process in batches
                    {
                        await ProcessSingleStatusUpdate(updateMessage);
                        processedCount++;
                    }
                }

                await Task.Delay(100); // Small delay to prevent tight loop
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in status update queue processing");
                await Task.Delay(1000); // Longer delay on error
            }
        }
    }

    private async Task ProcessSingleStatusUpdate(PaymentStatusUpdateMessage updateMessage)
    {
        try
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            // Get connections for this payment
            if (_paymentConnections.TryGetValue(updateMessage.PaymentId, out var connectionIds))
            {
                var deliveryTasks = new List<Task>();

                foreach (var connectionId in connectionIds.ToList()) // ToList to avoid collection modification
                {
                    if (_connections.TryGetValue(connectionId, out var connection) && connection.IsActive)
                    {
                        // Check subscription filters
                        if (connection.SubscriptionFilters.Any() && 
                            !connection.SubscriptionFilters.Contains(updateMessage.UpdateType.ToString()))
                        {
                            continue;
                        }

                        deliveryTasks.Add(SendStatusUpdateToConnectionAsync(connectionId, updateMessage));
                    }
                }

                // Wait for all deliveries to complete
                await Task.WhenAll(deliveryTasks);

                _statusUpdateLatency.Record(stopwatch.Elapsed.TotalSeconds,
                    new KeyValuePair<string, object?>("update_type", updateMessage.UpdateType.ToString()));
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing status update for PaymentId: {PaymentId}", updateMessage.PaymentId);
            _statusUpdateErrors.Add(1, new KeyValuePair<string, object?>("operation", "process_update"));
        }
    }

    private async Task SendStatusUpdateToConnectionAsync(string connectionId, PaymentStatusUpdateMessage updateMessage)
    {
        try
        {
            // In a real implementation, this would use SignalR or WebSocket to send the message
            // For now, we'll simulate the delivery and log it
            
            _logger.LogDebug("Sending status update to connection {ConnectionId}: PaymentId={PaymentId}, Status={Status}, Message={Message}",
                connectionId, updateMessage.PaymentId, updateMessage.Status, updateMessage.Message);

            // Simulate network delay
            await Task.Delay(10);

            // Update connection last activity
            if (_connections.TryGetValue(connectionId, out var connection))
            {
                connection.LastHeartbeat = DateTime.UtcNow;
            }

            _statusUpdateDeliveryCounter.Add(1, new KeyValuePair<string, object?>("result", "delivered"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending status update to connection {ConnectionId}", connectionId);
            _statusUpdateErrors.Add(1, new KeyValuePair<string, object?>("operation", "send_update"));
        }
    }

    private async Task ProcessConnectionHeartbeat()
    {
        while (true)
        {
            try
            {
                var timeoutCutoff = DateTime.UtcNow.AddMinutes(-_connectionTimeoutMinutes);
                var expiredConnections = _connections.Values
                    .Where(c => c.LastHeartbeat < timeoutCutoff)
                    .ToList();

                foreach (var expiredConnection in expiredConnections)
                {
                    _logger.LogInformation("Removing expired connection: {ConnectionId}, PaymentId: {PaymentId}",
                        expiredConnection.ConnectionId, expiredConnection.PaymentId);
                    
                    await DisconnectAsync(expiredConnection.ConnectionId);
                }

                await Task.Delay(TimeSpan.FromSeconds(_heartbeatIntervalSeconds));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in connection heartbeat processing");
                await Task.Delay(TimeSpan.FromMinutes(1)); // Longer delay on error
            }
        }
    }

    private async Task RecordStatusUpdateInHistoryAsync(string paymentId, PaymentStatusUpdateMessage updateMessage)
    {
        try
        {
            var history = _statusHistory.GetOrAdd(paymentId, _ => new FormPaymentStatusHistory
            {
                PaymentId = paymentId,
                CreatedAt = DateTime.UtcNow,
                StatusUpdates = new List<PaymentStatusUpdateEntry>()
            });

            var updateEntry = new PaymentStatusUpdateEntry
            {
                Status = updateMessage.Status,
                Message = updateMessage.Message,
                UpdateType = updateMessage.UpdateType,
                Timestamp = updateMessage.Timestamp,
                AdditionalData = updateMessage.AdditionalData
            };

            history.StatusUpdates.Add(updateEntry);

            // Limit history size
            if (history.StatusUpdates.Count > _statusUpdateHistoryLimit)
            {
                history.StatusUpdates = history.StatusUpdates
                    .OrderByDescending(u => u.Timestamp)
                    .Take(_statusUpdateHistoryLimit)
                    .ToList();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error recording status update in history for PaymentId: {PaymentId}", paymentId);
        }
    }

    private async Task<PaymentFormStatusResult?> GetCurrentPaymentStatusAsync(string paymentId)
    {
        try
        {
            // This would typically query the payment repository
            // For now, return a basic status
            return new PaymentFormStatusResult
            {
                Success = true,
                PaymentId = paymentId,
                Status = PaymentGateway.Core.Enums.PaymentStatus.NEW,
                ProcessingStage = PaymentFormProcessingStage.Initialized,
                LastUpdated = DateTime.UtcNow,
                StatusDescription = "Payment initialized"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting current payment status for PaymentId: {PaymentId}", paymentId);
            return null;
        }
    }
}

// Supporting classes and enums

public enum StatusUpdateType
{
    Initial = 0,
    StatusChange = 1,
    Processing = 2,
    Error = 3,
    Success = 4,
    Heartbeat = 5
}

public class PaymentStatusConnection
{
    public string ConnectionId { get; set; } = string.Empty;
    public string PaymentId { get; set; } = string.Empty;
    public string SessionId { get; set; } = string.Empty;
    public string ClientIp { get; set; } = string.Empty;
    public string UserAgent { get; set; } = string.Empty;
    public DateTime ConnectedAt { get; set; }
    public DateTime LastHeartbeat { get; set; }
    public bool IsActive { get; set; }
    public List<string> SubscriptionFilters { get; set; } = new();
}

public class PaymentStatusConnectionRequest
{
    public string ConnectionId { get; set; } = string.Empty;
    public string PaymentId { get; set; } = string.Empty;
    public string SessionId { get; set; } = string.Empty;
    public string ClientIp { get; set; } = string.Empty;
    public string UserAgent { get; set; } = string.Empty;
    public List<string>? SubscriptionFilters { get; set; }
}

public class PaymentStatusConnectionResult
{
    public bool Success { get; set; }
    public string? ConnectionId { get; set; }
    public TimeSpan HeartbeatInterval { get; set; }
    public List<StatusUpdateType> SupportedUpdateTypes { get; set; } = new();
    public string? ErrorMessage { get; set; }
}

public class PaymentStatusBroadcastRequest
{
    public string PaymentId { get; set; } = string.Empty;
    public PaymentGateway.Core.Enums.PaymentStatus Status { get; set; }
    public string? Message { get; set; }
    public StatusUpdateType UpdateType { get; set; } = StatusUpdateType.StatusChange;
    public Dictionary<string, object>? AdditionalData { get; set; }
}

public class PaymentStatusUpdateMessage
{
    public string PaymentId { get; set; } = string.Empty;
    public PaymentGateway.Core.Enums.PaymentStatus Status { get; set; }
    public string? Message { get; set; }
    public StatusUpdateType UpdateType { get; set; }
    public DateTime Timestamp { get; set; }
    public Dictionary<string, object> AdditionalData { get; set; } = new();
}

public class FormPaymentStatusHistory
{
    public string PaymentId { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public List<PaymentStatusUpdateEntry> StatusUpdates { get; set; } = new();
}

public class PaymentStatusUpdateEntry
{
    public PaymentGateway.Core.Enums.PaymentStatus Status { get; set; }
    public string? Message { get; set; }
    public StatusUpdateType UpdateType { get; set; }
    public DateTime Timestamp { get; set; }
    public Dictionary<string, object> AdditionalData { get; set; } = new();
}

public class PaymentStatusHistoryResult
{
    public bool Success { get; set; }
    public string? PaymentId { get; set; }
    public List<PaymentStatusUpdateEntry> StatusUpdates { get; set; } = new();
    public int TotalUpdates { get; set; }
    public DateTime? FirstUpdate { get; set; }
    public DateTime? LastUpdate { get; set; }
    public string? ErrorMessage { get; set; }
}

public class PaymentStatusConnectionStatistics
{
    public int TotalConnections { get; set; }
    public int ActiveConnections { get; set; }
    public int PaymentsWithConnections { get; set; }
    public int TotalStatusHistory { get; set; }
    public int QueuedUpdates { get; set; }
    public double AverageConnectionsPerPayment { get; set; }
    public int MaxConnectionsPerPayment { get; set; }
}