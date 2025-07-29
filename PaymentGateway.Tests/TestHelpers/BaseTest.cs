// SPDX-License-Identifier: MIT
// Copyright (c) 2025 HackLoad Payment Gateway

using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using PaymentGateway.Core.Interfaces;
using PaymentGateway.Core.Repositories;

namespace PaymentGateway.Tests.TestHelpers;

/// <summary>
/// Base test class providing common test infrastructure and utilities
/// </summary>
public abstract class BaseTest : IDisposable
{
    protected readonly TestDataBuilder TestDataBuilder;
    protected readonly Mock<ILogger> MockLogger;
    protected readonly Mock<IConfiguration> MockConfiguration;
    protected readonly Mock<IMemoryCache> MockMemoryCache;
    protected readonly ServiceCollection Services;
    protected ServiceProvider? ServiceProvider;

    protected BaseTest()
    {
        TestDataBuilder = new TestDataBuilder();
        MockLogger = new Mock<ILogger>();
        MockConfiguration = new Mock<IConfiguration>();
        MockMemoryCache = new Mock<IMemoryCache>();
        Services = new ServiceCollection();
        
        SetupBasicServices();
        SetupConfiguration();
        SetupLogging();
        SetupCaching();
    }

    /// <summary>
    /// Setup basic services for dependency injection
    /// </summary>
    private void SetupBasicServices()
    {
        Services.AddSingleton(MockLogger.Object);
        Services.AddSingleton(MockConfiguration.Object);
        Services.AddSingleton(MockMemoryCache.Object);
    }

    /// <summary>
    /// Setup configuration mock with common values
    /// </summary>
    private void SetupConfiguration()
    {
        var configValues = new Dictionary<string, string>
        {
            ["ConnectionStrings:DefaultConnection"] = "Host=localhost;Database=test;Username=test;Password=test",
            ["Payment:DefaultCurrency"] = "RUB",
            ["Payment:MinAmount"] = "10",
            ["Payment:MaxAmount"] = "1000000",
            ["Payment:ExpirationHours"] = "24",
            ["Authentication:TokenExpirationMinutes"] = "30",
            ["Authentication:MaxFailedAttempts"] = "5",
            ["RateLimit:RequestsPerMinute"] = "100",
            ["RateLimit:BurstSize"] = "10",
            ["Logging:LogLevel:Default"] = "Information",
            ["Metrics:Enabled"] = "true"
        };

        foreach (var kvp in configValues)
        {
            MockConfiguration.Setup(c => c[kvp.Key]).Returns(kvp.Value);
            MockConfiguration.Setup(c => c.GetValue<string>(kvp.Key, It.IsAny<string>()))
                .Returns(kvp.Value);
        }

        // Setup typed configuration values
        MockConfiguration.Setup(c => c.GetValue<int>("Payment:ExpirationHours", It.IsAny<int>()))
            .Returns(24);
        MockConfiguration.Setup(c => c.GetValue<decimal>("Payment:MinAmount", It.IsAny<decimal>()))
            .Returns(10m);
        MockConfiguration.Setup(c => c.GetValue<decimal>("Payment:MaxAmount", It.IsAny<decimal>()))
            .Returns(1000000m);
        MockConfiguration.Setup(c => c.GetValue<int>("Authentication:TokenExpirationMinutes", It.IsAny<int>()))
            .Returns(30);
        MockConfiguration.Setup(c => c.GetValue<int>("Authentication:MaxFailedAttempts", It.IsAny<int>()))
            .Returns(5);
        MockConfiguration.Setup(c => c.GetValue<int>("RateLimit:RequestsPerMinute", It.IsAny<int>()))
            .Returns(100);
        MockConfiguration.Setup(c => c.GetValue<bool>("Metrics:Enabled", It.IsAny<bool>()))
            .Returns(true);
    }

    /// <summary>
    /// Setup logging mock to capture log messages
    /// </summary>
    private void SetupLogging()
    {
        // Setup generic logger mock that can be used for any type
        var loggerFactory = new Mock<ILoggerFactory>();
        loggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>()))
            .Returns(MockLogger.Object);
        
        Services.AddSingleton(loggerFactory.Object);
        Services.AddSingleton(typeof(ILogger<>), typeof(Logger<>));
    }

    /// <summary>
    /// Setup memory cache mock with common behaviors
    /// </summary>
    private void SetupCaching()
    {
        var cacheEntries = new Dictionary<object, object?>();
        
        MockMemoryCache.Setup(mc => mc.TryGetValue(It.IsAny<object>(), out It.Ref<object?>.IsAny))
            .Returns((object key, out object? value) =>
            {
                return cacheEntries.TryGetValue(key, out value);
            });

        MockMemoryCache.Setup(mc => mc.Set(It.IsAny<object>(), It.IsAny<object>(), It.IsAny<MemoryCacheEntryOptions>()))
            .Returns((object key, object value, MemoryCacheEntryOptions options) =>
            {
                cacheEntries[key] = value;
                return Mock.Of<ICacheEntry>();
            });

        MockMemoryCache.Setup(mc => mc.Remove(It.IsAny<object>()))
            .Callback<object>(key => cacheEntries.Remove(key));
    }

    /// <summary>
    /// Add mock repository to services
    /// </summary>
    protected Mock<T> AddMockRepository<T>() where T : class
    {
        var mock = MockFactory.CreateMockWithDefaults<T>();
        Services.AddSingleton(mock.Object);
        return mock;
    }

    /// <summary>
    /// Add mock service to services
    /// </summary>
    protected Mock<T> AddMockService<T>() where T : class
    {
        var mock = MockFactory.CreateMockWithDefaults<T>();
        Services.AddSingleton(mock.Object);
        return mock;
    }

    /// <summary>
    /// Build service provider for dependency injection
    /// </summary>
    protected ServiceProvider BuildServiceProvider()
    {
        ServiceProvider?.Dispose();
        ServiceProvider = Services.BuildServiceProvider();
        return ServiceProvider;
    }

    /// <summary>
    /// Get service from the service provider
    /// </summary>
    protected T GetService<T>() where T : notnull
    {
        var provider = ServiceProvider ?? BuildServiceProvider();
        return provider.GetRequiredService<T>();
    }

    /// <summary>
    /// Verify that a mock was called with specific parameters
    /// </summary>
    protected void VerifyMockCall<T>(Mock<T> mock, Expression<Action<T>> expression, Times times)
        where T : class
    {
        mock.Verify(expression, times);
    }

    /// <summary>
    /// Verify that a mock was called with specific parameters (async version)
    /// </summary>
    protected void VerifyMockCallAsync<T>(Mock<T> mock, Expression<Func<T, Task>> expression, Times times)
        where T : class
    {
        mock.Verify(expression, times);
    }

    /// <summary>
    /// Reset all mocks to their initial state
    /// </summary>
    protected void ResetMocks()
    {
        MockLogger.Reset();
        MockConfiguration.Reset();
        MockMemoryCache.Reset();
        
        SetupConfiguration();
        SetupLogging();
        SetupCaching();
    }

    /// <summary>
    /// Capture log messages for verification
    /// </summary>
    protected List<string> CaptureLogMessages()
    {
        var messages = new List<string>();
        
        MockLogger.Setup(l => l.Log(
            It.IsAny<LogLevel>(),
            It.IsAny<EventId>(),
            It.IsAny<It.IsAnyType>(),
            It.IsAny<Exception>(),
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()))
            .Callback<LogLevel, EventId, object, Exception?, Delegate>((level, id, state, ex, formatter) =>
            {
                messages.Add(formatter.DynamicInvoke(state, ex)?.ToString() ?? "");
            });
        
        return messages;
    }

    /// <summary>
    /// Create a test cancellation token that cancels after a timeout
    /// </summary>
    protected CancellationToken CreateTestCancellationToken(int timeoutMs = 5000)
    {
        return new CancellationTokenSource(timeoutMs).Token;
    }

    /// <summary>
    /// Create multiple test tasks for concurrent testing
    /// </summary>
    protected async Task RunConcurrentTasks(Func<Task> taskFactory, int concurrency = 10)
    {
        var tasks = Enumerable.Range(0, concurrency)
            .Select(_ => taskFactory())
            .ToArray();
        
        await Task.WhenAll(tasks);
    }

    /// <summary>
    /// Assert that an exception of a specific type is thrown
    /// </summary>
    protected async Task<TException> AssertThrowsAsync<TException>(Func<Task> action)
        where TException : Exception
    {
        try
        {
            await action();
            throw new XunitException($"Expected exception of type {typeof(TException).Name} was not thrown.");
        }
        catch (TException ex)
        {
            return ex;
        }
        catch (Exception ex)
        {
            throw new XunitException($"Expected exception of type {typeof(TException).Name}, but got {ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>
    /// Dispose resources
    /// </summary>
    public virtual void Dispose()
    {
        ServiceProvider?.Dispose();
        GC.SuppressFinalize(this);
    }
}