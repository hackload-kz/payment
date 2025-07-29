using Microsoft.EntityFrameworkCore;
using PaymentGateway.Core.Entities;
using System.Security.Cryptography;
using System.Text;

namespace PaymentGateway.Infrastructure.Data.Seed;

public static class SeedData
{
    public static void Seed(ModelBuilder modelBuilder)
    {
        // Seed initial teams
        var teams = new[]
        {
            new Team
            {
                Id = Guid.Parse("11111111-1111-1111-1111-111111111111"),
                TeamSlug = "demo-team",
                TeamName = "Demo Team",
                PasswordHash = HashPassword("demo123"),
                IsActive = true,
                NotificationUrl = "https://webhook.site/demo-notifications",
                SuccessUrl = "https://demo.example.com/success",
                FailUrl = "https://demo.example.com/fail",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                CreatedBy = "SYSTEM",
                UpdatedBy = "SYSTEM"
            },
            new Team
            {
                Id = Guid.Parse("22222222-2222-2222-2222-222222222222"),
                TeamSlug = "test-team",
                TeamName = "Test Team",
                PasswordHash = HashPassword("test123"),
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                CreatedBy = "SYSTEM",
                UpdatedBy = "SYSTEM"
            }
        };

        foreach (var team in teams)
        {
            modelBuilder.Entity<Team>().HasData(team);
        }
    }

    private static string HashPassword(string password)
    {
        using var sha256 = SHA256.Create();
        var hashedBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(password));
        return Convert.ToHexString(hashedBytes).ToLower();
    }
}