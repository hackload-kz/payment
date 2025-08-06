using Microsoft.Extensions.Logging;

namespace PaymentGateway.Core.Services;

public interface INotificationWebhookService
{
    Task SendPaymentNotificationAsync(string paymentId, string status, string? webhookUrl = null);
    Task SendPaymentCompletedNotificationAsync(string paymentId);
    Task SendPaymentFailedNotificationAsync(string paymentId, string reason);
    Task SendNotificationAsync(object request);
}

public class NotificationWebhookService : INotificationWebhookService
{
    private readonly ILogger<NotificationWebhookService> _logger;

    public NotificationWebhookService(ILogger<NotificationWebhookService> logger)
    {
        _logger = logger;
    }

    public async Task SendPaymentNotificationAsync(string paymentId, string status, string? webhookUrl = null)
    {
        _logger.LogInformation("Webhook notification for payment {PaymentId} with status {Status} to {WebhookUrl}", 
            paymentId, status, webhookUrl ?? "default");
        await Task.CompletedTask;
    }

    public async Task SendPaymentCompletedNotificationAsync(string paymentId)
    {
        _logger.LogInformation("Payment completed webhook for {PaymentId}", paymentId);
        await Task.CompletedTask;
    }

    public async Task SendPaymentFailedNotificationAsync(string paymentId, string reason)
    {
        _logger.LogInformation("Payment failed webhook for {PaymentId}: {Reason}", paymentId, reason);
        await Task.CompletedTask;
    }

    public async Task SendNotificationAsync(object request)
    {
        _logger.LogInformation("Sending generic webhook notification: {Request}", request);
        await Task.CompletedTask;
    }
}