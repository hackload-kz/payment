using Payment.Gateway.Models;

namespace Payment.Gateway.Services;

public class MerchantService : IMerchantService
{
    private readonly IMerchantRepository _merchantRepository;
    private readonly ILogger<MerchantService> _logger;

    public MerchantService(IMerchantRepository merchantRepository, ILogger<MerchantService> logger)
    {
        _merchantRepository = merchantRepository;
        _logger = logger;
    }

    public async Task<Merchant?> GetMerchantAsync(string terminalKey)
    {
        try
        {
            _logger.LogDebug("Getting merchant for terminal key: {TerminalKey}", terminalKey);
            var merchant = await _merchantRepository.GetByTerminalKeyAsync(terminalKey);
            
            if (merchant != null)
            {
                _logger.LogDebug("Merchant found for terminal key: {TerminalKey}", terminalKey);
                merchant.LastLoginDate = DateTime.UtcNow;
                await _merchantRepository.UpdateAsync(merchant);
            }
            else
            {
                _logger.LogWarning("Merchant not found for terminal key: {TerminalKey}", terminalKey);
            }

            return merchant;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting merchant for terminal key: {TerminalKey}", terminalKey);
            throw;
        }
    }

    public async Task<bool> ValidateCredentialsAsync(string terminalKey, string password)
    {
        if (string.IsNullOrWhiteSpace(terminalKey) || string.IsNullOrWhiteSpace(password))
        {
            _logger.LogWarning("Invalid credentials provided - empty terminal key or password");
            return false;
        }

        try
        {
            _logger.LogDebug("Validating credentials for terminal key: {TerminalKey}", terminalKey);
            var isValid = await _merchantRepository.ValidateCredentialsAsync(terminalKey, password);
            
            if (!isValid)
            {
                _logger.LogWarning("Invalid credentials for terminal key: {TerminalKey}", terminalKey);
            }
            
            return isValid;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating credentials for terminal key: {TerminalKey}", terminalKey);
            return false;
        }
    }
}