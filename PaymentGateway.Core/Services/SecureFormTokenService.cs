using Microsoft.Extensions.Logging;

namespace PaymentGateway.Core.Services;

public interface ISecureFormTokenService
{
    Task<string> GenerateFormTokenAsync(string paymentId);
    Task<bool> ValidateFormTokenAsync(string token, string paymentId);
    Task InvalidateFormTokenAsync(string token);
}

public class SecureFormTokenService : ISecureFormTokenService
{
    private readonly ILogger<SecureFormTokenService> _logger;

    public SecureFormTokenService(ILogger<SecureFormTokenService> logger)
    {
        _logger = logger;
    }

    public async Task<string> GenerateFormTokenAsync(string paymentId)
    {
        _logger.LogDebug("Generating form token for payment {PaymentId}", paymentId);
        await Task.CompletedTask;
        return Convert.ToBase64String(Guid.NewGuid().ToByteArray());
    }

    public async Task<bool> ValidateFormTokenAsync(string token, string paymentId)
    {
        _logger.LogDebug("Validating form token for payment {PaymentId}", paymentId);
        await Task.CompletedTask;
        return !string.IsNullOrEmpty(token);
    }

    public async Task InvalidateFormTokenAsync(string token)
    {
        _logger.LogDebug("Invalidating form token {Token}", token);
        await Task.CompletedTask;
    }
}