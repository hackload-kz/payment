// SPDX-License-Identifier: MIT
// Copyright (c) 2025 HackLoad Payment Gateway

using PaymentGateway.Core.Entities;
using PaymentGateway.Core.Interfaces;
using PaymentGateway.Core.Repositories;
using PaymentGateway.Core.Enums;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Text.Json;
using Prometheus;

namespace PaymentGateway.Core.Services;

/// <summary>
/// Basic payment fraud detection service with extensible hooks for future enhancement
/// </summary>
public interface IPaymentFraudDetectionService
{
    Task<FraudDetectionResult> AnalyzePaymentAsync(Payment payment, CancellationToken cancellationToken = default);
    Task<FraudDetectionResult> AnalyzeTransactionPatternAsync(long paymentId, CancellationToken cancellationToken = default);
    Task<RiskScore> CalculateRiskScoreAsync(Payment payment, CancellationToken cancellationToken = default);
    Task RegisterFraudDetectionHookAsync(IFraudDetectionHook hook);
    Task UnregisterFraudDetectionHookAsync(string hookId);
    Task<FraudDetectionStatistics> GetFraudStatisticsAsync(Guid? teamId = null, TimeSpan? period = null, CancellationToken cancellationToken = default);
    Task<IEnumerable<FraudAlert>> GetActiveFraudAlertsAsync(CancellationToken cancellationToken = default);
    Task UpdateFraudRulesAsync(Guid teamId, FraudDetectionRules rules, CancellationToken cancellationToken = default);
    Task<FraudDetectionRules> GetFraudRulesAsync(Guid teamId, CancellationToken cancellationToken = default);
}

public enum FraudRiskLevel
{
    Low,
    Medium,
    High,
    Critical
}

public class FraudDetectionResult
{
    public long PaymentId { get; set; }
    public FraudRiskLevel RiskLevel { get; set; }
    public RiskScore RiskScore { get; set; }
    public List<FraudIndicator> Indicators { get; set; } = new();
    public List<string> RecommendedActions { get; set; } = new();
    public bool ShouldBlock { get; set; }
    public string BlockReason { get; set; }
    public Dictionary<string, object> AnalysisMetadata { get; set; } = new();
    public DateTime AnalyzedAt { get; set; } = DateTime.UtcNow;
}

public class RiskScore
{
    public double OverallScore { get; set; } // 0-100
    public Dictionary<string, double> ComponentScores { get; set; } = new();
    public string ScoreBreakdown { get; set; }
    public DateTime CalculatedAt { get; set; } = DateTime.UtcNow;
}

public class FraudIndicator
{
    public string IndicatorType { get; set; }
    public string Description { get; set; }
    public double Weight { get; set; }
    public double Score { get; set; }
    public Dictionary<string, object> Details { get; set; } = new();
}

public class FraudAlert
{
    public string AlertId { get; set; }
    public string PaymentId { get; set; }
    public Guid TeamId { get; set; }
    public FraudRiskLevel RiskLevel { get; set; }
    public string AlertType { get; set; }
    public string Description { get; set; }
    public DateTime CreatedAt { get; set; }
    public bool IsActive { get; set; }
    public Dictionary<string, object> AlertData { get; set; } = new();
}

public class FraudDetectionStatistics
{
    public TimeSpan Period { get; set; }
    public int TotalPaymentsAnalyzed { get; set; }
    public int FraudDetected { get; set; }
    public int FalsePositives { get; set; }
    public double FraudRate { get; set; }
    public double DetectionAccuracy { get; set; }
    public Dictionary<FraudRiskLevel, int> DetectionsByRiskLevel { get; set; } = new();
    public Dictionary<string, int> DetectionsByIndicatorType { get; set; } = new();
    public decimal TotalAmountPrevented { get; set; }
}

public class FraudDetectionRules
{
    public Guid TeamId { get; set; }
    public bool EnableFraudDetection { get; set; } = true;
    public decimal HighValueThreshold { get; set; } = 100000; // Minor units
    public int MaxDailyTransactions { get; set; } = 1000;
    public decimal MaxDailyAmount { get; set; } = 10000000; // Minor units
    public int VelocityCheckMinutes { get; set; } = 60;
    public int MaxVelocityTransactions { get; set; } = 10;
    public List<string> BlockedCountries { get; set; } = new();
    public Dictionary<string, double> RiskWeights { get; set; } = new()
    {
        ["amount"] = 0.3,
        ["velocity"] = 0.4,
        ["pattern"] = 0.2,
        ["location"] = 0.1
    };
    public double BlockThreshold { get; set; } = 80.0; // Risk score threshold for blocking
    public bool EnablePatternAnalysis { get; set; } = true;
    public bool EnableVelocityChecks { get; set; } = true;
}

public interface IFraudDetectionHook
{
    string HookId { get; }
    string Name { get; }
    int Priority { get; }
    Task<FraudHookResult> ExecuteAsync(Payment payment, FraudDetectionContext context, CancellationToken cancellationToken = default);
}

public class FraudHookResult
{
    public bool ShouldBlock { get; set; }
    public double RiskScore { get; set; }
    public List<FraudIndicator> Indicators { get; set; } = new();
    public string BlockReason { get; set; }
    public Dictionary<string, object> Metadata { get; set; } = new();
}

public class FraudDetectionContext
{
    public DateTime AnalysisTime { get; set; } = DateTime.UtcNow;
    public FraudDetectionRules Rules { get; set; }
    public List<Payment> RecentPayments { get; set; } = new();
    public Dictionary<string, object> AdditionalData { get; set; } = new();
}

public class PaymentFraudDetectionService : IPaymentFraudDetectionService
{
    private readonly IPaymentRepository _paymentRepository;
    private readonly ICustomerRepository _customerRepository;
    private readonly ITeamRepository _teamRepository;
    private readonly ILogger<PaymentFraudDetectionService> _logger;
    
    // Fraud detection hooks registry
    private readonly ConcurrentDictionary<string, IFraudDetectionHook> _registeredHooks = new();
    private readonly ConcurrentDictionary<Guid, FraudDetectionRules> _teamRules = new();
    private readonly ConcurrentDictionary<string, FraudAlert> _activeAlerts = new();
    
    // Metrics
    private static readonly Counter FraudDetectionOperations = Metrics
        .CreateCounter("fraud_detection_operations_total", "Total fraud detection operations", new[] { "team_id", "risk_level", "result" });
    
    private static readonly Histogram FraudDetectionDuration = Metrics
        .CreateHistogram("fraud_detection_duration_seconds", "Fraud detection analysis duration");
    
    private static readonly Gauge ActiveFraudAlerts = Metrics
        .CreateGauge("active_fraud_alerts_total", "Total active fraud alerts", new[] { "risk_level" });
    
    private static readonly Counter FraudPreventedAmount = Metrics
        .CreateCounter("fraud_prevented_amount_total", "Total amount prevented by fraud detection", new[] { "team_id" });

    // Default fraud rules
    private static readonly FraudDetectionRules DefaultRules = new();

    public PaymentFraudDetectionService(
        IPaymentRepository paymentRepository,
        ICustomerRepository customerRepository,
        ITeamRepository teamRepository,
        ILogger<PaymentFraudDetectionService> logger)
    {
        _paymentRepository = paymentRepository;
        _customerRepository = customerRepository;
        _teamRepository = teamRepository;
        _logger = logger;
        
        // Register default fraud detection hooks
        RegisterDefaultHooks();
    }

    public async Task<FraudDetectionResult> AnalyzePaymentAsync(Payment payment, CancellationToken cancellationToken = default)
    {
        using var activity = FraudDetectionDuration.NewTimer();
        
        try
        {
            var rules = await GetFraudRulesAsync(payment.TeamId, cancellationToken);
            if (!rules.EnableFraudDetection)
            {
                return new FraudDetectionResult
                {
                    PaymentId = payment.PaymentId.GetHashCode(), // TODO: Fix data model - convert string PaymentId to long
                    RiskLevel = FraudRiskLevel.Low,
                    RiskScore = new RiskScore { OverallScore = 0 }
                };
            }

            var result = new FraudDetectionResult
            {
                PaymentId = payment.PaymentId.GetHashCode(), // TODO: Fix data model - convert string PaymentId to long
                Indicators = new List<FraudIndicator>()
            };

            // Calculate risk score
            result.RiskScore = await CalculateRiskScoreAsync(payment, cancellationToken);
            
            // Execute fraud detection hooks
            var context = new FraudDetectionContext
            {
                Rules = rules,
                RecentPayments = await GetRecentPaymentsForAnalysis(payment, cancellationToken)
            };

            await ExecuteFraudDetectionHooksAsync(payment, context, result, cancellationToken);

            // Determine risk level and actions
            result.RiskLevel = DetermineRiskLevel(result.RiskScore.OverallScore);
            result.RecommendedActions = GenerateRecommendedActions(result);
            
            // Check if payment should be blocked
            result.ShouldBlock = result.RiskScore.OverallScore >= rules.BlockThreshold;
            if (result.ShouldBlock)
            {
                result.BlockReason = $"High fraud risk score: {result.RiskScore.OverallScore:F1}";
            }

            // Create fraud alert if necessary
            if (result.RiskLevel >= FraudRiskLevel.High)
            {
                await CreateFraudAlertAsync(payment, result, cancellationToken);
            }

            // Record metrics
            FraudDetectionOperations.WithLabels(payment.TeamId.ToString(), result.RiskLevel.ToString(), 
                result.ShouldBlock ? "blocked" : "allowed").Inc();
            
            if (result.ShouldBlock)
            {
                FraudPreventedAmount.WithLabels(payment.TeamId.ToString()).Inc((double)payment.Amount);
            }

            _logger.LogInformation("Fraud analysis completed: PaymentId: {PaymentId}, RiskLevel: {RiskLevel}, Score: {Score:F1}, Blocked: {Blocked}", 
                payment.PaymentId, result.RiskLevel, result.RiskScore.OverallScore, result.ShouldBlock);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fraud detection analysis failed: {PaymentId}", payment.PaymentId);
            
            // Return safe default on error
            return new FraudDetectionResult
            {
                PaymentId = payment.PaymentId.GetHashCode(), // TODO: Fix data model - convert string PaymentId to long
                RiskLevel = FraudRiskLevel.Medium,
                RiskScore = new RiskScore { OverallScore = 50 },
                RecommendedActions = new List<string> { "Manual review required due to analysis error" }
            };
        }
    }

    public async Task<FraudDetectionResult> AnalyzeTransactionPatternAsync(long paymentId, CancellationToken cancellationToken = default)
    {
        try
        {
            // TODO: Fix data model inconsistency - method expects long but repository uses Guid
            var guidBytes = new byte[16];
            BitConverter.GetBytes(paymentId).CopyTo(guidBytes, 0);
            var payment = await _paymentRepository.GetByIdAsync(new Guid(guidBytes), cancellationToken);
            if (payment == null)
            {
                throw new ArgumentException("Payment not found", nameof(paymentId));
            }

            // Get recent payments for pattern analysis
            var recentPayments = await GetRecentPaymentsForAnalysis(payment, cancellationToken);
            
            var result = new FraudDetectionResult
            {
                PaymentId = paymentId,
                Indicators = new List<FraudIndicator>()
            };

            // Analyze velocity patterns
            var velocityIndicator = AnalyzeVelocityPattern(payment, recentPayments);
            if (velocityIndicator != null)
            {
                result.Indicators.Add(velocityIndicator);
            }

            // Analyze amount patterns
            var amountIndicator = AnalyzeAmountPattern(payment, recentPayments);
            if (amountIndicator != null)
            {
                result.Indicators.Add(amountIndicator);
            }

            // Analyze timing patterns
            var timingIndicator = AnalyzeTimingPattern(payment, recentPayments);
            if (timingIndicator != null)
            {
                result.Indicators.Add(timingIndicator);
            }

            // Calculate pattern-based risk score
            var patternScore = result.Indicators.Sum(i => i.Score * i.Weight);
            result.RiskScore = new RiskScore
            {
                OverallScore = Math.Min(patternScore, 100),
                ComponentScores = new Dictionary<string, double>
                {
                    ["velocity"] = velocityIndicator?.Score ?? 0,
                    ["amount"] = amountIndicator?.Score ?? 0,  
                    ["timing"] = timingIndicator?.Score ?? 0
                }
            };

            result.RiskLevel = DetermineRiskLevel(result.RiskScore.OverallScore);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Transaction pattern analysis failed: {PaymentId}", paymentId);
            throw;
        }
    }

    public async Task<RiskScore> CalculateRiskScoreAsync(Payment payment, CancellationToken cancellationToken = default)
    {
        try
        {
            var rules = await GetFraudRulesAsync(payment.TeamId, cancellationToken);
            var riskScore = new RiskScore
            {
                ComponentScores = new Dictionary<string, double>()
            };

            // Amount-based risk
            var amountRisk = CalculateAmountRisk(payment.Amount, rules.HighValueThreshold);
            riskScore.ComponentScores["amount"] = amountRisk;

            // Velocity-based risk (requires recent payment data)
            var recentPayments = await GetRecentPaymentsForAnalysis(payment, cancellationToken);
            var velocityRisk = CalculateVelocityRisk(payment, recentPayments, rules);
            riskScore.ComponentScores["velocity"] = velocityRisk;

            // Pattern-based risk
            var patternRisk = CalculatePatternRisk(payment, recentPayments);
            riskScore.ComponentScores["pattern"] = patternRisk;

            // Location-based risk (placeholder - would need additional data)
            var locationRisk = CalculateLocationRisk(payment);
            riskScore.ComponentScores["location"] = locationRisk;

            // Calculate weighted overall score
            riskScore.OverallScore = 0;
            foreach (var component in riskScore.ComponentScores)
            {
                if (rules.RiskWeights.TryGetValue(component.Key, out var weight))
                {
                    riskScore.OverallScore += component.Value * weight;
                }
            }

            riskScore.OverallScore = Math.Min(riskScore.OverallScore, 100);
            riskScore.ScoreBreakdown = JsonSerializer.Serialize(riskScore.ComponentScores);

            return riskScore;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Risk score calculation failed: {PaymentId}", payment.PaymentId);
            return new RiskScore { OverallScore = 50 }; // Safe default
        }
    }

    public async Task RegisterFraudDetectionHookAsync(IFraudDetectionHook hook)
    {
        try
        {
            _registeredHooks.AddOrUpdate(hook.HookId, hook, (k, v) => hook);
            _logger.LogInformation("Fraud detection hook registered: {HookId} - {Name}", hook.HookId, hook.Name);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to register fraud detection hook: {HookId}", hook.HookId);
            throw;
        }
    }

    public async Task UnregisterFraudDetectionHookAsync(string hookId)
    {
        try
        {
            if (_registeredHooks.TryRemove(hookId, out var hook))
            {
                _logger.LogInformation("Fraud detection hook unregistered: {HookId} - {Name}", hookId, hook.Name);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to unregister fraud detection hook: {HookId}", hookId);
            throw;
        }
    }

    public async Task<FraudDetectionStatistics> GetFraudStatisticsAsync(Guid? teamId = null, TimeSpan? period = null, CancellationToken cancellationToken = default)
    {
        try
        {
            period ??= TimeSpan.FromDays(7);
            
            // This would typically query fraud detection database/logs
            // For now, return simulated statistics
            var stats = new FraudDetectionStatistics
            {
                Period = period.Value,
                TotalPaymentsAnalyzed = 10000,
                FraudDetected = 25,
                FalsePositives = 5,
                FraudRate = 0.0025,
                DetectionAccuracy = 0.80,
                DetectionsByRiskLevel = new Dictionary<FraudRiskLevel, int>
                {
                    [FraudRiskLevel.Low] = 9900,
                    [FraudRiskLevel.Medium] = 75,
                    [FraudRiskLevel.High] = 20,
                    [FraudRiskLevel.Critical] = 5
                },
                TotalAmountPrevented = 250000
            };

            return stats;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get fraud statistics");
            return new FraudDetectionStatistics();
        }
    }

    public async Task<IEnumerable<FraudAlert>> GetActiveFraudAlertsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return _activeAlerts.Values.Where(a => a.IsActive).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get active fraud alerts");
            return new List<FraudAlert>();
        }
    }

    public async Task UpdateFraudRulesAsync(Guid teamId, FraudDetectionRules rules, CancellationToken cancellationToken = default)
    {
        try
        {
            rules.TeamId = teamId;
            _teamRules.AddOrUpdate(teamId, rules, (k, v) => rules);
            
            _logger.LogInformation("Fraud detection rules updated for team: {TeamId}", teamId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update fraud rules for team: {TeamId}", teamId);
            throw;
        }
    }

    public async Task<FraudDetectionRules> GetFraudRulesAsync(Guid teamId, CancellationToken cancellationToken = default)
    {
        try
        {
            if (_teamRules.TryGetValue(teamId, out var rules))
            {
                return rules;
            }

            // Load from database or use default
            var defaultRules = new FraudDetectionRules { TeamId = teamId };
            _teamRules.TryAdd(teamId, defaultRules);
            
            return defaultRules;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get fraud rules for team: {TeamId}", teamId);
            return DefaultRules;
        }
    }

    private void RegisterDefaultHooks()
    {
        // Register built-in fraud detection hooks
        var defaultHooks = new List<IFraudDetectionHook>
        {
            new HighValueTransactionHook(),
            new VelocityCheckHook(),
            new PatternAnalysisHook()
        };

        foreach (var hook in defaultHooks)
        {
            _registeredHooks.TryAdd(hook.HookId, hook);
        }

        _logger.LogInformation("Registered {Count} default fraud detection hooks", defaultHooks.Count);
    }

    private async Task ExecuteFraudDetectionHooksAsync(Payment payment, FraudDetectionContext context, FraudDetectionResult result, CancellationToken cancellationToken)
    {
        var hooks = _registeredHooks.Values.OrderBy(h => h.Priority).ToList();
        
        foreach (var hook in hooks)
        {
            try
            {
                var hookResult = await hook.ExecuteAsync(payment, context, cancellationToken);
                
                if (hookResult.ShouldBlock)
                {
                    result.ShouldBlock = true;
                    result.BlockReason = hookResult.BlockReason;
                }

                result.Indicators.AddRange(hookResult.Indicators);
                
                // Merge metadata
                foreach (var kvp in hookResult.Metadata)
                {
                    result.AnalysisMetadata[$"{hook.HookId}_{kvp.Key}"] = kvp.Value;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Fraud detection hook failed: {HookId}", hook.HookId);
                // Continue with other hooks
            }
        }
    }

    private async Task<List<Payment>> GetRecentPaymentsForAnalysis(Payment payment, CancellationToken cancellationToken)
    {
        try
        {
            // Get recent payments from the same team for pattern analysis
            var recentPayments = await _paymentRepository.GetRecentPaymentsByTeamAsync(
                payment.TeamId, 
                100, 
                cancellationToken);
            
            return recentPayments.Where(p => p.PaymentId != payment.PaymentId).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get recent payments for analysis");
            return new List<Payment>();
        }
    }

    private FraudIndicator AnalyzeVelocityPattern(Payment payment, List<Payment> recentPayments)
    {
        var recentCount = recentPayments.Count(p => 
            DateTime.UtcNow - p.CreatedAt < TimeSpan.FromMinutes(60));
        
        if (recentCount > 10) // Configurable threshold
        {
            return new FraudIndicator
            {
                IndicatorType = "velocity",
                Description = $"High transaction velocity: {recentCount} transactions in last hour",
                Weight = 0.4,
                Score = Math.Min(recentCount * 5, 50), // Cap at 50
                Details = new Dictionary<string, object> { ["transaction_count"] = recentCount }
            };
        }

        return null;
    }

    private FraudIndicator AnalyzeAmountPattern(Payment payment, List<Payment> recentPayments)
    {
        if (!recentPayments.Any()) return null;

        var avgAmount = recentPayments.Average(p => (double)p.Amount);
        var deviation = Math.Abs((double)payment.Amount - avgAmount) / avgAmount;

        if (deviation > 5.0) // More than 5x deviation
        {
            return new FraudIndicator
            {
                IndicatorType = "amount",
                Description = $"Unusual payment amount: {deviation:F1}x deviation from average",
                Weight = 0.3,
                Score = Math.Min(deviation * 5, 40),
                Details = new Dictionary<string, object> 
                { 
                    ["amount"] = payment.Amount,
                    ["average_amount"] = avgAmount,
                    ["deviation"] = deviation
                }
            };
        }

        return null;
    }

    private FraudIndicator AnalyzeTimingPattern(Payment payment, List<Payment> recentPayments)
    {
        var hour = payment.CreatedAt.Hour;
        
        // Flag transactions outside business hours (22:00 - 06:00) as potentially suspicious
        if (hour >= 22 || hour <= 6)
        {
            return new FraudIndicator
            {
                IndicatorType = "timing",
                Description = $"Transaction outside business hours: {hour:00}:00",
                Weight = 0.1,
                Score = 15,
                Details = new Dictionary<string, object> { ["hour"] = hour }
            };
        }

        return null;
    }

    private double CalculateAmountRisk(decimal amount, decimal highValueThreshold)
    {
        if (amount >= highValueThreshold)
        {
            return Math.Min(((double)amount / (double)highValueThreshold) * 20, 50);
        }
        return 0;
    }

    private double CalculateVelocityRisk(Payment payment, List<Payment> recentPayments, FraudDetectionRules rules)
    {
        var recentCount = recentPayments.Count(p => 
            DateTime.UtcNow - p.CreatedAt < TimeSpan.FromMinutes(rules.VelocityCheckMinutes));
        
        if (recentCount >= rules.MaxVelocityTransactions)
        {
            return Math.Min((double)recentCount / rules.MaxVelocityTransactions * 30, 40);
        }
        return 0;
    }

    private double CalculatePatternRisk(Payment payment, List<Payment> recentPayments)
    {
        // Simple pattern analysis - could be much more sophisticated
        if (recentPayments.Count > 5)
        {
            var sameAmountCount = recentPayments.Count(p => p.Amount == payment.Amount);
            if (sameAmountCount >= 3)
            {
                return 25; // Repeated same amounts might be suspicious
            }
        }
        return 0;
    }

    private double CalculateLocationRisk(Payment payment)
    {
        // Placeholder for location-based risk
        // Would need IP geolocation or other location data
        return 0;
    }

    private FraudRiskLevel DetermineRiskLevel(double riskScore)
    {
        return riskScore switch
        {
            >= 80 => FraudRiskLevel.Critical,
            >= 60 => FraudRiskLevel.High,
            >= 30 => FraudRiskLevel.Medium,
            _ => FraudRiskLevel.Low
        };
    }

    private List<string> GenerateRecommendedActions(FraudDetectionResult result)
    {
        var actions = new List<string>();

        if (result.RiskLevel >= FraudRiskLevel.High)
        {
            actions.Add("Require manual review");
            actions.Add("Additional identity verification");
        }

        if (result.RiskLevel >= FraudRiskLevel.Critical)
        {
            actions.Add("Block transaction immediately");
            actions.Add("Contact security team");
        }

        if (result.Indicators.Any(i => i.IndicatorType == "velocity"))
        {
            actions.Add("Implement rate limiting");
        }

        if (result.Indicators.Any(i => i.IndicatorType == "amount"))
        {
            actions.Add("Verify payment method");
        }

        return actions;
    }

    private async Task CreateFraudAlertAsync(Payment payment, FraudDetectionResult result, CancellationToken cancellationToken)
    {
        try
        {
            var alert = new FraudAlert
            {
                AlertId = Guid.NewGuid().ToString(),
                PaymentId = payment.PaymentId,
                TeamId = payment.TeamId,
                RiskLevel = result.RiskLevel,
                AlertType = "FraudDetection",
                Description = $"High fraud risk detected: Score {result.RiskScore.OverallScore:F1}",
                CreatedAt = DateTime.UtcNow,
                IsActive = true,
                AlertData = new Dictionary<string, object>
                {
                    ["risk_score"] = result.RiskScore.OverallScore,
                    ["indicators"] = result.Indicators.Select(i => i.IndicatorType).ToList(),
                    ["payment_amount"] = payment.Amount
                }
            };

            _activeAlerts.TryAdd(alert.AlertId, alert);
            ActiveFraudAlerts.WithLabels(result.RiskLevel.ToString()).Inc();

            _logger.LogWarning("Fraud alert created: {AlertId} for payment {PaymentId}, Risk: {RiskLevel}", 
                alert.AlertId, payment.PaymentId, result.RiskLevel);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create fraud alert for payment: {PaymentId}", payment.PaymentId);
        }
    }
}

// Built-in fraud detection hooks
public class HighValueTransactionHook : IFraudDetectionHook
{
    public string HookId => "high_value_transaction";
    public string Name => "High Value Transaction Detection";
    public int Priority => 1;

    public async Task<FraudHookResult> ExecuteAsync(Payment payment, FraudDetectionContext context, CancellationToken cancellationToken = default)
    {
        var result = new FraudHookResult();
        
        if (payment.Amount >= context.Rules.HighValueThreshold)
        {
            result.RiskScore = Math.Min((double)payment.Amount / (double)context.Rules.HighValueThreshold * 30, 50);
            result.Indicators.Add(new FraudIndicator
            {
                IndicatorType = "high_value",
                Description = $"High value transaction: {payment.Amount}",
                Weight = 0.3,
                Score = result.RiskScore
            });
        }

        return result;
    }
}

public class VelocityCheckHook : IFraudDetectionHook
{
    public string HookId => "velocity_check";
    public string Name => "Transaction Velocity Check";
    public int Priority => 2;

    public async Task<FraudHookResult> ExecuteAsync(Payment payment, FraudDetectionContext context, CancellationToken cancellationToken = default)
    {
        var result = new FraudHookResult();
        
        if (!context.Rules.EnableVelocityChecks) return result;

        var recentCount = context.RecentPayments.Count(p => 
            context.AnalysisTime - p.CreatedAt < TimeSpan.FromMinutes(context.Rules.VelocityCheckMinutes));

        if (recentCount >= context.Rules.MaxVelocityTransactions)
        {
            result.ShouldBlock = true;
            result.BlockReason = $"Velocity limit exceeded: {recentCount} transactions in {context.Rules.VelocityCheckMinutes} minutes";
            result.RiskScore = 60;
            result.Indicators.Add(new FraudIndicator
            {
                IndicatorType = "velocity",
                Description = result.BlockReason,
                Weight = 0.4,
                Score = result.RiskScore
            });
        }

        return result;
    }
}

public class PatternAnalysisHook : IFraudDetectionHook
{
    public string HookId => "pattern_analysis";
    public string Name => "Transaction Pattern Analysis";
    public int Priority => 3;

    public async Task<FraudHookResult> ExecuteAsync(Payment payment, FraudDetectionContext context, CancellationToken cancellationToken = default)
    {
        var result = new FraudHookResult();
        
        if (!context.Rules.EnablePatternAnalysis) return result;

        // Simple pattern detection - repeated amounts
        var sameAmountCount = context.RecentPayments.Count(p => p.Amount == payment.Amount);
        if (sameAmountCount >= 3)
        {
            result.RiskScore = 35;
            result.Indicators.Add(new FraudIndicator
            {
                IndicatorType = "pattern",
                Description = $"Repeated amount pattern: {sameAmountCount} transactions with same amount",
                Weight = 0.2,
                Score = result.RiskScore
            });
        }

        return result;
    }
}