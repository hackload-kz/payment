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

    public Task<PaymentEntity> ConfirmPaymentAsync(string paymentId, object request)
    {
        // Implementation will be added in Task 5
        throw new NotImplementedException("Will be implemented in Task 5");
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