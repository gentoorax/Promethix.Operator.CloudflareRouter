using Promethix.CloudflareTunnelOperator.Routing.Domain;

namespace Promethix.CloudflareTunnelOperator.Routing.Application;

public sealed record RouteIntentDocument(
    string Source,
    IReadOnlyCollection<ManagedRouteIntent> ManagedRoutes,
    IReadOnlyCollection<InvalidRouteIntent> InvalidRoutes)
{
    public IReadOnlyCollection<PublicHostnameRoute> Routes => ManagedRoutes.Select(intent => intent.Route).ToArray();
}
