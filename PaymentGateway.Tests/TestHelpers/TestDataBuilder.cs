// SPDX-License-Identifier: MIT
// Copyright (c) 2025 HackLoad Payment Gateway

using AutoFixture;
using Moq;
using PaymentGateway.Core.DTOs;
using PaymentGateway.Core.DTOs.PaymentInit;
using PaymentGateway.Core.Entities;
using PaymentGateway.Core.Enums;
using PaymentGateway.Core.Interfaces;
using PaymentGateway.Core.Repositories;

namespace PaymentGateway.Tests.TestHelpers;

/// <summary>
/// Comprehensive test data builder for creating consistent test data
/// </summary>
public class TestDataBuilder
{
    public readonly IFixture _fixture;

    public TestDataBuilder()
    {
        _fixture = new Fixture();
        ConfigureFixture();
    }

    private void ConfigureFixture()
    {
        // Configure fixture to create valid test data
        _fixture.Behaviors.OfType<ThrowingRecursionBehavior>().ToList()
            .ForEach(b => _fixture.Behaviors.Remove(b));
        _fixture.Behaviors.Add(new OmitOnRecursionBehavior());

        // Configure string generators
        _fixture.Customize<string>(composer => composer.FromFactory(() => 
            _fixture.Create<string>().Substring(0, Math.Min(50, _fixture.Create<string>().Length))));
    }

    /// <summary>
    /// Create a valid Payment entity
    /// </summary>
    public Payment CreatePayment(
        PaymentStatus? status = null,
        int? teamId = null,
        decimal? amount = null,
        string? currency = null,
        string? paymentId = null,
        string? orderId = null)
    {
        var payment = new Payment
        {
            Id = Guid.NewGuid(),
            PaymentId = paymentId ?? $"PAY_{Guid.NewGuid():N}",
            OrderId = orderId ?? $"ORDER_{DateTime.UtcNow:yyyyMMdd}_{_fixture.Create<int>()}",
            TeamId = teamId ?? _fixture.Create<int>(),
            Amount = amount ?? _fixture.CreateDecimalBetween(10m, 100000m),
            Currency = currency ?? "RUB",
            Status = status ?? PaymentStatus.NEW,
            Description = _fixture.Create<string>(),
            CustomerEmail = _fixture.CreateValidEmail(),
            CustomerPhone = _fixture.CreateValidPhone(),
            SuccessUrl = _fixture.CreateValidUrl(),
            FailUrl = _fixture.CreateValidUrl(),
            NotificationUrl = _fixture.CreateValidUrl(),
            ErrorCode = null, // New property - typically null for successful payments
            ErrorMessage = null, // New property - typically null for successful payments  
            Receipt = null, // New property - typically null until payment is completed
            CreatedAt = DateTime.UtcNow.AddMinutes(-_fixture.Create<int>() % 60),
            UpdatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddHours(24),
            IsDeleted = false
        };
        
        return payment;
    }

    /// <summary>
    /// Create a valid Team entity
    /// </summary>
    public Team CreateTeam(
        string? teamSlug = null,
        string? name = null,
        bool? isActive = null)
    {
        return new Team
        {
            Id = Guid.NewGuid(),
            TeamSlug = teamSlug ?? $"team_{_fixture.Create<string>().ToLower().Substring(0, 8)}",
            Name = name ?? _fixture.Create<string>(),
            IsActive = isActive ?? true,
            Password = _fixture.Create<string>(),
            ApiKey = Convert.ToBase64String(_fixture.CreateMany<byte>(32).ToArray()),
            CallbackUrl = _fixture.CreateValidUrl(),
            DailyLimit = _fixture.CreateDecimalBetween(10000m, 1000000m),
            TransactionLimit = _fixture.CreateDecimalBetween(1000m, 100000m),
            SupportedCurrencies = "RUB,USD,EUR",
            CreatedAt = DateTime.UtcNow.AddDays(-_fixture.Create<int>() % 30),
            UpdatedAt = DateTime.UtcNow,
            IsDeleted = false
        };
    }

    /// <summary>
    /// Create a valid Customer entity
    /// </summary>
    public Customer CreateCustomer(
        Guid? teamId = null,
        string? email = null,
        string? customerId = null)
    {
        return new Customer
        {
            Id = Guid.NewGuid(),
            CustomerId = customerId ?? $"CUST_{Guid.NewGuid():N}",
            TeamId = teamId ?? Guid.NewGuid(),
            Email = email ?? _fixture.CreateValidEmail(),
            Phone = _fixture.CreateValidPhone(),
            Name = _fixture.Create<string>(),
            IsActive = true,
            RiskScore = _fixture.Create<decimal>() % 100,
            CreatedAt = DateTime.UtcNow.AddDays(-_fixture.Create<int>() % 30),
            UpdatedAt = DateTime.UtcNow,
            IsDeleted = false
        };
    }

    /// <summary>
    /// Create a valid Transaction entity
    /// </summary>
    public Transaction CreateTransaction(
        Guid? paymentId = null,
        TransactionType? type = null,
        TransactionStatus? status = null)
    {
        return new Transaction
        {
            Id = Guid.NewGuid(),
            TransactionId = $"TXN_{Guid.NewGuid():N}",
            PaymentId = paymentId ?? Guid.NewGuid(),
            Type = type ?? TransactionType.AUTHORIZATION,
            Status = status ?? TransactionStatus.PENDING,
            Amount = _fixture.CreateDecimalBetween(10m, 100000m),
            Currency = "RUB",
            ProcessingCode = _fixture.Create<string>().Substring(0, 10),
            BankResponseCode = "00",
            BankResponseMessage = "Success",
            CreatedAt = DateTime.UtcNow,
            ProcessedAt = DateTime.UtcNow,
            IsDeleted = false
        };
    }

    /// <summary>
    /// Create a valid PaymentInitRequestDto
    /// </summary>
    public PaymentInitRequestDto CreatePaymentInitRequest(
        string? teamSlug = null,
        decimal? amount = null,
        string? orderId = null,
        string? currency = null)
    {
        return new PaymentInitRequestDto
        {
            TeamSlug = teamSlug ?? $"team_{_fixture.Create<string>().ToLower().Substring(0, 8)}",
            Amount = amount ?? _fixture.CreateDecimalBetween(10m, 100000m),
            OrderId = orderId ?? $"ORDER_{DateTime.UtcNow:yyyyMMdd}_{_fixture.Create<int>()}",
            Currency = currency ?? "RUB",
            Description = _fixture.Create<string>(),
            CustomerEmail = _fixture.CreateValidEmail(),
            CustomerPhone = _fixture.CreateValidPhone(),
            SuccessUrl = _fixture.CreateValidUrl(),
            FailUrl = _fixture.CreateValidUrl(),
            NotificationUrl = _fixture.CreateValidUrl(),
            Language = "ru",
            Data = new Dictionary<string, object>
            {
                ["customField1"] = _fixture.Create<string>(),
                ["customField2"] = _fixture.Create<int>()
            }
        };
    }

    /// <summary>
    /// Create multiple entities of the same type
    /// </summary>
    public IEnumerable<T> CreateMany<T>(int count = 3, Func<T>? factory = null)
    {
        factory ??= () => _fixture.Create<T>();
        return Enumerable.Range(0, count).Select(_ => factory());
    }
}

/// <summary>
/// Extension methods for AutoFixture to create domain-specific valid data
/// </summary>
public static class FixtureExtensions
{
    public static decimal CreateDecimalBetween(this IFixture fixture, decimal min, decimal max)
    {
        var random = new Random();
        return min + (decimal)random.NextDouble() * (max - min);
    }

    public static string CreateValidEmail(this IFixture fixture)
    {
        var username = fixture.Create<string>().Substring(0, Math.Min(10, fixture.Create<string>().Length));
        var domain = fixture.Create<string>().Substring(0, Math.Min(10, fixture.Create<string>().Length));
        return $"{username}@{domain}.com".ToLower();
    }

    public static string CreateValidPhone(this IFixture fixture)
    {
        var random = new Random();
        return $"+7{random.Next(900, 999)}{random.Next(1000000, 9999999)}";
    }

    public static string CreateValidUrl(this IFixture fixture)
    {
        var domain = fixture.Create<string>().Substring(0, Math.Min(10, fixture.Create<string>().Length));
        return $"https://{domain}.com/callback".ToLower();
    }

    public static string CreateValidPaymentId(this IFixture fixture)
    {
        return $"PAY_{Guid.NewGuid():N}";
    }

    public static string CreateValidOrderId(this IFixture fixture)
    {
        var random = new Random();
        return $"ORDER_{DateTime.UtcNow:yyyyMMdd}_{random.Next(1000, 9999)}";
    }

    public static string CreateValidCardNumber(this IFixture fixture)
    {
        // Generate valid test card numbers
        var testCards = new[]
        {
            "4111111111111111", // Visa
            "5555555555554444", // MasterCard
            "4000000000000002", // Visa (declined)
            "4000000000000341", // Visa (expired)
            "4242424242424242"  // Visa (test)
        };
        
        var random = new Random();
        return testCards[random.Next(testCards.Length)];
    }

    public static string CreateValidCvv(this IFixture fixture)
    {
        var random = new Random();
        return random.Next(100, 999).ToString();
    }

    public static string CreateValidExpiryDate(this IFixture fixture)
    {
        var random = new Random();
        var month = random.Next(1, 12).ToString("D2");
        var year = (DateTime.Now.Year + random.Next(1, 5)).ToString().Substring(2);
        return $"{month}/{year}";
    }
}

/// <summary>
/// Factory for creating configured mock objects
/// </summary>
public static class MockFactory
{
    public static Mock<T> CreateMock<T>() where T : class
    {
        return new Mock<T>();
    }

    public static Mock<T> CreateMockWithDefaults<T>() where T : class
    {
        var mock = new Mock<T>();
        
        // Configure common default behaviors
        if (typeof(T).Name.Contains("Repository"))
        {
            // Configure repository mocks with common patterns
            ConfigureRepositoryMock(mock);
        }
        
        if (typeof(T).Name.Contains("Service"))
        {
            // Configure service mocks with common patterns
            ConfigureServiceMock(mock);
        }
        
        return mock;
    }

    private static void ConfigureRepositoryMock<T>(Mock<T> mock) where T : class
    {
        // Common repository method configurations can be added here
        // This will be extended as specific repository tests are created
    }

    private static void ConfigureServiceMock<T>(Mock<T> mock) where T : class
    {
        // Common service method configurations can be added here
        // This will be extended as specific service tests are created
    }
}