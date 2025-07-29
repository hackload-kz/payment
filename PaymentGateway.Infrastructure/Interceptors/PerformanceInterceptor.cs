using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Data.Common;
using System.Diagnostics;
using System.Text;

namespace PaymentGateway.Infrastructure.Interceptors;

/// <summary>
/// Interceptor for monitoring and optimizing database performance
/// </summary>
public class PerformanceInterceptor : DbCommandInterceptor
{
    private readonly ILogger<PerformanceInterceptor> _logger;
    private readonly ConcurrentDictionary<Guid, QueryPerformanceData> _activeQueries = new();
    private readonly ConcurrentDictionary<string, QueryStatistics> _queryStatistics = new();
    private static readonly TimeSpan SlowQueryThreshold = TimeSpan.FromMilliseconds(1000);
    private static readonly TimeSpan VerySlowQueryThreshold = TimeSpan.FromMilliseconds(5000);

    public PerformanceInterceptor(ILogger<PerformanceInterceptor> logger)
    {
        _logger = logger;
    }

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        var queryId = Guid.NewGuid();
        var performanceData = new QueryPerformanceData
        {
            QueryId = queryId,
            CommandText = command.CommandText,
            Parameters = ExtractParameters(command),
            StartTime = DateTime.UtcNow,
            Stopwatch = Stopwatch.StartNew()
        };

        _activeQueries[queryId] = performanceData;

        // Log query start for very slow queries tracking
        _logger.LogDebug("Query {QueryId} started: {CommandText}", queryId, TruncateQuery(command.CommandText));

        return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
    }

    public override async ValueTask<DbDataReader> ReaderExecutedAsync(
        DbCommand command,
        CommandExecutedEventData eventData,
        DbDataReader result,
        CancellationToken cancellationToken = default)
    {
        await ProcessQueryCompletion(command, eventData);
        return await base.ReaderExecutedAsync(command, eventData, result, cancellationToken);
    }

    public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        var queryId = Guid.NewGuid();
        var performanceData = new QueryPerformanceData
        {
            QueryId = queryId,
            CommandText = command.CommandText,
            Parameters = ExtractParameters(command),
            StartTime = DateTime.UtcNow,
            Stopwatch = Stopwatch.StartNew()
        };

        _activeQueries[queryId] = performanceData;

        _logger.LogDebug("NonQuery {QueryId} started: {CommandText}", queryId, TruncateQuery(command.CommandText));

        return base.NonQueryExecutingAsync(command, eventData, result, cancellationToken);
    }

    public override async ValueTask<int> NonQueryExecutedAsync(
        DbCommand command,
        CommandExecutedEventData eventData,
        int result,
        CancellationToken cancellationToken = default)
    {
        await ProcessQueryCompletion(command, eventData);
        return await base.NonQueryExecutedAsync(command, eventData, result, cancellationToken);
    }

    public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<object> result,
        CancellationToken cancellationToken = default)
    {
        var queryId = Guid.NewGuid();
        var performanceData = new QueryPerformanceData
        {
            QueryId = queryId,
            CommandText = command.CommandText,
            Parameters = ExtractParameters(command),
            StartTime = DateTime.UtcNow,
            Stopwatch = Stopwatch.StartNew()
        };

        _activeQueries[queryId] = performanceData;

        _logger.LogDebug("Scalar {QueryId} started: {CommandText}", queryId, TruncateQuery(command.CommandText));

        return base.ScalarExecutingAsync(command, eventData, result, cancellationToken);
    }

    public override async ValueTask<object> ScalarExecutedAsync(
        DbCommand command,
        CommandExecutedEventData eventData,
        object result,
        CancellationToken cancellationToken = default)
    {
        await ProcessQueryCompletion(command, eventData);
        return await base.ScalarExecutedAsync(command, eventData, result, cancellationToken);
    }

    public override void CommandFailed(DbCommand command, CommandErrorEventData eventData)
    {
        ProcessQueryError(command, eventData.Exception);
        base.CommandFailed(command, eventData);
    }

    public override Task CommandFailedAsync(DbCommand command, CommandErrorEventData eventData, CancellationToken cancellationToken = default)
    {
        ProcessQueryError(command, eventData.Exception);
        return base.CommandFailedAsync(command, eventData, cancellationToken);
    }

    private async Task ProcessQueryCompletion(DbCommand command, CommandEventData eventData)
    {
        await Task.CompletedTask; // Make method async

        var activeQuery = _activeQueries.Values.FirstOrDefault(q => q.CommandText == command.CommandText);
        if (activeQuery == null) return;

        activeQuery.Stopwatch.Stop();
        var duration = activeQuery.Stopwatch.Elapsed;

        // Update query statistics
        var queryHash = GetQueryHash(command.CommandText);
        _queryStatistics.AddOrUpdate(queryHash,
            new QueryStatistics
            {
                QueryHash = queryHash,
                ExecutionCount = 1,
                TotalDuration = duration,
                AverageDuration = duration,
                MinDuration = duration,
                MaxDuration = duration,
                LastExecuted = DateTime.UtcNow
            },
            (key, existing) =>
            {
                var newCount = existing.ExecutionCount + 1;
                var newTotal = existing.TotalDuration + duration;
                
                return existing with
                {
                    ExecutionCount = newCount,
                    TotalDuration = newTotal,
                    AverageDuration = TimeSpan.FromTicks(newTotal.Ticks / newCount),
                    MinDuration = duration < existing.MinDuration ? duration : existing.MinDuration,
                    MaxDuration = duration > existing.MaxDuration ? duration : existing.MaxDuration,
                    LastExecuted = DateTime.UtcNow
                };
            });

        // Log performance warnings
        if (duration > VerySlowQueryThreshold)
        {
            _logger.LogWarning("Very slow query detected ({Duration}ms): {Query} | Parameters: {Parameters}",
                duration.TotalMilliseconds, 
                TruncateQuery(command.CommandText),
                activeQuery.Parameters);
        }
        else if (duration > SlowQueryThreshold)
        {
            _logger.LogInformation("Slow query detected ({Duration}ms): {Query}",
                duration.TotalMilliseconds, 
                TruncateQuery(command.CommandText));
        }

        // Clean up
        _activeQueries.TryRemove(activeQuery.QueryId, out _);
    }

    private void ProcessQueryError(DbCommand command, Exception exception)
    {
        var activeQuery = _activeQueries.Values.FirstOrDefault(q => q.CommandText == command.CommandText);
        if (activeQuery != null)
        {
            activeQuery.Stopwatch.Stop();
            _logger.LogError(exception, "Query {QueryId} failed after {Duration}ms: {Query} | Parameters: {Parameters}",
                activeQuery.QueryId,
                activeQuery.Stopwatch.ElapsedMilliseconds,
                TruncateQuery(command.CommandText),
                activeQuery.Parameters);

            _activeQueries.TryRemove(activeQuery.QueryId, out _);
        }
    }

    private string ExtractParameters(DbCommand command)
    {
        if (command.Parameters.Count == 0) return "None";

        var parameters = new StringBuilder();
        foreach (DbParameter parameter in command.Parameters)
        {
            if (parameters.Length > 0) parameters.Append(", ");
            
            var value = parameter.Value?.ToString() ?? "NULL";
            // Mask sensitive data
            if (IsSensitiveParameter(parameter.ParameterName))
            {
                value = "***MASKED***";
            }
            else if (value.Length > 100)
            {
                value = value.Substring(0, 100) + "...";
            }
            
            parameters.Append($"{parameter.ParameterName}={value}");
        }

        return parameters.ToString();
    }

    private static bool IsSensitiveParameter(string parameterName)
    {
        var sensitiveKeywords = new[] { "password", "token", "key", "secret", "card", "cvv", "pan" };
        return sensitiveKeywords.Any(keyword => 
            parameterName.Contains(keyword, StringComparison.OrdinalIgnoreCase));
    }

    private static string TruncateQuery(string query)
    {
        if (query.Length <= 200) return query;
        return query.Substring(0, 200) + "...";
    }

    private static string GetQueryHash(string query)
    {
        // Normalize query for statistics (remove parameters, extra whitespace, etc.)
        var normalized = System.Text.RegularExpressions.Regex.Replace(query, @"\s+", " ").Trim();
        normalized = System.Text.RegularExpressions.Regex.Replace(normalized, @"@\w+", "@param");
        return normalized.GetHashCode().ToString();
    }

    /// <summary>
    /// Get current query statistics for monitoring
    /// </summary>
    public IEnumerable<QueryStatistics> GetQueryStatistics()
    {
        return _queryStatistics.Values.OrderByDescending(q => q.ExecutionCount);
    }

    /// <summary>
    /// Get currently active queries for monitoring
    /// </summary>
    public IEnumerable<QueryPerformanceData> GetActiveQueries()
    {
        return _activeQueries.Values.OrderByDescending(q => q.StartTime);
    }

    /// <summary>
    /// Clear statistics (for testing or memory management)
    /// </summary>
    public void ClearStatistics()
    {
        _queryStatistics.Clear();
    }
}

/// <summary>
/// Performance data for individual query execution
/// </summary>
public class QueryPerformanceData
{
    public Guid QueryId { get; set; }
    public string CommandText { get; set; } = string.Empty;
    public string Parameters { get; set; } = string.Empty;
    public DateTime StartTime { get; set; }
    public Stopwatch Stopwatch { get; set; } = new();
}

/// <summary>
/// Aggregated statistics for query patterns
/// </summary>
public record QueryStatistics
{
    public string QueryHash { get; init; } = string.Empty;
    public int ExecutionCount { get; init; }
    public TimeSpan TotalDuration { get; init; }
    public TimeSpan AverageDuration { get; init; }
    public TimeSpan MinDuration { get; init; }
    public TimeSpan MaxDuration { get; init; }
    public DateTime LastExecuted { get; init; }
}