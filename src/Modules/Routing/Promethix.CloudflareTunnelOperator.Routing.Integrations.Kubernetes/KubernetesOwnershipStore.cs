using k8s;
using k8s.Autorest;
using k8s.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Promethix.CloudflareTunnelOperator.Routing.Application;
using System.Net;
using System.Text.Json;

namespace Promethix.CloudflareTunnelOperator.Routing.Integrations.Kubernetes;

public sealed class KubernetesOwnershipStore(
    IKubernetes kubernetes,
    IOptions<KubernetesOperatorOptions> options,
    ILogger<KubernetesOwnershipStore> logger) : IManagedRouteOwnershipStore
{
    private const string OwnershipDataKey = "ownership.json";

    private static readonly Action<ILogger, string, string, Exception?> LogCreatingConfigMap =
        LoggerMessage.Define<string, string>(
            LogLevel.Information,
            new EventId(3100, nameof(SaveOwnershipAsync)),
            "Creating ownership ConfigMap {ConfigMapName} in namespace {Namespace}.");

    public async Task<IReadOnlyDictionary<string, string>> GetOwnershipAsync(CancellationToken cancellationToken)
    {
        try
        {
            var configMap = await kubernetes.CoreV1.ReadNamespacedConfigMapAsync(
                options.Value.OwnershipConfigMapName,
                options.Value.OwnershipConfigMapNamespace,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            if (configMap.Data is null || !configMap.Data.TryGetValue(OwnershipDataKey, out var json) || string.IsNullOrWhiteSpace(json))
            {
                return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }

            return JsonSerializer.Deserialize<Dictionary<string, string>>(json)
                ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
        catch (HttpOperationException ex) when (ex.Response.StatusCode == HttpStatusCode.NotFound)
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    public async Task SaveOwnershipAsync(IReadOnlyDictionary<string, string> ownershipByHostname, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ownershipByHostname);

        var serializedOwnership = JsonSerializer.Serialize(ownershipByHostname);

        try
        {
            var existing = await kubernetes.CoreV1.ReadNamespacedConfigMapAsync(
                options.Value.OwnershipConfigMapName,
                options.Value.OwnershipConfigMapNamespace,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            existing.Data ??= new Dictionary<string, string>(StringComparer.Ordinal);
            existing.Data[OwnershipDataKey] = serializedOwnership;

            await kubernetes.CoreV1.ReplaceNamespacedConfigMapAsync(
                existing,
                existing.Metadata.Name,
                existing.Metadata.NamespaceProperty,
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (HttpOperationException ex) when (ex.Response.StatusCode == HttpStatusCode.NotFound)
        {
            LogCreatingConfigMap(logger, options.Value.OwnershipConfigMapName, options.Value.OwnershipConfigMapNamespace, null);

            var configMap = new V1ConfigMap
            {
                Metadata = new V1ObjectMeta
                {
                    Name = options.Value.OwnershipConfigMapName,
                    NamespaceProperty = options.Value.OwnershipConfigMapNamespace,
                },
                Data = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    [OwnershipDataKey] = serializedOwnership,
                },
            };

            await kubernetes.CoreV1.CreateNamespacedConfigMapAsync(
                configMap,
                options.Value.OwnershipConfigMapNamespace,
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }
    }
}
