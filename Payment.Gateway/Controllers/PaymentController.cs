using Microsoft.AspNetCore.Mvc;
using Payment.Gateway.DTOs;
using Payment.Gateway.Services;
using Payment.Gateway.Validators;

namespace Payment.Gateway.Controllers;

[ApiController]
[Route("api/[controller]")]
public class PaymentController : ControllerBase
{
    private readonly IPaymentService _paymentService;
    private readonly ILogger<PaymentController> _logger;
    private readonly InitPaymentRequestValidator _validator;

    public PaymentController(
        IPaymentService paymentService, 
        ILogger<PaymentController> logger,
        InitPaymentRequestValidator validator)
    {
        _paymentService = paymentService;
        _logger = logger;
        _validator = validator;
    }

    /// <summary>
    /// Initialize a new payment session
    /// </summary>
    /// <param name="request">Payment initialization request</param>
    /// <returns>Payment initialization response with PaymentId and status</returns>
    [HttpPost("init")]
    [Consumes("application/json")]
    [Produces("application/json")]
    public async Task<ActionResult<InitPaymentResponse>> InitializePayment([FromBody] InitPaymentRequest request)
    {
        try
        {
            _logger.LogInformation("Init payment request received for OrderId: {OrderId}, TerminalKey: {TerminalKey}, Amount: {Amount}", 
                request.OrderId, request.TerminalKey, request.Amount);

            // Validate request
            var validationResult = await _validator.ValidateAsync(request);
            if (!validationResult.IsValid)
            {
                _logger.LogWarning("Init payment validation failed for OrderId: {OrderId}. Errors: {Errors}", 
                    request.OrderId, string.Join(", ", validationResult.Errors.Select(e => e.ErrorMessage)));

                return BadRequest(new InitPaymentResponse
                {
                    TerminalKey = request.TerminalKey,
                    Amount = request.Amount,
                    OrderId = request.OrderId,
                    Success = false,
                    Status = "ERROR",
                    PaymentId = string.Empty,
                    ErrorCode = "251", // Validation error
                    Message = "Validation failed",
                    Details = string.Join("; ", validationResult.Errors.Select(e => e.ErrorMessage))
                });
            }

            // Process payment initialization
            var response = await _paymentService.InitializePaymentAsync(request);

            _logger.LogInformation("Init payment {Status} for OrderId: {OrderId}, PaymentId: {PaymentId}", 
                response.Success ? "successful" : "failed", request.OrderId, response.PaymentId);

            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing init payment for OrderId: {OrderId}", request.OrderId);

            return StatusCode(500, new InitPaymentResponse
            {
                TerminalKey = request.TerminalKey,
                Amount = request.Amount,
                OrderId = request.OrderId,
                Success = false,
                Status = "ERROR",
                PaymentId = string.Empty,
                ErrorCode = "999", // System error
                Message = "Internal server error",
                Details = "An unexpected error occurred while processing the payment"
            });
        }
    }
}