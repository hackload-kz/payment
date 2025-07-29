using FluentValidation.Results;
using Microsoft.Extensions.Logging;
using PaymentGateway.Core.Validation.Localization;
using System.Globalization;

namespace PaymentGateway.Core.Validation.Aggregation;

/// <summary>
/// Service for aggregating and reporting validation results
/// </summary>
public interface IValidationResultAggregator
{
    ValidationSummary AggregateResults(params ValidationResult[] results);
    ValidationSummary AggregateResults(IEnumerable<ValidationResult> results);
    ValidationReport GenerateReport(ValidationSummary summary, string language = "ru");
    ValidationReport GenerateDetailedReport(ValidationSummary summary, string language = "ru");
    Dictionary<string, object> GenerateMetrics(ValidationSummary summary);
    bool HasCriticalErrors(ValidationSummary summary);
    List<ValidationFailure> GetErrorsByCategory(ValidationSummary summary, ErrorCategory category);
    string GenerateUserFriendlyMessage(ValidationSummary summary, string language = "ru");
}

/// <summary>
/// Implementation of validation result aggregator
/// </summary>
public class ValidationResultAggregator : IValidationResultAggregator
{
    private readonly IValidationMessageLocalizer _messageLocalizer;
    private readonly ILogger<ValidationResultAggregator> _logger;

    public ValidationResultAggregator(
        IValidationMessageLocalizer messageLocalizer,
        ILogger<ValidationResultAggregator> logger)
    {
        _messageLocalizer = messageLocalizer;
        _logger = logger;
    }

    public ValidationSummary AggregateResults(params ValidationResult[] results)
    {
        return AggregateResults((IEnumerable<ValidationResult>)results);
    }

    public ValidationSummary AggregateResults(IEnumerable<ValidationResult> results)
    {
        var summary = new ValidationSummary
        {
            AggregatedAt = DateTime.UtcNow,
            TotalValidationResults = results.Count()
        };

        var allErrors = new List<ValidationFailure>();
        var validationSources = new List<string>();

        foreach (var result in results)
        {
            summary.TotalErrors += result.Errors.Count;
            allErrors.AddRange(result.Errors);
            
            if (!result.IsValid)
            {
                summary.FailedValidationCount++;
            }
            else
            {
                summary.PassedValidationCount++;
            }

            // Track validation sources (if available in custom properties)
            var source = GetValidationSource(result);
            if (!string.IsNullOrEmpty(source) && !validationSources.Contains(source))
            {
                validationSources.Add(source);
            }
        }

        summary.ValidationSources = validationSources;
        summary.AllErrors = allErrors;

        // Categorize errors
        summary.ErrorsByCategory = CategorizeErrors(allErrors);
        summary.ErrorsBySeverity = CategorizeErrorsBySeverity(allErrors);
        summary.ErrorsByField = GroupErrorsByField(allErrors);

        // Calculate error statistics
        summary.ErrorStatistics = CalculateErrorStatistics(allErrors);

        // Determine overall status
        summary.IsValid = summary.TotalErrors == 0;
        summary.HasCriticalErrors = HasCriticalErrors(summary);
        summary.HasWarnings = HasWarnings(allErrors);

        // Generate recommendations
        summary.Recommendations = GenerateRecommendations(summary);

        return summary;
    }

    public ValidationReport GenerateReport(ValidationSummary summary, string language = "ru")
    {
        var report = new ValidationReport
        {
            GeneratedAt = DateTime.UtcNow,
            Language = language,
            IsValid = summary.IsValid,
            TotalErrors = summary.TotalErrors,
            HasCriticalErrors = summary.HasCriticalErrors
        };

        // Generate localized error messages
        report.LocalizedErrors = new List<LocalizedValidationError>();
        foreach (var error in summary.AllErrors)
        {
            var localizedError = new LocalizedValidationError
            {
                PropertyName = error.PropertyName,
                ErrorCode = error.ErrorCode,
                AttemptedValue = error.AttemptedValue?.ToString(),
                Message = _messageLocalizer.GetLocalizedMessage(error.ErrorCode, language),
                Severity = DetermineSeverity(error.ErrorCode),
                Category = DetermineCategory(error.ErrorCode)
            };

            report.LocalizedErrors.Add(localizedError);
        }

        // Group errors by field for better presentation
        report.ErrorsByField = report.LocalizedErrors
            .GroupBy(e => e.PropertyName)
            .ToDictionary(g => g.Key, g => g.ToList());

        // Generate summary message
        report.SummaryMessage = GenerateUserFriendlyMessage(summary, language);

        return report;
    }

    public ValidationReport GenerateDetailedReport(ValidationSummary summary, string language = "ru")
    {
        var report = GenerateReport(summary, language);

        // Add detailed analysis
        report.DetailedAnalysis = new ValidationAnalysis
        {
            ErrorDistribution = summary.ErrorsByCategory.ToDictionary(
                kvp => kvp.Key.ToString(), 
                kvp => kvp.Value.Count),
            SeverityDistribution = summary.ErrorsBySeverity.ToDictionary(
                kvp => kvp.Key.ToString(), 
                kvp => kvp.Value.Count),
            FieldAnalysis = GenerateFieldAnalysis(summary, language),
            Recommendations = summary.Recommendations.Select(r => 
                _messageLocalizer.GetLocalizedMessage($"RECOMMENDATION_{r.Type}", language, r.Parameters)).ToList(),
            TrendAnalysis = GenerateTrendAnalysis(summary),
            PerformanceMetrics = GenerateMetrics(summary)
        };

        // Add validation patterns
        report.ValidationPatterns = IdentifyValidationPatterns(summary);

        return report;
    }

    public Dictionary<string, object> GenerateMetrics(ValidationSummary summary)
    {
        return new Dictionary<string, object>
        {
            ["total_errors"] = summary.TotalErrors,
            ["validation_success_rate"] = summary.TotalValidationResults > 0 
                ? (double)summary.PassedValidationCount / summary.TotalValidationResults 
                : 0.0,
            ["critical_error_rate"] = summary.TotalErrors > 0 
                ? (double)summary.ErrorsBySeverity.GetValueOrDefault(ErrorSeverity.Critical, new List<ValidationFailure>()).Count / summary.TotalErrors 
                : 0.0,
            ["most_common_error"] = summary.ErrorStatistics.MostCommonErrorCode,
            ["most_problematic_field"] = summary.ErrorStatistics.MostProblematicField,
            ["errors_by_category"] = summary.ErrorsByCategory.ToDictionary(
                kvp => kvp.Key.ToString(), 
                kvp => kvp.Value.Count),
            ["average_errors_per_validation"] = summary.TotalValidationResults > 0 
                ? (double)summary.TotalErrors / summary.TotalValidationResults 
                : 0.0,
            ["has_critical_errors"] = summary.HasCriticalErrors,
            ["validation_sources"] = summary.ValidationSources.Count,
            ["aggregation_timestamp"] = summary.AggregatedAt
        };
    }

    public bool HasCriticalErrors(ValidationSummary summary)
    {
        return summary.ErrorsBySeverity.ContainsKey(ErrorSeverity.Critical) &&
               summary.ErrorsBySeverity[ErrorSeverity.Critical].Any();
    }

    public List<ValidationFailure> GetErrorsByCategory(ValidationSummary summary, ErrorCategory category)
    {
        return summary.ErrorsByCategory.GetValueOrDefault(category, new List<ValidationFailure>());
    }

    public string GenerateUserFriendlyMessage(ValidationSummary summary, string language = "ru")
    {
        if (summary.IsValid)
        {
            return language == "ru" 
                ? "Валидация успешно завершена" 
                : "Validation completed successfully";
        }

        if (summary.HasCriticalErrors)
        {
            var criticalCount = summary.ErrorsBySeverity.GetValueOrDefault(ErrorSeverity.Critical, new List<ValidationFailure>()).Count;
            return language == "ru" 
                ? $"Обнаружены критические ошибки ({criticalCount}). Операция не может быть выполнена." 
                : $"Critical errors detected ({criticalCount}). Operation cannot proceed.";
        }

        var errorCount = summary.TotalErrors;
        return language == "ru" 
            ? $"Обнаружены ошибки валидации ({errorCount}). Пожалуйста, исправьте указанные проблемы." 
            : $"Validation errors detected ({errorCount}). Please fix the indicated issues.";
    }

    private Dictionary<ErrorCategory, List<ValidationFailure>> CategorizeErrors(List<ValidationFailure> errors)
    {
        var categorized = new Dictionary<ErrorCategory, List<ValidationFailure>>();

        foreach (var error in errors)
        {
            var category = DetermineCategory(error.ErrorCode);
            if (!categorized.ContainsKey(category))
            {
                categorized[category] = new List<ValidationFailure>();
            }
            categorized[category].Add(error);
        }

        return categorized;
    }

    private Dictionary<ErrorSeverity, List<ValidationFailure>> CategorizeErrorsBySeverity(List<ValidationFailure> errors)
    {
        var categorized = new Dictionary<ErrorSeverity, List<ValidationFailure>>();

        foreach (var error in errors)
        {
            var severity = DetermineSeverity(error.ErrorCode);
            if (!categorized.ContainsKey(severity))
            {
                categorized[severity] = new List<ValidationFailure>();
            }
            categorized[severity].Add(error);
        }

        return categorized;
    }

    private Dictionary<string, List<ValidationFailure>> GroupErrorsByField(List<ValidationFailure> errors)
    {
        return errors.GroupBy(e => e.PropertyName ?? "General")
            .ToDictionary(g => g.Key, g => g.ToList());
    }

    private ErrorStatistics CalculateErrorStatistics(List<ValidationFailure> errors)
    {
        var errorCodeCounts = errors.GroupBy(e => e.ErrorCode)
            .ToDictionary(g => g.Key, g => g.Count());

        var fieldErrorCounts = errors.GroupBy(e => e.PropertyName ?? "General")
            .ToDictionary(g => g.Key, g => g.Count());

        return new ErrorStatistics
        {
            TotalUniqueErrors = errorCodeCounts.Count,
            TotalAffectedFields = fieldErrorCounts.Count,
            MostCommonErrorCode = errorCodeCounts.OrderByDescending(kvp => kvp.Value).FirstOrDefault().Key,
            MostProblematicField = fieldErrorCounts.OrderByDescending(kvp => kvp.Value).FirstOrDefault().Key,
            ErrorCodeDistribution = errorCodeCounts,
            FieldErrorDistribution = fieldErrorCounts
        };
    }

    private bool HasWarnings(List<ValidationFailure> errors)
    {
        return errors.Any(e => DetermineSeverity(e.ErrorCode) == ErrorSeverity.Warning);
    }

    private List<ValidationRecommendation> GenerateRecommendations(ValidationSummary summary)
    {
        var recommendations = new List<ValidationRecommendation>();

        // Analyze patterns and generate recommendations
        if (summary.ErrorStatistics.ErrorCodeDistribution.ContainsKey("AMOUNT_TOO_SMALL"))
        {
            recommendations.Add(new ValidationRecommendation
            {
                Type = "AMOUNT_VALIDATION",
                Priority = RecommendationPriority.High,
                Parameters = new[] { "1000", "10 RUB" }
            });
        }

        if (summary.ErrorStatistics.ErrorCodeDistribution.ContainsKey("EMAIL_INVALID_FORMAT"))
        {
            recommendations.Add(new ValidationRecommendation
            {
                Type = "EMAIL_FORMAT_CHECK",
                Priority = RecommendationPriority.Medium,
                Parameters = Array.Empty<string>()
            });
        }

        return recommendations;
    }

    private ErrorCategory DetermineCategory(string errorCode)
    {
        if (string.IsNullOrEmpty(errorCode))
            return ErrorCategory.General;

        return errorCode switch
        {
            var code when code.Contains("AMOUNT") || code.Contains("CURRENCY") => ErrorCategory.Financial,
            var code when code.Contains("EMAIL") || code.Contains("PHONE") => ErrorCategory.Contact,
            var code when code.Contains("TEAM") || code.Contains("TOKEN") => ErrorCategory.Authentication,
            var code when code.Contains("PAYMENT") || code.Contains("ORDER") => ErrorCategory.Payment,
            var code when code.Contains("CUSTOMER") => ErrorCategory.Customer,
            var code when code.Contains("URL") => ErrorCategory.Integration,
            var code when code.Contains("ITEM") => ErrorCategory.Items,
            var code when code.Contains("RECEIPT") => ErrorCategory.Receipt,
            _ => ErrorCategory.General
        };
    }

    private ErrorSeverity DetermineSeverity(string errorCode)
    {
        if (string.IsNullOrEmpty(errorCode))
            return ErrorSeverity.Medium;

        var criticalErrors = new[]
        {
            "TEAM_NOT_FOUND", "TOKEN_INVALID", "PAYMENT_NOT_FOUND", 
            "DAILY_LIMIT_EXCEEDED", "VALIDATION_SERVICE_ERROR"
        };

        var warningErrors = new[]
        {
            "DESCRIPTION_TOO_LONG", "CUSTOMER_DATA_INCONSISTENT"
        };

        if (criticalErrors.Contains(errorCode))
            return ErrorSeverity.Critical;

        if (warningErrors.Contains(errorCode))
            return ErrorSeverity.Warning;

        return ErrorSeverity.Medium;
    }

    private string GetValidationSource(ValidationResult result)
    {
        // Try to extract source from custom properties if available
        return "UnknownSource"; // Would be enhanced with actual source tracking
    }

    private List<FieldAnalysisResult> GenerateFieldAnalysis(ValidationSummary summary, string language)
    {
        return summary.ErrorsByField.Select(kvp => new FieldAnalysisResult
        {
            FieldName = kvp.Key,
            ErrorCount = kvp.Value.Count,
            MostCommonError = kvp.Value.GroupBy(e => e.ErrorCode)
                .OrderByDescending(g => g.Count())
                .FirstOrDefault()?.Key,
            Recommendation = GenerateFieldRecommendation(kvp.Key, kvp.Value, language)
        }).ToList();
    }

    private string GenerateFieldRecommendation(string fieldName, List<ValidationFailure> errors, string language)
    {
        var mostCommonError = errors.GroupBy(e => e.ErrorCode)
            .OrderByDescending(g => g.Count())
            .FirstOrDefault()?.Key;

        return _messageLocalizer.GetLocalizedMessage($"FIELD_RECOMMENDATION_{mostCommonError}", language);
    }

    private TrendAnalysis GenerateTrendAnalysis(ValidationSummary summary)
    {
        // This would typically analyze historical validation data
        return new TrendAnalysis
        {
            ErrorTrends = new Dictionary<string, double>(),
            ImprovementSuggestions = new List<string>()
        };
    }

    private List<ValidationPattern> IdentifyValidationPatterns(ValidationSummary summary)
    {
        var patterns = new List<ValidationPattern>();

        // Identify common error patterns
        var errorGroups = summary.AllErrors.GroupBy(e => e.ErrorCode);
        foreach (var group in errorGroups.Where(g => g.Count() > 1))
        {
            patterns.Add(new ValidationPattern
            {
                PatternType = "REPEATED_ERROR",
                ErrorCode = group.Key,
                Frequency = group.Count(),
                AffectedFields = group.Select(e => e.PropertyName).Distinct().ToList()
            });
        }

        return patterns;
    }
}

// Supporting classes for validation aggregation
public class ValidationSummary
{
    public DateTime AggregatedAt { get; set; }
    public int TotalValidationResults { get; set; }
    public int PassedValidationCount { get; set; }
    public int FailedValidationCount { get; set; }
    public int TotalErrors { get; set; }
    public bool IsValid { get; set; }
    public bool HasCriticalErrors { get; set; }
    public bool HasWarnings { get; set; }
    public List<string> ValidationSources { get; set; } = new();
    public List<ValidationFailure> AllErrors { get; set; } = new();
    public Dictionary<ErrorCategory, List<ValidationFailure>> ErrorsByCategory { get; set; } = new();
    public Dictionary<ErrorSeverity, List<ValidationFailure>> ErrorsBySeverity { get; set; } = new();
    public Dictionary<string, List<ValidationFailure>> ErrorsByField { get; set; } = new();
    public ErrorStatistics ErrorStatistics { get; set; } = new();
    public List<ValidationRecommendation> Recommendations { get; set; } = new();
}

public class ValidationReport
{
    public DateTime GeneratedAt { get; set; }
    public string Language { get; set; } = "ru";
    public bool IsValid { get; set; }
    public int TotalErrors { get; set; }
    public bool HasCriticalErrors { get; set; }
    public string SummaryMessage { get; set; } = string.Empty;
    public List<LocalizedValidationError> LocalizedErrors { get; set; } = new();
    public Dictionary<string, List<LocalizedValidationError>> ErrorsByField { get; set; } = new();
    public ValidationAnalysis? DetailedAnalysis { get; set; }
    public List<ValidationPattern> ValidationPatterns { get; set; } = new();
}

public class LocalizedValidationError
{
    public string PropertyName { get; set; } = string.Empty;
    public string ErrorCode { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string? AttemptedValue { get; set; }
    public ErrorSeverity Severity { get; set; }
    public ErrorCategory Category { get; set; }
}

public class ValidationAnalysis
{
    public Dictionary<string, int> ErrorDistribution { get; set; } = new();
    public Dictionary<string, int> SeverityDistribution { get; set; } = new();
    public List<FieldAnalysisResult> FieldAnalysis { get; set; } = new();
    public List<string> Recommendations { get; set; } = new();
    public TrendAnalysis TrendAnalysis { get; set; } = new();
    public Dictionary<string, object> PerformanceMetrics { get; set; } = new();
}

public class ErrorStatistics
{
    public int TotalUniqueErrors { get; set; }
    public int TotalAffectedFields { get; set; }
    public string MostCommonErrorCode { get; set; } = string.Empty;
    public string MostProblematicField { get; set; } = string.Empty;
    public Dictionary<string, int> ErrorCodeDistribution { get; set; } = new();
    public Dictionary<string, int> FieldErrorDistribution { get; set; } = new();
}

public class ValidationRecommendation
{
    public string Type { get; set; } = string.Empty;
    public RecommendationPriority Priority { get; set; }
    public string[] Parameters { get; set; } = Array.Empty<string>();
}

public class FieldAnalysisResult
{
    public string FieldName { get; set; } = string.Empty;
    public int ErrorCount { get; set; }
    public string? MostCommonError { get; set; }
    public string Recommendation { get; set; } = string.Empty;
}

public class TrendAnalysis
{
    public Dictionary<string, double> ErrorTrends { get; set; } = new();
    public List<string> ImprovementSuggestions { get; set; } = new();
}

public class ValidationPattern
{
    public string PatternType { get; set; } = string.Empty;
    public string ErrorCode { get; set; } = string.Empty;
    public int Frequency { get; set; }
    public List<string> AffectedFields { get; set; } = new();
}

// Enums for validation categorization
public enum ErrorCategory
{
    General,
    Authentication,
    Financial,
    Payment,
    Customer,
    Contact,
    Integration,
    Items,
    Receipt
}

public enum ErrorSeverity
{
    Warning,
    Medium,
    Critical
}

public enum RecommendationPriority
{
    Low,
    Medium, 
    High,
    Critical
}