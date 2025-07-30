// SPDX-License-Identifier: MIT
// Copyright (c) 2025 HackLoad Payment Gateway

using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Caching.Memory;
using PaymentGateway.Core.Entities;
using PaymentGateway.Tests.TestHelpers;

namespace PaymentGateway.Tests.UnitTests;

/// <summary>
/// Tests to validate the unit testing framework is working correctly
/// </summary>
public class TestFrameworkValidationTests : BaseTest
{
    [Fact]
    public void TestDataBuilder_ShouldCreateValidData()
    {
        // Act
        var payment = TestDataBuilder.CreatePayment();

        // Assert
        payment.Should().NotBeNull();
        payment.PaymentId.Should().NotBeNullOrEmpty();
        payment.OrderId.Should().NotBeNullOrEmpty();
        payment.Amount.Should().BeGreaterThan(0);
        payment.Currency.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void TestDataBuilder_ShouldCreateMultipleUniqueItems()
    {
        // Act
        var payments = TestDataBuilder.CreateMany<Payment>(() => TestDataBuilder.CreatePayment(), 5).ToList();

        // Assert
        payments.Should().HaveCount(5);
        payments.Select(p => p.PaymentId).Should().OnlyHaveUniqueItems();
        payments.Select(p => p.OrderId).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void BaseTest_ShouldProvideMockConfiguration()
    {
        // Act
        var connectionString = MockConfiguration.Object["ConnectionStrings:DefaultConnection"];
        var minAmount = MockConfiguration.Object["Payment:MinAmount"] != null ? 
            decimal.Parse(MockConfiguration.Object["Payment:MinAmount"]!) : 0m;

        // Assert
        connectionString.Should().NotBeNullOrEmpty();
        minAmount.Should().Be(10m);
    }

    [Fact]
    public void BaseTest_ShouldProvideMemoryCache()
    {
        // Arrange
        var key = "test_key";
        var value = "test_value";

        // Act
        using var entry = MockMemoryCache.Object.CreateEntry(key);
        entry.Value = value;
        entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5);
        var retrievedValue = MockMemoryCache.Object.TryGetValue(key, out var result) ? result : null;

        // Assert
        retrievedValue.Should().Be(value);
    }

    [Theory]
    [InlineData("test@example.com")]
    [InlineData("user@domain.org")]
    [InlineData("admin@company.net")]
    public void TestDataBuilder_CreateValidEmail_ShouldCreateValidEmails(string expectedPattern)
    {
        // Act
        var email = TestDataBuilder._fixture.CreateValidEmail();

        // Assert
        email.Should().NotBeNullOrEmpty();
        email.Should().Contain("@");
        email.Should().Contain(".");
        email.Should().EndWith(".com");
    }

    [Fact]
    public void TestDataBuilder_CreateValidCardNumber_ShouldCreateValidTestCard()
    {
        // Act
        var cardNumber = TestDataBuilder._fixture.CreateValidCardNumber();

        // Assert
        cardNumber.Should().NotBeNullOrEmpty();
        cardNumber.Should().HaveLength(16);
        cardNumber.Should().MatchRegex(@"^\d{16}$");
    }

    [Fact]
    public void TestDataBuilder_CreateValidCvv_ShouldCreateValidCvv()
    {
        // Act
        var cvv = TestDataBuilder._fixture.CreateValidCvv();

        // Assert
        cvv.Should().NotBeNullOrEmpty();
        cvv.Should().HaveLength(3);
        cvv.Should().MatchRegex(@"^\d{3}$");
    }

    [Fact]
    public void TestDataBuilder_CreateValidExpiryDate_ShouldCreateFutureDate()
    {
        // Act
        var expiryDate = TestDataBuilder._fixture.CreateValidExpiryDate();

        // Assert
        expiryDate.Should().NotBeNullOrEmpty();
        expiryDate.Should().MatchRegex(@"^\d{2}/\d{2}$");
        
        // Verify it's a future date
        var parts = expiryDate.Split('/');
        var month = int.Parse(parts[0]);
        var year = int.Parse("20" + parts[1]);
        
        month.Should().BeInRange(1, 12);
        year.Should().BeGreaterThan(DateTime.Now.Year);
    }

    [Fact]
    public async Task AssertThrowsAsync_ShouldCatchExpectedExceptions()
    {
        // Act & Assert
        var exception = await AssertThrowsAsync<InvalidOperationException>(async () =>
        {
            await Task.Delay(1);
            throw new InvalidOperationException("Test exception");
        });

        exception.Should().NotBeNull();
        exception.Message.Should().Be("Test exception");
    }

    [Fact]
    public async Task RunConcurrentTasks_ShouldExecuteTasksConcurrently()
    {
        // Arrange
        var executionCount = 0;

        // Act
        await RunConcurrentTasks(async () =>
        {
            await Task.Delay(10);
            Interlocked.Increment(ref executionCount);
        }, 5);

        // Assert
        executionCount.Should().Be(5);
    }

    [Fact]
    public void CreateTestCancellationToken_ShouldCreateTokenWithTimeout()
    {
        // Act
        var token = CreateTestCancellationToken(100);

        // Assert
        token.Should().NotBe(CancellationToken.None);
        
        // Wait for timeout
        Thread.Sleep(150);
        token.IsCancellationRequested.Should().BeTrue();
    }

    [Fact]
    public void MockFactory_ShouldCreateMocks()
    {
        // Act
        var mockService = MockFactory.CreateMock<IDisposable>();
        var mockServiceWithDefaults = MockFactory.CreateMockWithDefaults<IDisposable>();

        // Assert
        mockService.Should().NotBeNull();
        mockServiceWithDefaults.Should().NotBeNull();
    }

    [Theory]
    [InlineData(100, 1000)]
    [InlineData(1, 100)]
    [InlineData(0.1, 10.5)]
    public void FixtureExtensions_CreateDecimalBetween_ShouldCreateValueInRange(decimal min, decimal max)
    {
        // Act
        var value = TestDataBuilder._fixture.CreateDecimalBetween(min, max);

        // Assert
        value.Should().BeInRange(min, max);
    }

    [Fact]
    public void FixtureExtensions_CreateValidPhone_ShouldCreateValidPhoneNumber()
    {
        // Act
        var phone = TestDataBuilder._fixture.CreateValidPhone();

        // Assert
        phone.Should().NotBeNullOrEmpty();
        phone.Should().StartWith("+7");
        phone.Should().HaveLength(12);
        phone.Should().MatchRegex(@"^\+7\d{10}$");
    }

    [Fact]
    public void FixtureExtensions_CreateValidUrl_ShouldCreateValidUrl()
    {
        // Act
        var url = TestDataBuilder._fixture.CreateValidUrl();

        // Assert
        url.Should().NotBeNullOrEmpty();
        url.Should().StartWith("https://");
        url.Should().EndWith("/callback");
    }

    [Fact]
    public void FixtureExtensions_CreateValidPaymentId_ShouldCreateValidFormat()
    {
        // Act
        var paymentId = TestDataBuilder._fixture.CreateValidPaymentId();

        // Assert
        paymentId.Should().NotBeNullOrEmpty();
        paymentId.Should().StartWith("PAY_");
        paymentId.Should().HaveLength(36); // "PAY_" + 32 character GUID
    }

    [Fact]
    public void FixtureExtensions_CreateValidOrderId_ShouldCreateValidFormat()
    {
        // Act
        var orderId = TestDataBuilder._fixture.CreateValidOrderId();

        // Assert
        orderId.Should().NotBeNullOrEmpty();
        orderId.Should().StartWith("ORDER_");
        orderId.Should().MatchRegex(@"^ORDER_\d{8}_\d{4}$");
    }
}