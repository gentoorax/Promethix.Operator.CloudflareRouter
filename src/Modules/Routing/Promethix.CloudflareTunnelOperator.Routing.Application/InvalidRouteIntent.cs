namespace Promethix.CloudflareTunnelOperator.Routing.Application;

public sealed record InvalidRouteIntent(
    string Name,
    string Namespace,
    long? Generation,
    string Reason);
