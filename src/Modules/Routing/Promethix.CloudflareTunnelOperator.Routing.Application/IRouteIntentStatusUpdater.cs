namespace Promethix.CloudflareTunnelOperator.Routing.Application;

public interface IRouteIntentStatusUpdater
{
    Task UpdateAsync(ReconciliationResult result, Exception? failure, CancellationToken cancellationToken);

    Task UpdateFailureAsync(RouteIntentDocument intent, Exception failure, CancellationToken cancellationToken);

    Task UpdateInvalidAsync(InvalidRouteIntent invalidIntent, CancellationToken cancellationToken);

    Task UpdateSecurityPolicyAsync(
        ManagedRouteIntent intent,
        SecurityPolicyReconciliationResult? result,
        Exception? failure,
        CancellationToken cancellationToken);

    Task UpdateCleanupPendingAsync(
        string resourceNamespace,
        string name,
        long? observedGeneration,
        string? appliedHostname,
        string message,
        CancellationToken cancellationToken);

    Task UpdateCleanupBlockedAsync(
        string resourceNamespace,
        string name,
        long? observedGeneration,
        string? appliedHostname,
        string reason,
        string message,
        CancellationToken cancellationToken);

    Task UpdateCleanupCompletedAsync(
        string resourceNamespace,
        string name,
        long? observedGeneration,
        string? appliedHostname,
        string message,
        CancellationToken cancellationToken);
}
