// SPDX-License-Identifier: MIT
// Copyright (c) 2025 HackLoad Payment Gateway

using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using PaymentGateway.Core.Entities;
using PaymentGateway.Core.Interfaces;
using PaymentGateway.Core.Services;
using PaymentGateway.Tests.TestHelpers;
using System.Security.Cryptography;
using System.Text;

namespace PaymentGateway.Tests.UnitTests;

/// <summary>
/// Unit tests for AuthenticationService
/// </summary>
public class AuthenticationServiceTests : BaseTest
{
    private readonly Mock<ITeamRepository> _mockTeamRepository;
    private readonly Mock<IMemoryCache> _mockMemoryCache;
    private readonly AuthenticationService _authenticationService;

    public AuthenticationServiceTests()
    {
        _mockTeamRepository = AddMockRepository<ITeamRepository>();
        _mockMemoryCache = AddMockService<IMemoryCache>();

        _authenticationService = new AuthenticationService(
            GetService<ILogger<AuthenticationService>>(),
            MockConfiguration.Object,
            _mockMemoryCache.Object,
            _mockTeamRepository.Object
        );
    }

    [Fact]
    public async Task AuthenticateAsync_WithValidCredentials_ShouldReturnSuccess()
    {
        // Arrange
        var team = TestDataBuilder.CreateTeam("test_team", "Test Team");
        var password = "test_password";
        var requestParameters = new Dictionary<string, object>
        {
            ["Amount"] = "1000",
            ["OrderId"] = "ORDER_123",
            ["Currency"] = "RUB"
        };

        // Generate valid token
        var sortedParams = string.Join("&", requestParameters
            .OrderBy(kvp => kvp.Key)
            .Select(kvp => $"{kvp.Key}={kvp.Value}"));
        var tokenData = $"{team.TeamSlug}{sortedParams}{password}";
        var tokenBytes = SHA256.HashData(Encoding.UTF8.GetBytes(tokenData));
        var expectedToken = Convert.ToBase64String(tokenBytes);

        _mockTeamRepository
            .Setup(r => r.GetByTeamSlugAsync(team.TeamSlug))
            .ReturnsAsync(team);

        // Act
        var result = await _authenticationService.AuthenticateAsync(
            team.TeamSlug, requestParameters, password, expectedToken);

        // Assert
        result.IsAuthenticated.Should().BeTrue();
        result.Team.Should().NotBeNull();
        result.Team!.TeamSlug.Should().Be(team.TeamSlug);
        result.ErrorMessage.Should().BeNull();
    }

    [Fact]
    public async Task AuthenticateAsync_WithInvalidTeam_ShouldReturnFailure()
    {
        // Arrange
        var teamSlug = "nonexistent_team";
        var password = "test_password";
        var requestParameters = new Dictionary<string, object>();
        var token = "invalid_token";

        _mockTeamRepository
            .Setup(r => r.GetByTeamSlugAsync(teamSlug))
            .ReturnsAsync((Team?)null);

        // Act
        var result = await _authenticationService.AuthenticateAsync(
            teamSlug, requestParameters, password, token);

        // Assert
        result.IsAuthenticated.Should().BeFalse();
        result.Team.Should().BeNull();
        result.ErrorMessage.Should().Contain("Team not found");
    }

    [Fact]
    public async Task AuthenticateAsync_WithInactiveTeam_ShouldReturnFailure()
    {
        // Arrange
        var team = TestDataBuilder.CreateTeam("test_team", "Test Team", isActive: false);
        var password = "test_password";
        var requestParameters = new Dictionary<string, object>();
        var token = "some_token";

        _mockTeamRepository
            .Setup(r => r.GetByTeamSlugAsync(team.TeamSlug))
            .ReturnsAsync(team);

        // Act
        var result = await _authenticationService.AuthenticateAsync(
            team.TeamSlug, requestParameters, password, token);

        // Assert
        result.IsAuthenticated.Should().BeFalse();
        result.Team.Should().BeNull();
        result.ErrorMessage.Should().Contain("Team is not active");
    }

    [Fact]
    public async Task AuthenticateAsync_WithInvalidToken_ShouldReturnFailure()
    {
        // Arrange
        var team = TestDataBuilder.CreateTeam("test_team", "Test Team");
        var password = "test_password";
        var requestParameters = new Dictionary<string, object>
        {
            ["Amount"] = "1000",
            ["OrderId"] = "ORDER_123"
        };
        var invalidToken = "invalid_token";

        _mockTeamRepository
            .Setup(r => r.GetByTeamSlugAsync(team.TeamSlug))
            .ReturnsAsync(team);

        // Act
        var result = await _authenticationService.AuthenticateAsync(
            team.TeamSlug, requestParameters, password, invalidToken);

        // Assert
        result.IsAuthenticated.Should().BeFalse();
        result.Team.Should().BeNull();
        result.ErrorMessage.Should().Contain("Invalid token");
    }

    [Fact]
    public async Task GenerateTokenAsync_ShouldGenerateValidToken()
    {
        // Arrange
        var teamSlug = "test_team";
        var password = "test_password";
        var requestParameters = new Dictionary<string, object>
        {
            ["Amount"] = "1000",
            ["OrderId"] = "ORDER_123",
            ["Currency"] = "RUB"
        };

        // Act
        var token = await _authenticationService.GenerateTokenAsync(
            teamSlug, requestParameters, password);

        // Assert
        token.Should().NotBeNullOrEmpty();
        
        // Verify token can be used for authentication
        var team = TestDataBuilder.CreateTeam(teamSlug, "Test Team");
        _mockTeamRepository
            .Setup(r => r.GetByTeamSlugAsync(teamSlug))
            .ReturnsAsync(team);

        var authResult = await _authenticationService.AuthenticateAsync(
            teamSlug, requestParameters, password, token);
        
        authResult.IsAuthenticated.Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("   ")]
    public async Task AuthenticateAsync_WithInvalidTeamSlug_ShouldReturnFailure(string? teamSlug)
    {
        // Arrange
        var password = "test_password";
        var requestParameters = new Dictionary<string, object>();
        var token = "some_token";

        // Act
        var result = await _authenticationService.AuthenticateAsync(
            teamSlug!, requestParameters, password, token);

        // Assert
        result.IsAuthenticated.Should().BeFalse();
        result.ErrorMessage.Should().Contain("TeamSlug is required");
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("   ")]
    public async Task AuthenticateAsync_WithInvalidPassword_ShouldReturnFailure(string? password)
    {
        // Arrange
        var teamSlug = "test_team";
        var requestParameters = new Dictionary<string, object>();
        var token = "some_token";

        // Act
        var result = await _authenticationService.AuthenticateAsync(
            teamSlug, requestParameters, password!, token);

        // Assert
        result.IsAuthenticated.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Password is required");
    }

    [Fact]
    public async Task AuthenticateAsync_WithNullRequestParameters_ShouldReturnFailure()
    {
        // Arrange
        var teamSlug = "test_team";
        var password = "test_password";
        var token = "some_token";

        // Act
        var result = await _authenticationService.AuthenticateAsync(
            teamSlug, null!, password, token);

        // Assert
        result.IsAuthenticated.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Request parameters are required");
    }

    [Fact]
    public async Task ValidateTokenAsync_WithValidToken_ShouldReturnTrue()
    {
        // Arrange
        var teamSlug = "test_team";
        var password = "test_password";
        var requestParameters = new Dictionary<string, object>
        {
            ["Amount"] = "1000",
            ["OrderId"] = "ORDER_123"
        };

        var token = await _authenticationService.GenerateTokenAsync(
            teamSlug, requestParameters, password);

        // Act
        var isValid = await _authenticationService.ValidateTokenAsync(
            teamSlug, requestParameters, password, token);

        // Assert
        isValid.Should().BeTrue();
    }

    [Fact]
    public async Task ValidateTokenAsync_WithInvalidToken_ShouldReturnFalse()
    {
        // Arrange
        var teamSlug = "test_team";
        var password = "test_password";
        var requestParameters = new Dictionary<string, object>
        {
            ["Amount"] = "1000",
            ["OrderId"] = "ORDER_123"
        };
        var invalidToken = "invalid_token";

        // Act
        var isValid = await _authenticationService.ValidateTokenAsync(
            teamSlug, requestParameters, password, invalidToken);

        // Assert
        isValid.Should().BeFalse();
    }

    [Fact]
    public async Task AuthenticateAsync_WithRateLimiting_ShouldRespectLimits()
    {
        // Arrange
        var team = TestDataBuilder.CreateTeam("test_team", "Test Team");
        var password = "wrong_password";
        var requestParameters = new Dictionary<string, object>();
        var token = "invalid_token";

        _mockTeamRepository
            .Setup(r => r.GetByTeamSlugAsync(team.TeamSlug))
            .ReturnsAsync(team);

        // Setup cache to simulate multiple failed attempts
        var failedAttempts = 4; // One less than the limit
        var cacheKey = $"auth_attempts_{team.TeamSlug}";
        
        _mockMemoryCache
            .Setup(mc => mc.TryGetValue(cacheKey, out It.Ref<object?>.IsAny))
            .Returns((object key, out object? value) =>
            {
                value = failedAttempts;
                return true;
            });

        // Act
        var result = await _authenticationService.AuthenticateAsync(
            team.TeamSlug, requestParameters, password, token);

        // Assert
        result.IsAuthenticated.Should().BeFalse();
        result.ErrorMessage.Should().NotBeNull();
    }

    [Fact]
    public async Task GetAuthenticationStatisticsAsync_ShouldReturnStatistics()
    {
        // Arrange
        var teamSlug = "test_team";

        // Act
        var stats = await _authenticationService.GetAuthenticationStatisticsAsync(teamSlug);

        // Assert
        stats.Should().NotBeNull();
        stats.TeamSlug.Should().Be(teamSlug);
        stats.TotalAttempts.Should().BeGreaterOrEqualTo(0);
        stats.SuccessfulAttempts.Should().BeGreaterOrEqualTo(0);
        stats.FailedAttempts.Should().BeGreaterOrEqualTo(0);
    }

    [Fact]
    public async Task ConcurrentAuthentication_ShouldHandleCorrectly()
    {
        // Arrange
        var team = TestDataBuilder.CreateTeam("test_team", "Test Team");
        var password = "test_password";
        var requestParameters = new Dictionary<string, object>
        {
            ["Amount"] = "1000",
            ["OrderId"] = "ORDER_123"
        };

        var token = await _authenticationService.GenerateTokenAsync(
            team.TeamSlug, requestParameters, password);

        _mockTeamRepository
            .Setup(r => r.GetByTeamSlugAsync(team.TeamSlug))
            .ReturnsAsync(team);

        // Act
        var tasks = Enumerable.Range(0, 10).Select(async i =>
            await _authenticationService.AuthenticateAsync(
                team.TeamSlug, requestParameters, password, token)
        );

        var results = await Task.WhenAll(tasks);

        // Assert
        results.Should().OnlyContain(r => r.IsAuthenticated == true);
        
        _mockTeamRepository.Verify(
            r => r.GetByTeamSlugAsync(team.TeamSlug),
            Times.AtLeast(10));
    }

    [Fact]
    public async Task TokenGeneration_WithDifferentParameters_ShouldGenerateDifferentTokens()
    {
        // Arrange
        var teamSlug = "test_team";
        var password = "test_password";
        var params1 = new Dictionary<string, object>
        {
            ["Amount"] = "1000",
            ["OrderId"] = "ORDER_123"
        };
        var params2 = new Dictionary<string, object>
        {
            ["Amount"] = "2000",
            ["OrderId"] = "ORDER_456"
        };

        // Act
        var token1 = await _authenticationService.GenerateTokenAsync(teamSlug, params1, password);
        var token2 = await _authenticationService.GenerateTokenAsync(teamSlug, params2, password);

        // Assert
        token1.Should().NotBe(token2);
    }

    [Fact]
    public async Task TokenGeneration_WithSameParameters_ShouldGenerateSameToken()
    {
        // Arrange
        var teamSlug = "test_team";
        var password = "test_password";
        var requestParameters = new Dictionary<string, object>
        {
            ["Amount"] = "1000",
            ["OrderId"] = "ORDER_123"
        };

        // Act
        var token1 = await _authenticationService.GenerateTokenAsync(teamSlug, requestParameters, password);
        var token2 = await _authenticationService.GenerateTokenAsync(teamSlug, requestParameters, password);

        // Assert
        token1.Should().Be(token2);
    }
}