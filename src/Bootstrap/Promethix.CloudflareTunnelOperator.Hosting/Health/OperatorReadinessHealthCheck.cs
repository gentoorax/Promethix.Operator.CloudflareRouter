using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Promethix.CloudflareTunnelOperator.Hosting.Health;

internal sealed class OperatorReadinessHealthCheck(OperatorState state) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(
            state.HasCompletedInitialReconciliation
                ? HealthCheckResult.Healthy("Initial reconciliation has completed.")
                : HealthCheckResult.Degraded("Initial reconciliation has not completed yet."));
    }
}
