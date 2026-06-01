namespace Promethix.CloudflareTunnelOperator.Routing.Application;

public interface IClusterRouteIntentSource
{
    Task<RouteIntentDocument> GetDesiredRoutesAsync(CancellationToken cancellationToken);
}
