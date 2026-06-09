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
    IIngressTargetValidator ingressTargetValidator,
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
                    await ToRouteAsync(resource, routingOptions.Value.OwnershipTag, cancellationToken).ConfigureAwait(false)));
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

    public async Task<(ManagedRouteIntent? ManagedIntent, InvalidRouteIntent? InvalidIntent)> TryBuildIntentAsync(
        TunnelPublicHostnameCustomResource resource,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(resource);

        try
        {
            return (
                new ManagedRouteIntent(
                resource.Metadata.Name ?? string.Empty,
                resource.Metadata.NamespaceProperty ?? string.Empty,
                resource.Metadata.Generation,
                await ToRouteAsync(resource, routingOptions.Value.OwnershipTag, cancellationToken).ConfigureAwait(false)),
                null);
        }
        catch (ArgumentException ex)
        {
            return (null, CreateInvalidIntent(resource, ex.Message));
        }
        catch (InvalidOperationException ex)
        {
            return (null, CreateInvalidIntent(resource, ex.Message));
        }
        catch (UriFormatException ex)
        {
            return (null, CreateInvalidIntent(resource, ex.Message));
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

    private async Task<PublicHostnameRoute> ToRouteAsync(
        TunnelPublicHostnameCustomResource resource,
        string ownershipTag,
        CancellationToken cancellationToken)
    {
        var target = resource.Spec.Target;

        if (target is null || string.IsNullOrWhiteSpace(target.Mode))
        {
            return ToLegacyDirectRoute(resource, ownershipTag);
        }

        if (string.Equals(target.Mode.Trim(), "ingress", StringComparison.OrdinalIgnoreCase))
        {
            return await ToIngressRouteAsync(resource, target, ownershipTag, cancellationToken).ConfigureAwait(false);
        }

        if (string.Equals(target.Mode.Trim(), "direct", StringComparison.OrdinalIgnoreCase))
        {
            return ToDirectRoute(resource, target, ownershipTag);
        }

        throw new InvalidOperationException($"Unsupported target mode '{target.Mode}'.");
    }

    private async Task<PublicHostnameRoute> ToIngressRouteAsync(
        TunnelPublicHostnameCustomResource resource,
        TunnelTargetSpec target,
        string ownershipTag,
        CancellationToken cancellationToken)
    {
        var ingress = target.Ingress
            ?? throw new InvalidOperationException("spec.target.ingress is required when spec.target.mode=ingress.");

        if (string.IsNullOrWhiteSpace(ingress.ClassName))
        {
            throw new InvalidOperationException("spec.target.ingress.className is required when spec.target.mode=ingress.");
        }

        if (!string.Equals(ingress.ClassName, options.Value.ManagedIngressClassName, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Ingress target class '{ingress.ClassName}' does not match managed ingress class '{options.Value.ManagedIngressClassName}'.");
        }

        await ingressTargetValidator.ValidateAsync(resource, ingress, cancellationToken).ConfigureAwait(false);

        var targetUrl = ResolveIngressTargetUrl(ingress);
        var protocol = string.Equals(targetUrl.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            ? RouteProtocol.Https
            : RouteProtocol.Http;

        return PublicHostnameRoute.Create(resource.Spec.Hostname, targetUrl, protocol, ownershipTag);
    }

    private static PublicHostnameRoute ToDirectRoute(
        TunnelPublicHostnameCustomResource resource,
        TunnelTargetSpec target,
        string ownershipTag)
    {
        var direct = target.Direct
            ?? throw new InvalidOperationException("spec.target.direct is required when spec.target.mode=direct.");

        var protocol = ParseProtocol(direct.Protocol);
        return PublicHostnameRoute.Create(resource.Spec.Hostname, direct.Url, protocol, ownershipTag);
    }

    private static PublicHostnameRoute ToLegacyDirectRoute(
        TunnelPublicHostnameCustomResource resource,
        string ownershipTag)
    {
        var origin = resource.Spec.Origin
            ?? throw new InvalidOperationException("spec.origin is required when spec.target is not supplied.");

        var protocol = ParseProtocol(origin.Protocol);
        return PublicHostnameRoute.Create(resource.Spec.Hostname, origin.Url, protocol, ownershipTag);
    }

    private Uri ResolveIngressTargetUrl(TunnelIngressTargetSpec ingress)
    {
        if (ingress.Service is null)
        {
            return options.Value.IngressTargetUrl;
        }

        if (string.IsNullOrWhiteSpace(ingress.Service.Name))
        {
            throw new InvalidOperationException("spec.target.ingress.service.name is required when spec.target.ingress.service is supplied.");
        }

        if (string.IsNullOrWhiteSpace(ingress.Service.Namespace))
        {
            throw new InvalidOperationException("spec.target.ingress.service.namespace is required when spec.target.ingress.service is supplied.");
        }

        if (ingress.Service.Port <= 0)
        {
            throw new InvalidOperationException("spec.target.ingress.service.port must be greater than zero when spec.target.ingress.service is supplied.");
        }

        var scheme = ingress.Service.Scheme?.Trim();
        if (!string.Equals(scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("spec.target.ingress.service.scheme must be http or https when spec.target.ingress.service is supplied.");
        }

        return new Uri($"{scheme}://{ingress.Service.Name}.{ingress.Service.Namespace}.svc.cluster.local:{ingress.Service.Port}");
    }
}
