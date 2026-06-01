using Promethix.CloudflareTunnelOperator.Routing.Domain;

namespace Promethix.CloudflareTunnelOperator.Routing.Application;

public interface ICloudflareTunnelRouteClient
{
    Task<IReadOnlyCollection<PublicHostnameRoute>> GetRoutesAsync(CancellationToken cancellationToken);

    Task ApplyAsync(RoutePlan plan, CancellationToken cancellationToken);
}
