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
                // Use SIMPLIFIED token formula: Amount + Currency + OrderId + Password + TeamSlug
                // Extract only the 5 core parameters as per documentation
                var extractedParameters = ExtractSimplifiedParameters(requestObject);
                foreach (var kvp in extractedParameters)
                {
                    parameters[kvp.Key] = kvp.Value;
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

            // CRITICAL FIX: Do NOT include route values (action, controller) in token generation
            // Token generation should only be based on request body parameters as per specification
            // foreach (var routeValue in context.RouteData.Values)
            // {
            //     if (routeValue.Value != null)
            //     {
            //         parameters[routeValue.Key] = routeValue.Value;
            //     }
            // }

            return parameters.Count > 0 ? parameters : null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to extract request parameters");
            return null;
        }
    }

    /// <summary>
    /// Extract only root-level scalar parameters that were actually provided in the JSON request.
    /// This prevents including DTO default values in token generation, per payment-authentication.md spec.
    /// </summary>
    private Dictionary<string, object> ExtractRootLevelScalarParametersFromRequest(ActionExecutingContext context, object requestObject)
    {
        var parameters = new Dictionary<string, object>();

        try
        {
            // Get the original request body as string to parse what was actually provided
            context.HttpContext.Request.Body.Position = 0;
            using var reader = new StreamReader(context.HttpContext.Request.Body, leaveOpen: true);
            var requestBody = reader.ReadToEnd();
            context.HttpContext.Request.Body.Position = 0;

            if (string.IsNullOrEmpty(requestBody))
            {
                // Fallback to DTO-based extraction if we can't read the original body
                return ExtractFromDtoWithDefaults(requestObject);
            }

            // Parse JSON to see what fields were actually provided
            using var jsonDoc = JsonDocument.Parse(requestBody);
            var root = jsonDoc.RootElement;

            // Always include required fields (these must be present)
            if (root.TryGetProperty("teamSlug", out var teamSlug) && teamSlug.ValueKind == JsonValueKind.String)
                parameters["TeamSlug"] = teamSlug.GetString()!;
            
            if (root.TryGetProperty("token", out var token) && token.ValueKind == JsonValueKind.String)
                parameters["Token"] = token.GetString()!;
            
            if (root.TryGetProperty("amount", out var amount) && amount.ValueKind == JsonValueKind.Number)
                parameters["Amount"] = amount.GetDecimal().ToString();
            
            if (root.TryGetProperty("orderId", out var orderId) && orderId.ValueKind == JsonValueKind.String)
                parameters["OrderId"] = orderId.GetString()!;
            
            if (root.TryGetProperty("currency", out var currency) && currency.ValueKind == JsonValueKind.String)
                parameters["Currency"] = currency.GetString()!;

            // Include optional fields only if they were explicitly provided in JSON
            if (root.TryGetProperty("description", out var desc) && desc.ValueKind == JsonValueKind.String)
                parameters["Description"] = desc.GetString()!;
            
            if (root.TryGetProperty("customerKey", out var custKey) && custKey.ValueKind == JsonValueKind.String)
                parameters["CustomerKey"] = custKey.GetString()!;
            
            if (root.TryGetProperty("email", out var email) && email.ValueKind == JsonValueKind.String)
                parameters["Email"] = email.GetString()!;
            
            if (root.TryGetProperty("phone", out var phone) && phone.ValueKind == JsonValueKind.String)
                parameters["Phone"] = phone.GetString()!;
            
            if (root.TryGetProperty("language", out var lang) && lang.ValueKind == JsonValueKind.String)
                parameters["Language"] = lang.GetString()!;
            
            if (root.TryGetProperty("paymentExpiry", out var payExp) && payExp.ValueKind == JsonValueKind.Number)
                parameters["PaymentExpiry"] = payExp.GetInt32().ToString();
            
            if (root.TryGetProperty("successURL", out var succUrl) && succUrl.ValueKind == JsonValueKind.String)
                parameters["SuccessURL"] = succUrl.GetString()!;
            
            if (root.TryGetProperty("failURL", out var failUrl) && failUrl.ValueKind == JsonValueKind.String)
                parameters["FailURL"] = failUrl.GetString()!;
            
            if (root.TryGetProperty("notificationURL", out var notifUrl) && notifUrl.ValueKind == JsonValueKind.String)
                parameters["NotificationURL"] = notifUrl.GetString()!;
            
            if (root.TryGetProperty("redirectMethod", out var redirMethod) && redirMethod.ValueKind == JsonValueKind.String)
                parameters["RedirectMethod"] = redirMethod.GetString()!;
            
            if (root.TryGetProperty("version", out var vers) && vers.ValueKind == JsonValueKind.String)
                parameters["Version"] = vers.GetString()!;
            
            if (root.TryGetProperty("payType", out var payType) && payType.ValueKind == JsonValueKind.String)
                parameters["PayType"] = payType.GetString()!;

            // CRITICAL: Do NOT include server-generated fields like Timestamp, CorrelationId, etc.
            // These have default values in BaseRequestDto but were NOT provided by the client
            // Exclude nested objects like Items, Receipt, Data per specification

            _logger.LogInformation("FILTER DEBUG: Extracted {Count} parameters from JSON request", parameters.Count);
            foreach (var kvp in parameters.OrderBy(x => x.Key))
            {
                _logger.LogInformation("FILTER DEBUG: Parameter {Key} = {Value}", kvp.Key, kvp.Value);
            }

            return parameters;
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to parse request JSON, falling back to DTO extraction");
            return ExtractFromDtoWithDefaults(requestObject);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in request parameter extraction");
            return ExtractFromDtoWithDefaults(requestObject);
        }
    }

    /// <summary>
    /// Fallback method when JSON parsing fails - extract from DTO but exclude known defaults
    /// </summary>
    private Dictionary<string, object> ExtractFromDtoWithDefaults(object requestObject)
    {
        var parameters = new Dictionary<string, object>();
        
        // Use reflection but be smart about excluding default values
        var properties = requestObject.GetType().GetProperties();
        
        _logger.LogInformation("FILTER DEBUG: Processing {Count} DTO properties", properties.Length);
        
        foreach (var property in properties)
        {
            var value = property.GetValue(requestObject);
            if (value == null) continue;

            _logger.LogInformation("FILTER DEBUG: Property {Name} = {Value} (Type: {Type})", 
                property.Name, value, value.GetType().Name);

            // Include based on property name and exclude known defaults
            switch (property.Name)
            {
                case "TeamSlug":
                case "Token":
                case "Amount":
                case "OrderId":
                case "Currency":
                    parameters[property.Name] = value;
                    _logger.LogInformation("FILTER DEBUG: INCLUDED {Name} = {Value}", property.Name, value);
                    break;

                case "Description":
                case "CustomerKey":
                case "Email":
                case "Phone":
                case "SuccessURL":
                case "FailURL":
                case "NotificationURL":
                case "PayType":
                    if (!string.IsNullOrEmpty(value.ToString()))
                    {
                        parameters[property.Name] = value;
                        _logger.LogInformation("FILTER DEBUG: INCLUDED {Name} = {Value}", property.Name, value);
                    }
                    else
                    {
                        _logger.LogInformation("FILTER DEBUG: EXCLUDED {Name} (empty string)", property.Name);
                    }
                    break;

                case "Language":
                    if (!string.IsNullOrEmpty(value.ToString()) && !value.ToString().Equals("ru"))
                    {
                        parameters[property.Name] = value;
                        _logger.LogInformation("FILTER DEBUG: INCLUDED {Name} = {Value}", property.Name, value);
                    }
                    else
                    {
                        _logger.LogInformation("FILTER DEBUG: EXCLUDED {Name} = {Value} (default)", property.Name, value);
                    }
                    break;

                case "PaymentExpiry":
                    if (!value.Equals(30)) // Only include if not the default
                    {
                        parameters[property.Name] = value.ToString()!;
                        _logger.LogInformation("FILTER DEBUG: INCLUDED {Name} = {Value}", property.Name, value);
                    }
                    else
                    {
                        _logger.LogInformation("FILTER DEBUG: EXCLUDED {Name} = {Value} (default)", property.Name, value);
                    }
                    break;

                case "RedirectMethod":
                    if (!string.IsNullOrEmpty(value.ToString()) && !value.ToString().Equals("POST"))
                    {
                        parameters[property.Name] = value;
                        _logger.LogInformation("FILTER DEBUG: INCLUDED {Name} = {Value}", property.Name, value);
                    }
                    else
                    {
                        _logger.LogInformation("FILTER DEBUG: EXCLUDED {Name} = {Value} (default)", property.Name, value);
                    }
                    break;

                case "Version":
                    if (!string.IsNullOrEmpty(value.ToString()) && !value.ToString().Equals("1.0"))
                    {
                        parameters[property.Name] = value;
                        _logger.LogInformation("FILTER DEBUG: INCLUDED {Name} = {Value}", property.Name, value);
                    }
                    else
                    {
                        _logger.LogInformation("FILTER DEBUG: EXCLUDED {Name} = {Value} (default)", property.Name, value);
                    }
                    break;

                // CRITICAL: Exclude server-generated fields
                case "Timestamp":
                case "CorrelationId":
                    // Do NOT include these - they are server-generated defaults
                    _logger.LogInformation("FILTER DEBUG: EXCLUDED {Name} = {Value} (server-generated)", property.Name, value);
                    break;

                // Exclude complex objects
                case "Items":
                case "Receipt":
                case "Data":
                    // Do NOT include nested objects per specification
                    _logger.LogInformation("FILTER DEBUG: EXCLUDED {Name} (complex object)", property.Name);
                    break;

                default:
                    _logger.LogInformation("FILTER DEBUG: EXCLUDED {Name} = {Value} (unknown property)", property.Name, value);
                    break;
            }
        }

        _logger.LogInformation("FILTER DEBUG: Final parameter count: {Count}", parameters.Count);
        foreach (var kvp in parameters.OrderBy(x => x.Key))
        {
            _logger.LogInformation("FILTER DEBUG: Final parameter {Key} = {Value}", kvp.Key, kvp.Value);
        }

        return parameters;
    }

    /// <summary>
    /// Extract only the 5 core parameters for simplified authentication: Amount, Currency, OrderId, TeamSlug, Token
    /// Password will be added by the authentication service
    /// </summary>
    private Dictionary<string, object> ExtractSimplifiedParameters(object requestObject)
    {
        var parameters = new Dictionary<string, object>();
        var properties = requestObject.GetType().GetProperties();
        
        _logger.LogInformation("FILTER DEBUG: Using SIMPLIFIED parameter extraction (5 params only)");
        
        foreach (var property in properties)
        {
            var value = property.GetValue(requestObject);
            if (value == null) continue;

            // Only include the 5 core parameters for simplified authentication
            switch (property.Name)
            {
                case "TeamSlug":
                case "Token":
                case "Amount":
                case "OrderId":
                case "Currency":
                    parameters[property.Name] = value;
                    _logger.LogInformation("FILTER DEBUG: SIMPLIFIED - INCLUDED {Name} = {Value}", property.Name, value);
                    break;
                default:
                    _logger.LogInformation("FILTER DEBUG: SIMPLIFIED - EXCLUDED {Name} = {Value} (not in core 5)", property.Name, value);
                    break;
            }
        }

        _logger.LogInformation("FILTER DEBUG: SIMPLIFIED - Final parameter count: {Count}", parameters.Count);
        return parameters;
    }
}