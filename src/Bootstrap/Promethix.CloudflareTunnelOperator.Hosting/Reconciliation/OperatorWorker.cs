using Microsoft.Extensions.Options;
using Promethix.CloudflareTunnelOperator.Routing.Application;

namespace Promethix.CloudflareTunnelOperator.Hosting.Reconciliation;

internal sealed class OperatorWorker(
    RouteReconciler reconciler,
    IRouteIntentStatusUpdater statusUpdater,
    ReconciliationSignalQueue signalQueue,
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
        await RunIterationAsync("startup", stoppingToken).ConfigureAwait(false);

        while (!stoppingToken.IsCancellationRequested)
        {
            var triggerReason = await signalQueue
                .WaitAsync(TimeSpan.FromSeconds(options.Value.ReconciliationIntervalSeconds), stoppingToken)
                .ConfigureAwait(false);

            await RunIterationAsync(triggerReason, stoppingToken).ConfigureAwait(false);
        }
    }

    private async Task RunIterationAsync(string triggerReason, CancellationToken cancellationToken)
    {
        LogReconciliationTriggered(logger, triggerReason, null);

        try
        {
            var result = await reconciler.ReconcileAsync(options.Value, cancellationToken).ConfigureAwait(false);
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
}
