using Payment.Gateway.Models;

namespace Payment.Gateway.Services;

public class MerchantService : IMerchantService
{
    public Task<Merchant?> GetMerchantAsync(string terminalKey)
    {
        // Implementation will be added in Task 2
        throw new NotImplementedException("Will be implemented in Task 2");
    }

    public Task<bool> ValidateCredentialsAsync(string terminalKey, string password)
    {
        // Implementation will be added in Task 2
        throw new NotImplementedException("Will be implemented in Task 2");
    }
}