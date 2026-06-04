using k8s;
using k8s.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Promethix.CloudflareTunnelOperator.Routing.Application;
using System.Text.Json;

namespace Promethix.CloudflareTunnelOperator.Routing.Integrations.Kubernetes;

public sealed class KubernetesRouteIntentStatusUpdater(
    IKubernetes kubernetes,
    IOptions<KubernetesOperatorOptions> options,
    IOptions<RoutingOperatorOptions> routingOptions,
    ILogger<KubernetesRouteIntentStatusUpdater> logger) : IRouteIntentStatusUpdater
{
    private static readonly Action<ILogger, string, string, string, Exception?> LogUpdatingStatus =
        LoggerMessage.Define<string, string, string>(
            LogLevel.Information,
            new EventId(3200, nameof(UpdateAsync)),
            "Updating TunnelPublicHostname status for {Namespace}/{Name} to {ConditionReason}.");

    public async Task UpdateAsync(ReconciliationResult result, Exception? failure, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(result);

        var conflictHostnames = result.Plan.Conflicts
            .Select(conflict => conflict.Hostname)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var plannedHostnames = result.Plan.ToCreate
            .Concat(result.Plan.ToUpdate)
            .Select(route => route.Hostname)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var invalid in result.Intent.InvalidRoutes)
        {
            await UpdateStatusAsync(
                invalid.Namespace,
                invalid.Name,
                CreateStatus(
                    invalid.Generation,
                    routingOptions.Value.OwnershipTag,
                    options.Value.ManagedTunnelName,
                    null,
                    "False",
                    "InvalidSpec",
                    invalid.Reason),
                cancellationToken).ConfigureAwait(false);
        }

        foreach (var intent in result.Intent.ManagedRoutes)
        {
            var conditionStatus = "True";
            var reason = "Reconciled";
            var message = "Route intent reconciled.";

            if (failure is not null)
            {
                conditionStatus = "False";
                reason = "ReconcileFailed";
                message = failure.Message;
            }
            else if (conflictHostnames.Contains(intent.Route.Hostname))
            {
                conditionStatus = "False";
                reason = "Conflict";
                message = "Hostname exists in Cloudflare but is not owned by this operator.";
            }
            else if (!result.ChangesApplied && plannedHostnames.Contains(intent.Route.Hostname))
            {
                conditionStatus = "False";
                reason = "Planned";
                message = "Route change planned but not applied.";
            }

            await UpdateStatusAsync(
                intent.Namespace,
                intent.Name,
                CreateStatus(
                    intent.Generation,
                    routingOptions.Value.OwnershipTag,
                    options.Value.ManagedTunnelName,
                    intent.Route.Hostname,
                    conditionStatus,
                    reason,
                    message),
                cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task UpdateFailureAsync(RouteIntentDocument intent, Exception failure, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(intent);
        ArgumentNullException.ThrowIfNull(failure);

        foreach (var invalid in intent.InvalidRoutes)
        {
            await UpdateStatusAsync(
                invalid.Namespace,
                invalid.Name,
                CreateStatus(
                    invalid.Generation,
                    routingOptions.Value.OwnershipTag,
                    options.Value.ManagedTunnelName,
                    null,
                    "False",
                    "InvalidSpec",
                    invalid.Reason),
                cancellationToken).ConfigureAwait(false);
        }

        foreach (var intentRoute in intent.ManagedRoutes)
        {
            await UpdateStatusAsync(
                intentRoute.Namespace,
                intentRoute.Name,
                CreateStatus(
                    intentRoute.Generation,
                    routingOptions.Value.OwnershipTag,
                    options.Value.ManagedTunnelName,
                    intentRoute.Route.Hostname,
                    "False",
                    "ReconcileFailed",
                    failure.Message),
                cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task UpdateStatusAsync(string @namespace, string name, TunnelPublicHostnameStatus status, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(@namespace) || string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        LogUpdatingStatus(logger, @namespace, name, status.Conditions[0].Reason ?? string.Empty, null);

        var patchDocument = new
        {
            status,
        };

        var patch = new V1Patch(
            JsonSerializer.Serialize(patchDocument),
            V1Patch.PatchType.MergePatch);

        await kubernetes.CustomObjects.PatchNamespacedCustomObjectStatusAsync(
            patch,
            TunnelPublicHostnameCustomResource.Group,
            TunnelPublicHostnameCustomResource.Version,
            @namespace,
            TunnelPublicHostnameCustomResource.PluralName,
            name,
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    private static TunnelPublicHostnameStatus CreateStatus(
        long? observedGeneration,
        string ownershipTag,
        string tunnelName,
        string? appliedHostname,
        string conditionStatus,
        string reason,
        string message)
    {
        var status = new TunnelPublicHostnameStatus
        {
            ObservedGeneration = observedGeneration,
            OwnershipTag = ownershipTag,
            AppliedTunnelName = tunnelName,
            AppliedHostname = appliedHostname,
        };

        status.Conditions.Add(new V1Condition
        {
            Type = "Ready",
            Status = conditionStatus,
            Reason = reason,
            Message = message,
            LastTransitionTime = DateTime.UtcNow,
            ObservedGeneration = observedGeneration,
        });

        return status;
    }
}
