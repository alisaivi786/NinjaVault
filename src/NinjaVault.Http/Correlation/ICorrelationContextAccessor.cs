namespace NinjaVault.Http.Correlation;

/// <summary>
/// Provides ambient access to the current <see cref="CorrelationContext"/>. Backed by an
/// <c>AsyncLocal</c> so the value flows to every concurrent async continuation started while
/// handling the same logical operation.
/// </summary>
public interface ICorrelationContextAccessor
{
    /// <summary>The correlation context for the current logical operation, if one has been set.</summary>
    CorrelationContext? Current { get; set; }

    /// <summary>
    /// Returns the current correlation id, or a newly generated one if no context is set.
    /// </summary>
    string GetOrCreateCorrelationId();
}