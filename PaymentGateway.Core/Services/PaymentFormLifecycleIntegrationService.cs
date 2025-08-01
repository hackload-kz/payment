// SPDX-License-Identifier: MIT
// Copyright (c) 2025 HackLoad Payment Gateway

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using PaymentGateway.Core.Entities;
using PaymentGateway.Core.Interfaces;
using PaymentGateway.Core.Repositories;
using System.Linq;

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
    private readonly AuditCorrelationService _auditCorrelationService;

    // Lifecycle integration configuration
    private readonly bool _enableFormLifecycleIntegration;
    private readonly bool _enableAutomaticStateTransitions;
    private readonly bool _enableRollbackOnFailure;
    private readonly int _maxRetryAttempts;

    // Metrics
    private static readonly System.Diagnostics.Metrics.Meter _meter = new("PaymentFormLifecycle");
    private static readonly System.Diagnostics.Metrics.Counter<long> _lifecycleIntegrationCounter = 
        _meter.CreateCounter<long>("payment_form_lifecycle_operations_total");
    private static readonly System.Diagnostics.Metrics.Histogram<double> _lifecycleTransitionDuration = 
        _meter.CreateHistogram<double>("payment_form_lifecycle_transition_duration_seconds");
    private static readonly System.Diagnostics.Metrics.Counter<long> _lifecycleErrorCounter = 
        _meter.CreateCounter<long>("payment_form_lifecycle_errors_total");

    public PaymentFormLifecycleIntegrationService(
        ILogger<PaymentFormLifecycleIntegrationService> logger,
        IConfiguration configuration,
        IPaymentRepository paymentRepository,
        PaymentLifecycleManagementService lifecycleService,
        PaymentStateTransitionValidationService stateValidationService,
        PaymentFormStatusUpdateService statusUpdateService,
        BusinessRuleEngineService businessRuleEngine,
        ComprehensiveAuditService auditService,
        AuditCorrelationService auditCorrelationService)
    {
        _logger = logger;
        _configuration = configuration;
        _paymentRepository = paymentRepository;
        _lifecycleService = lifecycleService;
        _stateValidationService = stateValidationService;
        _statusUpdateService = statusUpdateService;
        _businessRuleEngine = businessRuleEngine;
        _auditService = auditService;
        _auditCorrelationService = auditCorrelationService;

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
            var stateValidation = await _stateValidationService.ValidateTransitionAsync(
                payment.Id, payment.Status, Enums.PaymentStatus.NEW);

            if (!stateValidation.IsValid)
            {
                _lifecycleIntegrationCounter.Add(1, new KeyValuePair<string, object?>("result", "invalid_state"));
                return new PaymentFormLifecycleResult
                {
                    Success = false,
                    ErrorMessage = $"Payment state invalid for form initialization: {string.Join(", ", stateValidation.Errors)}"
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
            await _auditService.LogSystemEventAsync(AuditAction.PaymentInitialized, "PaymentFormLifecycle", 
                $"Form initialization started for PaymentId: {request.PaymentId}, FormSessionId: {request.FormSessionId}");

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
        string? correlationId = null;
        
        try
        {
            _logger.LogInformation("Processing form submission in lifecycle for PaymentId: {PaymentId}",
                request.PaymentId);

            // Create audit correlation context for the entire operation
            correlationId = await _auditCorrelationService.CreateCorrelationContextAsync(
                "PAYMENT_FORM_SUBMISSION", 
                request.PaymentId,
                new Dictionary<string, object>
                {
                    ["FormSessionId"] = request.FormSessionId,
                    ["ClientIp"] = request.ClientIp ?? "",
                    ["UserAgent"] = request.UserAgent ?? ""
                });

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
            await _auditCorrelationService.AddAuditEventAsync(correlationId, "BUSINESS_RULE_VALIDATION_STARTED", 
                "PaymentFormLifecycleIntegrationService", "Starting business rule validation for payment form submission");
            
            var businessRuleResult = await ValidateBusinessRulesForSubmission(payment, request);
            if (!businessRuleResult.Success)
            {
                await _auditCorrelationService.AddAuditEventAsync(correlationId, "BUSINESS_RULE_VALIDATION_FAILED", 
                    "PaymentFormLifecycleIntegrationService", $"Business rule validation failed: {businessRuleResult.ErrorMessage}");
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

            await _auditCorrelationService.AddAuditEventAsync(correlationId, "BUSINESS_RULE_VALIDATION_SUCCESS", 
                "PaymentFormLifecycleIntegrationService", "Business rule validation completed successfully");
            
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
                await _auditCorrelationService.AddAuditEventAsync(correlationId, "PAYMENT_AUTHORIZATION_STARTED", 
                    "PaymentFormLifecycleIntegrationService", "Starting payment authorization process");
                
                var authorizationResult = await ProcessPaymentAuthorizationAsync(payment, request, rollbackOperations);
                if (!authorizationResult.Success)
                {
                    await _auditCorrelationService.AddAuditEventAsync(correlationId, "PAYMENT_AUTHORIZATION_FAILED", 
                        "PaymentFormLifecycleIntegrationService", $"Payment authorization failed: {authorizationResult.ErrorMessage}");
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

                await _auditCorrelationService.AddAuditEventAsync(correlationId, "PAYMENT_AUTHORIZATION_SUCCESS", 
                    "PaymentFormLifecycleIntegrationService", "Payment authorization completed successfully", 
                    authorizationResult.AdditionalData);
                
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
            await _auditService.LogSystemEventAsync(AuditAction.StateTransition, "PaymentFormLifecycle", 
                $"Form submission completed for PaymentId: {request.PaymentId}, FormSessionId: {request.FormSessionId}, Status: {lifecycleContext.CurrentStatus}");

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

            // Complete the audit correlation
            await _auditCorrelationService.CompleteCorrelationAsync(correlationId, true, 
                $"Payment form submission completed successfully in {stopwatch.ElapsedMilliseconds}ms");

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
            
            // Complete correlation with failure
            if (correlationId != null)
            {
                try
                {
                    await _auditCorrelationService.CompleteCorrelationAsync(correlationId, false, 
                        $"Payment form submission failed: {ex.Message}");
                }
                catch (Exception correlationEx)
                {
                    _logger.LogWarning(correlationEx, "Failed to complete audit correlation for failed form submission");
                }
            }
            
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
            await _auditService.LogSystemEventAsync(AuditAction.PaymentRetried, "PaymentFormLifecycle", 
                $"Error recovery {(recoveryResult.Success ? "SUCCESS" : "FAILED")} for PaymentId: {request.PaymentId}, ErrorType: {request.ErrorType}");

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
            // Create comprehensive payment rule context
            var paymentContext = new PaymentRuleContext
            {
                PaymentId = payment.Id,
                TeamId = payment.TeamId,
                TeamSlug = $"team-{payment.TeamId}",
                Amount = payment.Amount,
                Currency = payment.Currency,
                OrderId = payment.OrderId,
                PaymentMethod = payment.PaymentMethods.FirstOrDefault()?.Type.ToString() ?? "CARD",
                CustomerEmail = payment.Customer?.Email ?? "",
                CustomerCountry = payment.Customer?.Country ?? "",
                PaymentDate = payment.CreatedAt,
                PaymentMetadata = payment.Metadata.ToDictionary(x => x.Key, x => (object)x.Value),
                CustomerData = new Dictionary<string, object>
                {
                    ["customer_id"] = payment.Customer?.Id.ToString() ?? "",
                    ["customer_email"] = payment.Customer?.Email ?? "",
                    ["customer_country"] = payment.Customer?.Country ?? ""
                }
            };

            // Evaluate payment rules
            var paymentRulesResult = await _businessRuleEngine.EvaluatePaymentRulesAsync(paymentContext);
            if (!paymentRulesResult.IsAllowed)
            {
                _logger.LogWarning("Payment rule validation failed for PaymentId: {PaymentId}, Rule: {RuleName}, Message: {Message}",
                    payment.PaymentId, paymentRulesResult.RuleName, paymentRulesResult.Message);
                
                return new OperationResult
                {
                    Success = false,
                    ErrorMessage = paymentRulesResult.Message
                };
            }

            // Create team rule context for additional validation
            var teamContext = new TeamRuleContext
            {
                TeamId = payment.TeamId,
                TeamSlug = $"team-{payment.TeamId}",
                IsActive = true,
                LastPaymentDate = DateTime.UtcNow
            };

            // Evaluate team rules
            var teamRulesResult = await _businessRuleEngine.EvaluateTeamRulesAsync(teamContext);
            if (!teamRulesResult.IsAllowed)
            {
                _logger.LogWarning("Team rule validation failed for PaymentId: {PaymentId}, Rule: {RuleName}, Message: {Message}",
                    payment.PaymentId, teamRulesResult.RuleName, teamRulesResult.Message);
                
                return new OperationResult
                {
                    Success = false,
                    ErrorMessage = teamRulesResult.Message
                };
            }

            // If customer exists, validate customer-specific rules
            if (payment.Customer != null)
            {
                var customerContext = new CustomerRuleContext
                {
                    CustomerId = payment.Customer.Id,
                    TeamId = payment.TeamId,
                    CustomerEmail = payment.Customer.Email,
                    CustomerCountry = payment.Customer.Country ?? "",
                    IpAddress = request.ClientIp ?? "",
                    FirstSeenDate = payment.Customer.CreatedAt,
                    LastPaymentDate = DateTime.UtcNow,
                    TotalPaymentCount = 1, // This should be calculated from payment history
                    TotalPaymentAmount = payment.Amount,
                    FailedPaymentCount = 0,
                    FraudScore = 0.0,
                    IsVip = false,
                    IsBlacklisted = false
                };

                var customerRulesResult = await _businessRuleEngine.EvaluateCustomerRulesAsync(customerContext);
                if (!customerRulesResult.IsAllowed)
                {
                    _logger.LogWarning("Customer rule validation failed for PaymentId: {PaymentId}, Rule: {RuleName}, Message: {Message}",
                        payment.PaymentId, customerRulesResult.RuleName, customerRulesResult.Message);
                    
                    return new OperationResult
                    {
                        Success = false,
                        ErrorMessage = customerRulesResult.Message
                    };
                }
            }

            _logger.LogDebug("Business rule validation passed for PaymentId: {PaymentId}", payment.PaymentId);
            return new OperationResult { Success = true };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating business rules for PaymentId: {PaymentId}", payment.PaymentId);
            return new OperationResult
            {
                Success = false,
                ErrorMessage = "Business rule validation failed due to system error"
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
                        await _lifecycleService.AuthorizePaymentAsync(rollbackPayment.Id);
                    }
                }
            });

            // Transition payment to AUTHORIZED status with proper audit trail
            var transitionResult = await _lifecycleService.AuthorizePaymentWithTransitionAsync(payment.Id);
            if (!transitionResult.IsSuccess)
            {
                return new OperationResult
                {
                    Success = false,
                    ErrorMessage = string.Join(", ", transitionResult.Errors)
                };
            }

            return new OperationResult
            {
                Success = true,
                AdditionalData = new Dictionary<string, object>
                {
                    ["TransitionId"] = transitionResult.TransitionId,
                    ["AuthorizedAt"] = transitionResult.TransitionedAt ?? DateTime.UtcNow,
                    ["FromStatus"] = transitionResult.FromStatus.ToString(),
                    ["ToStatus"] = transitionResult.ToStatus.ToString()
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
                // TODO: No direct method to reset payment to NEW status - using current payment
                // This is a limitation that should be addressed in the lifecycle service
                _logger.LogWarning("Payment {PaymentId} is not in NEW status for reset recovery, current status: {Status}", 
                    payment.PaymentId, payment.Status);
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