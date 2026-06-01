namespace Promethix.CloudflareTunnelOperator.Routing.Application;

public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
