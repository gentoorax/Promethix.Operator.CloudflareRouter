namespace Promethix.CloudflareTunnelOperator.Routing.Application;

public interface IRouteIntentStatusUpdater
{
    Task UpdateAsync(ReconciliationResult result, Exception? failure, CancellationToken cancellationToken);

    Task UpdateFailureAsync(RouteIntentDocument intent, Exception failure, CancellationToken cancellationToken);
}
