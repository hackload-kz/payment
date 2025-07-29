namespace PaymentGateway.Core.Interfaces;

// Database optimization service interfaces
public interface IDatabaseIndexingService
{
    Task AnalyzeQueryPatternsAsync(CancellationToken cancellationToken = default);
    Task OptimizeIndexesAsync(CancellationToken cancellationToken = default);
    Task<IndexAnalysisResult> GetIndexAnalysisAsync(CancellationToken cancellationToken = default);
    Task CreateMissingIndexesAsync(CancellationToken cancellationToken = default);
    Task DropUnusedIndexesAsync(CancellationToken cancellationToken = default);
    Task<List<IndexRecommendation>> GetIndexRecommendationsAsync(CancellationToken cancellationToken = default);
}

public interface IQueryOptimizationService
{
    Task<QueryExecutionPlan> AnalyzeQueryPlanAsync(string query, CancellationToken cancellationToken = default);
    Task<List<QueryOptimizationSuggestion>> GetOptimizationSuggestionsAsync(CancellationToken cancellationToken = default);
    Task<QueryPerformanceReport> GeneratePerformanceReportAsync(CancellationToken cancellationToken = default);
    Task OptimizeSlowQueriesAsync(CancellationToken cancellationToken = default);
    Task<List<QueryStatistics>> GetTopSlowQueriesAsync(int limit = 50, CancellationToken cancellationToken = default);
    Task UpdateQueryStatisticsAsync(CancellationToken cancellationToken = default);
    Task<QueryOptimizationResult> OptimizeQueryAsync(string originalQuery, CancellationToken cancellationToken = default);
}

public interface IDatabaseConnectionPoolingService
{
    Task<ConnectionPoolStatistics> GetPoolStatisticsAsync(CancellationToken cancellationToken = default);
    Task<List<ConnectionPoolHealth>> GetPoolHealthAsync(CancellationToken cancellationToken = default);
    Task OptimizeConnectionPoolsAsync(CancellationToken cancellationToken = default);
    Task<ConnectionPoolRecommendations> GetPoolRecommendationsAsync(CancellationToken cancellationToken = default);
    Task MonitorConnectionUsageAsync(CancellationToken cancellationToken = default);
    Task ClearIdleConnectionsAsync(CancellationToken cancellationToken = default);
    Task<ConnectionPoolConfiguration> GetOptimalConfigurationAsync(CancellationToken cancellationToken = default);
}

public interface IDatabaseMonitoringService
{
    Task<DatabasePerformanceMetrics> GetPerformanceMetricsAsync(CancellationToken cancellationToken = default);
    Task<List<DatabaseAlert>> CheckAlertsAsync(CancellationToken cancellationToken = default);
    Task<DatabaseHealthReport> GenerateHealthReportAsync(CancellationToken cancellationToken = default);
    Task StartMonitoringAsync(CancellationToken cancellationToken = default);
    Task StopMonitoringAsync(CancellationToken cancellationToken = default);
    Task<List<SlowQueryReport>> GetSlowQueriesAsync(int limit = 50, CancellationToken cancellationToken = default);
    Task<DatabaseResourceUsage> GetResourceUsageAsync(CancellationToken cancellationToken = default);
}

public interface IQueryCacheService
{
    Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default) where T : class;
    Task SetAsync<T>(string key, T value, TimeSpan? expiration = null, CancellationToken cancellationToken = default) where T : class;
    Task RemoveAsync(string key, CancellationToken cancellationToken = default);
    Task RemoveByPatternAsync(string pattern, CancellationToken cancellationToken = default);
    Task<CacheStatistics> GetStatisticsAsync(CancellationToken cancellationToken = default);
    Task ClearAsync(CancellationToken cancellationToken = default);
    Task<T> GetOrSetAsync<T>(string key, Func<Task<T>> factory, TimeSpan? expiration = null, CancellationToken cancellationToken = default) where T : class;
    string GenerateKey(string operation, params object[] parameters);
}

// Supporting classes for database optimization
public class IndexAnalysisResult
{
    public int TotalIndexes { get; set; }
    public List<IndexInfo> UnusedIndexes { get; set; } = new();
    public List<DuplicateIndexInfo> DuplicateIndexes { get; set; } = new();
    public List<MissingIndexRecommendation> MissingIndexes { get; set; } = new();
}

public class IndexRecommendation
{
    public string TableName { get; set; } = string.Empty;
    public List<string> ColumnNames { get; set; } = new();
    public string IndexName { get; set; } = string.Empty;
    public IndexPriority Priority { get; set; }
    public double ImpactScore { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string CreateScript { get; set; } = string.Empty;
}

public class IndexInfo
{
    public string SchemaName { get; set; } = string.Empty;
    public string IndexName { get; set; } = string.Empty;
    public string TableName { get; set; } = string.Empty;
    public long ScanCount { get; set; }
    public bool IsEssential { get; set; }
}

public class DuplicateIndexInfo
{
    public string SchemaName { get; set; } = string.Empty;
    public string TableName { get; set; } = string.Empty;
    public List<string> DuplicateIndexes { get; set; } = new();
    public List<string> Columns { get; set; } = new();
}

public class MissingIndexRecommendation
{
    public string TableName { get; set; } = string.Empty;
    public string ColumnName { get; set; } = string.Empty;
    public string SuggestedIndexName { get; set; } = string.Empty;
    public string CreateScript { get; set; } = string.Empty;
}

public class QueryExecutionPlan
{
    public double TotalExecutionTime { get; set; }
    public double PlanningTime { get; set; }
    public bool HasSequentialScans { get; set; }
    public bool HasNestedLoops { get; set; }
    public long EstimatedRows { get; set; }
    public List<string> PerformanceIssues { get; set; } = new();
}

public class QueryOptimizationSuggestion
{
    public QueryOptimizationType Type { get; set; }
    public OptimizationPriority Priority { get; set; }
    public string Description { get; set; } = string.Empty;
    public string Suggestion { get; set; } = string.Empty;
    public double PotentialImprovement { get; set; }
    public string? QueryId { get; set; }
    public string? TableName { get; set; }
    public double EstimatedTimeReduction { get; set; }
}

public class QueryPerformanceReport
{
    public DateTime GeneratedAt { get; set; }
    public long TotalQueries { get; set; }
    public double AverageExecutionTime { get; set; }
    public long SlowQueryCount { get; set; }
    public List<QueryStatistics> TopSlowQueries { get; set; } = new();
}

public class QueryStatistics
{
    public string QueryId { get; set; } = string.Empty;
    public string Query { get; set; } = string.Empty;
    public long Calls { get; set; }
    public double TotalExecTime { get; set; }
    public double MeanExecTime { get; set; }
    public double StddevExecTime { get; set; }
    public long Rows { get; set; }
    public double HitPercent { get; set; }
}

public class QueryOptimizationResult
{
    public string OriginalQuery { get; set; } = string.Empty;
    public string OptimizedQuery { get; set; } = string.Empty;
    public double OriginalExecutionTime { get; set; }
    public double OptimizedExecutionTime { get; set; }
    public bool IsOptimized { get; set; }
    public double PerformanceImprovement { get; set; }
    public List<string> OptimizationDetails { get; set; } = new();
}

public class ConnectionPoolStatistics
{
    public DateTime Timestamp { get; set; }
    public string DatabaseName { get; set; } = string.Empty;
    public int ActiveConnections { get; set; }
    public long TransactionsCommitted { get; set; }
    public long TransactionsRolledBack { get; set; }
    public double CacheHitRatio { get; set; }
    public double TransactionSuccessRate { get; set; }
}

public class ConnectionPoolHealth
{
    public string PoolName { get; set; } = string.Empty;
    public string ConnectionString { get; set; } = string.Empty;
    public bool IsHealthy { get; set; }
    public int HealthScore { get; set; }
    public List<string> Issues { get; set; } = new();
    public DateTime LastChecked { get; set; }
}

public class ConnectionPoolRecommendations
{
    public DateTime GeneratedAt { get; set; }
    public List<PoolRecommendation> Recommendations { get; set; } = new();
}

public class PoolRecommendation
{
    public OptimizationType Type { get; set; }
    public RecommendationPriority Priority { get; set; }
    public string Description { get; set; } = string.Empty;
    public string Suggestion { get; set; } = string.Empty;
    public string EstimatedImpact { get; set; } = string.Empty;
    public string? CurrentValue { get; set; }
    public string? RecommendedValue { get; set; }
}

public class ConnectionPoolConfiguration
{
    public DateTime GeneratedAt { get; set; }
    public bool BasedOnMetrics { get; set; }
    public int MinPoolSize { get; set; }
    public int MaxPoolSize { get; set; }
    public TimeSpan CommandTimeout { get; set; }
    public TimeSpan ConnectionTimeout { get; set; }
    public TimeSpan ConnectionIdleLifetime { get; set; }
    public TimeSpan ConnectionLifetime { get; set; }
}

public class DatabasePerformanceMetrics
{
    public DateTime Timestamp { get; set; }
    public long DatabaseSizeBytes { get; set; }
    public int MaxConnections { get; set; }
    public int TotalConnections { get; set; }
    public int ActiveConnections { get; set; }
    public int IdleConnections { get; set; }
    public double CacheHitRatio { get; set; }
    public long TransactionsCommitted { get; set; }
    public long TransactionsRolledBack { get; set; }
    public long TransactionsPerSecond { get; set; }
    public double AverageQueryExecutionTime { get; set; }
    public int SlowQueryCount { get; set; }
}

public class DatabaseAlert
{
    public AlertType Type { get; set; }
    public AlertSeverity Severity { get; set; }
    public string Message { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
    public double Threshold { get; set; }
    public double CurrentValue { get; set; }
}

public class DatabaseHealthReport
{
    public DateTime GeneratedAt { get; set; }
    public int HealthScore { get; set; }
    public HealthStatus HealthStatus { get; set; }
    public List<DatabaseAlert> ActiveAlerts { get; set; } = new();
    public List<string> Recommendations { get; set; } = new();
    public DatabaseResourceUsage ResourceUsage { get; set; } = new();
}

public class SlowQueryReport
{
    public string QueryId { get; set; } = string.Empty;
    public string QueryText { get; set; } = string.Empty;
    public long Calls { get; set; }
    public double TotalExecutionTime { get; set; }
    public double MeanExecutionTime { get; set; }
    public double MaxExecutionTime { get; set; }
    public double StandardDeviation { get; set; }
    public long RowsReturned { get; set; }
    public double CacheHitRatio { get; set; }
}

public class DatabaseResourceUsage
{
    public long DatabaseSize { get; set; }
    public long TableSize { get; set; }
    public long IndexSize { get; set; }
    public int TotalConnections { get; set; }
    public int ActiveConnections { get; set; }
    public int IdleConnections { get; set; }
}

public class CacheStatistics
{
    public long Hits { get; set; }
    public long Misses { get; set; }
    public long Sets { get; set; }
    public long Removes { get; set; }
    public long Evictions { get; set; }
    public long Clears { get; set; }
    public long CurrentEntries { get; set; }
    public double HitRatio { get; set; }
    public long TotalRequests => Hits + Misses;
    public DateTime? LastHitAt { get; set; }
    public DateTime? LastMissAt { get; set; }
    public DateTime? LastSetAt { get; set; }
    public DateTime? LastEvictionAt { get; set; }
}

// Enums for database optimization
public enum IndexPriority
{
    Low,
    Medium,
    High,
    Critical
}

public enum QueryOptimizationType
{
    IndexOptimization,
    JoinOptimization,
    CacheOptimization,
    MaintenanceOptimization,
    StatisticsOptimization,
    ConfigurationOptimization,
    QueryRewrite
}

public enum OptimizationPriority
{
    Low,
    Medium,
    High,
    Critical
}

public enum OptimizationType
{
    PoolSizeIncrease,
    PoolSizeDecrease,
    ConnectionLifetimeIncrease,
    ConnectionLifetimeDecrease,
    TimeoutOptimization,
    CacheOptimization,
    ConnectionOptimization
}

public enum RecommendationPriority
{
    Low,
    Medium,
    High,
    Critical
}

public enum AlertType
{
    HighConnectionCount,
    LowCacheHitRatio,
    HighDatabaseSize,
    SlowQueries,
    Deadlocks,
    HighLockWaits,
    HighTempFileUsage,
    ReplicationLag,
    DiskSpaceLow
}

public enum AlertSeverity
{
    Info,
    Warning,
    Critical
}

public enum HealthStatus
{
    Critical,
    Poor,
    Fair,
    Good,
    Excellent
}