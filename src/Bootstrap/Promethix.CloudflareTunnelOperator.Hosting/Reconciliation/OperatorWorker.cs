using Microsoft.Extensions.Options;
using Promethix.CloudflareTunnelOperator.Routing.Application;

namespace Promethix.CloudflareTunnelOperator.Hosting.Reconciliation;

internal sealed class OperatorWorker(
    RouteReconciler reconciler,
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

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var result = await reconciler.ReconcileAsync(options.Value, stoppingToken).ConfigureAwait(false);
                state.MarkReconciliationCompleted(result.CompletedAt);

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
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
#pragma warning disable CA1031
            catch (Exception ex)
#pragma warning restore CA1031
            {
                LogReconciliationFailed(logger, ex);
            }

            await Task.Delay(TimeSpan.FromSeconds(options.Value.ReconciliationIntervalSeconds), stoppingToken).ConfigureAwait(false);
        }
    }
}
