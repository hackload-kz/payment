using Microsoft.EntityFrameworkCore;
using Payment.Gateway.Infrastructure;
using Payment.Gateway.Models;

namespace Payment.Gateway.Services;

public class MerchantRepository : IMerchantRepository
{
    private readonly PaymentDbContext _context;

    public MerchantRepository(PaymentDbContext context)
    {
        _context = context;
    }

    public async Task<Merchant?> GetByTerminalKeyAsync(string terminalKey)
    {
        return await _context.Merchants
            .FirstOrDefaultAsync(m => m.TerminalKey == terminalKey && m.IsActive);
    }

    public async Task<bool> ValidateCredentialsAsync(string terminalKey, string password)
    {
        var merchant = await GetByTerminalKeyAsync(terminalKey);
        return merchant != null && merchant.Password == password;
    }

    public async Task<Merchant> CreateAsync(Merchant merchant)
    {
        _context.Merchants.Add(merchant);
        await _context.SaveChangesAsync();
        return merchant;
    }

    public async Task<Merchant> UpdateAsync(Merchant merchant)
    {
        _context.Merchants.Update(merchant);
        await _context.SaveChangesAsync();
        return merchant;
    }
}