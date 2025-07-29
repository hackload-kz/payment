using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Data;
using System.Data.Common;

namespace PaymentGateway.Core.Services;

public interface IDatabaseResilienceService
{
    Task<TResult> ExecuteWithRetryAsync<TResult>(Func<Task<TResult>> operation, CancellationToken cancellationToken = default);
    Task ExecuteWithRetryAsync(Func<Task> operation, CancellationToken cancellationToken = default);
    Task<bool> CheckDatabaseConnectionAsync(CancellationToken cancellationToken = default);
    Task<DatabaseHealthStatus> GetDatabaseHealthAsync(CancellationToken cancellationToken = default);
}

public enum DatabaseHealthStatus
{
    Healthy,
    Degraded,
    Unhealthy
}

public class DatabaseResilienceService : IDatabaseResilienceService
{
    private readonly ILogger<DatabaseResilienceService> _logger;
    private readonly int _maxRetryAttempts = 3;
    private readonly TimeSpan _baseDelay = TimeSpan.FromSeconds(1);
    
    public DatabaseResilienceService(ILogger<DatabaseResilienceService> logger)
    {
        _logger = logger;
    }
    
    public async Task<TResult> ExecuteWithRetryAsync<TResult>(Func<Task<TResult>> operation, CancellationToken cancellationToken = default)
    {
        var lastException = default(Exception);
        
        for (int attempt = 1; attempt <= _maxRetryAttempts; attempt++)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                return await operation();
            }
            catch (DbException ex) when (attempt < _maxRetryAttempts)
            {
                lastException = ex;
                var delay = TimeSpan.FromMilliseconds(_baseDelay.TotalMilliseconds * Math.Pow(2, attempt - 1) + Random.Shared.Next(0, 100));
                _logger.LogWarning(ex, "Database operation failed. Retry {Attempt}/{MaxAttempts} in {Delay}ms", 
                    attempt, _maxRetryAttempts, delay.TotalMilliseconds);
                await Task.Delay(delay, cancellationToken);
            }
            catch (TimeoutException ex) when (attempt < _maxRetryAttempts)
            {
                lastException = ex;
                var delay = TimeSpan.FromMilliseconds(_baseDelay.TotalMilliseconds * Math.Pow(2, attempt - 1) + Random.Shared.Next(0, 100));
                _logger.LogWarning(ex, "Database timeout. Retry {Attempt}/{MaxAttempts} in {Delay}ms", 
                    attempt, _maxRetryAttempts, delay.TotalMilliseconds);
                await Task.Delay(delay, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Database operation failed with non-retryable error");
                throw;
            }
        }
        
        _logger.LogError(lastException, "Database operation failed after {MaxAttempts} attempts", _maxRetryAttempts);
        throw lastException ?? new InvalidOperationException("Database operation failed after all retry attempts");
    }
    
    public async Task ExecuteWithRetryAsync(Func<Task> operation, CancellationToken cancellationToken = default)
    {
        await ExecuteWithRetryAsync(async () =>
        {
            await operation();
            return Task.CompletedTask;
        }, cancellationToken);
    }
    
    public async Task<bool> CheckDatabaseConnectionAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            // This would need to be injected with actual DbContext
            // For now, implementing a simple check pattern
            await Task.Delay(10, cancellationToken); // Simulate connection check
            
            return await ExecuteWithRetryAsync(async () =>
            {
                // Simulate database ping
                await Task.Delay(5, cancellationToken);
                return true;
            }, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Database connection check failed");
            return false;
        }
    }
    
    public async Task<DatabaseHealthStatus> GetDatabaseHealthAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var connectionCheckTask = CheckDatabaseConnectionAsync(cancellationToken);
            var timeoutTask = Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
            
            var completedTask = await Task.WhenAny(connectionCheckTask, timeoutTask);
            
            if (completedTask == timeoutTask)
            {
                _logger.LogWarning("Database health check timed out");
                return DatabaseHealthStatus.Degraded;
            }
            
            var isHealthy = await connectionCheckTask;
            
            if (isHealthy)
            {
                return DatabaseHealthStatus.Healthy;
            }
            else
            {
                return DatabaseHealthStatus.Unhealthy;
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Database health check was cancelled");
            return DatabaseHealthStatus.Degraded;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking database health");
            return DatabaseHealthStatus.Unhealthy;
        }
    }
}


