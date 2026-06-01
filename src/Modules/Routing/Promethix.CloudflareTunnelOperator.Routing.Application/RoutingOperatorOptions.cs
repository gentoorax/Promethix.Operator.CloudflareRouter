namespace Promethix.CloudflareTunnelOperator.Routing.Application;

public sealed class RoutingOperatorOptions
{
    public const string SectionName = "RoutingOperator";

    public string OwnershipTag { get; set; } = "promethix-cloudflare-tunnel-operator";

    public int ReconciliationIntervalSeconds { get; set; } = 30;

    public bool ApplyChanges { get; set; }
}
