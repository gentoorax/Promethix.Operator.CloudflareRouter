using System.Text.Json.Serialization;

namespace Promethix.CloudflareTunnelOperator.Routing.Integrations.Cloudflare;

internal sealed class CloudflareApiEnvelope<T>
{
    [JsonPropertyName("result")]
    public T? Result { get; set; }
}
