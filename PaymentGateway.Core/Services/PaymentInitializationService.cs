using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using PaymentGateway.Core.DTOs.PaymentInit;
using PaymentGateway.Core.Entities;
using PaymentGateway.Core.Enums;
using PaymentGateway.Core.Repositories;
using PaymentGateway.Core.Interfaces;
using PaymentGateway.Core.Services;
using PaymentGateway.Core.Configuration;
using System.Text.Json;

namespace PaymentGateway.Core.Services;

/// <summary>
/// Service interface for payment initialization
/// </summary>
public interface IPaymentInitializationService
{
    Task<PaymentInitResponseDto> InitializePaymentAsync(PaymentInitRequestDto request, CancellationToken cancellationToken = default);
    Task<bool> ValidateOrderIdUniquenessAsync(string teamSlug, string orderId, CancellationToken cancellationToken = default);
    Task<string> GeneratePaymentUrlAsync(string paymentId, CancellationToken cancellationToken = default);
    Task<PaymentSession?> GetPaymentSessionAsync(string paymentId, CancellationToken cancellationToken = default);
    Task<PaymentInitializationMetrics> GetInitializationMetricsAsync(string teamSlug, CancellationToken cancellationToken = default);
}

/// <summary>
/// Payment initialization service implementation
/// </summary>
public class PaymentInitializationService : IPaymentInitializationService
{
    private readonly IPaymentRepository _paymentRepository;
    private readonly ITeamRepository _teamRepository;
    private readonly ICustomerRepository _customerRepository;
    private readonly ILogger<PaymentInitializationService> _logger;
    private readonly IMetricsService _metricsService;
    private readonly IComprehensiveAuditService _auditService;
    private readonly IPaymentStateManager _paymentStateManager;
    private readonly IConfiguration _configuration;

    public PaymentInitializationService(
        IPaymentRepository paymentRepository,
        ITeamRepository teamRepository,
        ICustomerRepository customerRepository,
        ILogger<PaymentInitializationService> logger,
        IMetricsService metricsService,
        IComprehensiveAuditService auditService,
        IPaymentStateManager paymentStateManager,
        IConfiguration configuration)
    {
        _paymentRepository = paymentRepository;
        _teamRepository = teamRepository;
        _customerRepository = customerRepository;
        _logger = logger;
        _metricsService = metricsService;
        _auditService = auditService;
        _paymentStateManager = paymentStateManager;
        _configuration = configuration;
    }

    public async Task<PaymentInitResponseDto> InitializePaymentAsync(PaymentInitRequestDto request, CancellationToken cancellationToken = default)
    {
        var initializationStartTime = DateTime.UtcNow;
        
        try
        {
            _logger.LogInformation("Starting payment initialization for OrderId {OrderId}, TeamSlug {TeamSlug}", 
                request.OrderId, request.TeamSlug);

            // 1. Validate team existence and active status
            var team = await _teamRepository.GetByTeamSlugAsync(request.TeamSlug, cancellationToken);
            if (team == null || !team.IsActive)
            {
                var errorResponse = CreateErrorResponse(request, "1001", "Invalid team key", 
                    $"Team '{request.TeamSlug}' is not found or inactive");
                
                await RecordInitializationMetricsAsync(request.TeamSlug, false, DateTime.UtcNow - initializationStartTime);
                return errorResponse;
            }

            // 2. Validate OrderId uniqueness for the team
            if (!await ValidateOrderIdUniquenessAsync(request.TeamSlug, request.OrderId, cancellationToken))
            {
                var errorResponse = CreateErrorResponse(request, "1002", "Duplicate order", 
                    $"OrderId '{request.OrderId}' already exists for team '{request.TeamSlug}'");
                
                await RecordInitializationMetricsAsync(request.TeamSlug, false, DateTime.UtcNow - initializationStartTime);
                return errorResponse;
            }

            // 3. Validate customer if provided
            Customer? customer = null;
            if (!string.IsNullOrEmpty(request.CustomerKey))
            {
                customer = await ValidateCustomerAsync(team, request.CustomerKey, cancellationToken);
                if (customer == null)
                {
                    var errorResponse = CreateErrorResponse(request, "1003", "Invalid customer", 
                        $"Customer '{request.CustomerKey}' is not valid for team '{request.TeamSlug}'");
                    
                    await RecordInitializationMetricsAsync(request.TeamSlug, false, DateTime.UtcNow - initializationStartTime);
                    return errorResponse;
                }
            }

            // 4. Validate business rules (amount limits, currency support, etc.)
            var businessValidationResult = await ValidateBusinessRulesAsync(team, request, cancellationToken);
            if (!businessValidationResult.IsValid)
            {
                var errorResponse = CreateErrorResponse(request, businessValidationResult.ErrorCode, 
                    businessValidationResult.ErrorMessage, businessValidationResult.ErrorDetails);
                
                await RecordInitializationMetricsAsync(request.TeamSlug, false, DateTime.UtcNow - initializationStartTime);
                return errorResponse;
            }

            // 5. Create payment entity with INIT state
            var payment = await CreatePaymentEntityAsync(team, customer, request, cancellationToken);

            // 6. Transition payment to NEW state
            payment.Status = Enums.PaymentStatus.NEW;
            payment.UpdatedAt = DateTime.UtcNow;

            // 7. Save payment to database
            await _paymentRepository.AddAsync(payment, cancellationToken);

            // 8. Synchronize state with PaymentStateManager
            await _paymentStateManager.SynchronizePaymentStateAsync(payment.PaymentId, payment.Status);

            // 9. Generate PaymentURL for hosted payment pages
            var paymentUrl = await GeneratePaymentUrlAsync(payment.PaymentId, cancellationToken);

            // 10. Create payment session
            var paymentSession = await CreatePaymentSessionAsync(payment, request, cancellationToken);

            // 11. Create successful response
            var response = CreateSuccessResponse(payment, paymentUrl);

            // 12. Audit logging
            await _auditService.LogSystemEventAsync(
                AuditAction.PaymentInitialized,
                "Payment",
                $"Payment {payment.PaymentId} initialized successfully - OrderId: {request.OrderId}, TeamSlug: {request.TeamSlug}, Amount: {request.Amount}");

            // 13. Record metrics
            await RecordInitializationMetricsAsync(request.TeamSlug, true, DateTime.UtcNow - initializationStartTime);

            _logger.LogInformation("Payment initialization completed successfully. PaymentId: {PaymentId}, OrderId: {OrderId}", 
                payment.PaymentId, request.OrderId);

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during payment initialization for OrderId {OrderId}, TeamSlug {TeamSlug}", 
                request.OrderId, request.TeamSlug);

            await RecordInitializationMetricsAsync(request.TeamSlug, false, DateTime.UtcNow - initializationStartTime);

            return CreateErrorResponse(request, "9999", "Internal error", 
                "An internal error occurred during payment initialization");
        }
    }

    public async Task<bool> ValidateOrderIdUniquenessAsync(string teamSlug, string orderId, CancellationToken cancellationToken = default)
    {
        try
        {
            var existingPayment = await _paymentRepository.GetByOrderIdAsync(teamSlug, orderId, cancellationToken);
            return existingPayment == null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating OrderId uniqueness for TeamSlug {TeamSlug}, OrderId {OrderId}", 
                teamSlug, orderId);
            return false;
        }
    }

    public async Task<string> GeneratePaymentUrlAsync(string paymentId, CancellationToken cancellationToken = default)
    {
        try
        {
            // Generate hosted payment page URL using configuration
            var baseUrl = _configuration["Api:BaseUrl"] ?? "http://localhost:7010";
            var paymentUrl = $"{baseUrl}/api/v1/paymentform/render/{paymentId}";
            
            _logger.LogDebug("Generated PaymentURL: {PaymentUrl} for PaymentId: {PaymentId}", paymentUrl, paymentId);
            
            await Task.CompletedTask; // Placeholder for async operations
            return paymentUrl;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating PaymentURL for PaymentId {PaymentId}", paymentId);
            return string.Empty;
        }
    }

    public async Task<PaymentSession?> GetPaymentSessionAsync(string paymentId, CancellationToken cancellationToken = default)
    {
        try
        {
            // Retrieve payment session data
            // In a real implementation, this would retrieve from a session store
            _logger.LogDebug("Retrieved payment session for PaymentId: {PaymentId}", paymentId);
            
            await Task.CompletedTask; // Placeholder for async operations
            return new PaymentSession
            {
                PaymentId = paymentId,
                CreatedAt = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow.AddHours(24),
                IsActive = true
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving payment session for PaymentId {PaymentId}", paymentId);
            return null;
        }
    }

    public async Task<PaymentInitializationMetrics> GetInitializationMetricsAsync(string teamSlug, CancellationToken cancellationToken = default)
    {
        try
        {
            // Get initialization metrics for the team
            // In a real implementation, this would query metrics from a metrics store
            _logger.LogDebug("Retrieved initialization metrics for TeamSlug: {TeamSlug}", teamSlug);
            
            await Task.CompletedTask; // Placeholder for async operations
            return new PaymentInitializationMetrics
            {
                TeamSlug = teamSlug,
                TotalInitializations = 100,
                SuccessfulInitializations = 95,
                FailedInitializations = 5,
                SuccessRate = 0.95,
                AverageInitializationTime = TimeSpan.FromMilliseconds(250),
                LastUpdated = DateTime.UtcNow
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving initialization metrics for TeamSlug {TeamSlug}", teamSlug);
            return new PaymentInitializationMetrics
            {
                TeamSlug = teamSlug,
                LastUpdated = DateTime.UtcNow
            };
        }
    }

    private async Task<Customer?> ValidateCustomerAsync(Team team, string customerKey, CancellationToken cancellationToken)
    {
        try
        {
            // In a real implementation, this would validate customer against the database
            var customer = await _customerRepository.GetByIdAsync(Guid.Parse(customerKey), cancellationToken);
            return customer?.TeamId == team.Id ? customer : null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating customer {CustomerKey} for team {TeamId}", customerKey, team.Id);
            return null;
        }
    }

    private async Task<BusinessValidationResult> ValidateBusinessRulesAsync(Team team, PaymentInitRequestDto request, CancellationToken cancellationToken)
    {
        try
        {
            // Get payment configuration
            var paymentConfig = _configuration.GetSection(PaymentLimitsConfiguration.SectionName).Get<PaymentLimitsConfiguration>() 
                ?? new PaymentLimitsConfiguration();

            // Validate amount limits using configuration
            var minAmount = paymentConfig.GlobalMinPaymentAmount;
            if (request.Amount < minAmount)
            {
                return new BusinessValidationResult
                {
                    IsValid = false,
                    ErrorCode = "1004",
                    ErrorMessage = "Amount too small",
                    ErrorDetails = $"Payment amount must be at least {minAmount} kopecks ({minAmount / 100:N0} RUB)"
                };
            }

            // Use team-specific max payment amount if configured, otherwise use global configuration
            var maxAmount = team?.MaxPaymentAmount ?? paymentConfig.GlobalMaxPaymentAmount;
            if (request.Amount > maxAmount)
            {
                return new BusinessValidationResult
                {
                    IsValid = false,
                    ErrorCode = "1005",
                    ErrorMessage = "Amount too large",
                    ErrorDetails = $"Payment amount cannot exceed {maxAmount} kopecks ({maxAmount / 100:N0} RUB)"
                };
            }

            // Validate currency support
            var supportedCurrencies = new[] { "KZT", "USD", "EUR", "BYN", "RUB" };
            if (!supportedCurrencies.Contains(request.Currency?.ToUpperInvariant()))
            {
                return new BusinessValidationResult
                {
                    IsValid = false,
                    ErrorCode = "1006",
                    ErrorMessage = "Currency not supported",
                    ErrorDetails = $"Currency '{request.Currency}' is not supported"
                };
            }

            // Validate daily limits (simplified check)
            var currentDailyAmount = await GetCurrentDailyAmountAsync(team, cancellationToken);
            var dailyLimit = team?.DailyPaymentLimit ?? paymentConfig.GlobalDailyPaymentLimit;
            
            if (currentDailyAmount + request.Amount > dailyLimit)
            {
                return new BusinessValidationResult
                {
                    IsValid = false,
                    ErrorCode = "1007",
                    ErrorMessage = "Daily limit exceeded",
                    ErrorDetails = "Payment would exceed daily limit for this team"
                };
            }

            return new BusinessValidationResult { IsValid = true };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating business rules for team {TeamId}", team.Id);
            return new BusinessValidationResult
            {
                IsValid = false,
                ErrorCode = "9998",
                ErrorMessage = "Validation error",
                ErrorDetails = "Error occurred during business rule validation"
            };
        }
    }

    private async Task<Payment> CreatePaymentEntityAsync(Team team, Customer? customer, PaymentInitRequestDto request, CancellationToken cancellationToken)
    {
        var paymentId = GeneratePaymentId();
        var expiryTime = CalculatePaymentExpiry(request.PaymentExpiry);

        var payment = new Payment
        {
            PaymentId = paymentId,
            OrderId = request.OrderId,
            Amount = request.Amount,
            Currency = request.Currency,
            Status = Enums.PaymentStatus.INIT,
            Description = request.Description,
            TeamId = team.Id, // Only set the foreign key, not the navigation property
            CustomerId = customer?.Id, // Only set the foreign key, not the navigation property
            CustomerEmail = request.Email ?? customer?.Email,
            ExpiresAt = expiryTime,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            // Explicitly set default values to avoid NOT NULL constraint violations
            RefundedAmount = 0.00m,
            RefundCount = 0,
            AuthorizationAttempts = 0,
            // Initialize other required fields that might cause NOT NULL issues
            TeamSlug = team.TeamSlug ?? "",
            PaymentMethod = 0,
            MaxAllowedAttempts = 3
        };

        // Process DICT data (additional parameters)
        if (request.Data != null && request.Data.Any())
        {
            payment.Metadata = request.Data;
        }

        // Process Items if provided
        if (request.Items != null && request.Items.Any())
        {
            // Store items in metadata for now (Payment entity doesn't have ItemsData)
            payment.Metadata["Items"] = JsonSerializer.Serialize(request.Items);
            
            // Validate that items total matches payment amount
            var itemsTotal = request.Items.Sum(item => item.Amount);
            if (Math.Abs(itemsTotal - request.Amount) > 0.01m)
            {
                _logger.LogWarning("Items total ({ItemsTotal}) does not match payment amount ({PaymentAmount}) for PaymentId {PaymentId}", 
                    itemsTotal, request.Amount, paymentId);
            }
        }

        // Process Receipt if provided
        if (request.Receipt != null)
        {
            payment.Metadata["Receipt"] = JsonSerializer.Serialize(request.Receipt);
        }

        await Task.CompletedTask; // Placeholder for async operations
        return payment;
    }

    private async Task<PaymentSession> CreatePaymentSessionAsync(Payment payment, PaymentInitRequestDto request, CancellationToken cancellationToken)
    {
        var session = new PaymentSession
        {
            PaymentId = payment.PaymentId,
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = payment.ExpiresAt ?? DateTime.UtcNow.AddHours(24),
            IsActive = true,
            SessionData = JsonSerializer.Serialize(new
            {
                TeamSlug = request.TeamSlug,
                OrderId = request.OrderId,
                Amount = request.Amount,
                Currency = request.Currency,
                Language = request.Language,
                CustomerKey = request.CustomerKey
            })
        };

        // In a real implementation, this would be stored in a session store
        _logger.LogDebug("Created payment session for PaymentId: {PaymentId}", payment.PaymentId);
        
        await Task.CompletedTask; // Placeholder for async operations
        return session;
    }

    private PaymentInitResponseDto CreateSuccessResponse(Payment payment, string paymentUrl)
    {
        return new PaymentInitResponseDto
        {
            Amount = payment.Amount,
            OrderId = payment.OrderId,
            Success = true,
            Status = payment.Status.ToString(),
            PaymentId = payment.PaymentId,
            ErrorCode = "0",
            PaymentURL = paymentUrl,
            Message = null,
            Details = new PaymentDetailsDto
            {
                Description = payment.Description,
                PayType = "O",
                Language = "ru",
                Data = payment.Metadata
            }
        };
    }

    private PaymentInitResponseDto CreateErrorResponse(PaymentInitRequestDto request, string errorCode, string message, string details)
    {
        return new PaymentInitResponseDto
        {
            Amount = request.Amount,
            OrderId = request.OrderId,
            Success = false,
            Status = "ERROR",
            PaymentId = string.Empty,
            ErrorCode = errorCode,
            PaymentURL = null,
            Message = message,
            Details = new PaymentDetailsDto
            {
                Description = details
            }
        };
    }

    private string GeneratePaymentId()
    {
        // Generate unique payment ID
        // In a real implementation, this would use a more sophisticated ID generation strategy
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var random = new Random().Next(1000, 9999);
        return $"{timestamp}{random}";
    }

    private DateTime CalculatePaymentExpiry(int paymentExpiryMinutes)
    {
        // Calculate payment expiry time based on provided minutes
        var expiryMinutes = paymentExpiryMinutes > 0 ? paymentExpiryMinutes : 30; // Default 30 minutes
        return DateTime.UtcNow.AddMinutes(expiryMinutes);
    }

    private async Task<decimal> GetCurrentDailyAmountAsync(Team team, CancellationToken cancellationToken)
    {
        try
        {
            // Get current daily amount for the team
            // In a real implementation, this would query the database
            var today = DateTime.UtcNow.Date;
            // Simplified: return 0 for now
            await Task.CompletedTask;
            return 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting current daily amount for team {TeamId}", team.Id);
            return 0;
        }
    }

    private async Task RecordInitializationMetricsAsync(string teamSlug, bool success, TimeSpan duration)
    {
        try
        {
            await _metricsService.RecordCounterAsync("payment_initializations_total", 1, new Dictionary<string, string>
            {
                { "team_slug", teamSlug },
                { "success", success.ToString().ToLowerInvariant() }
            });

            await _metricsService.RecordHistogramAsync("payment_initialization_duration_seconds", duration.TotalSeconds, new Dictionary<string, string>
            {
                { "team_slug", teamSlug }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error recording initialization metrics for team {TeamSlug}", teamSlug);
        }
    }
}

// Supporting classes for payment initialization
public class BusinessValidationResult
{
    public bool IsValid { get; set; }
    public string ErrorCode { get; set; } = string.Empty;
    public string ErrorMessage { get; set; } = string.Empty;
    public string ErrorDetails { get; set; } = string.Empty;
}

public class PaymentSession
{
    public string PaymentId { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public bool IsActive { get; set; }
    public string? SessionData { get; set; }
}

public class PaymentInitializationMetrics
{
    public string TeamSlug { get; set; } = string.Empty;
    public long TotalInitializations { get; set; }
    public long SuccessfulInitializations { get; set; }
    public long FailedInitializations { get; set; }
    public double SuccessRate { get; set; }
    public TimeSpan AverageInitializationTime { get; set; }
    public DateTime LastUpdated { get; set; }
}