using Microsoft.EntityFrameworkCore;
using Payment.Gateway.Models;

namespace Payment.Gateway.Infrastructure;

public class PaymentDbContext : DbContext
{
    public PaymentDbContext(DbContextOptions<PaymentDbContext> options) : base(options)
    {
    }

    public DbSet<PaymentEntity> Payments { get; set; }
    public DbSet<Merchant> Merchants { get; set; }
    public DbSet<PaymentStatusHistory> PaymentStatusHistory { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Payment configuration
        modelBuilder.Entity<PaymentEntity>(entity =>
        {
            entity.HasKey(e => e.PaymentId);
            entity.Property(e => e.PaymentId).HasMaxLength(20);
            entity.Property(e => e.OrderId).HasMaxLength(36).IsRequired();
            entity.Property(e => e.TerminalKey).HasMaxLength(20).IsRequired();
            entity.Property(e => e.Amount).IsRequired();
            entity.Property(e => e.Description).HasMaxLength(500);
            entity.Property(e => e.CustomerKey).HasMaxLength(36);
            entity.Property(e => e.PayType).HasMaxLength(1);
            entity.Property(e => e.Language).HasMaxLength(2);

            entity.HasIndex(e => e.OrderId);
            entity.HasIndex(e => e.TerminalKey);
            entity.HasIndex(e => new { e.OrderId, e.TerminalKey });
        });

        // Merchant configuration
        modelBuilder.Entity<Merchant>(entity =>
        {
            entity.HasKey(e => e.TerminalKey);
            entity.Property(e => e.TerminalKey).HasMaxLength(20);
            entity.Property(e => e.Password).HasMaxLength(50).IsRequired();
            entity.Property(e => e.IsActive).IsRequired();
        });

        // PaymentStatusHistory configuration
        modelBuilder.Entity<PaymentStatusHistory>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.PaymentId).HasMaxLength(20).IsRequired();
            entity.Property(e => e.Status).IsRequired();
            entity.Property(e => e.Timestamp).IsRequired();
            entity.Property(e => e.ErrorCode).HasMaxLength(10);

            entity.HasOne<PaymentEntity>()
                  .WithMany()
                  .HasForeignKey(e => e.PaymentId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(e => e.PaymentId);
            entity.HasIndex(e => e.Timestamp);
        });

        base.OnModelCreating(modelBuilder);
    }
}