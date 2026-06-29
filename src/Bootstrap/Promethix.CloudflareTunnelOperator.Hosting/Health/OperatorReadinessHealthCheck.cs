using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Promethix.CloudflareTunnelOperator.Hosting.Admission;
using Promethix.CloudflareTunnelOperator.Routing.Application;

namespace Promethix.CloudflareTunnelOperator.Hosting.Health;

#pragma warning disable IDE0045 // Readability is preferred here over conditional-expression simplification.
#pragma warning disable IDE0046 // Readability is preferred here over if-statement simplification.

internal sealed class OperatorReadinessHealthCheck(
    OperatorState state,
    AdmissionWebhookRuntimeState webhookState,
    IOptions<RoutingOperatorOptions> options) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        if (TryGetInitialInventoryResult(out var result))
        {
            return Task.FromResult(result);
        }

        if (TryGetWebhookResult(out result))
        {
            return Task.FromResult(result);
        }

        if (TryGetMutationSafetyResult(out result))
        {
            return Task.FromResult(result);
        }

        return Task.FromResult(HealthCheckResult.Healthy("Operator startup safety checks have completed."));
    }

    private bool TryGetInitialInventoryResult(out HealthCheckResult result)
    {
        if (state.HasCompletedInitialFullInventoryPass)
        {
            result = HealthCheckResult.Healthy(string.Empty);
            return false;
        }

        result = HealthCheckResult.Degraded("Initial full inventory reconciliation has not completed yet.");
        return true;
    }

    private bool TryGetWebhookResult(out HealthCheckResult result)
    {
        if (!webhookState.Enabled || webhookState.ListenerReady)
        {
            result = HealthCheckResult.Healthy(string.Empty);
            return false;
        }

        if (webhookState.AreCertificateFilesPresent())
        {
            result = HealthCheckResult.Unhealthy(
                webhookState.FailureReason ?? "Admission webhook TLS material is present but the HTTPS listener is not serving.");
            return true;
        }

        result = HealthCheckResult.Unhealthy(
            webhookState.FailureReason ?? "Admission webhook TLS material is not available yet.");
        return true;
    }

    private bool TryGetMutationSafetyResult(out HealthCheckResult result)
    {
        if (!options.Value.ApplyChanges || state.IsStartupSafeForMutation)
        {
            result = HealthCheckResult.Healthy(string.Empty);
            return false;
        }

        result = HealthCheckResult.Unhealthy(
            state.StartupBlockMessage ?? "Cloudflare writes are blocked because startup safety checks have not passed.",
            data: new Dictionary<string, object>
            {
                ["reason"] = state.StartupBlockReason ?? "Unknown",
            });
        return true;
    }
}

#pragma warning restore IDE0046
#pragma warning restore IDE0045
