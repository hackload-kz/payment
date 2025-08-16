using FluentValidation;
using FluentValidation.Results;
using Microsoft.Extensions.Logging;
using PaymentGateway.Core.DTOs.PaymentInit;
using PaymentGateway.Core.DTOs.PaymentConfirm;
using PaymentGateway.Core.DTOs.PaymentCancel;
using PaymentGateway.Core.DTOs.PaymentCheck;
using PaymentGateway.Core.Interfaces;
using PaymentGateway.Core.Repositories;
using PaymentGateway.Core.Enums;

namespace PaymentGateway.Core.Validation.Async;

/// <summary>
/// Service for performing async validation that requires database access
/// </summary>
public interface IAsyncValidationService
{
    Task<ValidationResult> ValidatePaymentInitAsync(PaymentInitRequestDto request, CancellationToken cancellationToken = default);
    Task<ValidationResult> ValidatePaymentConfirmAsync(PaymentConfirmRequestDto request, CancellationToken cancellationToken = default);
    Task<ValidationResult> ValidatePaymentCancelAsync(PaymentCancelRequestDto request, CancellationToken cancellationToken = default);
    Task<ValidationResult> ValidatePaymentCheckAsync(PaymentCheckRequestDto request, CancellationToken cancellationToken = default);
    Task<bool> IsTeamValidAsync(string teamSlug, CancellationToken cancellationToken = default);
    Task<bool> IsOrderIdUniqueAsync(string teamSlug, string orderId, CancellationToken cancellationToken = default);
    Task<bool> IsPaymentIdValidAsync(string paymentId, CancellationToken cancellationToken = default);
    Task<bool> IsCustomerValidForTeamAsync(string teamSlug, string customerKey, CancellationToken cancellationToken = default);
}

/// <summary>
/// Implementation of async validation service
/// </summary>
public class AsyncValidationService : IAsyncValidationService
{
    private readonly ITeamRepository _teamRepository;
    private readonly IPaymentRepository _paymentRepository;
    private readonly ICustomerRepository _customerRepository;
    private readonly ILogger<AsyncValidationService> _logger;

    public AsyncValidationService(
        ITeamRepository teamRepository,
        IPaymentRepository paymentRepository,
        ICustomerRepository customerRepository,
        ILogger<AsyncValidationService> logger)
    {
        _teamRepository = teamRepository;
        _paymentRepository = paymentRepository;
        _customerRepository = customerRepository;
        _logger = logger;
    }

    public async Task<ValidationResult> ValidatePaymentInitAsync(PaymentInitRequestDto request, CancellationToken cancellationToken = default)
    {
        var result = new ValidationResult();

        try
        {
            // Validate team exists and is active
            if (!await IsTeamValidAsync(request.TeamSlug, cancellationToken))
            {
                result.Errors.Add(new ValidationFailure("TeamSlug", "Team does not exist or is not active")
                {
                    ErrorCode = "TEAM_NOT_FOUND"
                });
            }

            // Validate OrderId uniqueness
            if (!await IsOrderIdUniqueAsync(request.TeamSlug, request.OrderId, cancellationToken))
            {
                result.Errors.Add(new ValidationFailure("OrderId", "OrderId already exists for this team")
                {
                    ErrorCode = "ORDER_ID_DUPLICATE"
                });
            }

            // Validate customer if provided
            if (!string.IsNullOrEmpty(request.CustomerKey))
            {
                if (!await IsCustomerValidForTeamAsync(request.TeamSlug, request.CustomerKey, cancellationToken))
                {
                    result.Errors.Add(new ValidationFailure("CustomerKey", "Customer is not valid for this team")
                    {
                        ErrorCode = "CUSTOMER_NOT_VALID"
                    });
                }
            }

            // Validate team-specific limits
            var teamLimits = await GetTeamLimitsAsync(request.TeamSlug, cancellationToken);
            if (teamLimits != null)
            {
                if (request.Amount > teamLimits.MaxTransactionAmount)
                {
                    result.Errors.Add(new ValidationFailure("Amount", "Amount exceeds team transaction limit")
                    {
                        ErrorCode = "TRANSACTION_LIMIT_EXCEEDED"
                    });
                }

                if (!teamLimits.SupportedCurrencies.Contains(request.Currency))
                {
                    result.Errors.Add(new ValidationFailure("Currency", "Currency is not supported for this team")
                    {
                        ErrorCode = "CURRENCY_NOT_SUPPORTED"
                    });
                }
            }

            // Validate daily limits
            var dailyUsage = await GetDailyUsageAsync(request.TeamSlug, cancellationToken);
            if (dailyUsage != null && teamLimits != null)
            {
                if (dailyUsage.TotalAmount + request.Amount > teamLimits.DailyLimit)
                {
                    result.Errors.Add(new ValidationFailure("Amount", "Payment would exceed daily limit")
                    {
                        ErrorCode = "DAILY_LIMIT_EXCEEDED"
                    });
                }

                if (dailyUsage.TransactionCount >= teamLimits.DailyTransactionLimit)
                {
                    result.Errors.Add(new ValidationFailure("", "Daily transaction count limit exceeded")
                    {
                        ErrorCode = "DAILY_TRANSACTION_COUNT_EXCEEDED"
                    });
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during async validation for payment init");
            result.Errors.Add(new ValidationFailure("", "Validation service temporarily unavailable")
            {
                ErrorCode = "VALIDATION_SERVICE_ERROR"
            });
        }

        return result;
    }

    public async Task<ValidationResult> ValidatePaymentConfirmAsync(PaymentConfirmRequestDto request, CancellationToken cancellationToken = default)
    {
        var result = new ValidationResult();

        try
        {
            // Validate team exists and is active
            if (!await IsTeamValidAsync(request.TeamSlug, cancellationToken))
            {
                result.Errors.Add(new ValidationFailure("TeamSlug", "Team does not exist or is not active")
                {
                    ErrorCode = "TEAM_NOT_FOUND"
                });
            }

            // Validate payment exists and is in correct state
            var payment = await _paymentRepository.GetByPaymentIdAsync(request.PaymentId, cancellationToken);
            if (payment == null)
            {
                result.Errors.Add(new ValidationFailure("PaymentId", "Payment not found")
                {
                    ErrorCode = "PAYMENT_NOT_FOUND"
                });
            }
            else
            {
                // Validate payment state
                if (!IsPaymentConfirmable(payment.Status))
                {
                    result.Errors.Add(new ValidationFailure("PaymentId", "Payment is not in a confirmable state")
                    {
                        ErrorCode = "PAYMENT_NOT_CONFIRMABLE"
                    });
                }

                // Validate payment belongs to team
                if (payment.Team?.TeamSlug != request.TeamSlug)
                {
                    result.Errors.Add(new ValidationFailure("PaymentId", "Payment does not belong to this team")
                    {
                        ErrorCode = "PAYMENT_TEAM_MISMATCH"
                    });
                }

                // Validate confirmation amount if provided
                if (request.Amount.HasValue)
                {
                    if (request.Amount.Value > payment.Amount)
                    {
                        result.Errors.Add(new ValidationFailure("Amount", "Confirmation amount cannot exceed payment amount")
                        {
                            ErrorCode = "CONFIRMATION_AMOUNT_EXCEEDS_PAYMENT"
                        });
                    }

                    if (request.Amount.Value <= 0)
                    {
                        result.Errors.Add(new ValidationFailure("Amount", "Confirmation amount must be greater than zero")
                        {
                            ErrorCode = "CONFIRMATION_AMOUNT_INVALID"
                        });
                    }
                }

                // Validate payment expiry
                if (payment.ExpiresAt.HasValue && payment.ExpiresAt.Value < DateTime.UtcNow)
                {
                    result.Errors.Add(new ValidationFailure("PaymentId", "Payment has expired")
                    {
                        ErrorCode = "PAYMENT_EXPIRED"
                    });
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during async validation for payment confirm");
            result.Errors.Add(new ValidationFailure("", "Validation service temporarily unavailable")
            {
                ErrorCode = "VALIDATION_SERVICE_ERROR"
            });
        }

        return result;
    }

    public async Task<ValidationResult> ValidatePaymentCancelAsync(PaymentCancelRequestDto request, CancellationToken cancellationToken = default)
    {
        var result = new ValidationResult();

        try
        {
            // Validate team exists and is active
            if (!await IsTeamValidAsync(request.TeamSlug, cancellationToken))
            {
                result.Errors.Add(new ValidationFailure("TeamSlug", "Team does not exist or is not active")
                {
                    ErrorCode = "TEAM_NOT_FOUND"
                });
            }

            // Validate payment exists and is in correct state
            var payment = await _paymentRepository.GetByPaymentIdAsync(request.PaymentId, cancellationToken);
            if (payment == null)
            {
                result.Errors.Add(new ValidationFailure("PaymentId", "Payment not found")
                {
                    ErrorCode = "PAYMENT_NOT_FOUND"
                });
            }
            else
            {
                // Validate payment state
                if (!IsPaymentCancellable(payment.Status))
                {
                    result.Errors.Add(new ValidationFailure("PaymentId", "Payment is not in a cancellable state")
                    {
                        ErrorCode = "PAYMENT_NOT_CANCELLABLE"
                    });
                }

                // Validate payment belongs to team
                if (payment.Team?.TeamSlug != request.TeamSlug)
                {
                    result.Errors.Add(new ValidationFailure("PaymentId", "Payment does not belong to this team")
                    {
                        ErrorCode = "PAYMENT_TEAM_MISMATCH"
                    });
                }

                // Validate refund amount if provided
                if (request.Amount.HasValue)
                {
                    if (request.Amount.Value > payment.Amount)
                    {
                        result.Errors.Add(new ValidationFailure("Amount", "Refund amount cannot exceed payment amount")
                        {
                            ErrorCode = "REFUND_AMOUNT_EXCEEDS_PAYMENT"
                        });
                    }

                    if (request.Amount.Value <= 0)
                    {
                        result.Errors.Add(new ValidationFailure("Amount", "Refund amount must be greater than zero")
                        {
                            ErrorCode = "REFUND_AMOUNT_INVALID"
                        });
                    }

                    // Check for partial refunds (if not allowed)
                    if (request.Amount.Value < payment.Amount)
                    {
                        var teamConfig = await GetTeamConfigurationAsync(request.TeamSlug, cancellationToken);
                        if (teamConfig != null && !teamConfig.AllowPartialRefunds)
                        {
                            result.Errors.Add(new ValidationFailure("Amount", "Partial refunds are not allowed for this team")
                            {
                                ErrorCode = "PARTIAL_REFUNDS_NOT_ALLOWED"
                            });
                        }
                    }
                }

                // Validate refund time limits
                var refundTimeLimit = await GetRefundTimeLimitAsync(request.TeamSlug, cancellationToken);
                if (refundTimeLimit.HasValue && payment.CompletedAt.HasValue)
                {
                    var timeSinceCompletion = DateTime.UtcNow - payment.CompletedAt.Value;
                    if (timeSinceCompletion > refundTimeLimit.Value)
                    {
                        result.Errors.Add(new ValidationFailure("PaymentId", "Refund time limit has been exceeded")
                        {
                            ErrorCode = "REFUND_TIME_LIMIT_EXCEEDED"
                        });
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during async validation for payment cancel");
            result.Errors.Add(new ValidationFailure("", "Validation service temporarily unavailable")
            {
                ErrorCode = "VALIDATION_SERVICE_ERROR"
            });
        }

        return result;
    }

    public async Task<ValidationResult> ValidatePaymentCheckAsync(PaymentCheckRequestDto request, CancellationToken cancellationToken = default)
    {
        var result = new ValidationResult();

        try
        {
            // Validate team exists and is active
            if (!await IsTeamValidAsync(request.TeamSlug, cancellationToken))
            {
                result.Errors.Add(new ValidationFailure("TeamSlug", "Team does not exist or is not active")
                {
                    ErrorCode = "TEAM_NOT_FOUND"
                });
            }

            // Validate payment exists if PaymentId provided
            if (!string.IsNullOrEmpty(request.PaymentId))
            {
                var payment = await _paymentRepository.GetByPaymentIdAsync(request.PaymentId, cancellationToken);
                if (payment == null)
                {
                    result.Errors.Add(new ValidationFailure("PaymentId", "Payment not found")
                    {
                        ErrorCode = "PAYMENT_NOT_FOUND"
                    });
                }
                else if (payment.Team?.TeamSlug != request.TeamSlug)
                {
                    result.Errors.Add(new ValidationFailure("PaymentId", "Payment does not belong to this team")
                    {
                        ErrorCode = "PAYMENT_TEAM_MISMATCH"
                    });
                }
            }

            // Validate payment exists if OrderId provided
            if (!string.IsNullOrEmpty(request.OrderId))
            {
                var payment = await _paymentRepository.GetByOrderIdAsync(request.TeamSlug, request.OrderId, cancellationToken);
                if (payment == null)
                {
                    result.Errors.Add(new ValidationFailure("OrderId", "Payment with this OrderId not found")
                    {
                        ErrorCode = "PAYMENT_NOT_FOUND_BY_ORDER_ID"
                    });
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during async validation for payment check");
            result.Errors.Add(new ValidationFailure("", "Validation service temporarily unavailable")
            {
                ErrorCode = "VALIDATION_SERVICE_ERROR"
            });
        }

        return result;
    }

    public async Task<bool> IsTeamValidAsync(string teamSlug, CancellationToken cancellationToken = default)
    {
        try
        {
            var team = await _teamRepository.GetByTeamSlugAsync(teamSlug, cancellationToken);
            return team != null && team.IsActive;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating team {TeamSlug}", teamSlug);
            return false;
        }
    }

    public async Task<bool> IsOrderIdUniqueAsync(string teamSlug, string orderId, CancellationToken cancellationToken = default)
    {
        try
        {
            var existingPayment = await _paymentRepository.GetByOrderIdAsync(teamSlug, orderId, cancellationToken);
            return existingPayment == null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking OrderId uniqueness for team {TeamSlug}, orderId {OrderId}", teamSlug, orderId);
            return false;
        }
    }

    public async Task<bool> IsPaymentIdValidAsync(string paymentId, CancellationToken cancellationToken = default)
    {
        try
        {
            var payment = await _paymentRepository.GetByPaymentIdAsync(paymentId, cancellationToken);
            return payment != null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating PaymentId {PaymentId}", paymentId);
            return false;
        }
    }

    public async Task<bool> IsCustomerValidForTeamAsync(string teamSlug, string customerKey, CancellationToken cancellationToken = default)
    {
        try
        {
            var customer = await _customerRepository.GetByCustomerKeyAsync(teamSlug, customerKey, cancellationToken);
            return customer != null && customer.IsActive;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating customer {CustomerKey} for team {TeamSlug}", customerKey, teamSlug);
            return false;
        }
    }

    private bool IsPaymentConfirmable(Enums.PaymentStatus status)
    {
        return status == Enums.PaymentStatus.AUTHORIZED || status == Enums.PaymentStatus.NEW;
    }

    private bool IsPaymentCancellable(Enums.PaymentStatus status)
    {
        return status == Enums.PaymentStatus.NEW || 
               status == Enums.PaymentStatus.AUTHORIZED || 
               status == Enums.PaymentStatus.CONFIRMED ||
               status == Enums.PaymentStatus.CAPTURED;
    }

    private async Task<TeamLimits?> GetTeamLimitsAsync(string teamSlug, CancellationToken cancellationToken)
    {
        // This would typically fetch team-specific limits from the database
        // For now, returning default limits
        return new TeamLimits
        {
            MaxTransactionAmount = 1000000, // 10,000 RUB
            DailyLimit = 10000000, // 100,000 RUB
            DailyTransactionLimit = 100,
            SupportedCurrencies = new[] { "KZT", "USD", "EUR", "BYN", "RUB" }
        };
    }

    private async Task<DailyUsage?> GetDailyUsageAsync(string teamSlug, CancellationToken cancellationToken)
    {
        // This would typically fetch current daily usage from the database
        return new DailyUsage
        {
            TotalAmount = 500000, // 5,000 RUB
            TransactionCount = 25
        };
    }

    private async Task<TeamConfiguration?> GetTeamConfigurationAsync(string teamSlug, CancellationToken cancellationToken)
    {
        // This would typically fetch team configuration from the database
        return new TeamConfiguration
        {
            AllowPartialRefunds = true
        };
    }

    private async Task<TimeSpan?> GetRefundTimeLimitAsync(string teamSlug, CancellationToken cancellationToken)
    {
        // This would typically fetch team-specific refund time limit
        return TimeSpan.FromDays(30); // 30 days default
    }
}

// Helper classes for validation
public class TeamLimits
{
    public decimal MaxTransactionAmount { get; set; }
    public decimal DailyLimit { get; set; }
    public int DailyTransactionLimit { get; set; }
    public string[] SupportedCurrencies { get; set; } = Array.Empty<string>();
}

public class DailyUsage
{
    public decimal TotalAmount { get; set; }
    public int TransactionCount { get; set; }
}

public class TeamConfiguration
{
    public bool AllowPartialRefunds { get; set; }
}