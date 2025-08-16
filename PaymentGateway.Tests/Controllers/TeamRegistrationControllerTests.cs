using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using PaymentGateway.API.Controllers;
using PaymentGateway.Core.DTOs.TeamRegistration;
using PaymentGateway.Core.Services;
using PaymentGateway.Core.Entities;

namespace PaymentGateway.Tests.Controllers;

[TestFixture]
public class TeamRegistrationControllerTests
{
    private Mock<ITeamRegistrationService> _mockTeamService;
    private Mock<IAdminAuthenticationService> _mockAdminAuth;
    private Mock<ILogger<TeamRegistrationController>> _mockLogger;
    private TeamRegistrationController _controller;

    [SetUp]
    public void SetUp()
    {
        _mockTeamService = new Mock<ITeamRegistrationService>();
        _mockAdminAuth = new Mock<IAdminAuthenticationService>();
        _mockLogger = new Mock<ILogger<TeamRegistrationController>>();
        _controller = new TeamRegistrationController(_mockTeamService.Object, _mockAdminAuth.Object, _mockLogger.Object);
        
        // Setup HttpContext for admin authentication
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers["X-Admin-Token"] = "admin_token_2025_hackload_payment_gateway_secure_key_dev_only";
        _controller.ControllerContext = new ControllerContext()
        {
            HttpContext = httpContext
        };
    }

    [TearDown]
    public void TearDown()
    {
        // Controller doesn't implement IDisposable
    }

    #region RegisterTeam Tests

    [Test]
    public async Task TC_TEAM_001_RegisterTeam_WithValidRequest_ShouldReturnOk()
    {
        // Arrange
        var request = CreateValidRegistrationRequest();
        var expectedResponse = CreateSuccessfulRegistrationResponse();

        _mockTeamService
            .Setup(x => x.RegisterTeamAsync(It.IsAny<TeamRegistrationRequestDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResponse);

        // Act
        var result = await _controller.RegisterTeam(request);

        // Assert
        result.Should().BeOfType<ActionResult<TeamRegistrationResponseDto>>();
        var createdResult = result.Result.Should().BeOfType<CreatedAtActionResult>().Subject;
        var response = createdResult.Value.Should().BeOfType<TeamRegistrationResponseDto>().Subject;
        
        response.Success.Should().BeTrue();
        response.TeamSlug.Should().Be(request.TeamSlug);
        response.Message.Should().Contain("successfully registered");
    }

    [Test]
    public async Task TC_TEAM_002_RegisterTeam_WithDuplicateTeamSlug_ShouldReturnConflict()
    {
        // Arrange
        var request = CreateValidRegistrationRequest();
        var expectedResponse = CreateErrorResponse("1001", "Team slug already exists");

        _mockTeamService
            .Setup(x => x.RegisterTeamAsync(It.IsAny<TeamRegistrationRequestDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResponse);

        // Act
        var result = await _controller.RegisterTeam(request);

        // Assert
        result.Should().BeOfType<ActionResult<TeamRegistrationResponseDto>>();
        var conflictResult = result.Result.Should().BeOfType<ConflictObjectResult>().Subject;
        var response = conflictResult.Value.Should().BeOfType<TeamRegistrationResponseDto>().Subject;
        
        response.Success.Should().BeFalse();
        response.ErrorCode.Should().Be("2002");
        response.Message.Should().Contain("Team slug already exists");
    }

    [Test]
    public async Task TC_TEAM_003_RegisterTeam_WithDuplicateEmail_ShouldReturnConflict()
    {
        // Arrange
        var request = CreateValidRegistrationRequest();
        var expectedResponse = CreateErrorResponse("1002", "Email already registered");

        _mockTeamService
            .Setup(x => x.RegisterTeamAsync(It.IsAny<TeamRegistrationRequestDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResponse);

        // Act
        var result = await _controller.RegisterTeam(request);

        // Assert
        result.Should().BeOfType<ActionResult<TeamRegistrationResponseDto>>();
        var conflictResult = result.Result.Should().BeOfType<ConflictObjectResult>().Subject;
        var response = conflictResult.Value.Should().BeOfType<TeamRegistrationResponseDto>().Subject;
        
        response.Success.Should().BeFalse();
        response.ErrorCode.Should().Be("2003");
        response.Message.Should().Contain("Email already registered");
    }

    [Test]
    public async Task TC_TEAM_004_RegisterTeam_WithInvalidModelState_ShouldReturnBadRequest()
    {
        // Arrange
        var request = CreateValidRegistrationRequest();
        request.TeamSlug = ""; // Invalid empty team slug
        _controller.ModelState.AddModelError("TeamSlug", "TeamSlug is required");

        // Act
        var result = await _controller.RegisterTeam(request);

        // Assert
        result.Should().BeOfType<ActionResult<TeamRegistrationResponseDto>>();
        var badRequestResult = result.Result.Should().BeOfType<BadRequestObjectResult>().Subject;
        var response = badRequestResult.Value.Should().BeOfType<TeamRegistrationResponseDto>().Subject;
        
        response.Success.Should().BeFalse();
        response.ErrorCode.Should().Be("2001");
        response.Message.Should().Contain("Validation failed");
    }

    [Test]
    public async Task TC_TEAM_005_RegisterTeam_WithServiceException_ShouldReturnInternalServerError()
    {
        // Arrange
        var request = CreateValidRegistrationRequest();

        _mockTeamService
            .Setup(x => x.RegisterTeamAsync(It.IsAny<TeamRegistrationRequestDto>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Database connection failed"));

        // Act
        var result = await _controller.RegisterTeam(request);

        // Assert
        result.Should().BeOfType<ActionResult<TeamRegistrationResponseDto>>();
        var serverErrorResult = result.Result.Should().BeOfType<ObjectResult>().Subject;
        serverErrorResult.StatusCode.Should().Be(500);
        
        var response = serverErrorResult.Value.Should().BeOfType<TeamRegistrationResponseDto>().Subject;
        response.Success.Should().BeFalse();
        response.ErrorCode.Should().Be("9999");
        response.Message.Should().Be("Internal error");
    }

    [Test]
    public async Task TC_TEAM_006_RegisterTeam_WithNullRequest_ShouldReturnBadRequest()
    {
        // Arrange
        TeamRegistrationRequestDto request = null!;

        // Act
        var result = await _controller.RegisterTeam(request);

        // Assert
        result.Should().BeOfType<ActionResult<TeamRegistrationResponseDto>>();
        var badRequestResult = result.Result.Should().BeOfType<BadRequestObjectResult>().Subject;
        var response = badRequestResult.Value.Should().BeOfType<TeamRegistrationResponseDto>().Subject;
        
        response.Success.Should().BeFalse();
        response.ErrorCode.Should().Be("2001");
        response.Message.Should().Contain("Invalid request");
    }

    [Test]
    public async Task TC_TEAM_007_RegisterTeam_WithWeakPassword_ShouldReturnBadRequest()
    {
        // Arrange
        var request = CreateValidRegistrationRequest();
        request.Password = "weak"; // Too short password
        
        var expectedResponse = CreateErrorResponse("1003", "Password does not meet security requirements");

        _mockTeamService
            .Setup(x => x.RegisterTeamAsync(It.IsAny<TeamRegistrationRequestDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResponse);

        // Act
        var result = await _controller.RegisterTeam(request);

        // Assert
        result.Should().BeOfType<ActionResult<TeamRegistrationResponseDto>>();
        var badRequestResult = result.Result.Should().BeOfType<BadRequestObjectResult>().Subject;
        var response = badRequestResult.Value.Should().BeOfType<TeamRegistrationResponseDto>().Subject;
        
        response.Success.Should().BeFalse();
        response.ErrorCode.Should().Be("2001");
        response.Message.Should().Contain("Business validation failed");
    }

    #endregion

    #region CheckAvailability Tests

    [Test]
    public async Task TC_TEAM_008_CheckAvailability_WithAvailableSlug_ShouldReturnOk()
    {
        // Arrange
        var teamSlug = "available-team-slug";
        
        _mockTeamService
            .Setup(x => x.IsTeamSlugAvailableAsync(teamSlug, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var result = await _controller.CheckTeamSlugAvailability(teamSlug);

        // Assert
        result.Should().BeOfType<ActionResult>();
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var response = okResult.Value.Should().BeAssignableTo<object>().Subject;
        
        response.Should().BeEquivalentTo(new { available = true, teamSlug = teamSlug });
    }

    [Test]
    public async Task TC_TEAM_009_CheckAvailability_WithTakenSlug_ShouldReturnOk()
    {
        // Arrange
        var teamSlug = "taken-team-slug";
        
        _mockTeamService
            .Setup(x => x.IsTeamSlugAvailableAsync(teamSlug, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        // Act
        var result = await _controller.CheckTeamSlugAvailability(teamSlug);

        // Assert
        result.Should().BeOfType<ActionResult>();
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var response = okResult.Value.Should().BeAssignableTo<object>().Subject;
        
        response.Should().BeEquivalentTo(new { available = false, teamSlug = teamSlug });
    }

    [Test]
    public async Task TC_TEAM_010_CheckAvailability_WithEmptySlug_ShouldReturnBadRequest()
    {
        // Arrange
        var teamSlug = "";

        // Act
        var result = await _controller.CheckTeamSlugAvailability(teamSlug);

        // Assert
        result.Should().BeOfType<ActionResult>();
        var badRequestResult = result.Should().BeOfType<BadRequestObjectResult>().Subject;
        var response = badRequestResult.Value.Should().BeAssignableTo<object>().Subject;
        
        response.Should().BeEquivalentTo(new { error = "TeamSlug is required" });
    }

    [Test]
    public async Task TC_TEAM_011_CheckAvailability_WithServiceException_ShouldReturnInternalServerError()
    {
        // Arrange
        var teamSlug = "test-slug";
        
        _mockTeamService
            .Setup(x => x.IsTeamSlugAvailableAsync(teamSlug, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Database connection failed"));

        // Act
        var result = await _controller.CheckTeamSlugAvailability(teamSlug);

        // Assert
        result.Should().BeOfType<ActionResult>();
        var serverErrorResult = result.Should().BeOfType<ObjectResult>().Subject;
        serverErrorResult.StatusCode.Should().Be(500);
        
        var response = serverErrorResult.Value.Should().BeAssignableTo<object>().Subject;
        response.Should().BeEquivalentTo(new { error = "Internal server error" });
    }

    #endregion

    #region GetStatus Tests

    [Test]
    public async Task TC_TEAM_012_GetStatus_WithExistingTeam_ShouldReturnOk()
    {
        // Arrange
        var teamSlug = "existing-team";
        var expectedTeam = new Team
        {
            Id = Guid.NewGuid(),
            TeamSlug = teamSlug,
            TeamName = "Test Team",
            IsActive = true,
            CreatedAt = DateTime.UtcNow.AddDays(-1),
            UpdatedAt = DateTime.UtcNow,
            ContactEmail = "test@example.com",
            SupportedCurrencies = new List<string> { "RUB", "USD" }
        };

        _mockTeamService
            .Setup(x => x.GetTeamStatusAsync(teamSlug, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedTeam);

        // Act
        var result = await _controller.GetTeamStatus(teamSlug);

        // Assert
        result.Should().BeOfType<ActionResult>();
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var response = okResult.Value.Should().BeAssignableTo<object>().Subject;
        
        response.Should().NotBeNull();
        var teamSlugProperty = response.GetType().GetProperty("TeamSlug")?.GetValue(response);
        teamSlugProperty.Should().Be(teamSlug);
    }

    [Test]
    public async Task TC_TEAM_013_GetStatus_WithNonExistentTeam_ShouldReturnNotFound()
    {
        // Arrange
        var teamSlug = "non-existent-team";
        
        _mockTeamService
            .Setup(x => x.GetTeamStatusAsync(teamSlug, It.IsAny<CancellationToken>()))
            .ReturnsAsync(null as Team);

        // Act
        var result = await _controller.GetTeamStatus(teamSlug);

        // Assert
        result.Should().BeOfType<ActionResult>();
        var notFoundResult = result.Should().BeOfType<NotFoundObjectResult>().Subject;
        var response = notFoundResult.Value.Should().BeAssignableTo<object>().Subject;
        
        response.Should().BeEquivalentTo(new { error = "Team not found", teamSlug = teamSlug });
    }

    [Test]
    public async Task TC_TEAM_014_GetStatus_WithEmptySlug_ShouldReturnBadRequest()
    {
        // Arrange
        var teamSlug = "";

        // Act
        var result = await _controller.GetTeamStatus(teamSlug);

        // Assert
        result.Should().BeOfType<ActionResult>();
        var badRequestResult = result.Should().BeOfType<BadRequestObjectResult>().Subject;
        var response = badRequestResult.Value.Should().BeAssignableTo<object>().Subject;
        
        response.Should().BeEquivalentTo(new { error = "TeamSlug is required" });
    }

    #endregion

    #region Private Helper Methods

    private static TeamRegistrationRequestDto CreateValidRegistrationRequest()
    {
        return new TeamRegistrationRequestDto
        {
            TeamSlug = "my-online-store",
            TeamName = "My Online Store",
            Password = "SecurePassword123!",
            Email = "merchant@mystore.com",
            Phone = "+1234567890",
            SuccessURL = "https://mystore.com/payment/success",
            FailURL = "https://mystore.com/payment/fail",
            NotificationURL = "https://mystore.com/payment/webhook",
            SupportedCurrencies = "USD,EUR,RUB",
            BusinessInfo = new Dictionary<string, string> 
            {
                ["businessType"] = "ecommerce",
                ["description"] = "Online retail store for electronics"
            },
            AcceptTerms = true
        };
    }

    private static TeamRegistrationResponseDto CreateSuccessfulRegistrationResponse()
    {
        return new TeamRegistrationResponseDto
        {
            Success = true,
            TeamSlug = "my-online-store",
            Message = "Team successfully registered with payment gateway",
            TeamId = Guid.NewGuid(),
            Details = new TeamRegistrationDetailsDto
            {
                NextSteps = new[]
                {
                    "Save your API credentials in a secure location",
                    "Test your integration using the sandbox environment",
                    "Configure webhook endpoints to receive payment notifications",
                    "Review our API documentation for implementation details"
                }
            }
        };
    }

    private static TeamRegistrationResponseDto CreateErrorResponse(string errorCode, string message)
    {
        return new TeamRegistrationResponseDto
        {
            Success = false,
            ErrorCode = errorCode,
            Message = message,
            TeamId = Guid.NewGuid(),
            TeamSlug = "",
            Details = new TeamRegistrationDetailsDto
            {
                NextSteps = new string[0]
            }
        };
    }

    #endregion

    #region Data Validation Tests

    [Test]
    [TestCase("", false, "Empty team slug should be invalid")]
    [TestCase("ab", false, "Team slug too short should be invalid")]
    [TestCase("my-store", true, "Valid team slug with hyphens should be valid")]
    [TestCase("my_store_123", true, "Valid team slug with underscores and numbers should be valid")]
    [TestCase("MyStore", true, "Valid team slug with mixed case should be valid")]
    [TestCase("my store", false, "Team slug with spaces should be invalid")]
    [TestCase("my@store", false, "Team slug with special characters should be invalid")]
    public void TC_TEAM_015_ValidateTeamSlug_WithVariousInputs_ShouldReturnExpectedResult(
        string teamSlug, bool expectedValid, string testDescription)
    {
        // Act
        var isValid = IsValidTeamSlug(teamSlug);

        // Assert
        isValid.Should().Be(expectedValid, testDescription);
    }

    [Test]
    public void TC_TEAM_015b_ValidateTeamSlug_WithTooLongString_ShouldReturnFalse()
    {
        // Arrange
        var longTeamSlug = new string('a', 51); // 51 characters - too long

        // Act
        var isValid = IsValidTeamSlug(longTeamSlug);

        // Assert
        isValid.Should().BeFalse("Team slug too long should be invalid");
    }

    [Test]
    [TestCase("password", false, "Password too short should be invalid")]
    [TestCase("Password123", true, "Password with mixed case and numbers should be valid")]
    [TestCase("password123", false, "Password without uppercase should be invalid")]
    [TestCase("PASSWORD123", false, "Password without lowercase should be invalid")]
    [TestCase("Password", false, "Password without numbers should be invalid")]
    [TestCase("Pa1", false, "Password too short even with mixed case should be invalid")]
    [TestCase("VeryLongPasswordWith123", true, "Long password with requirements should be valid")]
    public void TC_TEAM_016_ValidatePassword_WithVariousInputs_ShouldReturnExpectedResult(
        string password, bool expectedValid, string testDescription)
    {
        // Act
        var isValid = IsValidPassword(password);

        // Assert
        isValid.Should().Be(expectedValid, testDescription);
    }

    [Test]
    [TestCase("test@example.com", true, "Valid email should be valid")]
    [TestCase("user.name@domain.co.uk", true, "Valid email with subdomain should be valid")]
    [TestCase("invalid-email", false, "Invalid email without @ should be invalid")]
    [TestCase("@domain.com", false, "Email without username should be invalid")]
    [TestCase("user@", false, "Email without domain should be invalid")]
    [TestCase("", false, "Empty email should be invalid")]
    public void TC_TEAM_017_ValidateEmail_WithVariousInputs_ShouldReturnExpectedResult(
        string email, bool expectedValid, string testDescription)
    {
        // Act
        var isValid = IsValidEmail(email);

        // Assert
        isValid.Should().Be(expectedValid, testDescription);
    }

    // Helper validation methods
    private static bool IsValidTeamSlug(string teamSlug) =>
        !string.IsNullOrWhiteSpace(teamSlug) &&
        teamSlug.Length >= 3 &&
        teamSlug.Length <= 50 &&
        System.Text.RegularExpressions.Regex.IsMatch(teamSlug, @"^[a-zA-Z0-9\-_]+$");

    private static bool IsValidPassword(string password) =>
        !string.IsNullOrWhiteSpace(password) &&
        password.Length >= 8 &&
        password.Length <= 100 &&
        System.Text.RegularExpressions.Regex.IsMatch(password, @"^(?=.*[a-z])(?=.*[A-Z])(?=.*\d).+$");

    private static bool IsValidEmail(string email) =>
        !string.IsNullOrWhiteSpace(email) &&
        System.Text.RegularExpressions.Regex.IsMatch(email, 
            @"^[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}$");

    #endregion
}