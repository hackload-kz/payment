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

namespace PaymentGateway.Tests.Integration;

/// <summary>
/// Integration tests that use a real PostgreSQL database
/// Make sure you have PostgreSQL running and update the connection string below
/// </summary>
[TestFixture]
public class RealDatabaseIntegrationTests
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
                        // Use real PostgreSQL connection string
                        ["ConnectionStrings:DefaultConnection"] = "Host=localhost;Database=paymentgateway_test;Username=postgres;Password=postgres",
                        ["AdminAuthentication:AdminToken"] = "test-admin-token-for-real-db-tests",
                        ["AdminAuthentication:EnableAdminEndpoints"] = "true",
                        ["AdminAuthentication:TokenHeaderName"] = "X-Admin-Token"
                    });
                });

                builder.ConfigureServices(services =>
                {
                    // Remove the existing DbContext
                    var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(DbContextOptions<CoreDbContext>));
                    if (descriptor != null)
                        services.Remove(descriptor);

                    // Add real PostgreSQL database for testing
                    services.AddDbContext<CoreDbContext>(options =>
                    {
                        options.UseNpgsql("Host=localhost;Database=paymentgateway_test;Username=postgres;Password=postgres");
                        options.EnableSensitiveDataLogging();
                        options.EnableDetailedErrors();
                    });
                });

                builder.UseEnvironment("Testing");
            });

        _client = _factory.CreateClient();
        _adminToken = "test-admin-token-for-real-db-tests";
        
        // Set up admin authentication header
        _client.DefaultRequestHeaders.Add("X-Admin-Token", _adminToken);
        
        // Create service scope for database access
        _scope = _factory.Services.CreateScope();
        _dbContext = _scope.ServiceProvider.GetRequiredService<CoreDbContext>();
        
        // Ensure database is created and migrated
        try 
        {
            _dbContext.Database.EnsureCreated();
            Console.WriteLine("✅ Database connection established");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Database connection failed: {ex.Message}");
            Console.WriteLine("Make sure PostgreSQL is running and the connection string is correct");
            throw;
        }
    }

    [OneTimeTearDown]
    public void OneTimeTearDown()
    {
        // Clean up test data
        try
        {
            var testTeams = _dbContext.Teams.Where(t => t.TeamSlug.StartsWith("real-db-test-")).ToList();
            _dbContext.Teams.RemoveRange(testTeams);
            _dbContext.SaveChanges();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Warning: Could not clean up test data: {ex.Message}");
        }

        _dbContext?.Dispose();
        _scope?.Dispose();
        _client?.Dispose();
        _factory?.Dispose();
    }

    [SetUp]
    public void SetUp()
    {
        // Clean up any existing test data before each test
        var existingTestTeams = _dbContext.Teams
            .Where(t => t.TeamSlug.StartsWith("real-db-test-"))
            .ToList();
        
        if (existingTestTeams.Any())
        {
            _dbContext.Teams.RemoveRange(existingTestTeams);
            _dbContext.SaveChanges();
        }
    }

    [Test]
    public async Task TC_REALDB_001_RegisterTeam_WithValidData_ShouldPersistInRealDatabase()
    {
        // Arrange
        var teamSlug = $"real-db-test-{DateTime.Now:yyyyMMddHHmmss}";
        var request = CreateValidRegistrationRequest(teamSlug);
        var json = JsonSerializer.Serialize(request, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        // Act
        var response = await _client.PostAsync("/api/v1/TeamRegistration/register", content);

        // Assert
        Console.WriteLine($"Response Status: {response.StatusCode}");
        var responseContent = await response.Content.ReadAsStringAsync();
        Console.WriteLine($"Response Content: {responseContent}");

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        
        // Verify in real database
        var teamInDb = await _dbContext.Teams.FirstOrDefaultAsync(t => t.TeamSlug == teamSlug);
        teamInDb.Should().NotBeNull("Team should be persisted in real database");
        teamInDb!.TeamName.Should().Be(request.TeamName);
        teamInDb.ContactEmail.Should().Be(request.Email);
        teamInDb.IsActive.Should().BeTrue();
        
        Console.WriteLine($"✅ Team '{teamSlug}' successfully created in real database");
    }

    [Test]
    public async Task TC_REALDB_002_RegisterTeam_CheckAvailability_FullWorkflow()
    {
        // Arrange
        var teamSlug = $"real-db-test-workflow-{DateTime.Now:yyyyMMddHHmmss}";

        // Step 1: Check availability (should be available)
        var availabilityResponse1 = await _client.GetAsync($"/api/v1/TeamRegistration/check-availability/{teamSlug}");
        availabilityResponse1.StatusCode.Should().Be(HttpStatusCode.OK);
        
        var availContent1 = await availabilityResponse1.Content.ReadAsStringAsync();
        Console.WriteLine($"Availability check 1: {availContent1}");

        // Step 2: Register the team
        var request = CreateValidRegistrationRequest(teamSlug);
        var json = JsonSerializer.Serialize(request, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var registerResponse = await _client.PostAsync("/api/v1/TeamRegistration/register", content);
        registerResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        
        var registerContent = await registerResponse.Content.ReadAsStringAsync();
        Console.WriteLine($"Registration: {registerContent}");

        // Step 3: Check availability again (should be taken)
        var availabilityResponse2 = await _client.GetAsync($"/api/v1/TeamRegistration/check-availability/{teamSlug}");
        availabilityResponse2.StatusCode.Should().Be(HttpStatusCode.OK);
        
        var availContent2 = await availabilityResponse2.Content.ReadAsStringAsync();
        Console.WriteLine($"Availability check 2: {availContent2}");

        // Step 4: Get team status
        var statusResponse = await _client.GetAsync($"/api/v1/TeamRegistration/status/{teamSlug}");
        statusResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        
        var statusContent = await statusResponse.Content.ReadAsStringAsync();
        Console.WriteLine($"Team status: {statusContent}");

        Console.WriteLine($"✅ Full workflow completed successfully for team '{teamSlug}'");
    }

    [Test]
    public async Task TC_REALDB_003_RegisterTeam_DuplicateSlug_ShouldReturnConflict()
    {
        // Arrange
        var teamSlug = $"real-db-test-duplicate-{DateTime.Now:yyyyMMddHHmmss}";
        
        // First registration
        var request1 = CreateValidRegistrationRequest(teamSlug);
        var json1 = JsonSerializer.Serialize(request1, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        var content1 = new StringContent(json1, Encoding.UTF8, "application/json");

        var response1 = await _client.PostAsync("/api/v1/TeamRegistration/register", content1);
        response1.StatusCode.Should().Be(HttpStatusCode.Created);

        // Second registration with same slug
        var request2 = CreateValidRegistrationRequest(teamSlug);
        request2.Email = "different@email.com"; // Different email but same slug
        var json2 = JsonSerializer.Serialize(request2, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        var content2 = new StringContent(json2, Encoding.UTF8, "application/json");

        // Act
        var response2 = await _client.PostAsync("/api/v1/TeamRegistration/register", content2);

        // Assert
        response2.StatusCode.Should().Be(HttpStatusCode.Conflict);
        
        var responseContent = await response2.Content.ReadAsStringAsync();
        Console.WriteLine($"Conflict response: {responseContent}");
        
        // Verify only one team exists in database
        var teamsCount = await _dbContext.Teams.CountAsync(t => t.TeamSlug == teamSlug);
        teamsCount.Should().Be(1, "Only one team should exist with the given slug");

        Console.WriteLine($"✅ Duplicate slug correctly rejected for team '{teamSlug}'");
    }

    private static TeamRegistrationRequestDto CreateValidRegistrationRequest(string teamSlug)
    {
        return new TeamRegistrationRequestDto
        {
            TeamSlug = teamSlug,
            TeamName = $"Real DB Test Team {teamSlug}",
            Password = "SecurePassword123!",
            Email = $"{teamSlug}@realdbtest.com",
            Phone = "+1234567890",
            NotificationURL = $"https://{teamSlug}.test.com/webhook",
            SuccessURL = $"https://{teamSlug}.test.com/success",
            FailURL = $"https://{teamSlug}.test.com/fail",
            SupportedCurrencies = "RUB,USD,EUR",
            BusinessInfo = new Dictionary<string, string>
            {
                ["description"] = $"Real database test for {teamSlug}",
                ["businessType"] = "test"
            },
            AcceptTerms = true
        };
    }
}