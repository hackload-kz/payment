using Payment.Gateway.Models;

namespace Payment.Gateway.Services;

public interface IMerchantRepository
{
    Task<Merchant?> GetByTerminalKeyAsync(string terminalKey);
    Task<bool> ValidateCredentialsAsync(string terminalKey, string password);
    Task<Merchant> CreateAsync(Merchant merchant);
    Task<Merchant> UpdateAsync(Merchant merchant);
}