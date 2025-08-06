namespace PaymentGateway.Core.Configuration;

public class LockConfiguration
{
    public TimeSpan DefaultTimeout { get; set; } = TimeSpan.FromSeconds(30);
    public TimeSpan RetryInterval { get; set; } = TimeSpan.FromMilliseconds(100);
    public TimeSpan CleanupInterval { get; set; } = TimeSpan.FromMinutes(5);
}