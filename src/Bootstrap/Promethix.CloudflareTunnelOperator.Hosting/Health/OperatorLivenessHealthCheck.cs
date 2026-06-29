using Microsoft.Extensions.Diagnostics.HealthChecks;
using Promethix.CloudflareTunnelOperator.Hosting.Admission;

namespace Promethix.CloudflareTunnelOperator.Hosting.Health;

#pragma warning disable IDE0045 // Readability is preferred here over conditional-expression simplification.
#pragma warning disable IDE0046 // Readability is preferred here over if-statement simplification.

internal sealed class OperatorLivenessHealthCheck(
    AdmissionWebhookRuntimeState webhookState) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        if (!webhookState.Enabled)
        {
            return Task.FromResult(HealthCheckResult.Healthy("Webhook is disabled."));
        }

        if (webhookState.ListenerReady)
        {
            return Task.FromResult(HealthCheckResult.Healthy("Webhook listener is ready."));
        }

        if (!webhookState.AreCertificateFilesPresent())
        {
            return Task.FromResult(HealthCheckResult.Healthy("Waiting for webhook TLS material to be projected."));
        }

        return Task.FromResult(
            HealthCheckResult.Unhealthy(
                webhookState.FailureReason ?? "Webhook TLS material is present but the HTTPS listener is not serving."));
    }
}

#pragma warning restore IDE0046
#pragma warning restore IDE0045
