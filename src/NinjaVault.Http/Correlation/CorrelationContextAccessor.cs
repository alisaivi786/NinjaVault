namespace NinjaVault.Http.Correlation;

/// <summary>
/// Default <see cref="ICorrelationContextAccessor"/> backed by <see cref="AsyncLocal{T}"/>.
/// Registered as a singleton; the ambient value still flows per async logical call.
/// </summary>
public sealed class CorrelationContextAccessor : ICorrelationContextAccessor
{
    private static readonly AsyncLocal<CorrelationContext?> Ambient = new();

    public CorrelationContext? Current
    {
        get => Ambient.Value;
        set => Ambient.Value = value;
    }

    public string GetOrCreateCorrelationId()
    {
        CorrelationContext? current = Ambient.Value;
        if (current is not null && !string.IsNullOrWhiteSpace(current.CorrelationId))
        {
            return current.CorrelationId;
        }

        string correlationId = Guid.NewGuid().ToString("N");
        Ambient.Value = new CorrelationContext(correlationId, current?.TraceId);
        return correlationId;
    }
}