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
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
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
            else if (result.ApplyBlocked)
            {
                readyStatus = "False";
                readyReason = result.ApplyBlockReason ?? "ApplyBlocked";
                readyMessage = result.ApplyBlockMessage ?? "Route change blocked by operator safety policy.";
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

    public Task UpdateCleanupPendingAsync(
    public Task UpdateSecurityPolicyAsync(
        ManagedRouteIntent intent,
        SecurityPolicyReconciliationResult? result,
        Exception? failure,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(intent);

        var status = "True";
        var reason = "Reconciled";
        var message = "Security policy reconciled.";

        if (intent.SecurityPolicy is null)
        {
            reason = "NotRequested";
            message = "No security policy reconciliation requested.";
        }
        else if (failure is not null)
        {
            status = "False";
            reason = "ReconcileFailed";
            message = failure.Message;
        }
        else if (result is not null && result.Plan.Conflicts.Count > 0)
        {
            status = "False";
            reason = "Conflict";
            message = "Security policy contains conflicts.";
        }
        else if (result is not null && result.Plan.HasChanges && !result.ChangesApplied)
        {
            status = "False";
            reason = "Planned";
            message = "Security policy change planned but not applied.";
        }

        return UpdateConditionAsync(
            intent.Namespace,
            intent.Name,
            CreateCondition("SecurityPolicyReady", status, reason, message, intent.Generation),
            cancellationToken);
    }

    public Task UpdateCleanupPendingAsync(
        string resourceNamespace,
        string name,
        long? observedGeneration,
        string? appliedHostname,
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
                "CleanupPending",
                message,
                "Unknown",
                "Deleting",
                "Resource is being deleted or removed from managed scope.",
                "True",
                "CleanupPending",
                message),
            cancellationToken);
    }

    public Task UpdateCleanupBlockedAsync(
        string resourceNamespace,
        string name,
        long? observedGeneration,
        string? appliedHostname,
        string reason,
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
                reason,
                message,
                "Unknown",
                "Deleting",
                "Resource is being deleted or removed from managed scope.",
                "True",
                reason,
                message),
            cancellationToken);
    }

    public Task UpdateCleanupCompletedAsync(
        string resourceNamespace,
        string name,
        long? observedGeneration,
        string? appliedHostname,
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
                "Deleted",
                message,
                "Unknown",
                "Deleting",
                "Resource is being deleted or removed from managed scope.",
                "False",
                "CleanedUp",
                message),
            cancellationToken);
    }

    private async Task UpdateConditionAsync(string @namespace, string name, V1Condition condition, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(@namespace) || string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        for (var attempt = 1; attempt <= StatusPatchRetryCount; attempt++)
        {
            var resource = await kubernetes.CustomObjects.GetNamespacedCustomObjectAsync<TunnelPublicHostnameCustomResource>(
                TunnelPublicHostnameCustomResource.Group,
                TunnelPublicHostnameCustomResource.Version,
                @namespace,
                TunnelPublicHostnameCustomResource.PluralName,
                name,
                cancellationToken).ConfigureAwait(false);

            var status = resource.Status ?? new TunnelPublicHostnameStatus();
            var conditions = status.Conditions
                .Where(existing => !string.Equals(existing.Type, condition.Type, StringComparison.Ordinal))
                .Append(condition)
                .ToArray();

            var patchDocument = new
            {
                status = new
                {
                    conditions = conditions.Select(current => new
                    {
                        type = current.Type,
                        status = current.Status,
                        reason = current.Reason,
                        message = current.Message,
                        lastTransitionTime = current.LastTransitionTime,
                        observedGeneration = current.ObservedGeneration,
                    }).ToArray(),
                },
            };

            var patch = new V1Patch(
                JsonSerializer.Serialize(patchDocument, JsonOptions),
                V1Patch.PatchType.MergePatch);

            try
            {
                _ = await kubernetes.CustomObjects.PatchNamespacedCustomObjectStatusAsync(
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
            catch (HttpOperationException ex) when (ex.Response.StatusCode == HttpStatusCode.NotFound)
            {
                return;
            }
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
            status = new
            {
                observedGeneration = status.ObservedGeneration,
                ownershipTag = status.OwnershipTag,
                appliedTunnelName = status.AppliedTunnelName,
                appliedHostname = status.AppliedHostname,
                conditions = status.Conditions.Select(condition => new
                {
                    type = condition.Type,
                    status = condition.Status,
                    reason = condition.Reason,
                    message = condition.Message,
                    lastTransitionTime = condition.LastTransitionTime,
                    observedGeneration = condition.ObservedGeneration,
                }).ToArray(),
            },
        };

        var patch = new V1Patch(
            JsonSerializer.Serialize(patchDocument, JsonOptions),
            V1Patch.PatchType.MergePatch);

        for (var attempt = 1; attempt <= StatusPatchRetryCount; attempt++)
        {
            try
            {
                _ = await kubernetes.CustomObjects.PatchNamespacedCustomObjectStatusAsync(
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
            catch (HttpOperationException ex) when (ex.Response.StatusCode == HttpStatusCode.NotFound)
            {
                return;
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
        status.Conditions.Add(CreateCondition("SecurityPolicyReady", "True", "NotRequested", "No security policy reconciliation requested.", observedGeneration));
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
