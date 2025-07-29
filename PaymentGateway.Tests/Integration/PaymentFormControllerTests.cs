// SPDX-License-Identifier: MIT
// Copyright (c) 2025 HackLoad Payment Gateway

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Text;
using Xunit;
using Xunit.Abstractions;
using PaymentGateway.API;
using PaymentGateway.Core.Entities;
using PaymentGateway.Core.Enums;
using PaymentGateway.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace PaymentGateway.Tests.Integration;

/// <summary>
/// Integration tests for PaymentFormController
/// Tests payment form rendering, validation, and processing functionality
/// </summary>
public class PaymentFormControllerTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly ITestOutputHelper _output;
    private readonly HttpClient _client;

    public PaymentFormControllerTests(WebApplicationFactory<Program> factory, ITestOutputHelper output)
    {
        _factory = factory;
        _output = output;
        _client = _factory.CreateClient();
    }

    [Fact]
    public async Task RenderPaymentForm_ValidPaymentId_ReturnsHtmlForm()
    {
        // Arrange
        var paymentId = "TEST-PAYMENT-" + Guid.NewGuid().ToString("N")[..8];
        
        // Create test payment in database
        using var scope = _factory.Services.CreateScope();
        var paymentRepo = scope.ServiceProvider.GetRequiredService<IPaymentRepository>();
        var teamRepo = scope.ServiceProvider.GetRequiredService<ITeamRepository>();
        
        // Get or create test team
        var team = await teamRepo.GetByTeamSlugAsync("test-team") ?? 
                   await CreateTestTeam(teamRepo);
        
        var payment = await CreateTestPayment(paymentRepo, team.Id, paymentId);

        // Act
        var response = await _client.GetAsync($"/api/v1/paymentform/render/{paymentId}");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html; charset=utf-8", response.Content.Headers.ContentType?.ToString());

        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains(paymentId, content);
        Assert.Contains(payment.Amount.ToString("F2"), content);
        Assert.Contains(payment.Currency, content);
        Assert.Contains("payment-form", content);
        Assert.Contains("csrf", content);
        
        _output.WriteLine($"Payment form rendered successfully for PaymentId: {paymentId}");
    }

    [Fact]
    public async Task RenderPaymentForm_InvalidPaymentId_ReturnsBadRequest()
    {
        // Arrange
        var invalidPaymentId = "invalid-payment-id-@#$%";

        // Act
        var response = await _client.GetAsync($"/api/v1/paymentform/render/{invalidPaymentId}");

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("Invalid payment ID format", content);
        
        _output.WriteLine($"Invalid PaymentId correctly rejected: {invalidPaymentId}");
    }

    [Fact]
    public async Task RenderPaymentForm_NonExistentPayment_ReturnsNotFound()
    {
        // Arrange
        var nonExistentPaymentId = "NON-EXISTENT-PAYMENT-123";

        // Act
        var response = await _client.GetAsync($"/api/v1/paymentform/render/{nonExistentPaymentId}");

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("Payment not found", content);
        
        _output.WriteLine($"Non-existent payment correctly handled: {nonExistentPaymentId}");
    }

    [Fact]
    public async Task RenderPaymentForm_AuthorizedPayment_ReturnsBadRequest()
    {
        // Arrange
        var paymentId = "AUTHORIZED-PAYMENT-" + Guid.NewGuid().ToString("N")[..8];
        
        using var scope = _factory.Services.CreateScope();
        var paymentRepo = scope.ServiceProvider.GetRequiredService<IPaymentRepository>();
        var teamRepo = scope.ServiceProvider.GetRequiredService<ITeamRepository>();
        
        var team = await teamRepo.GetByTeamSlugAsync("test-team") ?? 
                   await CreateTestTeam(teamRepo);
        
        var payment = await CreateTestPayment(paymentRepo, team.Id, paymentId);
        payment.Status = PaymentStatus.AUTHORIZED;
        await paymentRepo.UpdateAsync(payment);

        // Act
        var response = await _client.GetAsync($"/api/v1/paymentform/render/{paymentId}");

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("AUTHORIZED status", content);
        
        _output.WriteLine($"Authorized payment form rendering correctly rejected: {paymentId}");
    }

    [Fact]
    public async Task RenderPaymentForm_WithLanguageParameter_ReturnsLocalizedForm()
    {
        // Arrange
        var paymentId = "LOCALIZED-PAYMENT-" + Guid.NewGuid().ToString("N")[..8];
        
        using var scope = _factory.Services.CreateScope();
        var paymentRepo = scope.ServiceProvider.GetRequiredService<IPaymentRepository>();
        var teamRepo = scope.ServiceProvider.GetRequiredService<ITeamRepository>();
        
        var team = await teamRepo.GetByTeamSlugAsync("test-team") ?? 
                   await CreateTestTeam(teamRepo);
        
        await CreateTestPayment(paymentRepo, team.Id, paymentId);

        // Act
        var response = await _client.GetAsync($"/api/v1/paymentform/render/{paymentId}?lang=ru");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("data-lang=\"ru\"", content);
        
        _output.WriteLine($"Russian language form rendered successfully for PaymentId: {paymentId}");
    }

    [Fact]
    public async Task SubmitPaymentForm_ValidData_ProcessesSuccessfully()
    {
        // Arrange
        var paymentId = "SUBMIT-PAYMENT-" + Guid.NewGuid().ToString("N")[..8];
        
        using var scope = _factory.Services.CreateScope();
        var paymentRepo = scope.ServiceProvider.GetRequiredService<IPaymentRepository>();
        var teamRepo = scope.ServiceProvider.GetRequiredService<ITeamRepository>();
        
        var team = await teamRepo.GetByTeamSlugAsync("test-team") ?? 
                   await CreateTestTeam(teamRepo);
        
        await CreateTestPayment(paymentRepo, team.Id, paymentId);

        // First, get the form to obtain CSRF token
        var formResponse = await _client.GetAsync($"/api/v1/paymentform/render/{paymentId}");
        var formContent = await formResponse.Content.ReadAsStringAsync();
        
        // Extract CSRF token (simple extraction for test)
        var csrfToken = ExtractCsrfToken(formContent);
        
        var formData = new List<KeyValuePair<string, string>>
        {
            new("PaymentId", paymentId),
            new("CardNumber", "4111111111111111"), // Valid test Visa card
            new("ExpiryDate", "12/25"),
            new("Cvv", "123"),
            new("CardholderName", "John Doe"),
            new("Email", "john.doe@example.com"),
            new("Phone", "+1234567890"),
            new("TermsAgreement", "true"),
            new("CsrfToken", csrfToken)
        };

        var formContent2 = new FormUrlEncodedContent(formData);

        // Act
        var response = await _client.PostAsync("/api/v1/paymentform/submit", formContent2);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html; charset=utf-8", response.Content.Headers.ContentType?.ToString());
        
        var responseContent = await response.Content.ReadAsStringAsync();
        Assert.Contains("Payment Successful", responseContent);
        Assert.Contains(paymentId, responseContent);
        
        _output.WriteLine($"Payment form submitted successfully for PaymentId: {paymentId}");
    }

    [Fact]
    public async Task SubmitPaymentForm_InvalidCardNumber_ReturnsValidationError()
    {
        // Arrange
        var paymentId = "INVALID-CARD-" + Guid.NewGuid().ToString("N")[..8];
        
        using var scope = _factory.Services.CreateScope();
        var paymentRepo = scope.ServiceProvider.GetRequiredService<IPaymentRepository>();
        var teamRepo = scope.ServiceProvider.GetRequiredService<ITeamRepository>();
        
        var team = await teamRepo.GetByTeamSlugAsync("test-team") ?? 
                   await CreateTestTeam(teamRepo);
        
        await CreateTestPayment(paymentRepo, team.Id, paymentId);

        var formData = new List<KeyValuePair<string, string>>
        {
            new("PaymentId", paymentId),
            new("CardNumber", "1234567890123456"), // Invalid card number
            new("ExpiryDate", "12/25"),
            new("Cvv", "123"),
            new("CardholderName", "John Doe"),
            new("Email", "john.doe@example.com"),
            new("TermsAgreement", "true"),
            new("CsrfToken", "dummy-token") // We'll test CSRF separately
        };

        var formContent = new FormUrlEncodedContent(formData);

        // Act
        var response = await _client.PostAsync("/api/v1/paymentform/submit", formContent);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        
        var responseContent = await response.Content.ReadAsStringAsync();
        Assert.Contains("Invalid card number", responseContent);
        
        _output.WriteLine($"Invalid card number correctly rejected for PaymentId: {paymentId}");
    }

    [Fact]
    public async Task SubmitPaymentForm_MissingRequiredFields_ReturnsValidationErrors()
    {
        // Arrange
        var paymentId = "MISSING-FIELDS-" + Guid.NewGuid().ToString("N")[..8];
        
        using var scope = _factory.Services.CreateScope();
        var paymentRepo = scope.ServiceProvider.GetRequiredService<IPaymentRepository>();
        var teamRepo = scope.ServiceProvider.GetRequiredService<ITeamRepository>();
        
        var team = await teamRepo.GetByTeamSlugAsync("test-team") ?? 
                   await CreateTestTeam(teamRepo);
        
        await CreateTestPayment(paymentRepo, team.Id, paymentId);

        var formData = new List<KeyValuePair<string, string>>
        {
            new("PaymentId", paymentId),
            // Missing required fields: CardNumber, ExpiryDate, Cvv, CardholderName, Email
            new("CsrfToken", "dummy-token")
        };

        var formContent = new FormUrlEncodedContent(formData);

        // Act
        var response = await _client.PostAsync("/api/v1/paymentform/submit", formContent);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        
        var responseContent = await response.Content.ReadAsStringAsync();
        Assert.Contains("Card number is required", responseContent);
        Assert.Contains("Expiry date is required", responseContent);
        Assert.Contains("CVV is required", responseContent);
        Assert.Contains("Cardholder name is required", responseContent);
        Assert.Contains("Email is required", responseContent);
        
        _output.WriteLine($"Missing required fields correctly validated for PaymentId: {paymentId}");
    }

    [Fact]
    public async Task SubmitPaymentForm_ExpiredCard_ReturnsValidationError()
    {
        // Arrange
        var paymentId = "EXPIRED-CARD-" + Guid.NewGuid().ToString("N")[..8];
        
        using var scope = _factory.Services.CreateScope();
        var paymentRepo = scope.ServiceProvider.GetRequiredService<IPaymentRepository>();
        var teamRepo = scope.ServiceProvider.GetRequiredService<ITeamRepository>();
        
        var team = await teamRepo.GetByTeamSlugAsync("test-team") ?? 
                   await CreateTestTeam(teamRepo);
        
        await CreateTestPayment(paymentRepo, team.Id, paymentId);

        var formData = new List<KeyValuePair<string, string>>
        {
            new("PaymentId", paymentId),
            new("CardNumber", "4111111111111111"),
            new("ExpiryDate", "01/20"), // Expired date
            new("Cvv", "123"),
            new("CardholderName", "John Doe"),
            new("Email", "john.doe@example.com"),
            new("TermsAgreement", "true"),
            new("CsrfToken", "dummy-token")
        };

        var formContent = new FormUrlEncodedContent(formData);

        // Act
        var response = await _client.PostAsync("/api/v1/paymentform/submit", formContent);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        
        var responseContent = await response.Content.ReadAsStringAsync();
        Assert.Contains("Invalid or expired card", responseContent);
        
        _output.WriteLine($"Expired card correctly rejected for PaymentId: {paymentId}");
    }

    [Fact]
    public async Task SubmitPaymentForm_InvalidCsrfToken_ReturnsBadRequest()
    {
        // Arrange
        var paymentId = "CSRF-TEST-" + Guid.NewGuid().ToString("N")[..8];
        
        using var scope = _factory.Services.CreateScope();
        var paymentRepo = scope.ServiceProvider.GetRequiredService<IPaymentRepository>();
        var teamRepo = scope.ServiceProvider.GetRequiredService<ITeamRepository>();
        
        var team = await teamRepo.GetByTeamSlugAsync("test-team") ?? 
                   await CreateTestTeam(teamRepo);
        
        await CreateTestPayment(paymentRepo, team.Id, paymentId);

        var formData = new List<KeyValuePair<string, string>>
        {
            new("PaymentId", paymentId),
            new("CardNumber", "4111111111111111"),
            new("ExpiryDate", "12/25"),
            new("Cvv", "123"),
            new("CardholderName", "John Doe"),
            new("Email", "john.doe@example.com"),
            new("TermsAgreement", "true"),
            new("CsrfToken", "invalid-csrf-token")
        };

        var formContent = new FormUrlEncodedContent(formData);

        // Act
        var response = await _client.PostAsync("/api/v1/paymentform/submit", formContent);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        
        var responseContent = await response.Content.ReadAsStringAsync();
        Assert.Contains("Invalid security token", responseContent);
        
        _output.WriteLine($"Invalid CSRF token correctly rejected for PaymentId: {paymentId}");
    }

    [Fact]
    public async Task GetPaymentResult_ValidPayment_ReturnsResultPage()
    {
        // Arrange
        var paymentId = "RESULT-PAYMENT-" + Guid.NewGuid().ToString("N")[..8];
        
        using var scope = _factory.Services.CreateScope();
        var paymentRepo = scope.ServiceProvider.GetRequiredService<IPaymentRepository>();
        var teamRepo = scope.ServiceProvider.GetRequiredService<ITeamRepository>();
        
        var team = await teamRepo.GetByTeamSlugAsync("test-team") ?? 
                   await CreateTestTeam(teamRepo);
        
        var payment = await CreateTestPayment(paymentRepo, team.Id, paymentId);
        payment.Status = PaymentStatus.AUTHORIZED;
        await paymentRepo.UpdateAsync(payment);

        // Act
        var response = await _client.GetAsync($"/api/v1/paymentform/result/{paymentId}?success=true&message=Payment%20successful");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html; charset=utf-8", response.Content.Headers.ContentType?.ToString());
        
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("Payment Successful", content);
        Assert.Contains(paymentId, content);
        Assert.Contains(payment.Amount.ToString("F2"), content);
        Assert.Contains("Payment successful", content);
        
        _output.WriteLine($"Payment result page rendered successfully for PaymentId: {paymentId}");
    }

    [Fact]
    public async Task GetPaymentResult_PaymentFailure_ReturnsFailurePage()
    {
        // Arrange
        var paymentId = "FAILED-PAYMENT-" + Guid.NewGuid().ToString("N")[..8];
        
        using var scope = _factory.Services.CreateScope();
        var paymentRepo = scope.ServiceProvider.GetRequiredService<IPaymentRepository>();
        var teamRepo = scope.ServiceProvider.GetRequiredService<ITeamRepository>();
        
        var team = await teamRepo.GetByTeamSlugAsync("test-team") ?? 
                   await CreateTestTeam(teamRepo);
        
        var payment = await CreateTestPayment(paymentRepo, team.Id, paymentId);
        payment.Status = PaymentStatus.FAILED;
        await paymentRepo.UpdateAsync(payment);

        // Act
        var response = await _client.GetAsync($"/api/v1/paymentform/result/{paymentId}?success=false&message=Card%20declined");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("Payment Failed", content);
        Assert.Contains(paymentId, content);
        Assert.Contains("Card declined", content);
        Assert.Contains("result-error", content);
        
        _output.WriteLine($"Payment failure page rendered successfully for PaymentId: {paymentId}");
    }

    // Helper methods

    private async Task<Team> CreateTestTeam(ITeamRepository teamRepo)
    {
        var team = new Team
        {
            Id = Guid.NewGuid(),
            Name = "Test Team",
            TeamSlug = "test-team",
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        await teamRepo.AddAsync(team);
        return team;
    }

    private async Task<Payment> CreateTestPayment(IPaymentRepository paymentRepo, Guid teamId, string paymentId)
    {
        var payment = new Payment
        {
            Id = Guid.NewGuid(),
            PaymentId = paymentId,
            OrderId = "ORDER-" + Guid.NewGuid().ToString("N")[..8],
            TeamId = teamId,
            Amount = 1500.00m,
            Currency = "RUB",
            Description = "Test payment",
            Status = PaymentStatus.NEW,
            PaymentExpiresAt = DateTime.UtcNow.AddHours(1),
            SuccessUrl = "https://example.com/success",
            FailUrl = "https://example.com/fail",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        await paymentRepo.AddAsync(payment);
        return payment;
    }

    private static string ExtractCsrfToken(string htmlContent)
    {
        // Simple regex to extract CSRF token from form
        var match = System.Text.RegularExpressions.Regex.Match(
            htmlContent, 
            @"name=""csrf_token""[^>]*value=""([^""]+)""");
        
        return match.Success ? match.Groups[1].Value : "test-csrf-token";
    }
}