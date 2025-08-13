using Microsoft.Extensions.Logging;
using System.Text;
using System.Text.Json;
using System.Security.Cryptography;
using PaymentGateway.Core.Entities;
using PaymentGateway.Core.Repositories;

namespace PaymentGateway.Core.Services;

public interface INotificationWebhookService
{
    Task SendPaymentNotificationAsync(string paymentId, string status, string teamSlug, Dictionary<string, object>? additionalData = null);
    Task SendPaymentCompletedNotificationAsync(string paymentId, string teamSlug);
    Task SendPaymentFailedNotificationAsync(string paymentId, string reason, string teamSlug);
    Task SendNotificationAsync(object request, string teamSlug);
}

public class NotificationWebhookService : INotificationWebhookService
{
    private readonly ILogger<NotificationWebhookService> _logger;
    private readonly HttpClient _httpClient;
    private readonly ITeamRepository _teamRepository;

    public NotificationWebhookService(
        ILogger<NotificationWebhookService> logger,
        HttpClient httpClient,
        ITeamRepository teamRepository)
    {
        _logger = logger;
        _httpClient = httpClient;
        _teamRepository = teamRepository;
    }

    public async Task SendPaymentNotificationAsync(string paymentId, string status, string teamSlug, Dictionary<string, object>? additionalData = null)
    {
        try
        {
            var team = await _teamRepository.GetByTeamSlugAsync(teamSlug);
            if (team == null || !team.EnableWebhooks || string.IsNullOrEmpty(team.NotificationUrl))
            {
                _logger.LogDebug("Webhook disabled or not configured for team {TeamSlug}", teamSlug);
                return;
            }

            var payload = new
            {
                PaymentId = paymentId,
                Status = status,
                TeamSlug = teamSlug,
                Timestamp = DateTime.UtcNow,
                Data = additionalData ?? new Dictionary<string, object>()
            };

            await SendWebhookAsync(team, payload, "payment_status_change");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send payment notification webhook for payment {PaymentId}", paymentId);
        }
    }

    public async Task SendPaymentCompletedNotificationAsync(string paymentId, string teamSlug)
    {
        await SendPaymentNotificationAsync(paymentId, "completed", teamSlug, new Dictionary<string, object>
        {
            ["event_type"] = "payment_completed"
        });
    }

    public async Task SendPaymentFailedNotificationAsync(string paymentId, string reason, string teamSlug)
    {
        await SendPaymentNotificationAsync(paymentId, "failed", teamSlug, new Dictionary<string, object>
        {
            ["event_type"] = "payment_failed",
            ["failure_reason"] = reason
        });
    }

    public async Task SendNotificationAsync(object request, string teamSlug)
    {
        try
        {
            var team = await _teamRepository.GetByTeamSlugAsync(teamSlug);
            if (team == null || !team.EnableWebhooks || string.IsNullOrEmpty(team.NotificationUrl))
            {
                _logger.LogDebug("Webhook disabled or not configured for team {TeamSlug}", teamSlug);
                return;
            }

            var payload = new
            {
                TeamSlug = teamSlug,
                Timestamp = DateTime.UtcNow,
                Data = request
            };

            await SendWebhookAsync(team, payload, "generic_notification");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send generic webhook notification for team {TeamSlug}", teamSlug);
        }
    }

    private async Task SendWebhookAsync(Team team, object payload, string eventType)
    {
        var maxRetries = team.WebhookRetryAttempts;
        var timeoutSeconds = team.WebhookTimeoutSeconds;
        
        for (int attempt = 0; attempt <= maxRetries; attempt++)
        {
            try
            {
                var jsonPayload = JsonSerializer.Serialize(payload, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                });

                using var request = new HttpRequestMessage(HttpMethod.Post, team.NotificationUrl);
                request.Content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");
                
                // Add webhook signature if secret is configured
                if (!string.IsNullOrEmpty(team.WebhookSecret))
                {
                    var signature = GenerateWebhookSignature(jsonPayload, team.WebhookSecret);
                    request.Headers.Add("X-Webhook-Signature", signature);
                }
                
                // Add custom headers
                request.Headers.Add("X-Webhook-Event", eventType);
                request.Headers.Add("X-Webhook-Delivery", Guid.NewGuid().ToString());
                request.Headers.Add("User-Agent", "PaymentGateway-Webhook/1.0");

                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
                using var response = await _httpClient.SendAsync(request, cts.Token);

                if (response.IsSuccessStatusCode)
                {
                    _logger.LogInformation("Webhook delivered successfully to {Url} for team {TeamSlug} (attempt {Attempt})", 
                        team.NotificationUrl, team.TeamSlug, attempt + 1);
                    return;
                }
                else
                {
                    _logger.LogWarning("Webhook delivery failed with status {StatusCode} to {Url} for team {TeamSlug} (attempt {Attempt})", 
                        response.StatusCode, team.NotificationUrl, team.TeamSlug, attempt + 1);
                }
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("Webhook delivery timed out after {Timeout}s to {Url} for team {TeamSlug} (attempt {Attempt})", 
                    timeoutSeconds, team.NotificationUrl, team.TeamSlug, attempt + 1);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Webhook delivery failed to {Url} for team {TeamSlug} (attempt {Attempt})", 
                    team.NotificationUrl, team.TeamSlug, attempt + 1);
            }

            // Wait before retry (exponential backoff)
            if (attempt < maxRetries)
            {
                var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt));
                await Task.Delay(delay);
            }
        }

        _logger.LogError("Failed to deliver webhook to {Url} for team {TeamSlug} after {MaxRetries} attempts", 
            team.NotificationUrl, team.TeamSlug, maxRetries + 1);
    }

    private static string GenerateWebhookSignature(string payload, string secret)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
        return "sha256=" + Convert.ToHexString(hash).ToLowerInvariant();
    }
}