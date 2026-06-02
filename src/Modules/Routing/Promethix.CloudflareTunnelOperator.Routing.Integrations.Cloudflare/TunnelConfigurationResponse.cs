using System.Text.Json.Serialization;

namespace Promethix.CloudflareTunnelOperator.Routing.Integrations.Cloudflare;

internal sealed class TunnelConfigurationResponse
{
    [JsonPropertyName("config")]
    public TunnelConfiguration? Config { get; set; }
}
