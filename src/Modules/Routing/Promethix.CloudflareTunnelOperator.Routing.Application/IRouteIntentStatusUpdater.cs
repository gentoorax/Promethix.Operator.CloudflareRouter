namespace Promethix.CloudflareTunnelOperator.Routing.Application;

public interface IRouteIntentStatusUpdater
{
    Task UpdateAsync(ReconciliationResult result, Exception? failure, CancellationToken cancellationToken);

    Task UpdateFailureAsync(RouteIntentDocument intent, Exception failure, CancellationToken cancellationToken);

    Task UpdateInvalidAsync(InvalidRouteIntent invalidIntent, CancellationToken cancellationToken);

    Task UpdateCleanupAsync(
        string resourceNamespace,
        string name,
        long? observedGeneration,
        string? appliedHostname,
        bool completed,
        string message,
        CancellationToken cancellationToken);
}
