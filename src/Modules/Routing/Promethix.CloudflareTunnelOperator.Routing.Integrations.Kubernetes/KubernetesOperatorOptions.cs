namespace Promethix.CloudflareTunnelOperator.Routing.Integrations.Kubernetes;

public sealed class KubernetesOperatorOptions
{
    public const string SectionName = "KubernetesOperator";

    public string ManagedClassName { get; set; } = "public";

    public string ManagedTunnelName { get; set; } = "public-tunnel";

    public string ManagedIngressClassName { get; set; } = "traefik-cloudflare-tunnel";

    public Uri IngressTargetUrl { get; set; } = new("https://traefik-cloudflare-tunnel.traefik.svc.cluster.local");

    public string ManagedFinalizerName { get; set; } = "edge.promethix.net/tunnelpublichostname-protection";

    public string OwnershipConfigMapNamespace { get; set; } = "cloudflare-tunnel-operator-system";

    public string OwnershipConfigMapName { get; set; } = "promethix-cloudflare-tunnel-operator-ownership";
}
