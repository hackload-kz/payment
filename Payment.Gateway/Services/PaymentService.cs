using Microsoft.Extensions.Logging;
using Payment.Gateway.DTOs;
using Payment.Gateway.Models;
using System.Text.Json;

namespace Payment.Gateway.Services;

public class PaymentService : IPaymentService
{
    private readonly IPaymentRepository _paymentRepository;
    private readonly IPaymentStateMachine _stateMachine;
    private readonly ITokenGenerationService _tokenService;
    private readonly IMerchantService _merchantService;
    private readonly ILogger<PaymentService> _logger;

    public PaymentService(
        IPaymentRepository paymentRepository,
        IPaymentStateMachine stateMachine,
        ITokenGenerationService tokenService,
        IMerchantService merchantService,
        ILogger<PaymentService> logger)
    {
        _paymentRepository = paymentRepository;
        _stateMachine = stateMachine;
        _tokenService = tokenService;
        _merchantService = merchantService;
        _logger = logger;
    }

    public async Task<InitPaymentResponse> InitializePaymentAsync(InitPaymentRequest request)
    {
        try
        {
            // Validate merchant credentials
            var merchant = await _merchantService.GetMerchantAsync(request.TerminalKey);
            if (merchant == null || !merchant.IsActive)
            {
                return new InitPaymentResponse
                {
                    TerminalKey = request.TerminalKey,
                    Amount = request.Amount,
                    OrderId = request.OrderId,
                    Success = false,
                    Status = "ERROR",
                    PaymentId = string.Empty,
                    ErrorCode = "202", // Terminal blocked
                    Message = "Terminal not found or inactive"
                };
            }

            // Validate token
            var requestParameters = ConvertRequestToDictionary(request);
            var expectedToken = _tokenService.GenerateToken(requestParameters, merchant.Password);
            
            if (request.Token != expectedToken)
            {
                _logger.LogWarning("Invalid token for TerminalKey: {TerminalKey}, OrderId: {OrderId}", 
                    request.TerminalKey, request.OrderId);
                
                return new InitPaymentResponse
                {
                    TerminalKey = request.TerminalKey,
                    Amount = request.Amount,
                    OrderId = request.OrderId,
                    Success = false,
                    Status = "ERROR",
                    PaymentId = string.Empty,
                    ErrorCode = "204", // Invalid token
                    Message = "Invalid token signature"
                };
            }

            // Check for duplicate OrderId
            var existingPayments = await _paymentRepository.GetByOrderIdAsync(request.OrderId, request.TerminalKey);
            if (existingPayments.Any())
            {
                _logger.LogWarning("Duplicate OrderId: {OrderId} for TerminalKey: {TerminalKey}", 
                    request.OrderId, request.TerminalKey);
                
                return new InitPaymentResponse
                {
                    TerminalKey = request.TerminalKey,
                    Amount = request.Amount,
                    OrderId = request.OrderId,
                    Success = false,
                    Status = "ERROR",
                    PaymentId = string.Empty,
                    ErrorCode = "335", // Duplicate order
                    Message = "Order with this OrderId already exists"
                };
            }

            // Generate unique PaymentId
            var paymentId = GeneratePaymentId();

            // Create payment entity
            var payment = new PaymentEntity
            {
                PaymentId = paymentId,
                OrderId = request.OrderId,
                TerminalKey = request.TerminalKey,
                Amount = request.Amount,
                CurrentStatus = PaymentStatus.INIT,
                Description = request.Description,
                CustomerKey = request.CustomerKey,
                PayType = request.PayType ?? "O", // Default to single-stage
                Language = request.Language ?? "ru", // Default to Russian
                NotificationURL = request.NotificationURL,
                SuccessURL = request.SuccessURL,
                FailURL = request.FailURL,
                ExpirationDate = request.RedirectDueDate ?? DateTime.UtcNow.AddDays(1), // Default 24 hours
                Recurrent = request.Recurrent == "Y",
                DataJson = request.DATA != null ? JsonSerializer.Serialize(request.DATA) : null,
                ReceiptJson = request.Receipt != null ? JsonSerializer.Serialize(request.Receipt) : null,
                ShopsJson = request.Shops != null ? JsonSerializer.Serialize(request.Shops) : null,
                CreatedDate = DateTime.UtcNow,
                UpdatedDate = DateTime.UtcNow
            };

            // Save payment to repository
            await _paymentRepository.CreateAsync(payment);

            // Transition to NEW state
            await _stateMachine.TransitionAsync(paymentId, PaymentStatus.NEW);

            // Generate PaymentURL for non-PCI DSS merchants
            var paymentUrl = GeneratePaymentUrl(paymentId);

            _logger.LogInformation("Payment initialized successfully. PaymentId: {PaymentId}, OrderId: {OrderId}", 
                paymentId, request.OrderId);

            return new InitPaymentResponse
            {
                TerminalKey = request.TerminalKey,
                Amount = request.Amount,
                OrderId = request.OrderId,
                Success = true,
                Status = "NEW",
                PaymentId = paymentId,
                ErrorCode = "0",
                PaymentURL = paymentUrl
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error initializing payment for OrderId: {OrderId}", request.OrderId);
            
            return new InitPaymentResponse
            {
                TerminalKey = request.TerminalKey,
                Amount = request.Amount,
                OrderId = request.OrderId,
                Success = false,
                Status = "ERROR",
                PaymentId = string.Empty,
                ErrorCode = "999", // System error
                Message = "Internal server error",
                Details = ex.Message
            };
        }
    }

    private static Dictionary<string, object> ConvertRequestToDictionary(InitPaymentRequest request)
    {
        var parameters = new Dictionary<string, object>
        {
            ["TerminalKey"] = request.TerminalKey,
            ["Amount"] = request.Amount,
            ["OrderId"] = request.OrderId
        };

        // Add optional simple parameters
        if (!string.IsNullOrEmpty(request.PayType))
            parameters["PayType"] = request.PayType;
        
        if (!string.IsNullOrEmpty(request.Description))
            parameters["Description"] = request.Description;
        
        if (!string.IsNullOrEmpty(request.CustomerKey))
            parameters["CustomerKey"] = request.CustomerKey;
        
        if (!string.IsNullOrEmpty(request.Recurrent))
            parameters["Recurrent"] = request.Recurrent;
        
        if (!string.IsNullOrEmpty(request.Language))
            parameters["Language"] = request.Language;
        
        if (!string.IsNullOrEmpty(request.NotificationURL))
            parameters["NotificationURL"] = request.NotificationURL;
        
        if (!string.IsNullOrEmpty(request.SuccessURL))
            parameters["SuccessURL"] = request.SuccessURL;
        
        if (!string.IsNullOrEmpty(request.FailURL))
            parameters["FailURL"] = request.FailURL;
        
        if (request.RedirectDueDate.HasValue)
            parameters["RedirectDueDate"] = request.RedirectDueDate.Value.ToString("yyyy-MM-ddTHH:mm:sszzz");
        
        if (!string.IsNullOrEmpty(request.Descriptor))
            parameters["Descriptor"] = request.Descriptor;

        // Note: Complex objects (DATA, Receipt, Shops) are excluded from token generation per specification

        return parameters;
    }

    private static string GeneratePaymentId()
    {
        // Generate a unique 20-character payment ID
        return DateTime.UtcNow.ToString("yyyyMMddHHmmss") + Random.Shared.Next(100000, 999999);
    }

    private static string GeneratePaymentUrl(string paymentId)
    {
        // Generate payment URL for hosted payment page
        return $"https://securepay.gateway.com/pay/form/{paymentId}";
    }

    public async Task<ConfirmPaymentResponse> ConfirmPaymentAsync(ConfirmPaymentRequest request)
    {
        try
        {
            // Validate merchant credentials
            var merchant = await _merchantService.GetMerchantAsync(request.TerminalKey);
            if (merchant == null || !merchant.IsActive)
            {
                return new ConfirmPaymentResponse
                {
                    TerminalKey = request.TerminalKey,
                    OrderId = string.Empty,
                    Success = false,
                    Status = "ERROR",
                    PaymentId = request.PaymentId,
                    ErrorCode = "202", // Terminal blocked
                    Message = "Terminal not found or inactive"
                };
            }

            // Validate token
            var requestParameters = ConvertConfirmRequestToDictionary(request);
            var expectedToken = _tokenService.GenerateToken(requestParameters, merchant.Password);
            
            if (request.Token != expectedToken)
            {
                _logger.LogWarning("Invalid token for TerminalKey: {TerminalKey}, PaymentId: {PaymentId}", 
                    request.TerminalKey, request.PaymentId);
                
                return new ConfirmPaymentResponse
                {
                    TerminalKey = request.TerminalKey,
                    OrderId = string.Empty,
                    Success = false,
                    Status = "ERROR",
                    PaymentId = request.PaymentId,
                    ErrorCode = "204", // Invalid token
                    Message = "Invalid token signature"
                };
            }

            // Get payment by PaymentId
            var payment = await _paymentRepository.GetByIdAsync(request.PaymentId);
            if (payment == null)
            {
                _logger.LogWarning("Payment not found: {PaymentId}", request.PaymentId);
                
                return new ConfirmPaymentResponse
                {
                    TerminalKey = request.TerminalKey,
                    OrderId = string.Empty,
                    Success = false,
                    Status = "ERROR",
                    PaymentId = request.PaymentId,
                    ErrorCode = "255", // Payment not found
                    Message = "Payment not found"
                };
            }

            // Verify payment belongs to the terminal
            if (payment.TerminalKey != request.TerminalKey)
            {
                _logger.LogWarning("Payment {PaymentId} does not belong to terminal {TerminalKey}", 
                    request.PaymentId, request.TerminalKey);
                
                return new ConfirmPaymentResponse
                {
                    TerminalKey = request.TerminalKey,
                    OrderId = payment.OrderId,
                    Success = false,
                    Status = payment.CurrentStatus.ToString(),
                    PaymentId = request.PaymentId,
                    ErrorCode = "255", // Payment not found (security)
                    Message = "Payment not found"
                };
            }

            // Validate payment is in AUTHORIZED status
            if (payment.CurrentStatus != PaymentStatus.AUTHORIZED)
            {
                _logger.LogWarning("Payment {PaymentId} not in AUTHORIZED status. Current: {Status}", 
                    request.PaymentId, payment.CurrentStatus);
                
                return new ConfirmPaymentResponse
                {
                    TerminalKey = request.TerminalKey,
                    OrderId = payment.OrderId,
                    Success = false,
                    Status = payment.CurrentStatus.ToString(),
                    PaymentId = request.PaymentId,
                    ErrorCode = "1003", // Invalid status
                    Message = "Payment not in valid status for confirmation",
                    Details = $"Payment must be in AUTHORIZED status to perform confirmation. Current status: {payment.CurrentStatus}"
                };
            }

            // Validate confirmation amount
            var confirmAmount = request.Amount ?? payment.Amount;
            if (confirmAmount > payment.Amount)
            {
                _logger.LogWarning("Confirmation amount {ConfirmAmount} exceeds authorized amount {AuthorizedAmount} for payment {PaymentId}", 
                    confirmAmount, payment.Amount, request.PaymentId);
                
                return new ConfirmPaymentResponse
                {
                    TerminalKey = request.TerminalKey,
                    OrderId = payment.OrderId,
                    Success = false,
                    Status = payment.CurrentStatus.ToString(),
                    PaymentId = request.PaymentId,
                    ErrorCode = "1007", // Amount exceeded
                    Message = "Confirmation amount exceeds authorized amount",
                    Details = $"Requested: {confirmAmount} kopecks, Authorized: {payment.Amount} kopecks"
                };
            }

            // Transition to CONFIRMING state
            var transitionSuccess = await _stateMachine.TransitionAsync(request.PaymentId, PaymentStatus.CONFIRMING);
            if (!transitionSuccess)
            {
                _logger.LogError("Failed to transition payment {PaymentId} to CONFIRMING status", request.PaymentId);
                
                return new ConfirmPaymentResponse
                {
                    TerminalKey = request.TerminalKey,
                    OrderId = payment.OrderId,
                    Success = false,
                    Status = payment.CurrentStatus.ToString(),
                    PaymentId = request.PaymentId,
                    ErrorCode = "999", // System error
                    Message = "Internal server error during confirmation"
                };
            }

            // Update payment with confirmation details
            payment.Amount = confirmAmount; // Update to confirmed amount if partial
            payment.ReceiptJson = request.Receipt != null ? System.Text.Json.JsonSerializer.Serialize(request.Receipt) : payment.ReceiptJson;
            payment.ShopsJson = request.Shops != null ? System.Text.Json.JsonSerializer.Serialize(request.Shops) : payment.ShopsJson;
            payment.UpdatedDate = DateTime.UtcNow;

            await _paymentRepository.UpdateAsync(payment);

            // Simulate confirmation processing (in real implementation, this would involve bank processing)
            // Transition to CONFIRMED state
            transitionSuccess = await _stateMachine.TransitionAsync(request.PaymentId, PaymentStatus.CONFIRMED);
            if (!transitionSuccess)
            {
                _logger.LogError("Failed to transition payment {PaymentId} to CONFIRMED status", request.PaymentId);
                
                // In case of failure, the payment remains in CONFIRMING state for manual review
                return new ConfirmPaymentResponse
                {
                    TerminalKey = request.TerminalKey,
                    OrderId = payment.OrderId,
                    Success = true, // Processing started successfully
                    Status = "CONFIRMING",
                    PaymentId = request.PaymentId,
                    ErrorCode = "0"
                };
            }

            _logger.LogInformation("Payment {PaymentId} confirmed successfully. Amount: {Amount}", 
                request.PaymentId, confirmAmount);

            return new ConfirmPaymentResponse
            {
                TerminalKey = request.TerminalKey,
                OrderId = payment.OrderId,
                Success = true,
                Status = "CONFIRMED",
                PaymentId = request.PaymentId,
                ErrorCode = "0"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error confirming payment {PaymentId}", request.PaymentId);
            
            return new ConfirmPaymentResponse
            {
                TerminalKey = request.TerminalKey,
                OrderId = string.Empty,
                Success = false,
                Status = "ERROR",
                PaymentId = request.PaymentId,
                ErrorCode = "999", // System error
                Message = "Internal server error",
                Details = ex.Message
            };
        }
    }

    private static Dictionary<string, object> ConvertConfirmRequestToDictionary(ConfirmPaymentRequest request)
    {
        var parameters = new Dictionary<string, object>
        {
            ["TerminalKey"] = request.TerminalKey,
            ["PaymentId"] = request.PaymentId
        };

        // Add optional simple parameters
        if (!string.IsNullOrEmpty(request.IP))
            parameters["IP"] = request.IP;
        
        if (request.Amount.HasValue)
            parameters["Amount"] = request.Amount.Value;
        
        if (!string.IsNullOrEmpty(request.Route))
            parameters["Route"] = request.Route;
        
        if (!string.IsNullOrEmpty(request.Source))
            parameters["Source"] = request.Source;

        // Note: Complex objects (Receipt, Shops) are excluded from token generation per specification

        return parameters;
    }

    public Task<PaymentEntity> CancelPaymentAsync(string paymentId, object request)
    {
        // Implementation will be added in Task 7
        throw new NotImplementedException("Will be implemented in Task 7");
    }

    public Task<PaymentEntity[]> CheckOrderAsync(string orderId, string terminalKey)
    {
        // Implementation will be added in Task 6
        throw new NotImplementedException("Will be implemented in Task 6");
    }
}