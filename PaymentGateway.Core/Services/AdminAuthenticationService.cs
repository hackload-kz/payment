using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Security.Cryptography;
using System.Text;

namespace PaymentGateway.Core.Services;

public interface IAdminAuthenticationService
{
    bool ValidateAdminToken(string token);
    bool IsAdminTokenConfigured();
    string GetAdminTokenHeaderName();
}

public class AdminAuthenticationService : IAdminAuthenticationService
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<AdminAuthenticationService> _logger;
    private readonly string? _adminTokenHash;
    private readonly string _tokenHeaderName;

    public AdminAuthenticationService(
        IConfiguration configuration,
        ILogger<AdminAuthenticationService> logger)
    {
        _configuration = configuration;
        _logger = logger;
        
        var adminToken = _configuration["AdminAuthentication:AdminToken"];
        if (!string.IsNullOrEmpty(adminToken))
        {
            _adminTokenHash = ComputeSha256Hash(adminToken);
        }

        _tokenHeaderName = _configuration["AdminAuthentication:TokenHeaderName"] ?? "X-Admin-Token";
    }

    public bool ValidateAdminToken(string token)
    {
        if (string.IsNullOrEmpty(token) || string.IsNullOrEmpty(_adminTokenHash))
        {
            _logger.LogWarning("Admin authentication attempted with missing token or configuration");
            return false;
        }

        try
        {
            var tokenHash = ComputeSha256Hash(token);
            var isValid = _adminTokenHash.Equals(tokenHash, StringComparison.Ordinal);
            
            if (isValid)
            {
                _logger.LogInformation("Admin authentication successful");
            }
            else
            {
                _logger.LogWarning("Admin authentication failed - invalid token");
            }
            
            return isValid;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during admin token validation");
            return false;
        }
    }

    public bool IsAdminTokenConfigured()
    {
        return !string.IsNullOrEmpty(_adminTokenHash);
    }

    public string GetAdminTokenHeaderName()
    {
        return _tokenHeaderName;
    }

    private static string ComputeSha256Hash(string input)
    {
        using var sha256 = SHA256.Create();
        var bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}