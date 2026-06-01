using Microsoft.Extensions.Options;
using Promethix.CloudflareTunnelOperator.Routing.Application;
using Promethix.CloudflareTunnelOperator.Routing.Domain;

namespace Promethix.CloudflareTunnelOperator.Hosting.Reconciliation;

internal sealed class ClusterRouteIntentSource(IOptions<RoutingOperatorOptions> options) : IClusterRouteIntentSource
{
    public Task<RouteIntentDocument> GetDesiredRoutesAsync(CancellationToken cancellationToken)
    {
        var sampleRoute = PublicHostnameRoute.Create(
            hostname: "example.internal.promethix.net",
            originService: new Uri("https://example-service.default.svc.cluster.local:8443"),
            protocol: RouteProtocol.Https,
            ownershipTag: options.Value.OwnershipTag);

        return Task.FromResult(
            new RouteIntentDocument(
                Source: "placeholder-kubernetes-intent-source",
                Routes: new[] { sampleRoute }));
    }
}
