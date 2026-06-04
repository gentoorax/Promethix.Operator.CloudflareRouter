using Promethix.CloudflareTunnelOperator.Routing.Domain;

namespace Promethix.CloudflareTunnelOperator.Routing.Application;

public sealed record ManagedRouteIntent(
    string Name,
    string Namespace,
    long? Generation,
    PublicHostnameRoute Route);
