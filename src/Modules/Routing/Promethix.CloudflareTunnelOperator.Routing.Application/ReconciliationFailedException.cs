namespace Promethix.CloudflareTunnelOperator.Routing.Application;

public sealed class ReconciliationFailedException : Exception
{
    public ReconciliationFailedException()
    {
    }

    public ReconciliationFailedException(string message)
        : base(message)
    {
    }

    public ReconciliationFailedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public ReconciliationFailedException(RouteIntentDocument intent, Exception innerException)
        : base("Route reconciliation failed after route intent was loaded.", innerException)
    {
        this.Intent = intent ?? throw new ArgumentNullException(nameof(intent));
    }

    public RouteIntentDocument? Intent { get; }
}
