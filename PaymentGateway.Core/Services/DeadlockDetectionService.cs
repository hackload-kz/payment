using System.Collections.Concurrent;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace PaymentGateway.Core.Services;

public interface IDeadlockDetectionService
{
    void RegisterLockRequest(string threadId, string resource, DateTime requestTime);
    void RegisterLockAcquired(string threadId, string resource, DateTime acquiredTime);
    void RegisterLockReleased(string threadId, string resource);
    Task<bool> DetectDeadlockAsync(string threadId, string resource);
    DeadlockReport GetDeadlockReport();
}

public record LockInfo(string ThreadId, string Resource, DateTime RequestTime, DateTime? AcquiredTime, bool IsHeld);

public record DeadlockReport(
    int ActiveLocks,
    int WaitingRequests,
    List<DeadlockChain> DetectedDeadlocks,
    Dictionary<string, TimeSpan> LockWaitTimes);

public record DeadlockChain(List<string> ThreadIds, List<string> Resources, TimeSpan DetectionTime);

public class DeadlockDetectionOptions
{
    public TimeSpan DetectionInterval { get; set; } = TimeSpan.FromSeconds(30);
    public TimeSpan MaxLockWaitTime { get; set; } = TimeSpan.FromMinutes(2);
    public bool EnableAutomaticResolution { get; set; } = true;
    public int MaxDeadlockHistory { get; set; } = 100;
}

public class DeadlockDetectionService : IDeadlockDetectionService
{
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, LockInfo>> _threadLocks;
    private readonly ConcurrentDictionary<string, List<string>> _resourceWaiters;
    private readonly ConcurrentQueue<DeadlockChain> _deadlockHistory;
    private readonly ILogger<DeadlockDetectionService> _logger;
    private readonly DeadlockDetectionOptions _options;

    public DeadlockDetectionService(
        ILogger<DeadlockDetectionService> logger,
        IOptions<DeadlockDetectionOptions> options)
    {
        _threadLocks = new ConcurrentDictionary<string, ConcurrentDictionary<string, LockInfo>>();
        _resourceWaiters = new ConcurrentDictionary<string, List<string>>();
        _deadlockHistory = new ConcurrentQueue<DeadlockChain>();
        _logger = logger;
        _options = options.Value;
    }

    public void RegisterLockRequest(string threadId, string resource, DateTime requestTime)
    {
        ArgumentNullException.ThrowIfNull(threadId);
        ArgumentNullException.ThrowIfNull(resource);

        var threadLocks = _threadLocks.GetOrAdd(threadId, _ => new ConcurrentDictionary<string, LockInfo>());
        var lockInfo = new LockInfo(threadId, resource, requestTime, null, false);
        
        threadLocks.AddOrUpdate(resource, lockInfo, (_, _) => lockInfo);

        _resourceWaiters.AddOrUpdate(resource, 
            new List<string> { threadId },
            (_, waiters) =>
            {
                lock (waiters)
                {
                    if (!waiters.Contains(threadId))
                    {
                        waiters.Add(threadId);
                    }
                }
                return waiters;
            });

        _logger.LogDebug("Registered lock request for thread {ThreadId} on resource {Resource}", threadId, resource);
    }

    public void RegisterLockAcquired(string threadId, string resource, DateTime acquiredTime)
    {
        ArgumentNullException.ThrowIfNull(threadId);
        ArgumentNullException.ThrowIfNull(resource);

        if (_threadLocks.TryGetValue(threadId, out var threadLocks) &&
            threadLocks.TryGetValue(resource, out var existingLock))
        {
            var updatedLock = existingLock with { AcquiredTime = acquiredTime, IsHeld = true };
            threadLocks.TryUpdate(resource, updatedLock, existingLock);
        }

        if (_resourceWaiters.TryGetValue(resource, out var waiters))
        {
            lock (waiters)
            {
                waiters.Remove(threadId);
                if (waiters.Count == 0)
                {
                    _resourceWaiters.TryRemove(resource, out _);
                }
            }
        }

        _logger.LogDebug("Registered lock acquired for thread {ThreadId} on resource {Resource}", threadId, resource);
    }

    public void RegisterLockReleased(string threadId, string resource)
    {
        ArgumentNullException.ThrowIfNull(threadId);
        ArgumentNullException.ThrowIfNull(resource);

        if (_threadLocks.TryGetValue(threadId, out var threadLocks))
        {
            threadLocks.TryRemove(resource, out _);
            
            if (threadLocks.IsEmpty)
            {
                _threadLocks.TryRemove(threadId, out _);
            }
        }

        _logger.LogDebug("Registered lock released for thread {ThreadId} on resource {Resource}", threadId, resource);
    }

    public async Task<bool> DetectDeadlockAsync(string threadId, string resource)
    {
        ArgumentNullException.ThrowIfNull(threadId);
        ArgumentNullException.ThrowIfNull(resource);

        var visitedThreads = new HashSet<string>();
        var threadChain = new List<string>();
        var resourceChain = new List<string>();

        var hasDeadlock = await DetectDeadlockRecursiveAsync(threadId, resource, visitedThreads, threadChain, resourceChain);

        if (hasDeadlock)
        {
            var deadlockChain = new DeadlockChain(new List<string>(threadChain), new List<string>(resourceChain), TimeSpan.Zero);
            
            _deadlockHistory.Enqueue(deadlockChain);
            while (_deadlockHistory.Count > _options.MaxDeadlockHistory)
            {
                _deadlockHistory.TryDequeue(out _);
            }

            _logger.LogWarning("Deadlock detected involving thread {ThreadId} and resource {Resource}. Chain: {ThreadChain} -> {ResourceChain}", 
                threadId, resource, string.Join(" -> ", threadChain), string.Join(" -> ", resourceChain));

            if (_options.EnableAutomaticResolution)
            {
                await ResolveDeadlockAsync(deadlockChain);
            }
        }

        return hasDeadlock;
    }

    private async Task<bool> DetectDeadlockRecursiveAsync(
        string currentThread, 
        string targetResource, 
        HashSet<string> visitedThreads, 
        List<string> threadChain, 
        List<string> resourceChain)
    {
        if (visitedThreads.Contains(currentThread))
        {
            return threadChain.Contains(currentThread);
        }

        visitedThreads.Add(currentThread);
        threadChain.Add(currentThread);

        if (!_threadLocks.TryGetValue(currentThread, out var threadLocks))
        {
            threadChain.RemoveAt(threadChain.Count - 1);
            return false;
        }

        foreach (var lockInfo in threadLocks.Values.Where(l => l.IsHeld))
        {
            resourceChain.Add(lockInfo.Resource);

            if (_resourceWaiters.TryGetValue(lockInfo.Resource, out var waiters))
            {
                foreach (var waiter in waiters.ToList())
                {
                    if (waiter != currentThread)
                    {
                        if (await DetectDeadlockRecursiveAsync(waiter, targetResource, visitedThreads, threadChain, resourceChain))
                        {
                            return true;
                        }
                    }
                }
            }

            resourceChain.RemoveAt(resourceChain.Count - 1);
        }

        threadChain.RemoveAt(threadChain.Count - 1);
        return false;
    }

    private async Task ResolveDeadlockAsync(DeadlockChain deadlockChain)
    {
        var oldestThread = deadlockChain.ThreadIds.FirstOrDefault();
        
        if (oldestThread != null)
        {
            _logger.LogWarning("Attempting to resolve deadlock by releasing locks for thread {ThreadId}", oldestThread);

            if (_threadLocks.TryGetValue(oldestThread, out var threadLocks))
            {
                foreach (var resource in threadLocks.Keys.ToList())
                {
                    RegisterLockReleased(oldestThread, resource);
                }
            }
        }

        await Task.CompletedTask;
    }

    public DeadlockReport GetDeadlockReport()
    {
        var activeLocks = _threadLocks.Values.SelectMany(tl => tl.Values).Count(l => l.IsHeld);
        var waitingRequests = _resourceWaiters.Values.SelectMany(w => w).Count();
        var detectedDeadlocks = _deadlockHistory.ToList();
        
        var lockWaitTimes = new Dictionary<string, TimeSpan>();
        var now = DateTime.UtcNow;
        
        foreach (var threadLocks in _threadLocks.Values)
        {
            foreach (var lockInfo in threadLocks.Values.Where(l => !l.IsHeld))
            {
                var waitTime = now - lockInfo.RequestTime;
                lockWaitTimes[lockInfo.Resource] = waitTime;
            }
        }

        return new DeadlockReport(activeLocks, waitingRequests, detectedDeadlocks, lockWaitTimes);
    }
}

public class DeadlockDetectionBackgroundService : BackgroundService
{
    private readonly IDeadlockDetectionService _deadlockDetectionService;
    private readonly ILogger<DeadlockDetectionBackgroundService> _logger;
    private readonly DeadlockDetectionOptions _options;

    public DeadlockDetectionBackgroundService(
        IDeadlockDetectionService deadlockDetectionService,
        ILogger<DeadlockDetectionBackgroundService> logger,
        IOptions<DeadlockDetectionOptions> options)
    {
        _deadlockDetectionService = deadlockDetectionService;
        _logger = logger;
        _options = options.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Deadlock detection background service started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var report = _deadlockDetectionService.GetDeadlockReport();
                
                if (report.DetectedDeadlocks.Any())
                {
                    _logger.LogWarning("Deadlock detection report: {ActiveLocks} active locks, {WaitingRequests} waiting requests, {DeadlockCount} deadlocks detected",
                        report.ActiveLocks, report.WaitingRequests, report.DetectedDeadlocks.Count);
                }
                else
                {
                    _logger.LogDebug("Deadlock detection report: {ActiveLocks} active locks, {WaitingRequests} waiting requests, no deadlocks detected",
                        report.ActiveLocks, report.WaitingRequests);
                }

                foreach (var waitTime in report.LockWaitTimes.Where(w => w.Value > _options.MaxLockWaitTime))
                {
                    _logger.LogWarning("Long lock wait detected for resource {Resource}: {WaitTime}", 
                        waitTime.Key, waitTime.Value);
                }

                await Task.Delay(_options.DetectionInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in deadlock detection background service");
                await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
            }
        }

        _logger.LogInformation("Deadlock detection background service stopped");
    }
}