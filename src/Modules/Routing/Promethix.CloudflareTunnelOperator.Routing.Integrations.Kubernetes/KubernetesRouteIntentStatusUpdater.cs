using k8s;
using k8s.Autorest;
using k8s.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Promethix.CloudflareTunnelOperator.Routing.Application;
using System.Net;
using System.Text.Json;

namespace Promethix.CloudflareTunnelOperator.Routing.Integrations.Kubernetes;

public sealed class KubernetesRouteIntentStatusUpdater(
    IKubernetes kubernetes,
    IOptions<KubernetesOperatorOptions> options,
    IOptions<RoutingOperatorOptions> routingOptions,
    ILogger<KubernetesRouteIntentStatusUpdater> logger) : IRouteIntentStatusUpdater
{
    private const int StatusPatchRetryCount = 3;

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
            await UpdateInvalidAsync(invalid, cancellationToken).ConfigureAwait(false);
        }

        foreach (var intent in result.Intent.ManagedRoutes)
        {
            var readyStatus = "True";
            var readyReason = "Reconciled";
            var readyMessage = "Route intent reconciled.";

            if (failure is not null)
            {
                readyStatus = "False";
                readyReason = "ReconcileFailed";
                readyMessage = failure.Message;
            }
            else if (conflictHostnames.Contains(intent.Route.Hostname))
            {
                readyStatus = "False";
                readyReason = "Conflict";
                readyMessage = "Hostname exists in Cloudflare but is not owned by this operator.";
            }
            else if (!result.ChangesApplied && plannedHostnames.Contains(intent.Route.Hostname))
            {
                readyStatus = "False";
                readyReason = "Planned";
                readyMessage = "Route change planned but not applied.";
            }

            await UpdateStatusAsync(
                intent.Namespace,
                intent.Name,
                CreateStatus(
                    intent.Generation,
                    routingOptions.Value.OwnershipTag,
                    options.Value.ManagedTunnelName,
                    intent.Route.Hostname,
                    readyStatus,
                    readyReason,
                    readyMessage,
                    "True",
                    "Validated",
                    "Route intent is valid.",
                    "False",
                    "NotDeleting",
                    "Resource is not being deleted."),
                cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task UpdateFailureAsync(RouteIntentDocument intent, Exception failure, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(intent);
        ArgumentNullException.ThrowIfNull(failure);

        foreach (var invalid in intent.InvalidRoutes)
        {
            await UpdateInvalidAsync(invalid, cancellationToken).ConfigureAwait(false);
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
                    failure.Message,
                    "True",
                    "Validated",
                    "Route intent is valid.",
                    "False",
                    "NotDeleting",
                    "Resource is not being deleted."),
                cancellationToken).ConfigureAwait(false);
        }
    }

    public Task UpdateInvalidAsync(InvalidRouteIntent invalidIntent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(invalidIntent);

        return UpdateStatusAsync(
            invalidIntent.Namespace,
            invalidIntent.Name,
            CreateStatus(
                invalidIntent.Generation,
                routingOptions.Value.OwnershipTag,
                options.Value.ManagedTunnelName,
                null,
                "False",
                "InvalidSpec",
                invalidIntent.Reason,
                "False",
                "InvalidSpec",
                invalidIntent.Reason,
                "False",
                "NotDeleting",
                "Resource is not being deleted."),
            cancellationToken);
    }

    public Task UpdateCleanupAsync(
        string resourceNamespace,
        string name,
        long? observedGeneration,
        string? appliedHostname,
        bool completed,
        string message,
        CancellationToken cancellationToken)
    {
        return UpdateStatusAsync(
            resourceNamespace,
            name,
            CreateStatus(
                observedGeneration,
                routingOptions.Value.OwnershipTag,
                options.Value.ManagedTunnelName,
                appliedHostname,
                "False",
                completed ? "Deleted" : "CleanupPending",
                completed ? "Managed route cleanup completed." : message,
                "Unknown",
                "Deleting",
                "Resource is being deleted or removed from managed scope.",
                completed ? "False" : "True",
                completed ? "CleanedUp" : "CleanupPending",
                completed ? "Managed route cleanup completed." : message),
            cancellationToken);
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

        for (var attempt = 1; attempt <= StatusPatchRetryCount; attempt++)
        {
            try
            {
                await kubernetes.CustomObjects.PatchNamespacedCustomObjectStatusAsync(
                    patch,
                    TunnelPublicHostnameCustomResource.Group,
                    TunnelPublicHostnameCustomResource.Version,
                    @namespace,
                    TunnelPublicHostnameCustomResource.PluralName,
                    name,
                    cancellationToken: cancellationToken).ConfigureAwait(false);
                return;
            }
            catch (HttpOperationException ex) when (ex.Response.StatusCode == HttpStatusCode.Conflict && attempt < StatusPatchRetryCount)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(100 * attempt), cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private static TunnelPublicHostnameStatus CreateStatus(
        long? observedGeneration,
        string ownershipTag,
        string tunnelName,
        string? appliedHostname,
        string readyStatus,
        string readyReason,
        string readyMessage,
        string specValidStatus,
        string specValidReason,
        string specValidMessage,
        string cleanupStatus,
        string cleanupReason,
        string cleanupMessage)
    {
        var status = new TunnelPublicHostnameStatus
        {
            ObservedGeneration = observedGeneration,
            OwnershipTag = ownershipTag,
            AppliedTunnelName = tunnelName,
            AppliedHostname = appliedHostname,
        };

        status.Conditions.Add(CreateCondition("Ready", readyStatus, readyReason, readyMessage, observedGeneration));
        status.Conditions.Add(CreateCondition("SpecValid", specValidStatus, specValidReason, specValidMessage, observedGeneration));
        status.Conditions.Add(CreateCondition("Cleanup", cleanupStatus, cleanupReason, cleanupMessage, observedGeneration));
        return status;
    }

    private static V1Condition CreateCondition(
        string type,
        string status,
        string reason,
        string message,
        long? observedGeneration)
    {
        return new V1Condition
        {
            Type = type,
            Status = status,
            Reason = reason,
            Message = message,
            LastTransitionTime = DateTime.UtcNow,
            ObservedGeneration = observedGeneration,
        };
    }
}
