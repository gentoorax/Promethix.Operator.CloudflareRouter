namespace Promethix.CloudflareTunnelOperator.Routing.Integrations.Kubernetes;

public sealed class TunnelDirectTargetSpec
{
    public Uri? Url { get; set; }

    public string Protocol { get; set; } = "http";

    public TunnelIngressServiceTargetSpec? Service { get; set; }
}
