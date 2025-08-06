using Microsoft.Extensions.Logging;

namespace PaymentGateway.Core.Services;

public interface ITokenGenerationService
{
    Task<string> GenerateTokenAsync(string userId);
    Task<bool> ValidateTokenAsync(string token);
    Task<string> RefreshTokenAsync(string oldToken);
}

public class TokenGenerationService : ITokenGenerationService
{
    private readonly ILogger<TokenGenerationService> _logger;

    public TokenGenerationService(ILogger<TokenGenerationService> logger)
    {
        _logger = logger;
    }

    public async Task<string> GenerateTokenAsync(string userId)
    {
        _logger.LogDebug("Generating token for user {UserId}", userId);
        await Task.CompletedTask;
        return Convert.ToBase64String(Guid.NewGuid().ToByteArray());
    }

    public async Task<bool> ValidateTokenAsync(string token)
    {
        _logger.LogDebug("Validating token");
        await Task.CompletedTask;
        return !string.IsNullOrEmpty(token);
    }

    public async Task<string> RefreshTokenAsync(string oldToken)
    {
        _logger.LogDebug("Refreshing token");
        await Task.CompletedTask;
        return Convert.ToBase64String(Guid.NewGuid().ToByteArray());
    }
}