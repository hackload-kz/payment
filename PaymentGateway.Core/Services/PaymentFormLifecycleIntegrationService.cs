// SPDX-License-Identifier: MIT
// Copyright (c) 2025 HackLoad Payment Gateway

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using PaymentGateway.Core.Entities;
using PaymentGateway.Core.Interfaces;
using PaymentGateway.Core.Repositories;

namespace PaymentGateway.Core.Services;

/// <summary>
/// Payment Form Lifecycle Integration Service for managing payment forms within the payment lifecycle
/// 
/// This service implements:
/// - Integration of payment forms with the complete payment lifecycle
/// - Form-driven payment state transitions
/// - Lifecycle event handling for form operations
/// - Form completion workflow management
/// - Error recovery and rollback mechanisms
/// - Lifecycle metrics and monitoring
/// </summary>
public class PaymentFormLifecycleIntegrationService
{
    private readonly ILogger<PaymentFormLifecycleIntegrationService> _logger;
    private readonly IConfiguration _configuration;
    private readonly IPaymentRepository _paymentRepository;
    private readonly PaymentLifecycleManagementService _lifecycleService;
    private readonly PaymentStateTransitionValidationService _stateValidationService;
    private readonly PaymentFormStatusUpdateService _statusUpdateService;
    private readonly BusinessRuleEngineService _businessRuleEngine;
    private readonly ComprehensiveAuditService _auditService;

    // Lifecycle integration configuration
    private readonly bool _enableFormLifecycleIntegration;
    private readonly bool _enableAutomaticStateTransitions;
    private readonly bool _enableRollbackOnFailure;
    private readonly int _maxRetryAttempts;

    // Metrics
    private static readonly System.Diagnostics.Metrics.Counter<long> _lifecycleIntegrationCounter = 
        System.Diagnostics.Metrics.Meter.CreateCounter<long>("payment_form_lifecycle_operations_total");
    private static readonly System.Diagnostics.Metrics.Histogram<double> _lifecycleTransitionDuration = 
        System.Diagnostics.Metrics.Meter.CreateHistogram<double>("payment_form_lifecycle_transition_duration_seconds");
    private static readonly System.Diagnostics.Metrics.Counter<long> _lifecycleErrorCounter = 
        System.Diagnostics.Metrics.Meter.CreateCounter<long>("payment_form_lifecycle_errors_total");

    public PaymentFormLifecycleIntegrationService(
        ILogger<PaymentFormLifecycleIntegrationService> logger,
        IConfiguration configuration,
        IPaymentRepository paymentRepository,
        PaymentLifecycleManagementService lifecycleService,
        PaymentStateTransitionValidationService stateValidationService,
        PaymentFormStatusUpdateService statusUpdateService,
        BusinessRuleEngineService businessRuleEngine,
        ComprehensiveAuditService auditService)
    {
        _logger = logger;
        _configuration = configuration;
        _paymentRepository = paymentRepository;
        _lifecycleService = lifecycleService;
        _stateValidationService = stateValidationService;
        _statusUpdateService = statusUpdateService;
        _businessRuleEngine = businessRuleEngine;
        _auditService = auditService;

        // Load configuration
        _enableFormLifecycleIntegration = _configuration.GetValue<bool>("PaymentForm:EnableLifecycleIntegration", true);
        _enableAutomaticStateTransitions = _configuration.GetValue<bool>("PaymentForm:EnableAutomaticStateTransitions", true);
        _enableRollbackOnFailure = _configuration.GetValue<bool>("PaymentForm:EnableRollbackOnFailure", true);
        _maxRetryAttempts = _configuration.GetValue<int>("PaymentForm:MaxRetryAttempts", 3);
    }

    /// <summary>
    /// Process form initialization within payment lifecycle
    /// </summary>
    public async Task<PaymentFormLifecycleResult> ProcessFormInitializationAsync(FormLifecycleInitializationRequest request)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        
        try
        {
            _logger.LogInformation("Processing form initialization in lifecycle for PaymentId: {PaymentId}",
                request.PaymentId);

            if (!_enableFormLifecycleIntegration)
            {
                return new PaymentFormLifecycleResult
                {
                    Success = false,
                    ErrorMessage = "Form lifecycle integration is disabled"
                };
            }

            // Get payment data
            var payment = await _paymentRepository.GetByPaymentIdAsync(request.PaymentId);
            if (payment == null)
            {
                _lifecycleIntegrationCounter.Add(1, new KeyValuePair<string, object?>("result", "payment_not_found"));
                return new PaymentFormLifecycleResult
                {
                    Success = false,
                    ErrorMessage = "Payment not found"
                };
            }

            // Validate current payment state for form initialization
            var stateValidation = await _stateValidationService.ValidateStateTransitionAsync(
                payment.Id, payment.Status, Enums.PaymentStatus.NEW);

            if (!stateValidation.IsValid)
            {
                _lifecycleIntegrationCounter.Add(1, new KeyValuePair<string, object?>("result", "invalid_state"));
                return new PaymentFormLifecycleResult
                {
                    Success = false,
                    ErrorMessage = $"Payment state invalid for form initialization: {stateValidation.ErrorMessage}"
                };
            }

            // Create lifecycle context for form operations
            var lifecycleContext = new PaymentFormLifecycleContext
            {
                PaymentId = request.PaymentId,
                PaymentEntityId = payment.Id,
                InitialStatus = payment.Status,
                CurrentStatus = payment.Status,
                FormSessionId = request.FormSessionId,
                ClientIp = request.ClientIp,
                UserAgent = request.UserAgent,
                InitiatedAt = DateTime.UtcNow,
                LifecycleStage = FormLifecycleStage.Initialization,
                Operations = new List<FormLifecycleOperation>()
            };

            // Record initialization operation
            lifecycleContext.Operations.Add(new FormLifecycleOperation
            {
                OperationType = FormLifecycleOperationType.Initialization,
                ExecutedAt = DateTime.UtcNow,
                Status = OperationStatus.Completed,
                Description = "Form initialization started"
            });

            // Audit the initialization
            await _auditService.LogAuditEventAsync("PAYMENT_FORM_LIFECYCLE", new
            {
                PaymentId = request.PaymentId,
                Operation = "FORM_INITIALIZATION",
                Status = "STARTED",
                Context = new
                {
                    FormSessionId = request.FormSessionId,
                    ClientIp = request.ClientIp,
                    InitialPaymentStatus = payment.Status.ToString()
                }
            });

            // Broadcast status update
            await _statusUpdateService.BroadcastPaymentStatusUpdateAsync(new PaymentStatusBroadcastRequest
            {
                PaymentId = request.PaymentId,
                Status = payment.Status,
                Message = "Payment form initialized",
                UpdateType = StatusUpdateType.Initial
            });

            _lifecycleIntegrationCounter.Add(1, new KeyValuePair<string, object?>("result", "success"),
                new KeyValuePair<string, object?>("operation", "initialization"));

            _logger.LogInformation("Form initialization processed successfully for PaymentId: {PaymentId}, Duration: {Duration}ms",
                request.PaymentId, stopwatch.ElapsedMilliseconds);

            return new PaymentFormLifecycleResult
            {
                Success = true,
                LifecycleContext = lifecycleContext,
                CurrentStatus = payment.Status,
                NextAllowedOperations = GetNextAllowedOperations(payment.Status)
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing form initialization for PaymentId: {PaymentId}", request.PaymentId);
            
            _lifecycleIntegrationCounter.Add(1, new KeyValuePair<string, object?>("result", "error"));
            _lifecycleErrorCounter.Add(1, new KeyValuePair<string, object?>("operation", "initialization"));

            return new PaymentFormLifecycleResult
            {
                Success = false,
                ErrorMessage = "Form initialization failed"
            };
        }
        finally
        {
            _lifecycleTransitionDuration.Record(stopwatch.Elapsed.TotalSeconds,
                new KeyValuePair<string, object?>("operation", "initialization"));
        }
    }

    /// <summary>
    /// Process form submission through payment lifecycle
    /// </summary>
    public async Task<PaymentFormLifecycleResult> ProcessFormSubmissionAsync(FormLifecycleSubmissionRequest request)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var rollbackOperations = new List<RollbackOperation>();
        
        try
        {
            _logger.LogInformation("Processing form submission in lifecycle for PaymentId: {PaymentId}",
                request.PaymentId);

            // Get current payment
            var payment = await _paymentRepository.GetByPaymentIdAsync(request.PaymentId);
            if (payment == null)
            {
                _lifecycleIntegrationCounter.Add(1, new KeyValuePair<string, object?>("result", "payment_not_found"));
                return new PaymentFormLifecycleResult
                {
                    Success = false,
                    ErrorMessage = "Payment not found"
                };
            }

            var lifecycleContext = new PaymentFormLifecycleContext
            {
                PaymentId = request.PaymentId,
                PaymentEntityId = payment.Id,
                InitialStatus = payment.Status,
                CurrentStatus = payment.Status,
                FormSessionId = request.FormSessionId,
                ClientIp = request.ClientIp,
                UserAgent = request.UserAgent,
                InitiatedAt = DateTime.UtcNow,
                LifecycleStage = FormLifecycleStage.Processing,
                Operations = new List<FormLifecycleOperation>()
            };

            // Step 1: Validate business rules
            var businessRuleResult = await ValidateBusinessRulesForSubmission(payment, request);
            if (!businessRuleResult.Success)
            {
                lifecycleContext.Operations.Add(new FormLifecycleOperation
                {
                    OperationType = FormLifecycleOperationType.BusinessRuleValidation,
                    ExecutedAt = DateTime.UtcNow,
                    Status = OperationStatus.Failed,
                    Description = businessRuleResult.ErrorMessage!
                });

                return new PaymentFormLifecycleResult
                {
                    Success = false,
                    ErrorMessage = businessRuleResult.ErrorMessage,
                    LifecycleContext = lifecycleContext
                };
            }

            lifecycleContext.Operations.Add(new FormLifecycleOperation
            {
                OperationType = FormLifecycleOperationType.BusinessRuleValidation,
                ExecutedAt = DateTime.UtcNow,
                Status = OperationStatus.Completed,
                Description = "Business rules validated successfully"
            });

            // Step 2: Process payment authorization
            if (_enableAutomaticStateTransitions)
            {
                var authorizationResult = await ProcessPaymentAuthorizationAsync(payment, request, rollbackOperations);
                if (!authorizationResult.Success)
                {
                    if (_enableRollbackOnFailure)
                    {
                        await ExecuteRollbackOperationsAsync(rollbackOperations);
                    }

                    lifecycleContext.Operations.Add(new FormLifecycleOperation
                    {
                        OperationType = FormLifecycleOperationType.PaymentAuthorization,
                        ExecutedAt = DateTime.UtcNow,
                        Status = OperationStatus.Failed,
                        Description = authorizationResult.ErrorMessage!
                    });

                    return new PaymentFormLifecycleResult
                    {
                        Success = false,
                        ErrorMessage = authorizationResult.ErrorMessage,
                        LifecycleContext = lifecycleContext
                    };
                }

                lifecycleContext.CurrentStatus = Enums.PaymentStatus.AUTHORIZED;
                lifecycleContext.Operations.Add(new FormLifecycleOperation
                {
                    OperationType = FormLifecycleOperationType.PaymentAuthorization,
                    ExecutedAt = DateTime.UtcNow,
                    Status = OperationStatus.Completed,
                    Description = "Payment authorized successfully",
                    AdditionalData = authorizationResult.AdditionalData
                });
            }

            // Step 3: Update lifecycle stage
            lifecycleContext.LifecycleStage = FormLifecycleStage.Completed;
            lifecycleContext.CompletedAt = DateTime.UtcNow;

            // Step 4: Audit the submission
            await _auditService.LogAuditEventAsync("PAYMENT_FORM_LIFECYCLE", new
            {
                PaymentId = request.PaymentId,
                Operation = "FORM_SUBMISSION",
                Status = "COMPLETED",
                Context = new
                {
                    FormSessionId = request.FormSessionId,
                    InitialStatus = payment.Status.ToString(),
                    FinalStatus = lifecycleContext.CurrentStatus.ToString(),
                    ProcessingDuration = stopwatch.ElapsedMilliseconds
                }
            });

            // Step 5: Broadcast final status update
            await _statusUpdateService.BroadcastPaymentStatusUpdateAsync(new PaymentStatusBroadcastRequest
            {
                PaymentId = request.PaymentId,
                Status = lifecycleContext.CurrentStatus,
                Message = "Payment form submitted and processed successfully",
                UpdateType = StatusUpdateType.Success,
                AdditionalData = new Dictionary<string, object>
                {
                    ["ProcessingDuration"] = stopwatch.ElapsedMilliseconds,
                    ["OperationsCount"] = lifecycleContext.Operations.Count
                }
            });

            _lifecycleIntegrationCounter.Add(1, new KeyValuePair<string, object?>("result", "success"),
                new KeyValuePair<string, object?>("operation", "submission"));

            _logger.LogInformation("Form submission processed successfully for PaymentId: {PaymentId}, Duration: {Duration}ms",
                request.PaymentId, stopwatch.ElapsedMilliseconds);

            return new PaymentFormLifecycleResult
            {
                Success = true,
                LifecycleContext = lifecycleContext,
                CurrentStatus = lifecycleContext.CurrentStatus,
                NextAllowedOperations = GetNextAllowedOperations(lifecycleContext.CurrentStatus)
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing form submission for PaymentId: {PaymentId}", request.PaymentId);
            
            if (_enableRollbackOnFailure)
            {
                await ExecuteRollbackOperationsAsync(rollbackOperations);
            }

            _lifecycleIntegrationCounter.Add(1, new KeyValuePair<string, object?>("result", "error"));
            _lifecycleErrorCounter.Add(1, new KeyValuePair<string, object?>("operation", "submission"));

            return new PaymentFormLifecycleResult
            {
                Success = false,
                ErrorMessage = "Form submission processing failed"
            };
        }
        finally
        {
            _lifecycleTransitionDuration.Record(stopwatch.Elapsed.TotalSeconds,
                new KeyValuePair<string, object?>("operation", "submission"));
        }
    }

    /// <summary>
    /// Process form error recovery within lifecycle
    /// </summary>
    public async Task<PaymentFormLifecycleResult> ProcessFormErrorRecoveryAsync(FormLifecycleErrorRecoveryRequest request)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        
        try
        {
            _logger.LogInformation("Processing form error recovery for PaymentId: {PaymentId}, Error: {ErrorType}",
                request.PaymentId, request.ErrorType);

            var payment = await _paymentRepository.GetByPaymentIdAsync(request.PaymentId);
            if (payment == null)
            {
                return new PaymentFormLifecycleResult
                {
                    Success = false,
                    ErrorMessage = "Payment not found"
                };
            }

            var recoveryContext = new PaymentFormLifecycleContext
            {
                PaymentId = request.PaymentId,
                PaymentEntityId = payment.Id,
                InitialStatus = payment.Status,
                CurrentStatus = payment.Status,
                FormSessionId = request.FormSessionId,
                ClientIp = request.ClientIp,
                InitiatedAt = DateTime.UtcNow,
                LifecycleStage = FormLifecycleStage.ErrorRecovery,
                Operations = new List<FormLifecycleOperation>()
            };

            // Determine recovery strategy based on error type
            var recoveryStrategy = DetermineRecoveryStrategy(request.ErrorType, payment.Status);
            
            recoveryContext.Operations.Add(new FormLifecycleOperation
            {
                OperationType = FormLifecycleOperationType.ErrorRecovery,
                ExecutedAt = DateTime.UtcNow,
                Status = OperationStatus.InProgress,
                Description = $"Executing recovery strategy: {recoveryStrategy}",
                AdditionalData = new Dictionary<string, object>
                {
                    ["ErrorType"] = request.ErrorType.ToString(),
                    ["RecoveryStrategy"] = recoveryStrategy.ToString(),
                    ["ErrorMessage"] = request.ErrorMessage ?? ""
                }
            });

            // Execute recovery based on strategy
            var recoveryResult = await ExecuteRecoveryStrategy(recoveryStrategy, payment, request);
            
            if (recoveryResult.Success)
            {
                recoveryContext.Operations.Last().Status = OperationStatus.Completed;
                recoveryContext.CurrentStatus = recoveryResult.NewStatus ?? payment.Status;
            }
            else
            {
                recoveryContext.Operations.Last().Status = OperationStatus.Failed;
                recoveryContext.Operations.Last().Description += $" - Recovery failed: {recoveryResult.ErrorMessage}";
            }

            // Audit the recovery attempt
            await _auditService.LogAuditEventAsync("PAYMENT_FORM_LIFECYCLE", new
            {
                PaymentId = request.PaymentId,
                Operation = "ERROR_RECOVERY",
                Status = recoveryResult.Success ? "SUCCESS" : "FAILED",
                Context = new
                {
                    ErrorType = request.ErrorType.ToString(),
                    RecoveryStrategy = recoveryStrategy.ToString(),
                    OriginalStatus = payment.Status.ToString(),
                    RecoveryResult = recoveryResult.Success,
                    ProcessingDuration = stopwatch.ElapsedMilliseconds
                }
            });

            _lifecycleIntegrationCounter.Add(1, new KeyValuePair<string, object?>("result", recoveryResult.Success ? "success" : "failed"),
                new KeyValuePair<string, object?>("operation", "error_recovery"));

            return new PaymentFormLifecycleResult
            {
                Success = recoveryResult.Success,
                ErrorMessage = recoveryResult.ErrorMessage,
                LifecycleContext = recoveryContext,
                CurrentStatus = recoveryContext.CurrentStatus,
                NextAllowedOperations = GetNextAllowedOperations(recoveryContext.CurrentStatus)
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during form error recovery for PaymentId: {PaymentId}", request.PaymentId);
            
            _lifecycleErrorCounter.Add(1, new KeyValuePair<string, object?>("operation", "error_recovery"));

            return new PaymentFormLifecycleResult
            {
                Success = false,
                ErrorMessage = "Error recovery processing failed"
            };
        }
        finally
        {
            _lifecycleTransitionDuration.Record(stopwatch.Elapsed.TotalSeconds,
                new KeyValuePair<string, object?>("operation", "error_recovery"));
        }
    }

    // Private helper methods

    private async Task<OperationResult> ValidateBusinessRulesForSubmission(Payment payment, FormLifecycleSubmissionRequest request)
    {
        try
        {
            // Create rule context for validation
            var ruleContext = new PaymentRuleContext
            {
                PaymentId = payment.PaymentId,
                TeamId = payment.TeamId,
                Amount = payment.Amount,
                Currency = payment.Currency,
                PaymentMethod = "CARD",
                ClientIp = request.ClientIp,
                UserAgent = request.UserAgent
            };

            var ruleResult = await _businessRuleEngine.EvaluateRulesAsync(BusinessRuleType.PAYMENT_LIMIT, ruleContext);
            if (!ruleResult.IsValid)
            {
                return new OperationResult
                {
                    Success = false,
                    ErrorMessage = $"Business rule violation: {string.Join(", ", ruleResult.Violations.Select(v => v.Message))}"
                };
            }

            return new OperationResult { Success = true };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating business rules for PaymentId: {PaymentId}", payment.PaymentId);
            return new OperationResult
            {
                Success = false,
                ErrorMessage = "Business rule validation failed"
            };
        }
    }

    private async Task<OperationResult> ProcessPaymentAuthorizationAsync(Payment payment, FormLifecycleSubmissionRequest request, List<RollbackOperation> rollbackOperations)
    {
        try
        {
            // Record rollback operation
            rollbackOperations.Add(new RollbackOperation
            {
                OperationType = "REVERT_PAYMENT_STATUS",
                PaymentId = payment.PaymentId,
                OriginalStatus = payment.Status,
                ExecuteRollback = async () =>
                {
                    var rollbackPayment = await _paymentRepository.GetByPaymentIdAsync(payment.PaymentId);
                    if (rollbackPayment != null)
                    {
                        await _lifecycleService.TransitionPaymentAsync(rollbackPayment.Id, payment.Status);
                    }
                }
            });

            // Transition payment to AUTHORIZED status
            var transitionResult = await _lifecycleService.TransitionPaymentAsync(payment.Id, Enums.PaymentStatus.AUTHORIZED);
            if (!transitionResult.Success)
            {
                return new OperationResult
                {
                    Success = false,
                    ErrorMessage = transitionResult.ErrorMessage
                };
            }

            return new OperationResult
            {
                Success = true,
                AdditionalData = new Dictionary<string, object>
                {
                    ["TransitionId"] = transitionResult.TransitionId ?? "",
                    ["AuthorizedAt"] = DateTime.UtcNow
                }
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing payment authorization for PaymentId: {PaymentId}", payment.PaymentId);
            return new OperationResult
            {
                Success = false,
                ErrorMessage = "Payment authorization failed"
            };
        }
    }

    private async Task ExecuteRollbackOperationsAsync(List<RollbackOperation> rollbackOperations)
    {
        foreach (var rollbackOperation in rollbackOperations.AsEnumerable().Reverse())
        {
            try
            {
                _logger.LogInformation("Executing rollback operation: {OperationType} for PaymentId: {PaymentId}",
                    rollbackOperation.OperationType, rollbackOperation.PaymentId);
                
                await rollbackOperation.ExecuteRollback();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error executing rollback operation: {OperationType} for PaymentId: {PaymentId}",
                    rollbackOperation.OperationType, rollbackOperation.PaymentId);
            }
        }
    }

    private FormErrorRecoveryStrategy DetermineRecoveryStrategy(FormLifecycleErrorType errorType, Enums.PaymentStatus currentStatus)
    {
        return errorType switch
        {
            FormLifecycleErrorType.ValidationError => FormErrorRecoveryStrategy.RetryWithCorrection,
            FormLifecycleErrorType.NetworkError => FormErrorRecoveryStrategy.RetryOperation,
            FormLifecycleErrorType.PaymentProcessingError => FormErrorRecoveryStrategy.ResetToInitial,
            FormLifecycleErrorType.SecurityViolation => FormErrorRecoveryStrategy.TerminateSession,
            FormLifecycleErrorType.SessionExpired => FormErrorRecoveryStrategy.RequireReinitialization,
            _ => FormErrorRecoveryStrategy.RetryOperation
        };
    }

    private async Task<OperationResult> ExecuteRecoveryStrategy(FormErrorRecoveryStrategy strategy, Payment payment, FormLifecycleErrorRecoveryRequest request)
    {
        try
        {
            return strategy switch
            {
                FormErrorRecoveryStrategy.RetryWithCorrection => new OperationResult
                {
                    Success = true,
                    ErrorMessage = "Retry with corrected data allowed"
                },
                FormErrorRecoveryStrategy.RetryOperation => new OperationResult
                {
                    Success = true,
                    ErrorMessage = "Operation retry allowed"
                },
                FormErrorRecoveryStrategy.ResetToInitial => await ResetPaymentToInitialState(payment),
                FormErrorRecoveryStrategy.TerminateSession => new OperationResult
                {
                    Success = true,
                    ErrorMessage = "Session terminated for security",
                    NewStatus = Enums.PaymentStatus.CANCELLED
                },
                FormErrorRecoveryStrategy.RequireReinitialization => new OperationResult
                {
                    Success = true,
                    ErrorMessage = "Reinitialization required"
                },
                _ => new OperationResult
                {
                    Success = false,
                    ErrorMessage = "Unknown recovery strategy"
                }
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing recovery strategy {Strategy} for PaymentId: {PaymentId}",
                strategy, payment.PaymentId);
            
            return new OperationResult
            {
                Success = false,
                ErrorMessage = "Recovery strategy execution failed"
            };
        }
    }

    private async Task<OperationResult> ResetPaymentToInitialState(Payment payment)
    {
        try
        {
            if (payment.Status != Enums.PaymentStatus.NEW)
            {
                var resetResult = await _lifecycleService.TransitionPaymentAsync(payment.Id, Enums.PaymentStatus.NEW);
                if (!resetResult.Success)
                {
                    return new OperationResult
                    {
                        Success = false,
                        ErrorMessage = "Failed to reset payment to initial state"
                    };
                }
            }

            return new OperationResult
            {
                Success = true,
                ErrorMessage = "Payment reset to initial state",
                NewStatus = Enums.PaymentStatus.NEW
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error resetting payment to initial state for PaymentId: {PaymentId}", payment.PaymentId);
            return new OperationResult
            {
                Success = false,
                ErrorMessage = "Reset operation failed"
            };
        }
    }

    private List<string> GetNextAllowedOperations(Enums.PaymentStatus currentStatus)
    {
        return currentStatus switch
        {
            Enums.PaymentStatus.NEW => new List<string> { "SUBMIT_FORM", "CANCEL_PAYMENT" },
            Enums.PaymentStatus.AUTHORIZED => new List<string> { "CONFIRM_PAYMENT", "CANCEL_PAYMENT" },
            Enums.PaymentStatus.CONFIRMED => new List<string> { "REFUND_PAYMENT" },
            Enums.PaymentStatus.FAILED => new List<string> { "RETRY_PAYMENT", "CANCEL_PAYMENT" },
            Enums.PaymentStatus.CANCELLED => new List<string>(),
            Enums.PaymentStatus.REFUNDED => new List<string>(),
            _ => new List<string>()
        };
    }
}

// Supporting classes and enums

public enum FormLifecycleStage
{
    Initialization = 0,
    Processing = 1,
    Completed = 2,
    ErrorRecovery = 3,
    Failed = 4
}

public enum FormLifecycleOperationType
{
    Initialization = 0,
    BusinessRuleValidation = 1,
    PaymentAuthorization = 2,
    StatusTransition = 3,
    ErrorRecovery = 4,
    Rollback = 5
}

public enum OperationStatus
{
    Pending = 0,
    InProgress = 1,
    Completed = 2,
    Failed = 3
}

public enum FormLifecycleErrorType
{
    ValidationError = 0,
    NetworkError = 1,
    PaymentProcessingError = 2,
    SecurityViolation = 3,
    SessionExpired = 4,
    SystemError = 5
}

public enum FormErrorRecoveryStrategy
{
    RetryWithCorrection = 0,
    RetryOperation = 1,
    ResetToInitial = 2,
    TerminateSession = 3,
    RequireReinitialization = 4
}

public class FormLifecycleInitializationRequest
{
    public string PaymentId { get; set; } = string.Empty;
    public string FormSessionId { get; set; } = string.Empty;
    public string ClientIp { get; set; } = string.Empty;
    public string UserAgent { get; set; } = string.Empty;
}

public class FormLifecycleSubmissionRequest
{
    public string PaymentId { get; set; } = string.Empty;
    public string FormSessionId { get; set; } = string.Empty;
    public string ClientIp { get; set; } = string.Empty;
    public string UserAgent { get; set; } = string.Empty;
    public Dictionary<string, string> FormData { get; set; } = new();
}

public class FormLifecycleErrorRecoveryRequest
{
    public string PaymentId { get; set; } = string.Empty;
    public string FormSessionId { get; set; } = string.Empty;
    public string ClientIp { get; set; } = string.Empty;
    public string UserAgent { get; set; } = string.Empty;
    public FormLifecycleErrorType ErrorType { get; set; }
    public string? ErrorMessage { get; set; }
}

public class PaymentFormLifecycleResult
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public PaymentFormLifecycleContext? LifecycleContext { get; set; }
    public Enums.PaymentStatus CurrentStatus { get; set; }
    public List<string> NextAllowedOperations { get; set; } = new();
}

public class PaymentFormLifecycleContext
{
    public string PaymentId { get; set; } = string.Empty;
    public Guid PaymentEntityId { get; set; }
    public Enums.PaymentStatus InitialStatus { get; set; }
    public Enums.PaymentStatus CurrentStatus { get; set; }
    public string FormSessionId { get; set; } = string.Empty;
    public string ClientIp { get; set; } = string.Empty;
    public string UserAgent { get; set; } = string.Empty;
    public DateTime InitiatedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public FormLifecycleStage LifecycleStage { get; set; }
    public List<FormLifecycleOperation> Operations { get; set; } = new();
}

public class FormLifecycleOperation
{
    public FormLifecycleOperationType OperationType { get; set; }
    public DateTime ExecutedAt { get; set; }
    public OperationStatus Status { get; set; }
    public string Description { get; set; } = string.Empty;
    public Dictionary<string, object>? AdditionalData { get; set; }
}

public class OperationResult
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public Enums.PaymentStatus? NewStatus { get; set; }
    public Dictionary<string, object>? AdditionalData { get; set; }
}

public class RollbackOperation
{
    public string OperationType { get; set; } = string.Empty;
    public string PaymentId { get; set; } = string.Empty;
    public Enums.PaymentStatus OriginalStatus { get; set; }
    public Func<Task> ExecuteRollback { get; set; } = () => Task.CompletedTask;
}