using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using PaymentGateway.Core.Services;
using System.Text.Json;

namespace PaymentGateway.API.Filters;

/// <summary>
/// Action filter for payment authentication using SHA-256 token validation
/// </summary>
public class PaymentAuthenticationFilter : IAsyncActionFilter
{
    private readonly IPaymentAuthenticationService _authenticationService;
    private readonly ILogger<PaymentAuthenticationFilter> _logger;

    public PaymentAuthenticationFilter(
        IPaymentAuthenticationService authenticationService,
        ILogger<PaymentAuthenticationFilter> logger)
    {
        _authenticationService = authenticationService;
        _logger = logger;
    }

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var correlationId = context.HttpContext.TraceIdentifier;
        
        try
        {
            _logger.LogDebug("Starting payment authentication for request {CorrelationId} to {Action}", 
                correlationId, context.ActionDescriptor.DisplayName);

            // Extract request parameters from the action parameters
            var requestParameters = ExtractRequestParameters(context);
            
            if (requestParameters == null || !requestParameters.ContainsKey("teamSlug"))
            {
                _logger.LogWarning("Authentication failed: Missing teamSlug in request parameters for {CorrelationId}. Available keys: {Keys}", 
                    correlationId, requestParameters?.Keys != null ? string.Join(", ", requestParameters.Keys) : "none");
                context.Result = new UnauthorizedObjectResult(new
                {
                    Success = false,
                    ErrorCode = "4001",
                    Message = "Authentication required",
                    Details = "Missing teamSlug in request",
                    CorrelationId = correlationId,
                    Timestamp = DateTime.UtcNow
                });
                return;
            }

            // Authenticate the request
            var authResult = await _authenticationService.AuthenticateAsync(requestParameters);
            
            if (!authResult.IsAuthenticated)
            {
                var teamSlug = requestParameters.GetValueOrDefault("teamSlug");
                _logger.LogWarning("Authentication failed for TeamSlug {TeamSlug}: {Reason} for {CorrelationId}", 
                    teamSlug, authResult.FailureReason, correlationId);
                
                context.Result = new UnauthorizedObjectResult(new
                {
                    Success = false,
                    ErrorCode = authResult.ErrorCode ?? "4001",
                    Message = authResult.FailureReason ?? "Authentication failed",
                    Details = "Authentication failed for the provided credentials",
                    CorrelationId = correlationId,
                    Timestamp = DateTime.UtcNow
                });
                return;
            }

            var successTeamSlug = requestParameters.GetValueOrDefault("teamSlug");
            _logger.LogDebug("Authentication successful for TeamSlug {TeamSlug} for {CorrelationId}", 
                successTeamSlug, correlationId);

            // Store authentication result in HttpContext for use by the action
            context.HttpContext.Items["AuthenticationResult"] = authResult;
            context.HttpContext.Items["AuthenticatedTeam"] = authResult.Team;

            // Continue to the action
            await next();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Payment authentication error for request {CorrelationId}", correlationId);
            
            context.Result = new ObjectResult(new
            {
                Success = false,
                ErrorCode = "9007",
                Message = "Internal authentication error",
                Details = "Authentication processing failed",
                CorrelationId = correlationId,
                Timestamp = DateTime.UtcNow
            })
            {
                StatusCode = 500
            };
        }
    }

    private Dictionary<string, object>? ExtractRequestParameters(ActionExecutingContext context)
    {
        try
        {
            // Use case-insensitive dictionary
            var parameters = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

            // First, try to get parameters from action arguments (request body)
            var requestObject = context.ActionArguments.Values.FirstOrDefault(v => v != null);
            if (requestObject != null)
            {
                // Use reflection to get all properties from the request object
                var properties = requestObject.GetType().GetProperties();
                foreach (var property in properties)
                {
                    var value = property.GetValue(requestObject);
                    if (value != null)
                    {
                        parameters[property.Name] = value;
                    }
                }
            }

            // Also check query string parameters (for GET requests)
            foreach (var queryParam in context.HttpContext.Request.Query)
            {
                if (!string.IsNullOrEmpty(queryParam.Value))
                {
                    parameters[queryParam.Key] = queryParam.Value.ToString();
                }
            }

            // Check route values as well
            foreach (var routeValue in context.RouteData.Values)
            {
                if (routeValue.Value != null)
                {
                    parameters[routeValue.Key] = routeValue.Value;
                }
            }

            return parameters.Count > 0 ? parameters : null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to extract request parameters");
            return null;
        }
    }
}