using PaymentGateway.Core.Enums;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Prometheus;
using System.Collections.Concurrent;

namespace PaymentGateway.Core.Services;

public interface IErrorAnalyticsService
{
    Task RecordErrorAsync(ErrorAnalyticsData errorData, CancellationToken cancellationToken = default);
    Task<ErrorAnalyticsReport> GenerateReportAsync(TimeSpan period, CancellationToken cancellationToken = default);
    Task<ErrorHealthScore> CalculateHealthScoreAsync(TimeSpan period);
    Task<List<ErrorRecommendation>> GetRecommendationsAsync();
    Task<ErrorForecast> ForecastErrorTrendsAsync(TimeSpan forecastPeriod);
    Task NotifyErrorThresholdExceededAsync(PaymentErrorCode errorCode, double currentRate, double threshold);
}

public record ErrorAnalyticsData(
    PaymentErrorCode ErrorCode,
    string CorrelationId,
    string? TeamSlug,
    string? PaymentId,
    string? RequestPath,
    Dictionary<string, string> Metadata,
    DateTime Timestamp);

public record ErrorAnalyticsReport(
    TimeSpan Period,
    DateTime GeneratedAt,
    ErrorSummary Summary,
    List<TopError> TopErrors,
    List<TeamErrorBreakdown> TeamBreakdowns,
    List<TimeSeriesPoint> ErrorTimeSeries,
    List<ErrorCorrelation> Correlations,
    List<ErrorRecommendation> Recommendations);

public record ErrorSummary(
    int TotalErrors,
    int UniqueErrors,
    double ErrorRate,
    double CriticalErrorRate,
    double AvailabilityScore,
    int AffectedTeams);

public record TopError(
    PaymentErrorCode ErrorCode,
    int Count,
    double Percentage,
    ErrorCategory Category,
    ErrorSeverity Severity,
    double TrendDirection); // -1 to 1, negative means decreasing

public record TeamErrorBreakdown(
    string TeamSlug,
    int TotalErrors,
    Dictionary<PaymentErrorCode, int> ErrorBreakdown,
    double ErrorRate,
    string Status); // "healthy", "warning", "critical"

public record TimeSeriesPoint(
    DateTime Timestamp,
    int ErrorCount,
    Dictionary<PaymentErrorCode, int> ErrorBreakdown);

public record ErrorCorrelation(
    PaymentErrorCode PrimaryError,
    PaymentErrorCode CorrelatedError,
    double CorrelationStrength,
    string Description);

public record ErrorRecommendation(
    string Title,
    string Description,
    List<string> ActionItems,
    ErrorSeverity Priority,
    List<PaymentErrorCode> RelatedErrors);

public record ErrorHealthScore(
    double OverallScore, // 0-100
    double SystemHealthScore,
    double AuthenticationHealthScore,
    double ValidationHealthScore,
    double PaymentProcessingHealthScore,
    Dictionary<string, double> TeamHealthScores,
    string HealthStatus); // "excellent", "good", "fair", "poor", "critical"

public record ErrorForecast(
    TimeSpan ForecastPeriod,
    Dictionary<PaymentErrorCode, ForecastPoint> Forecasts,
    double ConfidenceLevel,
    List<string> Assumptions);

public record ForecastPoint(
    PaymentErrorCode ErrorCode,
    int PredictedCount,
    double TrendSlope,
    double ConfidenceInterval);

public class ErrorAnalyticsOptions
{
    public TimeSpan DefaultReportPeriod { get; set; } = TimeSpan.FromHours(24);
    public Dictionary<string, double> ErrorRateThresholds { get; set; } = new()
    {
        { "Critical", 0.001 },  // 0.1% critical error rate
        { "High", 0.01 },       // 1% high severity error rate
        { "Medium", 0.05 },     // 5% medium severity error rate
        { "Overall", 0.1 }      // 10% overall error rate
    };
    public bool EnableRealTimeAlerting { get; set; } = true;
    public bool EnableTrendAnalysis { get; set; } = true;
    public bool EnableCorrelationAnalysis { get; set; } = true;
}

public class ErrorAnalyticsService : IErrorAnalyticsService
{
    private readonly ILogger<ErrorAnalyticsService> _logger;
    private readonly IErrorCategorizationService _categorizationService;
    private readonly IErrorTrackingService _trackingService;
    private readonly ErrorAnalyticsOptions _options;
    
    // Prometheus metrics
    private readonly Counter _errorCounter;
    private readonly Histogram _errorAnalyticsProcessingTime;
    private readonly Gauge _errorHealthScore;
    private readonly Gauge _currentErrorRate;

    private readonly ConcurrentDictionary<DateTime, ErrorAnalyticsData> _recentErrors;
    private readonly ConcurrentDictionary<string, double> _teamErrorRates;

    public ErrorAnalyticsService(
        ILogger<ErrorAnalyticsService> logger,
        IErrorCategorizationService categorizationService,
        IErrorTrackingService trackingService,
        IOptions<ErrorAnalyticsOptions> options)
    {
        _logger = logger;
        _categorizationService = categorizationService;
        _trackingService = trackingService;
        _options = options.Value;
        _recentErrors = new ConcurrentDictionary<DateTime, ErrorAnalyticsData>();
        _teamErrorRates = new ConcurrentDictionary<string, double>();

        // Initialize Prometheus metrics
        _errorCounter = Metrics.CreateCounter(
            "payment_errors_total",
            "Total number of payment errors",
            new[] { "error_code", "category", "severity", "team" });

        _errorAnalyticsProcessingTime = Metrics.CreateHistogram(
            "error_analytics_processing_duration_seconds",
            "Time spent processing error analytics");

        _errorHealthScore = Metrics.CreateGauge(
            "payment_system_health_score",
            "Overall payment system health score (0-100)");

        _currentErrorRate = Metrics.CreateGauge(
            "payment_error_rate",
            "Current payment error rate",
            new[] { "category", "team" });
    }

    public async Task RecordErrorAsync(ErrorAnalyticsData errorData, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(errorData);

        using var activity = _errorAnalyticsProcessingTime.NewTimer();

        try
        {
            // Store the error data
            _recentErrors.TryAdd(errorData.Timestamp, errorData);

            // Update Prometheus metrics
            var categoryInfo = _categorizationService.GetErrorCategoryInfo(errorData.ErrorCode);
            _errorCounter
                .WithLabels(
                    errorData.ErrorCode.ToString(),
                    categoryInfo.Category.ToString(),
                    categoryInfo.Severity.ToString(),
                    errorData.TeamSlug ?? "unknown")
                .Inc();

            // Update team error rates
            if (!string.IsNullOrEmpty(errorData.TeamSlug))
            {
                _teamErrorRates.AddOrUpdate(errorData.TeamSlug, 1.0, (_, existing) => existing + 1.0);
            }

            // Check for threshold violations
            if (_options.EnableRealTimeAlerting)
            {
                await CheckErrorThresholdsAsync(errorData);
            }

            // Clean up old data (keep last 7 days)
            await CleanupOldAnalyticsDataAsync();

            _logger.LogDebug("Recorded error analytics data for {ErrorCode} with correlation {CorrelationId}",
                errorData.ErrorCode, errorData.CorrelationId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to record error analytics data for {ErrorCode}", errorData.ErrorCode);
        }
    }

    public async Task<ErrorAnalyticsReport> GenerateReportAsync(TimeSpan period, CancellationToken cancellationToken = default)
    {
        using var activity = _errorAnalyticsProcessingTime.NewTimer();

        var endTime = DateTime.UtcNow;
        var startTime = endTime - period;

        var periodErrors = _recentErrors.Values
            .Where(e => e.Timestamp >= startTime && e.Timestamp <= endTime)
            .ToList();

        // Generate summary
        var summary = GenerateErrorSummary(periodErrors, period);

        // Generate top errors
        var topErrors = GenerateTopErrors(periodErrors);

        // Generate team breakdowns
        var teamBreakdowns = GenerateTeamBreakdowns(periodErrors);

        // Generate time series
        var timeSeries = GenerateTimeSeries(periodErrors, period);

        // Generate correlations if enabled
        var correlations = _options.EnableCorrelationAnalysis 
            ? await GenerateErrorCorrelationsAsync(periodErrors)
            : new List<ErrorCorrelation>();

        // Generate recommendations
        var recommendations = await GenerateRecommendationsAsync(periodErrors);

        var report = new ErrorAnalyticsReport(
            period,
            DateTime.UtcNow,
            summary,
            topErrors,
            teamBreakdowns,
            timeSeries,
            correlations,
            recommendations);

        _logger.LogInformation("Generated error analytics report for period {Period}: {TotalErrors} total errors, {ErrorRate:P2} error rate",
            period, summary.TotalErrors, summary.ErrorRate);

        return report;
    }

    public async Task<ErrorHealthScore> CalculateHealthScoreAsync(TimeSpan period)
    {
        var endTime = DateTime.UtcNow;
        var startTime = endTime - period;

        var periodErrors = _recentErrors.Values
            .Where(e => e.Timestamp >= startTime && e.Timestamp <= endTime)
            .ToList();

        // Calculate category-specific health scores
        var systemHealthScore = CalculateCategoryHealthScore(periodErrors, ErrorCategory.System);
        var authHealthScore = CalculateCategoryHealthScore(periodErrors, ErrorCategory.Authentication);
        var validationHealthScore = CalculateCategoryHealthScore(periodErrors, ErrorCategory.Validation);
        var paymentHealthScore = CalculateCategoryHealthScore(periodErrors, ErrorCategory.BusinessLogic);

        // Calculate team health scores
        var teamHealthScores = periodErrors
            .Where(e => !string.IsNullOrEmpty(e.TeamSlug))
            .GroupBy(e => e.TeamSlug!)
            .ToDictionary(g => g.Key, g => CalculateTeamHealthScore(g.ToList()));

        // Calculate overall health score (weighted average)
        var overallScore = (systemHealthScore * 0.3 + authHealthScore * 0.2 + 
                          validationHealthScore * 0.2 + paymentHealthScore * 0.3);

        var healthStatus = overallScore switch
        {
            >= 95 => "excellent",
            >= 85 => "good",
            >= 70 => "fair",
            >= 50 => "poor",
            _ => "critical"
        };

        var healthScore = new ErrorHealthScore(
            overallScore,
            systemHealthScore,
            authHealthScore,
            validationHealthScore,
            paymentHealthScore,
            teamHealthScores,
            healthStatus);

        // Update Prometheus metric
        _errorHealthScore.Set(overallScore);

        return await Task.FromResult(healthScore);
    }

    public async Task<List<ErrorRecommendation>> GetRecommendationsAsync()
    {
        var recommendations = new List<ErrorRecommendation>();

        // Analyze recent errors for patterns
        var recentErrors = _recentErrors.Values
            .Where(e => e.Timestamp > DateTime.UtcNow - TimeSpan.FromHours(24))
            .ToList();

        // Authentication issues
        var authErrors = recentErrors.Count(e => e.ErrorCode == PaymentErrorCode.InvalidToken);
        if (authErrors > 10)
        {
            recommendations.Add(new ErrorRecommendation(
                "High Authentication Failure Rate",
                "Multiple authentication failures detected in the last 24 hours",
                new List<string>
                {
                    "Review merchant credential management processes",
                    "Check for incorrect TerminalKey/SecretKey pairs",
                    "Implement authentication retry logic with exponential backoff",
                    "Monitor for potential brute force attacks"
                },
                ErrorSeverity.High,
                new List<PaymentErrorCode> { PaymentErrorCode.InvalidToken, PaymentErrorCode.TokenAuthenticationFailed }));
        }

        // Service availability issues
        var serviceErrors = recentErrors.Count(e => e.ErrorCode == PaymentErrorCode.ServiceTemporarilyUnavailable);
        if (serviceErrors > 20)
        {
            recommendations.Add(new ErrorRecommendation(
                "Service Availability Issues",
                "High number of service unavailable errors indicates potential system overload",
                new List<string>
                {
                    "Scale up payment processing infrastructure",
                    "Review system resource utilization",
                    "Implement circuit breaker pattern for external services",
                    "Add more aggressive retry policies for transient failures"
                },
                ErrorSeverity.Critical,
                new List<PaymentErrorCode> 
                { 
                    PaymentErrorCode.ServiceTemporarilyUnavailable,
                    PaymentErrorCode.ExternalServiceUnavailable 
                }));
        }

        // Validation errors
        var validationErrors = recentErrors.Count(e => 
            _categorizationService.GetErrorCategoryInfo(e.ErrorCode).Category == ErrorCategory.Validation);
        if (validationErrors > 50)
        {
            recommendations.Add(new ErrorRecommendation(
                "High Validation Error Rate",
                "Many requests are failing validation, indicating potential integration issues",
                new List<string>
                {
                    "Review API documentation with merchants",
                    "Implement better client-side validation",
                    "Provide clearer error messages for validation failures",
                    "Create validation examples and test cases for merchants"
                },
                ErrorSeverity.Medium,
                new List<PaymentErrorCode> 
                { 
                    PaymentErrorCode.InvalidParameterFormat,
                    PaymentErrorCode.MissingRequiredParameters,
                    PaymentErrorCode.RequestValidationFailed
                }));
        }

        return await Task.FromResult(recommendations);
    }

    public async Task<ErrorForecast> ForecastErrorTrendsAsync(TimeSpan forecastPeriod)
    {
        var historicalPeriod = TimeSpan.FromDays(30); // Use 30 days of historical data
        var endTime = DateTime.UtcNow;
        var startTime = endTime - historicalPeriod;

        var historicalErrors = _recentErrors.Values
            .Where(e => e.Timestamp >= startTime && e.Timestamp <= endTime)
            .ToList();

        var forecasts = new Dictionary<PaymentErrorCode, ForecastPoint>();

        // Simple linear trend analysis for each error code
        foreach (var errorCode in historicalErrors.Select(e => e.ErrorCode).Distinct())
        {
            var errorInstances = historicalErrors
                .Where(e => e.ErrorCode == errorCode)
                .OrderBy(e => e.Timestamp)
                .ToList();

            if (errorInstances.Count >= 10) // Need minimum data points
            {
                var forecast = CalculateLinearForecast(errorInstances, forecastPeriod);
                forecasts[errorCode] = forecast;
            }
        }

        return await Task.FromResult(new ErrorForecast(
            forecastPeriod,
            forecasts,
            0.70, // 70% confidence level for simple linear model
            new List<string>
            {
                "Based on linear trend analysis of last 30 days",
                "Does not account for seasonal patterns or external factors",
                "Confidence decreases with longer forecast periods"
            }));
    }

    public async Task NotifyErrorThresholdExceededAsync(PaymentErrorCode errorCode, double currentRate, double threshold)
    {
        _logger.LogWarning("Error threshold exceeded for {ErrorCode}: current rate {CurrentRate:P2} > threshold {Threshold:P2}",
            errorCode, currentRate, threshold);

        // This would integrate with notification services (email, Slack, etc.)
        await Task.CompletedTask;
    }

    private ErrorSummary GenerateErrorSummary(List<ErrorAnalyticsData> errors, TimeSpan period)
    {
        var totalErrors = errors.Count;
        var uniqueErrors = errors.Select(e => e.ErrorCode).Distinct().Count();
        
        // Assuming total requests is available from metrics (simplified calculation)
        var assumedTotalRequests = Math.Max(totalErrors * 10, 1000); // Rough estimate
        var errorRate = (double)totalErrors / assumedTotalRequests;

        var criticalErrors = errors.Count(e => 
            _categorizationService.GetErrorCategoryInfo(e.ErrorCode).Severity == ErrorSeverity.Critical);
        var criticalErrorRate = (double)criticalErrors / assumedTotalRequests;

        var availabilityScore = Math.Max(0, 100 - (errorRate * 100));
        var affectedTeams = errors.Where(e => !string.IsNullOrEmpty(e.TeamSlug))
            .Select(e => e.TeamSlug!)
            .Distinct()
            .Count();

        return new ErrorSummary(
            totalErrors,
            uniqueErrors,
            errorRate,
            criticalErrorRate,
            availabilityScore,
            affectedTeams);
    }

    private List<TopError> GenerateTopErrors(List<ErrorAnalyticsData> errors)
    {
        var totalErrors = Math.Max(errors.Count, 1);

        return errors
            .GroupBy(e => e.ErrorCode)
            .Select(g =>
            {
                var count = g.Count();
                var percentage = (double)count / totalErrors * 100;
                var categoryInfo = _categorizationService.GetErrorCategoryInfo(g.Key);
                
                return new TopError(
                    g.Key,
                    count,
                    percentage,
                    categoryInfo.Category,
                    categoryInfo.Severity,
                    0.0); // Trend analysis would require historical comparison
            })
            .OrderByDescending(e => e.Count)
            .Take(10)
            .ToList();
    }

    private List<TeamErrorBreakdown> GenerateTeamBreakdowns(List<ErrorAnalyticsData> errors)
    {
        return errors
            .Where(e => !string.IsNullOrEmpty(e.TeamSlug))
            .GroupBy(e => e.TeamSlug!)
            .Select(g =>
            {
                var teamErrors = g.ToList();
                var totalErrors = teamErrors.Count;
                var errorBreakdown = teamErrors
                    .GroupBy(e => e.ErrorCode)
                    .ToDictionary(eg => eg.Key, eg => eg.Count());

                var criticalErrors = teamErrors.Count(e =>
                    _categorizationService.GetErrorCategoryInfo(e.ErrorCode).Severity == ErrorSeverity.Critical);

                var status = criticalErrors > 0 ? "critical" :
                            totalErrors > 50 ? "warning" : "healthy";

                return new TeamErrorBreakdown(
                    g.Key,
                    totalErrors,
                    errorBreakdown,
                    0.0, // Would need total request count to calculate actual rate
                    status);
            })
            .OrderByDescending(t => t.TotalErrors)
            .ToList();
    }

    private List<TimeSeriesPoint> GenerateTimeSeries(List<ErrorAnalyticsData> errors, TimeSpan period)
    {
        var bucketSize = TimeSpan.FromHours(1); // Hourly buckets
        var buckets = new Dictionary<DateTime, List<ErrorAnalyticsData>>();

        foreach (var error in errors)
        {
            var bucketTime = new DateTime(
                error.Timestamp.Year,
                error.Timestamp.Month,
                error.Timestamp.Day,
                error.Timestamp.Hour,
                0, 0, DateTimeKind.Utc);

            if (!buckets.ContainsKey(bucketTime))
                buckets[bucketTime] = new List<ErrorAnalyticsData>();

            buckets[bucketTime].Add(error);
        }

        return buckets
            .OrderBy(kvp => kvp.Key)
            .Select(kvp => new TimeSeriesPoint(
                kvp.Key,
                kvp.Value.Count,
                kvp.Value.GroupBy(e => e.ErrorCode).ToDictionary(g => g.Key, g => g.Count())))
            .ToList();
    }

    private async Task<List<ErrorCorrelation>> GenerateErrorCorrelationsAsync(List<ErrorAnalyticsData> errors)
    {
        var correlations = new List<ErrorCorrelation>();

        // Simple correlation analysis - errors occurring together frequently
        var errorPairs = new Dictionary<(PaymentErrorCode, PaymentErrorCode), int>();

        var errorsByCorrelation = errors.GroupBy(e => e.CorrelationId);

        foreach (var correlationGroup in errorsByCorrelation)
        {
            var uniqueErrors = correlationGroup.Select(e => e.ErrorCode).Distinct().ToList();
            
            for (int i = 0; i < uniqueErrors.Count; i++)
            {
                for (int j = i + 1; j < uniqueErrors.Count; j++)
                {
                    var pair = (uniqueErrors[i], uniqueErrors[j]);
                    errorPairs.TryGetValue(pair, out var count);
                    errorPairs[pair] = count + 1;
                }
            }
        }

        // Convert to correlations (simplified)
        foreach (var kvp in errorPairs.Where(kvp => kvp.Value >= 3)) // At least 3 co-occurrences
        {
            var strength = Math.Min(kvp.Value / 10.0, 1.0); // Normalize to 0-1
            correlations.Add(new ErrorCorrelation(
                kvp.Key.Item1,
                kvp.Key.Item2,
                strength,
                $"These errors occur together {kvp.Value} times"));
        }

        return await Task.FromResult(correlations.Take(10).ToList());
    }

    private async Task<List<ErrorRecommendation>> GenerateRecommendationsAsync(List<ErrorAnalyticsData> errors)
    {
        // This would use the same logic as GetRecommendationsAsync but based on period data
        return await GetRecommendationsAsync();
    }

    private double CalculateCategoryHealthScore(List<ErrorAnalyticsData> errors, ErrorCategory category)
    {
        var categoryErrors = errors.Count(e => 
            _categorizationService.GetErrorCategoryInfo(e.ErrorCode).Category == category);

        if (categoryErrors == 0) return 100.0;

        // Simple scoring: fewer errors = higher score
        var totalErrors = Math.Max(errors.Count, 1);
        var errorRatio = (double)categoryErrors / totalErrors;
        
        return Math.Max(0, 100.0 - (errorRatio * 200)); // Scale to 0-100
    }

    private double CalculateTeamHealthScore(List<ErrorAnalyticsData> teamErrors)
    {
        if (teamErrors.Count == 0) return 100.0;

        var criticalErrors = teamErrors.Count(e =>
            _categorizationService.GetErrorCategoryInfo(e.ErrorCode).Severity == ErrorSeverity.Critical);

        if (criticalErrors > 0) return Math.Max(0, 50 - (criticalErrors * 10));

        return Math.Max(0, 100 - (teamErrors.Count * 2));
    }

    private ForecastPoint CalculateLinearForecast(List<ErrorAnalyticsData> historicalData, TimeSpan forecastPeriod)
    {
        // Simple linear regression on error counts over time
        var dataPoints = historicalData.Count;
        var avgCount = (double)dataPoints / Math.Max(forecastPeriod.TotalDays, 1);
        
        // Very simplified trend calculation
        var recent = historicalData.TakeLast(7).Count();
        var older = historicalData.Take(7).Count();
        var trendSlope = recent > older ? 1.0 : recent < older ? -1.0 : 0.0;

        var predictedCount = (int)(avgCount * forecastPeriod.TotalDays);

        return new ForecastPoint(
            historicalData.First().ErrorCode,
            predictedCount,
            trendSlope,
            predictedCount * 0.3); // 30% confidence interval
    }

    private async Task CheckErrorThresholdsAsync(ErrorAnalyticsData errorData)
    {
        var categoryInfo = _categorizationService.GetErrorCategoryInfo(errorData.ErrorCode);
        var severityKey = categoryInfo.Severity.ToString();

        if (_options.ErrorRateThresholds.TryGetValue(severityKey, out var threshold))
        {
            // This would implement real threshold checking logic
            // For now, just log
            _logger.LogDebug("Checking threshold for {ErrorCode} with severity {Severity}", 
                errorData.ErrorCode, categoryInfo.Severity);
        }

        await Task.CompletedTask;
    }

    private async Task CleanupOldAnalyticsDataAsync()
    {
        var cutoffTime = DateTime.UtcNow - TimeSpan.FromDays(7);
        var keysToRemove = _recentErrors.Keys.Where(k => k < cutoffTime).ToList();

        foreach (var key in keysToRemove)
        {
            _recentErrors.TryRemove(key, out _);
        }

        await Task.CompletedTask;
    }
}

// Background service for automated analytics and reporting
public class ErrorAnalyticsBackgroundService : BackgroundService
{
    private readonly IErrorAnalyticsService _analyticsService;
    private readonly ILogger<ErrorAnalyticsBackgroundService> _logger;
    private readonly TimeSpan _reportInterval = TimeSpan.FromHours(1);

    public ErrorAnalyticsBackgroundService(
        IErrorAnalyticsService analyticsService,
        ILogger<ErrorAnalyticsBackgroundService> logger)
    {
        _analyticsService = analyticsService;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Error analytics background service started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // Generate health score
                var healthScore = await _analyticsService.CalculateHealthScoreAsync(TimeSpan.FromHours(1));
                
                _logger.LogInformation("System health score: {HealthScore:F1}% ({Status})",
                    healthScore.OverallScore, healthScore.HealthStatus);

                // Generate recommendations if health is poor
                if (healthScore.OverallScore < 70)
                {
                    var recommendations = await _analyticsService.GetRecommendationsAsync();
                    if (recommendations.Any())
                    {
                        _logger.LogWarning("Generated {Count} recommendations for improving system health",
                            recommendations.Count);
                    }
                }

                await Task.Delay(_reportInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in analytics background service");
                await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
            }
        }

        _logger.LogInformation("Error analytics background service stopped");
    }
}