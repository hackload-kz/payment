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
                Password = "demo123",
                IsActive = true,
                NotificationUrl = "https://webhook.site/demo-notifications",
                SuccessUrl = "https://demo.example.com/success",
                FailUrl = "https://demo.example.com/fail",
                CreatedAt = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                UpdatedAt = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                CreatedBy = "SYSTEM",
                UpdatedBy = "SYSTEM"
            },
            new Team
            {
                Id = Guid.Parse("22222222-2222-2222-2222-222222222222"),
                TeamSlug = "test-team",
                TeamName = "Test Team",
                Password = "test123",
                IsActive = true,
                CreatedAt = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                UpdatedAt = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                CreatedBy = "SYSTEM",
                UpdatedBy = "SYSTEM"
            }
        };

        foreach (var team in teams)
        {
            modelBuilder.Entity<Team>().HasData(team);
        }
    }

}