using System.Security.Cryptography;
using System.Text;

namespace Payment.Gateway.Services;

public class TokenGenerationService : ITokenGenerationService
{
    public string GenerateToken(IDictionary<string, object> parameters, string password)
    {
        // Implementation will be added in Task 2
        throw new NotImplementedException("Will be implemented in Task 2");
    }

    public bool ValidateToken(IDictionary<string, object> parameters, string token, string password)
    {
        // Implementation will be added in Task 2
        throw new NotImplementedException("Will be implemented in Task 2");
    }
}