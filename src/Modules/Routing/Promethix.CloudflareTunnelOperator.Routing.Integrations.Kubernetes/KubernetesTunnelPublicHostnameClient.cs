using k8s;
using k8s.Autorest;
using k8s.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Promethix.CloudflareTunnelOperator.Routing.Application;
using Promethix.CloudflareTunnelOperator.Routing.Domain;
using System.Net;
using System.Text.Json;

namespace Promethix.CloudflareTunnelOperator.Routing.Integrations.Kubernetes;

public sealed class KubernetesTunnelPublicHostnameClient(
    IKubernetes kubernetes,
    IOptions<KubernetesOperatorOptions> options,
    IOptions<RoutingOperatorOptions> routingOptions,
    ILogger<KubernetesTunnelPublicHostnameClient> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private const int MetadataPatchRetryCount = 3;

    private static readonly Action<ILogger, int, string, string, Exception?> LogLoadedIntent =
        LoggerMessage.Define<int, string, string>(
            LogLevel.Information,
            new EventId(3000, nameof(GetDesiredRoutesAsync)),
            "Loaded {RouteCount} TunnelPublicHostname resources for class {ManagedClassName} and tunnel {ManagedTunnelName}.");

    private static readonly Action<ILogger, string, Exception?> LogEnsuringFinalizer =
        LoggerMessage.Define<string>(
            LogLevel.Information,
            new EventId(3001, nameof(EnsureFinalizerAsync)),
            "Ensuring finalizer for {ResourceKey}.");

    private static readonly Action<ILogger, string, Exception?> LogRemovingFinalizer =
        LoggerMessage.Define<string>(
            LogLevel.Information,
            new EventId(3002, nameof(RemoveFinalizerAsync)),
            "Removing finalizer for {ResourceKey}.");

    public async Task<RouteIntentDocument> GetDesiredRoutesAsync(CancellationToken cancellationToken)
    {
        var list = await ListAsync(cancellationToken).ConfigureAwait(false);
        var managedRoutes = new List<ManagedRouteIntent>();
        var invalidRoutes = new List<InvalidRouteIntent>();

        foreach (var resource in list.Items.Where(IsDesiredManagedResource))
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
                invalidRoutes.Add(CreateInvalidIntent(resource, ex.Message));
            }
            catch (InvalidOperationException ex)
            {
                invalidRoutes.Add(CreateInvalidIntent(resource, ex.Message));
            }
            catch (UriFormatException ex)
            {
                invalidRoutes.Add(CreateInvalidIntent(resource, ex.Message));
            }
        }

        LogLoadedIntent(logger, managedRoutes.Count, options.Value.ManagedClassName, options.Value.ManagedTunnelName, null);

        return new RouteIntentDocument(
            Source: $"kubernetes-crd:{TunnelPublicHostnameCustomResource.PluralName}",
            ManagedRoutes: managedRoutes,
            InvalidRoutes: invalidRoutes);
    }

    public bool IsManaged(TunnelPublicHostnameCustomResource resource)
    {
        ArgumentNullException.ThrowIfNull(resource);

        return resource.Spec.Enabled
            && string.Equals(resource.Spec.ClassName, options.Value.ManagedClassName, StringComparison.Ordinal)
            && string.Equals(resource.Spec.TunnelRef.Name, options.Value.ManagedTunnelName, StringComparison.Ordinal);
    }

    public static bool IsDeleting(TunnelPublicHostnameCustomResource resource)
    {
        ArgumentNullException.ThrowIfNull(resource);

        return resource.Metadata.DeletionTimestamp is not null;
    }

    public async Task<TunnelPublicHostnameCustomResource?> GetAsync(TunnelPublicHostnameResourceKey key, CancellationToken cancellationToken)
    {
        try
        {
            return await kubernetes.CustomObjects.GetNamespacedCustomObjectAsync<TunnelPublicHostnameCustomResource>(
                TunnelPublicHostnameCustomResource.Group,
                TunnelPublicHostnameCustomResource.Version,
                key.Namespace,
                TunnelPublicHostnameCustomResource.PluralName,
                key.Name,
                cancellationToken).ConfigureAwait(false);
        }
        catch (HttpOperationException ex) when (ex.Response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public Task EnsureFinalizerAsync(TunnelPublicHostnameResourceKey key, CancellationToken cancellationToken)
    {
        LogEnsuringFinalizer(logger, key.ToString(), null);
        return PatchFinalizerAsync(key, addFinalizer: true, cancellationToken);
    }

    public Task RemoveFinalizerAsync(TunnelPublicHostnameResourceKey key, CancellationToken cancellationToken)
    {
        LogRemovingFinalizer(logger, key.ToString(), null);
        return PatchFinalizerAsync(key, addFinalizer: false, cancellationToken);
    }

    public bool HasManagedFinalizer(TunnelPublicHostnameCustomResource resource)
    {
        ArgumentNullException.ThrowIfNull(resource);

        return resource.Metadata.Finalizers?.Contains(options.Value.ManagedFinalizerName, StringComparer.Ordinal) == true;
    }

    public static string? GetCleanupHostname(TunnelPublicHostnameCustomResource resource)
    {
        ArgumentNullException.ThrowIfNull(resource);

        if (!string.IsNullOrWhiteSpace(resource.Status?.AppliedHostname))
        {
            return resource.Status.AppliedHostname;
        }

        return string.IsNullOrWhiteSpace(resource.Spec.Hostname) ? null : resource.Spec.Hostname;
    }

    public bool TryBuildIntent(
        TunnelPublicHostnameCustomResource resource,
        out ManagedRouteIntent? managedIntent,
        out InvalidRouteIntent? invalidIntent)
    {
        ArgumentNullException.ThrowIfNull(resource);

        managedIntent = null;
        invalidIntent = null;

        try
        {
            managedIntent = new ManagedRouteIntent(
                resource.Metadata.Name ?? string.Empty,
                resource.Metadata.NamespaceProperty ?? string.Empty,
                resource.Metadata.Generation,
                ToRoute(resource, routingOptions.Value.OwnershipTag));
            return true;
        }
        catch (ArgumentException ex)
        {
            invalidIntent = CreateInvalidIntent(resource, ex.Message);
            return false;
        }
        catch (InvalidOperationException ex)
        {
            invalidIntent = CreateInvalidIntent(resource, ex.Message);
            return false;
        }
        catch (UriFormatException ex)
        {
            invalidIntent = CreateInvalidIntent(resource, ex.Message);
            return false;
        }
    }

    private async Task<TunnelPublicHostnameCustomResourceList> ListAsync(CancellationToken cancellationToken)
    {
        var payload = await kubernetes.CustomObjects.ListClusterCustomObjectAsync(
            group: TunnelPublicHostnameCustomResource.Group,
            version: TunnelPublicHostnameCustomResource.Version,
            plural: TunnelPublicHostnameCustomResource.PluralName,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        return JsonSerializer.Deserialize<TunnelPublicHostnameCustomResourceList>(
                   JsonSerializer.Serialize(payload),
                   JsonOptions)
               ?? new TunnelPublicHostnameCustomResourceList();
    }

    private async Task PatchFinalizerAsync(TunnelPublicHostnameResourceKey key, bool addFinalizer, CancellationToken cancellationToken)
    {
        for (var attempt = 1; attempt <= MetadataPatchRetryCount; attempt++)
        {
            var resource = await GetAsync(key, cancellationToken).ConfigureAwait(false);

            if (resource is null)
            {
                return;
            }

            resource.Metadata.Finalizers ??= [];
            var finalizers = resource.Metadata.Finalizers.ToList();
            var containsFinalizer = finalizers.Contains(options.Value.ManagedFinalizerName, StringComparer.Ordinal);

            if (addFinalizer && containsFinalizer)
            {
                return;
            }

            if (!addFinalizer && !containsFinalizer)
            {
                return;
            }

            if (addFinalizer)
            {
                finalizers.Add(options.Value.ManagedFinalizerName);
            }
            else
            {
                finalizers.RemoveAll(value => string.Equals(value, options.Value.ManagedFinalizerName, StringComparison.Ordinal));
            }

            var patchDocument = new
            {
                metadata = new
                {
                    resourceVersion = resource.Metadata.ResourceVersion,
                    finalizers,
                },
            };

            var patch = new V1Patch(JsonSerializer.Serialize(patchDocument), V1Patch.PatchType.MergePatch);

            try
            {
                await kubernetes.CustomObjects.PatchNamespacedCustomObjectAsync(
                    patch,
                    TunnelPublicHostnameCustomResource.Group,
                    TunnelPublicHostnameCustomResource.Version,
                    key.Namespace,
                    TunnelPublicHostnameCustomResource.PluralName,
                    key.Name,
                    cancellationToken: cancellationToken).ConfigureAwait(false);
                return;
            }
            catch (HttpOperationException ex) when (ex.Response.StatusCode == HttpStatusCode.Conflict && attempt < MetadataPatchRetryCount)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(100 * attempt), cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private bool IsDesiredManagedResource(TunnelPublicHostnameCustomResource resource)
    {
        return resource.Metadata.DeletionTimestamp is null && IsManaged(resource);
    }

    private static InvalidRouteIntent CreateInvalidIntent(TunnelPublicHostnameCustomResource resource, string reason)
    {
        return new InvalidRouteIntent(
            resource.Metadata.Name ?? string.Empty,
            resource.Metadata.NamespaceProperty ?? string.Empty,
            resource.Metadata.Generation,
            reason);
    }

    private static PublicHostnameRoute ToRoute(TunnelPublicHostnameCustomResource resource, string ownershipTag)
    {
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
