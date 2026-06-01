namespace Promethix.CloudflareTunnelOperator.Routing.Application;

public interface IClock
{
    DateTimeOffset UtcNow { get; }
}
