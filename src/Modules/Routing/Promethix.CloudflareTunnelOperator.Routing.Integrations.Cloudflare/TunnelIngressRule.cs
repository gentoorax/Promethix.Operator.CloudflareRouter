using System.Text.Json.Serialization;

namespace Promethix.CloudflareTunnelOperator.Routing.Integrations.Cloudflare;

internal sealed class TunnelIngressRule
{
    [JsonPropertyName("hostname")]
    public string? Hostname { get; set; }

    [JsonPropertyName("service")]
    public string Service { get; set; } = string.Empty;
}
