namespace Promethix.CloudflareTunnelOperator.Hosting.Admission;

internal sealed class AdmissionWebhookRuntimeState
{
    public bool Enabled { get; init; }

    public bool ListenerReady { get; set; }

    public string CertificatePath { get; init; } = string.Empty;

    public string PrivateKeyPath { get; init; } = string.Empty;

    public string? FailureReason { get; set; }

    public bool AreCertificateFilesPresent()
    {
        return File.Exists(CertificatePath) && File.Exists(PrivateKeyPath);
    }
}
