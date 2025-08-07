using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using PaymentGateway.Core.DTOs.TeamRegistration;

namespace PaymentGateway.Tests.Manual;

/// <summary>
/// Test client for testing against a live running PaymentGateway application
/// Run this against your actual running app for end-to-end testing
/// </summary>
public class LiveAppTestClient
{
    private readonly HttpClient _httpClient;
    private readonly string _baseUrl;
    private readonly string _adminToken;

    public LiveAppTestClient(string baseUrl = "https://localhost:7001", string adminToken = "admin_token_2025_hackload_payment_gateway_secure_key_dev_only")
    {
        _baseUrl = baseUrl;
        _adminToken = adminToken;
        
        _httpClient = new HttpClient()
        {
            BaseAddress = new Uri(baseUrl)
        };
        
        // Add admin token for authentication
        _httpClient.DefaultRequestHeaders.Add("X-Admin-Token", adminToken);
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    /// <summary>
    /// Test team registration with live app
    /// </summary>
    public async Task<(bool Success, string Message, string? Response)> TestTeamRegistration(string teamSlug)
    {
        try
        {
            Console.WriteLine($"🔄 Testing team registration for: {teamSlug}");
            
            var request = new TeamRegistrationRequestDto
            {
                TeamSlug = teamSlug,
                TeamName = $"Test Team {teamSlug}",
                Password = "SecurePassword123!",
                Email = $"{teamSlug}@example.com",
                Phone = "+1234567890",
                SuccessURL = $"https://{teamSlug}.example.com/success",
                FailURL = $"https://{teamSlug}.example.com/fail",
                NotificationURL = $"https://{teamSlug}.example.com/webhook",
                SupportedCurrencies = "RUB,USD,EUR",
                BusinessInfo = new Dictionary<string, string>
                {
                    ["businessType"] = "ecommerce",
                    ["description"] = $"Test business for {teamSlug}"
                },
                AcceptTerms = true
            };

            var json = JsonSerializer.Serialize(request, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync("/api/v1/TeamRegistration/register", content);
            var responseContent = await response.Content.ReadAsStringAsync();

            Console.WriteLine($"📊 Status: {response.StatusCode}");
            Console.WriteLine($"📄 Response: {responseContent}");

            return (response.IsSuccessStatusCode, response.StatusCode.ToString(), responseContent);
        }
        catch (Exception ex)
        {
            return (false, ex.Message, null);
        }
    }

    /// <summary>
    /// Test team slug availability check
    /// </summary>
    public async Task<(bool Success, string Message, bool? Available)> TestSlugAvailability(string teamSlug)
    {
        try
        {
            Console.WriteLine($"🔍 Checking availability for: {teamSlug}");
            
            var response = await _httpClient.GetAsync($"/api/v1/TeamRegistration/check-availability/{teamSlug}");
            var responseContent = await response.Content.ReadAsStringAsync();

            Console.WriteLine($"📊 Status: {response.StatusCode}");
            Console.WriteLine($"📄 Response: {responseContent}");

            if (response.IsSuccessStatusCode)
            {
                var result = JsonSerializer.Deserialize<JsonElement>(responseContent, 
                    new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
                
                if (result.TryGetProperty("available", out var availableProperty))
                {
                    return (true, response.StatusCode.ToString(), availableProperty.GetBoolean());
                }
            }

            return (response.IsSuccessStatusCode, response.StatusCode.ToString(), null);
        }
        catch (Exception ex)
        {
            return (false, ex.Message, null);
        }
    }

    /// <summary>
    /// Test getting team status
    /// </summary>
    public async Task<(bool Success, string Message, string? Response)> TestGetTeamStatus(string teamSlug)
    {
        try
        {
            Console.WriteLine($"📈 Getting status for: {teamSlug}");
            
            var response = await _httpClient.GetAsync($"/api/v1/TeamRegistration/status/{teamSlug}");
            var responseContent = await response.Content.ReadAsStringAsync();

            Console.WriteLine($"📊 Status: {response.StatusCode}");
            Console.WriteLine($"📄 Response: {responseContent}");

            return (response.IsSuccessStatusCode, response.StatusCode.ToString(), responseContent);
        }
        catch (Exception ex)
        {
            return (false, ex.Message, null);
        }
    }

    /// <summary>
    /// Run comprehensive test suite against live app
    /// </summary>
    public async Task RunTestSuite()
    {
        Console.WriteLine("🚀 Starting PaymentGateway Live App Test Suite");
        Console.WriteLine($"🔗 Base URL: {_baseUrl}");
        Console.WriteLine("=" + new string('=', 50));

        var testSlug = $"test-{DateTime.Now:yyyyMMdd-HHmmss}";

        // Test 1: Check availability (should be available)
        Console.WriteLine("\n📋 Test 1: Check slug availability (new slug)");
        var (availSuccess, availMsg, isAvailable) = await TestSlugAvailability(testSlug);
        Console.WriteLine($"✅ Result: {(availSuccess && isAvailable == true ? "PASS" : "FAIL")}");

        // Test 2: Register new team
        Console.WriteLine("\n📋 Test 2: Register new team");
        var (regSuccess, regMsg, regResponse) = await TestTeamRegistration(testSlug);
        Console.WriteLine($"✅ Result: {(regSuccess ? "PASS" : "FAIL")}");

        // Test 3: Check availability (should now be taken)
        Console.WriteLine("\n📋 Test 3: Check slug availability (existing slug)");
        var (availSuccess2, availMsg2, isAvailable2) = await TestSlugAvailability(testSlug);
        Console.WriteLine($"✅ Result: {(availSuccess2 && isAvailable2 == false ? "PASS" : "FAIL")}");

        // Test 4: Get team status
        Console.WriteLine("\n📋 Test 4: Get team status");
        var (statusSuccess, statusMsg, statusResponse) = await TestGetTeamStatus(testSlug);
        Console.WriteLine($"✅ Result: {(statusSuccess ? "PASS" : "FAIL")}");

        // Test 5: Try to register duplicate (should fail)
        Console.WriteLine("\n📋 Test 5: Try to register duplicate team");
        var (dupSuccess, dupMsg, dupResponse) = await TestTeamRegistration(testSlug);
        Console.WriteLine($"✅ Result: {(!dupSuccess ? "PASS" : "FAIL")} (Expected failure)");

        Console.WriteLine("\n" + "=" + new string('=', 50));
        Console.WriteLine("🎯 Test Suite Completed!");
    }

    public void Dispose()
    {
        _httpClient?.Dispose();
    }
}

/// <summary>
/// Console application to run manual tests
/// </summary>
public class Program
{
    public static async Task Main(string[] args)
    {
        var baseUrl = args.Length > 0 ? args[0] : "https://localhost:7001";
        
        Console.WriteLine("🔧 PaymentGateway Live App Tester");
        Console.WriteLine($"Using base URL: {baseUrl}");
        Console.WriteLine("Make sure your PaymentGateway app is running!");
        
        using var client = new LiveAppTestClient(baseUrl);
        await client.RunTestSuite();
        
        Console.WriteLine("\nPress any key to exit...");
        Console.ReadKey();
    }
}