namespace PaymentGateway.Core.Services;

public interface ICorrelationIdService
{
    string CurrentCorrelationId { get; }
    string GenerateCorrelationId();
    void SetCorrelationId(string correlationId);
}

public class CorrelationIdService : ICorrelationIdService
{
    private static readonly AsyncLocal<string> _correlationId = new();

    public string CurrentCorrelationId => _correlationId.Value ?? GenerateCorrelationId();

    public string GenerateCorrelationId()
    {
        var correlationId = Guid.NewGuid().ToString("N")[..16]; // 16 character correlation ID
        _correlationId.Value = correlationId;
        return correlationId;
    }

    public void SetCorrelationId(string correlationId)
    {
        _correlationId.Value = correlationId;
    }
}