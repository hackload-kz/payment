namespace Payment.Gateway.Services;

public interface ITokenGenerationService
{
    string GenerateToken(IDictionary<string, object> parameters, string password);
    bool ValidateToken(IDictionary<string, object> parameters, string token, string password);
}