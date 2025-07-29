namespace PaymentGateway.Core.Interfaces;

public interface IConnectionPoolMonitoringService
{
    Task<ConnectionPoolMetrics> GetMetricsAsync(CancellationToken cancellationToken = default);
    Task StartMonitoringAsync(CancellationToken cancellationToken = default);
    Task StopMonitoringAsync(CancellationToken cancellationToken = default);
    bool IsMonitoring { get; }
}

public class ConnectionPoolMetrics
{
    public int ActiveConnections { get; set; }
    public int IdleConnections { get; set; }
    public int TotalConnections { get; set; }
    public int MaxPoolSize { get; set; }
    public double ConnectionUtilization { get; set; }
    public TimeSpan AverageConnectionAge { get; set; }
    public bool IsHealthy { get; set; } = true;
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}