using System.Text.Json;
using Payment.Gateway.Services;

namespace Payment.Gateway.Middleware;

public class AuthenticationMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<AuthenticationMiddleware> _logger;
    private readonly IServiceProvider _serviceProvider;

    private static readonly string[] PublicEndpoints = { "/health", "/metrics", "/swagger" };

    public AuthenticationMiddleware(RequestDelegate next, ILogger<AuthenticationMiddleware> logger, IServiceProvider serviceProvider)
    {
        _next = next;
        _logger = logger;
        _serviceProvider = serviceProvider;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Skip authentication for public endpoints
        if (IsPublicEndpoint(context.Request.Path))
        {
            await _next(context);
            return;
        }

        // Skip authentication for non-API endpoints (e.g., static files)
        if (!context.Request.Path.StartsWithSegments("/api"))
        {
            await _next(context);
            return;
        }

        // Skip authentication for GET requests (health checks, etc.)
        if (context.Request.Method == HttpMethods.Get)
        {
            await _next(context);
            return;
        }

        try
        {
            using var scope = _serviceProvider.CreateScope();
            var tokenService = scope.ServiceProvider.GetRequiredService<ITokenGenerationService>();
            var merchantService = scope.ServiceProvider.GetRequiredService<IMerchantService>();

            // Read request body
            context.Request.EnableBuffering();
            var requestBody = await ReadRequestBodyAsync(context.Request);
            context.Request.Body.Position = 0; // Reset for controller consumption

            if (string.IsNullOrEmpty(requestBody))
            {
                await WriteErrorResponse(context, "201", "Missing request body");
                return;
            }

            // Parse JSON request
            JsonDocument? jsonDoc = null;
            try
            {
                jsonDoc = JsonDocument.Parse(requestBody);
            }
            catch (JsonException)
            {
                await WriteErrorResponse(context, "210", "Invalid JSON format");
                return;
            }

            using (jsonDoc)
            {
                var root = jsonDoc.RootElement;

                // Extract TerminalKey and Token
                if (!root.TryGetProperty("TerminalKey", out var terminalKeyElement))
                {
                    await WriteErrorResponse(context, "201", "TerminalKey is required");
                    return;
                }

                if (!root.TryGetProperty("Token", out var tokenElement))
                {
                    await WriteErrorResponse(context, "201", "Token is required");
                    return;
                }

                var terminalKey = terminalKeyElement.GetString();
                var token = tokenElement.GetString();

                if (string.IsNullOrWhiteSpace(terminalKey))
                {
                    await WriteErrorResponse(context, "201", "TerminalKey cannot be empty");
                    return;
                }

                if (string.IsNullOrWhiteSpace(token))
                {
                    await WriteErrorResponse(context, "201", "Token cannot be empty");
                    return;
                }

                // Get merchant and validate credentials
                var merchant = await merchantService.GetMerchantAsync(terminalKey);
                if (merchant == null)
                {
                    _logger.LogWarning("Authentication failed: Terminal not found {TerminalKey}", terminalKey);
                    await WriteErrorResponse(context, "205", "Terminal not found");
                    return;
                }

                if (!merchant.IsActive)
                {
                    _logger.LogWarning("Authentication failed: Terminal blocked {TerminalKey}", terminalKey);
                    await WriteErrorResponse(context, "202", "Terminal blocked");
                    return;
                }

                // Extract parameters for token validation
                var parameters = ExtractParameters(root);

                // Validate token
                if (!tokenService.ValidateToken(parameters, token, merchant.Password))
                {
                    _logger.LogWarning("Authentication failed: Invalid token for {TerminalKey}", terminalKey);
                    await WriteErrorResponse(context, "204", "Invalid token");
                    return;
                }

                _logger.LogDebug("Authentication successful for {TerminalKey}", terminalKey);

                // Store merchant info in context for controllers
                context.Items["Merchant"] = merchant;
                context.Items["TerminalKey"] = terminalKey;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Authentication middleware error");
            await WriteErrorResponse(context, "99", "Internal server error");
            return;
        }

        await _next(context);
    }

    private static bool IsPublicEndpoint(PathString path)
    {
        return PublicEndpoints.Any(endpoint => path.StartsWithSegments(endpoint, StringComparison.OrdinalIgnoreCase));
    }

    private static async Task<string> ReadRequestBodyAsync(HttpRequest request)
    {
        using var reader = new StreamReader(request.Body);
        return await reader.ReadToEndAsync();
    }

    private static Dictionary<string, object> ExtractParameters(JsonElement root)
    {
        var parameters = new Dictionary<string, object>();

        foreach (var property in root.EnumerateObject())
        {
            // Skip Token parameter from validation (it's not part of the signed data)
            if (property.Name == "Token")
                continue;

            parameters[property.Name] = property.Value;
        }

        return parameters;
    }

    private static async Task WriteErrorResponse(HttpContext context, string errorCode, string message)
    {
        context.Response.StatusCode = 401;
        context.Response.ContentType = "application/json";

        var response = new
        {
            Success = false,
            ErrorCode = errorCode,
            Message = message
        };

        await context.Response.WriteAsync(JsonSerializer.Serialize(response));
    }
}