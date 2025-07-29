// SPDX-License-Identifier: MIT
// Copyright (c) 2025 HackLoad Payment Gateway

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApiExplorer;

namespace PaymentGateway.API.Configuration;

/// <summary>
/// API versioning configuration for payment gateway endpoints
/// 
/// Provides comprehensive API versioning support with:
/// - Header-based versioning (X-API-Version)
/// - URL path versioning (/v1/, /v2/)
/// - Query parameter versioning (?version=1.0)
/// - Default version handling
/// - Version deprecation support
/// - Swagger/OpenAPI integration for versioned documentation
/// </summary>
public static class ApiVersioningConfiguration
{
    /// <summary>
    /// Configure API versioning services
    /// </summary>
    public static IServiceCollection AddPaymentGatewayApiVersioning(this IServiceCollection services)
    {
        services.AddApiVersioning(options =>
        {
            // Default version when none is specified
            options.DefaultApiVersion = new ApiVersion(1, 0);
            options.AssumeDefaultVersionWhenUnspecified = true;

            // Support multiple versioning strategies
            options.ApiVersionReader = ApiVersionReader.Combine(
                new HeaderApiVersionReader("X-API-Version"),
                new UrlSegmentApiVersionReader(),
                new QueryStringApiVersionReader("version"),
                new MediaTypeApiVersionReader("version")
            );

            // Version format
            options.ApiVersionSelector = new CurrentImplementationApiVersionSelector(options);
            
            // Error handling for unsupported versions
            options.ErrorResponses = new PaymentGatewayApiVersionErrorProvider();
        });

        // Add API explorer for versioned documentation
        services.AddVersionedApiExplorer(options =>
        {
            // Group format for API explorer
            options.GroupNameFormat = "'v'VVV";
            
            // Automatically substitute controller names in routes
            options.SubstituteApiVersionInUrl = true;
            
            // Include all API versions in documentation
            options.AssumeDefaultVersionWhenUnspecified = true;
        });

        return services;
    }

    /// <summary>
    /// Configure controllers with versioning attributes
    /// </summary>
    public static IMvcBuilder AddVersionedControllers(this IMvcBuilder builder)
    {
        return builder.ConfigureApplicationPartManager(manager =>
        {
            // Configure versioned controller discovery
            manager.FeatureProviders.Add(new VersionedControllerFeatureProvider());
        });
    }
}

/// <summary>
/// Custom API version error provider for payment gateway
/// </summary>
public class PaymentGatewayApiVersionErrorProvider : IErrorResponseProvider
{
    public IActionResult CreateResponse(ErrorResponseContext context)
    {
        var correlationId = context.Request.HttpContext.TraceIdentifier;
        
        var errorResponse = context.ErrorCode switch
        {
            "UnsupportedApiVersion" => new
            {
                Success = false,
                ErrorCode = "UNSUPPORTED_API_VERSION",
                Message = "The specified API version is not supported",
                Details = $"Supported versions: {string.Join(", ", GetSupportedVersions())}",
                RequestedVersion = context.RequestedVersion,
                SupportedVersions = GetSupportedVersions(),
                CorrelationId = correlationId,
                Timestamp = DateTime.UtcNow
            },
            "InvalidApiVersion" => new
            {
                Success = false,
                ErrorCode = "INVALID_API_VERSION",
                Message = "The specified API version format is invalid",
                Details = "API version must be in format 'major.minor' (e.g., '1.0', '2.1')",
                RequestedVersion = context.RequestedVersion,
                SupportedVersions = GetSupportedVersions(),
                CorrelationId = correlationId,
                Timestamp = DateTime.UtcNow
            },
            "AmbiguousApiVersion" => new
            {
                Success = false,
                ErrorCode = "AMBIGUOUS_API_VERSION",
                Message = "Multiple API versions were specified",
                Details = "Please specify only one API version using X-API-Version header, URL path, or query parameter",
                RequestedVersion = context.RequestedVersion,
                CorrelationId = correlationId,
                Timestamp = DateTime.UtcNow
            },
            _ => new
            {
                Success = false,
                ErrorCode = "API_VERSION_ERROR",
                Message = context.Message ?? "An API versioning error occurred",
                Details = "Please check your API version specification",
                CorrelationId = correlationId,
                Timestamp = DateTime.UtcNow
            }
        };

        return new ObjectResult(errorResponse)
        {
            StatusCode = 400
        };
    }

    private static string[] GetSupportedVersions()
    {
        return new[] { "1.0", "1.1", "2.0" };
    }
}

/// <summary>
/// Feature provider for versioned controllers
/// </summary>
public class VersionedControllerFeatureProvider : ControllerFeatureProvider
{
    protected override bool IsController(TypeInfo typeInfo)
    {
        // Include controllers that are decorated with versioning attributes
        if (!base.IsController(typeInfo))
            return false;

        // Check for API version attributes
        return typeInfo.GetCustomAttributes<ApiVersionAttribute>().Any() ||
               typeInfo.GetCustomAttributes<MapToApiVersionAttribute>().Any() ||
               typeInfo.GetCustomAttribute<ApiControllerAttribute>() != null;
    }
}

/// <summary>
/// Base versioned controller with common versioning setup
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[ApiVersion("1.1")]
[Route("api/v{version:apiVersion}/[controller]")]
[Produces("application/json")]
public abstract class VersionedApiControllerBase : ControllerBase
{
    /// <summary>
    /// Get the current API version from the request
    /// </summary>
    protected ApiVersion CurrentApiVersion => HttpContext.GetRequestedApiVersion() ?? new ApiVersion(1, 0);

    /// <summary>
    /// Check if the current request is for a specific API version
    /// </summary>
    protected bool IsApiVersion(int major, int minor)
    {
        var currentVersion = CurrentApiVersion;
        return currentVersion.MajorVersion == major && currentVersion.MinorVersion == minor;
    }

    /// <summary>
    /// Get version-specific response based on API version
    /// </summary>
    protected T GetVersionedResponse<T>(Func<T> v1Response, Func<T>? v2Response = null)
    {
        return CurrentApiVersion.MajorVersion switch
        {
            1 => v1Response(),
            2 => v2Response?.Invoke() ?? v1Response(),
            _ => v1Response()
        };
    }
}

/// <summary>
/// API version information for OpenAPI documentation
/// </summary>
public class ApiVersionInfo
{
    public string Version { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool IsDeprecated { get; set; }
    public DateTime? DeprecationDate { get; set; }
    public string? DeprecationMessage { get; set; }
}

/// <summary>
/// Extension methods for API versioning
/// </summary>
public static class ApiVersioningExtensions
{
    /// <summary>
    /// Get available API versions for the application
    /// </summary>
    public static ApiVersionInfo[] GetApiVersions()
    {
        return new[]
        {
            new ApiVersionInfo
            {
                Version = "1.0",
                Title = "Payment Gateway API v1.0",
                Description = "Initial version of the Payment Gateway API with core payment processing functionality",
                IsDeprecated = false
            },
            new ApiVersionInfo
            {
                Version = "1.1",
                Title = "Payment Gateway API v1.1",
                Description = "Enhanced version with improved validation and additional payment methods",
                IsDeprecated = false
            },
            new ApiVersionInfo
            {
                Version = "2.0",
                Title = "Payment Gateway API v2.0",
                Description = "Major revision with new architecture and advanced features",
                IsDeprecated = false
            }
        };
    }

    /// <summary>
    /// Configure Swagger for API versioning
    /// </summary>
    public static IServiceCollection AddVersionedSwagger(this IServiceCollection services)
    {
        services.AddSwaggerGen(options =>
        {
            var apiVersions = GetApiVersions();
            
            foreach (var version in apiVersions)
            {
                options.SwaggerDoc(version.Version, new Microsoft.OpenApi.Models.OpenApiInfo
                {
                    Title = version.Title,
                    Version = version.Version,
                    Description = version.Description + (version.IsDeprecated ? " (DEPRECATED)" : "")
                });
            }

            // Include version in operation IDs
            options.CustomOperationIds(apiDescription =>
            {
                var version = apiDescription.GetApiVersion()?.ToString() ?? "1.0";
                return $"{apiDescription.ActionDescriptor.RouteValues["controller"]}_{apiDescription.HttpMethod}_{version.Replace(".", "_")}";
            });
        });

        return services;
    }
}