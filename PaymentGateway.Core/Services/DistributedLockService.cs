using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace PaymentGateway.Core.Services;

public interface IDistributedLockService
{
    Task<IDistributedLock?> AcquireLockAsync(string resource, TimeSpan expiry, CancellationToken cancellationToken = default);
    Task<bool> AcquireLockAsync(string resource, TimeSpan expiry);
    Task<bool> ReleaseLockAsync(string resource, string lockId);
    Task ReleaseLockAsync(string resource);
    Task<Dictionary<string, List<string>>> GetLockDependenciesAsync();
}

public interface IDistributedLock : IDisposable, IAsyncDisposable
{
    string Resource { get; }
    string LockId { get; }
    DateTime ExpiresAt { get; }
    Task<bool> RenewAsync(TimeSpan expiry);
}

public class DistributedLockOptions
{
    public TimeSpan DefaultTimeout { get; set; } = TimeSpan.FromSeconds(30);
    public TimeSpan DefaultExpiry { get; set; } = TimeSpan.FromMinutes(5);
    public int MaxRetryAttempts { get; set; } = 3;
    public TimeSpan RetryDelay { get; set; } = TimeSpan.FromMilliseconds(100);
}

public class InMemoryDistributedLockService : IDistributedLockService
{
    private readonly ConcurrentDictionary<string, LockInfo> _locks;
    private readonly ILogger<InMemoryDistributedLockService> _logger;
    private readonly DistributedLockOptions _options;
    private readonly Timer _cleanupTimer;

    public InMemoryDistributedLockService(
        ILogger<InMemoryDistributedLockService> logger,
        IOptions<DistributedLockOptions> options)
    {
        _locks = new ConcurrentDictionary<string, LockInfo>();
        _logger = logger;
        _options = options.Value;
        
        _cleanupTimer = new Timer(CleanupExpiredLocks, null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
    }

    public async Task<IDistributedLock?> AcquireLockAsync(string resource, TimeSpan expiry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(resource);
        
        var lockId = Guid.NewGuid().ToString();
        var expiresAt = DateTime.UtcNow.Add(expiry);
        var lockInfo = new LockInfo(resource, lockId, expiresAt);

        for (int attempt = 0; attempt < _options.MaxRetryAttempts; attempt++)
        {
            if (_locks.TryAdd(resource, lockInfo))
            {
                _logger.LogDebug("Acquired distributed lock for resource {Resource} with ID {LockId}", resource, lockId);
                return new DistributedLock(this, resource, lockId, expiresAt);
            }

            if (_locks.TryGetValue(resource, out var existingLock) && existingLock.ExpiresAt <= DateTime.UtcNow)
            {
                if (_locks.TryUpdate(resource, lockInfo, existingLock))
                {
                    _logger.LogDebug("Acquired expired distributed lock for resource {Resource} with ID {LockId}", resource, lockId);
                    return new DistributedLock(this, resource, lockId, expiresAt);
                }
            }

            if (attempt < _options.MaxRetryAttempts - 1)
            {
                await Task.Delay(_options.RetryDelay, cancellationToken);
            }
        }

        _logger.LogWarning("Failed to acquire distributed lock for resource {Resource} after {Attempts} attempts", resource, _options.MaxRetryAttempts);
        return null;
    }

    public async Task<bool> ReleaseLockAsync(string resource, string lockId)
    {
        ArgumentNullException.ThrowIfNull(resource);
        ArgumentNullException.ThrowIfNull(lockId);

        if (_locks.TryGetValue(resource, out var lockInfo) && lockInfo.LockId == lockId)
        {
            if (_locks.TryRemove(resource, out _))
            {
                _logger.LogDebug("Released distributed lock for resource {Resource} with ID {LockId}", resource, lockId);
                return true;
            }
        }

        _logger.LogWarning("Failed to release distributed lock for resource {Resource} with ID {LockId}", resource, lockId);
        return await Task.FromResult(false);
    }

    private void CleanupExpiredLocks(object? state)
    {
        var expiredLocks = _locks.Where(kvp => kvp.Value.ExpiresAt <= DateTime.UtcNow).ToList();
        
        foreach (var expiredLock in expiredLocks)
        {
            if (_locks.TryRemove(expiredLock.Key, out _))
            {
                _logger.LogDebug("Cleaned up expired lock for resource {Resource}", expiredLock.Key);
            }
        }
    }

    public async Task<bool> AcquireLockAsync(string resource, TimeSpan expiry)
    {
        var lockResult = await AcquireLockAsync(resource, expiry, CancellationToken.None);
        return lockResult != null;
    }

    public async Task ReleaseLockAsync(string resource)
    {
        if (_locks.TryRemove(resource, out var lockInfo))
        {
            _logger.LogDebug("Released distributed lock for resource {Resource}", resource);
        }
        await Task.CompletedTask;
    }

    public async Task<Dictionary<string, List<string>>> GetLockDependenciesAsync()
    {
        var dependencies = new Dictionary<string, List<string>>();
        
        foreach (var lockKvp in _locks)
        {
            var resource = lockKvp.Key;
            dependencies[resource] = new List<string>();
        }
        
        return await Task.FromResult(dependencies);
    }

    public void Dispose()
    {
        _cleanupTimer?.Dispose();
        GC.SuppressFinalize(this);
    }

    private record LockInfo(string Resource, string LockId, DateTime ExpiresAt);
}

public class DistributedLock : IDistributedLock
{
    private readonly IDistributedLockService _lockService;
    private bool _disposed;

    public string Resource { get; }
    public string LockId { get; }
    public DateTime ExpiresAt { get; private set; }

    public DistributedLock(IDistributedLockService lockService, string resource, string lockId, DateTime expiresAt)
    {
        _lockService = lockService;
        Resource = resource;
        LockId = lockId;
        ExpiresAt = expiresAt;
    }

    public async Task<bool> RenewAsync(TimeSpan expiry)
    {
        if (_disposed)
            return false;

        ExpiresAt = DateTime.UtcNow.Add(expiry);
        return await Task.FromResult(true);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _lockService.ReleaseLockAsync(Resource, LockId).ConfigureAwait(false);
        _disposed = true;
        GC.SuppressFinalize(this);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        await _lockService.ReleaseLockAsync(Resource, LockId);
        _disposed = true;
        GC.SuppressFinalize(this);
    }
}