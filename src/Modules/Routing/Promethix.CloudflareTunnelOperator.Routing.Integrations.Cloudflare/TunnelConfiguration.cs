using System.Text.Json.Serialization;

namespace Promethix.CloudflareTunnelOperator.Routing.Integrations.Cloudflare;

internal sealed class TunnelConfiguration
{
    [JsonPropertyName("ingress")]
    public IList<TunnelIngressRule> Ingress { get; set; } = [];
}
