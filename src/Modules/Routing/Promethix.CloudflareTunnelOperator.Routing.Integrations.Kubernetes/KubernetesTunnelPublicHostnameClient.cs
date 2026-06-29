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
    IManagedTunnelPublicHostnameValidator managedValidator,
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

    public async Task<IReadOnlyCollection<TunnelPublicHostnameCustomResource>> GetCleanupCandidatesAsync(CancellationToken cancellationToken)
    {
        var list = await ListAsync(cancellationToken).ConfigureAwait(false);

        return
        [
            .. list.Items.Where(resource =>
                IsDeleting(resource)
                || (!IsManaged(resource) && HasManagedFinalizer(resource))),
        ];
    }

    public bool IsManaged(TunnelPublicHostnameCustomResource resource)
    {
        return managedValidator.IsManaged(resource);
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

        return !string.IsNullOrWhiteSpace(resource.Status?.AppliedHostname)
            ? resource.Status.AppliedHostname
            : string.IsNullOrWhiteSpace(resource.Spec.Hostname) ? null : resource.Spec.Hostname;
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
                _ = finalizers.RemoveAll(value => string.Equals(value, options.Value.ManagedFinalizerName, StringComparison.Ordinal));
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
                _ = await kubernetes.CustomObjects.PatchNamespacedCustomObjectAsync(
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
        return string.Equals(protocol.Trim(), "https", StringComparison.OrdinalIgnoreCase)
            ? RouteProtocol.Https
            : string.Equals(protocol.Trim(), "http", StringComparison.OrdinalIgnoreCase)
                ? RouteProtocol.Http
                : throw new InvalidOperationException($"Unsupported origin protocol '{protocol}'.");
    }

    private async Task<PublicHostnameRoute> ToRouteAsync(
        TunnelPublicHostnameCustomResource resource,
        string ownershipTag,
        CancellationToken cancellationToken)
    {
        await managedValidator.ValidateAsync(resource, cancellationToken).ConfigureAwait(false);

        var target = resource.Spec.Target;

        return target is null || string.IsNullOrWhiteSpace(target.Mode)
            ? ToLegacyDirectRoute(resource, ownershipTag)
            : string.Equals(target.Mode.Trim(), "ingress", StringComparison.OrdinalIgnoreCase)
                ? ToIngressRoute(resource, target, ownershipTag)
                : string.Equals(target.Mode.Trim(), "direct", StringComparison.OrdinalIgnoreCase)
                    ? ToDirectRoute(resource, target, ownershipTag)
                    : throw new InvalidOperationException($"Unsupported target mode '{target.Mode}'.");
    }

    private PublicHostnameRoute ToIngressRoute(
        TunnelPublicHostnameCustomResource resource,
        TunnelTargetSpec target,
        string ownershipTag)
    {
        var ingress = target.Ingress
            ?? throw new InvalidOperationException("spec.target.ingress is required when spec.target.mode=ingress.");

        var targetUrl = ResolveIngressTargetUrl(ingress);
        var protocol = string.Equals(targetUrl.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            ? RouteProtocol.Https
            : RouteProtocol.Http;
        var originServerName = protocol == RouteProtocol.Https
            ? resource.Spec.Hostname
            : null;

        return PublicHostnameRoute.Create(resource.Spec.Hostname, targetUrl, protocol, ownershipTag, originServerName);
    }

    private static PublicHostnameRoute ToDirectRoute(
        TunnelPublicHostnameCustomResource resource,
        TunnelTargetSpec target,
        string ownershipTag)
    {
        var direct = target.Direct
            ?? throw new InvalidOperationException("spec.target.direct is required when spec.target.mode=direct.");

        if (direct.Service is not null)
        {
            var targetUrl = ResolveServiceTargetUrl(direct.Service, "spec.target.direct.service");
            var serviceProtocol = string.Equals(targetUrl.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
                ? RouteProtocol.Https
                : RouteProtocol.Http;

            return PublicHostnameRoute.Create(resource.Spec.Hostname, targetUrl, serviceProtocol, ownershipTag);
        }

        if (direct.Url is null)
        {
            throw new InvalidOperationException("spec.target.direct.url is required when spec.target.direct.service is not supplied.");
        }

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
        return ingress.Service is null
            ? options.Value.IngressTargetUrl
            : ResolveServiceTargetUrl(ingress.Service, "spec.target.ingress.service");
    }

    private static Uri ResolveServiceTargetUrl(TunnelIngressServiceTargetSpec service, string fieldPath)
    {
        if (string.IsNullOrWhiteSpace(service.Name))
        {
            throw new InvalidOperationException($"{fieldPath}.name is required when {fieldPath} is supplied.");
        }

        if (string.IsNullOrWhiteSpace(service.Namespace))
        {
            throw new InvalidOperationException($"{fieldPath}.namespace is required when {fieldPath} is supplied.");
        }

        if (service.Port <= 0)
        {
            throw new InvalidOperationException($"{fieldPath}.port must be greater than zero when {fieldPath} is supplied.");
        }

        var scheme = service.Scheme?.Trim();
        return !string.Equals(scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            ? throw new InvalidOperationException($"{fieldPath}.scheme must be http or https when {fieldPath} is supplied.")
            : new Uri($"{scheme}://{service.Name}.{service.Namespace}.svc.cluster.local:{service.Port}");
    }
}
