using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PaymentGateway.API;
using PaymentGateway.Core.DTOs.TeamRegistration;
using PaymentGateway.Core.Entities;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using CoreDbContext = PaymentGateway.Core.Data.PaymentGatewayDbContext;
using InfraDbContext = PaymentGateway.Infrastructure.Data.PaymentGatewayDbContext;

namespace PaymentGateway.Tests.Integration;

[TestFixture]
public class TeamRegistrationIntegrationTests
{
    private WebApplicationFactory<Program> _factory;
    private HttpClient _client;
    private string _adminToken;
    private IServiceScope _scope;
    private CoreDbContext _dbContext;

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        _factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureAppConfiguration((context, config) =>
                {
                    config.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        // Use in-memory database for testing
                        ["ConnectionStrings:DefaultConnection"] = "DataSource=:memory:",
                        ["ConnectionStrings:PostgreSqlConnection"] = "",
                        ["AdminAuthentication:AdminToken"] = "test-admin-token-for-integration-tests",
                        ["AdminAuthentication:EnableAdminEndpoints"] = "true",
                        ["AdminAuthentication:TokenHeaderName"] = "X-Admin-Token"
                    });
                });

                builder.ConfigureServices(services =>
                {
                    // Remove the real database context
                    var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(DbContextOptions<CoreDbContext>));
                    if (descriptor != null)
                        services.Remove(descriptor);

                    // Add in-memory database for testing
                    services.AddDbContext<CoreDbContext>(options =>
                    {
                        options.UseInMemoryDatabase("TeamRegistrationTestDb");
                        options.EnableSensitiveDataLogging();
                        options.EnableDetailedErrors();
                    });

                    // Replace the Infrastructure DbContext with the Core one for testing
                    var infrastructureDescriptor = services.SingleOrDefault(d => d.ServiceType == typeof(DbContextOptions<InfraDbContext>));
                    if (infrastructureDescriptor != null)
                        services.Remove(infrastructureDescriptor);

                    services.AddDbContext<InfraDbContext>(options =>
                    {
                        options.UseInMemoryDatabase("TeamRegistrationTestDb");
                        options.EnableSensitiveDataLogging();
                        options.EnableDetailedErrors();
                    });
                });

                builder.UseEnvironment("Testing");
            });

        _client = _factory.CreateClient();
        _adminToken = "test-admin-token-for-integration-tests";
        
        // Set up admin authentication header
        _client.DefaultRequestHeaders.Add("X-Admin-Token", _adminToken);
        
        // Create service scope for database access
        _scope = _factory.Services.CreateScope();
        _dbContext = _scope.ServiceProvider.GetRequiredService<CoreDbContext>();
        
        // Ensure database is created
        _dbContext.Database.EnsureCreated();
    }

    [OneTimeTearDown]
    public void OneTimeTearDown()
    {
        _dbContext?.Dispose();
        _scope?.Dispose();
        _client?.Dispose();
        _factory?.Dispose();
    }

    [SetUp]
    public void SetUp()
    {
        // Clear the database before each test
        _dbContext.Teams.RemoveRange(_dbContext.Teams);
        _dbContext.SaveChanges();
    }

    #region Integration Tests - Real Database Operations

    [Test]
    public async Task TC_INTEGRATION_001_RegisterTeam_WithValidData_ShouldCreateTeamInDatabase()
    {
        // Arrange
        var request = CreateValidRegistrationRequest("integration-test-team");
        var json = JsonSerializer.Serialize(request, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        // Act
        var response = await _client.PostAsync("/api/v1/TeamRegistration/register", content);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        
        var responseContent = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<TeamRegistrationResponseDto>(responseContent, 
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

        result.Should().NotBeNull();
        result!.Success.Should().BeTrue();
        result.TeamSlug.Should().Be(request.TeamSlug);

        // Verify team was created in database
        var teamInDb = await _dbContext.Teams.FirstOrDefaultAsync(t => t.TeamSlug == request.TeamSlug);
        teamInDb.Should().NotBeNull();
        teamInDb!.TeamName.Should().Be(request.TeamName);
        teamInDb.ContactEmail.Should().Be(request.Email);
        teamInDb.IsActive.Should().BeTrue();
    }

    [Test]
    public async Task TC_INTEGRATION_002_RegisterTeam_WithDuplicateSlug_ShouldReturnConflict()
    {
        // Arrange - Create a team first
        var existingTeam = CreateTestTeamEntity("duplicate-slug-test");
        await _dbContext.Teams.AddAsync(existingTeam);
        await _dbContext.SaveChangesAsync();

        var request = CreateValidRegistrationRequest("duplicate-slug-test");
        var json = JsonSerializer.Serialize(request, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        // Act
        var response = await _client.PostAsync("/api/v1/TeamRegistration/register", content);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        
        var responseContent = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<TeamRegistrationResponseDto>(responseContent, 
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

        result.Should().NotBeNull();
        result!.Success.Should().BeFalse();
        result.ErrorCode.Should().Be("2002");
        result.Message.Should().Contain("Team slug already exists");
    }

    [Test]
    public async Task TC_INTEGRATION_003_RegisterTeam_WithDuplicateEmail_ShouldReturnConflict()
    {
        // Arrange - Create a team with the same email first
        var existingTeam = CreateTestTeamEntity("existing-team");
        existingTeam.ContactEmail = "duplicate@email.com";
        await _dbContext.Teams.AddAsync(existingTeam);
        await _dbContext.SaveChangesAsync();

        var request = CreateValidRegistrationRequest("new-team");
        request.Email = "duplicate@email.com";
        var json = JsonSerializer.Serialize(request, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        // Act
        var response = await _client.PostAsync("/api/v1/TeamRegistration/register", content);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        
        var responseContent = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<TeamRegistrationResponseDto>(responseContent, 
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

        result.Should().NotBeNull();
        result!.Success.Should().BeFalse();
        result.ErrorCode.Should().Be("2003");
        result.Message.Should().Contain("Email already registered");
    }

    [Test]
    public async Task TC_INTEGRATION_004_CheckAvailability_WithAvailableSlug_ShouldReturnTrue()
    {
        // Arrange
        var teamSlug = "available-slug-test";

        // Act
        var response = await _client.GetAsync($"/api/v1/TeamRegistration/check-availability/{teamSlug}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        
        var responseContent = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<Dictionary<string, object>>(responseContent, 
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

        result.Should().NotBeNull();
        result!.Should().ContainKey("available");
        result["available"].ToString().Should().Be("True");
        result["teamSlug"].ToString().Should().Be(teamSlug);
    }

    [Test]
    public async Task TC_INTEGRATION_005_CheckAvailability_WithTakenSlug_ShouldReturnFalse()
    {
        // Arrange - Create a team first
        var teamSlug = "taken-slug-test";
        var existingTeam = CreateTestTeamEntity(teamSlug);
        await _dbContext.Teams.AddAsync(existingTeam);
        await _dbContext.SaveChangesAsync();

        // Act
        var response = await _client.GetAsync($"/api/v1/TeamRegistration/check-availability/{teamSlug}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        
        var responseContent = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<Dictionary<string, object>>(responseContent, 
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

        result.Should().NotBeNull();
        result!.Should().ContainKey("available");
        result["available"].ToString().Should().Be("False");
        result["teamSlug"].ToString().Should().Be(teamSlug);
    }

    [Test]
    public async Task TC_INTEGRATION_006_GetStatus_WithExistingTeam_ShouldReturnTeamStatus()
    {
        // Arrange - Create a team first
        var teamSlug = "status-test-team";
        var existingTeam = CreateTestTeamEntity(teamSlug);
        await _dbContext.Teams.AddAsync(existingTeam);
        await _dbContext.SaveChangesAsync();

        // Act
        var response = await _client.GetAsync($"/api/v1/TeamRegistration/status/{teamSlug}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        
        var responseContent = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<Dictionary<string, object>>(responseContent, 
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

        result.Should().NotBeNull();
        result!.Should().ContainKey("teamSlug");
        result["teamSlug"].ToString().Should().Be(teamSlug);
        result.Should().ContainKey("isActive");
        result["isActive"].ToString().Should().Be("True");
    }

    [Test]
    public async Task TC_INTEGRATION_007_GetStatus_WithNonExistentTeam_ShouldReturnNotFound()
    {
        // Arrange
        var teamSlug = "non-existent-team";

        // Act
        var response = await _client.GetAsync($"/api/v1/TeamRegistration/status/{teamSlug}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        
        var responseContent = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<Dictionary<string, object>>(responseContent, 
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

        result.Should().NotBeNull();
        result!.Should().ContainKey("error");
        result["error"].ToString().Should().Be("Team not found");
    }

    [Test]
    public async Task TC_INTEGRATION_008_RegisterTeam_WithoutAdminToken_ShouldReturnForbidden()
    {
        // Arrange
        var clientWithoutToken = _factory.CreateClient(); // No admin token
        var request = CreateValidRegistrationRequest("unauthorized-test");
        var json = JsonSerializer.Serialize(request, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        // Act
        var response = await clientWithoutToken.PostAsync("/api/v1/TeamRegistration/register", content);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        
        var responseContent = await response.Content.ReadAsStringAsync();
        responseContent.Should().Contain("Invalid admin token");
    }

    [Test]
    public async Task TC_INTEGRATION_009_RegisterTeam_VerifyPasswordHashing_ShouldHashPasswordSecurely()
    {
        // Arrange
        var request = CreateValidRegistrationRequest("password-hash-test");
        var plainPassword = request.Password;
        var json = JsonSerializer.Serialize(request, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        // Act
        var response = await _client.PostAsync("/api/v1/TeamRegistration/register", content);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        
        // Verify password was hashed in database
        var teamInDb = await _dbContext.Teams.FirstOrDefaultAsync(t => t.TeamSlug == request.TeamSlug);
        teamInDb.Should().NotBeNull();
        teamInDb!.Password.Should().NotBe(plainPassword);
        teamInDb.Password.Should().NotBeNullOrEmpty();
        teamInDb.Password.Length.Should().BeGreaterThan(8); // Password should be longer than 8 characters
    }

    [Test]
    public async Task TC_INTEGRATION_010_RegisterTeam_VerifyArrayFields_ShouldStoreSupportedCurrenciesCorrectly()
    {
        // Arrange
        var request = CreateValidRegistrationRequest("array-fields-test");
        request.SupportedCurrencies = "USD,EUR,GBP,RUB";
        var json = JsonSerializer.Serialize(request, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        // Act
        var response = await _client.PostAsync("/api/v1/TeamRegistration/register", content);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        
        // Verify supported currencies were stored correctly
        var teamInDb = await _dbContext.Teams.FirstOrDefaultAsync(t => t.TeamSlug == request.TeamSlug);
        teamInDb.Should().NotBeNull();
        teamInDb!.SupportedCurrencies.Should().NotBeNull();
        var expectedCurrencies = request.SupportedCurrencies.Split(',').Select(c => c.Trim()).ToList();
        teamInDb.SupportedCurrencies.Should().BeEquivalentTo(expectedCurrencies);
        teamInDb.SupportedCurrencies.Count.Should().Be(4);
    }

    #endregion

    #region Performance Tests

    [Test]
    public async Task TC_INTEGRATION_011_RegisterTeam_PerformanceTest_ShouldCompleteWithinReasonableTime()
    {
        // Arrange
        var request = CreateValidRegistrationRequest("performance-test");
        var json = JsonSerializer.Serialize(request, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        var content = new StringContent(json, Encoding.UTF8, "application/json");
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        // Act
        var response = await _client.PostAsync("/api/v1/TeamRegistration/register", content);
        stopwatch.Stop();

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        stopwatch.ElapsedMilliseconds.Should().BeLessThan(5000); // Should complete within 5 seconds
    }

    [Test]
    public async Task TC_INTEGRATION_012_RegisterMultipleTeams_ConcurrencyTest_ShouldHandleConcurrentRequests()
    {
        // Arrange
        var tasks = new List<Task<HttpResponseMessage>>();
        const int concurrentRequests = 5;

        for (int i = 0; i < concurrentRequests; i++)
        {
            var request = CreateValidRegistrationRequest($"concurrent-test-{i}");
            var json = JsonSerializer.Serialize(request, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            
            tasks.Add(_client.PostAsync("/api/v1/TeamRegistration/register", content));
        }

        // Act
        var responses = await Task.WhenAll(tasks);

        // Assert
        foreach (var response in responses)
        {
            response.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        // Verify all teams were created in database
        var teamsInDb = await _dbContext.Teams.CountAsync(t => t.TeamSlug.StartsWith("concurrent-test-"));
        teamsInDb.Should().Be(concurrentRequests);
    }

    #endregion

    #region Helper Methods

    private static TeamRegistrationRequestDto CreateValidRegistrationRequest(string teamSlug)
    {
        return new TeamRegistrationRequestDto
        {
            TeamSlug = teamSlug,
            TeamName = $"Test Team {teamSlug}",
            Password = "SecurePassword123!",
            Email = $"{teamSlug}@testdomain.com",
            Phone = "+1234567890",
            NotificationURL = $"https://{teamSlug}.test.com/webhook",
            SuccessURL = $"https://{teamSlug}.test.com/success",
            FailURL = $"https://{teamSlug}.test.com/fail",
            SupportedCurrencies = "USD,EUR",
            BusinessInfo = new Dictionary<string, string>
            {
                ["description"] = $"Test team for {teamSlug}",
                ["legalName"] = $"Test Team {teamSlug} LLC",
                ["address"] = "123 Test Street, Test City, TC 12345",
                ["country"] = "US"
            },
            AcceptTerms = true
        };
    }

    private static Team CreateTestTeamEntity(string teamSlug)
    {
        return new Team
        {
            Id = Guid.NewGuid(),
            TeamSlug = teamSlug,
            TeamName = $"Test Team {teamSlug}",
            Password = "hashed_password_placeholder",
            ContactEmail = $"{teamSlug}@testdomain.com",
            ContactPhone = "+1234567890",
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            SupportedCurrencies = new List<string> { "USD", "EUR" },
            BusinessInfo = new Dictionary<string, string>(),
            Metadata = new Dictionary<string, string>()
        };
    }

    #endregion
}