using k8s;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Promethix.CloudflareTunnelOperator.Routing.Application;
using Promethix.CloudflareTunnelOperator.Routing.Domain;
using System.Text.Json;

namespace Promethix.CloudflareTunnelOperator.Routing.Integrations.Kubernetes;

public sealed class KubernetesRouteIntentSource(
    IKubernetes kubernetes,
    IOptions<KubernetesOperatorOptions> options,
    IOptions<RoutingOperatorOptions> routingOptions,
    ILogger<KubernetesRouteIntentSource> logger) : IClusterRouteIntentSource
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static readonly Action<ILogger, int, string, string, Exception?> LogLoadedIntent =
        LoggerMessage.Define<int, string, string>(
            LogLevel.Information,
            new EventId(3000, nameof(GetDesiredRoutesAsync)),
            "Loaded {RouteCount} TunnelPublicHostname resources for class {ManagedClassName} and tunnel {ManagedTunnelName}.");

    public async Task<RouteIntentDocument> GetDesiredRoutesAsync(CancellationToken cancellationToken)
    {
        var payload = await kubernetes.CustomObjects.ListClusterCustomObjectAsync(
            group: TunnelPublicHostnameCustomResource.Group,
            version: TunnelPublicHostnameCustomResource.Version,
            plural: TunnelPublicHostnameCustomResource.PluralName,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        var list = JsonSerializer.Deserialize<TunnelPublicHostnameCustomResourceList>(
            JsonSerializer.Serialize(payload),
            JsonOptions) ?? new TunnelPublicHostnameCustomResourceList();

        var managedRoutes = new List<ManagedRouteIntent>();
        var invalidRoutes = new List<InvalidRouteIntent>();

        foreach (var resource in list.Items
                     .Where(resource => resource.Spec.Enabled)
                     .Where(resource => string.Equals(resource.Spec.ClassName, options.Value.ManagedClassName, StringComparison.Ordinal))
                     .Where(resource => string.Equals(resource.Spec.TunnelRef.Name, options.Value.ManagedTunnelName, StringComparison.Ordinal)))
        {
            try
            {
                managedRoutes.Add(new ManagedRouteIntent(
                    resource.Metadata.Name ?? string.Empty,
                    resource.Metadata.NamespaceProperty ?? string.Empty,
                    resource.Metadata.Generation,
                    ToRoute(resource, routingOptions.Value.OwnershipTag)));
            }
            catch (ArgumentException ex)
            {
                invalidRoutes.Add(new InvalidRouteIntent(
                    resource.Metadata.Name ?? string.Empty,
                    resource.Metadata.NamespaceProperty ?? string.Empty,
                    resource.Metadata.Generation,
                    ex.Message));
            }
            catch (InvalidOperationException ex)
            {
                invalidRoutes.Add(new InvalidRouteIntent(
                    resource.Metadata.Name ?? string.Empty,
                    resource.Metadata.NamespaceProperty ?? string.Empty,
                    resource.Metadata.Generation,
                    ex.Message));
            }
            catch (UriFormatException ex)
            {
                invalidRoutes.Add(new InvalidRouteIntent(
                    resource.Metadata.Name ?? string.Empty,
                    resource.Metadata.NamespaceProperty ?? string.Empty,
                    resource.Metadata.Generation,
                    ex.Message));
            }
        }

        LogLoadedIntent(logger, managedRoutes.Count, options.Value.ManagedClassName, options.Value.ManagedTunnelName, null);

        return new RouteIntentDocument(
            Source: $"kubernetes-crd:{TunnelPublicHostnameCustomResource.PluralName}",
            ManagedRoutes: managedRoutes,
            InvalidRoutes: invalidRoutes);
    }

    private static PublicHostnameRoute ToRoute(TunnelPublicHostnameCustomResource resource, string ownershipTag)
    {
        ArgumentNullException.ThrowIfNull(resource);

        var protocol = ParseProtocol(resource.Spec.Origin.Protocol);
        return PublicHostnameRoute.Create(resource.Spec.Hostname, resource.Spec.Origin.Url, protocol, ownershipTag);
    }

    private static RouteProtocol ParseProtocol(string protocol)
    {
        if (string.Equals(protocol.Trim(), "http", StringComparison.OrdinalIgnoreCase))
        {
            return RouteProtocol.Http;
        }

        if (string.Equals(protocol.Trim(), "https", StringComparison.OrdinalIgnoreCase))
        {
            return RouteProtocol.Https;
        }

        throw new InvalidOperationException($"Unsupported origin protocol '{protocol}'.");
    }
}
