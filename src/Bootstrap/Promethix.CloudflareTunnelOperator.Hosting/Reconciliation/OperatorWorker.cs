using Microsoft.Extensions.Options;
using Promethix.CloudflareTunnelOperator.Routing.Application;
using Promethix.CloudflareTunnelOperator.Routing.Integrations.Kubernetes;

namespace Promethix.CloudflareTunnelOperator.Hosting.Reconciliation;

internal sealed class OperatorWorker(
    RouteReconciler reconciler,
    IRouteIntentStatusUpdater statusUpdater,
    KubernetesTunnelPublicHostnameClient resourceClient,
    RouteIntentWorkQueue workQueue,
    IOptions<RoutingOperatorOptions> options,
    OperatorState state,
    ILogger<OperatorWorker> logger) : BackgroundService
{
    private static readonly Action<ILogger, string, int, int, int, int, bool, Exception?> LogReconciliationCompleted =
        LoggerMessage.Define<string, int, int, int, int, bool>(
            LogLevel.Information,
            new EventId(2000, "ReconciliationCompleted"),
            "Reconciliation completed from {Source}. Create {CreateCount}, update {UpdateCount}, delete {DeleteCount}, conflicts {ConflictCount}, applied {ChangesApplied}.");

    private static readonly Action<ILogger, Exception> LogReconciliationFailed =
        LoggerMessage.Define(
            LogLevel.Error,
            new EventId(2001, "ReconciliationFailed"),
            "Reconciliation iteration failed.");

    private static readonly Action<ILogger, string, Exception?> LogReconciliationTriggered =
        LoggerMessage.Define<string>(
            LogLevel.Information,
            new EventId(2002, "ReconciliationTriggered"),
            "Starting reconciliation triggered by {TriggerReason}.");

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        workQueue.EnqueueFullResync("startup");

        while (!stoppingToken.IsCancellationRequested)
        {
            var workItem = await workQueue
                .WaitAsync(TimeSpan.FromSeconds(options.Value.ReconciliationIntervalSeconds), stoppingToken)
                .ConfigureAwait(false);

            await RunIterationAsync(workItem, stoppingToken).ConfigureAwait(false);
        }
    }

    private async Task RunIterationAsync(RouteIntentWorkItem workItem, CancellationToken cancellationToken)
    {
        LogReconciliationTriggered(logger, workItem.Reason, null);

        try
        {
            if (workItem.Kind == RouteIntentWorkItemKind.Resource && workItem.ResourceKey is { } resourceKey)
            {
                await RunResourceIterationAsync(resourceKey, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await RunFullIterationAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (ReconciliationFailedException ex) when (ex.Intent is not null && ex.InnerException is not null)
        {
            await statusUpdater.UpdateFailureAsync(ex.Intent, ex.InnerException, cancellationToken).ConfigureAwait(false);
            LogReconciliationFailed(logger, ex.InnerException);
        }
#pragma warning disable CA1031
        catch (Exception ex)
#pragma warning restore CA1031
        {
            LogReconciliationFailed(logger, ex);
        }
    }

    private async Task RunFullIterationAsync(CancellationToken cancellationToken)
    {
        var result = await reconciler.ReconcileAsync(options.Value, cancellationToken).ConfigureAwait(false);
        state.MarkReconciliationCompleted(result.CompletedAt);
        await EnsureManagedFinalizersAsync(result, cancellationToken).ConfigureAwait(false);
        await statusUpdater.UpdateAsync(result, failure: null, cancellationToken).ConfigureAwait(false);

        LogReconciliationCompleted(
            logger,
            result.Intent.Source,
            result.Plan.ToCreate.Count,
            result.Plan.ToUpdate.Count,
            result.Plan.ToDelete.Count,
            result.Plan.Conflicts.Count,
            result.ChangesApplied,
            null);
    }

    private async Task RunResourceIterationAsync(TunnelPublicHostnameResourceKey key, CancellationToken cancellationToken)
    {
        var resource = await resourceClient.GetAsync(key, cancellationToken).ConfigureAwait(false);

        if (resource is null)
        {
            return;
        }

        if (KubernetesTunnelPublicHostnameClient.IsDeleting(resource) || (!resourceClient.IsManaged(resource) && resourceClient.HasManagedFinalizer(resource)))
        {
            await CleanupResourceAsync(resource, cancellationToken).ConfigureAwait(false);
            return;
        }

        if (!resourceClient.IsManaged(resource))
        {
            return;
        }

        await resourceClient.EnsureFinalizerAsync(key, cancellationToken).ConfigureAwait(false);

        if (!resourceClient.TryBuildIntent(resource, out var managedIntent, out var invalidIntent))
        {
            if (invalidIntent is not null)
            {
                await statusUpdater.UpdateInvalidAsync(invalidIntent, cancellationToken).ConfigureAwait(false);
            }

            return;
        }

        if (managedIntent is null)
        {
            return;
        }

        var result = await reconciler.ReconcileManagedRouteAsync(options.Value, managedIntent, cancellationToken).ConfigureAwait(false);
        state.MarkReconciliationCompleted(result.CompletedAt);
        await statusUpdater.UpdateAsync(result, failure: null, cancellationToken).ConfigureAwait(false);

        LogReconciliationCompleted(
            logger,
            result.Intent.Source,
            result.Plan.ToCreate.Count,
            result.Plan.ToUpdate.Count,
            result.Plan.ToDelete.Count,
            result.Plan.Conflicts.Count,
            result.ChangesApplied,
            null);
    }

    private async Task EnsureManagedFinalizersAsync(ReconciliationResult result, CancellationToken cancellationToken)
    {
        foreach (var intent in result.Intent.ManagedRoutes)
        {
            await resourceClient.EnsureFinalizerAsync(
                new TunnelPublicHostnameResourceKey(intent.Namespace, intent.Name),
                cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task CleanupResourceAsync(TunnelPublicHostnameCustomResource resource, CancellationToken cancellationToken)
    {
        var key = new TunnelPublicHostnameResourceKey(
            resource.Metadata.NamespaceProperty ?? string.Empty,
            resource.Metadata.Name ?? string.Empty);
        var cleanupHostname = KubernetesTunnelPublicHostnameClient.GetCleanupHostname(resource);

        await statusUpdater.UpdateCleanupAsync(
            key.Namespace,
            key.Name,
            resource.Metadata.Generation,
            cleanupHostname,
            completed: false,
            message: "Cleaning up managed route before removing finalizer.",
            cancellationToken).ConfigureAwait(false);

        if (!string.IsNullOrWhiteSpace(cleanupHostname))
        {
            var cleanupResult = await reconciler.CleanupRouteAsync(options.Value, cleanupHostname, cancellationToken).ConfigureAwait(false);

            if (cleanupResult.Plan.HasChanges && !cleanupResult.ChangesApplied)
            {
                return;
            }
        }

        await statusUpdater.UpdateCleanupAsync(
            key.Namespace,
            key.Name,
            resource.Metadata.Generation,
            cleanupHostname,
            completed: true,
            message: "Managed route cleanup completed.",
            cancellationToken).ConfigureAwait(false);

        if (resourceClient.HasManagedFinalizer(resource))
        {
            await resourceClient.RemoveFinalizerAsync(key, cancellationToken).ConfigureAwait(false);
        }
    }
}
