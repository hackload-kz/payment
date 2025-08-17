namespace PaymentGateway.Core.Configuration;

/// <summary>
/// Configuration options for payment limits
/// </summary>
public class PaymentLimitsConfiguration
{
    public const string SectionName = "Payment";

    /// <summary>
    /// Global maximum payment amount in kopecks (default: 1B kopecks = 10M RUB)
    /// Teams can override this with their own MaxPaymentAmount setting
    /// </summary>
    public decimal GlobalMaxPaymentAmount { get; set; } = 1000000000; // 10M RUB default

    /// <summary>
    /// Global minimum payment amount in kopecks (default: 1000 kopecks = 10 RUB)
    /// </summary>
    public decimal GlobalMinPaymentAmount { get; set; } = 1000; // 10 RUB default

    /// <summary>
    /// Global daily payment limit in kopecks (default: 5B kopecks = 50M RUB)
    /// Teams can override this with their own DailyPaymentLimit setting
    /// </summary>
    public decimal GlobalDailyPaymentLimit { get; set; } = 5000000000; // 50M RUB default
}