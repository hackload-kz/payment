using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;
using System.ComponentModel;
using System.Reflection;

namespace PaymentGateway.Core.DTOs.Documentation;

/// <summary>
/// Custom attribute for OpenAPI documentation
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Class, AllowMultiple = false)]
public class OpenApiDocumentationAttribute : Attribute
{
    public string? Description { get; set; }
    public string? Example { get; set; }
    public string? Format { get; set; }
    public bool IsRequired { get; set; }
    public bool IsDeprecated { get; set; }
    public string? Pattern { get; set; }
    public double? Minimum { get; set; }
    public double? Maximum { get; set; }
    public int? MinLength { get; set; }
    public int? MaxLength { get; set; }
    public string[]? AllowedValues { get; set; }
}

/// <summary>
/// OpenAPI schema filter for payment DTOs
/// </summary>
public class PaymentDtoSchemaFilter : ISchemaFilter
{
    public void Apply(OpenApiSchema schema, SchemaFilterContext context)
    {
        if (context.Type == null)
            return;

        // Add custom documentation for payment-related DTOs
        if (context.Type.Namespace?.Contains("PaymentGateway.Core.DTOs") == true)
        {
            ApplyPaymentDtoDocumentation(schema, context);
        }

        // Apply property-level documentation
        ApplyPropertyDocumentation(schema, context);
    }

    private void ApplyPaymentDtoDocumentation(OpenApiSchema schema, SchemaFilterContext context)
    {
        var typeName = context.Type.Name;

        schema.Description = typeName switch
        {
            "PaymentInitRequestDto" => "Request object for initializing a payment session. This creates the payment and returns a URL for customer redirection to the payment form.",
            "PaymentInitResponseDto" => "Response object containing payment initialization details including payment URL and payment identifier.",
            "PaymentConfirmRequestDto" => "Request object for confirming (capturing) an authorized payment in a two-stage payment process.",
            "PaymentConfirmResponseDto" => "Response object containing confirmation details including captured amounts and settlement information.",
            "PaymentCancelRequestDto" => "Request object for cancelling a payment. Performs different operations based on payment status (reversal, refund, or cancellation).",
            "PaymentCancelResponseDto" => "Response object containing cancellation details including refund information and processing status.",
            "PaymentCheckRequestDto" => "Request object for checking payment status. Can query by payment ID or order ID.",
            "PaymentCheckResponseDto" => "Response object containing detailed payment status information including transaction history and customer details.",
            _ => schema.Description
        };

        // Add examples for request DTOs
        if (typeName.EndsWith("RequestDto"))
        {
            schema.Example = CreateExampleForRequestDto(context.Type);
        }
    }

    private void ApplyPropertyDocumentation(OpenApiSchema schema, SchemaFilterContext context)
    {
        if (context.Type == null || schema.Properties == null)
            return;

        var properties = context.Type.GetProperties();

        foreach (var property in properties)
        {
            var propertyName = GetJsonPropertyName(property);
            if (!schema.Properties.TryGetValue(propertyName, out var propertySchema))
                continue;

            // Apply OpenApiDocumentation attribute
            var docAttribute = property.GetCustomAttribute<OpenApiDocumentationAttribute>();
            if (docAttribute != null)
            {
                if (!string.IsNullOrEmpty(docAttribute.Description))
                    propertySchema.Description = docAttribute.Description;
                
                if (!string.IsNullOrEmpty(docAttribute.Example))
                    propertySchema.Example = new Microsoft.OpenApi.Any.OpenApiString(docAttribute.Example);
                
                if (!string.IsNullOrEmpty(docAttribute.Format))
                    propertySchema.Format = docAttribute.Format;
                
                if (!string.IsNullOrEmpty(docAttribute.Pattern))
                    propertySchema.Pattern = docAttribute.Pattern;
                
                if (docAttribute.Minimum.HasValue)
                    propertySchema.Minimum = (decimal)docAttribute.Minimum.Value;
                
                if (docAttribute.Maximum.HasValue)
                    propertySchema.Maximum = (decimal)docAttribute.Maximum.Value;
                
                if (docAttribute.MinLength.HasValue)
                    propertySchema.MinLength = docAttribute.MinLength.Value;
                
                if (docAttribute.MaxLength.HasValue)
                    propertySchema.MaxLength = docAttribute.MaxLength.Value;
                
                if (docAttribute.AllowedValues != null && docAttribute.AllowedValues.Length > 0)
                {
                    propertySchema.Enum = docAttribute.AllowedValues
                        .Select(v => new Microsoft.OpenApi.Any.OpenApiString(v))
                        .Cast<Microsoft.OpenApi.Any.IOpenApiAny>()
                        .ToList();
                }

                propertySchema.Deprecated = docAttribute.IsDeprecated;
            }

            // Apply specific documentation for common payment properties
            ApplyCommonPropertyDocumentation(propertyName, propertySchema);
        }
    }

    private string GetJsonPropertyName(PropertyInfo property)
    {
        var jsonPropertyAttribute = property.GetCustomAttribute<System.Text.Json.Serialization.JsonPropertyNameAttribute>();
        return jsonPropertyAttribute?.Name ?? property.Name.ToLowerInvariant();
    }

    private void ApplyCommonPropertyDocumentation(string propertyName, OpenApiSchema propertySchema)
    {
        switch (propertyName.ToLowerInvariant())
        {
            case "teamslug":
                propertySchema.Description = "Team identifier issued to merchant for authentication and routing";
                propertySchema.Example = new Microsoft.OpenApi.Any.OpenApiString("demo-merchant");
                propertySchema.Pattern = "^[a-zA-Z0-9_-]+$";
                break;

            case "token":
                propertySchema.Description = "Request signature for security validation generated using merchant's secret key";
                propertySchema.Example = new Microsoft.OpenApi.Any.OpenApiString("abc123def456...");
                break;

            case "amount":
                propertySchema.Description = "Payment amount in kopecks (e.g., 312 for 3.12 RUB)";
                propertySchema.Example = new Microsoft.OpenApi.Any.OpenApiInteger(15000);
                propertySchema.Minimum = 1000;
                propertySchema.Maximum = 50000000;
                break;

            case "orderid":
                propertySchema.Description = "Unique order identifier in merchant system for idempotency";
                propertySchema.Example = new Microsoft.OpenApi.Any.OpenApiString("ORDER-2023-001");
                propertySchema.Pattern = "^[a-zA-Z0-9_-]+$";
                break;

            case "paymentid":
                propertySchema.Description = "Payment identifier in the payment gateway system";
                propertySchema.Example = new Microsoft.OpenApi.Any.OpenApiString("PAY_123456789");
                break;

            case "currency":
                propertySchema.Description = "Currency code in ISO 4217 format";
                propertySchema.Example = new Microsoft.OpenApi.Any.OpenApiString("RUB");
                propertySchema.Enum = new List<Microsoft.OpenApi.Any.IOpenApiAny>
                {
                    new Microsoft.OpenApi.Any.OpenApiString("KZT"),
                    new Microsoft.OpenApi.Any.OpenApiString("USD"),
                    new Microsoft.OpenApi.Any.OpenApiString("EUR"),
                    new Microsoft.OpenApi.Any.OpenApiString("RUB"),
                    new Microsoft.OpenApi.Any.OpenApiString("BYN"),
                };
                break;

            case "status":
                propertySchema.Description = "Current payment status";
                propertySchema.Example = new Microsoft.OpenApi.Any.OpenApiString("NEW");
                break;

            case "description":
                propertySchema.Description = "Order description displayed on payment form";
                propertySchema.Example = new Microsoft.OpenApi.Any.OpenApiString("Payment for order #12345");
                break;

            case "email":
                propertySchema.Description = "Customer email address";
                propertySchema.Example = new Microsoft.OpenApi.Any.OpenApiString("customer@example.com");
                propertySchema.Format = "email";
                break;

            case "phone":
                propertySchema.Description = "Customer phone number (7-20 digits with optional leading +)";
                propertySchema.Example = new Microsoft.OpenApi.Any.OpenApiString("+79123456789");
                propertySchema.Pattern = @"^\+?[1-9]\d{6,19}$";
                break;

            case "language":
                propertySchema.Description = "Language for payment form and messages";
                propertySchema.Example = new Microsoft.OpenApi.Any.OpenApiString("ru");
                propertySchema.Enum = new List<Microsoft.OpenApi.Any.IOpenApiAny>
                {
                    new Microsoft.OpenApi.Any.OpenApiString("ru"),
                    new Microsoft.OpenApi.Any.OpenApiString("en")
                };
                break;

            case "paymenturl":
                propertySchema.Description = "URL for redirecting customer to payment form";
                propertySchema.Example = new Microsoft.OpenApi.Any.OpenApiString("https://payment.example.com/pay/PAY_123456789");
                propertySchema.Format = "uri";
                break;

            case "success":
                propertySchema.Description = "Indicates if the operation was successful";
                propertySchema.Example = new Microsoft.OpenApi.Any.OpenApiBoolean(true);
                break;

            case "errorcode":
                propertySchema.Description = "Error code if operation failed";
                propertySchema.Example = new Microsoft.OpenApi.Any.OpenApiString("INVALID_AMOUNT");
                break;

            case "message":
                propertySchema.Description = "Human-readable message describing the result";
                propertySchema.Example = new Microsoft.OpenApi.Any.OpenApiString("Payment initialized successfully");
                break;
        }
    }

    private Microsoft.OpenApi.Any.IOpenApiAny CreateExampleForRequestDto(Type dtoType)
    {
        var exampleJson = dtoType.Name switch
        {
            "PaymentInitRequestDto" => """
            {
              "teamSlug": "demo-merchant",
              "token": "abc123def456ghi789",
              "amount": 15000,
              "orderId": "ORDER-2023-001",
              "currency": "RUB",
              "description": "Payment for order #12345",
              "email": "customer@example.com",
              "phone": "+79123456789",
              "language": "ru",
              "successURL": "https://merchant.com/success",
              "failURL": "https://merchant.com/fail",
              "notificationURL": "https://merchant.com/webhook",
              "paymentExpiry": 30
            }
            """,
            
            "PaymentConfirmRequestDto" => """
            {
              "teamSlug": "demo-merchant",
              "token": "abc123def456ghi789",
              "paymentId": "PAY_123456789",
              "amount": 15000,
              "description": "Confirm payment for order #12345"
            }
            """,
            
            "PaymentCancelRequestDto" => """
            {
              "teamSlug": "demo-merchant",
              "token": "abc123def456ghi789",
              "paymentId": "PAY_123456789",
              "reason": "Customer requested cancellation",
              "amount": 15000
            }
            """,
            
            "PaymentCheckRequestDto" => """
            {
              "teamSlug": "demo-merchant",
              "token": "abc123def456ghi789",
              "paymentId": "PAY_123456789",
              "includeTransactions": true,
              "includeCardDetails": true,
              "language": "ru"
            }
            """,
            
            _ => "{}"
        };

        return new Microsoft.OpenApi.Any.OpenApiString(exampleJson);
    }
}

/// <summary>
/// OpenAPI operation filter for payment endpoints
/// </summary>
public class PaymentOperationFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        // Add common response headers
        if (operation.Responses != null)
        {
            foreach (var response in operation.Responses.Values)
            {
                response.Headers ??= new Dictionary<string, OpenApiHeader>();
                
                if (!response.Headers.ContainsKey("X-Correlation-ID"))
                {
                    response.Headers["X-Correlation-ID"] = new OpenApiHeader
                    {
                        Description = "Correlation ID for request tracking",
                        Schema = new OpenApiSchema { Type = "string" }
                    };
                }

                if (!response.Headers.ContainsKey("X-RateLimit-Remaining"))
                {
                    response.Headers["X-RateLimit-Remaining"] = new OpenApiHeader
                    {
                        Description = "Number of requests remaining in the current rate limit window",
                        Schema = new OpenApiSchema { Type = "integer" }
                    };
                }
            }
        }

        // Add payment-specific tags and descriptions
        var actionName = context.ApiDescription.ActionDescriptor.DisplayName ?? "";
        
        if (actionName.Contains("Init"))
        {
            operation.Tags = new List<OpenApiTag> { new() { Name = "Payment Initialization" } };
            operation.Summary = "Initialize Payment";
            operation.Description = "Creates a new payment session and returns a payment URL for customer redirection.";
        }
        else if (actionName.Contains("Confirm"))
        {
            operation.Tags = new List<OpenApiTag> { new() { Name = "Payment Confirmation" } };
            operation.Summary = "Confirm Payment";
            operation.Description = "Confirms (captures) an authorized payment in a two-stage payment process.";
        }
        else if (actionName.Contains("Cancel"))
        {
            operation.Tags = new List<OpenApiTag> { new() { Name = "Payment Cancellation" } };
            operation.Summary = "Cancel Payment";
            operation.Description = "Cancels a payment. Operation type depends on current payment status.";
        }
        else if (actionName.Contains("Check"))
        {
            operation.Tags = new List<OpenApiTag> { new() { Name = "Payment Status" } };
            operation.Summary = "Check Payment Status";
            operation.Description = "Retrieves current payment status and transaction history.";
        }

        // Add security requirements
        operation.Security = new List<OpenApiSecurityRequirement>
        {
            new()
            {
                {
                    new OpenApiSecurityScheme
                    {
                        Reference = new OpenApiReference
                        {
                            Type = ReferenceType.SecurityScheme,
                            Id = "TokenAuthentication"
                        }
                    },
                    new string[] { }
                }
            }
        };
    }
}