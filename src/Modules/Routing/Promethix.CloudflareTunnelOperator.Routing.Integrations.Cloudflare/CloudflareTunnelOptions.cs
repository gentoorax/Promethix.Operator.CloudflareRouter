namespace Promethix.CloudflareTunnelOperator.Routing.Integrations.Cloudflare;

public sealed class CloudflareTunnelOptions
{
    public const string SectionName = "CloudflareTunnel";

    public string AccountId { get; set; } = string.Empty;

    public string TunnelId { get; set; } = string.Empty;

    public string ApiToken { get; set; } = string.Empty;
}
