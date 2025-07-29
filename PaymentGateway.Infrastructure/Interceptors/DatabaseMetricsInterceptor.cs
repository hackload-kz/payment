using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using PaymentGateway.Core.Services;
using System.Data.Common;
using System.Diagnostics;

namespace PaymentGateway.Infrastructure.Interceptors;

public class DatabaseMetricsInterceptor : DbCommandInterceptor
{
    private readonly IPrometheusMetricsService _metricsService;
    private readonly ILogger<DatabaseMetricsInterceptor> _logger;

    public DatabaseMetricsInterceptor(IPrometheusMetricsService metricsService, ILogger<DatabaseMetricsInterceptor> logger)
    {
        _metricsService = metricsService;
        _logger = logger;
    }

    public override InterceptionResult<DbDataReader> ReaderExecuting(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result)
    {
        var operation = GetOperationType(command.CommandText);
        _metricsService.IncrementDatabaseOperations(operation);
        return base.ReaderExecuting(command, eventData, result);
    }

    public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        var operation = GetOperationType(command.CommandText);
        _metricsService.IncrementDatabaseOperations(operation);
        return await base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
    }

    public override DbDataReader ReaderExecuted(
        DbCommand command,
        CommandExecutedEventData eventData,
        DbDataReader result)
    {
        RecordExecutionMetrics(command, eventData);
        return base.ReaderExecuted(command, eventData, result);
    }

    public override async ValueTask<DbDataReader> ReaderExecutedAsync(
        DbCommand command,
        CommandExecutedEventData eventData,
        DbDataReader result,
        CancellationToken cancellationToken = default)
    {
        RecordExecutionMetrics(command, eventData);
        return await base.ReaderExecutedAsync(command, eventData, result, cancellationToken);
    }

    public override InterceptionResult<int> NonQueryExecuting(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<int> result)
    {
        var operation = GetOperationType(command.CommandText);
        _metricsService.IncrementDatabaseOperations(operation);
        return base.NonQueryExecuting(command, eventData, result);
    }

    public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        var operation = GetOperationType(command.CommandText);
        _metricsService.IncrementDatabaseOperations(operation);
        return await base.NonQueryExecutingAsync(command, eventData, result, cancellationToken);
    }

    public override int NonQueryExecuted(
        DbCommand command,
        CommandExecutedEventData eventData,
        int result)
    {
        RecordExecutionMetrics(command, eventData);
        return base.NonQueryExecuted(command, eventData, result);
    }

    public override async ValueTask<int> NonQueryExecutedAsync(
        DbCommand command,
        CommandExecutedEventData eventData,
        int result,
        CancellationToken cancellationToken = default)
    {
        RecordExecutionMetrics(command, eventData);
        return await base.NonQueryExecutedAsync(command, eventData, result, cancellationToken);
    }

    public override InterceptionResult<object> ScalarExecuting(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<object> result)
    {
        var operation = GetOperationType(command.CommandText);
        _metricsService.IncrementDatabaseOperations(operation);
        return base.ScalarExecuting(command, eventData, result);
    }

    public override async ValueTask<InterceptionResult<object>> ScalarExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<object> result,
        CancellationToken cancellationToken = default)
    {
        var operation = GetOperationType(command.CommandText);
        _metricsService.IncrementDatabaseOperations(operation);
        return await base.ScalarExecutingAsync(command, eventData, result, cancellationToken);
    }

    public override object? ScalarExecuted(
        DbCommand command,
        CommandExecutedEventData eventData,
        object? result)
    {
        RecordExecutionMetrics(command, eventData);
        return base.ScalarExecuted(command, eventData, result);
    }

    public override async ValueTask<object?> ScalarExecutedAsync(
        DbCommand command,
        CommandExecutedEventData eventData,
        object? result,
        CancellationToken cancellationToken = default)
    {
        RecordExecutionMetrics(command, eventData);
        return await base.ScalarExecutedAsync(command, eventData, result, cancellationToken);
    }

    public override void CommandFailed(
        DbCommand command,
        CommandErrorEventData eventData)
    {
        var operation = GetOperationType(command.CommandText);
        var errorType = GetErrorType(eventData.Exception);
        
        _metricsService.IncrementDatabaseErrors(operation, errorType);
        
        _logger.LogError(eventData.Exception, 
            "Database command failed. Operation: {Operation}, Duration: {Duration}ms", 
            operation, eventData.Duration.TotalMilliseconds);
        
        base.CommandFailed(command, eventData);
    }

    public override async Task CommandFailedAsync(
        DbCommand command,
        CommandErrorEventData eventData,
        CancellationToken cancellationToken = default)
    {
        var operation = GetOperationType(command.CommandText);
        var errorType = GetErrorType(eventData.Exception);
        
        _metricsService.IncrementDatabaseErrors(operation, errorType);
        
        _logger.LogError(eventData.Exception, 
            "Database command failed. Operation: {Operation}, Duration: {Duration}ms", 
            operation, eventData.Duration.TotalMilliseconds);
        
        await base.CommandFailedAsync(command, eventData, cancellationToken);
    }

    private void RecordExecutionMetrics(DbCommand command, CommandExecutedEventData eventData)
    {
        var operation = GetOperationType(command.CommandText);
        var duration = eventData.Duration.TotalMilliseconds;
        
        _metricsService.RecordDatabaseConnectionTime(duration, operation);
    }

    private static string GetOperationType(string commandText)
    {
        if (string.IsNullOrWhiteSpace(commandText))
            return "unknown";

        var trimmed = commandText.TrimStart().ToUpperInvariant();
        
        if (trimmed.StartsWith("SELECT"))
            return "select";
        if (trimmed.StartsWith("INSERT"))
            return "insert";
        if (trimmed.StartsWith("UPDATE"))
            return "update";
        if (trimmed.StartsWith("DELETE"))
            return "delete";
        if (trimmed.StartsWith("CREATE"))
            return "create";
        if (trimmed.StartsWith("ALTER"))
            return "alter";
        if (trimmed.StartsWith("DROP"))
            return "drop";
        if (trimmed.StartsWith("TRUNCATE"))
            return "truncate";
        if (trimmed.StartsWith("BEGIN") || trimmed.StartsWith("COMMIT") || trimmed.StartsWith("ROLLBACK"))
            return "transaction";
        
        return "other";
    }

    private static string GetErrorType(Exception exception)
    {
        return exception switch
        {
            TimeoutException => "timeout",
            InvalidOperationException => "invalid_operation",
            ArgumentException => "argument_error",
            UnauthorizedAccessException => "unauthorized",
            _ when exception.Message.Contains("connection", StringComparison.OrdinalIgnoreCase) => "connection_error",
            _ when exception.Message.Contains("timeout", StringComparison.OrdinalIgnoreCase) => "timeout",
            _ when exception.Message.Contains("constraint", StringComparison.OrdinalIgnoreCase) => "constraint_violation",
            _ when exception.Message.Contains("duplicate", StringComparison.OrdinalIgnoreCase) => "duplicate_key",
            _ => "unknown_error"
        };
    }
}