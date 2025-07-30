// SPDX-License-Identifier: MIT
// Copyright (c) 2025 HackLoad Payment Gateway

using PaymentGateway.Core.Entities;
using PaymentGateway.Core.Interfaces;
using PaymentGateway.Core.Repositories;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Caching.Memory;
using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.RegularExpressions;
using Prometheus;

namespace PaymentGateway.Core.Services;

/// <summary>
/// Business rule engine for configurable payment processing rules
/// </summary>
public interface IBusinessRuleEngineService
{
    Task<RuleEvaluationResult> EvaluatePaymentRulesAsync(PaymentRuleContext context, CancellationToken cancellationToken = default);
    Task<RuleEvaluationResult> EvaluateAmountRulesAsync(AmountRuleContext context, CancellationToken cancellationToken = default);
    Task<RuleEvaluationResult> EvaluateCurrencyRulesAsync(CurrencyRuleContext context, CancellationToken cancellationToken = default);
    Task<RuleEvaluationResult> EvaluateTeamRulesAsync(TeamRuleContext context, CancellationToken cancellationToken = default);
    Task<RuleEvaluationResult> EvaluateCustomerRulesAsync(CustomerRuleContext context, CancellationToken cancellationToken = default);
    Task<BusinessRule> CreateRuleAsync(BusinessRule rule, CancellationToken cancellationToken = default);
    Task<BusinessRule> UpdateRuleAsync(BusinessRule rule, CancellationToken cancellationToken = default);
    Task<bool> DeleteRuleAsync(string ruleId, CancellationToken cancellationToken = default);
    Task<IEnumerable<BusinessRule>> GetRulesAsync(Guid? teamId = null, RuleType? type = null, bool activeOnly = true, CancellationToken cancellationToken = default);
    Task<RuleTestResult> TestRuleAsync(BusinessRule rule, RuleTestContext testContext, CancellationToken cancellationToken = default);
    Task<RulePerformanceStatistics> GetRulePerformanceStatisticsAsync(TimeSpan? period = null, CancellationToken cancellationToken = default);
    Task RefreshRuleCacheAsync(CancellationToken cancellationToken = default);
}

public class PaymentRuleContext
{
    public Guid PaymentId { get; set; }
    public Guid TeamId { get; set; }
    public string TeamSlug { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string OrderId { get; set; } = string.Empty;
    public string PaymentMethod { get; set; } = string.Empty;
    public string CustomerEmail { get; set; } = string.Empty;
    public string CustomerCountry { get; set; } = string.Empty;
    public DateTime PaymentDate { get; set; }
    public Dictionary<string, object> PaymentMetadata { get; set; } = new();
    public Dictionary<string, object> CustomerData { get; set; } = new();
    public Dictionary<string, object> TransactionHistory { get; set; } = new();
}

public class AmountRuleContext
{
    public Guid TeamId { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string PaymentMethod { get; set; } = string.Empty;
    public decimal DailyTotal { get; set; }
    public decimal WeeklyTotal { get; set; }
    public decimal MonthlyTotal { get; set; }
    public int DailyTransactionCount { get; set; }
    public int WeeklyTransactionCount { get; set; }
    public int MonthlyTransactionCount { get; set; }
    public Dictionary<string, object> Metadata { get; set; } = new();
}

public class CurrencyRuleContext
{
    public Guid TeamId { get; set; }
    public string Currency { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string CustomerCountry { get; set; } = string.Empty;
    public string PaymentMethod { get; set; } = string.Empty;
    public List<string> SupportedCurrencies { get; set; } = new();
    public Dictionary<string, decimal> ExchangeRates { get; set; } = new();
    public Dictionary<string, object> Metadata { get; set; } = new();
}

public class TeamRuleContext
{
    public Guid TeamId { get; set; }
    public string TeamSlug { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public DateTime LastPaymentDate { get; set; }
    public decimal TotalProcessedAmount { get; set; }
    public int TotalTransactionCount { get; set; }
    public double RiskScore { get; set; }
    public List<string> AllowedCountries { get; set; } = new();
    public List<string> BlockedCountries { get; set; } = new();
    public Dictionary<string, object> TeamConfiguration { get; set; } = new();
    public Dictionary<string, object> Metadata { get; set; } = new();
}

public class TransactionRuleContext
{
    public string TransactionId { get; set; } = string.Empty;
    public Guid TeamId { get; set; }
    public Guid PaymentId { get; set; }
    public string TransactionType { get; set; } = string.Empty; // PAYMENT, REFUND, CAPTURE, etc.
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime TransactionDate { get; set; }
    public string PaymentMethod { get; set; } = string.Empty;
    public string ProcessorResponse { get; set; } = string.Empty;
    public string MerchantReference { get; set; } = string.Empty;
    public bool IsTestTransaction { get; set; }
    public double RiskScore { get; set; }
    public Dictionary<string, object> ProcessorData { get; set; } = new();
    public Dictionary<string, object> Metadata { get; set; } = new();
}

public class CustomerRuleContext
{
    public Guid CustomerId { get; set; }
    public Guid TeamId { get; set; }
    public string CustomerEmail { get; set; } = string.Empty;
    public string CustomerCountry { get; set; } = string.Empty;
    public string CustomerRegion { get; set; } = string.Empty;
    public string CustomerCity { get; set; } = string.Empty;
    public string IpAddress { get; set; } = string.Empty;
    public string UserAgent { get; set; } = string.Empty;
    public DateTime FirstSeenDate { get; set; }
    public DateTime LastPaymentDate { get; set; }
    public int TotalPaymentCount { get; set; }
    public decimal TotalPaymentAmount { get; set; }
    public int FailedPaymentCount { get; set; }
    public double FraudScore { get; set; }
    public bool IsVip { get; set; }
    public bool IsBlacklisted { get; set; }
    public List<string> PaymentMethods { get; set; } = new();
    public Dictionary<string, object> CustomerProfile { get; set; } = new();
    public Dictionary<string, object> Metadata { get; set; } = new();
}

public class RuleEvaluationResult
{
    public bool IsAllowed { get; set; }
    public bool IsWarning { get; set; }
    public string RuleId { get; set; } = string.Empty;
    public string RuleName { get; set; } = string.Empty;
    public RuleType RuleType { get; set; }
    public string Message { get; set; } = string.Empty;
    public string Details { get; set; } = string.Empty;
    public int Priority { get; set; }
    public TimeSpan EvaluationDuration { get; set; }
    public Dictionary<string, object> RuleContext { get; set; } = new();
    public List<RuleViolation> Violations { get; set; } = new();
    public Dictionary<string, object> ResultMetadata { get; set; } = new();
}

public class RuleViolation
{
    public string Field { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public string ExpectedValue { get; set; } = string.Empty;
    public string ViolationType { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public int Severity { get; set; } // 1 (low) to 5 (critical)
}

public enum RuleType
{
    PAYMENT_LIMIT = 1,
    AMOUNT_VALIDATION = 2,
    CURRENCY_VALIDATION = 3,
    TEAM_RESTRICTION = 4,
    GEOGRAPHIC_RESTRICTION = 5,
    TIME_RESTRICTION = 6,
    PAYMENT_METHOD_RESTRICTION = 7,
    FRAUD_PREVENTION = 8,
    COMPLIANCE_CHECK = 9,
    CUSTOM_VALIDATION = 10,
    CUSTOMER_RESTRICTION = 11
}

public enum RuleAction
{
    ALLOW = 1,
    DENY = 2,
    WARN = 3,
    REQUIRE_APPROVAL = 4,
    APPLY_FEE = 5,
    REDIRECT = 6
}

public class BusinessRule : BaseEntity
{
    public Guid TeamId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public RuleType Type { get; set; }
    public RuleAction Action { get; set; }
    public int Priority { get; set; } = 100; // 1 (highest) to 1000 (lowest)
    public bool IsActive { get; set; } = true;
    public string RuleExpression { get; set; } = string.Empty;
    public Dictionary<string, object> RuleParameters { get; set; } = new();
    public List<string> ApplicablePaymentMethods { get; set; } = new();
    public List<string> ApplicableCurrencies { get; set; } = new();
    public List<string> ApplicableCountries { get; set; } = new();
    public DateTime? ValidFrom { get; set; }
    public DateTime? ValidTo { get; set; }
    public string Tags { get; set; } = string.Empty;
    public Dictionary<string, object> RuleMetadata { get; set; } = new();
    
    // Navigation
    public Team Team { get; set; } = null!;
}

public class RuleTestContext
{
    public Dictionary<string, object> TestData { get; set; } = new();
    public List<string> ExpectedResults { get; set; } = new();
    public Dictionary<string, object> TestMetadata { get; set; } = new();
}

public class RuleTestResult
{
    public string RuleId { get; set; } = string.Empty;
    public bool TestPassed { get; set; }
    public RuleEvaluationResult EvaluationResult { get; set; } = new();
    public List<string> TestErrors { get; set; } = new();
    public TimeSpan TestDuration { get; set; }
    public Dictionary<string, object> TestMetadata { get; set; } = new();
}

public class RulePerformanceStatistics
{
    public TimeSpan Period { get; set; }
    public int TotalRuleEvaluations { get; set; }
    public int SuccessfulEvaluations { get; set; }
    public int FailedEvaluations { get; set; }
    public double SuccessRate { get; set; }
    public TimeSpan AverageEvaluationTime { get; set; }
    public TimeSpan MaxEvaluationTime { get; set; }
    public Dictionary<RuleType, int> EvaluationsByType { get; set; } = new();
    public Dictionary<string, int> RuleHitCounts { get; set; } = new();
    public Dictionary<string, TimeSpan> RulePerformance { get; set; } = new();
    public Dictionary<string, int> TeamRuleUsage { get; set; } = new();
}

public class RuleChangeAuditLog : BaseEntity
{
    public string RuleId { get; set; } = string.Empty;
    public Guid TeamId { get; set; }
    public string Action { get; set; } = string.Empty; // CREATE, UPDATE, DELETE, ACTIVATE, DEACTIVATE
    public string OldRuleData { get; set; } = string.Empty;
    public string NewRuleData { get; set; } = string.Empty;
    public string ChangedBy { get; set; } = string.Empty;
    public string ChangeReason { get; set; } = string.Empty;
    public Dictionary<string, object> ChangeMetadata { get; set; } = new();
}

public class BusinessRuleEngineService : IBusinessRuleEngineService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IPaymentRepository _paymentRepository;
    private readonly ITeamRepository _teamRepository;
    private readonly IMemoryCache _cache;
    private readonly ILogger<BusinessRuleEngineService> _logger;
    
    // Rule storage (in production, this would be database-backed)
    private readonly ConcurrentDictionary<string, BusinessRule> _rules = new();
    private readonly ConcurrentDictionary<string, RuleChangeAuditLog> _auditLogs = new();
    
    // Performance tracking
    private readonly ConcurrentDictionary<string, RulePerformanceData> _performanceData = new();
    
    // Metrics
    private static readonly Counter RuleEvaluationOperations = Metrics
        .CreateCounter("rule_evaluation_operations_total", "Total rule evaluation operations", new[] { "team_id", "rule_type", "result" });
    
    private static readonly Histogram RuleEvaluationDuration = Metrics
        .CreateHistogram("rule_evaluation_duration_seconds", "Rule evaluation operation duration", new[] { "rule_type" });
    
    private static readonly Counter RuleViolations = Metrics
        .CreateCounter("rule_violations_total", "Total rule violations", new[] { "team_id", "rule_type", "severity" });
    
    private static readonly Gauge ActiveRules = Metrics
        .CreateGauge("active_business_rules_total", "Total active business rules", new[] { "team_id", "rule_type" });
    
    private static readonly Counter RuleChanges = Metrics
        .CreateCounter("rule_changes_total", "Total rule changes", new[] { "team_id", "action" });

    // Rule evaluation cache
    private readonly ConcurrentDictionary<string, CachedRuleResult> _evaluationCache = new();
    private readonly TimeSpan _cacheExpiry = TimeSpan.FromMinutes(5);

    public BusinessRuleEngineService(
        IServiceProvider serviceProvider,
        IPaymentRepository paymentRepository,
        ITeamRepository teamRepository,
        IMemoryCache cache,
        ILogger<BusinessRuleEngineService> logger)
    {
        _serviceProvider = serviceProvider;
        _paymentRepository = paymentRepository;
        _teamRepository = teamRepository;
        _cache = cache;
        _logger = logger;
        
        InitializeDefaultRules();
    }

    public async Task<RuleEvaluationResult> EvaluatePaymentRulesAsync(PaymentRuleContext context, CancellationToken cancellationToken = default)
    {
        using var activity = RuleEvaluationDuration.WithLabels("payment").NewTimer();
        var startTime = DateTime.UtcNow;
        
        try
        {
            var applicableRules = await GetApplicableRulesAsync(context.TeamId, RuleType.PAYMENT_LIMIT, cancellationToken);
            var result = new RuleEvaluationResult
            {
                IsAllowed = true,
                RuleType = RuleType.PAYMENT_LIMIT
            };

            foreach (var rule in applicableRules.OrderBy(r => r.Priority))
            {
                var ruleResult = await EvaluateRuleAsync(rule, context, cancellationToken);
                if (!ruleResult.IsAllowed)
                {
                    result.IsAllowed = false;
                    result.RuleId = rule.Id.ToString();
                    result.RuleName = rule.Name;
                    result.Message = ruleResult.Message;
                    result.Details = ruleResult.Details;
                    result.Priority = rule.Priority;
                    result.Violations.AddRange(ruleResult.Violations);
                    
                    RuleViolations.WithLabels(context.TeamId.ToString(), RuleType.PAYMENT_LIMIT.ToString(), "high").Inc();
                    break;
                }
                else if (ruleResult.IsWarning)
                {
                    result.IsWarning = true;
                    result.Message = ruleResult.Message;
                    result.Violations.AddRange(ruleResult.Violations);
                    
                    RuleViolations.WithLabels(context.TeamId.ToString(), RuleType.PAYMENT_LIMIT.ToString(), "low").Inc();
                }
            }

            result.EvaluationDuration = DateTime.UtcNow - startTime;
            
            RuleEvaluationOperations.WithLabels(context.TeamId.ToString(), "payment", result.IsAllowed ? "allowed" : "denied").Inc();
            
            _logger.LogInformation("Payment rules evaluated: TeamId: {TeamId}, PaymentId: {PaymentId}, Allowed: {IsAllowed}, Duration: {Duration}ms", 
                context.TeamId, context.PaymentId, result.IsAllowed, result.EvaluationDuration.TotalMilliseconds);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Payment rule evaluation failed: TeamId: {TeamId}, PaymentId: {PaymentId}", context.TeamId, context.PaymentId);
            RuleEvaluationOperations.WithLabels(context.TeamId.ToString(), "payment", "error").Inc();
            
            return new RuleEvaluationResult
            {
                IsAllowed = false,
                RuleType = RuleType.PAYMENT_LIMIT,
                Message = "Rule evaluation error",
                EvaluationDuration = DateTime.UtcNow - startTime
            };
        }
    }

    public async Task<RuleEvaluationResult> EvaluateAmountRulesAsync(AmountRuleContext context, CancellationToken cancellationToken = default)
    {
        using var activity = RuleEvaluationDuration.WithLabels("amount").NewTimer();
        var startTime = DateTime.UtcNow;
        
        try
        {
            var applicableRules = await GetApplicableRulesAsync(context.TeamId, RuleType.AMOUNT_VALIDATION, cancellationToken);
            var result = new RuleEvaluationResult
            {
                IsAllowed = true,
                RuleType = RuleType.AMOUNT_VALIDATION
            };

            foreach (var rule in applicableRules.OrderBy(r => r.Priority))
            {
                var ruleResult = await EvaluateAmountRuleAsync(rule, context, cancellationToken);
                if (!ruleResult.IsAllowed)
                {
                    result = ruleResult;
                    result.RuleId = rule.Id.ToString();
                    result.RuleName = rule.Name;
                    result.Priority = rule.Priority;
                    
                    RuleViolations.WithLabels(context.TeamId.ToString(), RuleType.AMOUNT_VALIDATION.ToString(), "medium").Inc();
                    break;
                }
                else if (ruleResult.IsWarning)
                {
                    result.IsWarning = true;
                    result.Message = ruleResult.Message;
                    result.Violations.AddRange(ruleResult.Violations);
                }
            }

            result.EvaluationDuration = DateTime.UtcNow - startTime;
            
            RuleEvaluationOperations.WithLabels(context.TeamId.ToString(), "amount", result.IsAllowed ? "allowed" : "denied").Inc();
            
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Amount rule evaluation failed: TeamId: {TeamId}, Amount: {Amount}", context.TeamId, context.Amount);
            RuleEvaluationOperations.WithLabels(context.TeamId.ToString(), "amount", "error").Inc();
            
            return new RuleEvaluationResult
            {
                IsAllowed = false,
                RuleType = RuleType.AMOUNT_VALIDATION,
                Message = "Amount validation error",
                EvaluationDuration = DateTime.UtcNow - startTime
            };
        }
    }

    public async Task<RuleEvaluationResult> EvaluateCurrencyRulesAsync(CurrencyRuleContext context, CancellationToken cancellationToken = default)
    {
        using var activity = RuleEvaluationDuration.WithLabels("currency").NewTimer();
        var startTime = DateTime.UtcNow;
        
        try
        {
            var applicableRules = await GetApplicableRulesAsync(context.TeamId, RuleType.CURRENCY_VALIDATION, cancellationToken);
            var result = new RuleEvaluationResult
            {
                IsAllowed = true,
                RuleType = RuleType.CURRENCY_VALIDATION
            };

            foreach (var rule in applicableRules.OrderBy(r => r.Priority))
            {
                var ruleResult = await EvaluateCurrencyRuleAsync(rule, context, cancellationToken);
                if (!ruleResult.IsAllowed)
                {
                    result = ruleResult;
                    result.RuleId = rule.Id.ToString();
                    result.RuleName = rule.Name;
                    result.Priority = rule.Priority;
                    
                    RuleViolations.WithLabels(context.TeamId.ToString(), RuleType.CURRENCY_VALIDATION.ToString(), "medium").Inc();
                    break;
                }
            }

            result.EvaluationDuration = DateTime.UtcNow - startTime;
            
            RuleEvaluationOperations.WithLabels(context.TeamId.ToString(), "currency", result.IsAllowed ? "allowed" : "denied").Inc();
            
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Currency rule evaluation failed: TeamId: {TeamId}, Currency: {Currency}", context.TeamId, context.Currency);
            RuleEvaluationOperations.WithLabels(context.TeamId.ToString(), "currency", "error").Inc();
            
            return new RuleEvaluationResult
            {
                IsAllowed = false,
                RuleType = RuleType.CURRENCY_VALIDATION,
                Message = "Currency validation error",
                EvaluationDuration = DateTime.UtcNow - startTime
            };
        }
    }

    public async Task<RuleEvaluationResult> EvaluateTeamRulesAsync(TeamRuleContext context, CancellationToken cancellationToken = default)
    {
        using var activity = RuleEvaluationDuration.WithLabels("team").NewTimer();
        var startTime = DateTime.UtcNow;
        
        try
        {
            var applicableRules = await GetApplicableRulesAsync(context.TeamId, RuleType.TEAM_RESTRICTION, cancellationToken);
            var result = new RuleEvaluationResult
            {
                IsAllowed = true,
                RuleType = RuleType.TEAM_RESTRICTION
            };

            foreach (var rule in applicableRules.OrderBy(r => r.Priority))
            {
                var ruleResult = await EvaluateTeamRuleAsync(rule, context, cancellationToken);
                if (!ruleResult.IsAllowed)
                {
                    result = ruleResult;
                    result.RuleId = rule.Id.ToString();
                    result.RuleName = rule.Name;
                    result.Priority = rule.Priority;
                    
                    RuleViolations.WithLabels(context.TeamId.ToString(), RuleType.TEAM_RESTRICTION.ToString(), "high").Inc();
                    break;
                }
            }

            result.EvaluationDuration = DateTime.UtcNow - startTime;
            
            RuleEvaluationOperations.WithLabels(context.TeamId.ToString(), "team", result.IsAllowed ? "allowed" : "denied").Inc();
            
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Team rule evaluation failed: TeamId: {TeamId}", context.TeamId);
            RuleEvaluationOperations.WithLabels(context.TeamId.ToString(), "team", "error").Inc();
            
            return new RuleEvaluationResult
            {
                IsAllowed = false,
                RuleType = RuleType.TEAM_RESTRICTION,
                Message = "Team validation error",
                EvaluationDuration = DateTime.UtcNow - startTime
            };
        }
    }

    public async Task<RuleEvaluationResult> EvaluateCustomerRulesAsync(CustomerRuleContext context, CancellationToken cancellationToken = default)
    {
        var startTime = DateTime.UtcNow;
        
        try
        {
            _logger.LogDebug("Evaluating customer rules for CustomerId: {CustomerId}, TeamId: {TeamId}", 
                context.CustomerId, context.TeamId);

            var result = new RuleEvaluationResult
            {
                IsAllowed = true,
                RuleType = RuleType.CUSTOMER_RESTRICTION,
                Message = "Customer validation passed"
            };

            // Basic customer validation rules
            if (context.IsBlacklisted)
            {
                result.IsAllowed = false;
                result.Message = "Customer is blacklisted";
                result.RuleName = "Customer Blacklist Check";
                result.RuleId = "CUSTOMER_BLACKLIST";
                return result;
            }

            // Fraud score validation
            if (context.FraudScore > 80.0)
            {
                result.IsAllowed = false;
                result.Message = $"Customer fraud score too high: {context.FraudScore}";
                result.RuleName = "Customer Fraud Score Check";
                result.RuleId = "CUSTOMER_FRAUD_SCORE";
                return result;
            }

            // Failed payment count validation
            if (context.FailedPaymentCount > 5)
            {
                result.IsAllowed = false;
                result.Message = $"Too many failed payments: {context.FailedPaymentCount}";
                result.RuleName = "Customer Failed Payment Check";
                result.RuleId = "CUSTOMER_FAILED_PAYMENTS";
                return result;
            }

            // Email validation
            if (string.IsNullOrEmpty(context.CustomerEmail) || !context.CustomerEmail.Contains('@'))
            {
                result.IsAllowed = false;
                result.Message = "Invalid customer email";
                result.RuleName = "Customer Email Validation";
                result.RuleId = "CUSTOMER_EMAIL";
                return result;
            }

            // Country validation (basic example)
            if (string.IsNullOrEmpty(context.CustomerCountry))
            {
                result.IsWarning = true;
                result.Message = "Customer country not specified";
                result.RuleName = "Customer Country Check";
                result.RuleId = "CUSTOMER_COUNTRY";
            }

            result.EvaluationDuration = DateTime.UtcNow - startTime;
            
            RuleEvaluationOperations.WithLabels(context.TeamId.ToString(), "customer", result.IsAllowed ? "allowed" : "denied").Inc();
            
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Customer rule evaluation failed: CustomerId: {CustomerId}, TeamId: {TeamId}", 
                context.CustomerId, context.TeamId);
            RuleEvaluationOperations.WithLabels(context.TeamId.ToString(), "customer", "error").Inc();
            
            return new RuleEvaluationResult
            {
                IsAllowed = false,
                RuleType = RuleType.CUSTOMER_RESTRICTION,
                Message = "Customer validation error",
                EvaluationDuration = DateTime.UtcNow - startTime
            };
        }
    }

    #region Rule Management

    public async Task<BusinessRule> CreateRuleAsync(BusinessRule rule, CancellationToken cancellationToken = default)
    {
        try
        {
            rule.Id = Guid.NewGuid();
            rule.CreatedAt = DateTime.UtcNow;
            rule.UpdatedAt = DateTime.UtcNow;

            // Validate rule expression
            var validationResult = await ValidateRuleExpressionAsync(rule, cancellationToken);
            if (!validationResult.IsValid)
            {
                throw new InvalidOperationException($"Invalid rule expression: {string.Join(", ", validationResult.Errors)}");
            }

            _rules.TryAdd(rule.Id.ToString(), rule);
            
            await LogRuleChangeAsync(rule.Id.ToString(), rule.TeamId, "CREATE", "", JsonSerializer.Serialize(rule), "System", "Rule created", cancellationToken);
            
            ActiveRules.WithLabels(rule.TeamId.ToString(), rule.Type.ToString()).Inc();
            RuleChanges.WithLabels(rule.TeamId.ToString(), "create").Inc();
            
            await RefreshRuleCacheAsync(cancellationToken);
            
            _logger.LogInformation("Business rule created: {RuleId}, Team: {TeamId}, Type: {Type}", rule.Id, rule.TeamId, rule.Type);
            
            return rule;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create business rule: Team: {TeamId}, Type: {Type}", rule.TeamId, rule.Type);
            throw;
        }
    }

    public async Task<BusinessRule> UpdateRuleAsync(BusinessRule rule, CancellationToken cancellationToken = default)
    {
        try
        {
            if (!_rules.TryGetValue(rule.Id.ToString(), out var existingRule))
            {
                throw new InvalidOperationException($"Rule not found: {rule.Id}");
            }

            var oldRuleData = JsonSerializer.Serialize(existingRule);
            
            rule.UpdatedAt = DateTime.UtcNow;
            
            // Validate rule expression
            var validationResult = await ValidateRuleExpressionAsync(rule, cancellationToken);
            if (!validationResult.IsValid)
            {
                throw new InvalidOperationException($"Invalid rule expression: {string.Join(", ", validationResult.Errors)}");
            }

            _rules.TryUpdate(rule.Id.ToString(), rule, existingRule);
            
            await LogRuleChangeAsync(rule.Id.ToString(), rule.TeamId, "UPDATE", oldRuleData, JsonSerializer.Serialize(rule), "System", "Rule updated", cancellationToken);
            
            RuleChanges.WithLabels(rule.TeamId.ToString(), "update").Inc();
            
            await RefreshRuleCacheAsync(cancellationToken);
            
            _logger.LogInformation("Business rule updated: {RuleId}, Team: {TeamId}, Type: {Type}", rule.Id, rule.TeamId, rule.Type);
            
            return rule;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update business rule: {RuleId}", rule.Id);
            throw;
        }
    }

    public async Task<bool> DeleteRuleAsync(string ruleId, CancellationToken cancellationToken = default)
    {
        try
        {
            if (!_rules.TryRemove(ruleId, out var rule))
            {
                return false;
            }

            await LogRuleChangeAsync(ruleId, rule.TeamId, "DELETE", JsonSerializer.Serialize(rule), "", "System", "Rule deleted", cancellationToken);
            
            ActiveRules.WithLabels(rule.TeamId.ToString(), rule.Type.ToString()).Dec();
            RuleChanges.WithLabels(rule.TeamId.ToString(), "delete").Inc();
            
            await RefreshRuleCacheAsync(cancellationToken);
            
            _logger.LogInformation("Business rule deleted: {RuleId}, Team: {TeamId}, Type: {Type}", ruleId, rule.TeamId, rule.Type);
            
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete business rule: {RuleId}", ruleId);
            return false;
        }
    }

    public async Task<IEnumerable<BusinessRule>> GetRulesAsync(Guid? teamId = null, RuleType? type = null, bool activeOnly = true, CancellationToken cancellationToken = default)
    {
        try
        {
            var query = _rules.Values.AsEnumerable();
            
            if (teamId.HasValue)
            {
                query = query.Where(r => r.TeamId == teamId.Value);
            }
            
            if (type.HasValue)
            {
                query = query.Where(r => r.Type == type.Value);
            }
            
            if (activeOnly)
            {
                query = query.Where(r => r.IsActive && (!r.ValidTo.HasValue || r.ValidTo.Value > DateTime.UtcNow));
            }
            
            return query.OrderBy(r => r.Priority).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get business rules: TeamId: {TeamId}, Type: {Type}", teamId, type);
            return new List<BusinessRule>();
        }
    }

    #endregion

    #region Rule Testing and Performance

    public async Task<RuleTestResult> TestRuleAsync(BusinessRule rule, RuleTestContext testContext, CancellationToken cancellationToken = default)
    {
        var startTime = DateTime.UtcNow;
        
        try
        {
            var result = new RuleTestResult
            {
                RuleId = rule.Id.ToString(),
                TestPassed = true
            };

            // Create test payment context from test data
            var paymentContext = CreateTestPaymentContext(testContext.TestData);
            
            // Evaluate rule
            result.EvaluationResult = await EvaluateRuleAsync(rule, paymentContext, cancellationToken);
            
            // Check against expected results
            foreach (var expectedResult in testContext.ExpectedResults)
            {
                if (!ValidateExpectedResult(result.EvaluationResult, expectedResult))
                {
                    result.TestPassed = false;
                    result.TestErrors.Add($"Expected result not met: {expectedResult}");
                }
            }

            result.TestDuration = DateTime.UtcNow - startTime;
            result.TestMetadata = testContext.TestMetadata;
            
            _logger.LogInformation("Rule test completed: {RuleId}, Passed: {TestPassed}, Duration: {Duration}ms", 
                rule.Id, result.TestPassed, result.TestDuration.TotalMilliseconds);
            
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Rule test failed: {RuleId}", rule.Id);
            
            return new RuleTestResult
            {
                RuleId = rule.Id.ToString(),
                TestPassed = false,
                TestErrors = new List<string> { ex.Message },
                TestDuration = DateTime.UtcNow - startTime
            };
        }
    }

    public async Task<RulePerformanceStatistics> GetRulePerformanceStatisticsAsync(TimeSpan? period = null, CancellationToken cancellationToken = default)
    {
        period ??= TimeSpan.FromDays(7);
        
        try
        {
            var cutoffTime = DateTime.UtcNow.Subtract(period.Value);
            var relevantData = _performanceData.Values
                .Where(p => p.LastEvaluated >= cutoffTime)
                .ToList();

            var stats = new RulePerformanceStatistics
            {
                Period = period.Value,
                TotalRuleEvaluations = relevantData.Sum(p => p.EvaluationCount),
                SuccessfulEvaluations = relevantData.Sum(p => p.SuccessCount),
                FailedEvaluations = relevantData.Sum(p => p.ErrorCount),
                AverageEvaluationTime = TimeSpan.FromMilliseconds(relevantData.Any() ? relevantData.Average(p => p.AverageEvaluationTime.TotalMilliseconds) : 0),
                MaxEvaluationTime = relevantData.Any() ? relevantData.Max(p => p.MaxEvaluationTime) : TimeSpan.Zero
            };

            stats.SuccessRate = stats.TotalRuleEvaluations > 0 ? (double)stats.SuccessfulEvaluations / stats.TotalRuleEvaluations : 0;

            // Group by rule type
            foreach (var rule in _rules.Values)
            {
                var ruleData = relevantData.FirstOrDefault(p => p.RuleId == rule.Id.ToString());
                if (ruleData != null)
                {
                    stats.EvaluationsByType.TryAdd(rule.Type, 0);
                    stats.EvaluationsByType[rule.Type] += ruleData.EvaluationCount;
                    
                    stats.RuleHitCounts[rule.Id.ToString()] = ruleData.EvaluationCount;
                    stats.RulePerformance[rule.Id.ToString()] = ruleData.AverageEvaluationTime;
                    
                    stats.TeamRuleUsage.TryAdd(rule.TeamId.ToString(), 0);
                    stats.TeamRuleUsage[rule.TeamId.ToString()] += ruleData.EvaluationCount;
                }
            }

            return stats;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get rule performance statistics");
            return new RulePerformanceStatistics { Period = period.Value };
        }
    }

    public async Task RefreshRuleCacheAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            _evaluationCache.Clear();
            _cache.Remove("business_rules_cache");
            
            _logger.LogInformation("Business rule cache refreshed");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to refresh rule cache");
        }
    }

    #endregion

    #region Private Helper Methods

    private async Task<IEnumerable<BusinessRule>> GetApplicableRulesAsync(Guid teamId, RuleType ruleType, CancellationToken cancellationToken)
    {
        var cacheKey = $"rules_{teamId}_{ruleType}";
        if (_cache.TryGetValue<IEnumerable<BusinessRule>>(cacheKey, out var cachedRules) && cachedRules != null)
        {
            return cachedRules;
        }

        var rules = await GetRulesAsync(teamId, ruleType, true, cancellationToken);
        _cache.Set(cacheKey, rules, TimeSpan.FromMinutes(10));
        
        return rules;
    }

    private async Task<RuleEvaluationResult> EvaluateRuleAsync(BusinessRule rule, object context, CancellationToken cancellationToken)
    {
        var startTime = DateTime.UtcNow;
        
        try
        {
            // Track performance
            UpdateRulePerformance(rule.Id.ToString(), startTime);
            
            var result = new RuleEvaluationResult
            {
                IsAllowed = true,
                RuleType = rule.Type
            };

            // Evaluate based on rule type
            switch (rule.Type)
            {
                case RuleType.PAYMENT_LIMIT:
                    result = await EvaluatePaymentLimitRule(rule, context as PaymentRuleContext, cancellationToken);
                    break;
                case RuleType.AMOUNT_VALIDATION:
                    result = await EvaluateAmountRule(rule, context as AmountRuleContext, cancellationToken);
                    break;
                case RuleType.CURRENCY_VALIDATION:
                    result = await EvaluateCurrencyRule(rule, context as CurrencyRuleContext, cancellationToken);
                    break;
                case RuleType.TEAM_RESTRICTION:
                    result = await EvaluateTeamRule(rule, context as TeamRuleContext, cancellationToken);
                    break;
                default:
                    result = await EvaluateCustomRule(rule, context, cancellationToken);
                    break;
            }

            var duration = DateTime.UtcNow - startTime;
            UpdateRulePerformance(rule.Id.ToString(), startTime, true, duration);
            
            return result;
        }
        catch (Exception ex)
        {
            var duration = DateTime.UtcNow - startTime;
            UpdateRulePerformance(rule.Id.ToString(), startTime, false, duration);
            
            _logger.LogError(ex, "Rule evaluation failed: {RuleId}", rule.Id);
            
            return new RuleEvaluationResult
            {
                IsAllowed = false,
                RuleType = rule.Type,
                Message = "Rule evaluation error",
                EvaluationDuration = duration
            };
        }
    }

    private async Task<RuleEvaluationResult> EvaluatePaymentLimitRule(BusinessRule rule, PaymentRuleContext? context, CancellationToken cancellationToken)
    {
        if (context == null)
        {
            return new RuleEvaluationResult { IsAllowed = false, Message = "Invalid context" };
        }

        var result = new RuleEvaluationResult { IsAllowed = true, RuleType = RuleType.PAYMENT_LIMIT };

        // Check daily limit
        if (rule.RuleParameters.TryGetValue("daily_limit", out var dailyLimitObj) && dailyLimitObj is decimal dailyLimit)
        {
            var dailyTotal = await GetDailyTotalAsync(context.TeamId, cancellationToken);
            if (dailyTotal + context.Amount > dailyLimit)
            {
                result.IsAllowed = false;
                result.Message = $"Daily payment limit exceeded: {dailyTotal + context.Amount:C} > {dailyLimit:C}";
                result.Violations.Add(new RuleViolation
                {
                    Field = "daily_amount",
                    Value = (dailyTotal + context.Amount).ToString(),
                    ExpectedValue = dailyLimit.ToString(),
                    ViolationType = "LIMIT_EXCEEDED",
                    Message = result.Message,
                    Severity = 4
                });
            }
        }

        // Check single transaction limit
        if (rule.RuleParameters.TryGetValue("transaction_limit", out var transactionLimitObj) && transactionLimitObj is decimal transactionLimit)
        {
            if (context.Amount > transactionLimit)
            {
                result.IsAllowed = false;
                result.Message = $"Single transaction limit exceeded: {context.Amount:C} > {transactionLimit:C}";
                result.Violations.Add(new RuleViolation
                {
                    Field = "transaction_amount",
                    Value = context.Amount.ToString(),
                    ExpectedValue = transactionLimit.ToString(),
                    ViolationType = "LIMIT_EXCEEDED",
                    Message = result.Message,
                    Severity = 3
                });
            }
        }

        return result;
    }

    private async Task<RuleEvaluationResult> EvaluateAmountRule(BusinessRule rule, AmountRuleContext? context, CancellationToken cancellationToken)
    {
        if (context == null)
        {
            return new RuleEvaluationResult { IsAllowed = false, Message = "Invalid context" };
        }

        var result = new RuleEvaluationResult { IsAllowed = true, RuleType = RuleType.AMOUNT_VALIDATION };

        // Check minimum amount
        if (rule.RuleParameters.TryGetValue("min_amount", out var minAmountObj) && minAmountObj is decimal minAmount)
        {
            if (context.Amount < minAmount)
            {
                result.IsAllowed = false;
                result.Message = $"Amount below minimum: {context.Amount:C} < {minAmount:C}";
            }
        }

        // Check maximum amount
        if (rule.RuleParameters.TryGetValue("max_amount", out var maxAmountObj) && maxAmountObj is decimal maxAmount)
        {
            if (context.Amount > maxAmount)
            {
                result.IsAllowed = false;
                result.Message = $"Amount above maximum: {context.Amount:C} > {maxAmount:C}";
            }
        }

        return result;
    }

    private async Task<RuleEvaluationResult> EvaluateCurrencyRule(BusinessRule rule, CurrencyRuleContext? context, CancellationToken cancellationToken)
    {
        if (context == null)
        {
            return new RuleEvaluationResult { IsAllowed = false, Message = "Invalid context" };
        }

        var result = new RuleEvaluationResult { IsAllowed = true, RuleType = RuleType.CURRENCY_VALIDATION };

        // Check allowed currencies
        if (rule.ApplicableCurrencies.Any() && !rule.ApplicableCurrencies.Contains(context.Currency))
        {
            result.IsAllowed = false;
            result.Message = $"Currency not supported: {context.Currency}. Allowed: {string.Join(", ", rule.ApplicableCurrencies)}";
        }

        return result;
    }

    private async Task<RuleEvaluationResult> EvaluateTeamRule(BusinessRule rule, TeamRuleContext? context, CancellationToken cancellationToken)
    {
        if (context == null)
        {
            return new RuleEvaluationResult { IsAllowed = false, Message = "Invalid context" };
        }

        var result = new RuleEvaluationResult { IsAllowed = true, RuleType = RuleType.TEAM_RESTRICTION };

        // Check if team is active
        if (!context.IsActive)
        {
            result.IsAllowed = false;
            result.Message = "Team is not active";
        }

        // Check risk score
        if (rule.RuleParameters.TryGetValue("max_risk_score", out var maxRiskScoreObj) && maxRiskScoreObj is double maxRiskScore)
        {
            if (context.RiskScore > maxRiskScore)
            {
                result.IsAllowed = false;
                result.Message = $"Team risk score too high: {context.RiskScore} > {maxRiskScore}";
            }
        }

        return result;
    }

    private async Task<RuleEvaluationResult> EvaluateCustomRule(BusinessRule rule, object context, CancellationToken cancellationToken)
    {
        // Custom rule evaluation based on rule expression
        // This would implement a more sophisticated rule engine in production
        return new RuleEvaluationResult
        {
            IsAllowed = true,
            RuleType = rule.Type,
            Message = "Custom rule evaluation not implemented"
        };
    }

    private async Task<decimal> GetDailyTotalAsync(Guid teamId, CancellationToken cancellationToken)
    {
        // This would query actual payment data in production
        // For now, return simulated daily total
        return 150000m; // 1,500 RUB
    }

    private void UpdateRulePerformance(string ruleId, DateTime startTime, bool success = false, TimeSpan? duration = null)
    {
        var data = _performanceData.GetOrAdd(ruleId, _ => new RulePerformanceData { RuleId = ruleId });
        
        data.EvaluationCount++;
        data.LastEvaluated = DateTime.UtcNow;
        
        if (success)
        {
            data.SuccessCount++;
        }
        else
        {
            data.ErrorCount++;
        }
        
        if (duration.HasValue)
        {
            data.TotalEvaluationTime = data.TotalEvaluationTime.Add(duration.Value);
            data.AverageEvaluationTime = TimeSpan.FromTicks(data.TotalEvaluationTime.Ticks / data.EvaluationCount);
            
            if (duration.Value > data.MaxEvaluationTime)
            {
                data.MaxEvaluationTime = duration.Value;
            }
        }
    }

    private async Task LogRuleChangeAsync(string ruleId, Guid teamId, string action, string oldData, string newData, string changedBy, string reason, CancellationToken cancellationToken)
    {
        try
        {
            var auditLog = new RuleChangeAuditLog
            {
                Id = Guid.NewGuid(),
                RuleId = ruleId,
                TeamId = teamId,
                Action = action,
                OldRuleData = oldData,
                NewRuleData = newData,
                ChangedBy = changedBy,
                ChangeReason = reason,
                CreatedAt = DateTime.UtcNow,
                ChangeMetadata = new Dictionary<string, object>
                {
                    ["change_timestamp"] = DateTime.UtcNow,
                    ["change_source"] = "BusinessRuleEngine"
                }
            };

            _auditLogs.TryAdd(auditLog.Id.ToString(), auditLog);
            
            _logger.LogInformation("Rule change logged: {RuleId}, Action: {Action}, ChangedBy: {ChangedBy}", 
                ruleId, action, changedBy);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to log rule change: {RuleId}, Action: {Action}", ruleId, action);
        }
    }

    private async Task<RuleValidationResult> ValidateRuleExpressionAsync(BusinessRule rule, CancellationToken cancellationToken)
    {
        // Simplified rule expression validation
        // In production, this would use a proper expression parser
        var result = new RuleValidationResult { IsValid = true };
        
        if (string.IsNullOrWhiteSpace(rule.RuleExpression))
        {
            result.IsValid = false;
            result.Errors.Add("Rule expression cannot be empty");
        }
        
        return result;
    }

    private PaymentRuleContext CreateTestPaymentContext(Dictionary<string, object> testData)
    {
        return new PaymentRuleContext
        {
            PaymentId = testData.TryGetValue("payment_id", out var paymentId) ? Guid.Parse(paymentId.ToString()!) : Guid.NewGuid(),
            TeamId = testData.TryGetValue("team_id", out var teamId) ? Guid.Parse(teamId.ToString()!) : Guid.NewGuid(),
            Amount = testData.TryGetValue("amount", out var amount) ? Convert.ToDecimal(amount) : 1000m,
            Currency = testData.TryGetValue("currency", out var currency) ? currency.ToString() ?? "RUB" : "RUB",
            PaymentDate = DateTime.UtcNow
        };
    }

    private bool ValidateExpectedResult(RuleEvaluationResult result, string expectedResult)
    {
        return expectedResult.ToLower() switch
        {
            "allowed" => result.IsAllowed,
            "denied" => !result.IsAllowed,
            "warning" => result.IsWarning,
            _ => true
        };
    }

    private void InitializeDefaultRules()
    {
        var defaultRules = new[]
        {
            new BusinessRule
            {
                Id = Guid.Parse("11111111-1111-1111-1111-111111111111"),
                TeamId = Guid.Empty, // Global rule
                Name = "Default Daily Limit",
                Description = "Default daily payment limit for all teams",
                Type = RuleType.PAYMENT_LIMIT,
                Action = RuleAction.DENY,
                Priority = 100,
                IsActive = true,
                RuleExpression = "daily_total + amount <= daily_limit",
                RuleParameters = new Dictionary<string, object> { ["daily_limit"] = 1000000m }, // 10,000 RUB
                ApplicableCurrencies = new List<string> { "RUB", "USD", "EUR" }
            },
            new BusinessRule
            {
                Id = Guid.Parse("22222222-2222-2222-2222-222222222222"),
                TeamId = Guid.Empty,
                Name = "Default Transaction Limit",
                Description = "Default single transaction limit",
                Type = RuleType.PAYMENT_LIMIT,
                Action = RuleAction.DENY,
                Priority = 200,
                IsActive = true,
                RuleExpression = "amount <= transaction_limit",
                RuleParameters = new Dictionary<string, object> { ["transaction_limit"] = 500000m }, // 5,000 RUB
                ApplicableCurrencies = new List<string> { "RUB", "USD", "EUR" }
            },
            new BusinessRule
            {
                Id = Guid.Parse("33333333-3333-3333-3333-333333333333"),
                TeamId = Guid.Empty,
                Name = "Minimum Amount",
                Description = "Minimum payment amount validation",
                Type = RuleType.AMOUNT_VALIDATION,
                Action = RuleAction.DENY,
                Priority = 50,
                IsActive = true,
                RuleExpression = "amount >= min_amount",
                RuleParameters = new Dictionary<string, object> { ["min_amount"] = 100m }, // 1 RUB
                ApplicableCurrencies = new List<string> { "RUB", "USD", "EUR" }
            }
        };

        foreach (var rule in defaultRules)
        {
            _rules.TryAdd(rule.Id.ToString(), rule);
            ActiveRules.WithLabels(rule.TeamId.ToString(), rule.Type.ToString()).Inc();
        }
    }

    #endregion

    #region Helper Classes

    private class RulePerformanceData
    {
        public string RuleId { get; set; } = string.Empty;
        public int EvaluationCount { get; set; }
        public int SuccessCount { get; set; }
        public int ErrorCount { get; set; }
        public DateTime LastEvaluated { get; set; }
        public TimeSpan TotalEvaluationTime { get; set; }
        public TimeSpan AverageEvaluationTime { get; set; }
        public TimeSpan MaxEvaluationTime { get; set; }
    }

    private class CachedRuleResult
    {
        public RuleEvaluationResult Result { get; set; } = new();
        public DateTime CachedAt { get; set; }
        public TimeSpan Expiry { get; set; }
        
        public bool IsExpired => DateTime.UtcNow - CachedAt > Expiry;
    }

    private class RuleValidationResult
    {
        public bool IsValid { get; set; }
        public List<string> Errors { get; set; } = new();
    }

    // Missing method implementations
    private async Task<RuleEvaluationResult> EvaluateAmountRuleAsync(BusinessRule rule, AmountRuleContext context, CancellationToken cancellationToken)
    {
        // Simplified implementation for compilation
        return new RuleEvaluationResult
        {
            IsAllowed = context.Amount <= 1000000, // Basic amount limit
            RuleType = RuleType.AMOUNT_VALIDATION,
            Message = context.Amount > 1000000 ? "Amount exceeds limit" : ""
        };
    }

    private async Task<RuleEvaluationResult> EvaluateCurrencyRuleAsync(BusinessRule rule, CurrencyRuleContext context, CancellationToken cancellationToken)
    {
        // Simplified implementation for compilation
        var allowedCurrencies = new[] { "RUB", "USD", "EUR" };
        return new RuleEvaluationResult
        {
            IsAllowed = allowedCurrencies.Contains(context.Currency ?? "RUB"),
            RuleType = RuleType.CURRENCY_VALIDATION,
            Message = !allowedCurrencies.Contains(context.Currency ?? "RUB") ? "Currency not allowed" : ""
        };
    }

    private async Task<RuleEvaluationResult> EvaluateTeamRuleAsync(BusinessRule rule, TeamRuleContext context, CancellationToken cancellationToken)
    {
        // Simplified implementation for compilation
        return new RuleEvaluationResult
        {
            IsAllowed = context.TeamId != Guid.Empty, // Basic team validation
            RuleType = RuleType.TEAM_RESTRICTION,
            Message = context.TeamId == Guid.Empty ? "Invalid team" : ""
        };
    }

    #endregion
}