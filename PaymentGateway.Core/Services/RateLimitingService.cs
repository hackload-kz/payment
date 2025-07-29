using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace PaymentGateway.Core.Services;

public interface IRateLimitingService
{
    Task<RateLimitResult> CheckRateLimitAsync(string identifier, RateLimitPolicy policy, CancellationToken cancellationToken = default);
    Task ResetRateLimitAsync(string identifier, RateLimitPolicy policy);
    Task<Dictionary<string, RateLimitStatus>> GetRateLimitStatusAsync(RateLimitPolicy policy);
}

public record RateLimitResult(
    bool IsAllowed,
    int RemainingRequests,
    TimeSpan RetryAfter,
    string? RejectReason = null);

public record RateLimitStatus(
    string Identifier,
    int RequestCount,
    DateTime WindowStart,
    DateTime WindowEnd,
    bool IsBlocked);

public class RateLimitPolicy
{
    public string Name { get; init; } = string.Empty;
    public int MaxRequests { get; init; }
    public TimeSpan WindowSize { get; init; }
    public TimeSpan BlockDuration { get; init; } = TimeSpan.Zero;
    public bool EnableBurstProtection { get; init; } = false;
    public int BurstLimit { get; init; } = 0;
    public TimeSpan BurstWindow { get; init; } = TimeSpan.FromMinutes(1);
}

public class RateLimitingOptions
{
    public Dictionary<string, RateLimitPolicy> Policies { get; set; } = new();
    public bool EnableMetrics { get; set; } = true;
    public TimeSpan CleanupInterval { get; set; } = TimeSpan.FromMinutes(5);
}

internal class RateLimitEntry
{
    public int RequestCount { get; set; }
    public DateTime WindowStart { get; set; }
    public DateTime LastRequest { get; set; }
    public bool IsBlocked { get; set; }
    public DateTime? BlockedUntil { get; set; }
    
    // Burst protection
    public Queue<DateTime> BurstRequests { get; set; } = new();
}

public class RateLimitingService : IRateLimitingService, IDisposable
{
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, RateLimitEntry>> _policyEntries;
    private readonly ILogger<RateLimitingService> _logger;
    private readonly RateLimitingOptions _options;
    private readonly Timer _cleanupTimer;

    public static readonly RateLimitPolicy DefaultApiPolicy = new()
    {
        Name = "DefaultAPI",
        MaxRequests = 100,
        WindowSize = TimeSpan.FromMinutes(1),
        BlockDuration = TimeSpan.FromMinutes(5),
        EnableBurstProtection = true,
        BurstLimit = 20,
        BurstWindow = TimeSpan.FromSeconds(10)
    };

    public static readonly RateLimitPolicy PaymentInitPolicy = new()
    {
        Name = "PaymentInit",
        MaxRequests = 50,
        WindowSize = TimeSpan.FromMinutes(1),
        BlockDuration = TimeSpan.FromMinutes(10),
        EnableBurstProtection = true,
        BurstLimit = 10,
        BurstWindow = TimeSpan.FromSeconds(30)
    };

    public static readonly RateLimitPolicy PaymentProcessingPolicy = new()
    {
        Name = "PaymentProcessing",
        MaxRequests = 200,
        WindowSize = TimeSpan.FromMinutes(1),
        BlockDuration = TimeSpan.FromMinutes(2),
        EnableBurstProtection = true,
        BurstLimit = 30,
        BurstWindow = TimeSpan.FromSeconds(15)
    };

    public RateLimitingService(
        ILogger<RateLimitingService> logger,
        IOptions<RateLimitingOptions> options)
    {
        _policyEntries = new ConcurrentDictionary<string, ConcurrentDictionary<string, RateLimitEntry>>();
        _logger = logger;
        _options = options.Value;
        
        _cleanupTimer = new Timer(CleanupExpiredEntries, null, _options.CleanupInterval, _options.CleanupInterval);
    }

    public async Task<RateLimitResult> CheckRateLimitAsync(string identifier, RateLimitPolicy policy, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(identifier);
        ArgumentNullException.ThrowIfNull(policy);

        var now = DateTime.UtcNow;
        var policyEntries = _policyEntries.GetOrAdd(policy.Name, _ => new ConcurrentDictionary<string, RateLimitEntry>());
        
        var entry = policyEntries.AddOrUpdate(identifier,
            _ => new RateLimitEntry
            {
                RequestCount = 1,
                WindowStart = now,
                LastRequest = now,
                BurstRequests = new Queue<DateTime>(new[] { now })
            },
            (_, existing) => UpdateEntry(existing, now, policy));

        // Check if currently blocked
        if (entry.IsBlocked && entry.BlockedUntil.HasValue && now < entry.BlockedUntil.Value)
        {
            var retryAfter = entry.BlockedUntil.Value - now;
            _logger.LogWarning("Rate limit exceeded for {Identifier} on policy {Policy}. Blocked until {BlockedUntil}", 
                identifier, policy.Name, entry.BlockedUntil.Value);
            
            return new RateLimitResult(false, 0, retryAfter, "Rate limit exceeded");
        }

        // Check burst protection
        if (policy.EnableBurstProtection && IsBurstLimitExceeded(entry, policy, now))
        {
            entry.IsBlocked = true;
            entry.BlockedUntil = now.Add(policy.BlockDuration);
            
            _logger.LogWarning("Burst limit exceeded for {Identifier} on policy {Policy}. Requests in burst window: {BurstCount}", 
                identifier, policy.Name, entry.BurstRequests.Count);
            
            return new RateLimitResult(false, 0, policy.BlockDuration, "Burst limit exceeded");
        }

        // Check rate limit
        if (entry.RequestCount > policy.MaxRequests)
        {
            entry.IsBlocked = true;
            entry.BlockedUntil = now.Add(policy.BlockDuration);
            
            _logger.LogWarning("Rate limit exceeded for {Identifier} on policy {Policy}. Requests: {RequestCount}/{MaxRequests}", 
                identifier, policy.Name, entry.RequestCount, policy.MaxRequests);
            
            return new RateLimitResult(false, 0, policy.BlockDuration, "Rate limit exceeded");
        }

        var remainingRequests = Math.Max(0, policy.MaxRequests - entry.RequestCount);
        
        _logger.LogDebug("Rate limit check passed for {Identifier} on policy {Policy}. Remaining: {Remaining}", 
            identifier, policy.Name, remainingRequests);

        return await Task.FromResult(new RateLimitResult(true, remainingRequests, TimeSpan.Zero));
    }

    public async Task ResetRateLimitAsync(string identifier, RateLimitPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(identifier);
        ArgumentNullException.ThrowIfNull(policy);

        if (_policyEntries.TryGetValue(policy.Name, out var policyEntries))
        {
            policyEntries.TryRemove(identifier, out _);
            _logger.LogInformation("Rate limit reset for {Identifier} on policy {Policy}", identifier, policy.Name);
        }

        await Task.CompletedTask;
    }

    public async Task<Dictionary<string, RateLimitStatus>> GetRateLimitStatusAsync(RateLimitPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);

        var result = new Dictionary<string, RateLimitStatus>();
        var now = DateTime.UtcNow;

        if (_policyEntries.TryGetValue(policy.Name, out var policyEntries))
        {
            foreach (var kvp in policyEntries)
            {
                var entry = kvp.Value;
                var windowEnd = entry.WindowStart.Add(policy.WindowSize);
                var isBlocked = entry.IsBlocked && entry.BlockedUntil.HasValue && now < entry.BlockedUntil.Value;

                result[kvp.Key] = new RateLimitStatus(
                    kvp.Key,
                    entry.RequestCount,
                    entry.WindowStart,
                    windowEnd,
                    isBlocked);
            }
        }

        return await Task.FromResult(result);
    }

    private RateLimitEntry UpdateEntry(RateLimitEntry entry, DateTime now, RateLimitPolicy policy)
    {
        // Reset window if expired
        if (now >= entry.WindowStart.Add(policy.WindowSize))
        {
            entry.RequestCount = 1;
            entry.WindowStart = now;
            entry.IsBlocked = false;
            entry.BlockedUntil = null;
            entry.BurstRequests.Clear();
        }
        else
        {
            entry.RequestCount++;
        }

        entry.LastRequest = now;

        // Update burst tracking
        if (policy.EnableBurstProtection)
        {
            entry.BurstRequests.Enqueue(now);
            
            // Remove old burst requests outside the burst window
            while (entry.BurstRequests.Count > 0 && now - entry.BurstRequests.Peek() > policy.BurstWindow)
            {
                entry.BurstRequests.Dequeue();
            }
        }

        // Clear block if expired
        if (entry.IsBlocked && entry.BlockedUntil.HasValue && now >= entry.BlockedUntil.Value)
        {
            entry.IsBlocked = false;
            entry.BlockedUntil = null;
        }

        return entry;
    }

    private bool IsBurstLimitExceeded(RateLimitEntry entry, RateLimitPolicy policy, DateTime now)
    {
        if (!policy.EnableBurstProtection)
            return false;

        // Clean up old burst requests
        while (entry.BurstRequests.Count > 0 && now - entry.BurstRequests.Peek() > policy.BurstWindow)
        {
            entry.BurstRequests.Dequeue();
        }

        return entry.BurstRequests.Count > policy.BurstLimit;
    }

    private void CleanupExpiredEntries(object? state)
    {
        var now = DateTime.UtcNow;
        var cleanedCount = 0;

        try
        {
            foreach (var policyKvp in _policyEntries)
            {
                var policyName = policyKvp.Key;
                var entries = policyKvp.Value;
                
                var policy = _options.Policies.GetValueOrDefault(policyName);
                if (policy == null)
                    continue;

                var expiredKeys = new List<string>();

                foreach (var entryKvp in entries)
                {
                    var entry = entryKvp.Value;
                    
                    // Remove entries that are old and not blocked
                    if (!entry.IsBlocked && 
                        now - entry.LastRequest > policy.WindowSize.Add(TimeSpan.FromMinutes(5)))
                    {
                        expiredKeys.Add(entryKvp.Key);
                    }
                    
                    // Remove entries where block has expired
                    if (entry.IsBlocked && 
                        entry.BlockedUntil.HasValue && 
                        now > entry.BlockedUntil.Value.Add(TimeSpan.FromMinutes(1)))
                    {
                        expiredKeys.Add(entryKvp.Key);
                    }
                }

                foreach (var key in expiredKeys)
                {
                    if (entries.TryRemove(key, out _))
                    {
                        cleanedCount++;
                    }
                }
            }

            if (cleanedCount > 0)
            {
                _logger.LogDebug("Cleaned up {Count} expired rate limit entries", cleanedCount);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during rate limit cleanup");
        }
    }

    public void Dispose()
    {
        _cleanupTimer?.Dispose();
        GC.SuppressFinalize(this);
    }
}