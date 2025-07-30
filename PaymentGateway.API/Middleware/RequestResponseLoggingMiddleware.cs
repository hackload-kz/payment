// SPDX-License-Identifier: MIT
// Copyright (c) 2025 HackLoad Payment Gateway

using Microsoft.Extensions.Primitives;
using Microsoft.Extensions.Options;
using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace PaymentGateway.API.Middleware;

/// <summary>
/// Middleware for comprehensive request/response logging with structured data
/// 
/// This middleware captures detailed request/response information for audit trails,
/// debugging, and monitoring purposes. It handles sensitive data masking and
/// provides configurable logging levels based on endpoints.
/// 
/// Features:
/// - Request/response body logging with size limits
/// - Sensitive data masking (payment cards, tokens)
/// - Performance timing with correlation IDs
/// - Configurable logging levels per endpoint
/// - Request/response headers logging
/// - Error context capture
/// - Structured logging with JSON output
/// </summary>
public class RequestResponseLoggingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<RequestResponseLoggingMiddleware> _logger;
    private readonly RequestResponseLoggingOptions _options;

    // Sensitive fields to mask in logs
    private readonly HashSet<string> _sensitiveFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "token", "password", "cardNumber", "cvv", "pin", "cardData", 
        "authorization", "x-api-key", "clientSecret", "privateKey",
        "cardMask", "encryptedData", "signature", "hash"
    };

    // Headers to mask in logs
    private readonly HashSet<string> _sensitiveHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Authorization", "Cookie", "Set-Cookie", "X-API-Key", 
        "X-Auth-Token", "Authentication", "Proxy-Authorization"
    };

    public RequestResponseLoggingMiddleware(
        RequestDelegate next,
        ILogger<RequestResponseLoggingMiddleware> logger,
        IOptions<RequestResponseLoggingOptions> options)
    {
        _next = next;
        _logger = logger;
        _options = options.Value;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var stopwatch = Stopwatch.StartNew();
        var correlationId = context.TraceIdentifier;
        var startTime = DateTime.UtcNow;

        try
        {
            // Check if logging is enabled for this request
            if (!ShouldLog(context.Request.Path))
            {
                await _next(context);
                return;
            }

            // Log request
            var requestInfo = await CaptureRequestAsync(context.Request, correlationId);
            LogRequest(requestInfo, correlationId);

            // Capture response
            var originalResponseBodyStream = context.Response.Body;
            using var responseBodyStream = new MemoryStream();
            context.Response.Body = responseBodyStream;

            try
            {
                await _next(context);
            }
            finally
            {
                // Log response
                var responseInfo = await CaptureResponseAsync(context.Response, responseBodyStream, correlationId, stopwatch.Elapsed);
                LogResponse(responseInfo, correlationId, context.Response.StatusCode);

                // Copy response back to original stream
                responseBodyStream.Seek(0, SeekOrigin.Begin);
                await responseBodyStream.CopyToAsync(originalResponseBodyStream);
                context.Response.Body = originalResponseBodyStream;
            }
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex, "Error in request/response logging middleware. CorrelationId: {CorrelationId}, Duration: {Duration}ms",
                correlationId, stopwatch.ElapsedMilliseconds);
            throw;
        }
    }

    private bool ShouldLog(PathString path)
    {
        // Skip logging for health checks and metrics
        if (path.StartsWithSegments("/health") || 
            path.StartsWithSegments("/metrics") ||
            path.StartsWithSegments("/favicon.ico"))
        {
            return false;
        }

        // Always log API endpoints
        if (path.StartsWithSegments("/api"))
        {
            return true;
        }

        // Log based on configuration
        return _options.LogAllRequests;
    }

    private async Task<RequestLogInfo> CaptureRequestAsync(HttpRequest request, string correlationId)
    {
        var requestInfo = new RequestLogInfo
        {
            CorrelationId = correlationId,
            Method = request.Method,
            Path = request.Path.Value ?? "",
            QueryString = request.QueryString.Value ?? "",
            Scheme = request.Scheme,
            Host = request.Host.Value ?? "",
            ContentType = request.ContentType ?? "",
            ContentLength = request.ContentLength,
            Headers = CaptureHeaders(request.Headers),
            ClientIp = GetClientIpAddress(request),
            UserAgent = request.Headers.UserAgent.ToString(),
            Timestamp = DateTime.UtcNow
        };

        // Capture request body if enabled and present
        if (_options.LogRequestBody && request.ContentLength > 0 && request.ContentLength <= _options.MaxBodySize)
        {
            requestInfo.Body = await ReadRequestBodyAsync(request);
        }

        return requestInfo;
    }

    private async Task<ResponseLogInfo> CaptureResponseAsync(HttpResponse response, MemoryStream responseBodyStream, string correlationId, TimeSpan duration)
    {
        var responseInfo = new ResponseLogInfo
        {
            CorrelationId = correlationId,
            StatusCode = response.StatusCode,
            ContentType = response.ContentType ?? "",
            ContentLength = responseBodyStream.Length,
            Headers = CaptureHeaders(response.Headers),
            Duration = duration,
            Timestamp = DateTime.UtcNow
        };

        // Capture response body if enabled and within size limits
        if (_options.LogResponseBody && responseBodyStream.Length > 0 && responseBodyStream.Length <= _options.MaxBodySize)
        {
            responseBodyStream.Seek(0, SeekOrigin.Begin);
            using var reader = new StreamReader(responseBodyStream, Encoding.UTF8, leaveOpen: true);
            var body = await reader.ReadToEndAsync();
            responseInfo.Body = MaskSensitiveData(body);
            responseBodyStream.Seek(0, SeekOrigin.Begin);
        }

        return responseInfo;
    }

    private async Task<string?> ReadRequestBodyAsync(HttpRequest request)
    {
        if (request.Body == null || !request.Body.CanRead)
            return null;

        request.EnableBuffering();
        var buffer = new byte[_options.MaxBodySize];
        var bytesRead = await request.Body.ReadAsync(buffer, 0, buffer.Length);
        request.Body.Position = 0;

        if (bytesRead == 0)
            return null;

        var body = Encoding.UTF8.GetString(buffer, 0, bytesRead);
        return MaskSensitiveData(body);
    }

    private Dictionary<string, string> CaptureHeaders(IHeaderDictionary headers)
    {
        var capturedHeaders = new Dictionary<string, string>();

        foreach (var header in headers)
        {
            if (_options.LogHeaders)
            {
                var value = _sensitiveHeaders.Contains(header.Key) ? "[MASKED]" : header.Value.ToString();
                capturedHeaders[header.Key] = value;
            }
            else if (_options.LogImportantHeaders && IsImportantHeader(header.Key))
            {
                var value = _sensitiveHeaders.Contains(header.Key) ? "[MASKED]" : header.Value.ToString();
                capturedHeaders[header.Key] = value;
            }
        }

        return capturedHeaders;
    }

    private bool IsImportantHeader(string headerName)
    {
        var importantHeaders = new[]
        {
            "Content-Type", "Content-Length", "User-Agent", "Accept",
            "X-Forwarded-For", "X-Real-IP", "X-Request-ID", "X-Correlation-ID"
        };

        return importantHeaders.Contains(headerName, StringComparer.OrdinalIgnoreCase);
    }

    private string MaskSensitiveData(string content)
    {
        if (string.IsNullOrEmpty(content))
            return content;

        try
        {
            // Try to parse as JSON and mask sensitive fields
            var jsonDoc = JsonDocument.Parse(content);
            var maskedJson = MaskJsonObject(jsonDoc.RootElement);
            return JsonSerializer.Serialize(maskedJson, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = false
            });
        }
        catch
        {
            // If not JSON, apply simple text masking
            return MaskSensitiveTextFields(content);
        }
    }

    private object MaskJsonObject(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                var obj = new Dictionary<string, object>();
                foreach (var property in element.EnumerateObject())
                {
                    if (_sensitiveFields.Contains(property.Name))
                    {
                        obj[property.Name] = "[MASKED]";
                    }
                    else
                    {
                        obj[property.Name] = MaskJsonObject(property.Value);
                    }
                }
                return obj;

            case JsonValueKind.Array:
                var array = new List<object>();
                foreach (var item in element.EnumerateArray())
                {
                    array.Add(MaskJsonObject(item));
                }
                return array;

            case JsonValueKind.String:
                return element.GetString() ?? "";
            case JsonValueKind.Number:
                return element.TryGetInt64(out var longValue) ? longValue : element.GetDouble();
            case JsonValueKind.True:
                return true;
            case JsonValueKind.False:
                return false;
            case JsonValueKind.Null:
                return null!;
            default:
                return element.ToString();
        }
    }

    private string MaskSensitiveTextFields(string content)
    {
        // Simple text-based masking for non-JSON content
        foreach (var field in _sensitiveFields)
        {
            var pattern = $@"(""{field}""\s*:\s*""[^""]*"")";
            content = System.Text.RegularExpressions.Regex.Replace(content, pattern, 
                match => match.Value.Substring(0, match.Value.LastIndexOf('"')) + "[MASKED]\"",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        }

        return content;
    }

    private string GetClientIpAddress(HttpRequest request)
    {
        // Check for forwarded IP addresses
        if (request.Headers.TryGetValue("X-Forwarded-For", out var forwardedFor))
        {
            var firstIp = forwardedFor.ToString().Split(',')[0].Trim();
            if (!string.IsNullOrEmpty(firstIp))
                return firstIp;
        }

        if (request.Headers.TryGetValue("X-Real-IP", out var realIp))
        {
            return realIp.ToString();
        }

        return request.HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    }

    private void LogRequest(RequestLogInfo requestInfo, string correlationId)
    {
        _logger.LogInformation("HTTP Request - {Method} {Path} | CorrelationId: {CorrelationId} | IP: {ClientIp} | ContentType: {ContentType} | UserAgent: {UserAgent}",
            requestInfo.Method, requestInfo.Path, correlationId, requestInfo.ClientIp, requestInfo.ContentType, requestInfo.UserAgent);

        if (_options.EnableDetailedLogging)
        {
            _logger.LogDebug("Request Details - CorrelationId: {CorrelationId} | {@RequestInfo}", correlationId, requestInfo);
        }
    }

    private void LogResponse(ResponseLogInfo responseInfo, string correlationId, int statusCode)
    {
        var logLevel = statusCode >= 400 ? LogLevel.Warning : LogLevel.Information;

        _logger.Log(logLevel, "HTTP Response - {StatusCode} | CorrelationId: {CorrelationId} | Duration: {Duration}ms | ContentType: {ContentType}",
            statusCode, correlationId, responseInfo.Duration.TotalMilliseconds, responseInfo.ContentType);

        if (_options.EnableDetailedLogging)
        {
            _logger.LogDebug("Response Details - CorrelationId: {CorrelationId} | {@ResponseInfo}", correlationId, responseInfo);
        }
    }
}

/// <summary>
/// Configuration options for request/response logging middleware
/// </summary>
public class RequestResponseLoggingOptions
{
    public bool LogAllRequests { get; set; } = false;
    public bool LogRequestBody { get; set; } = true;
    public bool LogResponseBody { get; set; } = true;
    public bool LogHeaders { get; set; } = false;
    public bool LogImportantHeaders { get; set; } = true;
    public bool EnableDetailedLogging { get; set; } = false;
    public int MaxBodySize { get; set; } = 32 * 1024; // 32KB max body logging
}

/// <summary>
/// Request logging information
/// </summary>
public class RequestLogInfo
{
    public string CorrelationId { get; set; } = string.Empty;
    public string Method { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public string QueryString { get; set; } = string.Empty;
    public string Scheme { get; set; } = string.Empty;
    public string Host { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long? ContentLength { get; set; }
    public Dictionary<string, string> Headers { get; set; } = new();
    public string ClientIp { get; set; } = string.Empty;
    public string UserAgent { get; set; } = string.Empty;
    public string? Body { get; set; }
    public DateTime Timestamp { get; set; }
}

/// <summary>
/// Response logging information
/// </summary>
public class ResponseLogInfo
{
    public string CorrelationId { get; set; } = string.Empty;
    public int StatusCode { get; set; }
    public string ContentType { get; set; } = string.Empty;
    public long ContentLength { get; set; }
    public Dictionary<string, string> Headers { get; set; } = new();
    public string? Body { get; set; }
    public TimeSpan Duration { get; set; }
    public DateTime Timestamp { get; set; }
}

/// <summary>
/// Extension methods for registering the request/response logging middleware
/// </summary>
public static class RequestResponseLoggingMiddlewareExtensions
{
    public static IApplicationBuilder UseRequestResponseLogging(this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<RequestResponseLoggingMiddleware>();
    }

    public static IServiceCollection AddRequestResponseLogging(this IServiceCollection services, Action<RequestResponseLoggingOptions>? configure = null)
    {
        if (configure != null)
        {
            services.Configure(configure);
        }
        else
        {
            services.Configure<RequestResponseLoggingOptions>(options => { });
        }

        return services;
    }
}