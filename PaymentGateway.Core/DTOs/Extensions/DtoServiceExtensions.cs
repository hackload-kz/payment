using AutoMapper;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using PaymentGateway.Core.DTOs.Configuration;
using PaymentGateway.Core.DTOs.Documentation;
using PaymentGateway.Core.DTOs.Mapping;
using PaymentGateway.Core.DTOs.PaymentInit;
using PaymentGateway.Core.DTOs.Validation;
using System.Text.Json;

namespace PaymentGateway.Core.DTOs.Extensions;

/// <summary>
/// Extension methods for registering DTO-related services
/// </summary>
public static class DtoServiceExtensions
{
    /// <summary>
    /// Register all DTO-related services
    /// </summary>
    public static IServiceCollection AddDtoServices(this IServiceCollection services)
    {
        // Register AutoMapper
        services.AddAutoMapper(config =>
        {
            config.AddProfile<PaymentMappingProfile>();
            config.AddProfile<ValidationMappingProfile>();
        });

        // Register FluentValidation validators
        services.AddValidatorsFromAssemblyContaining<PaymentInitRequestValidator>();

        // Register validation pipeline
        services.AddScoped<IDtoValidationPipeline, DtoValidationPipeline>();

        // Configure JSON serialization
        services.Configure<JsonSerializerOptions>(options =>
        {
            var defaultOptions = JsonSerializationConfiguration.DefaultOptions;
            options.PropertyNamingPolicy = defaultOptions.PropertyNamingPolicy;
            options.WriteIndented = defaultOptions.WriteIndented;
            options.DefaultIgnoreCondition = defaultOptions.DefaultIgnoreCondition;
            options.PropertyNameCaseInsensitive = defaultOptions.PropertyNameCaseInsensitive;
            options.NumberHandling = defaultOptions.NumberHandling;
            
            foreach (var converter in defaultOptions.Converters)
            {
                options.Converters.Add(converter);
            }
        });

        return services;
    }

    /// <summary>
    /// Register OpenAPI documentation services
    /// </summary>
    public static IServiceCollection AddDtoDocumentation(this IServiceCollection services)
    {
        services.AddSwaggerGen(options =>
        {
            // Add schema filters
            options.SchemaFilter<PaymentDtoSchemaFilter>();
            
            // Add operation filters
            options.OperationFilter<PaymentOperationFilter>();

            // Configure security definition
            options.AddSecurityDefinition("TokenAuthentication", new Microsoft.OpenApi.Models.OpenApiSecurityScheme
            {
                Type = Microsoft.OpenApi.Models.SecuritySchemeType.ApiKey,
                In = Microsoft.OpenApi.Models.ParameterLocation.Header,
                Name = "Authorization",
                Description = "Token-based authentication using SHA-256 signature"
            });

            // Configure API info
            options.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo
            {
                Title = "Payment Gateway API",
                Version = "v1.0",
                Description = "Payment gateway API for processing payments, confirmations, cancellations, and status checks",
                Contact = new Microsoft.OpenApi.Models.OpenApiContact
                {
                    Name = "Payment Gateway Support",
                    Email = "support@paymentgateway.com"
                },
                License = new Microsoft.OpenApi.Models.OpenApiLicense
                {
                    Name = "MIT License",
                    Url = new Uri("https://opensource.org/licenses/MIT")
                }
            });

            // Include XML documentation if available
            var xmlFile = $"{System.Reflection.Assembly.GetExecutingAssembly().GetName().Name}.xml";
            var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
            if (File.Exists(xmlPath))
            {
                options.IncludeXmlComments(xmlPath);
            }

            // Configure enum handling
            options.UseInlineDefinitionsForEnums();
            
            // Configure schema generation
            options.CustomSchemaIds(type => type.FullName?.Replace('+', '.'));
        });

        return services;
    }

    /// <summary>
    /// Register DTO validation services with custom configuration
    /// </summary>
    public static IServiceCollection AddDtoValidation(this IServiceCollection services, Action<DtoValidationOptions>? configure = null)
    {
        var options = new DtoValidationOptions();
        configure?.Invoke(options);

        services.AddSingleton(options);

        // Register validators based on options
        if (options.EnableFluentValidation)
        {
            services.AddValidatorsFromAssemblyContaining<PaymentInitRequestValidator>();
        }

        if (options.EnableDataAnnotations)
        {
            // DataAnnotations validation is enabled by default in the pipeline
        }

        if (options.EnableCustomValidation)
        {
            // Custom validation interfaces are supported by default in the pipeline
        }

        return services;
    }
}

/// <summary>
/// Configuration options for DTO validation
/// </summary>
public class DtoValidationOptions
{
    /// <summary>
    /// Enable FluentValidation validators
    /// </summary>
    public bool EnableFluentValidation { get; set; } = true;

    /// <summary>
    /// Enable DataAnnotations validation
    /// </summary>
    public bool EnableDataAnnotations { get; set; } = true;

    /// <summary>
    /// Enable custom validation interfaces
    /// </summary>
    public bool EnableCustomValidation { get; set; } = true;

    /// <summary>
    /// Stop validation on first error
    /// </summary>
    public bool StopOnFirstError { get; set; } = false;

    /// <summary>
    /// Include stack traces in validation errors (development only)
    /// </summary>
    public bool IncludeStackTrace { get; set; } = false;

    /// <summary>
    /// Maximum validation errors to return
    /// </summary>
    public int MaxValidationErrors { get; set; } = 50;
}

/// <summary>
/// Extension methods for DTO mapping
/// </summary>
public static class DtoMappingExtensions
{
    /// <summary>
    /// Map DTO to domain model with validation
    /// </summary>
    public static async Task<TDomain> MapToDomainAsync<TDto, TDomain>(
        this TDto dto,
        IMapper mapper,
        IDtoValidationPipeline validationPipeline,
        CancellationToken cancellationToken = default)
        where TDto : class
        where TDomain : class
    {
        // Validate DTO first
        var validationResult = await validationPipeline.ValidateAsync(dto, cancellationToken);
        if (!validationResult.IsValid)
        {
            throw new ValidationException("DTO validation failed", validationResult.Errors);
        }

        // Map to domain model
        return mapper.Map<TDomain>(dto);
    }

    /// <summary>
    /// Map domain model to DTO
    /// </summary>
    public static TDto MapToDto<TDomain, TDto>(this TDomain domain, IMapper mapper)
        where TDomain : class
        where TDto : class
    {
        return mapper.Map<TDto>(domain);
    }

    /// <summary>
    /// Map collection of domain models to DTOs
    /// </summary>
    public static List<TDto> MapToDtos<TDomain, TDto>(this IEnumerable<TDomain> domains, IMapper mapper)
        where TDomain : class
        where TDto : class
    {
        return mapper.Map<List<TDto>>(domains);
    }
}

/// <summary>
/// Custom validation exception for DTO validation failures
/// </summary>
public class ValidationException : Exception
{
    public ValidationException(string message, IEnumerable<ValidationError> errors)
        : base(message)
    {
        Errors = errors.ToList();
    }

    public List<ValidationError> Errors { get; }
}