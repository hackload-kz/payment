using Payment.Gateway.Models;

namespace Payment.Gateway.Services;

public interface IMerchantService
{
    Task<Merchant?> GetMerchantAsync(string terminalKey);
    Task<bool> ValidateCredentialsAsync(string terminalKey, string password);
}