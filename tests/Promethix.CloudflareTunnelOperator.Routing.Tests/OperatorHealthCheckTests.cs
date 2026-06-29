using FluentAssertions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Promethix.CloudflareTunnelOperator.Hosting;
using Promethix.CloudflareTunnelOperator.Hosting.Admission;
using Promethix.CloudflareTunnelOperator.Hosting.Health;
using Promethix.CloudflareTunnelOperator.Routing.Application;

namespace Promethix.CloudflareTunnelOperator.Routing.Tests;

public sealed class OperatorHealthCheckTests
{
    [Fact]
    public async Task ReadinessShouldBeUnhealthyWhenWebhookIsEnabledAndListenerIsNotReady()
    {
        var state = new OperatorState();
        state.MarkInitialFullInventoryPass(DateTimeOffset.UtcNow, startupSafeForMutation: true);

        using var certificateFiles = CreateWebhookCertificateFiles();
        var webhookState = new AdmissionWebhookRuntimeState
        {
            Enabled = true,
            ListenerReady = false,
            CertificatePath = certificateFiles.CertificatePath,
            PrivateKeyPath = certificateFiles.PrivateKeyPath,
            FailureReason = "Webhook TLS listener is not serving.",
        };

        var healthCheck = new OperatorReadinessHealthCheck(
            state,
            webhookState,
            Options.Create(new RoutingOperatorOptions()));

        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext(), CancellationToken.None);

        _ = result.Status.Should().Be(HealthStatus.Unhealthy);
        _ = result.Description.Should().Be("Webhook TLS listener is not serving.");
    }

    [Fact]
    public async Task LivenessShouldStayHealthyWhileWaitingForWebhookTlsFiles()
    {
        var webhookState = new AdmissionWebhookRuntimeState
        {
            Enabled = true,
            ListenerReady = false,
            CertificatePath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.crt"),
            PrivateKeyPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.key"),
        };

        var healthCheck = new OperatorLivenessHealthCheck(webhookState);

        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext(), CancellationToken.None);

        _ = result.Status.Should().Be(HealthStatus.Healthy);
    }

    [Fact]
    public async Task LivenessShouldBeUnhealthyWhenWebhookTlsFilesExistButListenerIsNotReady()
    {
        using var certificateFiles = CreateWebhookCertificateFiles();
        var webhookState = new AdmissionWebhookRuntimeState
        {
            Enabled = true,
            ListenerReady = false,
            CertificatePath = certificateFiles.CertificatePath,
            PrivateKeyPath = certificateFiles.PrivateKeyPath,
            FailureReason = "Webhook TLS listener is not serving.",
        };

        var healthCheck = new OperatorLivenessHealthCheck(webhookState);

        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext(), CancellationToken.None);

        _ = result.Status.Should().Be(HealthStatus.Unhealthy);
        _ = result.Description.Should().Be("Webhook TLS listener is not serving.");
    }

    private static TemporaryWebhookCertificateFiles CreateWebhookCertificateFiles()
    {
        var directory = Directory.CreateTempSubdirectory("webhook-health-");
        var certificatePath = Path.Combine(directory.FullName, "tls.crt");
        var privateKeyPath = Path.Combine(directory.FullName, "tls.key");

        File.WriteAllText(certificatePath, "placeholder certificate");
        File.WriteAllText(privateKeyPath, "placeholder key");

        return new TemporaryWebhookCertificateFiles(directory, certificatePath, privateKeyPath);
    }

    private sealed class TemporaryWebhookCertificateFiles(
        DirectoryInfo directory,
        string certificatePath,
        string privateKeyPath) : IDisposable
    {
        public string CertificatePath { get; } = certificatePath;

        public string PrivateKeyPath { get; } = privateKeyPath;

        public void Dispose()
        {
            directory.Delete(recursive: true);
        }
    }
}
