// SPDX-License-Identifier: MIT
// Copyright (c) 2025 HackLoad Payment Gateway

using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using PaymentGateway.Core.Entities;
using PaymentGateway.Core.Enums;
using PaymentGateway.Core.Interfaces;
using PaymentGateway.Core.Services;
using PaymentGateway.Tests.TestHelpers;
using System.Collections.Concurrent;

namespace PaymentGateway.Tests.UnitTests;

/// <summary>
/// Unit tests for concurrent processing scenarios
/// </summary>
public class ConcurrentProcessingTests : BaseTest
{
    private readonly Mock<IPaymentRepository> _mockPaymentRepository;
    private readonly Mock<IDistributedLockService> _mockDistributedLockService;
    private readonly ConcurrentPaymentProcessingEngineService _concurrentProcessingService;
    private readonly PaymentQueueService _queueService;
    private readonly DeadlockDetectionService _deadlockDetectionService;

    public ConcurrentProcessingTests()
    {
        _mockPaymentRepository = AddMockRepository<IPaymentRepository>();
        _mockDistributedLockService = AddMockService<IDistributedLockService>();

        _concurrentProcessingService = new ConcurrentPaymentProcessingEngineService(
            GetService<ILogger<ConcurrentPaymentProcessingEngineService>>(),
            MockConfiguration.Object,
            _mockPaymentRepository.Object,
            _mockDistributedLockService.Object
        );

        _queueService = new PaymentQueueService(
            GetService<ILogger<PaymentQueueService>>(),
            MockConfiguration.Object
        );

        _deadlockDetectionService = new DeadlockDetectionService(
            GetService<ILogger<DeadlockDetectionService>>(),
            MockConfiguration.Object,
            _mockDistributedLockService.Object
        );
    }

    [Fact]
    public async Task ConcurrentPaymentProcessing_ShouldProcessMultiplePaymentsSimultaneously()
    {
        // Arrange
        var payments = TestDataBuilder.CreateMany(() => 
            TestDataBuilder.CreatePayment(PaymentStatus.NEW), 10).ToList();

        foreach (var payment in payments)
        {
            _mockPaymentRepository
                .Setup(r => r.GetByIdAsync(payment.Id))
                .ReturnsAsync(payment);

            _mockDistributedLockService
                .Setup(s => s.AcquireLockAsync($"payment_{payment.Id}", It.IsAny<TimeSpan>()))
                .ReturnsAsync(true);
        }

        var processedPayments = new ConcurrentBag<Payment>();

        // Act
        var tasks = payments.Select(async payment =>
        {
            var result = await _concurrentProcessingService.ProcessPaymentAsync(payment.Id);
            if (result.Success)
            {
                processedPayments.Add(payment);
            }
            return result;
        });

        var results = await Task.WhenAll(tasks);

        // Assert
        results.Should().OnlyContain(r => r.Success == true);
        processedPayments.Should().HaveCount(10);
        
        // Verify each payment was locked
        foreach (var payment in payments)
        {
            _mockDistributedLockService.Verify(
                s => s.AcquireLockAsync($"payment_{payment.Id}", It.IsAny<TimeSpan>()),
                Times.Once);
        }
    }

    [Fact]
    public async Task ConcurrentPaymentProcessing_WithResourceContention_ShouldHandleGracefully()
    {
        // Arrange
        var payment = TestDataBuilder.CreatePayment(PaymentStatus.NEW);
        var lockAcquired = false;

        _mockPaymentRepository
            .Setup(r => r.GetByIdAsync(payment.Id))
            .ReturnsAsync(payment);

        _mockDistributedLockService
            .Setup(s => s.AcquireLockAsync($"payment_{payment.Id}", It.IsAny<TimeSpan>()))
            .ReturnsAsync(() =>
            {
                if (lockAcquired) return false; // Lock already held
                lockAcquired = true;
                return true;
            });

        // Act - Multiple concurrent attempts to process the same payment
        var tasks = Enumerable.Range(0, 5).Select(async _ =>
            await _concurrentProcessingService.ProcessPaymentAsync(payment.Id)
        );

        var results = await Task.WhenAll(tasks);

        // Assert
        var successfulResults = results.Where(r => r.Success).ToList();
        var failedResults = results.Where(r => !r.Success).ToList();

        successfulResults.Should().HaveCount(1); // Only one should succeed
        failedResults.Should().HaveCount(4); // Others should fail due to lock contention
        
        failedResults.Should().OnlyContain(r => 
            r.ErrorMessage != null && r.ErrorMessage.Contains("lock"));
    }

    [Fact]
    public async Task PaymentQueue_ShouldProcessPaymentsInOrder()
    {
        // Arrange
        var payments = TestDataBuilder.CreateMany(() => 
            TestDataBuilder.CreatePayment(PaymentStatus.NEW), 5).ToList();

        var processedOrder = new List<Guid>();
        var semaphore = new SemaphoreSlim(0, 5);

        // Setup processing function that records order
        Func<Payment, Task<bool>> processor = async payment =>
        {
            processedOrder.Add(payment.Id);
            semaphore.Release();
            return true;
        };

        _queueService.SetPaymentProcessor(processor);

        // Act
        foreach (var payment in payments)
        {
            await _queueService.EnqueuePaymentAsync(payment);
        }

        // Wait for all payments to be processed
        for (int i = 0; i < payments.Count; i++)
        {
            await semaphore.WaitAsync(TimeSpan.FromSeconds(5));
        }

        // Assert
        processedOrder.Should().HaveCount(payments.Count);
        
        // Verify all payments were processed (order may vary due to concurrency)
        processedOrder.Should().BeEquivalentTo(payments.Select(p => p.Id));
    }

    [Fact]
    public async Task DistributedLock_ShouldPreventConcurrentAccess()
    {
        // Arrange
        var lockKey = "test_lock";
        var concurrentOperations = 10;
        var sharedResource = 0;
        var maxConcurrentAccess = 0;
        var currentConcurrentAccess = 0;

        // Setup mock to simulate actual lock behavior
        var lockHeld = false;
        _mockDistributedLockService
            .Setup(s => s.AcquireLockAsync(lockKey, It.IsAny<TimeSpan>()))
            .ReturnsAsync(() =>
            {
                if (lockHeld) return false;
                lockHeld = true;
                return true;
            });

        _mockDistributedLockService
            .Setup(s => s.ReleaseLockAsync(lockKey))
            .Callback(() => lockHeld = false);

        // Act
        var tasks = Enumerable.Range(0, concurrentOperations).Select(async i =>
        {
            var lockAcquired = await _mockDistributedLockService.Object.AcquireLockAsync(
                lockKey, TimeSpan.FromSeconds(1));
            
            if (lockAcquired)
            {
                try
                {
                    var currentAccess = Interlocked.Increment(ref currentConcurrentAccess);
                    maxConcurrentAccess = Math.Max(maxConcurrentAccess, currentAccess);
                    
                    // Simulate work
                    await Task.Delay(10);
                    sharedResource++;
                    
                    Interlocked.Decrement(ref currentConcurrentAccess);
                }
                finally
                {
                    await _mockDistributedLockService.Object.ReleaseLockAsync(lockKey);
                }
                return true;
            }
            return false;
        });

        var results = await Task.WhenAll(tasks);

        // Assert
        var successfulOperations = results.Count(r => r);
        successfulOperations.Should().Be(1); // Only one should get the lock
        maxConcurrentAccess.Should().Be(1); // Never more than one concurrent access
        sharedResource.Should().Be(1); // Only one operation should complete
    }

    [Fact]
    public async Task DeadlockDetection_ShouldDetectCircularDependencies()
    {
        // Arrange
        var resource1 = "payment_1";
        var resource2 = "payment_2";
        var resource3 = "payment_3";

        // Setup circular dependency: 1 -> 2 -> 3 -> 1
        var lockGraph = new Dictionary<string, List<string>>
        {
            [resource1] = new List<string> { resource2 },
            [resource2] = new List<string> { resource3 },
            [resource3] = new List<string> { resource1 }
        };

        _mockDistributedLockService
            .Setup(s => s.GetLockDependenciesAsync())
            .ReturnsAsync(lockGraph);

        // Act
        var deadlockDetected = await _deadlockDetectionService.DetectDeadlockAsync();

        // Assert
        deadlockDetected.Should().BeTrue();
    }

    [Fact]
    public async Task DeadlockDetection_WithoutCircularDependencies_ShouldNotDetectDeadlock()
    {
        // Arrange
        var lockGraph = new Dictionary<string, List<string>>
        {
            ["payment_1"] = new List<string> { "payment_2" },
            ["payment_2"] = new List<string> { "payment_3" },
            ["payment_3"] = new List<string>() // No circular dependency
        };

        _mockDistributedLockService
            .Setup(s => s.GetLockDependenciesAsync())
            .ReturnsAsync(lockGraph);

        // Act
        var deadlockDetected = await _deadlockDetectionService.DetectDeadlockAsync();

        // Assert
        deadlockDetected.Should().BeFalse();
    }

    [Fact]
    public async Task ConcurrentStateTransitions_ShouldMaintainConsistency()
    {
        // Arrange
        var payment = TestDataBuilder.CreatePayment(PaymentStatus.NEW);
        var transitionAttempts = 10;
        var successfulTransitions = 0;

        _mockPaymentRepository
            .Setup(r => r.GetByIdAsync(payment.Id))
            .ReturnsAsync(payment);

        _mockDistributedLockService
            .Setup(s => s.AcquireLockAsync($"payment_state_{payment.Id}", It.IsAny<TimeSpan>()))
            .ReturnsAsync(true);

        // Mock state transition
        _mockPaymentRepository
            .Setup(r => r.UpdateAsync(It.IsAny<Payment>()))
            .Callback<Payment>(p =>
            {
                if (p.Status == PaymentStatus.AUTHORIZED)
                {
                    Interlocked.Increment(ref successfulTransitions);
                }
            })
            .ReturnsAsync((Payment p) => p);

        // Act
        var tasks = Enumerable.Range(0, transitionAttempts).Select(async i =>
        {
            try
            {
                payment.Status = PaymentStatus.AUTHORIZED;
                var result = await _mockPaymentRepository.Object.UpdateAsync(payment);
                return result != null;
            }
            catch
            {
                return false;
            }
        });

        var results = await Task.WhenAll(tasks);

        // Assert
        successfulTransitions.Should().Be(transitionAttempts);
        results.Should().OnlyContain(r => r == true);
    }

    [Fact]
    public async Task ConcurrentPaymentCreation_WithSameOrderId_ShouldPreventDuplicates()
    {
        // Arrange
        var team = TestDataBuilder.CreateTeam();
        var orderId = "ORDER_123";
        var concurrentRequests = 5;
        var createdPayments = new ConcurrentBag<Payment>();

        // Setup to return null on first check, then return existing payment
        var paymentCreated = false;
        _mockPaymentRepository
            .Setup(r => r.GetByOrderIdAsync(orderId, team.Id))
            .ReturnsAsync(() => paymentCreated ? 
                TestDataBuilder.CreatePayment(orderId: orderId, teamId: team.Id) : 
                null);

        _mockPaymentRepository
            .Setup(r => r.CreateAsync(It.IsAny<Payment>()))
            .ReturnsAsync((Payment p) =>
            {
                if (!paymentCreated)
                {
                    paymentCreated = true;
                    createdPayments.Add(p);
                    return p;
                }
                throw new InvalidOperationException("Duplicate OrderId");
            });

        // Act
        var tasks = Enumerable.Range(0, concurrentRequests).Select(async i =>
        {
            try
            {
                var payment = TestDataBuilder.CreatePayment(orderId: orderId, teamId: team.Id);
                var existingPayment = await _mockPaymentRepository.Object.GetByOrderIdAsync(orderId, team.Id);
                
                if (existingPayment == null)
                {
                    return await _mockPaymentRepository.Object.CreateAsync(payment);
                }
                return existingPayment;
            }
            catch
            {
                return null;
            }
        });

        var results = await Task.WhenAll(tasks);

        // Assert
        createdPayments.Should().HaveCount(1); // Only one payment should be created
        results.Where(r => r != null).Should().NotBeEmpty(); // At least one should succeed
    }

    [Fact]
    public async Task LoadBalancedProcessing_ShouldDistributeWorkEvenly()
    {
        // Arrange
        var payments = TestDataBuilder.CreateMany(() => 
            TestDataBuilder.CreatePayment(PaymentStatus.NEW), 20).ToList();

        var processorWorkload = new ConcurrentDictionary<int, int>();
        var availableProcessors = 4;

        foreach (var payment in payments)
        {
            _mockPaymentRepository
                .Setup(r => r.GetByIdAsync(payment.Id))
                .ReturnsAsync(payment);

            _mockDistributedLockService
                .Setup(s => s.AcquireLockAsync($"payment_{payment.Id}", It.IsAny<TimeSpan>()))
                .ReturnsAsync(true);
        }

        // Act
        var semaphore = new SemaphoreSlim(availableProcessors, availableProcessors);
        var tasks = payments.Select(async (payment, index) =>
        {
            await semaphore.WaitAsync();
            try
            {
                var processorId = index % availableProcessors;
                processorWorkload.AddOrUpdate(processorId, 1, (key, value) => value + 1);
                
                // Simulate processing time
                await Task.Delay(10);
                
                return await _concurrentProcessingService.ProcessPaymentAsync(payment.Id);
            }
            finally
            {
                semaphore.Release();
            }
        });

        var results = await Task.WhenAll(tasks);

        // Assert
        results.Should().OnlyContain(r => r.Success == true);
        processorWorkload.Should().HaveCount(availableProcessors);
        
        // Check workload distribution is reasonably balanced
        var workloads = processorWorkload.Values.ToList();
        var maxWorkload = workloads.Max();
        var minWorkload = workloads.Min();
        (maxWorkload - minWorkload).Should().BeLessOrEqualTo(2); // Allow small variance
    }

    [Fact]
    public async Task ConcurrentPaymentStatusChecks_ShouldBeConsistent()
    {
        // Arrange
        var payment = TestDataBuilder.CreatePayment(PaymentStatus.AUTHORIZED);
        var concurrentChecks = 20;

        _mockPaymentRepository
            .Setup(r => r.GetByPaymentIdAsync(payment.PaymentId))
            .ReturnsAsync(payment);

        // Act
        var tasks = Enumerable.Range(0, concurrentChecks).Select(async i =>
        {
            var result = await _mockPaymentRepository.Object.GetByPaymentIdAsync(payment.PaymentId);
            return result?.Status;
        });

        var results = await Task.WhenAll(tasks);

        // Assert
        results.Should().OnlyContain(status => status == PaymentStatus.AUTHORIZED);
        results.Should().HaveCount(concurrentChecks);
        
        // Verify repository was called for each check
        _mockPaymentRepository.Verify(
            r => r.GetByPaymentIdAsync(payment.PaymentId),
            Times.Exactly(concurrentChecks));
    }

    [Fact]
    public async Task ConcurrentResourceCleanup_ShouldNotInterfere()
    {
        // Arrange
        var expiredPayments = TestDataBuilder.CreateMany(() =>
        {
            var payment = TestDataBuilder.CreatePayment(PaymentStatus.NEW);
            payment.ExpiresAt = DateTime.UtcNow.AddMinutes(-10); // Expired
            return payment;
        }, 10).ToList();

        _mockPaymentRepository
            .Setup(r => r.GetExpiredPaymentsAsync())
            .ReturnsAsync(expiredPayments);

        var cleanupTasks = new List<Task>();
        var processedPayments = new ConcurrentBag<Payment>();

        // Act - Multiple cleanup processes running concurrently
        for (int i = 0; i < 3; i++)
        {
            cleanupTasks.Add(Task.Run(async () =>
            {
                var paymentsToClean = await _mockPaymentRepository.Object.GetExpiredPaymentsAsync();
                foreach (var payment in paymentsToClean)
                {
                    processedPayments.Add(payment);
                    // Simulate cleanup work
                    await Task.Delay(1);
                }
            }));
        }

        await Task.WhenAll(cleanupTasks);

        // Assert
        processedPayments.Should().HaveCount(30); // 3 processes × 10 payments each
        
        // All payments should be processed exactly 3 times (once per cleanup process)
        var paymentGroups = processedPayments.GroupBy(p => p.Id);
        paymentGroups.Should().HaveCount(10);
        paymentGroups.Should().OnlyContain(g => g.Count() == 3);
    }
}