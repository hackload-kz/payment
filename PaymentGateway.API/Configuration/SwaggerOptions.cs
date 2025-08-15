namespace PaymentGateway.API.Configuration;

/// <summary>
/// Configuration options for Swagger/OpenAPI documentation
/// </summary>
public class SwaggerOptions
{
    public const string SectionName = "Swagger";

    /// <summary>
    /// Enable or disable Swagger UI
    /// </summary>
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// Route prefix for Swagger UI (e.g., "swagger" or "api-docs")
    /// </summary>
    public string RoutePrefix { get; set; } = "swagger";

    /// <summary>
    /// API documentation title
    /// </summary>
    public string Title { get; set; } = "Payment Gateway API";

    /// <summary>
    /// API documentation description
    /// </summary>
    public string Description { get; set; } = "Payment Gateway API Documentation";

    /// <summary>
    /// Require HTTPS for Swagger UI access
    /// </summary>
    public bool RequireHttps { get; set; } = true;

    /// <summary>
    /// Enable "Try it out" functionality in Swagger UI
    /// </summary>
    public bool EnableTryItOut { get; set; } = false;

    /// <summary>
    /// Custom endpoint URL for Swagger OpenAPI JSON (overrides default /openapi/v1.json)
    /// </summary>
    public string? EndpointUrl { get; set; }
}