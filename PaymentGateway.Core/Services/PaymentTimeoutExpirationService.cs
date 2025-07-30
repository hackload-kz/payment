// SPDX-License-Identifier: MIT
// Copyright (c) 2025 HackLoad Payment Gateway

using PaymentGateway.Core.Entities;
using PaymentGateway.Core.Interfaces;
using PaymentGateway.Core.Repositories;
using PaymentGateway.Core.Enums;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.Concurrent;
using Prometheus;

namespace PaymentGateway.Core.Services;

/// <summary>
/// Comprehensive payment timeout and expiration handling service
/// </summary>
public interface IPaymentTimeoutExpirationService
{
    Task<bool> IsPaymentExpiredAsync(Guid paymentId, CancellationToken cancellationToken = default);
    Task<TimeSpan?> GetTimeToExpirationAsync(Guid paymentId, CancellationToken cancellationToken = default);
    Task<IEnumerable<Payment>> GetExpiringPaymentsAsync(TimeSpan warningPeriod, CancellationToken cancellationToken = default);
    Task<IEnumerable<Payment>> GetExpiredPaymentsAsync(CancellationToken cancellationToken = default);
    Task<int> ExpirePaymentsAsync(IEnumerable<Guid> paymentIds, CancellationToken cancellationToken = default);
    Task<int> ProcessAllExpiredPaymentsAsync(CancellationToken cancellationToken = default);
    Task SchedulePaymentExpirationAsync(Guid paymentId, DateTime expirationTime, CancellationToken cancellationToken = default);
    Task<PaymentTimeoutConfiguration> GetTimeoutConfigurationAsync(Guid teamId, CancellationToken cancellationToken = default);
    Task UpdateTimeoutConfigurationAsync(Guid teamId, PaymentTimeoutConfiguration configuration, CancellationToken cancellationToken = default);
    Task<ExpirationStatistics> GetExpirationStatisticsAsync(Guid? teamId = null, TimeSpan? period = null, CancellationToken cancellationToken = default);
}

public class PaymentTimeoutConfiguration
{
    public TimeSpan DefaultTimeout { get; set; } = TimeSpan.FromMinutes(15);
    public TimeSpan MaxTimeout { get; set; } = TimeSpan.FromHours(24);
    public TimeSpan MinTimeout { get; set; } = TimeSpan.FromMinutes(1);
    public TimeSpan WarningPeriod { get; set; } = TimeSpan.FromMinutes(2);
    public bool EnableAutomaticExpiration { get; set; } = true;
    public bool EnableExpirationWarnings { get; set; } = true;
    public Dictionary<PaymentStatus, TimeSpan> StatusSpecificTimeouts { get; set; } = new()
    {
        [PaymentStatus.NEW] = TimeSpan.FromMinutes(15),
        [PaymentStatus.PROCESSING] = TimeSpan.FromMinutes(30),
        [PaymentStatus.AUTHORIZED] = TimeSpan.FromHours(24)
    };
}

public class ExpirationStatistics
{
    public int TotalExpirations { get; set; }
    public int AutomaticExpirations { get; set; }
    public int ManualExpirations { get; set; }
    public TimeSpan AveragePaymentLifetime { get; set; }
    public Dictionary<PaymentStatus, int> ExpirationsByStatus { get; set; } = new();
    public Dictionary<string, int> ExpirationsByTeam { get; set; } = new();
    public Dictionary<DateTime, int> ExpirationsByHour { get; set; } = new();
}

public class PaymentTimeoutExpirationService : IPaymentTimeoutExpirationService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IPaymentRepository _paymentRepository;
    private readonly ITeamRepository _teamRepository;
    private readonly ILogger<PaymentTimeoutExpirationService> _logger;
    
    // Scheduled expirations tracking
    private readonly ConcurrentDictionary<Guid, DateTime> _scheduledExpirations = new();
    private readonly ConcurrentDictionary<Guid, PaymentTimeoutConfiguration> _teamConfigurations = new();
    
    // Metrics
    private static readonly Counter ExpirationOperations = Metrics
        .CreateCounter("payment_expiration_operations_total", "Total payment expiration operations", new[] { "type", "status" });
    
    private static readonly Histogram ExpirationProcessingDuration = Metrics
        .CreateHistogram("payment_expiration_processing_duration_seconds", "Payment expiration processing duration");
    
    private static readonly Gauge ExpiredPaymentsGauge = Metrics
        .CreateGauge("expired_payments_total", "Total number of expired payments");
    
    private static readonly Gauge ExpiringPaymentsGauge = Metrics
        .CreateGauge("expiring_payments_total", "Total number of payments expiring soon");

    // Default configuration
    private static readonly PaymentTimeoutConfiguration DefaultConfiguration = new();

    public PaymentTimeoutExpirationService(
        IServiceProvider serviceProvider,
        IPaymentRepository paymentRepository,
        ITeamRepository teamRepository,
        ILogger<PaymentTimeoutExpirationService> logger)
    {
        _serviceProvider = serviceProvider;
        _paymentRepository = paymentRepository;
        _teamRepository = teamRepository;
        _logger = logger;
    }

    public async Task<bool> IsPaymentExpiredAsync(Guid paymentId, CancellationToken cancellationToken = default)
    {
        try
        {
            var payment = await _paymentRepository.GetByIdAsync(paymentId, cancellationToken);
            if (payment == null) return false;

            // Check if payment is already in expired state
            if (payment.Status == PaymentStatus.EXPIRED) return true;

            // Don't expire final states
            if (payment.Status == PaymentStatus.CONFIRMED ||
                payment.Status == PaymentStatus.CANCELLED ||
                payment.Status == PaymentStatus.REFUNDED) return false;

            var configuration = await GetTimeoutConfigurationAsync(payment.TeamId, cancellationToken);
            var timeout = GetTimeoutForPaymentStatus(payment.Status, configuration);
            
            return DateTime.UtcNow - payment.CreatedAt > timeout;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to check payment expiration: {PaymentId}", paymentId);
            return false;
        }
    }

    public async Task<TimeSpan?> GetTimeToExpirationAsync(Guid paymentId, CancellationToken cancellationToken = default)
    {
        try
        {
            var payment = await _paymentRepository.GetByIdAsync(paymentId, cancellationToken);
            if (payment == null) return null;

            // Already expired or in final state
            if (payment.Status == PaymentStatus.EXPIRED ||
                payment.Status == PaymentStatus.CONFIRMED ||
                payment.Status == PaymentStatus.CANCELLED ||
                payment.Status == PaymentStatus.REFUNDED) return null;

            var configuration = await GetTimeoutConfigurationAsync(payment.TeamId, cancellationToken);
            var timeout = GetTimeoutForPaymentStatus(payment.Status, configuration);
            var expirationTime = payment.CreatedAt.Add(timeout);
            
            var timeToExpiration = expirationTime - DateTime.UtcNow;
            return timeToExpiration > TimeSpan.Zero ? timeToExpiration : TimeSpan.Zero;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get time to expiration: {PaymentId}", paymentId);
            return null;
        }
    }

    public async Task<IEnumerable<Payment>> GetExpiringPaymentsAsync(TimeSpan warningPeriod, CancellationToken cancellationToken = default)
    {
        try
        {
            var expiringPayments = new List<Payment>();
            var activeStatuses = new[]
            {
                PaymentStatus.NEW,
                PaymentStatus.PROCESSING,
                PaymentStatus.AUTHORIZED
            };

            foreach (var status in activeStatuses)
            {
                var payments = await _paymentRepository.GetPaymentsByStatusAsync(status, cancellationToken);
                
                foreach (var payment in payments)
                {
                    var timeToExpiration = await GetTimeToExpirationAsync(payment.Id, cancellationToken);
                    if (timeToExpiration.HasValue && timeToExpiration.Value <= warningPeriod)
                    {
                        expiringPayments.Add(payment);
                    }
                }
            }

            ExpiringPaymentsGauge.Set(expiringPayments.Count);
            return expiringPayments;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get expiring payments");
            return new List<Payment>();
        }
    }

    public async Task<IEnumerable<Payment>> GetExpiredPaymentsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var expiredPayments = new List<Payment>();
            var activeStatuses = new[]
            {
                PaymentStatus.NEW,
                PaymentStatus.PROCESSING,
                PaymentStatus.AUTHORIZED
            };

            foreach (var status in activeStatuses)
            {
                var payments = await _paymentRepository.GetPaymentsByStatusAsync(status, cancellationToken);
                
                foreach (var payment in payments)
                {
                    var isExpired = await IsPaymentExpiredAsync(payment.Id, cancellationToken);
                    if (isExpired)
                    {
                        expiredPayments.Add(payment);
                    }
                }
            }

            ExpiredPaymentsGauge.Set(expiredPayments.Count);
            return expiredPayments;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get expired payments");
            return new List<Payment>();
        }
    }

    public async Task<int> ExpirePaymentsAsync(IEnumerable<Guid> paymentIds, CancellationToken cancellationToken = default)
    {
        using var activity = ExpirationProcessingDuration.NewTimer();
        var expiredCount = 0;

        try
        {
            using var scope = _serviceProvider.CreateScope();
            var lifecycleService = scope.ServiceProvider.GetRequiredService<IPaymentLifecycleManagementService>();

            foreach (var paymentId in paymentIds)
            {
                try
                {
                    var isExpired = await IsPaymentExpiredAsync(paymentId, cancellationToken);
                    if (isExpired)
                    {
                        await lifecycleService.ExpirePaymentAsync(paymentId, cancellationToken);
                        expiredCount++;
                        ExpirationOperations.WithLabels("automatic", "success").Inc();
                        _logger.LogInformation("Payment expired: {PaymentId}", paymentId);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to expire payment: {PaymentId}", paymentId);
                    ExpirationOperations.WithLabels("automatic", "failed").Inc();
                }
            }

            if (expiredCount > 0)
            {
                _logger.LogInformation("Expired {Count} payments", expiredCount);
            }

            return expiredCount;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to expire payments");
            return expiredCount;
        }
    }

    public async Task<int> ProcessAllExpiredPaymentsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var expiredPayments = await GetExpiredPaymentsAsync(cancellationToken);
            var paymentIds = expiredPayments.Select(p => p.Id);
            
            return await ExpirePaymentsAsync(paymentIds, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process all expired payments");
            return 0;
        }
    }

    public async Task SchedulePaymentExpirationAsync(Guid paymentId, DateTime expirationTime, CancellationToken cancellationToken = default)
    {
        try
        {
            _scheduledExpirations.AddOrUpdate(paymentId, expirationTime, (k, v) => expirationTime);
            
            _logger.LogInformation("Payment expiration scheduled: {PaymentId}, ExpirationTime: {ExpirationTime}", 
                paymentId, expirationTime);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to schedule payment expiration: {PaymentId}", paymentId);
            throw;
        }
    }

    public async Task<PaymentTimeoutConfiguration> GetTimeoutConfigurationAsync(Guid teamId, CancellationToken cancellationToken = default)
    {
        try
        {
            if (_teamConfigurations.TryGetValue(teamId, out var cachedConfig))
            {
                return cachedConfig;
            }

            // TODO: Implement proper team lookup by integer teamId
            // For now, using default configuration since Team.TimeoutConfiguration doesn't exist
            // and there's no clear mapping between int teamId and Team.Id (Guid)

            // Use default configuration
            _teamConfigurations.TryAdd(teamId, DefaultConfiguration);
            return DefaultConfiguration;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get timeout configuration for team: {TeamId}", teamId);
            return DefaultConfiguration;
        }
    }

    public async Task UpdateTimeoutConfigurationAsync(Guid teamId, PaymentTimeoutConfiguration configuration, CancellationToken cancellationToken = default)
    {
        try
        {
            // Validate configuration
            if (configuration.DefaultTimeout < configuration.MinTimeout ||
                configuration.DefaultTimeout > configuration.MaxTimeout)
            {
                throw new ArgumentException("Invalid timeout configuration");
            }

            // Update team configuration (this would typically save to database)
            _teamConfigurations.AddOrUpdate(teamId, configuration, (k, v) => configuration);
            
            _logger.LogInformation("Timeout configuration updated for team: {TeamId}", teamId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update timeout configuration for team: {TeamId}", teamId);
            throw;
        }
    }

    public async Task<ExpirationStatistics> GetExpirationStatisticsAsync(Guid? teamId = null, TimeSpan? period = null, CancellationToken cancellationToken = default)
    {
        try
        {
            // This would typically query an audit/analytics database
            // For now, return basic statistics
            var stats = new ExpirationStatistics
            {
                TotalExpirations = 0,
                AutomaticExpirations = 0,
                ManualExpirations = 0,
                AveragePaymentLifetime = TimeSpan.FromMinutes(10)
            };

            return stats;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get expiration statistics");
            return new ExpirationStatistics();
        }
    }

    private TimeSpan GetTimeoutForPaymentStatus(PaymentStatus status, PaymentTimeoutConfiguration configuration)
    {
        if (configuration.StatusSpecificTimeouts.TryGetValue(status, out var statusTimeout))
        {
            return statusTimeout;
        }

        return configuration.DefaultTimeout;
    }
}

/// <summary>
/// Background service for processing payment timeouts and expirations
/// </summary>
public class PaymentTimeoutBackgroundService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<PaymentTimeoutBackgroundService> _logger;
    private readonly TimeSpan _processingInterval = TimeSpan.FromMinutes(1);

    public PaymentTimeoutBackgroundService(
        IServiceProvider serviceProvider,
        ILogger<PaymentTimeoutBackgroundService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Payment timeout background service started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var timeoutService = scope.ServiceProvider.GetRequiredService<IPaymentTimeoutExpirationService>();

                // Process expired payments
                var expiredCount = await timeoutService.ProcessAllExpiredPaymentsAsync(stoppingToken);
                if (expiredCount > 0)
                {
                    _logger.LogInformation("Processed {Count} expired payments", expiredCount);
                }

                // Check for expiring payments and log warnings
                var expiringPayments = await timeoutService.GetExpiringPaymentsAsync(TimeSpan.FromMinutes(5), stoppingToken);
                if (expiringPayments.Any())
                {
                    _logger.LogWarning("Found {Count} payments expiring within 5 minutes", expiringPayments.Count());
                }

                await Task.Delay(_processingInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                // Expected when cancellation is requested
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in payment timeout background service");
                
                // Wait before retrying to avoid tight error loops
                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
            }
        }

        _logger.LogInformation("Payment timeout background service stopped");
    }
}